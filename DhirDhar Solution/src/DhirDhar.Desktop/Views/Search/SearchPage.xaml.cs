using System.Threading.Tasks;
using DhirDhar.Desktop.ViewModels.Search;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Views.Search;

public sealed partial class SearchPage : Page
{
    public SearchPage()
    {
        InitializeComponent();
    }

    public SearchViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(SearchViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        Bindings.Update();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is SearchViewModel viewModel)
        {
            SetViewModel(viewModel);
        }
    }

    public async Task LoadSearchPageAsync()
    {
        await Task.CompletedTask;
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
