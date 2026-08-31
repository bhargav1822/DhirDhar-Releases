using System;
using System.Threading.Tasks;
using DhirDhar.Desktop.ViewModels.License;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Views.License;

public sealed partial class LicenseRenewalDialog : ContentDialog
{
    public LicenseViewModel ViewModel { get; }

    public LicenseRenewalDialog(LicenseViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        InitializeComponent();
        Bindings.Update();

        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var success = await ViewModel.ExecuteActivateAsync();
            if (!success)
            {
                // Cancel dialog closing so the user can correct the key
                args.Cancel = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
