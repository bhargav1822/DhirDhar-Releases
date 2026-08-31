using System;
using System.Collections.Generic;
using System.Text;

namespace DhirDhar.Application.Localization;

/// <summary>
/// Authoritative Hindi Devanagari phonetic composition and transliteration engine.
/// Fully offline, deterministic, syllable-aware parser with longest-prefix matching,
/// conjuncts, inherent vowel resolution, and modifier handling.
/// </summary>
public sealed class HindiPhoneticEngine : IPhoneticLanguageEngine
{
    public static readonly HindiPhoneticEngine Instance = new();

    public string LanguageCode => "hi-IN";
    public string LanguageName => "Hindi";
    public bool IsPhoneticActive => true;

    private const string Virama = "\u094D";    // ्
    private const string Anusvara = "\u0902";  // ं
    private const string Visarga = "\u0903";   // ः
    private const string Nukta = "\u093C";     // ़
    private const string CandraE = "\u0945";   // ॅ
    private const string CandraO = "\u0949";   // ॉ

    // Consonant units strictly ordered by pattern length descending
    private static readonly (string Pattern, string Unicode)[] Consonants =
    {
        // 3-letter units
        ("ksh", "क्ष"), ("Ksh", "क्ष"), ("kSh", "क्ष"), ("KSh", "क्ष"), ("KSH", "क्ष"),
        ("dny", "ज्ञ"), ("Dny", "ज्ञ"), ("DNY", "ज्ञ"),
        ("gny", "ज्ञ"), ("Gny", "ज्ञ"), ("GNY", "ज्ञ"),
        ("jny", "ज्ञ"), ("Jny", "ज्ञ"), ("JNY", "ज्ञ"),
        ("chh", "छ"), ("Chh", "छ"), ("CHH", "छ"),
        ("shh", "ष"), ("Shh", "ष"), ("SHH", "ष"),
        ("shr", "श्र"), ("Shr", "श्र"), ("SHR", "श्र"),
        ("shl", "श्ल"), ("Shl", "श्ल"),
        ("khy", "ख्य"), ("Khy", "ख्य"),
        ("ghy", "घ्य"), ("Ghy", "घ्य"),
        ("chy", "च्य"), ("Chy", "च्य"),
        ("jhy", "झ्य"), ("Jhy", "झ्य"),
        ("thy", "थ्य"), ("Thy", "थ्य"),
        ("dhy", "ध्य"), ("Dhy", "ध्य"),
        ("phy", "फ़्य"), ("Phy", "फ़्य"),
        ("bhy", "भ्य"), ("Bhy", "भ्य"),
        ("shy", "श्य"), ("Shy", "श्य"),
        ("shv", "श्व"), ("Shv", "श्व"),
        ("khr", "ख्र"), ("Khr", "ख्र"),
        ("ghr", "घ्र"), ("Ghr", "घ्र"),
        ("thr", "थ्र"), ("Thr", "थ्र"),
        ("dhr", "ध्र"), ("Dhr", "ध्र"),
        ("phr", "फ्र"), ("Phr", "फ्र"),
        ("bhr", "भ्र"), ("Bhr", "भ्र"),
        ("nya", "ञ"), ("Nya", "ञ"),
        ("nga", "ङ"), ("Nga", "ङ"),
        ("lla", "ळ"), ("Lla", "ळ"),

        // 2-letter units
        ("kh", "ख"), ("Kh", "ख"), ("KH", "ख"),
        ("gh", "घ"), ("Gh", "घ"), ("GH", "घ"),
        ("ch", "च"), ("Ch", "च"), ("CH", "च"),
        ("jh", "झ"), ("Jh", "झ"), ("JH", "झ"),
        ("TH", "ठ"), ("Th", "थ"), ("th", "थ"), ("tH", "थ"),
        ("DH", "ढ"), ("Dh", "ध"), ("dh", "ध"), ("dH", "ध"),
        ("ph", "फ"), ("Ph", "फ"), ("PH", "फ"),
        ("bh", "भ"), ("Bh", "भ"), ("BH", "भ"),
        ("SH", "श"), ("Sh", "श"), ("sh", "श"),
        ("gn", "ज्ञ"), ("Gn", "ज्ञ"), ("GN", "ज्ञ"),
        ("gy", "ज्ञ"), ("Gy", "ज्ञ"), ("GY", "ज्ञ"),
        ("tr", "त्र"), ("Tr", "त्र"), ("TR", "त्र"),
        ("kr", "क्र"), ("Kr", "क्र"), ("KR", "क्र"),
        ("pr", "प्र"), ("Pr", "प्र"), ("PR", "प्र"),
        ("gr", "ग्र"), ("Gr", "ग्र"),
        ("dr", "द्र"), ("Dr", "द्र"),
        ("br", "ब्र"), ("Br", "ब्र"),
        ("mr", "म्र"), ("Mr", "म्र"),
        ("vr", "व्र"), ("Vr", "व्र"),
        ("sr", "स्र"), ("Sr", "स्र"),
        ("kt", "क्त"), ("Kt", "क्त"),
        ("pt", "प्त"), ("Pt", "प्त"),
        ("st", "स्त"), ("St", "स्त"),
        ("sk", "स्क"), ("Sk", "स्क"),
        ("sp", "स्प"), ("Sp", "स्प"),
        ("sm", "स्म"), ("Sm", "स्म"),
        ("sn", "स्न"), ("Sn", "स्न"),
        ("sv", "स्व"), ("Sv", "स्व"),
        ("sw", "स्व"), ("Sw", "स्व"),
        ("ng", "ङ"), ("Ng", "ङ"),
        ("nj", "ञ"), ("Nj", "ञ"),
        ("ll", "ळ"), ("LL", "ळ"),

        // 1-letter units
        ("k", "क"), ("K", "क"),
        ("g", "ग"), ("G", "ग"),
        ("c", "च"), ("C", "च"),
        ("j", "ज"), ("J", "ज"),
        ("z", "ज़"), ("Z", "ज़"),
        ("T", "ट"),
        ("t", "त"),
        ("D", "ड"),
        ("d", "द"),
        ("N", "ण"),
        ("n", "न"),
        ("p", "प"), ("P", "प"),
        ("f", "फ़"), ("F", "फ़"),
        ("b", "ब"), ("B", "ब"),
        ("m", "म"), ("M", "म"),
        ("y", "य"), ("Y", "य"),
        ("r", "र"), ("R", "र"),
        ("l", "ल"),
        ("L", "ळ"),
        ("v", "व"), ("V", "व"),
        ("w", "व"), ("W", "व"),
        ("S", "श"),
        ("s", "स"),
        ("h", "ह"), ("H", "ह"),
        ("x", "क्ष"), ("X", "क्ष")
    };

