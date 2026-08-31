using System.Threading.Tasks;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Desktop.ViewModels.Reports;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Views.Reports;

public sealed partial class ReportsPage : Page
{
    public ReportsPage()
    {
        InitializeComponent();

        BorrowerSearchBox.TextChanged += (sender, args) =>
        {
            if (args.Reason.ToString() == "UserInput")
            {
                ViewModel?.SearchBorrowers(sender.Text);
            }
        };

        BorrowerSearchBox.SuggestionChosen += (sender, args) =>
        {
            if (args.SelectedItem is BorrowerSummary borrower)
            {
                ViewModel?.SelectBorrower(borrower);
            }
        };

        BorrowerSearchBox.QuerySubmitted += (sender, args) =>
        {
            if (args.ChosenSuggestion is BorrowerSummary borrower)
            {
                ViewModel?.SelectBorrower(borrower);
            }
            else if (ViewModel?.SearchResults.Count > 0)
            {
                ViewModel?.SelectBorrower(ViewModel.SearchResults[0]);
            }
        };

        Loaded += async (s, e) =>
        {
            await LoadReportsPageAsync();
        };
    }

    public ReportsViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(ReportsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        Bindings.Update();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ReportsViewModel viewModel)
        {
            SetViewModel(viewModel);
        }
    }

    public async Task LoadReportsPageAsync()
    {
        if (ViewModel != null)
        {
            await ViewModel.LoadBorrowersAsync();
        }
    }
}
