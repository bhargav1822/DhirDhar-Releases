using System;
using DhirDhar.Application.Localization;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.Services;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class DatePickerDefaultTodayTests
{
    private readonly ILocalizationService _localizationService;
    private readonly DateTimeService _dateTimeService;

    public DatePickerDefaultTodayTests()
    {
        _localizationService = new LocalizationService();
        _dateTimeService = new DateTimeService();
    }

    [Fact]
    public void DateTimeService_ReturnsCurrentRuntimeDate()
    {
        var now = _dateTimeService.Now;
        var today = DateTime.Today;

        Assert.Equal(today.Year, now.Year);
        Assert.Equal(today.Month, now.Month);
        Assert.Equal(today.Day, now.Day);
    }

    [Fact]
    public void LocalizationService_FormatsDatesCorrectly_ForEnglishAndGujarati()
    {
        var testDate = new DateTime(2026, 8, 22);

        _localizationService.SetLanguage("en-IN");
        var enFormatted = _localizationService.ToLocalizedDate(testDate, "dd-MM-yyyy");
        Assert.Equal("22-08-2026", enFormatted);

        _localizationService.SetLanguage("gu-IN");
        var guFormatted = _localizationService.ToLocalizedDate(testDate, "dd-MM-yyyy");
        Assert.Equal("૨૨-૦૮-૨૦૨૬", guFormatted);
    }

    [Fact]
    public void LocalizationService_NormalizeDigitsToAscii_NormalizesGujaratiDateDigits()
    {
        var gujaratiDate = "૨૨-૦૮-૨૦૨૬";
        var normalized = LocalizationService.NormalizeDigitsToAscii(gujaratiDate);
        Assert.Equal("22-08-2026", normalized);
    }
}
