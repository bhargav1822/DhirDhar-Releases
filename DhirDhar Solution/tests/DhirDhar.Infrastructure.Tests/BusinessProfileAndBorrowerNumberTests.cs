using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Common.Exceptions;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Search;
using DhirDhar.Application.Search.Models;
using DhirDhar.Application.Settings;
using DhirDhar.Domain.Common;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Borrowers;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Search;
using DhirDhar.Infrastructure.Settings;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class BusinessProfileAndBorrowerNumberTests
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
        services.AddSingleton<ISearchService, SearchService>();

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return (context, scopeFactory, provider);
    }

    [Theory]
    [InlineData("DhirDhar Solution", "DS")]
    [InlineData("ABC Finance", "AF")]
    [InlineData("Shree Ram Finance", "SRF")]
    [InlineData("DhirDhar Solution India", "DSI")]
    [InlineData("Dwiti", "DW")]
    [InlineData("", "DS")]
    [InlineData("   ", "DS")]
    [InlineData(null, "DS")]
    public void BorrowerNumberHelper_GeneratePrefixFromBusinessName_GeneratesExpectedInitials(string? businessName, string expectedPrefix)
    {
        var prefix = BorrowerNumberHelper.GeneratePrefixFromBusinessName(businessName);
        Assert.Equal(expectedPrefix, prefix);
    }

    [Theory]
    [InlineData("DS", 1, "DS 01")]
    [InlineData("DS", 2, "DS 02")]
    [InlineData("DS", 9, "DS 09")]
    [InlineData("DS", 10, "DS 10")]
    [InlineData("DS", 99, "DS 99")]
    [InlineData("DS", 100, "DS 100")]
    [InlineData("DS", 101, "DS 101")]
    [InlineData("DS", 1002, "DS 1002")]
    [InlineData("DS", 5000, "DS 5000")]
    [InlineData("SRF", 1, "SRF 01")]
    [InlineData("AF", 1, "AF 01")]
    [InlineData("DSI", 1, "DSI 01")]
    public void BorrowerNumberHelper_FormatBorrowerNumber_FollowsRequiredFormatting(string prefix, long sequence, string expectedFormatted)
    {
        var formatted = BorrowerNumberHelper.FormatBorrowerNumber(prefix, sequence);
        Assert.Equal(expectedFormatted, formatted);
    }

    [Theory]
    [InlineData("DS 01", "DS", 1)]
    [InlineData("DS 09", "DS", 9)]
    [InlineData("DS 10", "DS", 10)]
    [InlineData("DS 99", "DS", 99)]
    [InlineData("DS 100", "DS", 100)]
    [InlineData("DS 1002", "DS", 1002)]
    [InlineData(" 1002 ", "DS", 1002)]
    [InlineData("01", "DS", 1)]
    [InlineData("#DS 1002", "DS", 1002)]
    [InlineData("SRF 01", "SRF", 1)]
    [InlineData("AF 01", "AF", 1)]
    [InlineData("DSI 01", "DSI", 1)]
    public void BorrowerNumberHelper_TryParseSequence_ParsesCorrectly(string input, string prefix, long expectedSequence)
    {
        var success = BorrowerNumberHelper.TryParseSequence(input, prefix, out var val);
        Assert.True(success);
        Assert.Equal(expectedSequence, val);
    }

    [Theory]
    [InlineData("", false, "BorrowerNumberRequired")]
    [InlineData("   ", false, "BorrowerNumberRequired")]
    [InlineData("abc", false, "InvalidBorrowerNumber")]
    [InlineData("-5", false, "InvalidBorrowerNumber")]
    [InlineData("12.5", false, "InvalidBorrowerNumber")]
    [InlineData("0", false, "BorrowerNumberGreaterThanZero")]
    [InlineData("01", true, null)]
    [InlineData("1002", true, null)]
    [InlineData("DS 1002", true, null)]
    public void BorrowerNumberHelper_ValidateSequenceInput_ValidatesExpectedly(string input, bool expectedValid, string? expectedErrorKey)
    {
        var isValid = BorrowerNumberHelper.ValidateSequenceInput(input, out var seq, out var errorKey);
        Assert.Equal(expectedValid, isValid);
        Assert.Equal(expectedErrorKey, errorKey);
    }

    [Fact]
    public async Task EmptyDatabase_StartsAtDS01_ThenIncrementsToDS02()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, _) = await CreateContextAndScopeAsync(temp);
        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

        // First borrower in empty DB
        var nextNum1 = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 01", nextNum1);

        var b1 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: string.Empty, // Auto-assign
            Name: "Ramesh Patel",
            Village: "Mehsana",
            LoanAmount: 10000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));

        Assert.Equal("DS 01", b1.BorrowerNumber);

        // Second borrower
        var nextNum2 = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 02", nextNum2);

        var b2 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: string.Empty,
            Name: "Suresh Shah",
            Village: "Ahmedabad",
            LoanAmount: 20000m,
            LoanDate: DateTime.Today,
            LoanType: "Gold",
            InterestRate: 3.00m));

        Assert.Equal("DS 02", b2.BorrowerNumber);
    }

    [Fact]
    public async Task BorrowerRegistration_ContinuesSequentially_FromDS99_ToDS100()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, _) = await CreateContextAndScopeAsync(temp);
        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

        // Seed DS 99 directly
        var existingB99 = new Borrower(
            "DS 99",
            "Preexisting Borrower 99",
            null, null, "Surat", null, null, null, null, DateTime.Today);
        dbContext.Borrowers.Add(existingB99);
        await dbContext.SaveChangesAsync();

        var nextNum = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 100", nextNum);

        var b100 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: string.Empty,
            Name: "New Borrower 100",
            Village: "Surat",
            LoanAmount: 5000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));

        Assert.Equal("DS 100", b100.BorrowerNumber);

        var nextNum101 = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 101", nextNum101);
    }

    [Fact]
    public async Task ManualNumberOverride_1002_NextBorrowerReceivesDS1003()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, _) = await CreateContextAndScopeAsync(temp);
        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

        // Auto generated is DS 01, but user enters 1002
        var nextNum1 = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 01", nextNum1);

        var b1002 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "1002",
            Name: "Manual 1002 Borrower",
            Village: "Patan",
            LoanAmount: 15000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));

        Assert.Equal("DS 1002", b1002.BorrowerNumber);

        // Next new borrower must automatically receive DS 1003
        var nextNum2 = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 1003", nextNum2);

        var b1003 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: string.Empty,
            Name: "Auto 1003 Borrower",
            Village: "Patan",
            LoanAmount: 20000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));

        Assert.Equal("DS 1003", b1003.BorrowerNumber);
    }

    [Fact]
    public async Task ManualNumberOverride_5000_NextBorrowerReceivesDS5001()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, _) = await CreateContextAndScopeAsync(temp);
        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

        var b5000 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "5000",
            Name: "High Number Borrower",
            Village: "Rajkot",
            LoanAmount: 50000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));

        Assert.Equal("DS 5000", b5000.BorrowerNumber);

        var nextNum = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 5001", nextNum);
    }

    [Fact]
    public async Task EditExistingBorrower_PreservesNumber_WhenUnchanged()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, _) = await CreateContextAndScopeAsync(temp);
        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

        var b = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "1002",
            Name: "Original Name",
            Village: "Village A",
            LoanAmount: 10000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));

        Assert.Equal("DS 1002", b.BorrowerNumber);

        // Edit borrower name without changing borrower number
        var updated = await borrowerService.UpdateAsync(new UpdateBorrowerRequest(
            Id: b.Id,
            Name: "Updated Name",
            Village: "Village A",
            LoanAmount: 10000m,
            LoanDate: DateTime.Today,
            InterestRate: 3.00m,
            BorrowerNumber: "1002"));

        Assert.Equal("DS 1002", updated.BorrowerNumber);
        Assert.Equal("Updated Name", updated.Name);

        // Next borrower should still be DS 1003
        var nextNum = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 1003", nextNum);
    }

    [Fact]
    public async Task EditExistingBorrower_ChangingNumber_UpdatesWatermarkAndNextNumber()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, _) = await CreateContextAndScopeAsync(temp);
        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

        var b = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "1002",
            Name: "Original Name",
            Village: "Village A",
            LoanAmount: 10000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));

        Assert.Equal("DS 1002", b.BorrowerNumber);

        // Change 1002 -> 2000
        var updated = await borrowerService.UpdateAsync(new UpdateBorrowerRequest(
            Id: b.Id,
            Name: "Original Name",
            Village: "Village A",
            LoanAmount: 10000m,
            LoanDate: DateTime.Today,
            InterestRate: 3.00m,
            BorrowerNumber: "2000"));

        Assert.Equal("DS 2000", updated.BorrowerNumber);

        // Next new borrower must receive DS 2001
        var nextNum = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 2001", nextNum);
    }

    [Fact]
    public async Task DuplicateBorrowerNumber_ThrowsValidationException()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, _) = await CreateContextAndScopeAsync(temp);
        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

        await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "1002",
            Name: "First Borrower",
            Village: "Village A",
            LoanAmount: 10000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));

        // Attempting to create duplicate 1002
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            borrowerService.CreateAsync(new CreateBorrowerRequest(
                BorrowerNumber: "1002",
                Name: "Second Borrower",
                Village: "Village B",
                LoanAmount: 20000m,
                LoanDate: DateTime.Today,
                LoanType: "Cash",
                InterestRate: 3.00m)));

        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DuplicateBorrowerNumber_OnUpdate_ThrowsValidationException()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, _) = await CreateContextAndScopeAsync(temp);
        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

        var b1 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "1001",
            Name: "First Borrower",
            Village: "Village A",
            LoanAmount: 10000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));

        var b2 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "1002",
            Name: "Second Borrower",
            Village: "Village B",
            LoanAmount: 20000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));

        // Attempting to update b2's number to b1's number (1001)
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            borrowerService.UpdateAsync(new UpdateBorrowerRequest(
                Id: b2.Id,
                Name: "Second Borrower",
                Village: "Village B",
                LoanAmount: 20000m,
                LoanDate: DateTime.Today,
                InterestRate: 3.00m,
                BorrowerNumber: "1001")));

        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeletedBorrowers_DoNotReuseBorrowerNumbers_AndSequencePersistsInDatabase()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, provider) = await CreateContextAndScopeAsync(temp);
        var borrowerService = provider.GetRequiredService<IBorrowerService>();

        var b1 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: string.Empty,
            Name: "Borrower One",
            Village: "Village 1",
            LoanAmount: 10000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));
        Assert.Equal("DS 01", b1.BorrowerNumber);

        var b2 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: string.Empty,
            Name: "Borrower Two",
            Village: "Village 2",
            LoanAmount: 20000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));
        Assert.Equal("DS 02", b2.BorrowerNumber);

        var b3 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: string.Empty,
            Name: "Borrower Three",
            Village: "Village 3",
            LoanAmount: 30000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));
        Assert.Equal("DS 03", b3.BorrowerNumber);

        // Delete borrower DS 03 from database
        var b3Entity = await dbContext.Borrowers.FindAsync(b3.Id);
        Assert.NotNull(b3Entity);
        dbContext.Borrowers.Remove(b3Entity);
        await dbContext.SaveChangesAsync();

        // Next borrower must remain DS 04, NOT reuse DS 03
        var nextNum = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 04", nextNum);

        var b4 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: string.Empty,
            Name: "Borrower Four",
            Village: "Village 4",
            LoanAmount: 40000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));
        Assert.Equal("DS 04", b4.BorrowerNumber);
    }

    [Fact]
    public async Task SearchBorrower_FindsByBorrowerNumber()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, provider) = await CreateContextAndScopeAsync(temp);
        var borrowerService = provider.GetRequiredService<IBorrowerService>();
        var searchService = provider.GetRequiredService<ISearchService>();

        var b1 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "1002",
            Name: "Kishore Kumar",
            Village: "Baroda",
            LoanAmount: 25000m,
            LoanDate: DateTime.Today,
            LoanType: "Gold",
            InterestRate: 3.00m));

        Assert.Equal("DS 1002", b1.BorrowerNumber);

        // Search full number: "DS 1002"
        var fullResults = await searchService.SearchAsync(new SearchFilter(SearchTerm: "DS 1002"));
        Assert.Contains(fullResults.Items, r => r.Id == b1.Id.ToString() || r.Title == "Kishore Kumar");

        // Search borrower name: "Kishore"
        var nameResults = await searchService.SearchAsync(new SearchFilter(SearchTerm: "Kishore"));
        Assert.Contains(nameResults.Items, r => r.Id == b1.Id.ToString());
    }

    [Fact]
    public async Task MultiThreaded_ConcurrentBorrowerCreation_PreventsDuplicateNumbers()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, _) = await CreateContextAndScopeAsync(temp);
        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

        int count = 20;
        var tasks = new List<Task<BorrowerSummary>>();

        for (int i = 0; i < count; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                return await borrowerService.CreateAsync(new CreateBorrowerRequest(
                    BorrowerNumber: string.Empty,
                    Name: $"Concurrent Borrower {index}",
                    Village: "Gandhinagar",
                    LoanAmount: 1000m + index,
                    LoanDate: DateTime.Today,
                    LoanType: "Cash",
                    InterestRate: 3.00m));
            }));
        }

        var createdBorrowers = await Task.WhenAll(tasks);

        var borrowerNumbers = createdBorrowers.Select(b => b.BorrowerNumber).ToList();
        Assert.Equal(count, borrowerNumbers.Count);
        Assert.Equal(count, borrowerNumbers.Distinct().Count()); // Zero duplicates
    }

    [Fact]
    public async Task DatabaseInitializer_PreservesExistingBorrowerNumbers_WithoutRenumbering()
    {
        using var temp = new TempDatabase();
        var options = temp.CreateOptions();

        var dbOptions = Options.Create(new DatabaseOptions { Provider = "Sqlite", DatabasePath = temp.FilePath });
        var pathService = new TestDatabasePathService(temp.FilePath);
        var dbContextFactory = new TestDbContextFactory(options);
        var initializer = new DatabaseInitializer(pathService, dbOptions, dbContextFactory, NullLogger<DatabaseInitializer>.Instance);

        var firstInit = await initializer.InitializeAsync();
        Assert.True(firstInit.IsSuccess);

        // Insert existing borrowers with custom/existing numbers
        using (var setupContext = new DhirDharDbContext(options))
        {
            var legacyB1 = new Borrower(
                "DS 01",
                "Alpha Legacy",
                null, null, "Patan", null, null, null, null,
                new DateTime(2025, 1, 10));

            var legacyB2 = new Borrower(
                "DS 24",
                "Beta Legacy",
                null, null, "Rajkot", null, null, null, null,
                new DateTime(2025, 2, 15));

            setupContext.Borrowers.AddRange(legacyB1, legacyB2);
            await setupContext.SaveChangesAsync();
        }

        // Run DatabaseInitializer again - must NOT modify or renumber existing records
        var result = await initializer.InitializeAsync();
        Assert.True(result.IsSuccess);

        using (var verifyContext = new DhirDharDbContext(options))
        {
            var bList = await verifyContext.Borrowers.OrderBy(b => b.BorrowerNumber.Length).ThenBy(b => b.BorrowerNumber).ToListAsync();
            Assert.Equal(2, bList.Count);

            Assert.Equal("DS 01", bList[0].BorrowerNumber);
            Assert.Equal("Alpha Legacy", bList[0].Name);

            Assert.Equal("DS 24", bList[1].BorrowerNumber);
            Assert.Equal("Beta Legacy", bList[1].Name);
        }

        // Verify that subsequent new borrower gets DS 25 (highest + 1)
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new DhirDharDbContext(options));
        services.AddSingleton<IBorrowerService, BorrowerService>();
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

        var nextNumber = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 25", nextNumber);

        var b25 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: string.Empty,
            Name: "Twenty Fifth Borrower",
            Village: "Mehsana",
            LoanAmount: 10000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));

        Assert.Equal("DS 25", b25.BorrowerNumber);
    }

    [Fact]
    public async Task NumberingTest_CompleteUserScenario_Tests1Through6()
    {
        using var temp = new TempDatabase();
        var (dbContext, scopeFactory, _) = await CreateContextAndScopeAsync(temp);
        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);

        // TEST 1: No borrowers. Open Add Borrower -> Expected: DS 01
        var test1Next = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 01", test1Next);

        // TEST 2: Create DS 01. Open Add Borrower again -> Expected: DS 02
        var b1 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "DS 01",
            Name: "User Test 1 Borrower",
            Village: "Village A",
            LoanAmount: 10000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));
        Assert.Equal("DS 01", b1.BorrowerNumber);

        var test2Next = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 02", test2Next);

        // TEST 3: Manually create DS 1002. Open Add Borrower -> Expected: DS 1003
        var b1002 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "1002",
            Name: "User Test 3 Borrower",
            Village: "Village B",
            LoanAmount: 15000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));
        Assert.Equal("DS 1002", b1002.BorrowerNumber);

        var test3Next = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 1003", test3Next);

        // TEST 4: Manually create DS 5000. Open Add Borrower -> Expected: DS 5001
        var b5000 = await borrowerService.CreateAsync(new CreateBorrowerRequest(
            BorrowerNumber: "DS 5000",
            Name: "User Test 4 Borrower",
            Village: "Village C",
            LoanAmount: 50000m,
            LoanDate: DateTime.Today,
            LoanType: "Cash",
            InterestRate: 3.00m));
        Assert.Equal("DS 5000", b5000.BorrowerNumber);

        var test4Next = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 5001", test4Next);

        // TEST 5: Edit DS 5000 without changing number -> Expected: DS 5000
        var b5000Edited = await borrowerService.UpdateAsync(new UpdateBorrowerRequest(
            Id: b5000.Id,
            Name: "User Test 4 Borrower Renamed",
            Village: "Village C",
            LoanAmount: 50000m,
            LoanDate: DateTime.Today,
            InterestRate: 3.00m,
            BorrowerNumber: "DS 5000"));
        Assert.Equal("DS 5000", b5000Edited.BorrowerNumber);
        Assert.Equal("User Test 4 Borrower Renamed", b5000Edited.Name);

        // Next automatic number remains DS 5001
        var test5Next = await borrowerService.GetNextBorrowerNumberAsync();
        Assert.Equal("DS 5001", test5Next);

        // TEST 6: Attempt duplicate DS 1002 -> Expected: validation error, no duplicate record
        var duplicateEx = await Assert.ThrowsAsync<ValidationException>(() =>
            borrowerService.CreateAsync(new CreateBorrowerRequest(
                BorrowerNumber: "1002",
                Name: "Duplicate 1002 Borrower",
                Village: "Village D",
                LoanAmount: 25000m,
                LoanDate: DateTime.Today,
                LoanType: "Cash",
                InterestRate: 3.00m)));
        Assert.Contains("already exists", duplicateEx.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class TestDatabasePathService : IDatabasePathService
{
    public TestDatabasePathService(string dbPath)
    {
        DatabasePath = dbPath;
        DatabaseDirectory = System.IO.Path.GetDirectoryName(dbPath) ?? string.Empty;
        ApplicationDataDirectory = DatabaseDirectory;
        BackupDirectory = System.IO.Path.Combine(DatabaseDirectory, "Backups");
        LogDirectory = System.IO.Path.Combine(DatabaseDirectory, "Logs");
    }

    public string ApplicationDataDirectory { get; }
    public string DatabaseDirectory { get; }
    public string DatabasePath { get; }
    public string BackupDirectory { get; }
    public string LogDirectory { get; }
}

public sealed class TestDbContextFactory : IDbContextFactory<DhirDharDbContext>
{
    private readonly DbContextOptions<DhirDharDbContext> _options;

    public TestDbContextFactory(DbContextOptions<DhirDharDbContext> options)
    {
        _options = options;
    }

    public DhirDharDbContext CreateDbContext() => new(_options);
}
