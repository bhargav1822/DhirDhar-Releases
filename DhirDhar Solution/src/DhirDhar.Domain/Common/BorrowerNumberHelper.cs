using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DhirDhar.Domain.Common;

/// <summary>
/// Centralized helper utilities for Business Profile prefix generation,
/// sequence parsing, borrower number formatting, and input validation.
/// Format: "{Prefix} {FormattedSequence}" (e.g. "DS 01", "DS 02", "DS 1002").
/// </summary>
public static class BorrowerNumberHelper
{
    public const string DefaultBusinessName = "DhirDhar Solution";
    public const string DefaultPrefix = "DS";

    /// <summary>
    /// Generates a standardized uppercase prefix from the Business Profile Name.
    /// Rules:
    /// - Multi-word: Use the first letter of each word in uppercase
    ///   (e.g., "DhirDhar Solution" -> "DS", "ABC Finance" -> "AF", "Shree Ram Finance" -> "SRF", "DhirDhar Solution India" -> "DSI").
    /// - Single-word: Use the first two characters in uppercase (e.g., "Dwiti" -> "DW").
    /// - Remove spaces and special characters.
    /// </summary>
    public static string GeneratePrefixFromBusinessName(string? businessName)
    {
        if (string.IsNullOrWhiteSpace(businessName))
        {
            return DefaultPrefix;
        }

        // Extract alphanumeric word tokens
        var words = Regex.Matches(businessName, @"[\p{L}\p{N}]+")
            .Cast<Match>()
            .Select(m => m.Value)
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .ToList();

        if (words.Count == 0)
        {
            return DefaultPrefix;
        }

        if (words.Count == 1)
        {
            var singleWord = words[0];
            var letters = new string(singleWord.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            if (letters.Length >= 2)
            {
                return letters[..2];
            }
            if (letters.Length == 1)
            {
                return letters;
            }
            return DefaultPrefix;
        }

        // Multi-word: take first letter of each word in uppercase
        var prefixChars = words.Select(w => char.ToUpperInvariant(w[0])).ToArray();
        var prefix = new string(prefixChars);
        return string.IsNullOrWhiteSpace(prefix) ? DefaultPrefix : prefix;
    }

    /// <summary>
    /// Formats a numeric sequence number.
    /// Values &lt; 100 are padded with a leading zero (e.g., 1 -> "01", 99 -> "99").
    /// Values &gt;= 100 are unpadded (e.g., 100 -> "100", 1002 -> "1002", 5000 -> "5000").
    /// </summary>
    public static string FormatSequence(long sequence)
    {
        var safe = Math.Max(1, sequence);
        return safe < 100
            ? safe.ToString("D2", CultureInfo.InvariantCulture)
            : safe.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a complete borrower number given a prefix and numeric sequence.
    /// Example: ("DS", 1) -> "DS 01", ("DS", 1002) -> "DS 1002", ("SRF", 5) -> "SRF 05".
    /// </summary>
    public static string FormatBorrowerNumber(string? prefix, long sequence)
    {
        var cleanPrefix = string.IsNullOrWhiteSpace(prefix) ? DefaultPrefix : prefix.Trim().ToUpperInvariant();
        return $"{cleanPrefix} {FormatSequence(sequence)}";
    }

    /// <summary>
    /// Overload formatting with default prefix.
    /// </summary>
    public static string FormatBorrowerNumber(long sequence)
    {
        return FormatBorrowerNumber(DefaultPrefix, sequence);
    }

    /// <summary>
    /// Tries to parse the numeric sequence from any borrower number or sequence string.
    /// Handles "DS 01", "DS 1002", "1002", "01", "DS-01", "DS01", "#DS 1002", Indic numerals, etc.
    /// </summary>
    public static bool TryParseSequence(string? input, string? prefix, out long sequence)
    {
        sequence = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = NormalizeDigitsToAscii(input.Trim());
        if (normalized.StartsWith("#"))
        {
            normalized = normalized.TrimStart('#').Trim();
        }

        var cleanPrefix = string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix.Trim().ToUpperInvariant();

        // 1. If starts with cleanPrefix, strip prefix
        if (!string.IsNullOrEmpty(cleanPrefix) && normalized.StartsWith(cleanPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = normalized[cleanPrefix.Length..].TrimStart('-', '_', ' ');
            if (long.TryParse(remainder, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedFromPrefix) && parsedFromPrefix > 0)
            {
                sequence = parsedFromPrefix;
                return true;
            }
        }

        // 2. Direct pure numeric parse (e.g. "1002", "01")
        if (long.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var direct) && direct > 0)
        {
            sequence = direct;
            return true;
        }

        // 3. Match any leading letter prefix followed by digits (e.g. "DS 01", "DJ01", "SRF 1002")
        var match = Regex.Match(normalized, @"^[A-Za-z\s-_]*?(\d+)$");
        if (match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var regexParsed) && regexParsed > 0)
        {
            sequence = regexParsed;
            return true;
        }

        // 4. Extract trailing digits fallback
        var digitsOnly = new string(normalized.Where(char.IsDigit).ToArray());
        if (!string.IsNullOrEmpty(digitsOnly) &&
            long.TryParse(digitsOnly, NumberStyles.None, CultureInfo.InvariantCulture, out var fromDigits) &&
            fromDigits > 0)
        {
            sequence = fromDigits;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Backward-compatible alias for TryParseSequence.
    /// </summary>
    public static bool TryParseBorrowerNumber(string? input, out long sequence)
    {
        return TryParseSequence(input, null, out sequence);
    }

    /// <summary>
    /// Validates user sequence input. Accepts pure numeric digits or full formatted numbers.
    /// Returns true if valid, or false with a corresponding localization error key.
    /// </summary>
    public static bool ValidateSequenceInput(string? input, out long sequence, out string? errorKey)
    {
        sequence = 0;
        errorKey = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            errorKey = "BorrowerNumberRequired";
            return false;
        }

        var ascii = NormalizeDigitsToAscii(input.Trim());
        if (ascii.StartsWith("#"))
        {
            ascii = ascii.TrimStart('#').Trim();
        }

        if (string.IsNullOrWhiteSpace(ascii))
        {
            errorKey = "BorrowerNumberRequired";
            return false;
        }

        // Must match optional letter prefix (e.g. DS, AF) followed by optional whitespace and digits only
        var match = Regex.Match(ascii, @"^[A-Za-z]*\s*(\d+)$");
        if (!match.Success)
        {
            errorKey = "InvalidBorrowerNumber";
            return false;
        }

        if (!long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out sequence))
        {
            errorKey = "InvalidBorrowerNumber";
            return false;
        }

        if (sequence <= 0)
        {
            errorKey = "BorrowerNumberGreaterThanZero";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates borrower number input and produces normalized "{Prefix} {FormattedSequence}" or numeric format.
    /// </summary>
    public static bool ValidateBorrowerNumber(string? input, out string normalized, out string? errorKey)
    {
        return ValidateBorrowerNumber(input, DefaultPrefix, out normalized, out errorKey);
    }

    /// <summary>
    /// Validates borrower number input for a given prefix and produces normalized "{Prefix} {FormattedSequence}".
    /// </summary>
    public static bool ValidateBorrowerNumber(string? input, string prefix, out string normalized, out string? errorKey)
    {
        normalized = string.Empty;
        errorKey = null;

        if (!ValidateSequenceInput(input, out var seq, out errorKey))
        {
            return false;
        }

        normalized = FormatBorrowerNumber(prefix, seq);
        return true;
    }

    /// <summary>
    /// Combines a prefix and a user-entered sequence into the canonical borrower number format.
    /// Example: ("DS", "1002") -> "DS 1002", ("DS", "01") -> "DS 01", ("DS", "DS 1002") -> "DS 1002".
    /// </summary>
    public static string CombinePrefixAndSequence(string prefix, string sequenceOrNumber)
    {
        var cleanPrefix = string.IsNullOrWhiteSpace(prefix) ? DefaultPrefix : prefix.Trim().ToUpperInvariant();
        if (TryParseSequence(sequenceOrNumber, cleanPrefix, out var seq))
        {
            return FormatBorrowerNumber(cleanPrefix, seq);
        }
        return $"{cleanPrefix} {sequenceOrNumber.Trim()}";
    }

    /// <summary>
    /// Normalizes Indic numerals (Gujarati, Devanagari, etc.) to standard ASCII 0-9 digits.
    /// </summary>
    public static string NormalizeDigitsToAscii(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var chars = input.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            // Gujarati digits: ૦ (U+0AE6) to ૯ (U+0AEF)
            if (c >= '\u0AE6' && c <= '\u0AEF')
            {
                chars[i] = (char)('0' + (c - '\u0AE6'));
            }
            // Devanagari / Hindi digits: ० (U+0966) to ९ (U+096F)
            else if (c >= '\u0966' && c <= '\u096F')
            {
                chars[i] = (char)('0' + (c - '\u0966'));
            }
            // Bengali digits: ০ (U+09E6) to ৯ (U+09EF)
            else if (c >= '\u09E6' && c <= '\u09EF')
            {
                chars[i] = (char)('0' + (c - '\u09E6'));
            }
            // Gurmukhi / Punjabi digits: ੦ (U+0A66) to ੯ (U+0A6F)
            else if (c >= '\u0A66' && c <= '\u0A6F')
            {
                chars[i] = (char)('0' + (c - '\u0A66'));
            }
            // Tamil digits: ௦ (U+0BE6) to ௯ (U+0BEF)
            else if (c >= '\u0BE6' && c <= '\u0BEF')
            {
                chars[i] = (char)('0' + (c - '\u0BE6'));
            }
            // Telugu digits: ౦ (U+0C66) to ౯ (U+0C6F)
            else if (c >= '\u0C66' && c <= '\u0C6F')
            {
                chars[i] = (char)('0' + (c - '\u0C66'));
            }
            // Kannada digits: ೦ (U+0CE6) to ೯ (U+0CEF)
            else if (c >= '\u0CE6' && c <= '\u0CEF')
            {
                chars[i] = (char)('0' + (c - '\u0CE6'));
            }
            // Malayalam digits: ൦ (U+0D66) to ൯ (U+0D6F)
            else if (c >= '\u0D66' && c <= '\u0D6F')
            {
                chars[i] = (char)('0' + (c - '\u0D66'));
            }
            // Odia digits: ୦ (U+0B66) to ୯ (U+0B6F)
            else if (c >= '\u0B66' && c <= '\u0B6F')
            {
                chars[i] = (char)('0' + (c - '\u0B66'));
            }
        }
        return new string(chars);
    }
}
