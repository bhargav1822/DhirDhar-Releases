using System;
using System.Collections.Generic;
using System.Linq;
using DhirDhar.Application.Localization;
using Xunit;

namespace DhirDhar.Application.Tests;

public class IndicInputAndTransliterationTests
{
    // =========================================================================
    // 1. ENGLISH TYPING
    // =========================================================================
    [Theory]
    [InlineData("Ramesh Patel")]
    [InlineData("Ahmedabad")]
    [InlineData("Main Street 123")]
    [InlineData("Personal Loan Notes")]
    [InlineData("Bhargav 123")]
    public void EnglishSelected_DirectTyping_PreservedAsLatin(string input)
    {
        var result = ScriptTranslator.Translate(input, "en");
        Assert.Equal(input, result);
    }

    // =========================================================================
    // 2. DIRECT GUJARATI UNICODE INPUT (e.g. from Google Indic Input / Windows IME)
    // =========================================================================
    [Theory]
    [InlineData("રમેશ પટેલ")]
    [InlineData("અમદાવાદ")]
    [InlineData("સુખસર")]
    [InlineData("ભાર્ગવ પ્રવિણચંદ્ર પંચાલ")]
    [InlineData("ધીરધાર")]
    [InlineData("મારું નામ ભાર્ગવ છે")]
    public void GujaratiSelected_DirectUnicodeTyping_PreservedAsEntered(string input)
    {
        Assert.True(ScriptTranslator.IsIndicScript(input));
        Assert.True(ScriptTranslator.IsGujaratiScript(input));

        var result = ScriptTranslator.Translate(input, "gu");
        Assert.Equal(input, result);
    }

    // =========================================================================
    // 3. GUJARATI INDEPENDENT VOWELS
    // =========================================================================
    [Theory]
    [InlineData("a", "અ")]
    [InlineData("aa", "આ")]
    [InlineData("ā", "આ")]
    [InlineData("i", "ઇ")]
    [InlineData("ii", "ઈ")]
    [InlineData("ee", "ઈ")]
    [InlineData("ī", "ઈ")]
    [InlineData("u", "ઉ")]
    [InlineData("uu", "ઊ")]
    [InlineData("oo", "ઊ")]
    [InlineData("ū", "ઊ")]
    [InlineData("ru", "ઋ")]
    [InlineData("ri", "ઋ")]
    [InlineData("r̥", "ઋ")]
    [InlineData("e", "એ")]
    [InlineData("ai", "ઐ")]
    [InlineData("o", "ઓ")]
    [InlineData("au", "ઔ")]
    [InlineData("am", "અં")]
    [InlineData("an", "અં")]
    [InlineData("aṃ", "અં")]
    [InlineData("ah", "અઃ")]
    [InlineData("aḥ", "અઃ")]
    [InlineData("ae", "ઍ")]
    public void IndependentVowels_TransliterateAccurately(string input, string expected)
    {
        var result = ScriptTranslator.ToGujarati(input);
        Assert.Equal(expected, result);
    }

    // =========================================================================
    // 4. CONSONANT + VOWEL COMBINATIONS (MATRAS)
    // =========================================================================
    [Theory]
    [InlineData("ka", "ક")]
    [InlineData("kaa", "કા")]
    [InlineData("ki", "કિ")]
    [InlineData("kee", "કી")]
    [InlineData("kii", "કી")]
    [InlineData("ku", "કુ")]
    [InlineData("koo", "કૂ")]
    [InlineData("kuu", "કૂ")]
    [InlineData("kru", "કૃ")]
    [InlineData("kri", "કૃ")]
    [InlineData("ke", "કે")]
    [InlineData("kai", "કૈ")]
    [InlineData("ko", "કો")]
    [InlineData("kau", "કૌ")]
    [InlineData("bha", "ભ")]
    [InlineData("bhaag", "ભાગ")]
    [InlineData("bhargav", "ભાર્ગવ")]
    [InlineData("bhargav panchal", "ભાર્ગવ પંચાલ")]
    public void ConsonantVowelCombinations_TransliterateAccurately(string input, string expected)
    {
        var result = ScriptTranslator.ToGujarati(input);
        Assert.Equal(expected, result);
    }