    // Dependent Matras (following a consonant) strictly ordered by length descending
    private static readonly (string Pattern, string Unicode)[] Matras =
    {
        // 3-letter matras
        ("aau", "ाउ"), ("AAU", "ाउ"), ("Aau", "ाउ"),
        ("aai", "ाई"), ("AAI", "ाई"), ("Aai", "ाई"),

        // 2-letter matras
        ("aa", "ा"), ("AA", "ा"), ("Aa", "ा"),
        ("ee", "ी"), ("EE", "ी"), ("Ee", "ी"),
        ("ii", "ी"), ("II", "ी"), ("Ii", "ी"),
        ("oo", "ू"), ("OO", "ू"), ("Oo", "ू"),
        ("uu", "ू"), ("UU", "ू"), ("Uu", "ू"),
        ("ai", "ै"), ("AI", "ै"), ("Ai", "ै"),
        ("au", "ौ"), ("AU", "ौ"), ("Au", "ौ"),
        ("ou", "ौ"), ("OU", "ौ"), ("Ou", "ौ"),
        ("ru", "ृ"), ("Ru", "ृ"), ("RU", "ृ"), ("rU", "ृ"),
        ("ri", "ृ"), ("Ri", "ृ"), ("RI", "ृ"), ("rI", "ृ"),
        ("ae", "ॅ"), ("Ae", "ॅ"), ("AE", "ॅ"),

        // 1-letter matras
        ("A", "ा"),
        ("a", ""),
        ("i", "ि"),
        ("I", "ी"),
        ("u", "ु"),
        ("U", "ू"),
        ("e", "े"),
        ("E", "ॅ"),
        ("o", "ो"),
        ("O", "ॉ")
    };

