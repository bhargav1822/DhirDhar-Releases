using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DhirDhar.Application.Backup;
using DhirDhar.Application.Licensing;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Settings;
using DhirDhar.Desktop.Configuration;
using DhirDhar.Desktop.Navigation;
using DhirDhar.Desktop.Services;
using DhirDhar.Desktop.Updates;
using DhirDhar.Desktop.ViewModels;
using DhirDhar.Desktop.ViewModels.Backup;
using Microsoft.Extensions.Logging;
using DhirDhar.Desktop.ViewModels.Borrowers;
using DhirDhar.Desktop.ViewModels.Interest;
using DhirDhar.Desktop.ViewModels.Ledger;
using DhirDhar.Desktop.ViewModels.Reports;
using DhirDhar.Desktop.ViewModels.Search;
using DhirDhar.Desktop.ViewModels.Security;
using DhirDhar.Desktop.ViewModels.Transactions;
using DhirDhar.Desktop.Views.Backup;
using DhirDhar.Desktop.Views.Borrowers;
using DhirDhar.Desktop.Views.Dashboard;
using DhirDhar.Desktop.Views.Integrity;
using DhirDhar.Desktop.Views.Interest;
using DhirDhar.Desktop.Views.Ledger;
using DhirDhar.Desktop.Views.Placeholder;
using DhirDhar.Desktop.Views.Reports;
using DhirDhar.Desktop.Views.Search;
using DhirDhar.Desktop.Views.Security;
using DhirDhar.Desktop.Views.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;

namespace DhirDhar.Desktop.Views.Shell;

public sealed partial class MainPage : Page
{
    public event Action? DashboardDataLoaded;

    private NavigationDestination _currentDestination = NavigationDestination.Dashboard;
    private BorrowersViewModel? _borrowersViewModel;

    private readonly Button[] _navButtons;
    private readonly HashSet<Button> _hoveredButtons = new();
    private readonly DispatcherTimer _hoverTimer;
    private IntPtr _windowHandle;
    private NativePoint _lastCursor;
    private DispatcherTimer? _animationTimer;
    private double _animationStartWidth;
    private double _animationTargetWidth;
    private DateTime _animationStartTime;
    private const double AnimationDurationMs = 180.0;

