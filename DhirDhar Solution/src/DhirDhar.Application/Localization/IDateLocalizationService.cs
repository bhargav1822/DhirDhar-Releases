namespace DhirDhar.Application.Localization;

public interface IDateLocalizationService
{
    string DateFormatPattern { get; }
    void SetDateFormatPattern(string pattern);
    string FormatShortDate(DateTime date);
    string FormatShortDate(DateTime? date);
    string FormatLongDate(DateTime date);
    string FormatLongDate(DateTime? date);
    string FormatMonthYear(DateTime date);
    string FormatDateRange(DateTime startDate, DateTime endDate);
    string FormatDateTime(DateTime dateTime);
    string GetMonthName(int month);
    string GetDayName(DayOfWeek dayOfWeek);
    string ToLocalizedNumber(long number);
    string ToLocalizedNumber(decimal number);
}
