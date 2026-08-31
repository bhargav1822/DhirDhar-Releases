namespace DhirDhar.Application.Localization;

public interface ILocalizationService
{
    string GetString(string key, string? languageCode = null);
    string LocalizeText(string? text);
    string LocalizeText(string? text, string languageCode);
    string FormatInterestDescription(DateTime startDate, DateTime endDate, string? languageCode = null);
    string LocalizeDigits(string? value);
    string ToLocalizedCurrency(decimal amount);
    string ToLocalizedCurrency(decimal amount, bool negative);
    string ToLocalizedDecimal(decimal amount, string format = "N2");
    string ToLocalizedInteger(long value);
    string ToLocalizedDate(DateTime value, string format = "dd-MM-yyyy");
    string ToLocalizedDateTime(DateTime value, string format = "g");
    string ToLocalizedTime(DateTime value, string format = "hh:mm:ss tt");
    string ToLocalizedPercentage(decimal value, string format = "N2");
    string CurrentLanguage { get; }
    event EventHandler? LanguageChanged;
    void SetLanguage(string languageCode);
    IReadOnlyList<SupportedLanguage> SupportedLanguages { get; }
    System.Globalization.CultureInfo GetCulture();
}

public sealed record SupportedLanguage(string Code, string NativeName, string EnglishName);
