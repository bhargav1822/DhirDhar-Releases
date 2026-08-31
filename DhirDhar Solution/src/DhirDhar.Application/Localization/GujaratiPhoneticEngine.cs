using System;
using System.Collections.Generic;
using System.Text;

namespace DhirDhar.Application.Localization;

/// <summary>
/// Authoritative Gujarati Indic Input 3-style phonetic composition and transliteration engine.
/// Fully offline, deterministic, syllable-aware parser with longest-prefix matching,
/// conjuncts, inherent vowel resolution, and modifier handling.
/// </summary>
public sealed class GujaratiPhoneticEngine : IPhoneticLanguageEngine
{
    public static readonly GujaratiPhoneticEngine Instance = new();

    public string LanguageCode => "gu-IN";
    public string LanguageName => "Gujarati";
    public bool IsPhoneticActive => true;

    public string TransliterateWord(string word) => TransliterateToken(word);
    string IPhoneticLanguageEngine.Transliterate(string input) => Transliterate(input);
    string IPhoneticLanguageEngine.TransliterateWord(string word) => TransliterateToken(word);
    private const string Virama = "\u0ACD";    // ્
    private const string Anusvara = "\u0A82";  // ં
    private const string Visarga = "\u0A83";   // ઃ
    private const string Nukta = "\u0ABC";     // ઼
    private const string CandraE = "\u0AC5";   // ૅ
    private const string CandraO = "\u0AC9";   // ૉ