    // =========================================================================
    // 5. COMPLETE GUJARATI CONSONANT SET & PHONETIC ENGLISH MAPPINGS
    // =========================================================================
    [Theory]
    [InlineData("ka", "ક")]
    [InlineData("kha", "ખ")]
    [InlineData("ga", "ગ")]
    [InlineData("gha", "ઘ")]
    [InlineData("nga", "ઙ")]
    [InlineData("cha", "ચ")]
    [InlineData("chha", "છ")]
    [InlineData("ja", "જ")]
    [InlineData("jha", "ઝ")]
    [InlineData("nya", "ઞ")]
    [InlineData("ta", "ત")]
    [InlineData("tha", "થ")]
    [InlineData("da", "દ")]
    [InlineData("dha", "ધ")]
    [InlineData("na", "ન")]
    [InlineData("pa", "પ")]
    [InlineData("pha", "ફ")]
    [InlineData("ba", "બ")]
    [InlineData("bha", "ભ")]
    [InlineData("ma", "મ")]
    [InlineData("ya", "ય")]
    [InlineData("ra", "ર")]
    [InlineData("la", "લ")]
    [InlineData("va", "વ")]
    [InlineData("sha", "શ")]
    [InlineData("shha", "ષ")]
    [InlineData("sa", "સ")]
    [InlineData("ha", "હ")]
    [InlineData("lla", "ળ")]
    [InlineData("ksha", "ક્ષ")]
    [InlineData("gnya", "જ્ઞ")]
    public void CompleteGujaratiConsonants_TransliterateAccurately(string input, string expected)
    {
        var result = ScriptTranslator.ToGujarati(input);
        Assert.Equal(expected, result);
    }

    // =========================================================================
    // 4. VOWEL COMBINATIONS & MATRAS
    // =========================================================================
    [Theory]
    [InlineData("ka", "ક")]
    [InlineData("kaa", "કા")]
    [InlineData("ki", "કિ")]
    [InlineData("kee", "કી")]
    [InlineData("kii", "કી")]
    [InlineData("ku", "કુ")]
    [InlineData("koo", "કૂ")]
    [InlineData("kuu", "કૂ")]
    [InlineData("ke", "કે")]
    [InlineData("kai", "કૈ")]
    [InlineData("ko", "કો")]
    [InlineData("kau", "કૌ")]
    [InlineData("bha", "ભ")]
    [InlineData("bhaag", "ભાગ")]
    [InlineData("bhargav", "ભાર્ગવ")]
    [InlineData("bhargav panchal", "ભાર્ગવ પંચાલ")]
    public void VowelCombinationsAndMatras_TransliterateAccurately(string input, string expected)
    {
        var result = ScriptTranslator.ToGujarati(input);
        Assert.Equal(expected, result);
    }

    // =========================================================================
    // 5. SPECIAL CONJUNCTS & FORMULAS
    // =========================================================================
    [Theory]
    [InlineData("k + sha", "ક્ષ")]
    [InlineData("j + nya", "જ્ઞ")]
    [InlineData("k + ta", "ક્ત")]
    [InlineData("k + ra", "ક્ર")]
    [InlineData("p + ra", "પ્ર")]
    [InlineData("t + ra", "ત્ર")]
    [InlineData("sh + ra", "શ્ર")]
    [InlineData("bh + ra", "ભ્ર")]
    [InlineData("dh + ya", "ધ્ય")]
    public void SpecialConjunctsAndFormulas_TransliterateAccurately(string input, string expected)
    {
        var result = ScriptTranslator.ToGujarati(input);
        Assert.Equal(expected, result);
    }

