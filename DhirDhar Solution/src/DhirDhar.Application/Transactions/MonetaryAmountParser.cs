using System;
using System.Globalization;
using System.Text;

namespace DhirDhar.Application.Transactions;

/// <summary>
/// Provides safe decimal parsing for Indian Rupee monetary amounts.
/// Does not use floating-point double arithmetic.
/// </summary>
public static class MonetaryAmountParser
{
    private static readonly string[] CurrencyPrefixes = { "₹", "Rs.", "Rs", "INR", "inr" };

    /// <summary>
    /// Safely parses an Indian Rupee or standard monetary string into a positive decimal amount.
    /// Supports inputs like 1, 100, 25000, 25000.50, 100000.00, formatted with commas (25,000, 1,00,000),
    /// currency symbols (₹25000), and localized Gujarati/Hindi digits.
    /// Rejects empty, 0, negative, and non-numeric inputs.
    /// </summary>
    public static bool TryParse(string? input, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();

        // Strip known currency prefixes if present at the beginning
        foreach (var prefix in CurrencyPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(prefix.Length).Trim();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Reject negative values or negative signs
        if (text.StartsWith("-") || text.Contains('-'))
        {
            return false;
        }

        if (text.StartsWith("+"))
        {
            text = text.Substring(1).Trim();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Normalize digits and validate that only valid monetary characters exist
        var sb = new StringBuilder(text.Length);
        int dotCount = 0;
        int digitCount = 0;

        foreach (var c in text)
        {
            if (c >= '0' && c <= '9')
            {
                sb.Append(c);
                digitCount++;
            }
            else if (c >= '૦' && c <= '૯')
            {
                sb.Append((char)('0' + (c - '૦')));
                digitCount++;
            }
            else if (c >= '०' && c <= '९')
            {
                sb.Append((char)('0' + (c - '०')));
                digitCount++;
            }
            else if (c == '.')
            {
                dotCount++;
                if (dotCount > 1) return false;
                sb.Append('.');
            }
            else if (c == ',' || c == ' ')
            {
                // Commas and spaces as grouping/thousands separators are skipped
                continue;
            }
            else
            {
                // Any other character (letters, unexpected symbols) causes rejection
                return false;
            }
        }

        if (digitCount == 0)
        {
            return false;
        }

        var cleaned = sb.ToString();
        if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsedValue))
        {
            return false;
        }

        if (parsedValue <= 0m)
        {
            return false;
        }

        amount = decimal.Round(parsedValue, 2, MidpointRounding.AwayFromZero);
        return amount > 0m;
    }
}
