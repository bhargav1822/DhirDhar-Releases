using System.Threading.Tasks;
using DhirDhar.Desktop.ViewModels.Transactions;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DhirDhar.Desktop.Views.Transactions;

public sealed partial class TransactionsPage : Page
{
    public TransactionsPage()
    {
        InitializeComponent();

        NewTransactionBorrowerSearchBox.TextChanged += (sender, args) =>
        {
            if (args.Reason.ToString() == "UserInput" || args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ViewModel?.SearchNewBorrowers(sender.Text);
            }
        };

        NewTransactionBorrowerSearchBox.SuggestionChosen += (sender, args) =>
        {
            if (args.SelectedItem is DhirDhar.Application.Borrowers.Models.BorrowerSummary borrower)
            {
                ViewModel?.SelectNewBorrower(borrower);
            }
        };

        NewTransactionBorrowerSearchBox.QuerySubmitted += (sender, args) =>
        {
            if (args.ChosenSuggestion is DhirDhar.Application.Borrowers.Models.BorrowerSummary borrower)
            {
                ViewModel?.SelectNewBorrower(borrower);
            }
            else if (ViewModel?.NewBorrowerSearchResults.Count > 0)
            {
                ViewModel?.SelectNewBorrower(ViewModel.NewBorrowerSearchResults[0]);
            }
        };
    }

    public TransactionsViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(TransactionsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        Bindings.Update();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is TransactionsViewModel viewModel)
        {
            SetViewModel(viewModel);
        }
    }

    public async Task LoadTransactionsAsync()
    {
        if (ViewModel != null)
        {
            await ViewModel.LoadAsync();
        }
    }

    private void TransactionListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TransactionRowItem item)
        {
            ViewModel.OpenTransactionDetailsCommand.Execute(item);
        }
    }
}