    // =========================================================================
    // 6. REAL GUJARATI NAMES & WORDS
    // =========================================================================
    [Theory]
    [InlineData("Bhargav", "ભાર્ગવ")]
    [InlineData("bhargav", "ભાર્ગવ")]
    [InlineData("Panchal", "પંચાલ")]
    [InlineData("DhirDhar", "ધીરધાર")]
    [InlineData("Ahmedabad", "અમદાવાદ")]
    [InlineData("Gujarat", "ગુજરાત")]
    [InlineData("Dwiti", "દ્વિતી")]
    [InlineData("Jignesh", "જિગ્નેશ")]
    [InlineData("Lakshmi", "લક્ષ્મી")]
    [InlineData("lakshmi", "લક્ષ્મી")]
    [InlineData("Kshatriya", "ક્ષત્રિય")]
    [InlineData("palak", "પલક")]
    [InlineData("Palak", "પલક")]
    [InlineData("paalak", "પાલક")]
    [InlineData("Paalak", "પાલક")]
    [InlineData("Valsing", "વાલસિંગ")]
    [InlineData("valsing", "વાલસિંગ")]
    [InlineData("valsang", "વાલસંગ")]
    [InlineData("sing", "સિંગ")]
    [InlineData("sang", "સંગ")]
    [InlineData("mangal", "મંગલ")]
    [InlineData("rang", "રંગ")]
    [InlineData("kiran", "કિરણ")]
    [InlineData("Kiran", "કિરણ")]
    [InlineData("ram", "રામ")]
    [InlineData("Ram", "રામ")]
    [InlineData("ghar", "ઘર")]
    [InlineData("Ghar", "ઘર")]
    [InlineData("dharm", "ધર્મ")]
    [InlineData("Dharm", "ધર્મ")]
    [InlineData("shakti", "શક્તિ")]
    [InlineData("Shakti", "શક્તિ")]
    [InlineData("kshama", "ક્ષમા")]
    [InlineData("Kshama", "ક્ષમા")]
    [InlineData("gnan", "જ્ઞાન")]
    [InlineData("Gnan", "જ્ઞાન")]
    public void RealGujaratiNamesAndWords_TransliterateAccurately(string input, string expected)
    {
        var result = ScriptTranslator.ToGujarati(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("kra", "ક્ર")]
    [InlineData("pra", "પ્ર")]
    [InlineData("tra", "ત્ર")]
    [InlineData("shra", "શ્ર")]
    [InlineData("bhra", "ભ્ર")]
    [InlineData("dhya", "ધ્ય")]
    [InlineData("kta", "ક્ત")]
    public void CommonConjuncts_TransliterateAccurately(string input, string expected)
    {
        var result = ScriptTranslator.ToGujarati(input);
        Assert.Equal(expected, result);
    }

    // =========================================================================
    // 7. NUMERAL LOCALIZATION & PRESERVATION
    // =========================================================================
    [Theory]
    [InlineData("Bhargav 123", "ભાર્ગવ ૧૨૩")]
    [InlineData("bhargav 123", "ભાર્ગવ ૧૨૩")]
    public void NumeralLocalization_ProducesGujaratiNumeralsInText(string input, string expected)
    {
        var result = ScriptTranslator.ToGujarati(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void LocalizedNumerals_NormalizeToAsciiForDatabaseStorage()
    {
        Assert.Equal("123", ScriptTranslator.NormalizeDigitsToAscii("૧૨૩"));
        Assert.Equal("0123456789", ScriptTranslator.NormalizeDigitsToAscii("૦૧૨૩૪૫૬૭૮૯"));
        Assert.Equal("Bhargav 123", ScriptTranslator.NormalizeDigitsToAscii("Bhargav ૧૨૩"));
    }

    // =========================================================================
    // 4. GUJARATI PHONETIC TRANSLITERATION OF COMMON NAMES & PLACES
    // =========================================================================
    [Theory]
    [InlineData("Ramesh", "રમેશ")]
    [InlineData("Suresh", "સુરેશ")]
    [InlineData("Mahesh", "મહેશ")]
    [InlineData("Rajesh", "રાજેશ")]
    [InlineData("Pravin", "પ્રવિણ")]
    [InlineData("Sukhsar", "સુખસર")]
    [InlineData("Patan", "પાટણ")]
    [InlineData("Gujarat", "ગુજરાત")]
    [InlineData("Patel", "પટેલ")]
    [InlineData("Shah", "શાહ")]
    [InlineData("Katara", "કટારા")]
    [InlineData("Vadodara", "વડોદરા")]
    [InlineData("Surat", "સુરત")]
    [InlineData("Rajkot", "રાજકોટ")]
    [InlineData("Gandhinagar", "ગાંધીનગર")]
    [InlineData("Dahod", "દાહોદ")]
    [InlineData("Godhra", "ગોધરા")]
    public void GujaratiPhoneticTransliteration_ProducesAccurateGujaratiText(string englishInput, string expectedGujarati)
    {
        var result = ScriptTranslator.ToGujarati(englishInput);
        Assert.Equal(expectedGujarati, result);
    }

    // =========================================================================
    // 5. EXISTING STORED GUJARATI TEXT REMAINS UNCHANGED
    // =========================================================================
    [Fact]
    public void ExistingStoredGujaratiText_RemainsUnchanged()
    {
        var original = "રમેશભાઈ પટેલ, સુખસર, દાહોદ";
        var detectedLang = ScriptTranslator.DetectLanguage(original);
        Assert.Equal("gu", detectedLang);

        var translated = ScriptTranslator.Translate(original, "gu");
        Assert.Equal(original, translated);
    }

    // =========================================================================
    // 6. NUMERIC AND TECHNICAL FIELDS ARE PRESERVED WITHOUT MODIFICATION
    // =========================================================================
    [Theory]
    [InlineData("9876543210")]
    [InlineData("123456789012")]
    [InlineData("50000.00")]
    [InlineData("3.00")]
    [InlineData("21/08/2026")]
    public void NumericAndDateFields_PreservedInEnglish_And_LocalizedInGujarati(string numericText)
    {
        // 1. In English mode, raw numbers are strictly preserved without modification
        var enResult = ScriptTranslator.Translate(numericText, "en");
        Assert.Equal(numericText, enResult);

        // 2. In Gujarati mode, digits are converted to Gujarati numerals
        var guResult = ScriptTranslator.ToGujarati(numericText);
        Assert.NotEqual(string.Empty, guResult);

        // 3. Database ASCII normalization recovers original numeric representation
        var normalizedAscii = ScriptTranslator.NormalizeDigitsToAscii(guResult);
        Assert.Equal(numericText, normalizedAscii);
    }

    // =========================================================================
    // 7. COMPOUND NAMES AND MULTI-WORD TRANSLITERATION
    // =========================================================================
    [Theory]
    [InlineData("Ramesh Patel", "રમેશ પટેલ")]
    [InlineData("Bhargav Pravinchandra Panchal", "ભાર્ગવ પ્રવિણચંદ્ર પંચાલ")]
    [InlineData("Ramsinh Valsinh Katara", "રામસિંહ વાલસિંહ કટારા")]
    public void CompoundNames_TransliterateAccurately(string latinName, string expectedGujaratiName)
    {
        var result = ScriptTranslator.ToGujarati(latinName);
        Assert.Equal(expectedGujaratiName, result);
    }

    // =========================================================================
    // 8. CONTINUOUS KEYSTROKE SIMULATION (Live Typing Sequence)
    // =========================================================================
    [Fact]
    public void ContinuousTyping_Bhargav_ProducesExpectedProgression()
    {
        // Simulate typing keystroke-by-keystroke
        var steps = new (string input, string expected)[]
        {
            ("b", "બ"),
            ("bh", "ભ"),
            ("bha", "ભ"),
            ("bhar", "ભર"),
            ("bharg", "ભરગ"),
            ("bharga", "ભરગ"),
            ("bhargav", "ભાર્ગવ")
        };

        foreach (var (input, expected) in steps)
        {
            var transliterated = ScriptTranslator.ToGujarati(input);
            Assert.Equal(expected, transliterated);
        }
    }

    [Fact]
    public void ContinuousTyping_Namaste_ProducesExpectedProgression()
    {
        var steps = new (string input, string expected)[]
        {
            ("n", "ન"),
            ("na", "ન"),
            ("nam", "નામ"),
            ("nama", "નમ"),
            ("namas", "નમસ"),
            ("namast", "નમસ્ત"),
            ("namaste", "નમસ્તે")
        };

        foreach (var (input, expected) in steps)
        {
            var transliterated = ScriptTranslator.ToGujarati(input);
            Assert.Equal(expected, transliterated);
        }
    }

    [Fact]
    public void BackspaceReduction_RemovesSyllablesCleanly()
    {
        // Simulate typing "bhargav" then hitting Backspace repeatedly
        string buffer = "bhargav";
        Assert.Equal("ભાર્ગવ", ScriptTranslator.ToGujarati(buffer));

        buffer = buffer[..^1]; // "bharga"
        Assert.Equal("ભરગ", ScriptTranslator.ToGujarati(buffer));

        buffer = buffer[..^1]; // "bharg"
        Assert.Equal("ભરગ", ScriptTranslator.ToGujarati(buffer));

        buffer = buffer[..^1]; // "bhar"
        Assert.Equal("ભર", ScriptTranslator.ToGujarati(buffer));

        buffer = buffer[..^1]; // "bha"
        Assert.Equal("ભ", ScriptTranslator.ToGujarati(buffer));

        buffer = buffer[..^1]; // "bh"
        Assert.Equal("ભ", ScriptTranslator.ToGujarati(buffer));

        buffer = buffer[..^1]; // "b"
        Assert.Equal("બ", ScriptTranslator.ToGujarati(buffer));
    }

    // =========================================================================
    // 9. IINDIC TRANSLITERATION SERVICE TESTS
    // =========================================================================
    [Fact]
    public void IndicTransliterationService_TransliteratesAccurately()
    {
        IIndicTransliterationService service = new IndicTransliterationService();

        Assert.Equal("ભાર્ગવ", service.Transliterate("Bhargav", "gu"));
        Assert.Equal("નમસ્તે", service.Transliterate("namaste", "gu"));
        Assert.Equal("મારું નામ ભાર્ગવ છે", service.Transliterate("maru naam Bhargav chhe", "gu"));
        Assert.Equal("Bhargav", service.Transliterate("Bhargav", "en"));
        Assert.True(service.ShouldTransliterate("Bhargav", "gu"));
        Assert.False(service.ShouldTransliterate("ભાર્ગવ", "gu"));
        Assert.False(service.ShouldTransliterate("12345", "gu"));
        Assert.False(service.ShouldTransliterate("Bhargav", "en"));
    }

    // =========================================================================
    // 10. MULTI-LANGUAGE SCRIPT CONVERSION (Brahmic offset preservation)
    // =========================================================================
    [Fact]
    public void MultiLanguageConversion_TranslatesGujaratiToHindiAccurately()
    {
        var gujarati = "રમેશ";
        var hindi = ScriptTranslator.ToHindi(gujarati);
        Assert.Equal("रमेश", hindi);

        var backToGujarati = ScriptTranslator.ToGujarati(hindi);
        Assert.Equal("રમેશ", backToGujarati);
    }

    // =========================================================================
    // 11. MULTI-WORD CONTINUOUS TYPING & MIXED STRING RESOLUTION
    // =========================================================================
    [Theory]
    [InlineData("Chetan malai", "ચેતન મલાઈ")]
    [InlineData("chetan malai", "ચેતન મલાઈ")]
    [InlineData("ચેતન malai", "ચેતન મલાઈ")]
    [InlineData("Palak", "પલક")]
    [InlineData("palak", "પલક")]
    [InlineData("paalak", "પાલક")]
    [InlineData("Paalak", "પાલક")]
    [InlineData("Bhargav", "ભાર્ગવ")]
    [InlineData("bhargav", "ભાર્ગવ")]
    [InlineData("Chetan", "ચેતન")]
    [InlineData("chetan", "ચેતન")]
    [InlineData("Bhargav Chetan Palak", "ભાર્ગવ ચેતન પલક")]
    public void MultiWordAndMixedString_TransliteratesWithoutEnglishBypass(string input, string expected)
    {
        var result = ScriptTranslator.Translate(input, "gu");
        Assert.Equal(expected, result);

        var toGujResult = ScriptTranslator.ToGujarati(input);
        Assert.Equal(expected, toGujResult);
    }

    // =========================================================================
    // 12. SCRIPT CLASSIFICATION & LATIN LETTER DETECTION
    // =========================================================================
    [Theory]
    [InlineData("ચેતન malai", true, true, false)]
    [InlineData("ચેતન", false, true, true)]
    [InlineData("malai", true, false, false)]
    [InlineData("12345", false, false, false)]
    [InlineData("ચેતન ૧૨૩", false, true, true)]
    public void ScriptClassification_AccuratelyDetectsLatinAndIndic(string text, bool expectedHasLatin, bool expectedHasIndic, bool expectedPureIndic)
    {
        Assert.Equal(expectedHasLatin, ScriptTranslator.ContainsLatinLetters(text));
        Assert.Equal(expectedHasIndic, ScriptTranslator.ContainsIndicScript(text));
        Assert.Equal(expectedPureIndic, ScriptTranslator.IsPureIndicScript(text));
    }

    // =========================================================================
    // 13. ALL 34 GUJARATI CONSONANTS VERIFICATION
    // =========================================================================
    [Theory]
    [InlineData("ka", "ક")]
    [InlineData("kha", "ખ")]
    [InlineData("ga", "ગ")]
    [InlineData("gha", "ઘ")]
    [InlineData("cha", "ચ")]
    [InlineData("chha", "છ")]
    [InlineData("ja", "જ")]
    [InlineData("jha", "ઝ")]
    [InlineData("Ta", "ટ")]
    [InlineData("Tha", "ઠ")]
    [InlineData("Da", "ડ")]
    [InlineData("Dha", "ઢ")]
    [InlineData("Na", "ણ")]
    [InlineData("ta", "ત")]
    [InlineData("tha", "થ")]
    [InlineData("da", "દ")]
    [InlineData("dha", "ધ")]
    [InlineData("na", "ન")]
    [InlineData("pa", "પ")]
    [InlineData("pha", "ફ")]
    [InlineData("ba", "બ")]
    [InlineData("bha", "ભ")]
    [InlineData("ma", "મ")]
    [InlineData("ya", "ય")]
    [InlineData("ra", "ર")]
    [InlineData("la", "લ")]
    [InlineData("va", "વ")]
    [InlineData("sha", "શ")]
    [InlineData("Sha", "ષ")]
    [InlineData("sa", "સ")]
    [InlineData("ha", "હ")]
    [InlineData("La", "ળ")]
    [InlineData("ksha", "ક્ષ")]
    [InlineData("gnya", "જ્ઞ")]
    public void AllGujaratiConsonants_ArePreservedAndAccurate(string input, string expected)
    {
        var result = ScriptTranslator.ToGujarati(input);
        Assert.Equal(expected, result);
    }

    // =========================================================================
    // 14. ALL 14 GUJARATI VOWELS VERIFICATION
    // =========================================================================
    [Theory]
    [InlineData("a", "અ")]
    [InlineData("aa", "આ")]
    [InlineData("i", "ઇ")]
    [InlineData("ee", "ઈ")]
    [InlineData("u", "ઉ")]
    [InlineData("oo", "ઊ")]
    [InlineData("ru", "ઋ")]
    [InlineData("e", "એ")]
    [InlineData("ai", "ઐ")]
    [InlineData("o", "ઓ")]
    [InlineData("au", "ઔ")]
    [InlineData("am", "અં")]
    [InlineData("ah", "અઃ")]
    [InlineData("ae", "ઍ")]
    public void AllGujaratiVowels_ArePreservedAndAccurate(string input, string expected)
    {
        var result = ScriptTranslator.ToGujarati(input);
        Assert.Equal(expected, result);
    }

    // =========================================================================
    // 15. LANGUAGE SELECTION SINGLE SOURCE OF TRUTH
    // =========================================================================
    [Fact]
    public void LanguageSelection_DeterminesTypingBehaviorAccurately()
    {
        IIndicTransliterationService service = new IndicTransliterationService();

        // Gujarati mode
        Assert.Equal("ચેતન મલાઈ", service.Transliterate("Chetan malai", "gu"));
        Assert.Equal("પલક", service.Transliterate("Palak", "gu"));
        Assert.Equal("ભાર્ગવ", service.Transliterate("Bhargav", "gu"));

        // Hindi mode
        Assert.Equal("चेतन", service.Transliterate("Chetan", "hi"));
        Assert.Equal("पलक", service.Transliterate("Palak", "hi"));
        Assert.Equal("भार्गव", service.Transliterate("Bhargav", "hi"));

        // English mode: complete bypass, no transliteration
        Assert.Equal("Chetan malai", service.Transliterate("Chetan malai", "en"));
        Assert.Equal("Palak", service.Transliterate("Palak", "en"));
        Assert.Equal("Bhargav", service.Transliterate("Bhargav", "en"));
    }

    // =========================================================================
    // 16. GUJARATI INDIC INPUT 3 EXACT USER REQUIREMENTS
    // =========================================================================
    [Theory]
    [InlineData("Bhargav", "ભાર્ગવ")]
    [InlineData("Palak", "પલક")]
    [InlineData("palak", "પલક")]
    [InlineData("paalak", "પાલક")]
    [InlineData("Paalak", "પાલક")]
    [InlineData("Chetan", "ચેતન")]
    [InlineData("chetan", "ચેતન")]
    [InlineData("Bharat", "ભારત")]
    [InlineData("bharat", "ભારત")]
    [InlineData("Ramesh", "રમેશ")]
    [InlineData("ramesh", "રમેશ")]
    [InlineData("Kiran", "કિરણ")]
    [InlineData("kiran", "કિરણ")]
    [InlineData("malai", "મલાઈ")]
    [InlineData("9924019827", "૯૯૨૪૦૧૯૮૨૭")]
    public void GujaratiIndicInput3_RequiredWordsAndNumbers_TransliterateCorrectly(string input, string expected)
    {
        var result = ScriptTranslator.ToGujarati(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GujaratiIndicInput3_LanguageSwitching_BehavesCorrectly()
    {
        // 1. Gujarati Mode
        Assert.Equal("ભાર્ગવ", ScriptTranslator.Translate("Bhargav", "gu"));
        Assert.Equal("પલક", ScriptTranslator.Translate("Palak", "gu"));
        Assert.Equal("ચેતન", ScriptTranslator.Translate("Chetan", "gu"));
        Assert.Equal("ભારત", ScriptTranslator.Translate("Bharat", "gu"));
        Assert.Equal("૯૯૨૪૦૧૯૮૨૭", ScriptTranslator.Translate("9924019827", "gu"));

        // 2. Switch to English Mode
        Assert.Equal("Bhargav", ScriptTranslator.Translate("Bhargav", "en"));
        Assert.Equal("Palak", ScriptTranslator.Translate("Palak", "en"));
        Assert.Equal("9924019827", ScriptTranslator.Translate("9924019827", "en"));

        // 3. Switch back to Gujarati Mode
        Assert.Equal("ભાર્ગવ", ScriptTranslator.Translate("Bhargav", "gu"));
        Assert.Equal("પલક", ScriptTranslator.Translate("Palak", "gu"));
        Assert.Equal("ચેતન", ScriptTranslator.Translate("Chetan", "gu"));
        Assert.Equal("ભારત", ScriptTranslator.Translate("Bharat", "gu"));
        Assert.Equal("૯૯૨૪૦૧૯૮૨૭", ScriptTranslator.Translate("9924019827", "gu"));

        // 4. Database ASCII preservation
        Assert.Equal("9924019827", ScriptTranslator.NormalizeDigitsToAscii("૯૯૨૪૦૧૯૮૨૭"));
    }

    [Theory]
    [InlineData("ksha", "ક્ષ")]
    [InlineData("kSha", "ક્ષ")]
    [InlineData("tra", "ત્ર")]
    [InlineData("gna", "જ્ઞ")]
    [InlineData("gya", "જ્ઞ")]
    [InlineData("xa", "ક્ષ")]
    public void GujaratiIndicInput3_Conjuncts_TransliterateCorrectly(string input, string expected)
    {
        Assert.Equal(expected, OfflineGujaratiTransliteration.Transliterate(input));
    }

    // =========================================================================
    // 17. SELECTED-LANGUAGE INPUT MODE - ZERO MIXED INPUT
    // =========================================================================
    [Fact]
    public void SelectedLanguage_GujaratiMode_ProducesStrictlyGujarati()
    {
        IIndicTransliterationService service = new IndicTransliterationService();

        // Names & Words
        Assert.Equal("ભાર્ગવ", service.Transliterate("Bhargav", "gu"));
        Assert.Equal("પલક", service.Transliterate("Palak", "gu"));
        Assert.Equal("પાલક", service.Transliterate("paalak", "gu"));
        Assert.Equal("ચેતન", service.Transliterate("Chetan", "gu"));

        // Numerals
        Assert.Equal("૧૨૩૪૫૬૭૮૯૦", service.Transliterate("1234567890", "gu"));
        Assert.Equal("૯૯૨૪૦૧૯૮૨૭", service.Transliterate("9924019827", "gu"));

        // No mixed English remains in same field
        Assert.Equal("ચેતન મલાઈ", service.Transliterate("ચેતન malai", "gu"));
        Assert.Equal("ચેતન મલાઈ", service.Transliterate("Chetan malai", "gu"));
    }

    [Fact]
    public void SelectedLanguage_EnglishMode_ProducesStrictlyEnglish()
    {
        IIndicTransliterationService service = new IndicTransliterationService();

        // Names & Words (untouched)
        Assert.Equal("Bhargav", service.Transliterate("Bhargav", "en"));
        Assert.Equal("Palak", service.Transliterate("Palak", "en"));
        Assert.Equal("paalak", service.Transliterate("paalak", "en"));
        Assert.Equal("Chetan", service.Transliterate("Chetan", "en"));

        // Numerals (untouched ASCII)
        Assert.Equal("1234567890", service.Transliterate("1234567890", "en"));
        Assert.Equal("9924019827", service.Transliterate("9924019827", "en"));

        // Complete bypass - zero Gujarati transliteration
        Assert.Equal("Chetan malai", service.Transliterate("Chetan malai", "en"));
    }

    [Fact]
    public void SelectedLanguage_HindiMode_ProducesStrictlyHindi()
    {
        IIndicTransliterationService service = new IndicTransliterationService();

        // Names & Words (Hindi Devanagari)
        Assert.Equal("भार्गव", service.Transliterate("Bhargav", "hi"));
        Assert.Equal("पलक", service.Transliterate("Palak", "hi"));
        Assert.Equal("चेतन", service.Transliterate("Chetan", "hi"));

        // Numerals (Hindi Devanagari)
        Assert.Equal("१२३४५६७८९०", service.Transliterate("1234567890", "hi"));
        Assert.Equal("९९२४०१९८२७", service.Transliterate("9924019827", "hi"));

        // Zero Gujarati characters
        var result = service.Transliterate("Chetan", "hi");
        Assert.False(ScriptTranslator.IsGujaratiScript(result));
        Assert.True(ScriptTranslator.IsHindiScript(result));
    }

    [Fact]
    public void SelectedLanguage_DynamicSwitching_SwitchesImmediatelyWithoutRestart()
    {
        // 1. English -> Gujarati
        Assert.Equal("ભાર્ગવ", ScriptTranslator.Translate("Bhargav", "gu"));
        Assert.Equal("૧૨૩૪૫૬૭૮૯૦", ScriptTranslator.Translate("1234567890", "gu"));

        // 2. Gujarati -> English
        Assert.Equal("Bhargav", ScriptTranslator.Translate("Bhargav", "en"));
        Assert.Equal("1234567890", ScriptTranslator.Translate("1234567890", "en"));

        // 3. English -> Hindi
        Assert.Equal("भार्गव", ScriptTranslator.Translate("Bhargav", "hi"));
        Assert.Equal("१२३४५६७८९०", ScriptTranslator.Translate("1234567890", "hi"));

        // 4. Hindi -> Gujarati
        Assert.Equal("ભાર્ગવ", ScriptTranslator.Translate("Bhargav", "gu"));
        Assert.Equal("૧૨૩૪૫૬૭૮૯૦", ScriptTranslator.Translate("1234567890", "gu"));

        // 5. Database canonical ASCII normalization works for all numeral systems
        Assert.Equal("1234567890", ScriptTranslator.NormalizeDigitsToAscii("૧૨૩૪૫૬૭૮૯૦"));
        Assert.Equal("1234567890", ScriptTranslator.NormalizeDigitsToAscii("१२३४५६७८९०"));
    }

    [Theory]
    [InlineData("1234567890", "gu", "૧૨૩૪૫૬૭૮૯૦")]
    [InlineData("1234567890", "hi", "१२३४५६७८९०")]
    [InlineData("1234567890", "mr", "१२३४५६७८९०")]
    [InlineData("1234567890", "bn", "১২৩৪৫৬৭৮৯০")]
    [InlineData("1234567890", "pa", "੧੨੩੪੫੬੭੮੯੦")]
    [InlineData("1234567890", "te", "౧౨౩౪౫౬౭౮౯౦")]
    [InlineData("1234567890", "kn", "೧೨೩೪೫೬೭೮೯೦")]
    [InlineData("1234567890", "ml", "൧൨൩൪൫൬൭൮൯൦")]
    [InlineData("1234567890", "or", "୧୨୩୪୫୬୭୮୯୦")]
    [InlineData("1234567890", "en", "1234567890")]
    public void ConvertDigitsToIndic_ConvertsAccuratelyForEachSupportedLanguage(string input, string lang, string expected)
    {
        var result = ScriptTranslator.ConvertDigitsToIndic(input, lang);
        Assert.Equal(expected, result);

        var normalizedBack = ScriptTranslator.NormalizeDigitsToAscii(result);
        Assert.Equal(input, normalizedBack);
    }
}
