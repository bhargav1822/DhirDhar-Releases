using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Domain.Entities;
using DhirDhar.Infrastructure.Localization;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class MobileAndAadhaarLocalizationTests
{
    private readonly ILocalizationService _localizationService;
    private readonly ITranslationService _translationService;

    public MobileAndAadhaarLocalizationTests()
    {
        _localizationService = new LocalizationService();
        _translationService = new TestTranslationService();
    }

    private sealed class TestTranslationService : ITranslationService
    {
        public string Translate(string? text, string targetLanguageCode) =>
            string.IsNullOrWhiteSpace(text) ? string.Empty : ScriptTranslator.Translate(text, targetLanguageCode);

        public Task<string> TranslateAsync(string? text, string targetLanguageCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Translate(text, targetLanguageCode));

        public string DetectLanguage(string? text) => ScriptTranslator.DetectLanguage(text);

        public Task InvalidateTranslationsAsync(string oldText, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PreloadTranslationsAsync(System.Collections.Generic.IEnumerable<string> texts, string targetLanguageCode, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetTranslationAsync(string sourceText, string targetLanguageCode, string translatedText, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public void LocalizeDigits_WhenGujarati_ConvertsMobileNumberDigitsToGujarati()
    {
        _localizationService.SetLanguage("gu-IN");
        var englishPhone = "9876543210";
        var localized = _localizationService.LocalizeDigits(englishPhone);

        Assert.Equal("૯૮૭૬૫૪૩૨૧૦", localized);
    }

    [Fact]
    public void LocalizeDigits_WhenEnglish_LeavesMobileNumberDigitsAsAscii()
    {
        _localizationService.SetLanguage("en-IN");
        var englishPhone = "9876543210";
        var localized = _localizationService.LocalizeDigits(englishPhone);

        Assert.Equal("9876543210", localized);
    }

    [Fact]
    public void NormalizeDigitsToAscii_ConvertsGujaratiDigitsBackToAscii()
    {
        var gujaratiPhone = "૯૮૭૬૫૪૩૨૧૦";
        var ascii = LocalizationService.NormalizeDigitsToAscii(gujaratiPhone);

        Assert.Equal("9876543210", ascii);
    }

    [Fact]
    public void AadhaarMasking_WhenGujarati_PreservesEnglishXAndConvertsLast4Digits()
    {
        _localizationService.SetLanguage("gu-IN");
        var rawAadhar = "123456785780";
        var ascii = LocalizationService.NormalizeDigitsToAscii(rawAadhar);
        var last4 = ascii[^4..];
        var masked = $"XXXX XXXX {_localizationService.LocalizeDigits(last4)}";

        Assert.Equal("XXXX XXXX ૫૭૮૦", masked);
        Assert.StartsWith("XXXX XXXX ", masked);
    }

    [Fact]
    public void AadhaarMasking_WhenEnglish_PreservesEnglishXAndLeavesAsciiDigits()
    {
        _localizationService.SetLanguage("en-IN");
        var rawAadhar = "123456785780";
        var ascii = LocalizationService.NormalizeDigitsToAscii(rawAadhar);
        var last4 = ascii[^4..];
        var masked = $"XXXX XXXX {_localizationService.LocalizeDigits(last4)}";

        Assert.Equal("XXXX XXXX 5780", masked);
    }

    [Fact]
    public void BorrowerLocalizationExtensions_LocalizesContactAadhaarAndBorrowerNumber_ForGujarati()
    {
        var summary = new BorrowerSummary(
            Guid.NewGuid(),
            "DJ01",
            "Ramesh Patel",
            "9876543210",
            "Active",
            DateTime.Today,
            0m,
            0m,
            50000m,
            null,
            "Kantilal",
            "Patel",
            "Ahmedabad",
            "123456785780",
            null,
            null,
            "Gold",
            "Ring",
            10.5m,
            50000m,
            DateTime.Today,
            3.0m,
            null);

        var localized = summary.Localize(_translationService, "gu-IN");

        Assert.Equal("DJ૦૧", localized.BorrowerNumber);
        Assert.Equal("૯૮૭૬૫૪૩૨૧૦", localized.Contact);
        Assert.Equal("૧૨૩૪૫૬૭૮૫૭૮૦", localized.AadharNumber);
    }

    [Fact]
    public void BorrowerLocalizationExtensions_NormalizesToAscii_ForEnglish()
    {
        var summary = new BorrowerSummary(
            Guid.NewGuid(),
            "DJ૦૧",
            "રમેશ પટેલ",
            "૯૮૭૬૫૪૩૨૧૦",
            "Active",
            DateTime.Today,
            0m,
            0m,
            50000m,
            null,
            "કાંતિલાલ",
            "પટેલ",
            "અમદાવાદ",
            "૧૨૩૪૫૬૭૮૫૭૮૦",
            null,
            null,
            "Gold",
            "Ring",
            10.5m,
            50000m,
            DateTime.Today,
            3.0m,
            null);

        var localized = summary.Localize(_translationService, "en-IN");

        Assert.Equal("DJ01", localized.BorrowerNumber);
        Assert.Equal("9876543210", localized.Contact);
        Assert.Equal("123456785780", localized.AadharNumber);
    }
}
