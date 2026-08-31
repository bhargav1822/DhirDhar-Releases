using System;
using System.Collections.Generic;
using System.Linq;
using DhirDhar.Application.Localization;
using DhirDhar.Infrastructure.Localization;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class ComprehensiveLocalizationTests
{
    private static readonly string[] All12LanguageCodes = new[]
    {
        "en-IN",
        "gu-IN",
        "hi-IN",
        "mr-IN",
        "bn-IN",
        "pa-IN",
        "ta-IN",
        "te-IN",
        "kn-IN",
        "ml-IN",
        "or-IN",
        "as-IN"
    };

    [Fact]
    public void SupportedLanguages_ContainsAll12Languages()
    {
        var service = new LocalizationService();
        var supported = service.SupportedLanguages;

        Assert.Equal(12, supported.Count);
        foreach (var code in All12LanguageCodes)
        {
            Assert.Contains(supported, l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData("en-IN")]
    [InlineData("gu-IN")]
    [InlineData("hi-IN")]
    [InlineData("mr-IN")]
    [InlineData("bn-IN")]
    [InlineData("pa-IN")]
    [InlineData("ta-IN")]
    [InlineData("te-IN")]
    [InlineData("kn-IN")]
    [InlineData("ml-IN")]
    [InlineData("or-IN")]
    [InlineData("as-IN")]
    public void All12Languages_HaveAllKeys_AndNonEmptyValues(string languageCode)
    {
        var service = new LocalizationService();
        service.SetLanguage(languageCode);

        // Core critical keys that every single language must provide
        var criticalKeys = new[]
        {
            "Dashboard", "Borrowers", "Transactions", "Interest", "Ledger",
            "Reports", "Backup", "Security", "Integrity", "Settings",
            "TotalBorrowers", "ActiveBorrowers", "TotalDeposits", "TotalWithdrawals",
            "TotalInterest", "TotalOutstanding", "AddBorrower", "NewTransaction",
            "FullName", "Village", "MobileNumber", "LoanAmount", "InterestRate",
            "LoanDate", "LoanType", "Cash", "Gold", "Silver",
            "Deposit", "Withdrawal", "PaymentReceived", "AmountGiven",
            "SinglePcAnnualOfflineLicense", "RenewChangeLicense", "LicenseStatus",
            "ConnectingToGoogleDrive", "ConnectedStatus", "NotConnectedStatus",
            "AnnualOfflineLicenseActivation", "LicenseActivationInstructions",
            "SerialKey", "ActivateLicense", "InputMode", "InputModePhonetic"
        };

        foreach (var key in criticalKeys)
        {
            var value = service.GetString(key);
            Assert.False(string.IsNullOrWhiteSpace(value), $"Key '{key}' is empty in language '{languageCode}'");
            if (languageCode != "en-IN")
            {
                // Must not be identical to key name (meaning missing)
                Assert.NotEqual(key, value);
            }
        }
    }

    [Fact]
    public void FormatInterestDescription_All12Languages_ProducesCorrectLocalizedFormat()
    {
        var service = new LocalizationService();
        var start = new DateTime(2026, 5, 22);
        var end = new DateTime(2026, 5, 31);

        var expectedMap = new Dictionary<string, string>
        {
            ["en-IN"] = "Interest for 22-May to 31-May",
            ["gu-IN"] = "22 મે થી 31 મે સુધીનું વ્યાજ",
            ["hi-IN"] = "22 मई से 31 मई तक का ब्याज",
            ["mr-IN"] = "22 मे ते 31 मे पर्यंतचे व्याज",
            ["bn-IN"] = "22 মে থেকে 31 মে পর্যন্ত সুদ",
            ["pa-IN"] = "22 ਮਈ ਤੋਂ 31 ਮਈ ਤੱਕ ਦਾ ਵਿਆਜ",
            ["ta-IN"] = "22 மே முதல் 31 மே வரை வட்டி",
            ["te-IN"] = "22 మే నుండి 31 మే వరకు వడ్డీ",
            ["kn-IN"] = "22 ಮೇ ರಿಂದ 31 ಮೇ ವರೆಗೆ ಬಡ್ಡಿ",
            ["ml-IN"] = "22 മെയ് മുതൽ 31 മെയ് വരെ പലിശ",
            ["or-IN"] = "22 ମେ ରୁ 31 ମେ ପର୍ଯ୍ୟନ୍ତ ସୁଧ",
            ["as-IN"] = "22 মে'ৰ পৰা 31 মে'লৈ সূত"
        };

        foreach (var (lang, expected) in expectedMap)
        {
            var actual = service.FormatInterestDescription(start, end, lang);
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData("Interest for 22-May to 31-May", "gu-IN", "22 મે થી 31 મે સુધીનું વ્યાજ")]
    [InlineData("Interest for 22-May to 31-May", "hi-IN", "22 मई से 31 मई तक का ब्याज")]
    [InlineData("Interest for 22-May to 31-May", "mr-IN", "22 मे ते 31 मे पर्यंतचे व्याज")]
    [InlineData("Interest for 22-May to 31-May", "bn-IN", "22 মে থেকে 31 মে পর্যন্ত সুদ")]
    [InlineData("Interest for 22-May to 31-May", "ta-IN", "22 மே முதல் 31 மே வரை வட்டி")]
    [InlineData("Interest for 22-May to 31-May", "te-IN", "22 మే నుండి 31 మే వరకు వడ్డీ")]
    [InlineData("Interest for 22-May to 31-May", "kn-IN", "22 ಮೇ ರಿಂದ 31 ಮೇ ವರೆಗೆ ಬಡ್ಡಿ")]
    [InlineData("Interest for 22-May to 31-May", "ml-IN", "22 മെയ് മുതൽ 31 മെയ് വരെ പലിശ")]
    [InlineData("Interest for 22-May to 31-May", "or-IN", "22 ମେ ରୁ 31 ମେ ପର୍ଯ୍ୟନ୍ତ ସୁଧ")]
    [InlineData("Interest for 22-May to 31-May", "as-IN", "22 মে'ৰ পৰা 31 মে'লৈ সূত")]
    [InlineData("22 ಮೇ ರಿಂದ 31 ಮೇ ವರೆಗೆ ಬಡ್ಡಿ", "en-IN", "Interest for 22-May to 31-May")]
    [InlineData("22 మే నుండి 31 మే వరకు వడ్డీ", "en-IN", "Interest for 22-May to 31-May")]
    public void LocalizeText_DynamicInterest_TranslatesBidirectionally(string source, string targetLang, string expected)
    {
        var service = new LocalizationService();
        var actual = service.LocalizeText(source, targetLang);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Deposit transaction", "gu-IN", "જમા વ્યવહાર")]
    [InlineData("Deposit transaction", "hi-IN", "जमा लेन-देन")]
    [InlineData("Deposit transaction", "mr-IN", "जमा (मिळाले) व्यवहार")]
    [InlineData("Withdrawal transaction", "gu-IN", "ઉપાડ વ્યવહાર")]
    [InlineData("Withdrawal transaction", "hi-IN", "निकासी लेन-देन")]
    [InlineData("Withdrawal transaction", "ta-IN", "பற்று (வழங்கியது) பரிவர்த்தனை")]
    public void LocalizeText_DynamicTransactionDescriptions_TranslatesCorrectly(string source, string targetLang, string expected)
    {
        var service = new LocalizationService();
        var actual = service.LocalizeText(source, targetLang);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("૧૨૩૪૫", "12345")]
    [InlineData("१२३४५", "12345")]
    [InlineData("১২৩৪৫", "12345")]
    [InlineData("੧੨੩੪੫", "12345")]
    [InlineData("௧௨௩௪௫", "12345")]
    [InlineData("౧౨౩౪౫", "12345")]
    [InlineData("೧೨೩೪೫", "12345")]
    [InlineData("൧൨൩൪൫", "12345")]
    [InlineData("୧୨୩୪୫", "12345")]
    public void LocalizationService_NormalizeDigitsToAscii_ConvertsAllIndicDigitsToAscii(string indicNumber, string expectedAscii)
    {
        var actual = LocalizationService.NormalizeDigitsToAscii(indicNumber);
        Assert.Equal(expectedAscii, actual);
    }

    [Theory]
    [InlineData("પોતે", "en-IN", "Self")]
    [InlineData("પોતે", "gu-IN", "પોતે")]
    [InlineData("પોતે", "hi-IN", "स्वयं")]
    [InlineData("પોતે", "mr-IN", "स्वतः")]
    [InlineData("Self", "gu-IN", "પોતે")]
    [InlineData("Self", "hi-IN", "स्वयं")]
    [InlineData("Self", "en-IN", "Self")]
    [InlineData("स्वयं", "en-IN", "Self")]
    [InlineData("स्वयं", "gu-IN", "પોતે")]
    [InlineData("નામ", "en-IN", "Name")]
    [InlineData("નામ", "hi-IN", "नाम")]
    [InlineData("નામ", "gu-IN", "નામ")]
    [InlineData("Name", "gu-IN", "નામ")]
    [InlineData("Name", "hi-IN", "नाम")]
    [InlineData("Notes.Self", "en-IN", "Self")]
    [InlineData("Notes.Self", "gu-IN", "પોતે")]
    [InlineData("Notes.Self", "hi-IN", "स्वयं")]
    [InlineData("Notes.Name", "en-IN", "Name")]
    [InlineData("Notes.Name", "gu-IN", "નામ")]
    [InlineData("Notes.Name", "hi-IN", "नाम")]
    [InlineData("TXN-101 - પોતે", "en-IN", "TXN-101 - Self")]
    [InlineData("TXN-101 - પોતે", "hi-IN", "TXN-101 - स्वयं")]
    [InlineData("TXN-101 - પોતે", "gu-IN", "TXN-101 - પોતે")]
    public void LocalizeText_NotesAndSelf_TranslatesAccordingToActiveLanguage(string source, string targetLang, string expected)
    {
        var service = new LocalizationService();
        var actual = service.LocalizeText(source, targetLang);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UserDataImmutability_BorrowerNamesAndIdentifiers_AreNeverModified()
    {
        var service = new LocalizationService();

        foreach (var lang in All12LanguageCodes)
        {
            service.SetLanguage(lang);

            // Identifiers must NEVER be modified or translated
            Assert.Equal("DJ01", service.LocalizeText("DJ01"));
            Assert.Equal("DJ99", service.LocalizeText("DJ99"));
            Assert.Equal("DHIRDHAR-2026-ABCD-1234", service.LocalizeText("DHIRDHAR-2026-ABCD-1234"));

            // User-entered borrower names and custom notes must NEVER be corrupted
            Assert.Equal("Bhargav Patel", service.LocalizeText("Bhargav Patel"));
            Assert.Equal("Ramsinh Katara", service.LocalizeText("Ramsinh Katara"));
            Assert.Equal("Special personal loan notes for business expansion", service.LocalizeText("Special personal loan notes for business expansion"));
        }
    }
}

