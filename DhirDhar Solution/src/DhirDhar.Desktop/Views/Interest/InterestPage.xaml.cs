using System.Threading.Tasks;
using DhirDhar.Desktop.ViewModels.Interest;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Views.Interest;

public sealed partial class InterestPage : Page
{
    public InterestPage()
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
            if (args.SelectedItem is DhirDhar.Application.Borrowers.Models.BorrowerSummary borrower)
            {
                ViewModel?.SelectBorrower(borrower);
            }
        };
        BorrowerSearchBox.QuerySubmitted += (sender, args) =>
        {
            if (args.ChosenSuggestion is DhirDhar.Application.Borrowers.Models.BorrowerSummary borrower)
            {
                ViewModel?.SelectBorrower(borrower);
            }
            else if (ViewModel?.SearchResults.Count > 0)
            {
                ViewModel?.SelectBorrower(ViewModel.SearchResults[0]);
            }
        };
    }

    public InterestViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(InterestViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        Bindings.Update();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is InterestViewModel viewModel)
        {
            SetViewModel(viewModel);
        }
    }

    public async Task LoadInterestPageAsync()
    {
        if (ViewModel != null)
        {
            await ViewModel.LoadBorrowersAsync();
        }
    }
}
