using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using DhirDhar.Application.Backup;
using DhirDhar.Application.Localization;
using DhirDhar.Desktop;
using Microsoft.Extensions.DependencyInjection;

namespace DhirDhar.Desktop.Converters;

public sealed class FileSizeConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        long? bytes = null;
        if (value is long l) bytes = l;
        else if (value is int i) bytes = i;
        else if (value is double d) bytes = (long)d;
        else if (value is string s && long.TryParse(s, out var parsed)) bytes = parsed;
        else if (value is null) bytes = null;

        var sp = App.ServiceProvider;
        var loc = sp?.GetService<ILocalizationService>();

        return FileSizeFormatter.Format(bytes, loc);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value ?? DependencyProperty.UnsetValue;
}

public sealed class StateToBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ViewModels.StartupState state)
        {
            var resourceKey = state switch
            {
                ViewModels.StartupState.Failed => "ErrorBrush",
                ViewModels.StartupState.Ready => "SuccessBrush",
                _ => "PrimaryBrush"
            };

            if (App.Current.Resources.TryGetValue(resourceKey, out var resource) && resource is SolidColorBrush solidBrush)
            {
                return new SolidColorBrush(solidBrush.Color);
            }
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class DigitLocalizingConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null) return string.Empty;
        var text = value is string s ? s : value.ToString() ?? string.Empty;
        var sp = App.ServiceProvider;
        if (sp == null) return text;
        var loc = sp.GetService<ILocalizationService>();
        return loc?.LocalizeDigits(text) ?? text;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value ?? DependencyProperty.UnsetValue;
}

public sealed class LocalizedNumberConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null) return "-";
        string text;
        if (value is string s)
        {
            text = s;
        }
        else if (value is IFormattable fmt)
        {
            var format = parameter?.ToString() ?? "G";
            text = fmt.ToString(format, CultureInfo.InvariantCulture) ?? "-";
        }
        else
        {
            text = value.ToString() ?? "-";
        }

        var sp = App.ServiceProvider;
        if (sp == null) return text;
        var loc = sp.GetService<ILocalizationService>();
        return loc?.LocalizeDigits(text) ?? text;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value ?? DependencyProperty.UnsetValue;
}

public sealed class StateToVisibilityConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is ViewModels.StartupState state && parameter is string targetState)
        {
            var matches = state.ToString().Equals(targetState, StringComparison.OrdinalIgnoreCase);
            return matches ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        bool boolValue = value is bool b && b;
        if (targetType == typeof(bool))
        {
            return boolValue;
        }
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        if (value is bool b)
        {
            return b;
        }
        return false;
    }
}

