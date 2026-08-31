using System;
using System.Collections.Generic;
using DhirDhar.Application.Localization;
using Xunit;

namespace DhirDhar.Application.Tests.Localization;

public class OfflineGujaratiTransliterationTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void Transliterate_EmptyOrNull_ReturnsAsIs(string? input, string? expected)
    {
        var result = OfflineGujaratiTransliteration.Transliterate(input!);
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
    [InlineData("e", "એ")]
    [InlineData("ai", "ઐ")]
    [InlineData("o", "ઓ")]
    [InlineData("au", "ઔ")]
    [InlineData("ou", "ઔ")]
    [InlineData("ru", "ઋ")]
    [InlineData("Ru", "ઋ")]
    public void Transliterate_IndependentVowels_TransliteratesCorrectly(string input, string expected)
    {
        var result = OfflineGujaratiTransliteration.Transliterate(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("k", "ક")]
    [InlineData("kh", "ખ")]
    [InlineData("Kh", "ખ")]
    [InlineData("g", "ગ")]
    [InlineData("gh", "ઘ")]
    [InlineData("Gh", "ઘ")]
    [InlineData("ch", "ચ")]
    [InlineData("Ch", "ચ")]
    [InlineData("chh", "છ")]
    [InlineData("j", "જ")]
    [InlineData("jh", "ઝ")]
    [InlineData("z", "ઝ")]
    [InlineData("T", "ટ")]
    [InlineData("Th", "ઠ")]
    [InlineData("D", "ડ")]
    [InlineData("Dh", "ઢ")]
    [InlineData("N", "ણ")]
    [InlineData("t", "ત")]
    [InlineData("th", "થ")]
    [InlineData("d", "દ")]
    [InlineData("dh", "ધ")]
    [InlineData("n", "ન")]
    [InlineData("p", "પ")]
    [InlineData("f", "ફ")]
    [InlineData("ph", "ફ")]
    [InlineData("b", "બ")]
    [InlineData("bh", "ભ")]
    [InlineData("Bh", "ભ")]
    [InlineData("m", "મ")]
    [InlineData("y", "ય")]
    [InlineData("r", "ર")]
    [InlineData("l", "લ")]
    [InlineData("L", "ળ")]
    [InlineData("v", "વ")]
    [InlineData("w", "વ")]
    [InlineData("sh", "શ")]
    [InlineData("Sh", "ષ")]
    [InlineData("s", "સ")]
    [InlineData("h", "હ")]
    [InlineData("x", "ક્ષ")]
    [InlineData("ksh", "ક્ષ")]
    [InlineData("gn", "જ્ઞ")]
    [InlineData("gy", "જ્ઞ")]
    public void Transliterate_Consonants_TransliteratesCorrectly(string input, string expected)
    {
        var result = OfflineGujaratiTransliteration.Transliterate(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("ka", "ક")]
    [InlineData("kaa", "કા")]
    [InlineData("kA", "કા")]
    [InlineData("ki", "કિ")]
    [InlineData("kee", "કી")]
    [InlineData("kii", "કી")]
    [InlineData("kI", "કી")]
    [InlineData("ku", "કુ")]
    [InlineData("koo", "કૂ")]
    [InlineData("kuu", "કૂ")]
    [InlineData("kU", "કૂ")]
    [InlineData("ke", "કે")]
    [InlineData("kai", "કૈ")]
    [InlineData("ko", "કો")]
    [InlineData("kau", "કૌ")]
    [InlineData("kou", "કૌ")]
    [InlineData("kRu", "કૃ")]
    public void Transliterate_Matras_TransliteratesCorrectly(string input, string expected)
    {
        var result = OfflineGujaratiTransliteration.Transliterate(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("kM", "કં")]
    [InlineData("km", "કમ")]
    public void Transliterate_Anusvara_TransliteratesCorrectly(string input, string expected)
    {
        var result = OfflineGujaratiTransliteration.Transliterate(input);
        Assert.Equal(expected, result);
    }

    [Theory]
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
    [InlineData("chetan", "ચેતન")]
    [InlineData("bhaarat", "ભારત")]
    [InlineData("bharat", "ભારત")]
    [InlineData("ramesh", "રમેશ")]
    [InlineData("kiraN", "કિરણ")]
    [InlineData("ksha", "ક્ષ")]
    [InlineData("gna", "જ્ઞ")]
    [InlineData("gya", "જ્ઞ")]
    [InlineData("tra", "ત્ર")]
    [InlineData("ahmedabad", "અમદાવાદ")]
    [InlineData("sukhsar", "સુખસર")]
    [InlineData("patan", "પાટણ")]
    [InlineData("gujarat", "ગુજરાત")]
    [InlineData("kandori", "કંદોરી")]
    [InlineData("Kandori", "કંદોરી")]
    [InlineData("kand", "કંદ")]
    [InlineData("kandar", "કંદર")]
    [InlineData("kandi", "કંદી")]
    [InlineData("kandu", "કંદુ")]
    [InlineData("band", "બંદ")]
    [InlineData("mand", "મંદ")]
    [InlineData("sand", "સંદ")]
    [InlineData("gang", "ગંગ")]
    [InlineData("pang", "પંગ")]
    public void Transliterate_Words_TransliterateAccurately(string input, string expected)
    {
        var result = OfflineGujaratiTransliteration.Transliterate(input);
        Assert.Equal(expected, result);
    }
}
