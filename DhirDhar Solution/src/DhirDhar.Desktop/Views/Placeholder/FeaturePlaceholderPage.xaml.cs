using DhirDhar.Application.Localization;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace DhirDhar.Desktop.Views.Placeholder;

public sealed partial class FeaturePlaceholderPage : Page
{
    private readonly ILocalizationService? _localization;

    public FeaturePlaceholderPage()
    {
        InitializeComponent();
        _localization = App.ServiceProvider?.GetService<ILocalizationService>();
        FeatureTitle = string.Empty;
        UpdateLocalizedText();
    }

    public FeaturePlaceholderPage(string featureTitle) : this()
    {
        FeatureTitle = L(featureTitle);
    }

    public string FeatureTitle { get; }

    private string L(string key) => _localization?.GetString(key) ?? key;

    private void UpdateLocalizedText()
    {
        ComingSoonText.Text = L("FeatureComingSoon");
        FoundationReadyText.Text = L("FeatureFoundationReady");
    }
}
