using System;
using System.Globalization;
using DhirDhar.Application.Localization;
using Xunit;

namespace DhirDhar.Application.Tests;

public class DhirDharInputEngineTests
{
    // =========================================================================
    // 1. DEDICATED GUJARATI PHONETIC ENGINE TESTS
    // =========================================================================

    [Theory]
    [InlineData("Valsing", "વાલસિંગ")]
    [InlineData("Bhargav", "ભાર્ગવ")]
    [InlineData("Kumar", "કુમાર")]
    [InlineData("Chudi", "ચૂડી")]
    [InlineData("Mangal", "મંગલ")]
    [InlineData("Rang", "રંગ")]
    [InlineData("Sing", "સિંગ")]
    [InlineData("Pravin", "પ્રવિણ")]
    [InlineData("Chandrakant", "ચંદ્રકાંત")]
    [InlineData("Patel", "પટેલ")]
    [InlineData("Ramesh", "રમેશ")]
    [InlineData("Chetan", "ચેતન")]
    [InlineData("Palak", "પલક")]
    [InlineData("Paalak", "પાલક")]
    [InlineData("Bharat", "ભારત")]
    [InlineData("Kiran", "કિરણ")]
    [InlineData("Malai", "મલાઈ")]
    [InlineData("DhirDhar", "ધીરધાર")]
    [InlineData("Ahmedabad", "અમદાવાદ")]
    [InlineData("Sukhsar", "સુખસર")]
    [InlineData("Kandori", "કંદોરી")]
    [InlineData("kandori", "કંદોરી")]
    [InlineData("Kand", "કંદ")]
    [InlineData("Kandar", "કંદર")]
    [InlineData("Kandi", "કંદી")]
    [InlineData("Kandu", "કંદુ")]
    [InlineData("Band", "બંદ")]
    [InlineData("Mand", "મંદ")]
    [InlineData("Sand", "સંદ")]
    [InlineData("Gang", "ગંગ")]
    [InlineData("Pang", "પંગ")]
    public void GujaratiPhoneticEngine_RequiredWords_TransliterateAccurately(string input, string expected)
    {
        IPhoneticLanguageEngine engine = GujaratiPhoneticEngine.Instance;
        Assert.Equal("gu-IN", engine.LanguageCode);
        Assert.Equal("Gujarati", engine.LanguageName);
        Assert.True(engine.IsPhoneticActive);

        var result = engine.Transliterate(input);
        Assert.Equal(expected, result);

        var wordResult = engine.TransliterateWord(input);
        Assert.Equal(expected, wordResult);
    }

