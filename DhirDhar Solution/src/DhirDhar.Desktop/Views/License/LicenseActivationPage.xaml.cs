using System;
using System.Threading.Tasks;
using DhirDhar.Desktop.ViewModels.License;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DhirDhar.Desktop.Views.License;

public sealed partial class LicenseActivationPage : Page
{
    public LicenseViewModel ViewModel { get; private set; } = null!;

    public LicenseActivationPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is LicenseViewModel vm)
        {
            ViewModel = vm;
        }
        else if (App.ServiceProvider != null)
        {
            ViewModel = App.ServiceProvider.GetRequiredService<LicenseViewModel>();
        }

        DataContext = ViewModel;
        Bindings.Update();

        if (ViewModel != null)
        {
            ViewModel.ActivationSucceeded += OnActivationSucceeded;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (ViewModel != null)
        {
            ViewModel.ActivationSucceeded -= OnActivationSucceeded;
        }
    }

    private async void OnActivationSucceeded()
    {
        // Brief pause for visual confirmation before navigating to the main shell
        await Task.Delay(600);

        DispatcherQueue.TryEnqueue(() =>
        {
            if (App.MainWindow is MainWindow mainWindow && App.ServiceProvider != null)
            {
                mainWindow.NavigateToMainShell(App.ServiceProvider);
            }
        });
    }
}
