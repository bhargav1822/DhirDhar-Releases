using System;
using System.Collections.Generic;
using DhirDhar.Application.Localization;
using Xunit;

namespace DhirDhar.Application.Tests.Localization;

public class GujaratiPhoneticEngineTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void Transliterate_EmptyOrNull_ReturnsAsIs(string? input, string? expected)
    {
        var result = GujaratiPhoneticEngine.Transliterate(input!);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("a", "અ")]
    [InlineData("aa", "આ")]
    [InlineData("A", "આ")]
    [InlineData("i", "ઇ")]
    [InlineData("ee", "ઈ")]
    [InlineData("ii", "ઈ")]
    [InlineData("I", "ઈ")]
    [InlineData("u", "ઉ")]
    [InlineData("oo", "ઊ")]
    [InlineData("uu", "ઊ")]
    [InlineData("U", "ઊ")]
    [InlineData("ru", "ઋ")]
    [InlineData("Ru", "ઋ")]
    [InlineData("ri", "ઋ")]
    [InlineData("e", "એ")]
    [InlineData("ai", "ઐ")]
    [InlineData("o", "ઓ")]
    [InlineData("au", "ઔ")]
    [InlineData("ou", "ઔ")]
    [InlineData("am", "અં")]
    [InlineData("an", "અં")]
    [InlineData("ah", "અઃ")]
    [InlineData("ae", "ઍ")]
    [InlineData("E", "ઍ")]
    public void Transliterate_All14IndependentVowels_TransliteratesCorrectly(string input, string expected)
    {
        var result = GujaratiPhoneticEngine.Transliterate(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    // ક ખ ગ ઘ
    [InlineData("k", "ક")]
    [InlineData("ka", "ક")]
    [InlineData("kh", "ખ")]
    [InlineData("kha", "ખ")]
    [InlineData("g", "ગ")]
    [InlineData("ga", "ગ")]
    [InlineData("gh", "ઘ")]
    [InlineData("gha", "ઘ")]
    [InlineData("ng", "ઙ")]
    [InlineData("nga", "ઙ")]
    // ચ છ જ ઝ
    [InlineData("ch", "ચ")]
    [InlineData("cha", "ચ")]
    [InlineData("chh", "છ")]
    [InlineData("chha", "છ")]
    [InlineData("j", "જ")]
    [InlineData("ja", "જ")]
    [InlineData("jh", "ઝ")]
    [InlineData("jha", "ઝ")]
    [InlineData("z", "ઝ")]
    [InlineData("za", "ઝ")]
    [InlineData("nj", "ઞ")]
    [InlineData("nya", "ઞ")]
    // ટ ઠ ડ ઢ ણ
    [InlineData("T", "ટ")]
    [InlineData("Ta", "ટ")]
    [InlineData("Th", "ઠ")]
    [InlineData("Tha", "ઠ")]
    [InlineData("D", "ડ")]
    [InlineData("Da", "ડ")]
    [InlineData("Dh", "ઢ")]
    [InlineData("Dha", "ઢ")]
    [InlineData("N", "ણ")]
    [InlineData("Na", "ણ")]
    // ત થ દ ધ ન
    [InlineData("t", "ત")]
    [InlineData("ta", "ત")]
    [InlineData("th", "થ")]
    [InlineData("tha", "થ")]
    [InlineData("d", "દ")]
    [InlineData("da", "દ")]
    [InlineData("dh", "ધ")]
    [InlineData("dha", "ધ")]
    [InlineData("n", "ન")]
    [InlineData("na", "ન")]
    // પ ફ બ ભ મ
    [InlineData("p", "પ")]
    [InlineData("pa", "પ")]
    [InlineData("ph", "ફ")]
    [InlineData("pha", "ફ")]
    [InlineData("f", "ફ")]
    [InlineData("fa", "ફ")]
    [InlineData("b", "બ")]
    [InlineData("ba", "બ")]
    [InlineData("bh", "ભ")]
    [InlineData("bha", "ભ")]
    [InlineData("m", "મ")]
    [InlineData("ma", "મ")]
    // ય ર લ વ
    [InlineData("y", "ય")]
    [InlineData("ya", "ય")]
    [InlineData("r", "ર")]
    [InlineData("ra", "ર")]
    [InlineData("l", "લ")]
    [InlineData("la", "લ")]
    [InlineData("v", "વ")]
    [InlineData("va", "વ")]
    [InlineData("w", "વ")]
    [InlineData("wa", "વ")]
    // શ ષ સ હ ળ
    [InlineData("sh", "શ")]
    [InlineData("sha", "શ")]
    [InlineData("Sh", "ષ")]
    [InlineData("Sha", "ષ")]
    [InlineData("shh", "ષ")]
    [InlineData("shha", "ષ")]
    [InlineData("s", "સ")]
    [InlineData("sa", "સ")]
    [InlineData("h", "હ")]
    [InlineData("ha", "હ")]
    [InlineData("L", "ળ")]
    [InlineData("La", "ળ")]
    [InlineData("ll", "ળ")]
    [InlineData("lla", "ળ")]
    // ક્ષ જ્ઞ
    [InlineData("ksh", "ક્ષ")]
    [InlineData("ksha", "ક્ષ")]
    [InlineData("x", "ક્ષ")]
    [InlineData("xa", "ક્ષ")]
    [InlineData("gn", "જ્ઞ")]
    [InlineData("gna", "જ્ઞ")]
    [InlineData("gy", "જ્ઞ")]
    [InlineData("gya", "જ્ઞ")]
    [InlineData("dnya", "જ્ઞ")]
    [InlineData("gnya", "જ્ઞ")]
    [InlineData("jnya", "જ્ઞ")]
    public void Transliterate_AllConsonantsAndConjuncts_TransliteratesCorrectly(string input, string expected)
    {
        var result = GujaratiPhoneticEngine.Transliterate(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    // 1. Required Test Set from User Specification
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
    [InlineData("bhargav", "ભાર્ગવ")]
    [InlineData("Bhargav", "ભાર્ગવ")]
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
    [InlineData("lakshmi", "લક્ષ્મી")]
    [InlineData("Lakshmi", "લક્ષ્મી")]
    [InlineData("kshama", "ક્ષમા")]
    [InlineData("Kshama", "ક્ષમા")]
    [InlineData("gnan", "જ્ઞાન")]
    [InlineData("Gnan", "જ્ઞાન")]
    // 2. Additional Core Verification Words
    [InlineData("Bharat", "ભારત")]
    [InlineData("bharat", "ભારત")]
    [InlineData("Chetan", "ચેતન")]
    [InlineData("chetan", "ચેતન")]
    [InlineData("Ramesh", "રમેશ")]
    [InlineData("ramesh", "રમેશ")]
    [InlineData("kamal", "કમલ")]
    [InlineData("manan", "મનન")]
    [InlineData("dwiti", "દ્વિતી")]
    [InlineData("malai", "મલાઈ")]
    [InlineData("panchal", "પંચાલ")]
    [InlineData("dhirdhar", "ધીરધાર")]
    [InlineData("namaste", "નમસ્તે")]
    [InlineData("namaskar", "નમસ્કાર")]
    [InlineData("ahmedabad", "અમદાવાદ")]
    [InlineData("sukhsar", "સુખસર")]
    [InlineData("patan", "પાટણ")]
    [InlineData("gujarat", "ગુજરાત")]
    public void Transliterate_UserRequiredWords_TransliterateAccurately(string input, string expected)
    {
        var result = GujaratiPhoneticEngine.Transliterate(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("0123456789", "૦૧૨૩૪૫૬૭૮૯")]
    [InlineData("1234567890", "૧૨૩૪૫૬૭૮૯૦")]
    [InlineData("9924019827", "૯૯૨૪૦૧૯૮૨૭")]
    public void Transliterate_Numerals_ConvertsToGujaratiDigits(string input, string expected)
    {
        var result = GujaratiPhoneticEngine.Transliterate(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    // General contextual nasalization (Anusvara before consonants)
    [InlineData("kandori", "કંદોરી")]
    [InlineData("Kandori", "કંદોરી")]
    [InlineData("kandar", "કંદર")]
    [InlineData("Kandar", "કંદર")]
    [InlineData("kand", "કંદ")]
    [InlineData("Kand", "કંદ")]
    [InlineData("kandi", "કંદી")]
    [InlineData("Kandi", "કંદી")]
    [InlineData("kandu", "કંદુ")]
    [InlineData("Kandu", "કંદુ")]
    [InlineData("mangal", "મંગલ")]
    [InlineData("Mangal", "મંગલ")]
    [InlineData("rang", "રંગ")]
    [InlineData("Rang", "રંગ")]
    [InlineData("sang", "સંગ")]
    [InlineData("Sang", "સંગ")]
    [InlineData("sing", "સિંગ")]
    [InlineData("Sing", "સિંગ")]
    [InlineData("singer", "સિંગેર")]
    [InlineData("pang", "પંગ")]
    [InlineData("Pang", "પંગ")]
    [InlineData("gang", "ગંગ")]
    [InlineData("Gang", "ગંગ")]
    [InlineData("band", "બંદ")]
    [InlineData("Band", "બંદ")]
    [InlineData("mand", "મંદ")]
    [InlineData("Mand", "મંદ")]
    [InlineData("sand", "સંદ")]
    [InlineData("Sand", "સંદ")]
    public void Transliterate_ContextualNasalAnusvara_ResolvesAccurately(string input, string expected)
    {
        var result = GujaratiPhoneticEngine.Transliterate(input);
        Assert.Equal(expected, result);

        var wordResult = GujaratiPhoneticEngine.Instance.TransliterateWord(input);
        Assert.Equal(expected, wordResult);
    }

    [Theory]
    // Normal N and M cases (must not become anusvara)
    [InlineData("n", "ન")]
    [InlineData("na", "ન")]
    [InlineData("ni", "નિ")]
    [InlineData("no", "નો")]
    [InlineData("man", "મન")]
    [InlineData("nam", "નમ")]
    [InlineData("naam", "નામ")]
    [InlineData("m", "મ")]
    [InlineData("ma", "મ")]
    [InlineData("mi", "મિ")]
    [InlineData("mo", "મો")]
    public void Transliterate_NormalNandM_PreservesConsonants(string input, string expected)
    {
        var result = GujaratiPhoneticEngine.Transliterate(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    // Consonant + Matra composition verification
    [InlineData("ka", "ક")]
    [InlineData("kaa", "કા")]
    [InlineData("ki", "કિ")]
    [InlineData("kee", "કી")]
    [InlineData("ku", "કુ")]
    [InlineData("koo", "કૂ")]
    [InlineData("ke", "કે")]
    [InlineData("kai", "કૈ")]
    [InlineData("ko", "કો")]
    [InlineData("kau", "કૌ")]
    public void Transliterate_ConsonantAndMatras_PreservesAccurateComposition(string input, string expected)
    {
        var result = GujaratiPhoneticEngine.Transliterate(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Transliterate_RealTimeIncrementalTyping_Kandori_ResolvesDeterministically()
    {
        var engine = GujaratiPhoneticEngine.Instance;

        // k -> ક
        Assert.Equal("ક", engine.TransliterateWord("k"));
        // ka -> ક
        Assert.Equal("ક", engine.TransliterateWord("ka"));
        // kan -> કન (nasal consonant before ambiguity is resolved)
        Assert.Equal("કન", engine.TransliterateWord("kan"));
        // kand -> કંદ (look-ahead context resolves n before d as anusvara)
        Assert.Equal("કંદ", engine.TransliterateWord("kand"));
        // kando -> કંદો
        Assert.Equal("કંદો", engine.TransliterateWord("kando"));
        // kandor -> કંદોર
        Assert.Equal("કંદોર", engine.TransliterateWord("kandor"));
        // kandori -> કંદોરી
        Assert.Equal("કંદોરી", engine.TransliterateWord("kandori"));
    }

    [Fact]
    public void Transliterate_IncrementalBackspaceAndRetype_Kandori_MaintainsGujaratiState()
    {
        var engine = GujaratiPhoneticEngine.Instance;
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

    [Fact]
    public void Transliterate_AlreadyGujaratiText_LeavesUntouchedWithoutDoubleTransliteration()
    {
        string input = "કંદોરી";
        string result = GujaratiPhoneticEngine.Transliterate(input);
        Assert.Equal("કંદોરી", result);
    }
}
