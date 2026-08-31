using System;
using System.Threading.Tasks;
using DhirDhar.Application.Localization;
using DhirDhar.Desktop.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DhirDhar.Desktop.Views.Settings;

public sealed partial class SettingsPage : Page
{
    private readonly ILocalizationService? _localization;

    public SettingsPage()
    {
        try
        {
            var sp = App.ServiceProvider;
            if (sp != null)
            {
                var vm = sp.GetService<SettingsViewModel>();
                if (vm != null)
                {
                    ViewModel = vm;
                    DataContext = vm;
                }
            }
        }
        catch
        {
        }

        if (ViewModel == null)
        {
            try
            {
                ViewModel = new SettingsViewModel();
                DataContext = ViewModel;
            }
            catch
            {
            }
        }

        InitializeComponent();
        _localization = App.ServiceProvider?.GetService<ILocalizationService>();
    }

    public SettingsViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        try
        {
            Bindings.Update();
        }
        catch
        {
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is SettingsViewModel viewModel)
        {
            SetViewModel(viewModel);
        }
        else if (ViewModel == null)
        {
            try
            {
                var vm = App.ServiceProvider?.GetService<SettingsViewModel>() ?? new SettingsViewModel();
                SetViewModel(vm);
            }
            catch
            {
            }
        }

        if (ViewModel != null)
        {
            _ = ViewModel.LoadAsync();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        try
        {
            ViewModel?.CancelPendingOperations();
        }
        catch
        {
        }
    }

    private string L(string key) => _localization?.GetString(key) ?? key;

    private async void OnResetSettingsClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var dialog = new ContentDialog
        {
            Title = L("ResetSettingsTitle"),
            Content = L("ResetSettingsConfirm"),
            PrimaryButtonText = L("Reset"),
            CloseButtonText = L("Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.ResetSettingsAsync();
        }
    }

    private async void OnRenewLicenseClick(object sender, RoutedEventArgs e)
    {
        if (App.ServiceProvider == null) return;
        var licenseVm = App.ServiceProvider.GetRequiredService<DhirDhar.Desktop.ViewModels.License.LicenseViewModel>();
        var dialog = new DhirDhar.Desktop.Views.License.LicenseRenewalDialog(licenseVm)
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
        ViewModel?.RefreshLicenseState();
    }
}
