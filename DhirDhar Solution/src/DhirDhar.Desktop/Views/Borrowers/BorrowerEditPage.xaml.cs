using DhirDhar.Desktop.ViewModels.Borrowers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DhirDhar.Desktop.Views.Borrowers;

public sealed partial class BorrowerEditPage : Page
{
    public BorrowerEditPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    public BorrowerEditViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(BorrowerEditViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        try
        {
            viewModel.WindowHandle = App.MainWindowHandle;
            viewModel.XamlRoot = XamlRoot;
        }
        catch { }
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.XamlRoot = XamlRoot;
        }

        FormScrollViewer?.ChangeView(null, 0, null, true);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is BorrowerEditViewModel viewModel)
        {
            SetViewModel(viewModel);
        }

        FormScrollViewer?.ChangeView(null, 0, null, true);
    }
}