    // Independent Vowels strictly ordered by length descending
    private static readonly (string Pattern, string Unicode)[] IndependentVowels =
    {
        // 3-letter vowels
        ("aau", "आउ"), ("AAU", "आउ"), ("Aau", "आउ"),
        ("aai", "आई"), ("AAI", "आई"), ("Aai", "आई"),

        // 2-letter vowels
        ("aa", "आ"), ("AA", "आ"), ("Aa", "आ"),
        ("ee", "ई"), ("EE", "ई"), ("Ee", "ई"),
        ("ii", "ई"), ("II", "ई"), ("Ii", "ई"),
        ("oo", "ऊ"), ("OO", "ऊ"), ("Oo", "ऊ"),
        ("uu", "ऊ"), ("UU", "ऊ"), ("Uu", "ऊ"),
        ("ai", "ऐ"), ("AI", "ऐ"), ("Ai", "ऐ"),
        ("au", "औ"), ("AU", "औ"), ("Au", "औ"),
        ("ou", "औ"), ("OU", "औ"), ("Ou", "औ"),
        ("ru", "ऋ"), ("Ru", "ऋ"), ("RU", "ऋ"), ("rU", "ऋ"),
        ("ri", "ऋ"), ("Ri", "ऋ"), ("RI", "ऋ"), ("rI", "ऋ"),
        ("am", "अं"), ("Am", "आं"), ("AM", "आं"),
        ("an", "अं"), ("An", "आं"), ("AN", "आं"),
        ("ah", "अः"), ("Ah", "आः"), ("AH", "आः"),
        ("ae", "ऍ"), ("Ae", "ऍ"), ("AE", "ऍ"),

        // 1-letter vowels
        ("A", "आ"),
        ("a", "अ"),
        ("i", "इ"),
        ("I", "ई"),
        ("u", "उ"),
        ("U", "ऊ"),
        ("e", "ए"),
        ("E", "ऍ"),
        ("o", "ओ"),
        ("O", "ऑ")
    };

