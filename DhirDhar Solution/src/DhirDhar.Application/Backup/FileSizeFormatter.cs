using System;
using System.Globalization;
using DhirDhar.Application.Localization;

namespace DhirDhar.Application.Backup;

public static class FileSizeFormatter
{
    private const long OneKB = 1024L;
    private const long OneMB = 1024L * 1024L;
    private const long OneGB = 1024L * 1024L * 1024L;
    private const long OneTB = 1024L * 1024L * 1024L * 1024L;

    public const string DefaultUnknown = "Unknown";

    public static string Format(long? bytes, ILocalizationService? localizationService = null)
    {
        if (!bytes.HasValue || bytes.Value < 0)
        {
            return localizationService?.GetString("Unknown") ?? DefaultUnknown;
        }

        return Format(bytes.Value, localizationService);
    }

    public static string Format(long bytes, ILocalizationService? localizationService = null)
    {
        if (bytes < 0)
        {
            return localizationService?.GetString("Unknown") ?? DefaultUnknown;
        }

        string rawFormatted;
        if (bytes < OneKB)
        {
            rawFormatted = $"{bytes} B";
        }
        else if (bytes < OneMB)
        {
            double kb = (double)bytes / OneKB;
            rawFormatted = $"{FormatNumber(kb)} KB";
        }
        else if (bytes < OneGB)
        {
            double mb = (double)bytes / OneMB;
            rawFormatted = $"{FormatNumber(mb)} MB";
        }
        else if (bytes < OneTB)
        {
            double gb = (double)bytes / OneGB;
            rawFormatted = $"{FormatNumber(gb)} GB";
        }
        else
        {
            double tb = (double)bytes / OneTB;
            rawFormatted = $"{FormatNumber(tb)} TB";
        }

        if (localizationService != null)
        {
            return localizationService.LocalizeDigits(rawFormatted);
        }

        return rawFormatted;
    }

    private static string FormatNumber(double value)
    {
        // Up to 2 decimals, trimming unnecessary trailing zeros (e.g. 1.0 -> 1, 1.50 -> 1.5, 12.45 -> 12.45)
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
