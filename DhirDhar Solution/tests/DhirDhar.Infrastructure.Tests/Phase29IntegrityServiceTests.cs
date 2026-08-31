using System;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Validation;
using DhirDhar.Application.Validation.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.DependencyInjection;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class Phase29IntegrityServiceTests
{
    private static ServiceProvider BuildProvider(DatabaseOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(options);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunIntegrityScanAsync_HealthyDatabase_ReturnsPassStatus()
    {
        using var temp = new TempDatabase();
        using var provider = BuildProvider(temp.CreateDatabaseOptions());

        using (var scope = provider.CreateScope())
        {
            var init = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            await init.InitializeAsync();

            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var borrower = new Borrower("B001", "John Doe", "1234567890", "123 Main St", "Notes", new DateTime(2026, 1, 1));
            dbContext.Borrowers.Add(borrower);
            await dbContext.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var integrityService = scope.ServiceProvider.GetRequiredService<IIntegrityService>();
            var report = await integrityService.RunIntegrityScanAsync();

            Assert.NotNull(report);
            Assert.Equal(IntegrityStatus.Pass, report.OverallStatus);
            Assert.Equal(0, report.TotalIssuesFound);
        }
    }

    [Fact]
    public async Task RunIntegrityScanAsync_DuplicateBorrowerNumber_ReturnsCriticalStatus()
    {
        using var temp = new TempDatabase();
        using var provider = BuildProvider(temp.CreateDatabaseOptions());

        using (var scope = provider.CreateScope())
        {
            var init = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            await init.InitializeAsync();

            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            await dbContext.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_Borrowers_BorrowerNumber;");
            var b1 = new Borrower("DUP-001", "Alice", "111", null, null, new DateTime(2026, 1, 1));
            var b2 = new Borrower("DUP-001", "Bob", "222", null, null, new DateTime(2026, 1, 1));
            dbContext.Borrowers.AddRange(b1, b2);
            await dbContext.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var integrityService = scope.ServiceProvider.GetRequiredService<IIntegrityService>();
            var report = await integrityService.RunIntegrityScanAsync();

            Assert.NotNull(report);
            Assert.Equal(IntegrityStatus.Critical, report.OverallStatus);
            Assert.True(report.TotalIssuesFound > 0);
        }
    }

    [Fact]
    public async Task RunIntegrityScanAsync_PostClosureTransaction_ReturnsCriticalStatus()
    {
        using var temp = new TempDatabase();
        using var provider = BuildProvider(temp.CreateDatabaseOptions());

        using (var scope = provider.CreateScope())
        {
            var init = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            await init.InitializeAsync();

            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var b = new Borrower("B002", "Jane Smith", "999", null, null, new DateTime(2026, 1, 1));
            b.CloseAccount(new DateTime(2026, 2, 1));
            dbContext.Borrowers.Add(b);

            var period = new FinancialPeriod("2026-02", new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));
            dbContext.FinancialPeriods.Add(period);
            await dbContext.SaveChangesAsync();

            var postClosureTxn = new Transaction(period.Id, Money.Create(500m), TransactionType.Deposit, new DateTime(2026, 2, 15), "Post closure payment");
            postClosureTxn.SetBorrowerId(b.Id);
            dbContext.Transactions.Add(postClosureTxn);
            await dbContext.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var integrityService = scope.ServiceProvider.GetRequiredService<IIntegrityService>();
            var report = await integrityService.RunIntegrityScanAsync();

            Assert.NotNull(report);
            Assert.Equal(IntegrityStatus.Critical, report.OverallStatus);
            Assert.True(report.TotalIssuesFound > 0);
        }
    }

    [Fact]
    public async Task RunIntegrityScanAsync_BorrowerWithInitialLoanDate_ReturnsPassStatus()
    {
        using var temp = new TempDatabase();
        using var provider = BuildProvider(temp.CreateDatabaseOptions());

        using (var scope = provider.CreateScope())
        {
            var init = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            await init.InitializeAsync();

            var borrowerService = scope.ServiceProvider.GetRequiredService<DhirDhar.Application.Borrowers.IBorrowerService>();
            var req = new DhirDhar.Application.Borrowers.Models.CreateBorrowerRequest(
                "DJ01",
                "Bhargavkumar Pravinchandra Luhar",
                null,
                null,
                "Ahmedabad",
                "9876543210",
                null,
                null,
                new DateTime(2026, 8, 18),
                50000m,
                new DateTime(2026, 8, 11),
                null,
                null,
                null,
                "Gold",
                null,
                null,
                3m);

            await borrowerService.CreateAsync(req);
        }

        using (var scope = provider.CreateScope())
        {
            var integrityService = scope.ServiceProvider.GetRequiredService<IIntegrityService>();
            var report = await integrityService.RunIntegrityScanAsync();

            Assert.NotNull(report);
            Assert.Equal(IntegrityStatus.Pass, report.OverallStatus);
            Assert.Equal(0, report.TotalIssuesFound);
        }
    }

    [Fact]
    public async Task InitializeAsync_HealsLegacyInconsistentEntryDate_IntegrityScanPasses()
    {
        using var temp = new TempDatabase();
        using var provider = BuildProvider(temp.CreateDatabaseOptions());

        using (var scope = provider.CreateScope())
        {
            var init = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            await init.InitializeAsync();

            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var borrower = new Borrower("DJ02", "Test Legacy", "111", null, null, new DateTime(2026, 8, 10));
            borrower.SetPhotosAndLoanType(null, null, "Gold", null, null, 25000m, new DateTime(2026, 8, 10), 2m);
            dbContext.Borrowers.Add(borrower);

            var period = new FinancialPeriod("2026-08", new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
            dbContext.FinancialPeriods.Add(period);
            await dbContext.SaveChangesAsync();

            var txn = new Transaction(borrower.Id, period.Id, Money.Create(25000m), TransactionType.Withdrawal, new DateTime(2026, 8, 10), "Initial Loan Amount", "INIT-DJ02");
            dbContext.Transactions.Add(txn);
            await dbContext.SaveChangesAsync();

            // Manually set inconsistent entry date via SQL to simulate existing legacy database record
            dbContext.Database.ExecuteSqlInterpolated($"UPDATE Borrowers SET EntryDate = '2026-08-18' WHERE Id = {borrower.Id};");
        }

        // Re-run initializer to simulate app startup healing
        using (var scope = provider.CreateScope())
        {
            var init = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            await init.InitializeAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var integrityService = scope.ServiceProvider.GetRequiredService<IIntegrityService>();
            var report = await integrityService.RunIntegrityScanAsync();

            Assert.NotNull(report);
            Assert.Equal(IntegrityStatus.Pass, report.OverallStatus);
            Assert.Equal(0, report.TotalIssuesFound);
        }
    }

    [Fact]
    public async Task RunIntegrityScanAsync_TransactionOnSameDayAsClosure_ReturnsPassStatus()
    {
        using var temp = new TempDatabase();
        using var provider = BuildProvider(temp.CreateDatabaseOptions());

        using (var scope = provider.CreateScope())
        {
            var init = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            await init.InitializeAsync();

            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var b = new Borrower("DJ38", "Pintu Bachu Machhar", "999", null, null, new DateTime(2021, 3, 15));
            b.CloseAccount(new DateTime(2021, 3, 26));
            dbContext.Borrowers.Add(b);

            var period = new FinancialPeriod("2021-03", new DateTime(2021, 3, 1), new DateTime(2021, 3, 31));
            dbContext.FinancialPeriods.Add(period);
            await dbContext.SaveChangesAsync();

            var initTxn = new Transaction(b.Id, period.Id, Money.Create(7000m), TransactionType.Withdrawal, new DateTime(2021, 3, 15), "Initial Loan Amount", "INIT-DJ38");
            // Final settlement transaction on the same day with a time component
            var sameDayTxn = new Transaction(b.Id, period.Id, Money.Create(7200m), TransactionType.Deposit, new DateTime(2021, 3, 26, 12, 27, 38), "Received Amount", "TXN-87AF2A23");
            dbContext.Transactions.AddRange(initTxn, sameDayTxn);
            await dbContext.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var integrityService = scope.ServiceProvider.GetRequiredService<IIntegrityService>();
            var report = await integrityService.RunIntegrityScanAsync();

            Assert.NotNull(report);
            Assert.Equal(IntegrityStatus.Pass, report.OverallStatus);
            Assert.Equal(0, report.TotalIssuesFound);
        }
    }

    [Fact]
    public async Task RunIntegrityScanAsync_MissingClosedDate_SafeRepairRestoresIntegrity()
    {
        using var temp = new TempDatabase();
        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        Guid borrowerId;

        using (var scope = provider.CreateScope())
        {
            var init = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            await init.InitializeAsync();

            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var b = new Borrower("DJ55", "Test Closed Missing Date", "111", null, null, new DateTime(2021, 1, 1));
            // Manually set status closed without ClosedDate
            dbContext.Borrowers.Add(b);
            await dbContext.SaveChangesAsync();
            borrowerId = b.Id;

            dbContext.Database.ExecuteSqlInterpolated($"UPDATE Borrowers SET Status = 3, ClosedDate = NULL WHERE Id = {borrowerId};");
        }

        using (var scope = provider.CreateScope())
        {
            var integrityService = scope.ServiceProvider.GetRequiredService<IIntegrityService>();
            var report = await integrityService.RunIntegrityScanAsync();

            Assert.NotNull(report);
            Assert.Equal(IntegrityStatus.Warning, report.OverallStatus);
            Assert.Equal(1, report.TotalIssuesFound);

            var issue = Assert.Single(report.Categories.SelectMany(c => c.Issues));
            Assert.True(issue.IsRepairable);
            Assert.Equal("FIX_MISSING_CLOSED_DATE", issue.RepairActionKey);

            // Execute safe auto repair
            var repairResult = await integrityService.RepairIssueAsync(issue.RepairActionKey!, issue.EntityId);
            Assert.True(repairResult.IsValid);

            // Re-scan
            var postRepairReport = await integrityService.RunIntegrityScanAsync();
            Assert.Equal(IntegrityStatus.Pass, postRepairReport.OverallStatus);
            Assert.Equal(0, postRepairReport.TotalIssuesFound);
        }
    }

    [Fact]
    public async Task RunIntegrityScanAsync_BorrowersAboveDJ50AndDJ100_ReturnsPassStatus()
    {
        using var temp = new TempDatabase();
        using var provider = BuildProvider(temp.CreateDatabaseOptions());

        using (var scope = provider.CreateScope())
        {
            var init = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
            await init.InitializeAsync();

            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var b50 = new Borrower("DJ50", "Borrower Fifty", "555", null, null, new DateTime(2021, 4, 1));
            var b104 = new Borrower("DJ104", "Borrower One Hundred Four", "104", null, null, new DateTime(2021, 5, 13));
            dbContext.Borrowers.AddRange(b50, b104);
            await dbContext.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var integrityService = scope.ServiceProvider.GetRequiredService<IIntegrityService>();
            var report = await integrityService.RunIntegrityScanAsync();

            Assert.NotNull(report);
            Assert.Equal(IntegrityStatus.Pass, report.OverallStatus);
            Assert.Equal(0, report.TotalIssuesFound);
        }
    }
}

