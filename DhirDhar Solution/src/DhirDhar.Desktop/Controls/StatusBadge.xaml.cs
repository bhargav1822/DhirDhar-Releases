using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DhirDhar.Desktop;

namespace DhirDhar.Desktop.Controls;

public sealed partial class StatusBadge : UserControl
{
    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(string), typeof(StatusBadge), new PropertyMetadata(string.Empty, OnStatusChanged));

    public static readonly DependencyProperty ColorBrushProperty =
        DependencyProperty.Register(nameof(ColorBrush), typeof(Brush), typeof(StatusBadge), new PropertyMetadata(null));

    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public Brush? ColorBrush
    {
        get => (Brush?)GetValue(ColorBrushProperty);
        set => SetValue(ColorBrushProperty, value);
    }

    public StatusBadge()
    {
        InitializeComponent();
        UpdateColor();
    }

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusBadge badge)
        {
            badge.UpdateColor();
        }
    }

    private void UpdateColor()
    {
        ColorBrush = Status?.ToLowerInvariant() switch
        {
            "active" => App.Current.Resources["SuccessBrush"] as Brush,
            "inactive" => App.Current.Resources["WarningBrush"] as Brush,
            "closed" => App.Current.Resources["ErrorBrush"] as Brush,
            "archived" => App.Current.Resources["SubtleForegroundBrush"] as Brush,
            _ => App.Current.Resources["PrimaryBrush"] as Brush
        };
    }
}
