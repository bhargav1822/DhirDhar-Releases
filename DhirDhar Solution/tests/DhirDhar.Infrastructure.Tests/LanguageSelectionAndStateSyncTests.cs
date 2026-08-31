using System;
using System.Collections.Generic;
using DhirDhar.Application.Localization;
using DhirDhar.Infrastructure.Localization;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

[Collection("LocalizationStateSyncTests")]
public sealed class LanguageSelectionAndStateSyncTests
{
    private static readonly (string Code, string EnglishName, string NativeName, string SampleDashboardTitle)[] LanguageData = new[]
    {
        ("en-IN", "English", "English", "Dashboard"),
        ("gu-IN", "Gujarati", "ગુજરાતી", "ડૅશબોર્ડ"),
        ("hi-IN", "Hindi", "हिन्दी", "डैशबोर्ड"),
        ("mr-IN", "Marathi", "मराठी", "डॅशबोर्ड"),
        ("bn-IN", "Bengali", "বাংলা", "ড্যাশবোর্ড"),
        ("pa-IN", "Punjabi", "ਪੰਜਾਬੀ", "ਡੈਸ਼ਬੋਰਡ"),
        ("ta-IN", "Tamil", "தமிழ்", "டாஷ்போர்டு"),
        ("te-IN", "Telugu", "తెలుగు", "డాష్‌బోర్డ్"),
        ("kn-IN", "Kannada", "ಕನ್ನಡ", "ಡ್ಯಾಶ್‌ಬೋರ್ಡ್"),
        ("ml-IN", "Malayalam", "മലയാളം", "ഡാഷ്‌ബോർഡ്"),
        ("or-IN", "Odia", "ଓଡ଼ିଆ", "ଡ୍ୟାସବୋର୍ଡ"),
        ("as-IN", "Assamese", "অসমীয়া", "ডেচবৰ্ড")
    };

    [Theory]
    [InlineData("en-IN", "en-IN")]
    [InlineData("English", "en-IN")]
    [InlineData("en", "en-IN")]
    [InlineData("hi-IN", "hi-IN")]
    [InlineData("Hindi", "hi-IN")]
    [InlineData("hi", "hi-IN")]
    [InlineData("हिन्दी", "hi-IN")]
    [InlineData("gu-IN", "gu-IN")]
    [InlineData("Gujarati", "gu-IN")]
    [InlineData("gu", "gu-IN")]
    [InlineData("ગુજરાતી", "gu-IN")]
    [InlineData("mr-IN", "mr-IN")]
    [InlineData("Marathi", "mr-IN")]
    [InlineData("bn-IN", "bn-IN")]
    [InlineData("Bengali", "bn-IN")]
    [InlineData("pa-IN", "pa-IN")]
    [InlineData("Punjabi", "pa-IN")]
    [InlineData("ta-IN", "ta-IN")]
    [InlineData("Tamil", "ta-IN")]
    [InlineData("te-IN", "te-IN")]
    [InlineData("Telugu", "te-IN")]
    [InlineData("kn-IN", "kn-IN")]
    [InlineData("Kannada", "kn-IN")]
    [InlineData("ml-IN", "ml-IN")]
    [InlineData("Malayalam", "ml-IN")]
    [InlineData("or-IN", "or-IN")]
    [InlineData("Odia", "or-IN")]
    [InlineData("as-IN", "as-IN")]
    [InlineData("Assamese", "as-IN")]
    public void NormalizeLanguageCode_ReturnsFullCanonicalCode(string input, string expectedCanonicalCode)
    {
        var actual = LocalizationService.NormalizeLanguageCode(input);
        Assert.Equal(expectedCanonicalCode, actual);
    }

    [Fact]
    public void LanguageTransitions_English_Hindi_English_ImmediatelyUpdatesAndRaisesEvent()
    {
        var service = new LocalizationService();
        int eventCount = 0;
        service.LanguageChanged += (s, e) => eventCount++;

        // Start in English
        service.SetLanguage("en-IN");
        Assert.Equal("en-IN", service.CurrentLanguage);
        Assert.Equal("Dashboard", service.GetString("Dashboard"));
        Assert.Equal("Borrowers", service.GetString("Borrowers"));
        Assert.Equal("Transactions", service.GetString("Transactions"));

        // Switch to Hindi
        service.SetLanguage("hi-IN");
        Assert.Equal("hi-IN", service.CurrentLanguage);
        Assert.Equal("डैशबोर्ड", service.GetString("Dashboard"));
        Assert.Equal("खाताधारक", service.GetString("Borrowers"));
        Assert.Equal("लेन-देन", service.GetString("Transactions"));
        Assert.True(eventCount >= 1);

        // Switch back to English
        service.SetLanguage("en-IN");
        Assert.Equal("en-IN", service.CurrentLanguage);
        Assert.Equal("Dashboard", service.GetString("Dashboard"));
        Assert.Equal("Borrowers", service.GetString("Borrowers"));
        Assert.Equal("Transactions", service.GetString("Transactions"));
    }

