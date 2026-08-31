using DhirDhar.Application.Dashboard.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Tests;

public class DashboardServiceTests
{
    private static ServiceProvider BuildProvider(DatabaseOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(options);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task GetSummaryAsync_EmptyDatabase_ReturnsZeros()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDhar.Infrastructure.Persistence.DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DhirDhar.Application.Dashboard.IDashboardService>();

        var summary = await service.GetSummaryAsync();

        Assert.Equal(0, summary.TotalBorrowers);
        Assert.Equal(0, summary.ActiveBorrowers);
        Assert.Equal(0, summary.InactiveBorrowers);
        Assert.Equal(0, summary.ClosedBorrowers);
        Assert.Equal(0, summary.ArchivedBorrowers);
        Assert.Equal(0m, summary.TotalDeposits);
        Assert.Equal(0m, summary.TotalWithdrawals);
        Assert.Equal(0m, summary.OutstandingAmount);
        Assert.Equal(0m, summary.TotalInterest);
        Assert.Empty(summary.RecentTransactions);
    }

    [Fact]
    public async Task GetSummaryAsync_WithBorrowers_ReturnsCorrectCounts()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDhar.Infrastructure.Persistence.DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        context.Borrowers.Add(new Borrower("Alice", null, null, null));
        context.Borrowers.Add(new Borrower("Bob", null, null, null));
        var archived = new Borrower("Charlie", null, null, null);
        archived.Archive();
        context.Borrowers.Add(archived);
        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DhirDhar.Application.Dashboard.IDashboardService>();

        var summary = await service.GetSummaryAsync();

