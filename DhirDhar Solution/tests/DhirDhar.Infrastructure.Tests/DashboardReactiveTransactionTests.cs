using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Dashboard;
using DhirDhar.Application.Dashboard.Models;
using DhirDhar.Application.Transactions;
using DhirDhar.Application.Transactions.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.DependencyInjection;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Tests.Persistence;
using DhirDhar.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class DashboardReactiveTransactionTests
{
    private static ServiceProvider BuildProvider(DatabaseOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(options);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void TransactionEventService_PublishesAndNotifiesSubscribers()
    {
        // Arrange
        var logger = NullLogger<TransactionEventService>.Instance;
        var eventService = new TransactionEventService(logger);

        TransactionChangedEventArgs? receivedArgs = null;
        int callCount = 0;
        eventService.TransactionChanged += (s, e) =>
        {
            callCount++;
            receivedArgs = e;
        };

        var txnId = Guid.NewGuid();
        var borrowerId = Guid.NewGuid();

        // Act
        eventService.PublishTransactionChanged(new TransactionChangedEventArgs(txnId, borrowerId, TransactionMutationKind.Created));

        // Assert
        Assert.Equal(1, callCount);
        Assert.NotNull(receivedArgs);
        Assert.Equal(txnId, receivedArgs!.TransactionId);
        Assert.Equal(borrowerId, receivedArgs.BorrowerId);
        Assert.Equal(TransactionMutationKind.Created, receivedArgs.MutationKind);
        Assert.NotNull(receivedArgs.Timestamp);
    }

    [Fact]
    public async Task TransactionService_CreateAsync_PublishesTransactionChangedEvent()
    {
        // Arrange
        using var temp = new TempDatabase();
        await using (var context = new DhirDharDbContext(temp.CreateOptions()))
        {
            await context.Database.EnsureCreatedAsync();
            var borrower = new Borrower("Test Borrower", null, null, null);
            context.Borrowers.Add(borrower);

            var period = new FinancialPeriod("Test Period", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
            context.FinancialPeriods.Add(period);
            await context.SaveChangesAsync();
        }

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        var eventService = provider.GetRequiredService<ITransactionEventService>();
        var transactionService = provider.GetRequiredService<ITransactionService>();

        TransactionChangedEventArgs? receivedArgs = null;
        int eventCount = 0;
        eventService.TransactionChanged += (s, e) =>
        {
            eventCount++;
            receivedArgs = e;
        };

        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
        var borrowerEntity = await dbContext.Borrowers.FirstAsync();

        // Act
        var result = await transactionService.CreateAsync(new CreateTransactionRequest(
            borrowerEntity.Id,
            TransactionType.Deposit,
            1500m,
            DateTime.UtcNow,
            "Payment received"));

        // Assert
        Assert.Equal(1, eventCount);
        Assert.NotNull(receivedArgs);
        Assert.Equal(result.Id, receivedArgs!.TransactionId);
        Assert.Equal(borrowerEntity.Id, receivedArgs.BorrowerId);
        Assert.Equal(TransactionMutationKind.Created, receivedArgs.MutationKind);
    }

    [Fact]
    public async Task RecentTransactions_AreOrderedByOccurredOnDescending_ThenByCreatedAt_ThenById()
    {
        using var temp = new TempDatabase();
        await using (var context = new DhirDharDbContext(temp.CreateOptions()))
        {
            await context.Database.EnsureCreatedAsync();
            var borrower = new Borrower("Alice", null, null, null);
            context.Borrowers.Add(borrower);

            var period = new FinancialPeriod("Period 1", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
            context.FinancialPeriods.Add(period);
            await context.SaveChangesAsync();

            var baseDate = new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc);

            // Add transactions out of chronological order
            context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(100m), TransactionType.Deposit, baseDate.AddDays(-2), "Tx Oldest"));
            context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(200m), TransactionType.Withdrawal, baseDate.AddDays(2), "Tx Newest"));
            context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(300m), TransactionType.Deposit, baseDate.AddDays(0), "Tx Middle"));
            context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(400m), TransactionType.Deposit, baseDate.AddDays(1), "Tx Later"));
            context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(500m), TransactionType.Withdrawal, baseDate.AddDays(-1), "Tx Earlier"));
            context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(600m), TransactionType.Deposit, baseDate.AddDays(-5), "Tx Very Old (should be excluded from top 5)"));

            await context.SaveChangesAsync();
        }

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var dashboardService = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        // Act
        var summary = await dashboardService.GetSummaryAsync();

        // Assert
        Assert.Equal(5, summary.RecentTransactions.Count);
        Assert.Equal("Tx Newest", summary.RecentTransactions[0].Description);
        Assert.Equal("Tx Later", summary.RecentTransactions[1].Description);
        Assert.Equal("Tx Middle", summary.RecentTransactions[2].Description);
        Assert.Equal("Tx Earlier", summary.RecentTransactions[3].Description);
        Assert.Equal("Tx Oldest", summary.RecentTransactions[4].Description);
        Assert.Equal("Alice", summary.RecentTransactions[0].BorrowerName);
    }

    [Fact]
    public async Task HistoricalOutstanding_ComputesDynamicNormalizedBarHeights_Safely()
    {
        using var temp = new TempDatabase();
        await using (var context = new DhirDharDbContext(temp.CreateOptions()))
        {
            await context.Database.EnsureCreatedAsync();
            var borrower = new Borrower("Bob", null, null, null);
            context.Borrowers.Add(borrower);

            var period = new FinancialPeriod("Period 1", DateTime.UtcNow.AddDays(-180), DateTime.UtcNow.AddDays(30));
            context.FinancialPeriods.Add(period);
            await context.SaveChangesAsync();

            var now = DateTime.Today;
            // Add withdrawal (loans) in current month and past months
            context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(50000m), TransactionType.Withdrawal, now, "Current Month Loan"));
            context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(10000m), TransactionType.Withdrawal, now.AddMonths(-2), "2 Months Ago Loan"));
            await context.SaveChangesAsync();
        }

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var dashboardService = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        // Act
        var summary = await dashboardService.GetSummaryAsync();

        // Assert
        Assert.Equal(6, summary.HistoricalOutstanding.Count);

        // Max amount is in current month (60,000 net outstanding)
        var currentMonthPoint = summary.HistoricalOutstanding[^1];
        Assert.Equal(60000m, currentMonthPoint.OutstandingAmount);
        Assert.Equal(140.0, currentMonthPoint.BarHeight, 2);

        // Baseline empty months (5 months ago, 4 months ago, 3 months ago) must have minimum baseline height (4.0)
        var oldestPoint = summary.HistoricalOutstanding[0];
        Assert.Equal(0m, oldestPoint.OutstandingAmount);
        Assert.Equal(4.0, oldestPoint.BarHeight, 2);

        // Every point must have a valid non-NaN, non-Infinite height between 4.0 and 140.0
        foreach (var point in summary.HistoricalOutstanding)
        {
            Assert.False(double.IsNaN(point.BarHeight));
            Assert.False(double.IsInfinity(point.BarHeight));
            Assert.InRange(point.BarHeight, 4.0, 140.0);
        }
    }

    [Fact]
    public async Task MonthlyPeriodSummary_CalculatesOpeningNewLoansPaymentsAndClosingAccurately()
    {
        using var temp = new TempDatabase();
        var targetYear = 2026;
        var targetMonth = 5;
        var monthStart = new DateTime(targetYear, targetMonth, 1);

        await using (var context = new DhirDharDbContext(temp.CreateOptions()))
        {
            await context.Database.EnsureCreatedAsync();
            var borrower = new Borrower("Charlie", null, null, null);
            context.Borrowers.Add(borrower);

            var period = new FinancialPeriod("Period 1", DateTime.UtcNow.AddDays(-180), DateTime.UtcNow.AddDays(30));
            context.FinancialPeriods.Add(period);
            await context.SaveChangesAsync();

            // Before May 2026: 20,000 deposit, 5,000 withdrawal => Opening = 15,000
            context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(20000m), TransactionType.Deposit, monthStart.AddDays(-10), "Prior Deposit"));
            context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(5000m), TransactionType.Withdrawal, monthStart.AddDays(-5), "Prior Withdrawal"));

            // During May 2026: 10,000 new loans (withdrawal), 4,000 payments (deposit)
            context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(10000m), TransactionType.Withdrawal, monthStart.AddDays(5), "May Loan"));
            context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(4000m), TransactionType.Deposit, monthStart.AddDays(15), "May Payment"));

            // After May 2026: Future activity (should not affect May summary)
            context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(8000m), TransactionType.Withdrawal, monthStart.AddMonths(1).AddDays(5), "June Loan"));

            await context.SaveChangesAsync();
        }

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var dashboardService = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        // Act
        var periodSummary = await dashboardService.GetMonthlyPeriodSummaryAsync(targetYear, targetMonth);

        // Assert
        Assert.Equal(15000m, periodSummary.OpeningBalance);
        Assert.Equal(10000m, periodSummary.NewLoans);
        Assert.Equal(4000m, periodSummary.Payments);
        // Closing = Opening (15,000) + Payments (4,000) - NewLoans (10,000) = 9,000
        Assert.Equal(9000m, periodSummary.ClosingBalance);
    }
}
