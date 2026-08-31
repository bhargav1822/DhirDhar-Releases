using DhirDhar.Application.Localization;
using DhirDhar.Infrastructure.Localization;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public sealed class ScriptTranslatorTests
{
    [Theory]
    [InlineData("Bhargav", "gu-IN", "ભાર્ગવ")]
    [InlineData("Bhargav", "hi-IN", "भार्गव")]
    [InlineData("Bhargav", "en-IN", "Bhargav")]
    [InlineData("Pravinchandra", "gu-IN", "પ્રવિણચંદ્ર")]
    [InlineData("Pravinchandra", "hi-IN", "प्रविणचन्द्र")]
    [InlineData("Panchal", "gu-IN", "પંચાલ")]
    [InlineData("Panchal", "hi-IN", "पंचाल")]
    [InlineData("Ramsinh Valsinh Katara", "gu-IN", "રામસિંહ વાલસિંહ કટારા")]
    [InlineData("Ramsinh Valsinh Katara", "hi-IN", "रामसिंह वालसिंह कटारा")]
    [InlineData("રામસિંહ વાલસિંહ કટારા", "en-IN", "Ramsinh Valsinh Katara")]
    [InlineData("રામસિંહ વાલસિંહ કટારા", "hi-IN", "रामसिंह वालसिंह कटारा")]
    [InlineData("रामसिंह वालसिंह कटारा", "en-IN", "Ramsinh Valsinh Katara")]
    [InlineData("रामसिंह वालसिंह कटारा", "gu-IN", "રામસિંહ વાલસિંહ કટારા")]
    public void ScriptTranslator_TranslatesNamesCorrectly(string source, string targetLang, string expected)
    {
        var result = ScriptTranslator.Translate(source, targetLang);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Bhargav", "Bhargav")]
    [InlineData("ભાર્ગવ", "Bhargav")]
    [InlineData("भार्गव", "Bhargav")]
    [InlineData("Panchal", "Panchal")]
    [InlineData("પંચાલ", "Panchal")]
    [InlineData("રામસિંહ વાલસિંહ કટારા", "Ramsinh Valsinh Katara")]
    [InlineData("रामसिंह वालसिंह कटारा", "Ramsinh Valsinh Katara")]
    [InlineData("પલક", "Palak")]
    [InlineData("કમલ", "Kamal")]
    [InlineData("મનન", "Manan")]
    public void ScriptTranslator_ToEnglish_NormalizesToLatin(string source, string expected)
    {
        var result = ScriptTranslator.ToEnglish(source);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Palak", "પલક")]
    [InlineData("palak", "પલક")]
    [InlineData("Paalak", "પાલક")]
    [InlineData("paalak", "પાલક")]
    [InlineData("kamal", "કમલ")]
    [InlineData("manan", "મનન")]
    [InlineData("man", "મન")]
    [InlineData("maan", "માન")]
    [InlineData("pal", "પલ")]
    [InlineData("paal", "પાલ")]
    [InlineData("ka", "ક")]
    [InlineData("kaa", "કા")]
    [InlineData("pa", "પ")]
    [InlineData("paa", "પા")]
    [InlineData("ma", "મ")]
    [InlineData("maa", "મા")]
    [InlineData("Ramesh", "રમેશ")]
    [InlineData("ramesh", "રમેશ")]
    [InlineData("rakesh", "રાકેશ")]
    [InlineData("raam", "રામ")]
    [InlineData("Bhargav", "ભાર્ગવ")]
    [InlineData("bhargav", "ભાર્ગવ")]
    [InlineData("Panchal", "પંચાલ")]
    [InlineData("panchal", "પંચાલ")]
    [InlineData("DhirDhar", "ધીરધાર")]
    [InlineData("dhirdhar", "ધીરધાર")]
    [InlineData("Gujarat", "ગુજરાત")]
    [InlineData("gujarat", "ગુજરાત")]
    [InlineData("Ahmedabad", "અમદાવાદ")]
    [InlineData("ahmedabad", "અમદાવાદ")]
    [InlineData("Bharat", "ભારત")]
    [InlineData("Mahatma", "મહાત્મા")]
    public void ScriptTranslator_PronunciationAwareTransliteration_CorrectlyDistinguishesVowels(string latin, string expectedGujarati)
    {
        var result = ScriptTranslator.ToGujarati(latin);
        Assert.Equal(expectedGujarati, result);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Test parameter formatting")]
    public void LocalizationService_LocalizeText_TranslatesStaticUI_And_PreservesUserText()
    {
        var service = new LocalizationService();

        service.SetLanguage("gu-IN");
        Assert.Equal(service.GetString("Dashboard"), service.GetString("Dashboard"));
        Assert.False(string.IsNullOrWhiteSpace(service.GetString("Dashboard")));
        Assert.Equal("Bhargav", service.LocalizeText("Bhargav"));
        Assert.Equal("ભાર્ગવ", ScriptTranslator.Translate("Bhargav", "gu-IN"));

        service.SetLanguage("hi-IN");
        Assert.Equal(service.GetString("Dashboard"), service.GetString("Dashboard"));
        Assert.False(string.IsNullOrWhiteSpace(service.GetString("Dashboard")));
        Assert.Equal("Bhargav", service.LocalizeText("Bhargav"));
        Assert.Equal("भार्गव", ScriptTranslator.Translate("Bhargav", "hi-IN"));

        service.SetLanguage("en-IN");
        Assert.Equal("Dashboard", service.GetString("Dashboard"));
        Assert.Equal("Bhargav", service.LocalizeText("Bhargav"));
    }

    [Fact]
    public void LocalizationService_InterestPage_TranslatesAllLabels_IntoGujarati()
    {
        var service = new LocalizationService();
        service.SetLanguage("gu-IN");

        Assert.Equal("વ્યાજ", service.GetString("Interest"));
        Assert.Equal("ખાતેદાર શોધો", service.GetString("SearchBorrower"));
        Assert.Equal("ગણતરી તારીખ", service.GetString("CalculationDate"));
        Assert.Equal("ગણતરી કરો", service.GetString("Calculate"));
        Assert.Equal("વર્તમાન બાકી રકમ", service.GetString("CurrentBalance"));
        Assert.Equal("કુલ વ્યાજ", service.GetString("TotalInterest"));
        Assert.Equal("કુલ બાકી રકમ", service.GetString("TotalOutstanding"));
        Assert.Equal("ગણતરી વિભાગો", service.GetString("CalculationSegments"));
        Assert.Equal("શરૂઆત", service.GetString("Start"));
        Assert.Equal("અંત", service.GetString("End"));
        Assert.Equal("મૂળ રકમ", service.GetString("Principal"));
        Assert.Equal("દર %", service.GetString("RatePercent"));
        Assert.Equal("દર %", service.GetString("Rate %"));
        Assert.Equal("દિવસો", service.GetString("Days"));
        Assert.Equal("દિવસો/મહિનો", service.GetString("DaysPerMonth"));
        Assert.Equal("દિવસો/મહિનો", service.GetString("Days/Month"));
        Assert.Equal("વ્યવહાર", service.GetString("Transaction"));
        Assert.Equal("રકમ", service.GetString("Amount"));
        Assert.Equal("અંતિમ બાકી રકમ", service.GetString("Closing"));
        Assert.Equal("ઉપાડ", service.GetString("Withdrawal"));
        Assert.Equal("જમા", service.GetString("Deposit"));
        Assert.Equal("પ્રારંભિક લોન રકમ", service.GetString("Initial Loan Amount"));
        Assert.Equal("પ્રારંભિક લોન રકમ", service.GetString("InitialLoanAmount"));
    }

    [Fact]
    public void LocalizationService_LocalizeText_DynamicallyTranslatesInterestDescriptions()
    {
        var service = new LocalizationService();

        // Test Gujarati
        service.SetLanguage("gu-IN");
        Assert.Equal("22 મે થી 22 મે સુધીનું વ્યાજ", service.LocalizeText("Interest for 22-May to 22-May"));
        Assert.Equal("22 મે થી 31 મે સુધીનું વ્યાજ", service.LocalizeText("Interest for 22-May to 31-May"));
        Assert.Equal("31 મે થી 30 જૂન સુધીનું વ્યાજ", service.LocalizeText("Interest for 31-May to 30-Jun"));
        Assert.Equal("30 જૂન થી 31 જુલાઈ સુધીનું વ્યાજ", service.LocalizeText("Interest for 30-Jun to 31-Jul"));
        Assert.Equal("31 જુલાઈ થી 31 ઓગસ્ટ સુધીનું વ્યાજ", service.LocalizeText("Interest for 31-Jul to 31-Aug"));
        Assert.Equal("31 ઓગસ્ટ થી 30 સપ્ટેમ્બર સુધીનું વ્યાજ", service.LocalizeText("Interest for 31-Aug to 30-Sep"));
        Assert.Equal("30 સપ્ટેમ્બર થી 31 ઓક્ટોબર સુધીનું વ્યાજ", service.LocalizeText("Interest for 30-Sep to 31-Oct"));
        Assert.Equal("31 ઓક્ટોબર થી 30 નવેમ્બર સુધીનું વ્યાજ", service.LocalizeText("Interest for 31-Oct to 30-Nov"));
        Assert.Equal("30 નવેમ્બર થી 31 ડિસેમ્બર સુધીનું વ્યાજ", service.LocalizeText("Interest for 30-Nov to 31-Dec"));
        Assert.Equal("31 ડિસેમ્બર થી 31 જાન્યુઆરી સુધીનું વ્યાજ", service.LocalizeText("Interest for 31-Dec to 31-Jan"));
        Assert.Equal("31 જાન્યુઆરી થી 28 ફેબ્રુઆરી સુધીનું વ્યાજ", service.LocalizeText("Interest for 31-Jan to 28-Feb"));
        Assert.Equal("28 ફેબ્રુઆરી થી 31 માર્ચ સુધીનું વ્યાજ", service.LocalizeText("Interest for 28-Feb to 31-Mar"));
        Assert.Equal("31 માર્ચ થી 30 એપ્રિલ સુધીનું વ્યાજ", service.LocalizeText("Interest for 31-Mar to 30-Apr"));

        // Test healing from legacy / transliterated formats
        Assert.Equal("22 મે થી 22 મે સુધીનું વ્યાજ", service.LocalizeText("ઈન્ટરેસ્ટ ફોર 22-May થી 22-May"));
        Assert.Equal("22 મે થી 31 મે સુધીનું વ્યાજ", service.LocalizeText("ઈન્ટરેસ્ટ ફોર 22-May થી 31-May"));
        Assert.Equal("31 મે થી 30 જૂન સુધીનું વ્યાજ", service.LocalizeText("ઈન્ટરેસ્ટ ફોર 31-May થી 30-Jun"));
        Assert.Equal("30 જૂન થી 31 જુલાઈ સુધીનું વ્યાજ", service.LocalizeText("ઈન્ટરેસ્ટ ફોર 30-Jun થી 31-Jul"));
        Assert.Equal("31 જુલાઈ થી 31 ઓગસ્ટ સુધીનું વ્યાજ", service.LocalizeText("ઈન્ટરેસ્ટ ફોર 31-Jul થી 31-Aug"));

        Assert.Equal("ઉપાડ", service.LocalizeText("Withdrawal"));
        Assert.Equal("જમા", service.LocalizeText("Deposit"));
        Assert.Equal("પ્રારંભિક લોન રકમ", service.LocalizeText("Initial Loan Amount"));
        Assert.Equal("જમા વ્યવહાર", service.LocalizeText("Deposit transaction"));
        Assert.Equal("ઉપાડ વ્યવહાર", service.LocalizeText("Withdrawal transaction"));

        // Test FormatInterestDescription
        Assert.Equal("22 મે થી 31 મે સુધીનું વ્યાજ", service.FormatInterestDescription(new DateTime(2023, 5, 22), new DateTime(2023, 5, 31)));
        Assert.Equal("31 મે થી 30 જૂન સુધીનું વ્યાજ", service.FormatInterestDescription(new DateTime(2023, 5, 31), new DateTime(2023, 6, 30)));
        Assert.Equal("30 જૂન થી 31 જુલાઈ સુધીનું વ્યાજ", service.FormatInterestDescription(new DateTime(2023, 6, 30), new DateTime(2023, 7, 31)));
        Assert.Equal("31 જુલાઈ થી 31 ઓગસ્ટ સુધીનું વ્યાજ", service.FormatInterestDescription(new DateTime(2023, 7, 31), new DateTime(2023, 8, 31)));
        Assert.Equal("31 ઓગસ્ટ થી 30 સપ્ટેમ્બર સુધીનું વ્યાજ", service.FormatInterestDescription(new DateTime(2023, 8, 31), new DateTime(2023, 9, 30)));
        Assert.Equal("30 સપ્ટેમ્બર થી 31 ઓક્ટોબર સુધીનું વ્યાજ", service.FormatInterestDescription(new DateTime(2023, 9, 30), new DateTime(2023, 10, 31)));
        Assert.Equal("31 ઓક્ટોબર થી 30 નવેમ્બર સુધીનું વ્યાજ", service.FormatInterestDescription(new DateTime(2023, 10, 31), new DateTime(2023, 11, 30)));
        Assert.Equal("30 નવેમ્બર થી 31 ડિસેમ્બર સુધીનું વ્યાજ", service.FormatInterestDescription(new DateTime(2023, 11, 30), new DateTime(2023, 12, 31)));
        Assert.Equal("31 ડિસેમ્બર થી 31 જાન્યુઆરી સુધીનું વ્યાજ", service.FormatInterestDescription(new DateTime(2023, 12, 31), new DateTime(2024, 1, 31)));
        Assert.Equal("31 જાન્યુઆરી થી 28 ફેબ્રુઆરી સુધીનું વ્યાજ", service.FormatInterestDescription(new DateTime(2024, 1, 31), new DateTime(2024, 2, 28)));
        Assert.Equal("28 ફેબ્રુઆરી થી 31 માર્ચ સુધીનું વ્યાજ", service.FormatInterestDescription(new DateTime(2024, 2, 28), new DateTime(2024, 3, 31)));
        Assert.Equal("31 માર્ચ થી 30 એપ્રિલ સુધીનું વ્યાજ", service.FormatInterestDescription(new DateTime(2024, 3, 31), new DateTime(2024, 4, 30)));

        // Test English (Preserved)
        service.SetLanguage("en-IN");
        Assert.Equal("Interest for 22-May to 31-May", service.LocalizeText("Interest for 22-May to 31-May"));
        Assert.Equal("Interest for 22-May to 31-May", service.LocalizeText("22 મે થી 31 મે સુધીનું વ્યાજ"));
        Assert.Equal("Interest for 22-May to 31-May", service.LocalizeText("ઈન્ટરેસ્ટ ફોર 22-May થી 31-May"));
        Assert.Equal("Interest for 22-May to 31-May", service.FormatInterestDescription(new DateTime(2023, 5, 22), new DateTime(2023, 5, 31)));
        Assert.Equal("Withdrawal", service.LocalizeText("Withdrawal"));
        Assert.Equal("Deposit", service.LocalizeText("Deposit"));
        Assert.Equal("Initial Loan Amount", service.LocalizeText("Initial Loan Amount"));
        Assert.Equal("Deposit transaction", service.LocalizeText("Deposit transaction"));
        Assert.Equal("Interest", service.GetString("Interest"));
        Assert.Equal("Current Balance", service.GetString("CurrentBalance"));
        Assert.Equal("Closing", service.GetString("Closing"));
        Assert.Equal("Search Borrower", service.GetString("SearchBorrower"));

        // Test ScriptTranslator directly
        Assert.Equal("22 મે થી 31 મે સુધીનું વ્યાજ", ScriptTranslator.Translate("Interest for 22-May to 31-May", "gu-IN"));
        Assert.Equal("Interest for 22-May to 31-May", ScriptTranslator.Translate("22 મે થી 31 મે સુધીનું વ્યાજ", "en-IN"));
        Assert.Equal("22 મે થી 31 મે સુધીનું વ્યાજ", ScriptTranslator.ToGujarati("Interest for 22-May to 31-May"));
        Assert.Equal("Interest for 22-May to 31-May", ScriptTranslator.ToEnglish("22 મે થી 31 મે સુધીનું વ્યાજ"));
    }
}