public sealed class StringToVisibilityConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        return !string.IsNullOrEmpty(value?.ToString()) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class SidebarLengthConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var expanded = value is bool b && b;
        return (parameter as string) switch
        {
            "SidebarWidth" => expanded ? new GridLength(252) : new GridLength(72),
            "NavIcon" => expanded ? new GridLength(40) : new GridLength(1, GridUnitType.Star),
            "NavGap" => expanded ? new GridLength(12) : new GridLength(0),
            "NavText" => expanded ? new GridLength(1, GridUnitType.Star) : new GridLength(0),
            "CardLogoCol" => expanded ? GridLength.Auto : new GridLength(1, GridUnitType.Star),
            "CardTextCol" => expanded ? new GridLength(1, GridUnitType.Star) : new GridLength(0),
            "CardOptionsCol" => expanded ? GridLength.Auto : new GridLength(0),
            _ => new GridLength(0)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class SidebarThicknessConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var expanded = value is bool b && b;
        return (parameter as string) switch
        {
            "HeaderPadding" => expanded ? new Thickness(16, 16, 16, 8) : new Thickness(16, 16, 16, 8),
            "NavPadding" => expanded ? new Thickness(16, 0, 16, 0) : new Thickness(0),
            "CardMargin" => expanded ? new Thickness(16, 8, 16, 16) : new Thickness(16, 8, 16, 16),
            "CardPadding" => expanded ? new Thickness(12, 8, 12, 8) : new Thickness(0, 8, 0, 8),
            "LogoMargin" => expanded ? new Thickness(0, 0, 8, 0) : new Thickness(0),
            _ => new Thickness(0)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class SidebarDoubleConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var expanded = value is bool b && b;
        return (parameter as string) switch
        {
            "LogoSize" => expanded ? 42.0 : 36.0,
            _ => expanded ? 42.0 : 36.0
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class SidebarAlignmentConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var expanded = value is bool b && b;
        return expanded ? HorizontalAlignment.Left : HorizontalAlignment.Center;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        bool boolVal = false;
        if (value is bool b) boolVal = b;
        else if (value is Visibility v) boolVal = (v == Visibility.Visible);

        bool inverted = !boolVal;

        if (targetType == typeof(Visibility))
        {
            return inverted ? Visibility.Visible : Visibility.Collapsed;
        }

        return inverted;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        bool boolVal = false;
        if (value is bool b) boolVal = b;
        else if (value is Visibility v) boolVal = (v == Visibility.Visible);

        bool inverted = !boolVal;

        if (targetType == typeof(Visibility))
        {
            return inverted ? Visibility.Visible : Visibility.Collapsed;
        }

        return inverted;
    }
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        bool boolVal = false;
        if (value is bool b) boolVal = b;
        else if (value is Visibility v) boolVal = (v == Visibility.Visible);

        bool inverted = !boolVal;

        if (targetType == typeof(bool))
        {
            return inverted;
        }

        return inverted ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        if (value is bool b)
        {
            return !b;
        }

        return true;
    }
}

