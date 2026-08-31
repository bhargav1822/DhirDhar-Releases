using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Validation;
using DhirDhar.Application.Validation.Models;
using DhirDhar.Infrastructure.Localization;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

[Collection("LocalizationStateSyncTests")]
public class MultilingualDatePickerAndSettingsIntegrityTests
{
    [Theory]
    [InlineData("gu-IN", "gu-IN")]
    [InlineData("gujarati", "gu-IN")]
    [InlineData("ગુજરાતી", "gu-IN")]
    [InlineData("hi-IN", "hi-IN")]
    [InlineData("hindi", "hi-IN")]
    [InlineData("हिन्दी", "hi-IN")]
    [InlineData("mr-IN", "mr-IN")]
    [InlineData("marathi", "mr-IN")]
    [InlineData("bn-IN", "bn-IN")]
    [InlineData("bengali", "bn-IN")]
    [InlineData("pa-IN", "pa-IN")]
    [InlineData("punjabi", "pa-IN")]
    [InlineData("ta-IN", "ta-IN")]
    [InlineData("tamil", "ta-IN")]
    [InlineData("te-IN", "te-IN")]
    [InlineData("telugu", "te-IN")]
    [InlineData("kn-IN", "kn-IN")]
    [InlineData("kannada", "kn-IN")]
    [InlineData("ml-IN", "ml-IN")]
    [InlineData("malayalam", "ml-IN")]
    [InlineData("or-IN", "or-IN")]
    [InlineData("odia", "or-IN")]
    [InlineData("as-IN", "as-IN")]
    [InlineData("assamese", "as-IN")]
    [InlineData("en-IN", "en-IN")]
    [InlineData("english", "en-IN")]
    [InlineData("UNKNOWN_CODE", "en-IN")]
    public void NormalizeLanguageCode_ReturnsExpected(string? input, string expected)
    {
        var normalized = LocalizationService.NormalizeLanguageCode(input!);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void LocalizationService_SetLanguage_UpdatesCultureAndFiresEvent()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("gu-IN"); // Ensure baseline is Gujarati
        var eventFired = false;
        loc.LanguageChanged += (s, e) => eventFired = true;

        loc.SetLanguage("en-IN");

        Assert.True(eventFired);
        Assert.Equal("en-IN", loc.CurrentLanguage);
        var culture = loc.GetCulture();
        Assert.Equal(CultureInfo.InvariantCulture, culture);
    }

    [Fact]
    public void GujaratiDatePicker_MonthAndDayNames_AreInGujarati()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("gu-IN");
        var culture = loc.GetCulture();

        var gujaratiMonthNames = culture.DateTimeFormat.MonthNames;
        Assert.NotEmpty(gujaratiMonthNames);

        var date = new DateTime(2026, 8, 22);
        var formatted = loc.ToLocalizedDate(date, "dd MMMM yyyy");
        Assert.Contains("૨૦૨૬", formatted);
    }

    [Fact]
    public void EnglishDatePicker_MonthAndDayNames_AreInEnglish()
    {
        var loc = new LocalizationService();
        loc.SetLanguage("en-IN");

        var date = new DateTime(2026, 8, 22);
        var formatted = loc.ToLocalizedDate(date, "dd-MM-yyyy");
        Assert.Equal("22-08-2026", formatted);
    }

    [Fact]
    public void LocalizationService_All11Languages_HaveIntegrityKeys()
    {
        var loc = new LocalizationService();
        var languages = new[] { "en-IN", "gu-IN", "hi-IN", "mr-IN", "bn-IN", "pa-IN", "ta-IN", "te-IN", "kn-IN", "ml-IN", "or-IN", "as-IN" };

        foreach (var lang in languages)
        {
            loc.SetLanguage(lang);
            var integrity = loc.GetString("Integrity");
            var scanSummary = loc.GetString("ScanSummary");
            var runFullScan = loc.GetString("RunFullScan");
            var totalIssues = loc.GetString("TotalIssues");
            var scannedAt = loc.GetString("ScannedAt");

            Assert.False(string.IsNullOrWhiteSpace(integrity), $"Missing Integrity in {lang}");
            Assert.False(string.IsNullOrWhiteSpace(scanSummary), $"Missing ScanSummary in {lang}");
            Assert.False(string.IsNullOrWhiteSpace(runFullScan), $"Missing RunFullScan in {lang}");
            Assert.False(string.IsNullOrWhiteSpace(totalIssues), $"Missing TotalIssues in {lang}");
            Assert.False(string.IsNullOrWhiteSpace(scannedAt), $"Missing ScannedAt in {lang}");
        }
    }

    [Theory]
    [InlineData("en-IN", 0, "Scan completed. Overall status: Healthy, Issues found: 0.")]
    [InlineData("en-IN", 1, "Scan completed. Overall status: Issues Found, Issues found: 1.")]
    [InlineData("gu-IN", 0, "સ્કેન પૂર્ણ. એકંદર સ્થિતિ: તંદુરસ્ત, મળેલી સમસ્યાઓ: ૦.")]
    [InlineData("gu-IN", 2, "સ્કેન પૂર્ણ. એકંદર સ્થિતિ: સમસ્યાઓ મળી, મળેલી સમસ્યાઓ: ૨.")]
    [InlineData("hi-IN", 0, "स्कैन पूर्ण. समग्र स्थिति: स्वस्थ, मिली समस्याएं: ०.")]
    [InlineData("hi-IN", 3, "स्कैन पूर्ण. समग्र स्थिति: समस्याएं पाई गईं, मिली समस्याएं: ३.")]
    public void IntegrityScanCompleted_FormatsCorrectly_WithoutLiteralPlaceholders(string lang, int totalIssues, string expectedMessage)
    {
        var loc = new LocalizationService();
        loc.SetLanguage(lang);

        var template = loc.GetString("IntegrityScanCompleted");
        var statusStr = totalIssues == 0 ? loc.GetString("Healthy") : loc.GetString("IssuesFound");
        var issuesStr = loc.LocalizeDigits(totalIssues.ToString());

        var formattedMessage = string.Format(template, statusStr, issuesStr);

        Assert.DoesNotContain("{0}", formattedMessage);
        Assert.DoesNotContain("{1}", formattedMessage);
        Assert.Equal(expectedMessage, formattedMessage);
    }

    [Fact]
    public void IntegrityScanCompleted_All12Languages_NeverContainLiteralPlaceholders()
    {
        var loc = new LocalizationService();
        var languages = new[] { "en-IN", "gu-IN", "hi-IN", "mr-IN", "bn-IN", "pa-IN", "ta-IN", "te-IN", "kn-IN", "ml-IN", "or-IN", "as-IN" };

        foreach (var lang in languages)
        {
            loc.SetLanguage(lang);
            var template = loc.GetString("IntegrityScanCompleted");
            var healthyStatus = loc.GetString("Healthy");
            var issuesStatus = loc.GetString("IssuesFound");

            // Healthy (0 issues)
            var msg0 = string.Format(template, healthyStatus, loc.LocalizeDigits("0"));
            Assert.DoesNotContain("{0}", msg0);
            Assert.DoesNotContain("{1}", msg0);
            Assert.Contains(healthyStatus, msg0);

            // Issues (5 issues)
            var msg5 = string.Format(template, issuesStatus, loc.LocalizeDigits("5"));
            Assert.DoesNotContain("{0}", msg5);
            Assert.DoesNotContain("{1}", msg5);
            Assert.Contains(issuesStatus, msg5);
        }
    }
}

