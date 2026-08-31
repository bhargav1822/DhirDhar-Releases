using System;
using System.Globalization;
using DhirDhar.Application.Localization;

namespace DhirDhar.Infrastructure.Localization;

public sealed class DateLocalizationService : IDateLocalizationService
{
    private readonly ILocalizationService? _localizationService;
    private CultureInfo _culture;
    private string _customPattern = "dd-MM-yyyy";

    public DateLocalizationService(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        _culture = _localizationService.GetCulture();
        _localizationService.LanguageChanged += OnLanguageChanged;
    }

    public DateLocalizationService()
    {
        _culture = CultureInfo.CurrentCulture;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_localizationService != null)
        {
            _culture = _localizationService.GetCulture();
        }
    }

    public string DateFormatPattern => _customPattern;

    public void SetCulture(CultureInfo culture)
    {
        _culture = culture ?? CultureInfo.CurrentCulture;
    }

    public void SetDateFormatPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return;

        _customPattern = pattern.ToUpperInvariant() switch
        {
            "DD/MM/YYYY" => "dd/MM/yyyy",
            "YYYY-MM-DD" => "yyyy-MM-dd",
            _ => "dd-MM-yyyy"
        };
    }

    public string FormatShortDate(DateTime date)
    {
        var text = date.ToString(_customPattern, _culture);
        return _localizationService?.LocalizeDigits(text) ?? text;
    }

    public string FormatShortDate(DateTime? date)
    {
        return date.HasValue ? FormatShortDate(date.Value) : string.Empty;
    }

    public string FormatLongDate(DateTime date)
    {
        var text = date.ToString("D", _culture);
        return _localizationService?.LocalizeDigits(text) ?? text;
    }

    public string FormatLongDate(DateTime? date)
    {
        return date.HasValue ? FormatLongDate(date.Value) : string.Empty;
    }

    public string FormatMonthYear(DateTime date)
    {
        var text = date.ToString("MMMM yyyy", _culture);
        return _localizationService?.LocalizeDigits(text) ?? text;
    }

    public string FormatDateRange(DateTime startDate, DateTime endDate)
    {
        return $"{FormatLongDate(startDate)} – {FormatLongDate(endDate)}";
    }

    public string FormatDateTime(DateTime dateTime)
    {
        var text = dateTime.ToString("f", _culture);
        return _localizationService?.LocalizeDigits(text) ?? text;
    }

    public string GetMonthName(int month)
    {
        return _culture.DateTimeFormat.GetMonthName(month);
    }

    public string GetDayName(DayOfWeek dayOfWeek)
    {
        return _culture.DateTimeFormat.GetDayName(dayOfWeek);
    }

    public string ToLocalizedNumber(long number)
    {
        var text = number.ToString(_culture);
        return _localizationService?.LocalizeDigits(text) ?? text;
    }

    public string ToLocalizedNumber(decimal number)
    {
        var text = number.ToString("N2", _culture);
        return _localizationService?.LocalizeDigits(text) ?? text;
    }
}
