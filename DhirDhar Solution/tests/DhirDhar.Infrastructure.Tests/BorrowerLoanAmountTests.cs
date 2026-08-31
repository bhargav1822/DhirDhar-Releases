using System;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Infrastructure.Borrowers;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class BorrowerLoanAmountTests
{
    [Fact]
    public async Task CreateBorrower_WithLoanAmount_SavesAndReloadsLoanAmountExactValue()
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

        var testLoanDate = new DateTime(2026, 8, 12);
        var request = new CreateBorrowerRequest(
            BorrowerNumber: "B1001",
            Name: "Ramesh Patel",
            FatherName: "Suresh Patel",
            Surname: "Patel",
            Village: "Mehsana",
            Contact: "9876543210",
            Address: null,
            AadharNumber: "123456789012",
            EntryDate: DateTime.Today,
            LoanAmount: 100000.00m,
            LoanDate: testLoanDate,
            Notes: null,
            LoanType: "Gold",
            OrnamentType: "Ring",
            OrnamentWeight: 10.5m,
            InterestRate: 3.0m);

        var createdSummary = await borrowerService.CreateAsync(request);

        Assert.NotNull(createdSummary);
        Assert.Equal(100000.00m, createdSummary.LoanAmount);
        Assert.Equal(testLoanDate, createdSummary.LoanDate);

        // Reload from database
        var reloaded = await borrowerService.GetByIdAsync(createdSummary.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(100000.00m, reloaded.LoanAmount);
        Assert.Equal(testLoanDate, reloaded.LoanDate);

        // Verify initial transaction in database
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var initialTxn = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Transactions, t => t.BorrowerId == createdSummary.Id);
            Assert.NotNull(initialTxn);
            Assert.Equal(100000.00m, initialTxn.Amount.Amount);
            Assert.Equal(testLoanDate, initialTxn.OccurredOn);
            Assert.Equal(DhirDhar.Domain.Enums.TransactionType.Withdrawal, initialTxn.Type);
        }

        // Update Loan Amount and Loan Date
        var updatedDate = new DateTime(2026, 8, 15);
        var updateRequest = new UpdateBorrowerRequest(
            Id: reloaded.Id,
            Name: reloaded.Name,
            FatherName: reloaded.FatherName,
            Surname: reloaded.Surname,
            Village: reloaded.Village,
            Phone: reloaded.Contact,
            Address: null,
            AadharNumber: reloaded.AadharNumber,
            Notes: null,
            LoanType: reloaded.LoanType,
            OrnamentType: reloaded.OrnamentType,
            OrnamentWeight: reloaded.OrnamentWeight,
            LoanAmount: 150000.00m,
            LoanDate: updatedDate,
            InterestRate: 3.0m);

        var updatedSummary = await borrowerService.UpdateAsync(updateRequest);
        Assert.Equal(150000.00m, updatedSummary.LoanAmount);
        Assert.Equal(updatedDate, updatedSummary.LoanDate);

        var reloadedUpdated = await borrowerService.GetByIdAsync(reloaded.Id);
        Assert.NotNull(reloadedUpdated);
        Assert.Equal(150000.00m, reloadedUpdated.LoanAmount);
        Assert.Equal(updatedDate, reloadedUpdated.LoanDate);

        // Verify updated transaction in database
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var initialTxn = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Transactions, t => t.BorrowerId == createdSummary.Id);
            Assert.NotNull(initialTxn);
            Assert.Equal(150000.00m, initialTxn.Amount.Amount);
            Assert.Equal(updatedDate, initialTxn.OccurredOn);
        }
    }

    [Fact]
    public async Task UpdateBorrower_EditMode_UpdatesOnlyMobileAndInterestRate_PreservesAllOtherFields()
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

        var origLoanDate = new DateTime(2023, 8, 12);
        var createReq = new CreateBorrowerRequest(
            BorrowerNumber: "B2002",
            Name: "Kishore Kumar",
            FatherName: "Ram Kumar",
            Surname: "Sharma",
            Village: "Udaipur",
            Contact: "9998887770",
            Address: null,
            AadharNumber: "987654321098",
            EntryDate: DateTime.Today,
            LoanAmount: 50000.00m,
            LoanDate: origLoanDate,
            Notes: null,
            LoanType: "Gold",
            OrnamentType: "Necklace",
            OrnamentWeight: 15.0m,
            InterestRate: 3.0m);

        var created = await borrowerService.CreateAsync(createReq);

        // Edit Borrower mode: User changes ONLY Mobile Number ("9111222333") and Interest Rate (2.5%)
        var updateReq = new UpdateBorrowerRequest(
            Id: created.Id,
            Name: created.Name,
            FatherName: created.FatherName,
            Surname: created.Surname,
            Village: created.Village,
            Phone: "9111222333",
            Address: null,
            AadharNumber: created.AadharNumber,
            Notes: null,
            LoanType: created.LoanType,
            OrnamentType: created.OrnamentType,
            OrnamentWeight: created.OrnamentWeight,
            LoanAmount: created.LoanAmount,
            LoanDate: created.LoanDate,
            InterestRate: 2.5m);

        var updatedSummary = await borrowerService.UpdateAsync(updateReq);

        Assert.Equal("9111222333", updatedSummary.Contact);
        Assert.Equal(2.5m, updatedSummary.InterestRate);

        // Verify preserved fields
        Assert.Equal("Kishore Kumar", updatedSummary.Name);
        Assert.Equal("Ram Kumar", updatedSummary.FatherName);
        Assert.Equal("Sharma", updatedSummary.Surname);
        Assert.Equal("Udaipur", updatedSummary.Village);
        Assert.Equal("987654321098", updatedSummary.AadharNumber);
        Assert.Equal("Gold", updatedSummary.LoanType);
        Assert.Equal("Necklace", updatedSummary.OrnamentType);
        Assert.Equal(15.0m, updatedSummary.OrnamentWeight);
        Assert.Equal(50000.00m, updatedSummary.LoanAmount);
        Assert.Equal(origLoanDate, updatedSummary.LoanDate);
    }
}