        Assert.Equal(3, summary.TotalBorrowers);
        Assert.Equal(2, summary.ActiveBorrowers);
        Assert.Equal(0, summary.InactiveBorrowers);
        Assert.Equal(1, summary.ClosedBorrowers);
        Assert.Equal(1, summary.ArchivedBorrowers);
    }

    [Fact]
    public async Task GetSummaryAsync_WithInactiveBorrower_ReturnsInactiveCount()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDhar.Infrastructure.Persistence.DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var inactive = new Borrower("Inactive User", null, null, null);
        inactive.Deactivate();
        context.Borrowers.Add(inactive);
        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DhirDhar.Application.Dashboard.IDashboardService>();

        var summary = await service.GetSummaryAsync();

        Assert.Equal(1, summary.TotalBorrowers);
        Assert.Equal(0, summary.ActiveBorrowers);
        Assert.Equal(1, summary.InactiveBorrowers);
        Assert.Equal(0, summary.ClosedBorrowers);
        Assert.Equal(0, summary.ArchivedBorrowers);
        Assert.Equal(0m, summary.TotalDeposits);
        Assert.Equal(0m, summary.TotalWithdrawals);
        Assert.Equal(0m, summary.OutstandingAmount);
    }

    [Fact]
    public async Task GetSummaryAsync_WithActiveBorrowerTransactions_ReturnsCorrectSums()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDhar.Infrastructure.Persistence.DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var borrower = new Borrower("Active Borrower", null, null, null);
        context.Borrowers.Add(borrower);

        var period = new FinancialPeriod("Test Period", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
        context.FinancialPeriods.Add(period);
        await context.SaveChangesAsync();

        context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(1000m), TransactionType.Deposit, DateTime.UtcNow, "Deposit 1"));
        context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(500m), TransactionType.Deposit, DateTime.UtcNow, "Deposit 2"));
        context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(300m), TransactionType.Withdrawal, DateTime.UtcNow, "Withdrawal 1"));
        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DhirDhar.Application.Dashboard.IDashboardService>();

        var summary = await service.GetSummaryAsync();

        Assert.Equal(1500m, summary.TotalDeposits);
        Assert.Equal(300m, summary.TotalWithdrawals);
        Assert.Equal(1200m, summary.OutstandingAmount);
    }

    [Fact]
    public async Task GetSummaryAsync_ClosedBorrowers_ExcludedFromFinancialTotals_AndIncludedWhenReopened()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDhar.Infrastructure.Persistence.DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var b1 = new Borrower("Borrower 1", null, null, null);
        var b2 = new Borrower("Borrower 2", null, null, null);
        context.Borrowers.Add(b1);
        context.Borrowers.Add(b2);

        var period = new FinancialPeriod("Test Period", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
        context.FinancialPeriods.Add(period);
        await context.SaveChangesAsync();

        // Borrower 1 txns: +10,000 deposit, -2,000 withdrawal => 8,000 outstanding
        context.Transactions.Add(new Transaction(b1.Id, period.Id, Money.Create(10000m), TransactionType.Deposit, DateTime.UtcNow, "B1 Dep"));
        context.Transactions.Add(new Transaction(b1.Id, period.Id, Money.Create(2000m), TransactionType.Withdrawal, DateTime.UtcNow, "B1 With"));

        // Borrower 2 txns: +5,000 deposit, -1,000 withdrawal => 4,000 outstanding
        context.Transactions.Add(new Transaction(b2.Id, period.Id, Money.Create(5000m), TransactionType.Deposit, DateTime.UtcNow, "B2 Dep"));
        context.Transactions.Add(new Transaction(b2.Id, period.Id, Money.Create(1000m), TransactionType.Withdrawal, DateTime.UtcNow, "B2 With"));
        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DhirDhar.Application.Dashboard.IDashboardService>();

        // Both active
        var summary1 = await service.GetSummaryAsync();
        Assert.Equal(2, summary1.ActiveBorrowers);
        Assert.Equal(0, summary1.ClosedBorrowers);
        Assert.Equal(15000m, summary1.TotalDeposits);
        Assert.Equal(3000m, summary1.TotalWithdrawals);
        Assert.Equal(12000m, summary1.OutstandingAmount);

        // Close Borrower 2
        b2.Archive(); // Closed/Archived
        await context.SaveChangesAsync();

        // Immediately removed from top 4 dashboard totals
        var summary2 = await service.GetSummaryAsync();
        Assert.Equal(1, summary2.ActiveBorrowers);
        Assert.Equal(1, summary2.ClosedBorrowers);
        Assert.Equal(10000m, summary2.TotalDeposits);
        Assert.Equal(2000m, summary2.TotalWithdrawals);
        Assert.Equal(8000m, summary2.OutstandingAmount);

        // Historical transactions remain in database intact
        var allTxCount = context.Transactions.Count();
        Assert.Equal(4, allTxCount);

        // Reopen Borrower 2
        b2.Activate();
        await context.SaveChangesAsync();

        // Immediately included back in top 4 dashboard totals
        var summary3 = await service.GetSummaryAsync();
        Assert.Equal(2, summary3.ActiveBorrowers);
        Assert.Equal(0, summary3.ClosedBorrowers);
        Assert.Equal(15000m, summary3.TotalDeposits);
        Assert.Equal(3000m, summary3.TotalWithdrawals);
        Assert.Equal(12000m, summary3.OutstandingAmount);
    }

    [Fact]
    public async Task GetSummaryAsync_RecentTransactions_AreOrderedByDateDescending()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDhar.Infrastructure.Persistence.DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("Test Period", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
        context.FinancialPeriods.Add(period);
        await context.SaveChangesAsync();

        var now = DateTime.UtcNow;
        context.Transactions.Add(new Transaction(period.Id, Money.Create(100m), TransactionType.Deposit, now.AddDays(-2), "Oldest"));
        context.Transactions.Add(new Transaction(period.Id, Money.Create(200m), TransactionType.Deposit, now, "Newest"));
        context.Transactions.Add(new Transaction(period.Id, Money.Create(150m), TransactionType.Deposit, now.AddDays(-1), "Middle"));
        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DhirDhar.Application.Dashboard.IDashboardService>();

        var summary = await service.GetSummaryAsync();

        Assert.Equal(3, summary.RecentTransactions.Count);
        Assert.Equal("Newest", summary.RecentTransactions[0].Description);
        Assert.Equal("Middle", summary.RecentTransactions[1].Description);
        Assert.Equal("Oldest", summary.RecentTransactions[2].Description);
    }

    [Fact]
    public async Task GetSummaryAsync_RecentTransactions_AreStrictlyLimitedToFive()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDhar.Infrastructure.Persistence.DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("Test Period", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
        context.FinancialPeriods.Add(period);
        await context.SaveChangesAsync();

        for (int i = 0; i < 25; i++)
        {
            context.Transactions.Add(new Transaction(period.Id, Money.Create(100m), TransactionType.Deposit, DateTime.UtcNow.AddMinutes(-i), $"Tx {i}"));
        }
        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DhirDhar.Application.Dashboard.IDashboardService>();

        var summary = await service.GetSummaryAsync();

        Assert.Equal(5, summary.RecentTransactions.Count);
    }

    [Fact]
    public async Task GetSummaryAsync_WithMixedBorrowers_CalculatesAllFiveTilesCorrectly()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDhar.Infrastructure.Persistence.DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        // 1. Active Borrower A with txns
        var activeA = new Borrower("Active Borrower A", null, null, null);
        // 2. Closed Borrower B with txns
        var closedB = new Borrower("Closed Borrower B", null, null, null);
        closedB.Archive(); // Closed
        // 3. Inactive Borrower C without txns
        var inactiveC = new Borrower("Inactive Borrower C", null, null, null);
        inactiveC.Deactivate();

        context.Borrowers.AddRange(activeA, closedB, inactiveC);

        var period = new FinancialPeriod("Test Period", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
        context.FinancialPeriods.Add(period);
        await context.SaveChangesAsync();

        // Transactions for Active Borrower A: +20,000 deposit, -5,000 withdrawal => 15,000 outstanding
        context.Transactions.Add(new Transaction(activeA.Id, period.Id, Money.Create(20000m), TransactionType.Deposit, DateTime.UtcNow.AddDays(-5), "A Deposit"));
        context.Transactions.Add(new Transaction(activeA.Id, period.Id, Money.Create(5000m), TransactionType.Withdrawal, DateTime.UtcNow.AddDays(-4), "A Withdrawal"));

        // Transactions for Closed Borrower B: +50,000 deposit, -10,000 withdrawal => 40,000 outstanding
        context.Transactions.Add(new Transaction(closedB.Id, period.Id, Money.Create(50000m), TransactionType.Deposit, DateTime.UtcNow.AddDays(-10), "B Deposit"));
        context.Transactions.Add(new Transaction(closedB.Id, period.Id, Money.Create(10000m), TransactionType.Withdrawal, DateTime.UtcNow.AddDays(-9), "B Withdrawal"));

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DhirDhar.Application.Dashboard.IDashboardService>();

        // Act
        var summary = await service.GetSummaryAsync();

        // Assert Tile 1: Total Borrowers count must be 3 (all borrowers)
        Assert.Equal(3, summary.TotalBorrowers);
        Assert.Equal(1, summary.ActiveBorrowers);
        Assert.Equal(1, summary.ClosedBorrowers);
        Assert.Equal(1, summary.InactiveBorrowers);

        // Assert Tiles 2-4: Financial amounts MUST only include Active Borrower A
        Assert.Equal(20000m, summary.TotalDeposits);
        Assert.Equal(5000m, summary.TotalWithdrawals);
        Assert.Equal(15000m, summary.OutstandingAmount);

        // Assert Data Integrity: Historical transactions for Closed Borrower B are NOT deleted
        Assert.Equal(4, context.Transactions.Count());

        // Reopen Borrower B
        closedB.Activate();
        await context.SaveChangesAsync();

        var reopenedSummary = await service.GetSummaryAsync();
        Assert.Equal(3, reopenedSummary.TotalBorrowers);
        Assert.Equal(2, reopenedSummary.ActiveBorrowers);
        Assert.Equal(0, reopenedSummary.ClosedBorrowers);

        // Now both A and B are included in the 4 financial tiles
        Assert.Equal(70000m, reopenedSummary.TotalDeposits);
        Assert.Equal(15000m, reopenedSummary.TotalWithdrawals);
        Assert.Equal(55000m, reopenedSummary.OutstandingAmount);
    }

    [Fact]
    public async Task GetSummaryAsync_RecentTransactions_WhenFewerThanFive_ReturnsAvailable()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDhar.Infrastructure.Persistence.DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var borrower = new Borrower("Test Borrower", null, null, null);
        context.Borrowers.Add(borrower);

        var period = new FinancialPeriod("Test Period", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
        context.FinancialPeriods.Add(period);
        await context.SaveChangesAsync();

        context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(500m), TransactionType.Deposit, DateTime.UtcNow.AddHours(-2), "Tx 1"));
        context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(300m), TransactionType.Withdrawal, DateTime.UtcNow.AddHours(-1), "Tx 2"));
        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DhirDhar.Application.Dashboard.IDashboardService>();

        var summary = await service.GetSummaryAsync();

        Assert.Equal(2, summary.RecentTransactions.Count);
        Assert.Equal("Tx 2", summary.RecentTransactions[0].Description);
        Assert.Equal("Tx 1", summary.RecentTransactions[1].Description);
        Assert.Equal("Test Borrower", summary.RecentTransactions[0].BorrowerName);
        Assert.Equal("Test Borrower", summary.RecentTransactions[1].BorrowerName);
    }

    [Fact]
    public async Task GetSummaryAsync_DatabaseFailure_ReturnsEmptySummary()
    {
        var options = new DhirDhar.Infrastructure.Configuration.DatabaseOptions
        {
            Provider = "Sqlite",
            DatabasePath = Path.Combine(Path.GetTempPath(), "nonexistent", "test.db"),
            CommandTimeout = 30,
            EnableSensitiveDataLogging = false
        };

        using var provider = BuildProvider(options);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DhirDhar.Application.Dashboard.IDashboardService>();

        var summary = await service.GetSummaryAsync();

        Assert.Equal(0, summary.TotalBorrowers);
        Assert.Equal(0m, summary.TotalDeposits);
        Assert.Equal(0m, summary.OutstandingAmount);
    }
}
