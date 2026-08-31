using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Localization;
using DhirDhar.Application.QrCode;
using DhirDhar.Application.Search.Models;
using DhirDhar.Desktop.Navigation;
using DhirDhar.Desktop.ViewModels;
using DhirDhar.Desktop.Views.Borrowers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

namespace DhirDhar.Desktop.Views.Dashboard;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ResetScrollToTop();
    }

    private void ResetScrollToTop()
    {
        DashboardScrollViewer?.ChangeView(null, 0, null, true);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            DashboardScrollViewer?.ChangeView(null, 0, null, true);
        });
    }

    public DashboardViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.RequestScanQr = OpenScanQrDialogAsync;
        DataContext = viewModel;
        System.Diagnostics.Debug.WriteLine("DashboardPage.SetViewModel: DataContext set");
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var logger = App.ServiceProvider?.GetService<Microsoft.Extensions.Logging.ILogger<DashboardPage>>();
        logger?.LogInformation("[LIFECYCLE] DashboardPage.OnNavigatedTo parameter={ParamType}", e.Parameter?.GetType().Name ?? "null");

        if (e.Parameter is DashboardViewModel viewModel)
        {
            SetViewModel(viewModel);
        }

        if (HeaderSearchTextBox != null)
        {
            HeaderSearchTextBox.Text = string.Empty;
        }

        if (ViewModel != null)
        {
            ViewModel.IsSearchExpanded = false;
        }

        ResetScrollToTop();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        var logger = App.ServiceProvider?.GetService<Microsoft.Extensions.Logging.ILogger<DashboardPage>>();
        logger?.LogInformation("[LIFECYCLE] DashboardPage.OnNavigatedFrom source={SourceType}", e.SourcePageType?.Name ?? "null");
    }

    public async Task LoadDashboardAsync()
    {
        if (ViewModel != null)
        {
            await ViewModel.LoadAsync();
        }

        ResetScrollToTop();
    }

    private void OnSearchIconClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (HeaderSearchTextBox != null)
        {
            HeaderSearchTextBox.Text = string.Empty;
        }

        if (ViewModel != null)
        {
            ViewModel.IsSearchExpanded = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                HeaderSearchTextBox?.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            });
        }
    }

    private void OnCloseSearchClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (HeaderSearchTextBox != null)
        {
            HeaderSearchTextBox.Text = string.Empty;
        }

        if (ViewModel != null)
        {
            ViewModel.IsSearchExpanded = false;
        }
    }

    private void OnSearchTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (ViewModel != null && sender is TextBox textBox)
        {
            ViewModel.SearchTerm = textBox.Text;
        }
    }

    private void OnSearchTextBoxKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            if (HeaderSearchTextBox != null)
            {
                HeaderSearchTextBox.Text = string.Empty;
            }

            if (ViewModel != null)
            {
                ViewModel.IsSearchExpanded = false;
                e.Handled = true;
            }
        }
    }

    private void OnSearchResultItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchResult result && ViewModel != null)
        {
            ViewModel.SelectSearchResultCommand.Execute(result);
        }
    }

    private void OnDismissUpdateFlyoutClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        UpdateNotificationFlyout?.Hide();
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
            var navigationService = App.ServiceProvider?.GetService<INavigationService>();
            navigationService?.Navigate(NavigationDestination.BorrowerDetails, dialog.ScannedBorrower.Id);
        }
    }

    private async void OnQrSearchClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await OpenScanQrDialogAsync();
    }
}
