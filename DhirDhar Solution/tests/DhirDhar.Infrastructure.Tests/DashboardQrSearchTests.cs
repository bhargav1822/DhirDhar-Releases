using System;
using DhirDhar.Application.Localization;
using DhirDhar.Application.QrCode;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.QrCode;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class DashboardQrSearchTests
{
    private readonly ILocalizationService _localizationService;
    private readonly IQrCodeService _qrCodeService;

    public DashboardQrSearchTests()
    {
        _localizationService = new LocalizationService();
        _qrCodeService = new QrCodeService();
    }

    [Theory]
    [InlineData("DHIRDHAR|ACCOUNT|DJ102", true, "DJ102")]
    [InlineData("dhirdhar|account|12345", true, "12345")]
    [InlineData("DHIRDHAR|ACCOUNT|BR-999", true, "BR-999")]
    [InlineData("OTHER_APP|ACCOUNT|101", false, "")]
    [InlineData("https://malicious-site.com/qr", false, "")]
    [InlineData("INVALID|FORMAT|TEST", false, "")]
    [InlineData("", false, "")]
    public void QrCodeService_TryParsePayload_ValidatesDhirDharFormat(string payload, bool expectedValid, string expectedBorrowerNumber)
    {
        var valid = _qrCodeService.TryParsePayload(payload, out var borrowerNumber);
        Assert.Equal(expectedValid, valid);
        if (expectedValid)
        {
            Assert.Equal(expectedBorrowerNumber, borrowerNumber);
        }
    }

    [Fact]
    public void QrCodeService_FormatPayload_GeneratesStandardDhirDharPayload()
    {
        var formatted = _qrCodeService.FormatPayload("DJ102");
        Assert.Equal("DHIRDHAR|ACCOUNT|DJ102", formatted);

        var valid = _qrCodeService.TryParsePayload(formatted, out var parsed);
        Assert.True(valid);
        Assert.Equal("DJ102", parsed);
    }

    [Fact]
    public void LocalizationService_ContainsQrScanningMessages_InEnglishAndGujarati()
    {
        _localizationService.SetLanguage("en-IN");
        Assert.Equal("Scan QR", _localizationService.GetString("ScanQr"));
        Assert.Equal("Invalid DhirDhar QR Code.", _localizationService.GetString("InvalidQrCode"));
        Assert.Equal("Borrower account not found.", _localizationService.GetString("BorrowerAccountNotFound"));

        _localizationService.SetLanguage("gu-IN");
        Assert.Equal("QR સ્કેન કરો", _localizationService.GetString("ScanQr"));
        Assert.Equal("અમાન્ય ધીરધાર QR કોડ.", _localizationService.GetString("InvalidQrCode"));
        Assert.Equal("ખાતાધારકનું ખાતું મળ્યું નથી.", _localizationService.GetString("BorrowerAccountNotFound"));
    }
}