public sealed class ConnectionStatusConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var connected = value is bool boolValue && boolValue;

        var serviceProvider = App.ServiceProvider;
        if (serviceProvider != null)
        {
            var localizationService = serviceProvider.GetService<ILocalizationService>();
            if (localizationService != null)
            {
                return localizationService.GetString(connected ? "Connected" : "NotConnected");
            }
        }

        return connected ? "Connected" : "Not Connected";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class StringFormatConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var format = parameter as string;
        if (value == null)
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(format))
        {
            return value.ToString();
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, value);
        }
        catch (FormatException)
        {
            return value.ToString();
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class DateTimeFormatConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var serviceProvider = App.ServiceProvider;
        if (serviceProvider == null)
        {
            return value?.ToString() ?? string.Empty;
        }

        var dateService = serviceProvider.GetService<IDateLocalizationService>();
        var localization = serviceProvider.GetService<ILocalizationService>();
        var style = parameter?.ToString() ?? string.Empty;

        string Localize(string s) => localization?.LocalizeDigits(s) ?? s;

        if (value is DateTime dt)
        {
            if (dateService == null) return Localize(dt.ToString());
            return Localize(style.ToLowerInvariant() switch
            {
                "short" => dateService.FormatShortDate(dt),
                "long" => dateService.FormatLongDate(dt),
                "monthyear" => dateService.FormatMonthYear(dt),
                _ => dateService.FormatDateTime(dt)
            });
        }

        if (value is DateTimeOffset dto)
        {
            if (dateService == null) return Localize(dto.ToString());
            return Localize(style.ToLowerInvariant() switch
            {
                "short" => dateService.FormatShortDate(dto.DateTime),
                "long" => dateService.FormatLongDate(dto.DateTime),
                "monthyear" => dateService.FormatMonthYear(dto.DateTime),
                _ => dateService.FormatDateTime(dto.DateTime)
            });
        }

        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class BorrowerFilterVisibilityConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string reportType)
        {
            return reportType.Equals("BorrowerStatement", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class TransactionFilterVisibilityConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string reportType)
        {
            return reportType.Equals("TransactionReport", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class StatusToBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        string text = value?.ToString()?.Trim() ?? string.Empty;
        var resourceKey = text switch
        {
            // Direct brush keys
            "SuccessBrush" => "SuccessBrush",
            "ErrorBrush" => "ErrorBrush",
            "PrimaryBrush" => "PrimaryBrush",
            "WarningBrush" => "WarningBrush",
            "SecondaryBrush" => "SecondaryBrush",

            // Principal / Initial Loan Amount / Deposit / Payment Received -> GREEN (SuccessBrush)
            "Active" or "Principal" or "Principal Balance" or "Loan Amount" or "Initial Loan Amount" or "Initial Loan"
                or "Deposit" or "Received" or "Receive" or "Payment Received" or "Received Amount" or "Total Received"
                or "Total Deposits" or "Payments"
                or "જમા" or "પ્રારંભિક લોન રકમ" or "પ્રારંભિક લોન" or "મળેલ" or "મળેલ ચુકવણી" or "મળેલ રકમ" or "કુલ જમા રકમ" or "સક્રિય" or "મૂળ રકમ"
                or "जमा" or "प्रारंभिक ऋण राशि" or "प्राप्त" or "सक्रिय" => "SuccessBrush",

            // Withdrawal / Amount Given / Given Amount / Account Closed -> RED (ErrorBrush)
            "Closed" or "Withdrawal" or "Given" or "Give" or "Amount Given" or "Given Amount"
                or "Total Withdrawals" or "New Loans"
                or "ઉપાડ" or "આપેલ" or "આપેલ રકમ" or "કુલ ઉપાડ" or "ખાતું બંધ છે" or "બંધ"
                or "निकासी" or "दिया" or "दी गई राशि" or "खाता बंद है" => "ErrorBrush",

            // Interest / Interest Accrued -> BLUE (PrimaryBrush)
            "Interest" or "Accrued" or "Interest Amount" or "Interest Rate" or "Accrued Interest"
                or "Interest Accrued" or "Percent"
                or "વ્યાજ" or "ઉમેરેલ વ્યાજ" or "વ્યાજ રકમ" or "વ્યાજ દર" or "ટકા"
                or "ब्याज" or "अर्जित ब्याज" or "प्रतिशत" => "PrimaryBrush",

            "Inactive" => "WarningBrush",
            "Archived" => "SecondaryBrush",
            _ => "SubtleForegroundBrush"
        };

        var isBadgeBackground = parameter?.ToString()?.Equals("bg", StringComparison.OrdinalIgnoreCase) == true;

        if (App.Current.Resources.TryGetValue(resourceKey, out var resource) && resource is SolidColorBrush solidBrush)
        {
            if (isBadgeBackground)
            {
                var color = solidBrush.Color;
                return new SolidColorBrush(Windows.UI.Color.FromArgb(0x26, color.R, color.G, color.B));
            }

            return new SolidColorBrush(solidBrush.Color);
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class BoolToTabBackgroundConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        bool isSelected = value is bool b && b;
        return isSelected
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 99, 235))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class BoolToTabForegroundConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        bool isSelected = value is bool b && b;
        return isSelected
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 41, 59));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class BoolToTabBorderBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        bool isSelected = value is bool b && b;
        return isSelected
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 99, 235))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 232, 240));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class DashToSubtleBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        string text = value?.ToString()?.Trim() ?? string.Empty;
        string param = parameter as string ?? string.Empty;

        if (string.IsNullOrEmpty(text) || text == "—" || text == "-" || text == "–" || text == "₹ 0.00" || text == "₹0.00" || text == "0.00" || text == "0")
        {
            if (App.Current.Resources.TryGetValue("SubtleForegroundBrush", out var subtleRes) && subtleRes is SolidColorBrush subtleBrush)
            {
                return new SolidColorBrush(subtleBrush.Color);
            }
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 148, 163, 184));
        }

        string resourceKey = param switch
        {
            "ErrorBrush" or "Withdrawal" or "Given" or "Give" or "Red" or "ઉપાડ" or "આપેલ" => "ErrorBrush",
            "SuccessBrush" or "Deposit" or "Received" or "Receive" or "Principal" or "Green" or "જમા" or "મળેલ" => "SuccessBrush",
            "PrimaryBrush" or "Interest" or "Accrued" or "Blue" or "વ્યાજ" => "PrimaryBrush",
            _ => param
        };

        if (App.Current.Resources.TryGetValue(resourceKey, out var res) && res is SolidColorBrush resBrush)
        {
            return new SolidColorBrush(resBrush.Color);
        }

        if (string.Equals(resourceKey, "ErrorBrush", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 67, 53));
        }
        if (string.Equals(resourceKey, "SuccessBrush", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 168, 83));
        }
        if (string.Equals(resourceKey, "PrimaryBrush", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 26, 115, 232));
        }

        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 41, 59));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        string? path = value?.ToString();
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
            {
                return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
            }
            if (System.IO.File.Exists(path))
            {
                return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path));
            }
        }
        catch
        {
            // Ignore invalid paths safely
        }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class StatusToLocalizedTextConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        string text = value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return text;

        var serviceProvider = App.ServiceProvider;
        if (serviceProvider != null)
        {
            var localization = serviceProvider.GetService<ILocalizationService>();
            if (localization != null)
            {
                return localization.GetString(text);
            }
        }

        return text;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class TextToLocalizedTextConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        string text = value?.ToString() ?? parameter?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return text;

        try
        {
            var serviceProvider = App.ServiceProvider;
            if (serviceProvider != null)
            {
                var localization = serviceProvider.GetService<ILocalizationService>();
                if (localization != null)
                {
                    return localization.LocalizeText(text);
                }
            }
        }
        catch
        {
            // Graceful fallback to original text
        }

        return text;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value ?? DependencyProperty.UnsetValue;
    }
}