    public MainPage()
    {
        InitializeComponent();

        ContentFrame.Navigated += OnContentFrameNavigated;

        _navButtons = new[]
        {
            NavDashboard, NavBorrowers, NavTransactions, NavInterest, NavLedger,
            NavReports, NavBackup, NavSecurity, NavIntegrity,
        };

        foreach (var button in _navButtons)
        {
            button.PointerEntered += OnNavItemPointerEntered;
            button.PointerExited += OnNavItemPointerExited;
        }

        _hoverTimer = new DispatcherTimer();
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(80);
        _hoverTimer.Tick += OnHoverTimerTick;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;

        var locService = App.ServiceProvider?.GetService<ILocalizationService>();
        if (locService != null)
        {
            Language = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeLanguageCode(locService.CurrentLanguage);
            locService.LanguageChanged += (s, e) =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    Language = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeLanguageCode(locService.CurrentLanguage);
                });
            };
        }
    }

    public MainViewModel ViewModel { get; private set; } = null!;

    public void SetViewModel(MainViewModel viewModel)
    {
        if (ViewModel != null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.NavigationService.NavigationChanged -= OnNavigationChanged;
        }

        ViewModel = viewModel;
        DataContext = viewModel;

        if (ViewModel != null)
        {
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ViewModel.NavigationService.NavigationChanged += OnNavigationChanged;
        }

        var dest = ViewModel?.NavigationService.CurrentDestination ?? NavigationDestination.Dashboard;
        NavigateToDestination(dest);
    }

    private void OnNavigationChanged(object? sender, NavigationState state)
    {
        NavigateToDestination(state.Destination, state.Parameter);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsSidebarExpanded))
        {
            AnimateSidebarWidth(ViewModel.IsSidebarExpanded);
        }
    }

    private void AnimateSidebarWidth(bool isExpanded)
    {
        double currentWidth = SidebarColumn.Width.Value;
        double targetWidth = isExpanded ? 252.0 : 72.0;

        if (Math.Abs(currentWidth - targetWidth) < 0.5)
        {
            SidebarColumn.Width = new GridLength(targetWidth);
            return;
        }

        _animationStartWidth = currentWidth;
        _animationTargetWidth = targetWidth;
        _animationStartTime = DateTime.Now;

        if (_animationTimer == null)
        {
            _animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(12)
            };
            _animationTimer.Tick += OnAnimationTimerTick;
        }

        if (!_animationTimer.IsEnabled)
        {
            _animationTimer.Start();
        }
    }

    private void OnAnimationTimerTick(object? sender, object e)
    {
        double elapsed = (DateTime.Now - _animationStartTime).TotalMilliseconds;
        double progress = Math.Clamp(elapsed / AnimationDurationMs, 0.0, 1.0);

        double easeProgress = 1.0 - Math.Pow(1.0 - progress, 3);
        double newWidth = _animationStartWidth + (_animationTargetWidth - _animationStartWidth) * easeProgress;

        SidebarColumn.Width = new GridLength(newWidth);

        if (progress >= 1.0)
        {
            _animationTimer?.Stop();
            SidebarColumn.Width = new GridLength(_animationTargetWidth);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is MainViewModel viewModel)
        {
            SetViewModel(viewModel);
        }
    }

    public bool IsDashboardDataLoaded()
    {
        var content = ContentFrame.Content;
        if (content is DashboardPage dashboardPage && dashboardPage.ViewModel != null)
        {
            return !dashboardPage.ViewModel.IsLoading;
        }
        return false;
    }

    private void OnMenuButtonClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.ToggleSidebar();
    }

    private void OnNavItemClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null)
        {
            return;
        }

        if (sender is Button button && button.Tag is string tag)
        {
            if (Enum.TryParse<NavigationDestination>(tag, out var destination))
            {
                ViewModel.NavigationService.Navigate(destination);
            }
        }
    }

    private void NavigateToDestination(NavigationDestination destination, object? parameter = null)
    {
        UpdateNavSelection(destination);

        switch (destination)
        {
            case NavigationDestination.Dashboard:
                NavigateToDashboard();
                break;
            case NavigationDestination.Borrowers:
                NavigateToBorrowers(parameter);
                break;
            case NavigationDestination.BorrowerDetails:
                NavigateToBorrowerDetails(parameter);
                break;
            case NavigationDestination.Transactions:
                NavigateToTransactions(parameter);
                break;
            case NavigationDestination.Interest:
                NavigateToInterest();
                break;
            case NavigationDestination.Ledger:
                NavigateToLedger();
                break;
            case NavigationDestination.Reports:
                NavigateToReports();
                break;
            case NavigationDestination.Search:
                NavigateToSearch();
                break;
            case NavigationDestination.BackupRestore:
                NavigateToBackupRestore();
                break;
            case NavigationDestination.Security:
                NavigateToSecurity();
                break;
            case NavigationDestination.Integrity:
                NavigateToIntegrity();
                break;
            case NavigationDestination.Settings:
                NavigateToSettings();
                break;
            default:
                ContentFrame.Navigate(typeof(FeaturePlaceholderPage), destination.ToString());
                break;
        }
    }

    private void NavigateToDashboard()
    {
        try
        {
            var sp = App.ServiceProvider;
            if (sp == null)
            {
                ContentFrame.Navigate(typeof(FeaturePlaceholderPage), "Dashboard");
                DashboardDataLoaded?.Invoke();
                return;
            }
            var vm = sp.GetService(typeof(DashboardViewModel)) as DashboardViewModel;
            if (vm != null)
            {
                ContentFrame.Navigate(typeof(DashboardPage), vm);
                _ = LoadDashboardAndNotifyAsync(vm);
            }
            else
            {
                ContentFrame.Navigate(typeof(FeaturePlaceholderPage), "Dashboard");
                DashboardDataLoaded?.Invoke();
            }
        }
        catch (Exception)
        {
            ContentFrame.Navigate(typeof(FeaturePlaceholderPage), "Dashboard");
            DashboardDataLoaded?.Invoke();
        }
    }

    private async Task LoadDashboardAndNotifyAsync(DashboardViewModel vm)
    {
        await vm.LoadAsync();
        DashboardDataLoaded?.Invoke();
    }

    private void NavigateToBorrowers(object? parameter = null)
    {
        var vm = App.ServiceProvider?.GetService(typeof(BorrowersViewModel)) as BorrowersViewModel;
        if (vm != null)
        {
            _borrowersViewModel = vm;
            var sp = App.ServiceProvider;
            vm.BorrowerEditViewModelFactory = sp is null ? null : () => (BorrowerEditViewModel)sp.GetRequiredService(typeof(BorrowerEditViewModel));
            vm.BorrowerEditNavigationRequested = NavigateToBorrowerEdit;
            vm.BorrowerDetailsNavigationRequested = id => NavigateToDestination(NavigationDestination.BorrowerDetails, id);

            if (parameter is "New" or "Add")
            {
                var editVm = vm.BorrowerEditViewModelFactory?.Invoke();
                if (editVm != null)
                {
                    editVm.CloseRequested += () => ViewModel?.NavigationService.GoBack();
                    ContentFrame.Navigate(typeof(BorrowerEditPage), editVm);
                    _ = editVm.LoadAsync();
                    return;
                }
            }

            ContentFrame.Navigate(typeof(BorrowersPage), vm);
            _ = vm.LoadAsync();
        }
        else
        {
            ContentFrame.Navigate(typeof(FeaturePlaceholderPage), "Borrowers");
        }
    }

    private void NavigateToBorrowerDetails(object? parameter)
    {
        var vm = App.ServiceProvider?.GetService(typeof(BorrowerDetailsViewModel)) as BorrowerDetailsViewModel;
        if (vm != null)
        {
            var sp = App.ServiceProvider;
            vm.BorrowerEditViewModelFactory = sp is null ? null : () => (BorrowerEditViewModel)sp.GetRequiredService(typeof(BorrowerEditViewModel));
            vm.BorrowerEditNavigationRequested = (editVm) => NavigateToBorrowerEditFromDetails(editVm, vm.BorrowerId);

            ContentFrame.Navigate(typeof(BorrowerDetailsPage), vm);
            if (parameter is Guid borrowerId)
            {
                _ = LoadBorrowerDetailsAndNotifyAsync(vm, borrowerId);
            }
        }
        else
        {
            ContentFrame.Navigate(typeof(FeaturePlaceholderPage), "Borrower Details");
        }
    }

    private void NavigateToBorrowerEditFromDetails(BorrowerEditViewModel editViewModel, Guid borrowerId)
    {
        void OnEditClosed()
        {
            editViewModel.CloseRequested -= OnEditClosed;
            NavigateToDestination(NavigationDestination.BorrowerDetails, borrowerId);
        }

        editViewModel.CloseRequested += OnEditClosed;
        ContentFrame.Navigate(typeof(BorrowerEditPage), editViewModel);
        _ = editViewModel.LoadAsync();
    }

    private async Task LoadBorrowerDetailsAndNotifyAsync(BorrowerDetailsViewModel vm, Guid borrowerId)
    {
        try
        {
            vm.BorrowerId = borrowerId;
            await vm.LoadAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading borrower details for BorrowerId '{borrowerId}': {ex}");
        }
    }

    private void NavigateToBorrowerEdit(BorrowerEditViewModel editViewModel)
    {
        editViewModel.CloseRequested += NavigateBackToBorrowers;
        ContentFrame.Navigate(typeof(BorrowerEditPage), editViewModel);
        _ = editViewModel.LoadAsync();
    }

    private void NavigateBackToBorrowers()
    {
        var vm = _borrowersViewModel;
        if (vm is null)
        {
            return;
        }

        ContentFrame.Navigate(typeof(BorrowersPage), vm);
        _ = vm.LoadAsync();
    }

    private void NavigateToTransactions(object? parameter = null)
    {
        var vm = App.ServiceProvider?.GetService(typeof(TransactionsViewModel)) as TransactionsViewModel;
        if (vm != null)
        {
            if (parameter is (string type, bool openForm))
            {
                vm.SelectedTransactionType = type;
                vm.NewType = type;
                vm.NewOccurredOn = DateTimeOffset.Now;
                vm.NewAmount = 0;
                vm.NewNotes = string.Empty;
                vm.NewBorrowerId = null;
                vm.IsAddingTransaction = openForm;
            }
            else if (parameter is string filterType)
            {
                vm.SelectedTransactionType = filterType;
                vm.IsAddingTransaction = false;
            }
            else if (parameter is Guid borrowerId)
            {
                vm.SelectedBorrowerId = borrowerId;
                vm.NewBorrowerId = borrowerId;
                vm.NewOccurredOn = DateTimeOffset.Now;
                vm.IsAddingTransaction = true;
            }
            else if (parameter is Tuple<string, Guid> tuple)
            {
                vm.SelectedTransactionType = tuple.Item1;
                vm.SelectedBorrowerId = tuple.Item2;
                vm.NewBorrowerId = tuple.Item2;
                vm.NewType = tuple.Item1;
                vm.NewOccurredOn = DateTimeOffset.Now;
                vm.IsAddingTransaction = true;
            }
            else if (parameter is (string t, Guid bId))
            {
                vm.SelectedTransactionType = t;
                vm.SelectedBorrowerId = bId;
                vm.NewBorrowerId = bId;
                vm.NewType = t;
                vm.NewOccurredOn = DateTimeOffset.Now;
                vm.IsAddingTransaction = true;
            }

            ContentFrame.Navigate(typeof(TransactionsPage), vm);
            _ = vm.LoadAsync();
        }
        else
        {
            ContentFrame.Navigate(typeof(FeaturePlaceholderPage), "Transactions");
        }
    }

    private void NavigateToInterest()
    {
        var vm = App.ServiceProvider?.GetService(typeof(InterestViewModel)) as InterestViewModel;
        if (vm != null)
        {
            ContentFrame.Navigate(typeof(InterestPage), vm);
            _ = vm.LoadBorrowersAsync();
        }
        else
        {
            ContentFrame.Navigate(typeof(FeaturePlaceholderPage), "Interest");
        }
    }

    private void NavigateToLedger()
    {
        var vm = App.ServiceProvider?.GetService(typeof(LedgerViewModel)) as LedgerViewModel;
        if (vm != null)
        {
            ContentFrame.Navigate(typeof(LedgerPage), vm);
            _ = vm.LoadAsync();
        }
        else
        {
            ContentFrame.Navigate(typeof(FeaturePlaceholderPage), "Ledger");
        }
    }

    private void NavigateToReports()
    {
        var vm = App.ServiceProvider?.GetService(typeof(ReportsViewModel)) as ReportsViewModel;
        if (vm != null)
        {
            ContentFrame.Navigate(typeof(ReportsPage), vm);
            _ = vm.LoadBorrowersAsync();
        }
        else
        {
            ContentFrame.Navigate(typeof(FeaturePlaceholderPage), "Reports");
        }
    }

    private void NavigateToSearch()
    {
        var vm = App.ServiceProvider?.GetService(typeof(SearchViewModel)) as SearchViewModel;
        if (vm != null)
        {
            ContentFrame.Navigate(typeof(SearchPage), vm);
        }
        else
        {
            ContentFrame.Navigate(typeof(FeaturePlaceholderPage), "Search");
        }
    }

    private void OnContentFrameNavigated(object sender, NavigationEventArgs e)
    {
        var content = e.Content;
        NavigationDestination? dest = content switch
        {
            DashboardPage => NavigationDestination.Dashboard,
            BorrowersPage => NavigationDestination.Borrowers,
            BorrowerDetailsPage => NavigationDestination.BorrowerDetails,
            BorrowerEditPage => NavigationDestination.Borrowers,
            TransactionsPage => NavigationDestination.Transactions,
            InterestPage => NavigationDestination.Interest,
            LedgerPage => NavigationDestination.Ledger,
            ReportsPage => NavigationDestination.Reports,
            SearchPage => NavigationDestination.Search,
            BackupRestorePage => NavigationDestination.BackupRestore,
            SecurityPage => NavigationDestination.Security,
            IntegrityPage => NavigationDestination.Integrity,
            DhirDhar.Desktop.Views.Settings.SettingsPage => NavigationDestination.Settings,
            _ => null
        };

        if (dest.HasValue)
        {
            UpdateNavSelection(dest.Value);
        }
    }

    private void NavigateToBackupRestore()
    {
        var logger = App.ServiceProvider?.GetService<ILogger<MainPage>>();
        BackupRestoreViewModel? vm = null;

        try
        {
            var sp = App.ServiceProvider;
            if (sp != null)
            {
                vm = sp.GetService<BackupRestoreViewModel>();
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error resolving BackupRestoreViewModel from DI.");
        }

        if (vm != null)
        {
            ContentFrame.Navigate(typeof(BackupRestorePage), vm);
        }
        else
        {
            ContentFrame.Navigate(typeof(FeaturePlaceholderPage), "Backup & Restore");
        }
    }

    private void NavigateToSecurity()
    {
        var vm = App.ServiceProvider?.GetService(typeof(SecurityViewModel)) as SecurityViewModel;
        if (vm != null)
        {
            ContentFrame.Navigate(typeof(SecurityPage), vm);
        }
        else
        {
            ContentFrame.Navigate(typeof(FeaturePlaceholderPage), "Security");
        }
    }

    private void NavigateToIntegrity()
    {
        var vm = App.ServiceProvider?.GetService(typeof(DhirDhar.Desktop.ViewModels.IntegrityViewModel)) as DhirDhar.Desktop.ViewModels.IntegrityViewModel;
        if (vm != null)
        {
            ContentFrame.Navigate(typeof(IntegrityPage), vm);
        }
        else
        {
            ContentFrame.Navigate(typeof(IntegrityPage));
        }
    }

    private void NavigateToSettings()
    {
        var logger = App.ServiceProvider?.GetService<ILogger<MainPage>>();
        try
        {
            var sp = App.ServiceProvider;
            var vm = sp?.GetService(typeof(DhirDhar.Desktop.ViewModels.Settings.SettingsViewModel)) as DhirDhar.Desktop.ViewModels.Settings.SettingsViewModel ?? new DhirDhar.Desktop.ViewModels.Settings.SettingsViewModel();
            ContentFrame.Navigate(typeof(DhirDhar.Desktop.Views.Settings.SettingsPage), vm);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to navigate to SettingsPage with ViewModel. Attempting direct navigation.");
            try
            {
                ContentFrame.Navigate(typeof(DhirDhar.Desktop.Views.Settings.SettingsPage));
            }
            catch (Exception directEx)
            {
                logger?.LogCritical(directEx, "Direct navigation to SettingsPage failed. Constructing SettingsPage directly.");
                try
                {
                    var page = new DhirDhar.Desktop.Views.Settings.SettingsPage();
                    ContentFrame.Content = page;
                }
                catch (Exception finalEx)
                {
                    logger?.LogCritical(finalEx, "Final fallback to direct SettingsPage instance failed.");
                }
            }
        }
    }

    private Button? GetNavButton(NavigationDestination destination) => destination switch
    {
        NavigationDestination.Dashboard => NavDashboard,
        NavigationDestination.Borrowers => NavBorrowers,
        NavigationDestination.Transactions => NavTransactions,
        NavigationDestination.Interest => NavInterest,
        NavigationDestination.Ledger => NavLedger,
        NavigationDestination.Reports => NavReports,
        NavigationDestination.BackupRestore => NavBackup,
        NavigationDestination.Security => NavSecurity,
        NavigationDestination.Integrity => NavIntegrity,
        NavigationDestination.Settings => null,
        _ => NavDashboard,
    };

    private void UpdateNavSelection(NavigationDestination destination)
    {
        _currentDestination = destination;
        UpdateNavStyles();
    }

    private void UpdateNavStyles()
    {
        var selectedStyle = (Style)Resources["NavItemSelectedStyle"];
        var defaultStyle = (Style)Resources["NavItemStyle"];

        foreach (var button in _navButtons)
        {
            bool isSelected = ReferenceEquals(button, GetNavButton(_currentDestination));
            bool isHovered = _hoveredButtons.Contains(button);
            button.Style = (isSelected || isHovered) ? selectedStyle : defaultStyle;
            SetNavItemContentForeground(button, (Brush)button.Foreground);
        }
    }

    private void OnNavItemPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            _hoveredButtons.Add(button);
            UpdateNavStyles();
        }
    }

    private void OnNavItemPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            _hoveredButtons.Remove(button);
            UpdateNavStyles();
        }
    }

    private static void SetNavItemContentForeground(Button button, Brush brush)
    {
        if (button.Content is Grid grid)
        {
            foreach (var child in grid.Children)
            {
                SetNavElementForeground(child, brush);
            }
        }
    }

    private static void SetNavElementForeground(UIElement element, Brush brush)
    {
        switch (element)
        {
            case FontIcon icon:
                icon.Foreground = brush;
                break;
            case TextBlock text:
                text.Foreground = brush;
                break;
            case Grid grid:
                foreach (var child in grid.Children)
                {
                    SetNavElementForeground(child, brush);
                }
                break;
        }
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _windowHandle = App.MainWindowHandle;
        _hoverTimer.Start();

        var licenseManager = App.ServiceProvider?.GetService<DhirDhar.Application.Licensing.ILicenseManager>();
        if (licenseManager != null)
        {
            licenseManager.LicenseStatusChanged += OnLicenseStatusChanged;
        }
        UpdateLicenseBanner();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _hoverTimer.Stop();
        _animationTimer?.Stop();
        if (ViewModel != null)
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        var licenseManager = App.ServiceProvider?.GetService<DhirDhar.Application.Licensing.ILicenseManager>();
        if (licenseManager != null)
        {
            licenseManager.LicenseStatusChanged -= OnLicenseStatusChanged;
        }
    }

    private void OnLicenseStatusChanged(object? sender, DhirDhar.Application.Licensing.Models.LicenseStatus e)
    {
        DispatcherQueue.TryEnqueue(UpdateLicenseBanner);
    }

    private void UpdateLicenseBanner()
    {
        var licenseManager = App.ServiceProvider?.GetService<DhirDhar.Application.Licensing.ILicenseManager>();
        if (licenseManager == null)
        {
            LicenseBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var status = licenseManager.Status;
        var license = licenseManager.CurrentLicense;

        if (status == DhirDhar.Application.Licensing.Models.LicenseStatus.Expired)
        {
            LicenseBanner.Visibility = Visibility.Visible;
            LicenseBanner.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 20, 20));
            LicenseBanner.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 40, 40));
            LicenseBannerText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 180, 180));
            LicenseBannerIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100));
            LicenseBannerText.Text = $"Your DhirDhar license expired on {license?.FormattedExpiresAt ?? "recently"}. Financial records are in Read-Only mode.";
            RenewLicenseButton.Visibility = Visibility.Visible;
        }
        else if (status == DhirDhar.Application.Licensing.Models.LicenseStatus.ExpiringSoon)
        {
            LicenseBanner.Visibility = Visibility.Visible;
            LicenseBanner.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 40, 10));
            LicenseBanner.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 140, 30));
            LicenseBannerText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 220, 120));
            LicenseBannerIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 200, 50));
            LicenseBannerText.Text = $"Your DhirDhar license will expire in {license?.DaysRemaining ?? 0} days on {license?.FormattedExpiresAt}.";
            RenewLicenseButton.Visibility = Visibility.Visible;
        }
        else
        {
            LicenseBanner.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnRenewLicenseClick(object sender, RoutedEventArgs e)
    {
        if (App.ServiceProvider == null) return;
        var licenseVm = App.ServiceProvider.GetRequiredService<DhirDhar.Desktop.ViewModels.License.LicenseViewModel>();
        var dialog = new DhirDhar.Desktop.Views.License.LicenseRenewalDialog(licenseVm)
        {
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
        UpdateLicenseBanner();
    }

    private void OnHoverTimerTick(object? sender, object e)
    {
        if (_windowHandle == IntPtr.Zero || GetForegroundWindow() != _windowHandle)
        {
            if (_hoveredButtons.Count > 0)
            {
                _hoveredButtons.Clear();
                UpdateNavStyles();
            }
            return;
        }

        if (!GetCursorPos(out var cursorPoint))
        {
            return;
        }

        if (cursorPoint.X == _lastCursor.X && cursorPoint.Y == _lastCursor.Y)
        {
            return;
        }
        _lastCursor = cursorPoint;

        if (!ScreenToClient(_windowHandle, ref cursorPoint))
        {
            return;
        }

        var scale = XamlRoot.RasterizationScale;
        var point = new Point(cursorPoint.X / scale, cursorPoint.Y / scale);
        var hovered = FindNavButtonAt(point);

        if (hovered == null)
        {
            if (_hoveredButtons.Count > 0)
            {
                _hoveredButtons.Clear();
                UpdateNavStyles();
            }
        }
        else if (_hoveredButtons.Count != 1 || !_hoveredButtons.Contains(hovered))
        {
            _hoveredButtons.Clear();
            _hoveredButtons.Add(hovered);
            UpdateNavStyles();
        }
    }

    private Button? FindNavButtonAt(Point point)
    {
        var elements = VisualTreeHelper.FindElementsInHostCoordinates(point, this);
        foreach (var element in elements)
        {
            var current = element;
            while (current != null)
            {
                if (current is Button button && Array.IndexOf(_navButtons, button) >= 0)
                {
                    return button;
                }
                current = VisualTreeHelper.GetParent(current) as UIElement;
            }
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
