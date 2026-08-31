using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DhirDhar.Domain.Common;

/// <summary>
/// Helper utilities for Business Profile prefix generation and borrower number formatting.
/// </summary>
public static class BusinessProfileHelper
{
    public const string DefaultBusinessName = BorrowerNumberHelper.DefaultBusinessName;
    public const string DefaultPrefix = BorrowerNumberHelper.DefaultPrefix;

    /// <summary>
    /// Generates a standardized uppercase prefix from a business name.
    /// Rules:
    /// - Multi-word: Use the first letter of each word (e.g., "DhirDhar Solution" -> "DS", "ABC Finance" -> "AF", "Shree Ram Finance" -> "SRF").
    /// - Single-word: Use the first two characters in uppercase (e.g., "Dwiti" -> "DW").
    /// </summary>
    public static string GeneratePrefix(string? businessName)
    {
        return BorrowerNumberHelper.GeneratePrefixFromBusinessName(businessName);
    }

    /// <summary>
    /// Formats a sequential number with the prefix in the standardized format "{Prefix} {Sequence}".
    /// 1 -> "DS 01", 9 -> "DS 09", 10 -> "DS 10", 99 -> "DS 99", 100 -> "DS 100", 1002 -> "DS 1002".
    /// </summary>
    public static string FormatBorrowerNumber(string prefix, long sequenceNumber)
    {
        return BorrowerNumberHelper.FormatBorrowerNumber(prefix, sequenceNumber);
    }

    /// <summary>
    /// Tries to parse the sequential number from a formatted borrower number for a given prefix.
    /// Example: "DS 01" with prefix "DS" -> returns 1. "DS 1002" with prefix "DS" -> returns 1002.
    /// </summary>
    public static bool TryParseSequenceNumber(string? borrowerNumber, string? prefix, out long sequenceNumber)
    {
        return BorrowerNumberHelper.TryParseSequence(borrowerNumber, prefix, out sequenceNumber);
    }

    /// <summary>
    /// Extracts the sequential numeric component from a borrower number, returning 0 if non-numeric.
    /// </summary>
    public static long ExtractSequenceNumber(string? borrowerNumber, string? prefix = null)
    {
        return TryParseSequenceNumber(borrowerNumber, prefix, out var seq) ? seq : 0;
    }
}