    // Well-known standard names & places handled accurately in Hindi
    private static readonly Dictionary<string, string> StandardNameRules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bhargav"] = "भार्गव",
        ["bhaargav"] = "भार्गव",
        ["bharat"] = "भारत",
        ["bhaarat"] = "भारत",
        ["chetan"] = "चेतन",
        ["ramesh"] = "रमेश",
        ["kiran"] = "किरण",
        ["kiraN"] = "किरण",
        ["palak"] = "पलक",
        ["paalak"] = "पालक",
        ["valsing"] = "वालसिंग",
        ["valsang"] = "वालसंग",
        ["kumar"] = "कुमार",
        ["kumaar"] = "कुमार",
        ["pravin"] = "प्रवीण",
        ["praveen"] = "प्रवीण",
        ["chandrakant"] = "चंद्रकांत",
        ["patel"] = "पटेल",
        ["dhirdhar"] = "धीरधार",
        ["dhir"] = "धीर",
        ["dhar"] = "धार",
        ["malai"] = "मलाई",
        ["panchal"] = "पंचाल",
        ["namaste"] = "नमस्ते",
        ["namaskar"] = "नमस्कार",
        ["mangal"] = "मंगल",
        ["rang"] = "रंग",
        ["sing"] = "सिंग",
        ["singh"] = "सिंह",
        ["chudi"] = "चूड़ी",
        ["ahmedabad"] = "अहमदाबाद",
        ["sukhsar"] = "सुखसर"
    };

    public string Transliterate(string input)
    {
        if (input == null) return null!;
        if (input.Length == 0) return string.Empty;

        var sb = new StringBuilder();
        int i = 0;
        int n = input.Length;

        while (i < n)
        {
            char c = input[i];

            // If digit, localize to Devanagari numeral
            if (c >= '0' && c <= '9')
            {
                sb.Append((char)('०' + (c - '0')));
                i++;
                continue;
            }

            // If whitespace or non-Latin letter/symbol, append directly
            if (!IsLatinLetter(c) && c != '^' && c != '_' && c != '~' && c != ':')
            {
                sb.Append(c);
                i++;
                continue;
            }

            // Extract continuous Latin word/token for composition
            int tokenStart = i;
            while (i < n && (IsLatinLetter(input[i]) || input[i] == '^' || input[i] == '_' || input[i] == '~' || input[i] == ':'))
            {
                i++;
            }

            string token = input.Substring(tokenStart, i - tokenStart);
            sb.Append(TransliterateToken(token));
        }

        return sb.ToString();
    }

    public string TransliterateWord(string word)
    {
        return TransliterateToken(word);
    }

    public static string TransliterateToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return string.Empty;

        if (StandardNameRules.TryGetValue(token, out var standardMatch))
        {
            return standardMatch;
        }

        var sb = new StringBuilder();
        int i = 0;
        int n = token.Length;
        bool prevIsConsonant = false;

        while (i < n)
        {
            char c = token[i];

            if (c == '^')
            {
                sb.Append(Anusvara);
                prevIsConsonant = false;
                i++;
                continue;
            }

            if (c == '_')
            {
                sb.Append(Virama);
                prevIsConsonant = false;
                i++;
                continue;
            }

            if (c == ':')
            {
                sb.Append(Visarga);
                prevIsConsonant = false;
                i++;
                continue;
            }

            if (c == 'M' && prevIsConsonant && (i + 1 == n || !IsVowelLetter(token[i + 1])))
            {
                sb.Append(Anusvara);
                prevIsConsonant = false;
                i++;
                continue;
            }

            // Special Nasal Sequences: "ngh", "ng", "nh"
            if (i + 3 <= n && string.Equals(token.Substring(i, 3), "ngh", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(Anusvara);
                sb.Append("घ");
                i += 3;
                prevIsConsonant = true;
                continue;
            }

            if (i + 2 <= n && string.Equals(token.Substring(i, 2), "ng", StringComparison.OrdinalIgnoreCase))
            {
                if (i > 0)
                {
                    sb.Append(Anusvara);
                    sb.Append("ग");
                    i += 2;
                    prevIsConsonant = true;
                    continue;
                }
            }

            if (i + 2 <= n && string.Equals(token.Substring(i, 2), "nh", StringComparison.OrdinalIgnoreCase))
            {
                if (i > 0)
                {
                    sb.Append(Anusvara);
                    sb.Append("ह");
                    i += 2;
                    prevIsConsonant = true;
                    continue;
                }
            }

            if (prevIsConsonant)
            {
                var (matraPattern, matraUnicode) = MatchLongestPrefix(token, i, Matras);
                if (matraPattern != null)
                {
                    sb.Append(matraUnicode);
                    i += matraPattern.Length;
                    prevIsConsonant = false;
                    continue;
                }

                var (consPattern, consUnicode) = MatchLongestPrefix(token, i, Consonants);
                if (consPattern != null)
                {
                    sb.Append(Virama);
                    sb.Append(consUnicode);
                    i += consPattern.Length;
                    prevIsConsonant = true;
                    continue;
                }
            }
            else
            {
                var (vowelPattern, vowelUnicode) = MatchLongestPrefix(token, i, IndependentVowels);
                if (vowelPattern != null)
                {
                    sb.Append(vowelUnicode);
                    i += vowelPattern.Length;
                    prevIsConsonant = false;
                    continue;
                }

                var (consPattern, consUnicode) = MatchLongestPrefix(token, i, Consonants);
                if (consPattern != null)
                {
                    sb.Append(consUnicode);
                    i += consPattern.Length;
                    prevIsConsonant = true;
                    continue;
                }
            }

            sb.Append(token[i]);
            prevIsConsonant = false;
            i++;
        }

        return sb.ToString();
    }

    private static (string? Pattern, string? Unicode) MatchLongestPrefix(
        string text,
        int startIndex,
        (string Pattern, string Unicode)[] table)
    {
        for (int t = 0; t < table.Length; t++)
        {
            var p = table[t].Pattern;
            if (startIndex + p.Length <= text.Length)
            {
                bool matches = true;
                for (int j = 0; j < p.Length; j++)
                {
                    if (text[startIndex + j] != p[j])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return (p, table[t].Unicode);
                }
            }
        }

        return (null, null);
    }

    private static bool IsVowelLetter(char c)
    {
        char lower = char.ToLowerInvariant(c);
        return lower is 'a' or 'e' or 'i' or 'o' or 'u';
    }

    private static bool IsLatinLetter(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }
}
