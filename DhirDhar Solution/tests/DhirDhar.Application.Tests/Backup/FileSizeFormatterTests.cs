using System;
using DhirDhar.Application.Backup;
using DhirDhar.Application.Localization;
using Xunit;

namespace DhirDhar.Application.Tests.Backup;

public sealed class FileSizeFormatterTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1280L, "1.25 KB")]
    [InlineData(128000L, "125 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(1310720L, "1.25 MB")]
    [InlineData(1572864L, "1.5 MB")]
    [InlineData(13054771L, "12.45 MB")]
    [InlineData(1073741824L, "1 GB")]
    [InlineData(1524713390L, "1.42 GB")]
    [InlineData(2308974411776L, "2.1 TB")]
    public void Format_ExactBinaryUnits_FormatsCorrectly(long bytes, string expected)
    {
        var result = FileSizeFormatter.Format(bytes);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Format_NullBytes_ReturnsUnknown()
    {
        long? nullBytes = null;
        var result = FileSizeFormatter.Format(nullBytes);
        Assert.Equal("Unknown", result);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(-100L)]
    public void Format_NegativeBytes_ReturnsUnknown(long negativeBytes)
    {
        var result = FileSizeFormatter.Format(negativeBytes);
        Assert.Equal("Unknown", result);
    }

    [Fact]
    public void Format_WithGujaratiDigitLocalization_LocalizesDigitsPreservingUnits()
    {
        var mockLoc = new MockGujaratiLocalizationService();

        Assert.Equal("૫૧૨ B", FileSizeFormatter.Format(512L, mockLoc));
        Assert.Equal("૧ KB", FileSizeFormatter.Format(1024L, mockLoc));
        Assert.Equal("૧.૫ MB", FileSizeFormatter.Format(1572864L, mockLoc));
        Assert.Equal("૧૨.૪૫ MB", FileSizeFormatter.Format(13054771L, mockLoc));
        Assert.Equal("૧ GB", FileSizeFormatter.Format(1073741824L, mockLoc));
        Assert.Equal("૨.૧ TB", FileSizeFormatter.Format(2308974411776L, mockLoc));
    }

    private sealed class MockGujaratiLocalizationService : ILocalizationService
    {
        public string GetString(string key, string? languageCode = null) => key;
        public string LocalizeText(string? text) => text ?? string.Empty;
        public string LocalizeText(string? text, string languageCode) => text ?? string.Empty;
        public string FormatInterestDescription(DateTime startDate, DateTime endDate, string? languageCode = null) => string.Empty;
        public string LocalizeDigits(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value
                .Replace('0', '૦')
                .Replace('1', '૧')
                .Replace('2', '૨')
                .Replace('3', '૩')
                .Replace('4', '૪')
                .Replace('5', '૫')
                .Replace('6', '૬')
                .Replace('7', '૭')
                .Replace('8', '૮')
                .Replace('9', '૯');
        }
        public string ToLocalizedCurrency(decimal amount) => string.Empty;
        public string ToLocalizedCurrency(decimal amount, bool negative) => string.Empty;
        public string ToLocalizedDecimal(decimal amount, string format = "N2") => string.Empty;
        public string ToLocalizedInteger(long value) => string.Empty;
        public string ToLocalizedDate(DateTime value, string format = "dd-MM-yyyy") => string.Empty;
        public string ToLocalizedDateTime(DateTime value, string format = "g") => string.Empty;
        public string ToLocalizedTime(DateTime value, string format = "hh:mm:ss tt") => string.Empty;
        public string ToLocalizedPercentage(decimal value, string format = "N2") => string.Empty;
        public string CurrentLanguage => "gu-IN";
        public event EventHandler? LanguageChanged { add { } remove { } }
        public void SetLanguage(string languageCode) { }
        public System.Collections.Generic.IReadOnlyList<SupportedLanguage> SupportedLanguages => Array.Empty<SupportedLanguage>();
        public System.Globalization.CultureInfo GetCulture() => System.Globalization.CultureInfo.InvariantCulture;
    }
}
