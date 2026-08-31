using System;
using System.ComponentModel;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Application.QrCode;
using DhirDhar.Desktop.ViewModels.Borrowers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Views.Borrowers;

public sealed partial class BorrowersPage : Page
{
    public BorrowersPage()
    {
        InitializeComponent();
    }

    public BorrowersViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(BorrowersViewModel viewModel)
    {
        if (ViewModel != null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.RequestScanQr = null;
        }

        ViewModel = viewModel;
        ViewModel.RequestScanQr = OpenScanQrDialogAsync;
        DataContext = viewModel;
        Bindings.Update();
        UpdateFilterSelection(ViewModel.CurrentFilter);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public async Task OpenScanQrDialogAsync()
    {
        var borrowerService = App.ServiceProvider?.GetService<IBorrowerService>();
        var qrCodeService = App.ServiceProvider?.GetService<IQrCodeService>();
        var localizationService = App.ServiceProvider?.GetService<ILocalizationService>();

        if (borrowerService == null || qrCodeService == null || localizationService == null)
        {
            return;
        }

        var dialog = new ScanQrDialog(borrowerService, qrCodeService, localizationService)
        {
            XamlRoot = this.XamlRoot
        };

        await dialog.ShowAsync();

        if (dialog.ScannedBorrower != null)
        {
            ViewModel?.SelectBorrowerCommand.Execute(dialog.ScannedBorrower.Id);
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is BorrowersViewModel viewModel)
        {
            SetViewModel(viewModel);
        }
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        if (ViewModel != null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    public async Task LoadBorrowersAsync()
    {
        if (ViewModel != null)
        {
            await ViewModel.LoadAsync();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BorrowersViewModel.CurrentFilter))
        {
            UpdateFilterSelection(ViewModel.CurrentFilter);
        }
    }

    private void UpdateFilterSelection(BorrowerFilter filter)
    {
        VisualStateManager.GoToState(AllFilterButton, filter == BorrowerFilter.All ? "Selected" : "Unselected", true);
        VisualStateManager.GoToState(ActiveFilterButton, filter == BorrowerFilter.Active ? "Selected" : "Unselected", true);
        VisualStateManager.GoToState(ClosedFilterButton, filter == BorrowerFilter.Closed ? "Selected" : "Unselected", true);
    }

    private void SearchTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            ViewModel?.SearchCommand.Execute(null);
        }
    }
}
