using System;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Reports;
using DhirDhar.Application.Search;
using DhirDhar.Application.Search.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Infrastructure.DependencyInjection;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class BorrowerMultilingualDataImmutabilityTests : IDisposable
{
    private readonly TempDatabase _tempDb;
    private readonly ServiceProvider _serviceProvider;
    private readonly IBorrowerService _borrowerService;
    private readonly LocalizationService _localizationService;
    private readonly ITranslationService _translationService;
    private readonly ISearchService _searchService;
    private readonly IReportService _reportService;

    public BorrowerMultilingualDataImmutabilityTests()
    {
        _tempDb = new TempDatabase();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(_tempDb.CreateDatabaseOptions());

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
        dbContext.Database.EnsureCreated();

        _borrowerService = _serviceProvider.GetRequiredService<IBorrowerService>();
        _localizationService = (LocalizationService)_serviceProvider.GetRequiredService<ILocalizationService>();
        _translationService = _serviceProvider.GetRequiredService<ITranslationService>();
        _searchService = _serviceProvider.GetRequiredService<ISearchService>();
        _reportService = _serviceProvider.GetRequiredService<IReportService>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _tempDb.Dispose();
    }

    [Fact]
    public async Task Borrower_MultilingualTranslationWorkflow_PreservesOriginalAndTranslatesDynamically()
    {
        // 1. Create borrower with Gujarati Unicode data
        const string originalGujaratiName = "રામસિંહ વાલસિંહ કટારા";
        const string originalFatherName = "વાલસિંહ";
        const string originalSurname = "કટારા";
        const string originalVillage = "સુખસર";

        var request = new CreateBorrowerRequest(
            "B-001",
            originalGujaratiName,
            originalFatherName,
            originalSurname,
            originalVillage,
            "9876543210",
            "ગામ સુખસર",
            "123456789012",
            DateTime.UtcNow,
            50000m,
            DateTime.Today,
            "નોંધ",
            null,
            null,
            "Cash",
            null,
            null,
            3.00m);

        var created = await _borrowerService.CreateAsync(request);

        // Verify original data in database is preserved untouched
        var loaded = await _borrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal(originalGujaratiName, loaded.Name);
        Assert.Equal(originalFatherName, loaded.FatherName);
        Assert.Equal(originalSurname, loaded.Surname);
        Assert.Equal(originalVillage, loaded.Village);

        // 2. Test Dynamic Translation to English
        var englishName = _translationService.Translate(loaded.Name, "en-IN");
        Assert.Equal("Ramsinh Valsinh Katara", englishName);

        // 3. Test Dynamic Translation to Hindi
        var hindiName = _translationService.Translate(loaded.Name, "hi-IN");
        Assert.Equal("रामसिंह वालसिंह कटारा", hindiName);

        // 4. Test Dynamic Translation back to Gujarati
        var gujaratiName = _translationService.Translate(loaded.Name, "gu-IN");
        Assert.Equal(originalGujaratiName, gujaratiName);

        // 5. Verify Reports Output Displays Dynamic Localization in Selected Language
        _localizationService.SetLanguage("en-IN");
        var reportEn = await _reportService.GenerateBorrowerStatementAsync(created.Id, DateTime.Today.AddDays(-30), DateTime.Today);
        Assert.Equal("Ramsinh Valsinh Katara", reportEn.BorrowerName);

        _localizationService.SetLanguage("hi-IN");
        var reportHi = await _reportService.GenerateBorrowerStatementAsync(created.Id, DateTime.Today.AddDays(-30), DateTime.Today);
        Assert.Equal("रामसिंह वालसिंह कटारा", reportHi.BorrowerName);

        _localizationService.SetLanguage("gu-IN");
        var reportGu = await _reportService.GenerateBorrowerStatementAsync(created.Id, DateTime.Today.AddDays(-30), DateTime.Today);
        Assert.Equal(originalGujaratiName, reportGu.BorrowerName);

        // 6. Test Multilingual Search in all languages
        var searchResultGu = await _searchService.SearchAsync(new SearchFilter("રામસિંહ", "All", null, null, null, null, null, "Date", true, 1, 10));
        Assert.Contains(searchResultGu.Items, r => r.Id == created.Id.ToString());

        var searchResultEn = await _searchService.SearchAsync(new SearchFilter("Ramsinh", "All", null, null, null, null, null, "Date", true, 1, 10));
        Assert.Contains(searchResultEn.Items, r => r.Id == created.Id.ToString());

        var searchResultHi = await _searchService.SearchAsync(new SearchFilter("रामसिंह", "All", null, null, null, null, null, "Date", true, 1, 10));
        Assert.Contains(searchResultHi.Items, r => r.Id == created.Id.ToString());
    }

    [Fact]
    public async Task EditingBorrower_UpdatesOriginalText_AndRegeneratesTranslations()
    {
        // 1. Create borrower
        var created = await _borrowerService.CreateAsync(new CreateBorrowerRequest(
            "B-EDIT", "રામસિંહ વાલસિંહ કટારા", "વાલસિંહ", "કટારા", "સુખસર", "9876543210", null, null, DateTime.UtcNow, 10000m, DateTime.Today, null, null, null, "Cash", null, null, 3m));

        Assert.Equal("Ramsinh Valsinh Katara", _translationService.Translate(created.Name, "en"));

        // 2. Edit Borrower Name to Suresh Patel
        const string newName = "સુરેશભાઈ પટેલ";
        await _borrowerService.UpdateAsync(new UpdateBorrowerRequest(
            created.Id, newName, "મોહનભાઈ", "પટેલ", "પાટણ", "9876543210", null, null, null, null, null, "Cash", null, null, 10000m, DateTime.Today, 3m));

        await _translationService.InvalidateTranslationsAsync("રામસિંહ વાલસિંહ કટારા");

        var updated = await _borrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(updated);
        Assert.Equal(newName, updated.Name);

        // Verify translation of newly edited name
        var newEnglishName = _translationService.Translate(updated.Name, "en");
        Assert.Contains("Suresh", newEnglishName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Patel", newEnglishName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BorrowerSummary_DynamicLocalization_TranslatesAcrossSupportedLanguages_PreservingIdentifiers()
    {
        var created = await _borrowerService.CreateAsync(new CreateBorrowerRequest(
            "DJ102",
            "ભાર્ગવકુમાર પ્રવિણચંદ્ર પંચાલ",
            "પ્રવિણચંદ્ર",
            "પંચાલ",
            "સુખસર",
            "9876543210",
            "સુખસર",
            "123456789012",
            new DateTime(2026, 1, 1),
            50000m,
            new DateTime(2026, 1, 1),
            "નોંધ",
            null,
            null,
            "Gold",
            "Ring",
            10.5m,
            3.0m));

        var loaded = await _borrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(loaded);

        // 1. English Localization
        var localizedEn = loaded.Localize(_translationService, "en");
        Assert.Equal(loaded.BorrowerNumber, localizedEn.BorrowerNumber);
        Assert.Equal("9876543210", localizedEn.Contact);
        Assert.Equal("123456789012", localizedEn.AadharNumber);
        Assert.Equal(50000m, localizedEn.LoanAmount);
        Assert.Contains("Bhargav", localizedEn.FullName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Panchal", localizedEn.FullName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Sukhsar", localizedEn.Village);
        Assert.Equal("Gold", localizedEn.LoanType);
        Assert.Equal("Ring", localizedEn.OrnamentType);

        // 2. Hindi Localization (with numeral localization for BorrowerNumber and Contact)
        var localizedHi = loaded.Localize(_translationService, "hi");
        Assert.Equal(ScriptTranslator.ConvertDigitsToIndic(loaded.BorrowerNumber, "hi"), localizedHi.BorrowerNumber);
        Assert.Equal("९८७६५४३२१०", localizedHi.Contact);
        Assert.Contains("भार्गव", localizedHi.FullName);
        Assert.Contains("पंचाल", localizedHi.FullName);
        Assert.Equal("सुखसर", localizedHi.Village);
        Assert.Equal("सोना", localizedHi.LoanType);
        Assert.Equal("अंगूठी", localizedHi.OrnamentType);

        // 3. Gujarati Localization (with numeral localization for BorrowerNumber and Contact)
        var localizedGu = loaded.Localize(_translationService, "gu");
        Assert.Equal(ScriptTranslator.ConvertDigitsToGujarati(loaded.BorrowerNumber), localizedGu.BorrowerNumber);
        Assert.Equal("૯૮૭૬૫૪૩૨૧૦", localizedGu.Contact);
        Assert.Contains("ભાર્ગવ", localizedGu.FullName);
        Assert.Contains("પંચાલ", localizedGu.FullName);
        Assert.Equal("સુખસર", localizedGu.Village);
        Assert.Equal("સોનું", localizedGu.LoanType);
        Assert.Equal("વીંટી", localizedGu.OrnamentType);
    }
}
