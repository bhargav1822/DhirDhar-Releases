using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Common.Exceptions;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Search;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Infrastructure.Borrowers;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Search;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class BorrowerFullNameTests
{
    private static async Task<(TempDatabase TempDb, IServiceProvider Sp)> CreateTestContextAsync()
    {
        var tempDb = new TempDatabase();
        var options = tempDb.CreateOptions();
        await using (var initContext = new DhirDharDbContext(options))
        {
            await initContext.Database.EnsureCreatedAsync();
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => new DhirDharDbContext(options));
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ITranslationService, TranslationService>();
        services.AddScoped<IBorrowerService, BorrowerService>();
        services.AddScoped<ISearchService, SearchService>();

        var sp = services.BuildServiceProvider();
        return (tempDb, sp);
    }

    [Fact]
    public async Task CreateBorrower_WithSingleFullName_SucceedsAndPersists()
    {
        var (tempDb, sp) = await CreateTestContextAsync();
        using (tempDb)
        {
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var service = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

            var request = new CreateBorrowerRequest(
                BorrowerNumber: "B1001",
                Name: "Bhargav Pravinchandra Panchal",
                FatherName: null,
                Surname: null,
                Village: "Sukhsar",
                Contact: "9876543210",
                Address: "Main Bazaar",
                AadharNumber: "123456789012",
                EntryDate: DateTime.Today,
                LoanAmount: 25000m,
                LoanDate: DateTime.Today,
                Notes: null,
                LoanType: "Cash",
                InterestRate: 3.00m);

            var summary = await service.CreateAsync(request);

            Assert.NotNull(summary);
            Assert.Equal("Bhargav Pravinchandra Panchal", summary.Name);
            Assert.Equal("Bhargav Pravinchandra Panchal", summary.FullName);
            Assert.Null(summary.FatherName);
            Assert.Null(summary.Surname);

            var loaded = await service.GetByIdAsync(summary.Id);
            Assert.NotNull(loaded);
            Assert.Equal("Bhargav Pravinchandra Panchal", loaded.Name);
            Assert.Equal("Bhargav Pravinchandra Panchal", loaded.FullName);
        }
    }

    [Fact]
    public async Task CreateBorrower_WithGujaratiFullName_SucceedsAndPersists()
    {
        var (tempDb, sp) = await CreateTestContextAsync();
        using (tempDb)
        {
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var service = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

            var request = new CreateBorrowerRequest(
                BorrowerNumber: "B1002",
                Name: "ભાર્ગવ પ્રવિણચંદ્ર પંચાલ",
                FatherName: null,
                Surname: null,
                Village: "સુખસર",
                Contact: "9876543210",
                Address: "મુખ્ય બજાર",
                AadharNumber: "123456789012",
                EntryDate: DateTime.Today,
                LoanAmount: 50000m,
                LoanDate: DateTime.Today,
                Notes: null,
                LoanType: "Cash",
                InterestRate: 3.00m);

            var summary = await service.CreateAsync(request);

            Assert.NotNull(summary);
            Assert.Equal("ભાર્ગવ પ્રવિણચંદ્ર પંચાલ", summary.Name);
            Assert.Equal("ભાર્ગવ પ્રવિણચંદ્ર પંચાલ", summary.FullName);

            var loaded = await service.GetByIdAsync(summary.Id);
            Assert.NotNull(loaded);
            Assert.Equal("ભાર્ગવ પ્રવિણચંદ્ર પંચાલ", loaded.Name);
            Assert.Equal("ભાર્ગવ પ્રવિણચંદ્ર પંચાલ", loaded.FullName);
        }
    }

    [Fact]
    public async Task UpdateBorrower_WithNewFullName_SucceedsAndPersists()
    {
        var (tempDb, sp) = await CreateTestContextAsync();
        using (tempDb)
        {
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var service = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

            var request = new CreateBorrowerRequest(
                BorrowerNumber: "B1003",
                Name: "Bhargav Panchal",
                FatherName: null,
                Surname: null,
                Village: "Sukhsar",
                Contact: "9876543210",
                Address: "Main Bazaar",
                AadharNumber: "123456789012",
                EntryDate: DateTime.Today,
                LoanAmount: 10000m,
                LoanDate: DateTime.Today,
                Notes: null,
                LoanType: "Cash",
                InterestRate: 3.00m);

            var created = await service.CreateAsync(request);

            var updateRequest = new UpdateBorrowerRequest(
                Id: created.Id,
                Name: "Bhargavkumar Pravinchandra Panchal",
                FatherName: null,
                Surname: null,
                Village: "Sukhsar",
                Phone: "9876543210",
                Address: "Main Bazaar",
                AadharNumber: "123456789012",
                Notes: null,
                LoanType: "Cash",
                LoanAmount: 10000m,
                LoanDate: DateTime.Today,
                InterestRate: 3.00m);

            var updated = await service.UpdateAsync(updateRequest);
            Assert.Equal("Bhargavkumar Pravinchandra Panchal", updated.Name);
            Assert.Equal("Bhargavkumar Pravinchandra Panchal", updated.FullName);

            var reloaded = await service.GetByIdAsync(created.Id);
            Assert.NotNull(reloaded);
            Assert.Equal("Bhargavkumar Pravinchandra Panchal", reloaded.Name);
            Assert.Equal("Bhargavkumar Pravinchandra Panchal", reloaded.FullName);
        }
    }

    [Fact]
    public void LegacyBorrower_WithSplitNameComponents_CombinesToFullName()
    {
        var summary = new BorrowerSummary(
            Id: Guid.NewGuid(),
            BorrowerNumber: "B001",
            Name: "Bhargav",
            Contact: "9876543210",
            Status: "Active",
            EntryDate: DateTime.Today,
            TotalDeposits: 0m,
            TotalWithdrawals: 0m,
            OutstandingAmount: 0m,
            LastTransactionDate: null,
            FatherName: "Pravinchandra",
            Surname: "Panchal",
            Village: "Sukhsar");

        Assert.Equal("Bhargav Pravinchandra Panchal", summary.FullName);
    }

    [Fact]
    public void BorrowerSummary_FullName_AvoidsDuplicateIfAlreadyCombined()
    {
        var summary = new BorrowerSummary(
            Id: Guid.NewGuid(),
            BorrowerNumber: "B002",
            Name: "Bhargav Pravinchandra Panchal",
            Contact: "9876543210",
            Status: "Active",
            EntryDate: DateTime.Today,
            TotalDeposits: 0m,
            TotalWithdrawals: 0m,
            OutstandingAmount: 0m,
            LastTransactionDate: null,
            FatherName: "Pravinchandra",
            Surname: "Panchal",
            Village: "Sukhsar");

        Assert.Equal("Bhargav Pravinchandra Panchal", summary.FullName);
    }

    [Fact]
    public async Task CreateBorrower_EmptyName_ThrowsValidationException()
    {
        var (tempDb, sp) = await CreateTestContextAsync();
        using (tempDb)
        {
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var service = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

            var request = new CreateBorrowerRequest(
                BorrowerNumber: "B1004",
                Name: "   ",
                Village: "Sukhsar",
                LoanAmount: 10000m,
                LoanDate: DateTime.Today,
                InterestRate: 3.00m);

            await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(request));
        }
    }

    [Fact]
    public void Localization_ContainsFullNameKeys_AcrossAllSupportedLanguages()
    {
        var loc = new LocalizationService();

        loc.SetLanguage("en-IN");
        Assert.Equal("Full Name", loc.GetString("FullName"));
        Assert.Equal("Enter full name", loc.GetString("FullNamePlaceholder"));
        Assert.Equal("Full Name is required.", loc.GetString("FullNameRequired"));

        loc.SetLanguage("gu-IN");
        Assert.Equal("પૂર્ણ નામ", loc.GetString("FullName"));
        Assert.Equal("પૂર્ણ નામ દાખલ કરો", loc.GetString("FullNamePlaceholder"));
        Assert.Equal("પૂર્ણ નામ જરૂરી છે.", loc.GetString("FullNameRequired"));

        loc.SetLanguage("hi-IN");
        Assert.Equal("पूरा नाम", loc.GetString("FullName"));
        Assert.Equal("पूरा नाम दर्ज करें", loc.GetString("FullNamePlaceholder"));
        Assert.Equal("पूरा नाम आवश्यक है।", loc.GetString("FullNameRequired"));
    }

    [Fact]
    public async Task SearchBorrowers_ByFullName_FindsMatch()
    {
        var (tempDb, sp) = await CreateTestContextAsync();
        using (tempDb)
        {
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);
            var searchService = new SearchService(scopeFactory, NullLogger<SearchService>.Instance);

            var created = await borrowerService.CreateAsync(new CreateBorrowerRequest(
                BorrowerNumber: "B2001",
                Name: "Bhargav Pravinchandra Panchal",
                Village: "Sukhsar",
                Contact: "9876543210",
                EntryDate: DateTime.Today,
                LoanAmount: 20000m,
                LoanDate: DateTime.Today,
                InterestRate: 3.00m));

            sp.GetRequiredService<ILocalizationService>().SetLanguage("en-IN");
            var results = await searchService.SearchBorrowersAsync("Bhargav Pravinchandra", "All", null, null);
            Assert.NotEmpty(results);
            Assert.Equal("Bhargav Pravinchandra Panchal", results[0].Name);

            var numResults = await searchService.SearchBorrowersAsync(created.BorrowerNumber, "All", null, null);
            Assert.NotEmpty(numResults);
            Assert.Equal(created.BorrowerNumber, numResults[0].BorrowerNumber);
        }
    }
}
