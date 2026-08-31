using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Settings;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Borrowers;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Settings;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class BorrowerPaginationAndLimitTests
{
    private static async Task<(DhirDharDbContext dbContext, IServiceScopeFactory scopeFactory, IServiceProvider provider)> CreateContextAndScopeAsync(TempDatabase temp)
    {
        var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(context);
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IDateLocalizationService, DateLocalizationService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IBorrowerService, BorrowerService>();

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return (context, scopeFactory, provider);
    }

    [Fact]
    public async Task GetListAsync_WithMoreThan50Borrowers_ReturnsAllBorrowersWhenPageSizeZero()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, provider) = await CreateContextAndScopeAsync(temp);
        var borrowerService = provider.GetRequiredService<IBorrowerService>();

        // Create 75 borrowers
        const int totalToCreate = 75;
        for (int i = 1; i <= totalToCreate; i++)
        {
            await borrowerService.CreateAsync(new CreateBorrowerRequest(
                BorrowerNumber: string.Empty,
                Name: $"Borrower {i:D3}",
                Village: "Test Village",
                LoanAmount: 10000m + i,
                LoanDate: DateTime.Today,
                LoanType: "Cash",
                InterestRate: 2.00m));
        }

        // Call GetListAsync with pageSize = 0 (denotes unlimited)
        var result = await borrowerService.GetListAsync(BorrowerFilter.All, null, 1, 0);

        Assert.Equal(totalToCreate, result.TotalCount);
        Assert.Equal(totalToCreate, result.Items.Count);

        // Verify first and last borrower numbers
        Assert.Equal("DS 01", result.Items[0].BorrowerNumber);
        Assert.Equal("DS 75", result.Items[74].BorrowerNumber);
    }

    [Fact]
    public async Task GetListAsync_WithMoreThan50Borrowers_PreservesSequentialBorrowerNumbersAcrossAll()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, provider) = await CreateContextAndScopeAsync(temp);
        var borrowerService = provider.GetRequiredService<IBorrowerService>();

        const int count = 65;
        for (int i = 1; i <= count; i++)
        {
            await borrowerService.CreateAsync(new CreateBorrowerRequest(
                BorrowerNumber: string.Empty,
                Name: $"Person {i}",
                Village: "Patan",
                LoanAmount: 5000m,
                LoanDate: DateTime.Today,
                LoanType: "Cash",
                InterestRate: 2.50m));
        }

        var result = await borrowerService.GetListAsync(BorrowerFilter.All, null, 1, 0);
        Assert.Equal(count, result.Items.Count);

        for (int i = 0; i < count; i++)
        {
            var expectedNumber = $"DS {DhirDhar.Domain.Common.BorrowerNumberHelper.FormatSequence(i + 1)}";
            Assert.Equal(expectedNumber, result.Items[i].BorrowerNumber);
        }
    }

    [Fact]
    public async Task GetListAsync_WithMoreThan50Borrowers_FiltersActiveAndClosedCorrectlyAcrossAll()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, provider) = await CreateContextAndScopeAsync(temp);
        var borrowerService = provider.GetRequiredService<IBorrowerService>();

        var createdBorrowers = new List<BorrowerSummary>();
        for (int i = 1; i <= 60; i++)
        {
            var b = await borrowerService.CreateAsync(new CreateBorrowerRequest(
                BorrowerNumber: string.Empty,
                Name: $"Borrower {i}",
                Village: "Ahmedabad",
                LoanAmount: 15000m,
                LoanDate: DateTime.Today,
                LoanType: "Cash",
                InterestRate: 2.00m));
            createdBorrowers.Add(b);
        }

        // Close 15 borrowers (e.g. #10 to #24)
        for (int i = 9; i < 24; i++)
        {
            await borrowerService.CloseAccountAsync(createdBorrowers[i].Id, DateTime.Today);
        }

        var allResult = await borrowerService.GetListAsync(BorrowerFilter.All, null, 1, 0);
        var activeResult = await borrowerService.GetListAsync(BorrowerFilter.Active, null, 1, 0);
        var closedResult = await borrowerService.GetListAsync(BorrowerFilter.Closed, null, 1, 0);

        Assert.Equal(60, allResult.TotalCount);
        Assert.Equal(60, allResult.Items.Count);

        Assert.Equal(45, activeResult.TotalCount);
        Assert.Equal(45, activeResult.Items.Count);
        Assert.All(activeResult.Items, b => Assert.Equal("Active", b.Status));

        Assert.Equal(15, closedResult.TotalCount);
        Assert.Equal(15, closedResult.Items.Count);
        Assert.All(closedResult.Items, b => Assert.Equal("Closed", b.Status));
    }

    [Fact]
    public async Task GetListAsync_WithMoreThan50Borrowers_SearchesAcrossAllRecordsBeyond50()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, provider) = await CreateContextAndScopeAsync(temp);
        var borrowerService = provider.GetRequiredService<IBorrowerService>();

        for (int i = 1; i <= 70; i++)
        {
            string name = (i == 68) ? "UniqueSpecialName Patel" : $"Regular Borrower {i}";
            await borrowerService.CreateAsync(new CreateBorrowerRequest(
                BorrowerNumber: string.Empty,
                Name: name,
                Village: "Surat",
                LoanAmount: 20000m,
                LoanDate: DateTime.Today,
                LoanType: "Cash",
                InterestRate: 2.00m));
        }

        // Search for the 68th borrower by name
        var searchResult = await borrowerService.GetListAsync(BorrowerFilter.All, "UniqueSpecialName", 1, 0);
        Assert.Single(searchResult.Items);
        Assert.Equal("DS 68", searchResult.Items[0].BorrowerNumber);
        Assert.Equal("UniqueSpecialName Patel", searchResult.Items[0].Name);

        // Search by borrower number "70"
        var numberSearchResult = await borrowerService.GetListAsync(BorrowerFilter.All, "70", 1, 0);
        Assert.Single(numberSearchResult.Items);
        Assert.Equal("DS 70", numberSearchResult.Items[0].BorrowerNumber);
    }

    [Fact]
    public async Task GetListAsync_WithLargePageSize_DoesNotCapAt200()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, provider) = await CreateContextAndScopeAsync(temp);
        var borrowerService = provider.GetRequiredService<IBorrowerService>();

        // Create 250 borrowers in batch directly via DbContext for test speed
        var now = DateTime.UtcNow;
        var list = new List<Borrower>();
        for (int i = 1; i <= 250; i++)
        {
            var b = new Borrower(
                borrowerNumber: $"DJ{i:D3}",
                name: $"Bulk Borrower {i}",
                fatherName: null,
                surname: null,
                village: "Mehsana",
                phone: "9876543210",
                address: "Street",
                notes: null,
                aadharNumber: null,
                entryDate: now);
            list.Add(b);
        }
        dbContext.Borrowers.AddRange(list);
        await dbContext.SaveChangesAsync();

        // Request 250 items with pageSize = 250
        var result = await borrowerService.GetListAsync(BorrowerFilter.All, null, 1, 250);

        Assert.Equal(250, result.TotalCount);
        Assert.Equal(250, result.Items.Count);
        Assert.Equal("DJ001", result.Items[0].BorrowerNumber);
        Assert.Equal("DJ250", result.Items[249].BorrowerNumber);
    }

    [Fact]
    public async Task GetListAsync_CalculatesFinancialSummariesCorrectlyInBulk()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, provider) = await CreateContextAndScopeAsync(temp);
        var borrowerService = provider.GetRequiredService<IBorrowerService>();

        // Create 60 borrowers, with deposits and withdrawals
        var b1 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: string.Empty,
            Name: "First Borrower",
            Village: "Rajkot",
            LoanAmount: 50000m,
            LoanDate: DateTime.Today.AddDays(-10),
            LoanType: "Cash",
            InterestRate: 2.00m));

        for (int i = 2; i <= 60; i++)
        {
            await borrowerService.CreateAsync(new CreateBorrowerRequest(
                BorrowerNumber: string.Empty,
                Name: $"Borrower {i}",
                Village: "Rajkot",
                LoanAmount: 10000m,
                LoanDate: DateTime.Today,
                LoanType: "Cash",
                InterestRate: 2.00m));
        }

        // Add extra deposit transaction for b1
        var period = await dbContext.FinancialPeriods.FirstAsync();
        var depositTxn = new Transaction(
            b1.Id,
            period.Id,
            Money.Create(15000m),
            TransactionType.Deposit,
            DateTime.Today.AddDays(-2),
            "Partial repayment");
        dbContext.Transactions.Add(depositTxn);
        await dbContext.SaveChangesAsync();

        var result = await borrowerService.GetListAsync(BorrowerFilter.All, null, 1, 0);

        Assert.Equal(60, result.TotalCount);
        Assert.Equal(60, result.Items.Count);

        var first = result.Items.First(b => b.Id == b1.Id);
        Assert.Equal(15000m, first.TotalDeposits);
        Assert.Equal(50000m, first.TotalWithdrawals);
        Assert.Equal(-35000m, first.OutstandingAmount);
        Assert.NotNull(first.LastTransactionDate);
    }
}