public sealed class SeverityToBackgroundBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var severity = value switch
        {
            DhirDhar.Application.Validation.Models.IntegritySeverityLevel lvl => lvl,
            string s when Enum.TryParse<DhirDhar.Application.Validation.Models.IntegritySeverityLevel>(s, out var parsed) => parsed,
            _ => DhirDhar.Application.Validation.Models.IntegritySeverityLevel.Info
        };

        var color = severity switch
        {
            DhirDhar.Application.Validation.Models.IntegritySeverityLevel.Critical => Windows.UI.Color.FromArgb(38, 239, 68, 68),
            DhirDhar.Application.Validation.Models.IntegritySeverityLevel.High => Windows.UI.Color.FromArgb(38, 249, 115, 22),
            DhirDhar.Application.Validation.Models.IntegritySeverityLevel.Warning => Windows.UI.Color.FromArgb(38, 245, 158, 11),
            _ => Windows.UI.Color.FromArgb(38, 59, 130, 246)
        };

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
}

public sealed class SeverityToForegroundBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var severity = value switch
        {
            DhirDhar.Application.Validation.Models.IntegritySeverityLevel lvl => lvl,
            string s when Enum.TryParse<DhirDhar.Application.Validation.Models.IntegritySeverityLevel>(s, out var parsed) => parsed,
            _ => DhirDhar.Application.Validation.Models.IntegritySeverityLevel.Info
        };

        var color = severity switch
        {
            DhirDhar.Application.Validation.Models.IntegritySeverityLevel.Critical => Windows.UI.Color.FromArgb(255, 239, 68, 68),
            DhirDhar.Application.Validation.Models.IntegritySeverityLevel.High => Windows.UI.Color.FromArgb(255, 249, 115, 22),
            DhirDhar.Application.Validation.Models.IntegritySeverityLevel.Warning => Windows.UI.Color.FromArgb(255, 245, 158, 11),
            _ => Windows.UI.Color.FromArgb(255, 59, 130, 246)
        };

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
}

