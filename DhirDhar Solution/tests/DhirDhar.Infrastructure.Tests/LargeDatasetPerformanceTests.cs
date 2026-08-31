using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Dashboard;
using DhirDhar.Application.Interest;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.DependencyInjection;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class LargeDatasetPerformanceTests
{
    private static ServiceProvider BuildProvider(DatabaseOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(options);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CalculateBatchAsync_ProducesIdenticalResultsToSingleCalculation_AcrossMultipleBorrowers()
    {
        using var temp = new TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("Test FY", new DateTime(2025, 4, 1), new DateTime(2026, 3, 31));
        context.FinancialPeriods.Add(period);
        await context.SaveChangesAsync();

        var borrowerIds = new List<Guid>();
        var random = new Random(42);

        // Seed 50 realistic borrowers with varied loan dates, rates, deposits, and withdrawals
        for (int i = 1; i <= 50; i++)
        {
            var loanDate = new DateTime(2025, 4, 1).AddDays(random.Next(0, 120));
            var rate = 1.5m + (decimal)random.Next(0, 4) * 0.5m;
            var principal = (decimal)random.Next(10, 100) * 1000m;

            var borrower = new Borrower($"DJ-{i:D4}", $"Borrower {i:D3}", $"98765{i:D5}", "Test Village", "Notes", loanDate);
            borrower.SetPhotosAndLoanType(null, null, "Gold", null, null, principal, loanDate, rate);
            context.Borrowers.Add(borrower);

            // Initial loan withdrawal
            context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(principal), TransactionType.Withdrawal, loanDate, "Initial Loan Amount"));

            // Random intermediate payments
            var numTxns = random.Next(1, 6);
            for (int t = 0; t < numTxns; t++)
            {
                var txnDate = loanDate.AddDays(random.Next(15, 180));
                if (txnDate <= DateTime.Today)
                {
                    var isDeposit = random.NextDouble() > 0.3;
                    var type = isDeposit ? TransactionType.Deposit : TransactionType.Withdrawal;
                    var amount = (decimal)random.Next(1, 10) * 1000m;
                    context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(amount), type, txnDate, $"Payment {t + 1}"));
                }
            }

            borrowerIds.Add(borrower.Id);
        }

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var interestService = scope.ServiceProvider.GetRequiredService<IInterestCalculationService>();

        var testEndDate = new DateTime(2026, 1, 31);

        // 1. Calculate sequentially using single CalculateAsync
        var singleResults = new Dictionary<Guid, decimal>();
        foreach (var id in borrowerIds)
        {
            var result = await interestService.CalculateAsync(id, testEndDate);
            singleResults[id] = result.TotalInterest;
        }

        // 2. Calculate in batch using high-performance CalculateBatchAsync
        var batchResults = await interestService.CalculateBatchAsync(borrowerIds, testEndDate);

        // 3. Assert 100% mathematical identity across all borrowers
        Assert.Equal(borrowerIds.Count, batchResults.Count);
        foreach (var id in borrowerIds)
        {
            Assert.True(batchResults.ContainsKey(id), $"Batch results missing Borrower ID '{id}'.");
            Assert.Equal(singleResults[id], batchResults[id]);
        }
    }

    [Fact]
    public async Task DashboardService_GetSummaryAsync_CalculatesRapidlyForLargeDataset()
    {
        using var temp = new TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("Test FY", new DateTime(2025, 4, 1), new DateTime(2026, 3, 31));
        context.FinancialPeriods.Add(period);
        await context.SaveChangesAsync();

        var activeBorrowerIds = new List<Guid>();
        var random = new Random(12345);

        // Seed 1,000 active borrowers with loan dates and transactions
        const int totalCount = 1000;
        var borrowers = new List<Borrower>(totalCount);
        var transactions = new List<Transaction>(totalCount * 2);

        for (int i = 1; i <= totalCount; i++)
        {
            var loanDate = new DateTime(2025, 4, 1).AddDays(random.Next(0, 150));
            var principal = (decimal)random.Next(5, 50) * 1000m;
            var borrower = new Borrower($"DJ-{i:D4}", $"Borrower {i:D4}", $"98765{i:D5}", "Rajkot", "Notes", loanDate);
            borrower.SetPhotosAndLoanType(null, null, "Gold", null, null, principal, loanDate, 2.0m);
            borrowers.Add(borrower);

            transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(principal), TransactionType.Withdrawal, loanDate, "Initial Loan Amount"));
            transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(principal * 0.2m), TransactionType.Deposit, loanDate.AddDays(30), "Part Repayment"));

            activeBorrowerIds.Add(borrower.Id);
        }

        context.Borrowers.AddRange(borrowers);
        context.Transactions.AddRange(transactions);
        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var dashboardService = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        // Measure execution time
        var sw = Stopwatch.StartNew();
        var summary = await dashboardService.GetSummaryAsync();
        sw.Stop();

        Assert.Equal(totalCount, summary.TotalBorrowers);
        Assert.Equal(totalCount, summary.ActiveBorrowers);
        Assert.True(summary.TotalDeposits > 0m);
        Assert.True(summary.TotalWithdrawals > 0m);
        Assert.True(summary.TotalInterest > 0m);

        // Batch calculation should execute within 3 seconds for 1,000 borrowers (previously 50+ seconds)
        Assert.True(sw.ElapsedMilliseconds < 3000, $"Dashboard summary took {sw.ElapsedMilliseconds} ms, exceeding threshold of 3000 ms.");
    }

    [Fact]
    public async Task BorrowerService_GetListAsync_HandlesThousandsOfBorrowersWithoutParameterOverflow()
    {
        using var temp = new TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("Test FY", new DateTime(2025, 4, 1), new DateTime(2026, 3, 31));
        context.FinancialPeriods.Add(period);
        await context.SaveChangesAsync();

        const int count = 1200;
        var borrowers = new List<Borrower>(count);
        var transactions = new List<Transaction>(count);

        for (int i = 1; i <= count; i++)
        {
            var b = new Borrower($"DJ-{i:D4}", $"Borrower {i:D4}", $"98700{i:D5}", "Village", "Notes", DateTime.Today.AddDays(-10));
            b.SetPhotosAndLoanType(null, null, "Gold", null, null, 10000m, DateTime.Today.AddDays(-10), 2.0m);
            borrowers.Add(b);
            transactions.Add(new Transaction(b.Id, period.Id, Money.Create(10000m), TransactionType.Withdrawal, DateTime.Today.AddDays(-10), "Initial Loan Amount"));
        }

        context.Borrowers.AddRange(borrowers);
        context.Transactions.AddRange(transactions);
        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var borrowerService = scope.ServiceProvider.GetRequiredService<IBorrowerService>();

        var sw = Stopwatch.StartNew();
        var result = await borrowerService.GetListAsync(BorrowerFilter.All, null, 1, count);
        sw.Stop();

        Assert.Equal(count, result.TotalCount);
        Assert.Equal(count, result.Items.Count);
        Assert.True(sw.ElapsedMilliseconds < 2500, $"GetListAsync took {sw.ElapsedMilliseconds} ms for {count} borrowers.");
    }
}