    [Fact]
    public void LanguageTransitions_English_Gujarati_English_ImmediatelyUpdates()
    {
        var service = new LocalizationService();

        service.SetLanguage("en-IN");
        Assert.Equal("en-IN", service.CurrentLanguage);
        Assert.Equal("Dashboard", service.GetString("Dashboard"));

        service.SetLanguage("gu-IN");
        Assert.Equal("gu-IN", service.CurrentLanguage);
        Assert.Equal("ડૅશબોર્ડ", service.GetString("Dashboard"));
        Assert.Equal("ખાતાધારકો", service.GetString("Borrowers"));

        service.SetLanguage("en-IN");
        Assert.Equal("en-IN", service.CurrentLanguage);
        Assert.Equal("Dashboard", service.GetString("Dashboard"));
        Assert.Equal("Borrowers", service.GetString("Borrowers"));
    }

    [Fact]
    public void LanguageTransitions_Hindi_Gujarati_Hindi_ImmediatelyUpdates()
    {
        var service = new LocalizationService();

        service.SetLanguage("hi-IN");
        Assert.Equal("hi-IN", service.CurrentLanguage);
        Assert.Equal("डैशबोर्ड", service.GetString("Dashboard"));

        service.SetLanguage("gu-IN");
        Assert.Equal("gu-IN", service.CurrentLanguage);
        Assert.Equal("ડૅશબોર્ડ", service.GetString("Dashboard"));

        service.SetLanguage("hi-IN");
        Assert.Equal("hi-IN", service.CurrentLanguage);
        Assert.Equal("डैशबोर्ड", service.GetString("Dashboard"));
    }

    [Fact]
    public void All12Languages_ImmediatelySwitch_AndReturnDistinctDashboardHeaders()
    {
        var service = new LocalizationService();

        foreach (var lang in LanguageData)
        {
            service.SetLanguage(lang.Code);
            Assert.Equal(lang.Code, service.CurrentLanguage);
            var title = service.GetString("Dashboard");
            Assert.Equal(lang.SampleDashboardTitle, title);
        }
    }

    [Fact]
    public void UserDataImmutability_BorrowerDataAndCustomNotesNeverAltered()
    {
        var service = new LocalizationService();
        var rawNames = new[] { "Ramesh Patel", "Hareshbhai", "Rajesh Sharma", "Pooja Verma", "Suresh Kumar" };
        var rawVillages = new[] { "Ahmedabad", "Surat", "Rajkot", "Vadodara", "Mehsana" };
        var rawNotes = new[] { "Personal loan given for farm expansion", "Initial token amount paid" };

        foreach (var lang in LanguageData)
        {
            service.SetLanguage(lang.Code);

            foreach (var name in rawNames)
            {
                Assert.Equal(name, service.LocalizeText(name));
            }

            foreach (var village in rawVillages)
            {
                Assert.Equal(village, service.LocalizeText(village));
            }

            foreach (var note in rawNotes)
            {
                Assert.Equal(note, service.LocalizeText(note));
            }
        }
    }

