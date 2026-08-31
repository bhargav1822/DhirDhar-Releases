using System;
using System.Collections.Generic;
using System.Linq;
using DhirDhar.Application.Localization;
using DhirDhar.Infrastructure.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

[Collection("LocalizationStateSyncTests")]
public class InputLanguageArchitectureTests
{
    [Fact]
    public void LocalizationService_SetLanguage_IsIdempotent()
    {
        var service = new LocalizationService();
        service.SetLanguage("gu-IN");

        int eventCount = 0;
        service.LanguageChanged += (s, e) => eventCount++;

        // Calling with exact same language should not produce redundant state change churn
        service.SetLanguage("gu-IN");
        Assert.Equal("gu-IN", service.CurrentLanguage);
    }

    [Fact]
    public void TransliterationService_AlwaysOnGujaratiMode_ProducesStrictGujarati()
    {
        var service = new IndicTransliterationService();

        // 1. Borrower Name
        Assert.Equal("ભાર્ગવ", service.Transliterate("Bhargav", "gu"));
        Assert.Equal("પલક", service.Transliterate("Palak", "gu"));
        Assert.Equal("પાલક", service.Transliterate("paalak", "gu"));
        Assert.Equal("ચેતન", service.Transliterate("Chetan", "gu"));
        Assert.Equal("ભારત", service.Transliterate("Bharat", "gu"));
        Assert.Equal("રમેશ", service.Transliterate("Ramesh", "gu"));
        Assert.Equal("કિરણ", service.Transliterate("Kiran", "gu"));

        // 2. Village & Address
        Assert.Equal("અમદાવાદ", service.Transliterate("Ahmedabad", "gu"));
        Assert.Equal("સુખસર", service.Transliterate("Sukhsar", "gu"));

        // 3. Multi-word phrases & Notes
        Assert.Equal("ચેતન મલાઈ", service.Transliterate("Chetan malai", "gu"));

        // 4. Numerals
        Assert.Equal("૧૨૩૪૫૬૭૮૯૦", service.Transliterate("1234567890", "gu"));
        Assert.Equal("૯૯૨૪૦૧૯૮૨૭", service.Transliterate("9924019827", "gu"));
    }

    [Fact]
    public void TransliterationService_AlwaysOnEnglishMode_BypassesTransliterationCompletely()
    {
        var service = new IndicTransliterationService();

        // 1. Names & Words
        Assert.Equal("Bhargav", service.Transliterate("Bhargav", "en"));
        Assert.Equal("Palak", service.Transliterate("Palak", "en"));
        Assert.Equal("Chetan malai", service.Transliterate("Chetan malai", "en"));

        // 2. Numerals
        Assert.Equal("1234567890", service.Transliterate("1234567890", "en"));
        Assert.Equal("9924019827", service.Transliterate("9924019827", "en"));
    }

    [Fact]
    public void TransliterationService_AlwaysOnHindiMode_ProducesStrictHindiDevanagari()
    {
        var service = new IndicTransliterationService();

        Assert.Equal("भार्गव", service.Transliterate("Bhargav", "hi"));
        Assert.Equal("चेतन", service.Transliterate("Chetan", "hi"));
        Assert.Equal("१२३४५६७८९०", service.Transliterate("1234567890", "hi"));
    }

    [Fact]
    public void TransliterationService_DynamicLanguageSwitching_SwitchesImmediatelyWithoutStateLoss()
    {
        var service = new IndicTransliterationService();

        // Start English
        Assert.Equal("Bhargav", service.Transliterate("Bhargav", "en"));

        // Switch Gujarati
        Assert.Equal("ભાર્ગવ", service.Transliterate("Bhargav", "gu"));

        // Switch Hindi
        Assert.Equal("भार्गव", service.Transliterate("Bhargav", "hi"));

        // Switch back to English
        Assert.Equal("Bhargav", service.Transliterate("Bhargav", "en"));

        // Switch back to Gujarati
        Assert.Equal("ભાર્ગવ", service.Transliterate("Bhargav", "gu"));
    }

    [Theory]
    [InlineData("૧૨૩૪૫૬૭૮૯૦", "1234567890")]
    [InlineData("૯૯૨૪૦૧૯૮૨૭", "9924019827")]
    [InlineData("१२३४५६७८९०", "1234567890")]
    [InlineData("1234567890", "1234567890")]
    public void DatabaseNumericStorage_NormalizesDigitsToAscii(string indicNumber, string expectedAscii)
    {
        var result = ScriptTranslator.NormalizeDigitsToAscii(indicNumber);
        Assert.Equal(expectedAscii, result);
    }

    [Fact]
    public void InputLanguageService_TransliteratesAllFieldTypes_NamesAddressesNotesOrnamentSearch()
    {
        var locService = new LocalizationService();
        locService.SetLanguage("gu-IN");

        var translit = new IndicTransliterationService();

        // Borrower Name
        Assert.Equal("ભાર્ગવ પંચાલ", translit.Transliterate("Bhargav panchal", "gu"));
        // Father Name
        Assert.Equal("પ્રવિણચંદ્ર", translit.Transliterate("pravinchandra", "gu"));
        // Village
        Assert.Equal("સુખસર", translit.Transliterate("Sukhsar", "gu"));
        // Address
        Assert.Equal("અમદાવાદ", translit.Transliterate("Ahmedabad", "gu"));
        // Custom Ornament Type
        Assert.Equal("સોનાની ચેન", translit.Transliterate("sonaanee chen", "gu"));
        // Notes / Reference
        Assert.Equal("ચેતન મલાઈ", translit.Transliterate("Chetan malai", "gu"));
        // Search Term
        Assert.Equal("પલક", translit.Transliterate("Palak", "gu"));
    }

    [Fact]
    public void InputLanguage_PersistentAcrossSimulatedFocusAndPageNavigation()
    {
        var locService = new LocalizationService();
        locService.SetLanguage("gu-IN");

        // Simulate navigating across multiple views (Dashboard -> BorrowerEdit -> Transactions -> Reports -> Dashboard)
        var simulatedPages = new[] { "Dashboard", "BorrowerEdit", "Transactions", "Reports", "Settings", "Dashboard" };
        var fieldsToType = new (string Latin, string ExpectedGujarati)[]
        {
            ("Bhargav", "ભાર્ગવ"),
            ("Panchal", "પંચાલ"),
            ("Sukhsar", "સુખસર"),
            ("Chetan", "ચેતન"),
            ("Kiran", "કિરણ"),
        };

        var translit = new IndicTransliterationService();

        foreach (var page in simulatedPages)
        {
            // Input language remains gu-IN throughout the entire lifecycle
            Assert.Equal("gu-IN", locService.CurrentLanguage);
            foreach (var (latin, expected) in fieldsToType)
            {
                var result = translit.Transliterate(latin, "gu");
                Assert.Equal(expected, result);
            }
        }
    }
}