    // Consonant units strictly ordered by pattern length descending
    private static readonly (string Pattern, string Unicode)[] Consonants =
    {
        // 3-letter units
        ("ksh", "ક્ષ"), ("Ksh", "ક્ષ"), ("kSh", "ક્ષ"), ("KSh", "ક્ષ"), ("KSH", "ક્ષ"),
        ("dny", "જ્ઞ"), ("Dny", "જ્ઞ"), ("DNY", "જ્ઞ"),
        ("gny", "જ્ઞ"), ("Gny", "જ્ઞ"), ("GNY", "જ્ઞ"),
        ("jny", "જ્ઞ"), ("Jny", "જ્ઞ"), ("JNY", "જ્ઞ"),
        ("chh", "છ"), ("Chh", "છ"), ("CHH", "છ"),
        ("shh", "ષ"), ("Shh", "ષ"), ("SHH", "ષ"),
        ("shr", "શ્ર"), ("Shr", "શ્ર"), ("SHR", "શ્ર"),
        ("shl", "શ્લ"), ("Shl", "શ્લ"),
        ("khy", "ખ્ય"), ("Khy", "ખ્ય"),
        ("ghy", "ઘ્ય"), ("Ghy", "ઘ્ય"),
        ("chy", "ચ્ય"), ("Chy", "ચ્ય"),
        ("jhy", "ઝ્ય"), ("Jhy", "ઝ્ય"),
        ("thy", "થ્ય"), ("Thy", "થ્ય"),
        ("dhy", "ધ્ય"), ("Dhy", "ધ્ય"),
        ("phy", "ફ્ય"), ("Phy", "ફ્ય"),
        ("bhy", "ભ્ય"), ("Bhy", "ભ્ય"),
        ("shy", "શ્ય"), ("Shy", "શ્ય"),
        ("shv", "શ્વ"), ("Shv", "શ્વ"),
        ("khr", "ખ્ર"), ("Khr", "ખ્ર"),
        ("ghr", "ઘ્ર"), ("Ghr", "ઘ્ર"),
        ("thr", "થ્ર"), ("Thr", "થ્ર"),
        ("dhr", "ધ્ર"), ("Dhr", "ધ્ર"),
        ("phr", "ફ્ર"), ("Phr", "ફ્ર"),
        ("bhr", "ભ્ર"), ("Bhr", "ભ્ર"),
        ("nya", "ઞ"), ("Nya", "ઞ"),
        ("nga", "ઙ"), ("Nga", "ઙ"),
        ("lla", "ળ"), ("Lla", "ળ"),

        // 2-letter units
        ("kh", "ખ"), ("Kh", "ખ"), ("KH", "ખ"),
        ("gh", "ઘ"), ("Gh", "ઘ"), ("GH", "ઘ"),
        ("ch", "ચ"), ("Ch", "ચ"), ("CH", "ચ"),
        ("jh", "ઝ"), ("Jh", "ઝ"), ("JH", "ઝ"),
        ("Th", "ઠ"), ("TH", "ઠ"),
        ("th", "થ"), ("tH", "થ"),
        ("Dh", "ઢ"), ("DH", "ઢ"),
        ("dh", "ધ"), ("dH", "ધ"),
        ("ph", "ફ"), ("Ph", "ફ"), ("PH", "ફ"),
        ("bh", "ભ"), ("Bh", "ભ"), ("BH", "ભ"),
        ("Sh", "ષ"), ("SH", "ષ"),
        ("sh", "શ"),
        ("gn", "જ્ઞ"), ("Gn", "જ્ઞ"), ("GN", "જ્ઞ"),
        ("gy", "જ્ઞ"), ("Gy", "જ્ઞ"), ("GY", "જ્ઞ"),
        ("tr", "ત્ર"), ("Tr", "ત્ર"), ("TR", "ત્ર"),
        ("kr", "ક્ર"), ("Kr", "ક્ર"), ("KR", "ક્ર"),
        ("pr", "પ્ર"), ("Pr", "પ્ર"), ("PR", "પ્ર"),
        ("gr", "ગ્ર"), ("Gr", "ગ્ર"),
        ("dr", "દ્ર"), ("Dr", "દ્ર"),
        ("br", "બ્ર"), ("Br", "બ્ર"),
        ("mr", "મ્ર"), ("Mr", "મ્ર"),
        ("vr", "વ્ર"), ("Vr", "વ્ર"),
        ("sr", "સ્ર"), ("Sr", "સ્ર"),
        ("kt", "ક્ત"), ("Kt", "ક્ત"),
        ("pt", "પ્ત"), ("Pt", "પ્ત"),
        ("st", "સ્ત"), ("St", "સ્ત"),
        ("sk", "સ્ક"), ("Sk", "સ્ક"),
        ("sp", "સ્પ"), ("Sp", "સ્પ"),
        ("sm", "સ્મ"), ("Sm", "સ્મ"),
        ("sn", "સ્ન"), ("Sn", "સ્ન"),
        ("sv", "સ્વ"), ("Sv", "સ્વ"),
        ("sw", "સ્વ"), ("Sw", "સ્વ"),
        ("ng", "ઙ"), ("Ng", "ઙ"),
        ("nj", "ઞ"), ("Nj", "ઞ"),
        ("ll", "ળ"), ("LL", "ળ"),

        // 1-letter units
        ("k", "ક"), ("K", "ક"),
        ("g", "ગ"), ("G", "ગ"),
        ("c", "ચ"), ("C", "ચ"),
        ("j", "જ"), ("J", "જ"),
        ("z", "ઝ"), ("Z", "ઝ"),
        ("T", "ટ"),
        ("t", "ત"),
        ("D", "ડ"),
        ("d", "દ"),
        ("N", "ણ"),
        ("n", "ન"),
        ("p", "પ"), ("P", "પ"),
        ("f", "ફ"), ("F", "ફ"),
        ("b", "બ"), ("B", "બ"),
        ("m", "મ"), ("M", "મ"),
        ("y", "ય"), ("Y", "ય"),
        ("r", "ર"), ("R", "ર"),
        ("l", "લ"),
        ("L", "ળ"),
        ("v", "વ"), ("V", "વ"),
        ("w", "વ"), ("W", "વ"),
        ("S", "સ"),
        ("s", "સ"),
        ("h", "હ"), ("H", "હ"),
        ("x", "ક્ષ"), ("X", "ક્ષ")
    };

    // Dependent Matras (following a consonant) strictly ordered by length descending
    private static readonly (string Pattern, string Unicode)[] Matras =
    {
        // 3-letter matras
        ("aau", "ાઉ"), ("AAU", "ાઉ"), ("Aau", "ાઉ"),
        ("aai", "ાઈ"), ("AAI", "ાઈ"), ("Aai", "ાઈ"),

        // 2-letter matras
        ("aa", "ા"), ("AA", "ા"), ("Aa", "ા"),
        ("ee", "ી"), ("EE", "ી"), ("Ee", "ી"),
        ("ii", "ી"), ("II", "ી"), ("Ii", "ી"),
        ("oo", "ૂ"), ("OO", "ૂ"), ("Oo", "ૂ"),
        ("uu", "ૂ"), ("UU", "ૂ"), ("Uu", "ૂ"),
        ("ai", "ૈ"), ("AI", "ૈ"), ("Ai", "ૈ"),
        ("au", "ૌ"), ("AU", "ૌ"), ("Au", "ૌ"),
        ("ou", "ૌ"), ("OU", "ૌ"), ("Ou", "ૌ"),
        ("ru", "ૃ"), ("Ru", "ૃ"), ("RU", "ૃ"), ("rU", "ૃ"),
        ("ri", "ૃ"), ("Ri", "ૃ"), ("RI", "ૃ"), ("rI", "ૃ"),
        ("ae", "ૅ"), ("Ae", "ૅ"), ("AE", "ૅ"),

        // 1-letter matras
        ("A", "ા"),
        ("a", ""),
        ("i", "િ"),
        ("I", "ી"),
        ("u", "ુ"),
        ("U", "ૂ"),
        ("e", "ે"),
        ("E", "ૅ"),
        ("o", "ો"),
        ("O", "ૉ")
    };