    [Fact]
    public void MultiCycleTransitions_NoLooping_AndStateConsistentlySynchronized()
    {
        var service = new LocalizationService();

        for (int cycle = 0; cycle < 10; cycle++)
        {
            service.SetLanguage("en-IN");
            Assert.Equal("en-IN", service.CurrentLanguage);
            Assert.Equal("Dashboard", service.GetString("Dashboard"));
            Assert.Equal("Settings", service.GetString("Settings"));

            service.SetLanguage("hi-IN");
            Assert.Equal("hi-IN", service.CurrentLanguage);
            Assert.Equal("डैशबोर्ड", service.GetString("Dashboard"));
            Assert.Equal("सेटिंग्स", service.GetString("Settings"));

            service.SetLanguage("gu-IN");
            Assert.Equal("gu-IN", service.CurrentLanguage);
            Assert.Equal("ડૅશબોર્ડ", service.GetString("Dashboard"));
            Assert.Equal("સેટિંગ્સ", service.GetString("Settings"));

            service.SetLanguage("mr-IN");
            Assert.Equal("mr-IN", service.CurrentLanguage);
            Assert.Equal("डॅशबोर्ड", service.GetString("Dashboard"));

            service.SetLanguage("bn-IN");
            Assert.Equal("bn-IN", service.CurrentLanguage);
            Assert.Equal("ড্যাশবোর্ড", service.GetString("Dashboard"));

            service.SetLanguage("ta-IN");
            Assert.Equal("ta-IN", service.CurrentLanguage);
            Assert.Equal("டாஷ்போர்டு", service.GetString("Dashboard"));

            service.SetLanguage("te-IN");
            Assert.Equal("te-IN", service.CurrentLanguage);
            Assert.Equal("డాష్‌బోర్డ్", service.GetString("Dashboard"));
        }
    }

    [Fact]
    public void EqualityGuard_SettingSameLanguage_DoesNotTriggerUnnecessaryEvents()
    {
        var service = new LocalizationService();
        service.SetLanguage("en-IN");

        int eventCount = 0;
        service.LanguageChanged += (s, e) => eventCount++;

        // Setting same language should not raise new event
        service.SetLanguage("en-IN");
        service.SetLanguage("en-IN");
        service.SetLanguage("en");
        service.SetLanguage("English");

        Assert.Equal(0, eventCount);

        // Setting different language should raise exactly one event
        service.SetLanguage("hi-IN");
        Assert.Equal(1, eventCount);

        // Setting same Hindi language repeatedly should not raise more events
        service.SetLanguage("hi-IN");
        service.SetLanguage("Hindi");
        service.SetLanguage("हिन्दी");
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void SingleAuthoritativeLanguageState_ControlsBothUiAndTypingSimultaneously()
    {
        var service = new LocalizationService();
        int globalEventCount = 0;
        EventHandler handler = (s, e) => globalEventCount++;
        LocalizationService.GlobalLanguageChanged += handler;

        try
        {
            // 1. Set English
            service.SetLanguage("en-IN");
            Assert.Equal("en-IN", service.CurrentLanguage);
            Assert.Equal("en-IN", LocalizationService.GlobalCurrentLanguage);
            Assert.Equal("Dashboard", service.GetString("Dashboard"));
            // Typing in English
            string englishTyping = "Bhargav";
            Assert.Equal("Bhargav", englishTyping);

            // 2. Set Gujarati
            service.SetLanguage("gu-IN");
            Assert.Equal("gu-IN", service.CurrentLanguage);
            Assert.Equal("gu-IN", LocalizationService.GlobalCurrentLanguage);
            Assert.Equal("ડૅશબોર્ડ", service.GetString("Dashboard"));
            // Typing in Gujarati
            string gujaratiTyping = GujaratiPhoneticEngine.Transliterate("Chetan");
            Assert.Equal("ચેતન", gujaratiTyping);
            string gujaratiDigits = ScriptTranslator.ConvertDigitsToIndic("1234567890", "gu");
            Assert.Equal("૧૨૩૪૫૬૭૮૯૦", gujaratiDigits);

            // 3. Switch back to English (e.g. from same focused field)
            service.SetLanguage("en-IN");
            Assert.Equal("en-IN", service.CurrentLanguage);
            Assert.Equal("en-IN", LocalizationService.GlobalCurrentLanguage);
            Assert.Equal("Dashboard", service.GetString("Dashboard"));
            string englishDigits = ScriptTranslator.ConvertDigitsToIndic("1234567890", "en");
            Assert.Equal("1234567890", englishDigits);

            // 4. Switch to Hindi
            service.SetLanguage("hi-IN");
            Assert.Equal("hi-IN", service.CurrentLanguage);
            Assert.Equal("hi-IN", LocalizationService.GlobalCurrentLanguage);
            Assert.Equal("डैशबोर्ड", service.GetString("Dashboard"));
            string hindiTyping = ScriptTranslator.Translate("Chetan", "hi");
            Assert.Equal("चेतन", hindiTyping);
            string hindiDigits = ScriptTranslator.ConvertDigitsToIndic("1234567890", "hi");
            Assert.Equal("१२३४५६७८९०", hindiDigits);

            Assert.True(globalEventCount >= 3);
        }
        finally
        {
            LocalizationService.GlobalLanguageChanged -= handler;
        }
    }
}

