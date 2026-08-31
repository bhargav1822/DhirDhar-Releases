using System;
using System.Threading.Tasks;
using DhirDhar.Desktop.ViewModels.Borrowers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DhirDhar.Desktop.Views.Borrowers;

public sealed partial class BorrowerDetailsPage : Page
{
    public BorrowerDetailsPage()
    {
        InitializeComponent();
    }

    public BorrowerDetailsViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(BorrowerDetailsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is BorrowerDetailsViewModel viewModel)
        {
            SetViewModel(viewModel);
        }
    }

    public async Task LoadBorrowerDetailsAsync(Guid borrowerId)
    {
        if (ViewModel != null)
        {
            ViewModel.BorrowerId = borrowerId;
            await ViewModel.LoadAsync();
        }
    }
}
