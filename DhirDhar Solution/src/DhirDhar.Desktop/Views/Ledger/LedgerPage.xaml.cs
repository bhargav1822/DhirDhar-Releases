using System;
using System.Threading.Tasks;
using DhirDhar.Desktop.ViewModels.Ledger;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Views.Ledger;

public sealed partial class LedgerPage : Page
{
    public LedgerPage()
    {
        InitializeComponent();
    }

    public LedgerViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(LedgerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        Bindings.Update();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is LedgerViewModel viewModel)
        {
            SetViewModel(viewModel);
        }
    }

    public async Task LoadLedgerAsync(Guid borrowerId)
    {
        if (ViewModel != null)
        {
            ViewModel.BorrowerId = borrowerId;
            await ViewModel.LoadAsync();
        }
    }

    private async void SearchTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            if (ViewModel != null)
            {
                await ViewModel.LoadAsync();
            }
        }
    }
}