    // Independent Vowels strictly ordered by length descending
    private static readonly (string Pattern, string Unicode)[] IndependentVowels =
    {
        // 3-letter vowels
        ("aau", "આઉ"), ("AAU", "આઉ"), ("Aau", "આઉ"),
        ("aai", "આઈ"), ("AAI", "આઈ"), ("Aai", "આઈ"),

        // 2-letter vowels
        ("aa", "આ"), ("AA", "આ"), ("Aa", "આ"),
        ("ee", "ઈ"), ("EE", "ઈ"), ("Ee", "ઈ"),
        ("ii", "ઈ"), ("II", "ઈ"), ("Ii", "ઈ"),
        ("oo", "ઊ"), ("OO", "ઊ"), ("Oo", "ઊ"),
        ("uu", "ઊ"), ("UU", "ઊ"), ("Uu", "ઊ"),
        ("ai", "ઐ"), ("AI", "ઐ"), ("Ai", "ઐ"),
        ("au", "ઔ"), ("AU", "ઔ"), ("Au", "ઔ"),
        ("ou", "ઔ"), ("OU", "ઔ"), ("Ou", "ઔ"),
        ("ru", "ઋ"), ("Ru", "ઋ"), ("RU", "ઋ"), ("rU", "ઋ"),
        ("ri", "ઋ"), ("Ri", "ઋ"), ("RI", "ઋ"), ("rI", "ઋ"),
        ("am", "અં"), ("Am", "આં"), ("AM", "આં"),
        ("an", "અં"), ("An", "આં"), ("AN", "આં"),
        ("ah", "અઃ"), ("Ah", "આઃ"), ("AH", "આઃ"),
        ("ae", "ઍ"), ("Ae", "ઍ"), ("AE", "ઍ"),

        // 1-letter vowels
        ("A", "આ"),
        ("a", "અ"),
        ("i", "ઇ"),
        ("I", "ઈ"),
        ("u", "ઉ"),
        ("U", "ઊ"),
        ("e", "એ"),
        ("E", "ઍ"),
        ("o", "ઓ"),
        ("O", "ઑ")
    };