    [Fact]
    public void GujaratiPhoneticEngine_FullSentence_MaintainsContinuousPhoneticTyping()
    {
        IPhoneticLanguageEngine engine = GujaratiPhoneticEngine.Instance;
        string sentence = "Valsing Bhargav Kumar Pravin Chandrakant Patel";
        string expected = "વાલસિંગ ભાર્ગવ કુમાર પ્રવિણ ચંદ્રકાંત પટેલ";

        var result = engine.Transliterate(sentence);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GujaratiPhoneticEngine_IncrementalTyping_SimulatesRealisticLiveTyping()
    {
        IPhoneticLanguageEngine engine = GujaratiPhoneticEngine.Instance;
        string word = "Valsing";
        string buffer = "";

        // Incremental keystrokes
        foreach (char c in word)
        {
            buffer += c;
            string preview = engine.TransliterateWord(buffer);
            Assert.NotEmpty(preview);
        }

        Assert.Equal("વાલસિંગ", engine.TransliterateWord(buffer));
    }

    [Fact]
    public void GujaratiPhoneticEngine_IncrementalBackspace_HandlesPrefixResumption()
    {
        IPhoneticLanguageEngine engine = GujaratiPhoneticEngine.Instance;
        string buffer = "Valsing";
        Assert.Equal("વાલસિંગ", engine.TransliterateWord(buffer));

        // Backspace 'g'
        buffer = buffer[..^1]; // "Valsin"
        Assert.Equal("વાલસિન", engine.TransliterateWord(buffer));

        // Backspace 'n'
        buffer = buffer[..^1]; // "Valsi"
        Assert.Equal("વાલસિ", engine.TransliterateWord(buffer));

        // Retype 'ng'
        buffer += "ng"; // "Valsing"
        Assert.Equal("વાલસિંગ", engine.TransliterateWord(buffer));
    }

    [Fact]
    public void GujaratiPhoneticEngine_IncrementalTyping_Kandori_SimulatesRealisticLiveTyping()
    {
        IPhoneticLanguageEngine engine = GujaratiPhoneticEngine.Instance;
        string buffer = "";

        // k -> ક, ka -> ક, kan -> કન, kand -> કંદ, kando -> કંદો, kandor -> કંદોર, kandori -> કંદોરી
        buffer += "k";
        Assert.Equal("ક", engine.TransliterateWord(buffer));
        buffer += "a";
        Assert.Equal("ક", engine.TransliterateWord(buffer));
        buffer += "n";
        Assert.Equal("કન", engine.TransliterateWord(buffer));
        buffer += "d";
        Assert.Equal("કંદ", engine.TransliterateWord(buffer));
        buffer += "o";
        Assert.Equal("કંદો", engine.TransliterateWord(buffer));
        buffer += "r";
        Assert.Equal("કંદોર", engine.TransliterateWord(buffer));
        buffer += "i";
        Assert.Equal("કંદોરી", engine.TransliterateWord(buffer));
    }

    [Fact]
    public void GujaratiPhoneticEngine_IncrementalBackspace_Kandori_HandlesPrefixResumption()
    {
        IPhoneticLanguageEngine engine = GujaratiPhoneticEngine.Instance;
        string buffer = "kandori";
        Assert.Equal("કંદોરી", engine.TransliterateWord(buffer));

        // Backspace 'i' -> "kandor"
        buffer = buffer[..^1];
        Assert.Equal("કંદોર", engine.TransliterateWord(buffer));

        // Backspace 'r' -> "kando"
        buffer = buffer[..^1];
        Assert.Equal("કંદો", engine.TransliterateWord(buffer));

        // Backspace 'o' -> "kand"
        buffer = buffer[..^1];
        Assert.Equal("કંદ", engine.TransliterateWord(buffer));

        // Backspace 'd' -> "kan"
        buffer = buffer[..^1];
        Assert.Equal("કન", engine.TransliterateWord(buffer));

        // Backspace 'n' -> "ka"
        buffer = buffer[..^1];
        Assert.Equal("ક", engine.TransliterateWord(buffer));

        // Backspace 'a' -> "k"
        buffer = buffer[..^1];
        Assert.Equal("ક", engine.TransliterateWord(buffer));

        // Retype full word
        Assert.Equal("કંદોરી", engine.TransliterateWord("kandori"));
    }

    [Theory]
    [InlineData("ksha", "ક્ષ")]
    [InlineData("gnya", "જ્ઞ")]
    [InlineData("dnya", "જ્ઞ")]
    [InlineData("jnya", "જ્ઞ")]
    [InlineData("tra", "ત્ર")]
    [InlineData("shra", "શ્ર")]
    public void GujaratiPhoneticEngine_Conjuncts_TransliterateAccurately(string input, string expected)
    {
        IPhoneticLanguageEngine engine = GujaratiPhoneticEngine.Instance;
        var result = engine.Transliterate(input);
        Assert.Equal(expected, result);
    }

    // =========================================================================
    // 2. DEDICATED HINDI PHONETIC ENGINE TESTS
    // =========================================================================

    [Theory]
    [InlineData("Bhargav", "भार्गव")]
    [InlineData("Kumar", "कुमार")]
    [InlineData("Chetan", "चेतन")]
    [InlineData("Palak", "पलक")]
    [InlineData("Paalak", "पालक")]
    [InlineData("Bharat", "भारत")]
    [InlineData("Ramesh", "रमेश")]
    [InlineData("Kiran", "किरण")]
    [InlineData("Pravin", "प्रवीण")]
    [InlineData("Chandrakant", "चंद्रकांत")]
    [InlineData("Patel", "पटेल")]
    [InlineData("DhirDhar", "धीरधार")]
    [InlineData("Malai", "मलाई")]
    [InlineData("Ahmedabad", "अहमदाबाद")]
    [InlineData("Sukhsar", "सुखसर")]
    public void HindiPhoneticEngine_RequiredWords_TransliterateAccurately(string input, string expected)
    {
        IPhoneticLanguageEngine engine = HindiPhoneticEngine.Instance;
        Assert.Equal("hi-IN", engine.LanguageCode);
        Assert.Equal("Hindi", engine.LanguageName);
        Assert.True(engine.IsPhoneticActive);

        var result = engine.Transliterate(input);
        Assert.Equal(expected, result);

        var wordResult = engine.TransliterateWord(input);
        Assert.Equal(expected, wordResult);
    }

    [Fact]
    public void HindiPhoneticEngine_FullSentence_MaintainsContinuousPhoneticTyping()
    {
        IPhoneticLanguageEngine engine = HindiPhoneticEngine.Instance;
        string sentence = "Valsing Bhargav Kumar Pravin Chandrakant Patel";
        string expected = "वालसिंग भार्गव कुमार प्रवीण चंद्रकांत पटेल";

        var result = engine.Transliterate(sentence);
        Assert.Equal(expected, result);
    }

    // =========================================================================
    // 3. DEDICATED ENGLISH INPUT ENGINE TESTS
    // =========================================================================

    [Theory]
    [InlineData("Valsing Bhargav Kumar Pravin Chandrakant Patel")]
    [InlineData("Ramesh Patel")]
    [InlineData("Ahmedabad")]
    [InlineData("Loan 12345")]
    public void EnglishInputEngine_PassesThroughDirectlyWithoutTransliteration(string input)
    {
        IPhoneticLanguageEngine engine = EnglishInputEngine.Instance;
        Assert.Equal("en-IN", engine.LanguageCode);
        Assert.Equal("English", engine.LanguageName);
        Assert.False(engine.IsPhoneticActive);

        Assert.Equal(input, engine.Transliterate(input));
        Assert.Equal(input, engine.TransliterateWord(input));
    }

    // =========================================================================
    // 4. ENGINE SELECTION & TRANSITION TESTS
    // =========================================================================

    [Fact]
    public void EngineSelection_TransitionsCleanlyBetweenEngines()
    {
        IPhoneticLanguageEngine gujarati = GujaratiPhoneticEngine.Instance;
        IPhoneticLanguageEngine hindi = HindiPhoneticEngine.Instance;
        IPhoneticLanguageEngine english = EnglishInputEngine.Instance;

        // 1. Gujarati Mode
        Assert.Equal("વાલસિંગ", gujarati.Transliterate("Valsing"));
        Assert.Equal("ભાર્ગવ", gujarati.Transliterate("Bhargav"));

        // 2. Hindi Mode
        Assert.Equal("वालसिंग", hindi.Transliterate("Valsing"));
        Assert.Equal("भार्गव", hindi.Transliterate("Bhargav"));

        // 3. English Mode
        Assert.Equal("Valsing", english.Transliterate("Valsing"));
        Assert.Equal("Bhargav", english.Transliterate("Bhargav"));

        // 4. Back to Gujarati
        Assert.Equal("વાલસિંગ", gujarati.Transliterate("Valsing"));
        Assert.Equal("ભાર્ગવ", gujarati.Transliterate("Bhargav"));
    }

    // =========================================================================
    // 5. COMBINING MARK AND CHARACTER DELETION TESTS
    // =========================================================================

    [Fact]
    public void CombiningMarkDeletion_DeletesSingleUnicodeMarksDeterministically()
    {
        // "વાલસિંગ"
        // 'વ' (U+0AB5), 'ા' (U+0ABE), 'લ' (U+0AB2), 'સ' (U+0AB8), 'િ' (U+0ABF), 'ં' (U+0A82), 'ગ' (U+0A97)
        string text = "વાલસિંગ";
        Assert.Equal(7, text.Length);

        // Deleting 'ગ'
        text = text.Remove(6, 1);
        Assert.Equal("વાલસિં", text);

        // Deleting anusvara 'ં'
        text = text.Remove(5, 1);
        Assert.Equal("વાલસિ", text);

        // Deleting matra 'િ'
        text = text.Remove(4, 1);
        Assert.Equal("વાલસ", text);

        // Deleting 'સ'
        text = text.Remove(3, 1);
        Assert.Equal("વાલ", text);

        // Deleting 'લ'
        text = text.Remove(2, 1);
        Assert.Equal("વા", text);

        // Deleting matra 'ા'
        text = text.Remove(1, 1);
        Assert.Equal("વ", text);

        // Deleting 'વ'
        text = text.Remove(0, 1);
        Assert.Equal("", text);
    }

    // =========================================================================
    // 6. SINGLE KEYSTROKE NO-DUPLICATION VERIFICATION TESTS
    // =========================================================================

    [Fact]
    public void SingleCharacter_Gujarati_TransliteratesExactlyOnce()
    {
        IPhoneticLanguageEngine engine = GujaratiPhoneticEngine.Instance;

        // 'a' -> 'અ' (exactly one Gujarati character)
        var resultA = engine.TransliterateWord("a");
        Assert.Equal("અ", resultA);
        Assert.NotEqual("આ", resultA);
        Assert.NotEqual("aa", resultA);

        // 'k' -> 'ક' (exactly one Gujarati character)
        var resultK = engine.TransliterateWord("k");
        Assert.Equal("ક", resultK);
        Assert.NotEqual("kk", resultK);
    }

    [Fact]
    public void RepeatedSingleKeystrokes_ProduceSingleLogicalOutput()
    {
        IPhoneticLanguageEngine engine = GujaratiPhoneticEngine.Instance;

        // Simulating single keypress 'P' -> 'પ', 'a' -> 'પ', 'l' -> 'પલ', 'a' -> 'પલ', 'k' -> 'પલક'
        string buf = "";
        buf += "P";
        Assert.Equal("પ", engine.TransliterateWord(buf));
        buf += "a";
        Assert.Equal("પ", engine.TransliterateWord(buf));
        buf += "l";
        Assert.Equal("પલ", engine.TransliterateWord(buf));
        buf += "a";
        Assert.Equal("પલ", engine.TransliterateWord(buf));
        buf += "k";
        Assert.Equal("પલક", engine.TransliterateWord(buf));
    }

    [Fact]
    public void BackspaceResumption_MaintainsGujaratiTypingWithoutEnglishFallback()
    {
        IPhoneticLanguageEngine engine = GujaratiPhoneticEngine.Instance;

        // Type "Valsing" -> "વાલસિંગ"
        string buf = "Valsing";
        Assert.Equal("વાલસિંગ", engine.TransliterateWord(buf));

        // Backspace repeatedly until empty
        while (buf.Length > 0)
        {
            buf = buf[..^1];
            if (buf.Length > 0)
            {
                var preview = engine.TransliterateWord(buf);
                Assert.NotNull(preview);
            }
        }
        Assert.Empty(buf);

        // Type "Bhargav" -> "ભાર્ગવ"
        buf = "Bhargav";
        Assert.Equal("ભાર્ગવ", engine.TransliterateWord(buf));
    }

    [Fact]
    public void SentenceWithPunctuationAndNumbers_MaintainsAccurateSingleOutput()
    {
        IPhoneticLanguageEngine engine = GujaratiPhoneticEngine.Instance;

        string input = "Valsing, Bhargav. 10000";
        string result = engine.Transliterate(input);

        // Punctuation and numbers preserved without duplication (10000 -> ૧૦૦૦૦)
        Assert.Equal("વાલસિંગ, ભાર્ગવ. ૧૦૦૦૦", result);
    }
}
