using System;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Borrowers;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class BorrowerAccountStatusClosedTests
{
    [Fact]
    public async Task Borrower_AccountStatus_FullLifecycle_Active_Close_Reopen()
    {
        using var tempDb = new TempDatabase();
        var options = tempDb.CreateOptions();
        await using (var initContext = new DhirDharDbContext(options))
        {
            await initContext.Database.EnsureCreatedAsync();
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => new DhirDharDbContext(options));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

        // 1. Create Active Borrower with Loan
        var loanDate = new DateTime(2026, 1, 15);
        var request = new CreateBorrowerRequest(
            BorrowerNumber: "B2001",
            Name: "રામસિંહ વાલસિંહ કટારા",
            FatherName: "વાલસિંહ",
            Surname: "કટારા",
            Village: "ઝાલોદ",
            Contact: "9876543210",
            Address: "Main Street",
            AadharNumber: "123456789012",
            EntryDate: DateTime.Today,
            LoanAmount: 50000.00m,
            LoanDate: loanDate,
            Notes: "Gold Loan",
            LoanType: "Gold",
            OrnamentType: "Bangles",
            OrnamentWeight: 25.0m,
            InterestRate: 2.0m);

        var created = await borrowerService.CreateAsync(request);
        Assert.NotNull(created);
        Assert.Equal(BorrowerStatus.Active.ToString(), created.Status);

        // Add a deposit transaction via DbContext
        await using (var context = new DhirDharDbContext(options))
        {
            var period = new FinancialPeriod("Test Period", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
            context.FinancialPeriods.Add(period);
            await context.SaveChangesAsync();

            var txn = new Transaction(
                created.Id,
                period.Id,
                Money.Create(10000.00m),
                TransactionType.Deposit,
                new DateTime(2026, 3, 1),
                "Part repayment",
                "TXN-001");
            context.Transactions.Add(txn);
            await context.SaveChangesAsync();
        }

        // 2. Verify in "All" and "Active" filters, but NOT in "Closed"
        var listAll = await borrowerService.GetListAsync(BorrowerFilter.All, null, 1, 50);
        Assert.Contains(listAll.Items, b => b.Id == created.Id);

        var listActive = await borrowerService.GetListAsync(BorrowerFilter.Active, null, 1, 50);
        Assert.Contains(listActive.Items, b => b.Id == created.Id);

        var listClosed = await borrowerService.GetListAsync(BorrowerFilter.Closed, null, 1, 50);
        Assert.DoesNotContain(listClosed.Items, b => b.Id == created.Id);

        // 3. Close Account
        var closeDate = new DateTime(2026, 6, 30);
        await borrowerService.CloseAccountAsync(created.Id, closeDate);

        // 4. Verify Account is Closed and NOT in Active, but IN Closed and All
        var afterClose = await borrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(afterClose);
        Assert.Equal(BorrowerStatus.Closed.ToString(), afterClose.Status);
        Assert.Equal(50000.00m, afterClose.LoanAmount);
        Assert.Equal("રામસિંહ વાલસિંહ કટારા", afterClose.Name);

        // Verify transaction history is fully intact in database (initial loan + repayment)
        await using (var context = new DhirDharDbContext(options))
        {
            var txns = await context.Transactions.Where(t => t.BorrowerId == created.Id).OrderBy(t => t.CreatedAt).ToListAsync();
            Assert.Equal(2, txns.Count);
            Assert.Equal(50000.00m, txns[0].Amount.Amount);
            Assert.Equal(10000.00m, txns[1].Amount.Amount);
        }

        // Check filter lists
        var listActiveAfterClose = await borrowerService.GetListAsync(BorrowerFilter.Active, null, 1, 50);
        Assert.DoesNotContain(listActiveAfterClose.Items, b => b.Id == created.Id);

        var listClosedAfterClose = await borrowerService.GetListAsync(BorrowerFilter.Closed, null, 1, 50);
        Assert.Contains(listClosedAfterClose.Items, b => b.Id == created.Id);

        var listAllAfterClose = await borrowerService.GetListAsync(BorrowerFilter.All, null, 1, 50);
        Assert.Contains(listAllAfterClose.Items, b => b.Id == created.Id);

        // 5. Search for Closed Borrower
        // Search in Closed filter
        var searchInClosed = await borrowerService.GetListAsync(BorrowerFilter.Closed, "કટારા", 1, 50);
        Assert.Contains(searchInClosed.Items, b => b.Id == created.Id);

        var searchInClosedByNumber = await borrowerService.GetListAsync(BorrowerFilter.Closed, created.BorrowerNumber, 1, 50);
        Assert.Contains(searchInClosedByNumber.Items, b => b.Id == created.Id);

        // Search in Active filter -> must NOT find closed borrower
        var searchInActive = await borrowerService.GetListAsync(BorrowerFilter.Active, "કટારા", 1, 50);
        Assert.DoesNotContain(searchInActive.Items, b => b.Id == created.Id);

        // Search in All filter -> MUST find closed borrower
        var searchInAll = await borrowerService.GetListAsync(BorrowerFilter.All, "કટારા", 1, 50);
        Assert.Contains(searchInAll.Items, b => b.Id == created.Id);

        // 6. Reopen Account
        var reopened = await borrowerService.ChangeStatusAsync(created.Id, BorrowerStatus.Active);
        Assert.Equal(BorrowerStatus.Active.ToString(), reopened.Status);

        var listActiveAfterReopen = await borrowerService.GetListAsync(BorrowerFilter.Active, null, 1, 50);
        Assert.Contains(listActiveAfterReopen.Items, b => b.Id == created.Id);

        var listClosedAfterReopen = await borrowerService.GetListAsync(BorrowerFilter.Closed, null, 1, 50);
        Assert.DoesNotContain(listClosedAfterReopen.Items, b => b.Id == created.Id);
    }

    [Fact]
    public async Task Borrower_CloseAccount_WithSelectedCustomDate_CalculatesInterestUpToSelectedDate()
    {
        using var tempDb = new TempDatabase();
        var options = tempDb.CreateOptions();
        await using (var initContext = new DhirDharDbContext(options))
        {
            await initContext.Database.EnsureCreatedAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new DhirDharDbContext(options));
        services.AddScoped<DhirDhar.Application.Interest.IInterestCalculationService, DhirDhar.Infrastructure.Interest.InterestCalculationService>();
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);
        var interestService = sp.GetRequiredService<DhirDhar.Application.Interest.IInterestCalculationService>();

        // 1. Create Active Borrower with Loan on 1 Jan 2026 at 2.0% per month, 100,000
        var loanDate = new DateTime(2026, 1, 1);
        var request = new CreateBorrowerRequest(
            BorrowerNumber: "B3001",
            Name: "Shree Borrower",
            FatherName: "Father",
            Surname: "Surname",
            Village: "Village",
            Contact: "9876543210",
            Address: "Address",
            AadharNumber: "123456789012",
            EntryDate: loanDate,
            LoanAmount: 100000.00m,
            LoanDate: loanDate,
            Notes: "Loan",
            LoanType: "Cash",
            OrnamentType: null,
            OrnamentWeight: null,
            InterestRate: 2.0m);

        var created = await borrowerService.CreateAsync(request);
        Assert.NotNull(created);

        // 2. Close Account with custom date: 1 March 2026 (2 months)
        var selectedCloseDate = new DateTime(2026, 3, 1);
        await borrowerService.CloseAccountAsync(created.Id, selectedCloseDate);

        var afterClose = await borrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(afterClose);
        Assert.Equal(BorrowerStatus.Closed.ToString(), afterClose.Status);
        Assert.Equal(selectedCloseDate, afterClose.ClosedDate);

        // 3. Calculate Interest requesting today/future date (e.g. 1 Dec 2026)
        // Since the account is closed on 1 March 2026, interest should only be calculated until closed date:
        // Complete Jan (2,000) + Complete Feb (2,000) = 4,000.00
        var futureDate = new DateTime(2026, 12, 1);
        var interestResult = await interestService.CalculateAsync(created.Id, futureDate);

        Assert.Equal(selectedCloseDate, interestResult.CalculationEndDate);
        Assert.True(interestResult.IsClosed);
        Assert.Equal(4000.00m, interestResult.TotalInterest);
        Assert.Equal(100000.00m, interestResult.ClosingPrincipal);
        Assert.Equal(104000.00m, interestResult.TotalOutstanding);
    }

    [Fact]
    public async Task Borrower_ReopenAccount_RestoresActiveStatusAndClearsClosedDate()
    {
        using var tempDb = new TempDatabase();
        var options = tempDb.CreateOptions();
        await using (var initContext = new DhirDharDbContext(options))
        {
            await initContext.Database.EnsureCreatedAsync();
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => new DhirDharDbContext(options));
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

        // 1. Create Borrower
        var request = new CreateBorrowerRequest(
            BorrowerNumber: "DHIR-100",
            Name: "કાંતિલાલ મહેતા",
            FatherName: "શાંતિલાલ",
            Surname: "મહેતા",
            Village: "અમદાવાદ",
            Contact: "9876543210",
            Address: "સી-૧૦૧",
            AadharNumber: "123456789012",
            EntryDate: new DateTime(2026, 1, 1),
            LoanAmount: 25000.00m,
            LoanDate: new DateTime(2026, 1, 1),
            Notes: "Gold Loan",
            LoanType: "Gold",
            OrnamentType: "Ring",
            OrnamentWeight: 10.5m,
            InterestRate: 2.5m);

        var created = await borrowerService.CreateAsync(request);
        Assert.NotNull(created);

        // 2. Close Account
        var selectedCloseDate = new DateTime(2026, 4, 1);
        await borrowerService.CloseAccountAsync(created.Id, selectedCloseDate);

        var afterClose = await borrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(afterClose);
        Assert.Equal(BorrowerStatus.Closed.ToString(), afterClose.Status);
        Assert.Equal(selectedCloseDate, afterClose.ClosedDate);

        // 3. Reopen Account via ChangeStatusAsync(Active)
        var reopened = await borrowerService.ChangeStatusAsync(created.Id, BorrowerStatus.Active);
        Assert.NotNull(reopened);
        Assert.Equal(BorrowerStatus.Active.ToString(), reopened.Status);
        Assert.Null(reopened.ClosedDate);

        // 4. Verify in Database
        var fromDb = await borrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(fromDb);
        Assert.Equal(BorrowerStatus.Active.ToString(), fromDb.Status);
        Assert.Null(fromDb.ClosedDate);
        Assert.Equal("કાંતિલાલ મહેતા", fromDb.Name);
        Assert.Equal(25000.00m, fromDb.LoanAmount);
        Assert.Equal(created.BorrowerNumber, fromDb.BorrowerNumber);
    }
}