    // Standard phonetic dictionary for standard names, places, and loan words
    private static readonly Dictionary<string, string> StandardNameRules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bhargav"] = "ભાર્ગવ",
        ["bhaargav"] = "ભાર્ગવ",
        ["bharat"] = "ભારત",
        ["bhaarat"] = "ભારત",
        ["chetan"] = "ચેતન",
        ["ramesh"] = "રમેશ",
        ["kiran"] = "કિરણ",
        ["kiraN"] = "કિરણ",
        ["palak"] = "પલક",
        ["paalak"] = "પાલક",
        ["valsing"] = "વાલસિંગ",
        ["valsang"] = "વાલસંગ",
        ["valji"] = "વાલજી",
        ["valbhai"] = "વાલભાઈ",
        ["chudi"] = "ચૂડી",
        ["choodi"] = "ચૂડી",
        ["chudo"] = "ચૂડો",
        ["choodo"] = "ચૂડો",
        ["chuda"] = "ચૂડા",
        ["chooda"] = "ચૂડા",
        ["bangle"] = "ચૂડી",
        ["bangles"] = "ચૂડી",
        ["kandoro"] = "કંદોરો",
        ["damani"] = "દામણી",
        ["baju"] = "બાજુ",
        ["hathphool"] = "હાથફૂલ",
        ["payal"] = "પાયલ",
        ["paijan"] = "પાયલ",
        ["kalla"] = "કલ્લા",
        ["pahochi"] = "પહોંચી",
        ["kadli"] = "કડલી",
        ["zud"] = "ઝૂડ",
        ["bor"] = "બોર",
        ["gokhru"] = "ગોખરુ",
        ["sankali"] = "સાંકળી",
        ["kap"] = "કાપ",
        ["mangalsutra"] = "મંગળસૂત્ર",
        ["nathan"] = "નથણી",
        ["nathi"] = "નથણી",
        ["nathani"] = "નથણી",
        ["nathni"] = "નથણી",
        ["haar"] = "હાર",
        ["viti"] = "વીંટી",
        ["veeti"] = "વીંટી",
        ["vinti"] = "વીંટી",
        ["veenti"] = "વીંટી",
        ["kada"] = "કડું",
        ["kadu"] = "કડું",
        ["mala"] = "માળા",
        ["jhumka"] = "ઝુમકા",
        ["jignesh"] = "જિગ્નેશ",
        ["kamal"] = "કમલ",
        ["manan"] = "મનન",
        ["dwiti"] = "દ્વિતી",
        ["lakshmi"] = "લક્ષ્મી",
        ["laxmi"] = "લક્ષ્મી",
        ["malai"] = "મલાઈ",
        ["panchal"] = "પંચાલ",
        ["dhirdhar"] = "ધીરધાર",
        ["namaste"] = "નમસ્તે",
        ["namaskar"] = "નમસ્કાર",
        ["ram"] = "રામ",
        ["raam"] = "રામ",
        ["ghar"] = "ઘર",
        ["dharm"] = "ધર્મ",
        ["dharma"] = "ધર્મ",
        ["shakti"] = "શક્તિ",
        ["kshama"] = "ક્ષમા",
        ["kshamaa"] = "ક્ષમા",
        ["gnan"] = "જ્ઞાન",
        ["gnaan"] = "જ્ઞાન",
        ["gyan"] = "જ્ઞાન",
        ["gyaan"] = "જ્ઞાન",
        ["ahmedabad"] = "અમદાવાદ",
        ["sukhsar"] = "સુખસર",
        ["patan"] = "પાટણ",
        ["gujarat"] = "ગુજરાત",
        ["maru"] = "મારું",
        ["naam"] = "નામ",
        ["chhe"] = "છે",
        ["mangal"] = "મંગલ",
        ["mangala"] = "મંગલા",
        ["mangalji"] = "મંગલજી",
        ["mangalabhai"] = "મંગળાભાઈ",
        ["rang"] = "રંગ",
        ["sing"] = "સિંગ",
        ["singh"] = "સિંગ",
        ["kumar"] = "કુમાર",
        ["kumaar"] = "કુમાર",
        ["pravin"] = "પ્રવિણ",
        ["praveen"] = "પ્રવીણ",
        ["chandrakant"] = "ચંદ્રકાંત",
        ["chandrakanta"] = "ચંદ્રકાંત",
        ["patel"] = "પટેલ",
        ["patidar"] = "પાટીદાર",
        ["chandra"] = "ચંદ્ર",
        ["kant"] = "કાંત"
    };

    /// <summary>
    /// Parses and transliterates live Latin input into accurate Gujarati Indic script.
    /// Handles both complete words and incremental live typing composition buffers.
    /// </summary>
    public static string Transliterate(string input)
    {
        if (input == null) return null!;
        if (input.Length == 0) return string.Empty;

        // Process token by token preserving spaces, newlines, and punctuation
        var sb = new StringBuilder();
        int i = 0;
        int n = input.Length;

        while (i < n)
        {
            char c = input[i];

            // If digit, localize to Gujarati numeral
            if (c >= '0' && c <= '9')
            {
                sb.Append((char)('૦' + (c - '0')));
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

    /// <summary>
    /// Parses a single continuous Latin token using syllable-based longest-match phonetic rules.
    /// </summary>
    public static string TransliterateToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return string.Empty;

        // 1. Check direct standard reference match if present
        if (StandardNameRules.TryGetValue(token, out var standardMatch))
        {
            return standardMatch;
        }

        // 2. Generic syllable/compound prefix decomposition (e.g. Valsing -> Val + sing, Chandrakant -> Chandra + kant)
        if (TryDecomposeCompoundPrefix(token, out var compoundDecomposed))
        {
            return compoundDecomposed;
        }

        var sb = new StringBuilder();
        int i = 0;
        int n = token.Length;
        bool prevIsConsonant = false;

        while (i < n)
        {
            char c = token[i];

            // 1. Explicit Modifiers from Gujarati Indic Input 3
            // Anusvara (^)
            if (c == '^')
            {
                sb.Append(Anusvara);
                prevIsConsonant = false;
                i++;
                continue;
            }

            // Explicit Halant (_)
            if (c == '_')
            {
                sb.Append(Virama);
                prevIsConsonant = false;
                i++;
                continue;
            }

            // Visarga (:)
            if (c == ':')
            {
                sb.Append(Visarga);
                prevIsConsonant = false;
                i++;
                continue;
            }

            // Anusvara using explicit 'M' key (e.g. kM -> કં)
            if (c == 'M' && prevIsConsonant && (i + 1 == n || (!IsVowelChar(token[i + 1]))))
            {
                sb.Append(Anusvara);
                prevIsConsonant = false;
                i++;
                continue;
            }

            // 2. Contextual Nasal / Anusvara (n, N, m, M before following consonant)
            if (i > 0 && (c == 'n' || c == 'N' || c == 'm' || c == 'M'))
            {
                if (IsContextualAnusvara(token, i))
                {
                    sb.Append(Anusvara);
                    prevIsConsonant = false;
                    i++;
                    continue;
                }
            }

            if (prevIsConsonant)
            {
                // When preceded by a consonant, check Matras FIRST
                var (matraPattern, matraUnicode) = MatchLongestPrefix(token, i, Matras);
                if (matraPattern != null)
                {
                    // For single 'i', apply word-final nominal/feminine deergha (ી) rule on 3+ letter tokens
                    if (matraPattern == "i" && (i + 1 == n) && n >= 3)
                    {
                        sb.Append("ી");
                    }
                    else
                    {
                        sb.Append(matraUnicode);
                    }
                    i += matraPattern.Length;
                    prevIsConsonant = false;
                    continue;
                }

                // If not a matra, check Consonants
                var (consPattern, consUnicode) = MatchLongestPrefix(token, i, Consonants);
                if (consPattern != null)
                {
                    sb.Append(consUnicode);
                    i += consPattern.Length;
                    prevIsConsonant = true;
                    continue;
                }
            }
            else
            {
                // When NOT preceded by a consonant (word-start or after a vowel):
                // Check Independent Vowels FIRST (e.g. "ru", "Ru", "aa", "a", "i", "ee", etc.)
                var (vowelPattern, vowelUnicode) = MatchLongestPrefix(token, i, IndependentVowels, isIndependentVowelTable: true);
                if (vowelPattern != null)
                {
                    sb.Append(vowelUnicode);
                    i += vowelPattern.Length;
                    prevIsConsonant = false;
                    continue;
                }

                // If not an independent vowel, check Consonants
                var (consPattern, consUnicode) = MatchLongestPrefix(token, i, Consonants);
                if (consPattern != null)
                {
                    sb.Append(consUnicode);
                    i += consPattern.Length;
                    prevIsConsonant = true;
                    continue;
                }
            }

            // Fallback: append character
            sb.Append(token[i]);
            prevIsConsonant = false;
            i++;
        }

        return sb.ToString();
    }

    private static (string? Pattern, string? Unicode) MatchLongestPrefix(
        string text,
        int startIndex,
        (string Pattern, string Unicode)[] table,
        bool isIndependentVowelTable = false)
    {
        for (int t = 0; t < table.Length; t++)
        {
            var p = table[t].Pattern;
            if (isIndependentVowelTable && startIndex > 0 && p.StartsWith("r", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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

    private static bool TryDecomposeCompoundPrefix(string token, out string result)
    {
        result = string.Empty;
        if (string.IsNullOrEmpty(token) || token.Length <= 3) return false;

        // Compound prefix "chandra" (e.g. Chandrakant, Chandrakanta, Chandravadan, Chandresh)
        if (token.StartsWith("chandra", StringComparison.OrdinalIgnoreCase))
        {
            string prefix = "ચંદ્ર";
            if (token.Length == 7)
            {
                result = prefix;
                return true;
            }
            string suffix = TransliterateToken(token.Substring(7));
            result = prefix + suffix;
            return true;
        }

        // Compound prefix "bhargav"
        if (token.Equals("bhargav", StringComparison.OrdinalIgnoreCase) || token.Equals("bhaargav", StringComparison.OrdinalIgnoreCase))
        {
            result = "ભાર્ગવ";
            return true;
        }

        // Compound prefix "pravin" / "praveen"
        if (token.Equals("pravin", StringComparison.OrdinalIgnoreCase))
        {
            result = "પ્રવિણ";
            return true;
        }
        if (token.Equals("praveen", StringComparison.OrdinalIgnoreCase))
        {
            result = "પ્રવીણ";
            return true;
        }

        // Compound prefix "chudi" / "choodi"
        if (token.Equals("chudi", StringComparison.OrdinalIgnoreCase) || token.Equals("choodi", StringComparison.OrdinalIgnoreCase))
        {
            result = "ચૂડી";
            return true;
        }

        // Prefix "val" before consonant (e.g. Valsing, Valsang, Valsad, Valji, Valbhai)
        if (token.StartsWith("val", StringComparison.OrdinalIgnoreCase))
        {
            char next = token[3];
            if (IsConsonantLetter(next))
            {
                string prefix = "વાલ";
                string suffix = TransliterateToken(token.Substring(3));
                result = prefix + suffix;
                return true;
            }
        }

        // Prefix "ram" before consonant (e.g. Ramsinh, Ramsing, Ramlal, Ramchand, Rambhai, Ramdas, Ramesh)
        if (token.StartsWith("ram", StringComparison.OrdinalIgnoreCase))
        {
            char next = token[3];
            if (IsConsonantLetter(next))
            {
                string prefix = "રામ";
                string suffix = TransliterateToken(token.Substring(3));
                result = prefix + suffix;
                return true;
            }
        }

        // Prefix "man" before 's' (e.g. Mansukh, Mansinh, Mansang)
        if (token.StartsWith("man", StringComparison.OrdinalIgnoreCase) && (token[3] == 's' || token[3] == 'S'))
        {
            string prefix = "મન";
            string suffix = TransliterateToken(token.Substring(3));
            result = prefix + suffix;
            return true;
        }

        // Prefix "dal" before 's' or 'p' (e.g. Dalsukh, Dalsing, Dalsang, Dalpat)
        if (token.StartsWith("dal", StringComparison.OrdinalIgnoreCase) && (token[3] == 's' || token[3] == 'S' || token[3] == 'p' || token[3] == 'P'))
        {
            string prefix = "દલ";
            string suffix = TransliterateToken(token.Substring(3));
            result = prefix + suffix;
            return true;
        }

        // Prefix "har" before 's' (e.g. Harsukh, Harsinh, Harsang)
        if (token.StartsWith("har", StringComparison.OrdinalIgnoreCase) && (token[3] == 's' || token[3] == 'S'))
        {
            string prefix = "હર";
            string suffix = TransliterateToken(token.Substring(3));
            result = prefix + suffix;
            return true;
        }

        return false;
    }

    private static bool IsContextualAnusvara(string token, int index)
    {
        if (index <= 0 || index + 1 >= token.Length)
        {
            return false;
        }

        char current = char.ToLowerInvariant(token[index]);
        char next = char.ToLowerInvariant(token[index + 1]);

        // 1. If followed by a vowel, it is the onset of the next syllable (e.g. na, ni, no, ma, mi, mo) -> NOT anusvara
        if (IsVowelChar(next))
        {
            return false;
        }

        // 2. If followed by explicit modifier or halant -> NOT anusvara
        if (next is '^' or '_' or '~' or ':')
        {
            return false;
        }

        // 3. If followed by identical nasal (geminate / double consonant nn, mm, e.g. panna -> પન્ના, ammi -> અમ્મી) -> NOT anusvara
        if (current == next)
        {
            return false;
        }

        // 4. If current is 'm' and next is 'r' (e.g. samrat, amrit -> mr is conjunct મ્ર) -> NOT anusvara
        if (current == 'm' && next == 'r')
        {
            return false;
        }

        // 5. Must be followed by a consonant letter (e.g. d, g, k, t, p, b, j, ch, s, v, h, etc.)
        if (!IsConsonantLetter(next))
        {
            return false;
        }

        return true;
    }

    private static bool IsVowelChar(char c)
    {
        char lower = char.ToLowerInvariant(c);
        return lower is 'a' or 'e' or 'i' or 'o' or 'u';
    }

    private static bool IsConsonantLetter(char c)
    {
        char lower = char.ToLowerInvariant(c);
        return (lower >= 'a' && lower <= 'z') && !IsVowelChar(lower);
    }

    private static bool IsLatinLetter(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }
}
