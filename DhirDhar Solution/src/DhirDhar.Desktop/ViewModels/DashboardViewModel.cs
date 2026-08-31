using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Caching;
using DhirDhar.Application.Dashboard;
using DhirDhar.Application.Dashboard.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Profiles;
using DhirDhar.Application.Search;
using DhirDhar.Application.Search.Models;
using DhirDhar.Application.Transactions;
using DhirDhar.Desktop.Navigation;
using DhirDhar.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace DhirDhar.Desktop.ViewModels;

public sealed class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly IDashboardService _dashboardService;
    private readonly IProfileService _profileService;
    private readonly ILocalizationService _localizationService;
    private readonly INavigationService _navigationService;
    private readonly ISearchService _searchService;
    private readonly ITranslationService _translationService;
    private readonly ILogger<DashboardViewModel> _logger;
    private readonly DhirDhar.Desktop.Updates.IUpdateService? _updateService;
    private readonly ITransactionEventService? _transactionEventService;
    private readonly ICacheService? _cacheService;

    private string _profileName = string.Empty;
    private int _totalBorrowers;
    private int _activeBorrowers;
    private int _inactiveBorrowers;
    private int _closedBorrowers;
    private decimal _totalDeposits;
    private decimal _totalWithdrawals;
    private decimal _outstandingAmount;
    private decimal _totalInterest;
    private bool _isLoading;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private DateTime _lastUpdated;
    private ObservableCollection<RecentTransactionSummary> _recentTransactions = new();
    private PeriodSummaryInfo _periodSummary = new(0m, 0m, 0m, 0m);
    private ObservableCollection<HistoricalOutstandingPoint> _historicalOutstanding = new();
    private IReadOnlyList<DashboardMonthOption> _monthOptions = Array.Empty<DashboardMonthOption>();
    private DashboardMonthOption? _selectedMonthOption;
    private bool _isSettingMonthOption;

    private IReadOnlyList<DashboardYearOption> _yearOptions = Array.Empty<DashboardYearOption>();
    private DashboardYearOption? _selectedYearOption;
    private bool _isSettingYearOption;
    private YearlyOutstandingChartData? _yearlyChartData;
    private ObservableCollection<MonthlyChartGroup> _monthlyGroups = new();
    private ObservableCollection<ChartYAxisTick> _yAxisTicks = new();

    // Search state
    private bool _isSearchExpanded;
    private string _searchTerm = string.Empty;
    private ObservableCollection<SearchResult> _searchResults = new();
    private bool _isSearching;
    private bool _hasSearchResults;
    private bool _hasNoSearchResults;

    // Update notification bell state
    private bool _isUpdateAvailable;
    private string _availableUpdateVersion = string.Empty;
    private string _currentInstalledVersion = string.Empty;

    // Event & Timer management
    private readonly DispatcherTimer _clockTimer;
    private readonly object _refreshLock = new();
    private CancellationTokenSource? _transactionRefreshDebounceCts;
    private bool _isDisposed;

    public DashboardViewModel(
        IDashboardService dashboardService,
        IProfileService profileService,
        ILocalizationService localizationService,
        INavigationService navigationService,
        ISearchService searchService,
        ITranslationService translationService,
        ILogger<DashboardViewModel> logger,
        DhirDhar.Desktop.Updates.IUpdateService? updateService = null,
        ITransactionEventService? transactionEventService = null,
        ICacheService? cacheService = null)
    {
        _dashboardService = dashboardService;
        _profileService = profileService;
        _localizationService = localizationService;
        _navigationService = navigationService;
        _searchService = searchService;
        _translationService = translationService;
        _logger = logger;
        _updateService = updateService;
        _transactionEventService = transactionEventService ?? App.ServiceProvider?.GetService<ITransactionEventService>();
        _cacheService = cacheService ?? App.ServiceProvider?.GetService<ICacheService>();

        RefreshCommand = new RelayCommand(async () => await LoadAsync());
        RetryCommand = new RelayCommand(async () => await LoadAsync());

        ToggleSearchCommand = new RelayCommand(() => IsSearchExpanded = !IsSearchExpanded);
        CloseSearchCommand = new RelayCommand(() => IsSearchExpanded = false);
        SelectSearchResultCommand = new RelayCommand<SearchResult>(OnSelectSearchResult);

        NavigateToBorrowersCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.Borrowers));
        NavigateToLedgerCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.Ledger));
        NavigateToDepositsCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.Transactions, "Deposit"));
        NavigateToWithdrawalsCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.Transactions, "Withdrawal"));
        NavigateToInterestCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.Interest));
        NavigateToTransactionsCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.Transactions));

        AddBorrowerCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.Borrowers, "New"));
        NewTransactionCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.Transactions));
        ReceivePaymentCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.Transactions, ("Deposit", true)));
        GivePaymentCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.Transactions, ("Withdrawal", true)));
        GiveAmountCommand = GivePaymentCommand;
        GenerateReportCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.Reports));
        BackupNowCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.BackupRestore));
        NavigateToSettingsCommand = new RelayCommand(() => _navigationService.Navigate(NavigationDestination.Settings));
        ViewUpdateCommand = new RelayCommand(OpenReleasePage);
        OpenScanQrCommand = new RelayCommand(async () => { if (RequestScanQr != null) await RequestScanQr(); });

        if (_updateService != null)
        {
            _currentInstalledVersion = _updateService.CurrentVersion;
            _updateService.UpdateAvailable += (s, updateInfo) =>
            {
                RunOnUiThread(() =>
                {
                    if (updateInfo != null && !string.IsNullOrWhiteSpace(updateInfo.Version))
                    {
                        AvailableUpdateVersion = updateInfo.Version;
                        CurrentInstalledVersion = _updateService.CurrentVersion;
                        IsUpdateAvailable = true;
                    }
                });
            };

            if (_updateService.AvailableUpdate != null)
            {
                _isUpdateAvailable = true;
                _availableUpdateVersion = _updateService.AvailableUpdate.Version;
            }
        }

        _clockTimer = new DispatcherTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();

        _localizationService.LanguageChanged += OnLanguageChangedHandler;

        if (_transactionEventService != null)
        {
            _transactionEventService.TransactionChanged += OnTransactionChanged;
        }
    }

    private void OnClockTick(object? sender, object e)
    {
        OnPropertyChanged(nameof(ClockDateText));
        OnPropertyChanged(nameof(ClockText));
    }

    private void OnLanguageChangedHandler(object? sender, EventArgs e)
    {
        var selMonth = _selectedMonthOption;
        var selYear = _selectedYearOption;
        _monthOptions = BuildMonthOptions();
        if (selMonth != null)
        {
            _selectedMonthOption = _monthOptions.FirstOrDefault(o => o.Year == selMonth.Year && o.Month == selMonth.Month) ?? _monthOptions.LastOrDefault();
        }
        OnPropertyChanged(string.Empty);
        OnPropertyChanged(nameof(MonthOptions));
        OnPropertyChanged(nameof(SelectedMonthOption));
        OnPropertyChanged(nameof(YearOptions));
        OnPropertyChanged(nameof(SelectedYearOption));
        OnPropertyChanged(nameof(NewLoansLegendLabel));
        OnPropertyChanged(nameof(WithdrawalsLegendLabel));
        OnPropertyChanged(nameof(DepositsLegendLabel));
        OnPropertyChanged(nameof(InterestEarnedLegendLabel));
        _ = LoadAsync(selMonth, selYear);
    }

    private void OnTransactionChanged(object? sender, TransactionChangedEventArgs e)
    {
        if (_isDisposed) return;

        _logger.LogInformation("[DASHBOARD] TransactionChanged received: Kind={Kind}, TxnId={TxnId}", e.MutationKind, e.TransactionId);

        lock (_refreshLock)
        {
            _transactionRefreshDebounceCts?.Cancel();
            _transactionRefreshDebounceCts?.Dispose();
            _transactionRefreshDebounceCts = new CancellationTokenSource();
        }

        var ct = _transactionRefreshDebounceCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                // Debounce rapid back-to-back notifications (e.g. bulk batch mutations)
                await Task.Delay(50, ct);
                if (ct.IsCancellationRequested || _isDisposed) return;

                // Ensure stale cached summary is removed
                _cacheService?.Remove("dashboard_summary");

                RunOnUiThread(() =>
                {
                    if (_isDisposed) return;
                    var preservedMonth = _selectedMonthOption;
                    var preservedYear = _selectedYearOption;
                    _ = LoadAsync(preservedMonth, preservedYear);
                });
            }
            catch (OperationCanceledException)
            {
                // Coalesced into newer refresh
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DASHBOARD] Failed to refresh dashboard on TransactionChanged event.");
            }
        });
    }

    public int TotalBorrowers
    {
        get => _totalBorrowers;
        private set => SetProperty(ref _totalBorrowers, value);
    }

    public int ActiveBorrowers
    {
        get => _activeBorrowers;
        private set => SetProperty(ref _activeBorrowers, value);
    }

    public int InactiveBorrowers
    {
        get => _inactiveBorrowers;
        private set => SetProperty(ref _inactiveBorrowers, value);
    }

    public int ClosedBorrowers
    {
        get => _closedBorrowers;
        private set
        {
            if (SetProperty(ref _closedBorrowers, value))
            {
                OnPropertyChanged(nameof(ArchivedBorrowers));
            }
        }
    }

    public int ArchivedBorrowers => ClosedBorrowers;

    public decimal TotalDeposits
    {
        get => _totalDeposits;
        private set => SetProperty(ref _totalDeposits, value);
    }

    public decimal TotalWithdrawals
    {
        get => _totalWithdrawals;
        private set => SetProperty(ref _totalWithdrawals, value);
    }

    public decimal OutstandingAmount
    {
        get => _outstandingAmount;
        private set => SetProperty(ref _outstandingAmount, value);
    }

    public decimal TotalInterest
    {
        get => _totalInterest;
        private set => SetProperty(ref _totalInterest, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public DateTime LastUpdated
    {
        get => _lastUpdated;
        private set => SetProperty(ref _lastUpdated, value);
    }

    public ObservableCollection<RecentTransactionSummary> RecentTransactions
    {
        get => _recentTransactions;
        private set => SetProperty(ref _recentTransactions, value);
    }

    public PeriodSummaryInfo PeriodSummary
    {
        get => _periodSummary;
        private set => SetProperty(ref _periodSummary, value);
    }

    public ObservableCollection<HistoricalOutstandingPoint> HistoricalOutstanding
    {
        get => _historicalOutstanding;
        private set => SetProperty(ref _historicalOutstanding, value);
    }

    public IReadOnlyList<DashboardMonthOption> MonthOptions
    {
        get => _monthOptions;
        private set => SetProperty(ref _monthOptions, value);
    }

    public DashboardMonthOption? SelectedMonthOption
    {
        get => _selectedMonthOption;
        set
        {
            if (value != null && !ReferenceEquals(_selectedMonthOption, value))
            {
                _selectedMonthOption = value;
                OnPropertyChanged();
                if (!_isSettingMonthOption)
                {
                    _ = LoadPeriodSummaryForMonthAsync(value);
                }
            }
        }
    }

    public IReadOnlyList<DashboardYearOption> YearOptions
    {
        get => _yearOptions;
        private set => SetProperty(ref _yearOptions, value);
    }

    public DashboardYearOption? SelectedYearOption
    {
        get => _selectedYearOption;
        set
        {
            if (value != null && !ReferenceEquals(_selectedYearOption, value))
            {
                _selectedYearOption = value;
                OnPropertyChanged();
                if (!_isSettingYearOption)
                {
                    _ = LoadYearlyDataForSelectedYearAsync(value.Year);
                }
            }
        }
    }

    public YearlyOutstandingChartData? YearlyChartData
    {
        get => _yearlyChartData;
        private set => SetProperty(ref _yearlyChartData, value);
    }

    public ObservableCollection<MonthlyChartGroup> MonthlyGroups
    {
        get => _monthlyGroups;
        private set => SetProperty(ref _monthlyGroups, value);
    }

    public ObservableCollection<ChartYAxisTick> YAxisTicks
    {
        get => _yAxisTicks;
        private set => SetProperty(ref _yAxisTicks, value);
    }

    public bool HasYearlyChartData => MonthlyGroups.Count > 0;

    public string NewLoansLegendLabel => _localizationService.GetString("NewLoans");
    public string WithdrawalsLegendLabel
    {
        get
        {
            var l = _localizationService.GetString("Withdrawals");
            if (string.IsNullOrWhiteSpace(l) || l == "Withdrawals") l = _localizationService.GetString("TotalWithdrawals");
            return string.IsNullOrWhiteSpace(l) || l == "TotalWithdrawals" ? "Withdrawals" : l;
        }
    }
    public string DepositsLegendLabel
    {
        get
        {
            var l = _localizationService.GetString("Deposits");
            if (string.IsNullOrWhiteSpace(l) || l == "Deposits") l = _localizationService.GetString("TotalDeposits");
            return string.IsNullOrWhiteSpace(l) || l == "TotalDeposits" ? "Deposits" : l;
        }
    }
    public string InterestEarnedLegendLabel => _localizationService.GetString("InterestEarned");

    public string YAxisMaxLabel => YAxisTicks.Count > 0 ? YAxisTicks[^1].FormattedLabel : "₹10L";
    public string YAxis75Label => YAxisTicks.Count > 4 ? YAxisTicks[4].FormattedLabel : (YAxisTicks.Count > 3 ? YAxisTicks[3].FormattedLabel : "₹8L");
    public string YAxis50Label => YAxisTicks.Count > 3 ? YAxisTicks[3].FormattedLabel : (YAxisTicks.Count > 2 ? YAxisTicks[2].FormattedLabel : "₹6L");
    public string YAxis25Label => YAxisTicks.Count > 1 ? YAxisTicks[1].FormattedLabel : (YAxisTicks.Count > 0 ? YAxisTicks[0].FormattedLabel : "₹2L");
    public string YAxisMinLabel => YAxisTicks.Count > 0 ? YAxisTicks[0].FormattedLabel : "₹0";

    public string ApplicationTitle => "DhirDhar Solution";
    public string FormattedApplicationVersion => $"{L("Version")} {LocalizeDigits(GetAppVersion())}";

    private string GetAppVersion()
    {
        try
        {
            return App.ServiceProvider?.GetService<DhirDhar.Desktop.Configuration.AppOptions>()?.Version ?? "2.1.1";
        }
        catch
        {
            return "2.1.1";
        }
    }

    public string PageTitle => string.IsNullOrWhiteSpace(_profileName)
        ? _localizationService.GetString("Dashboard")
        : _profileName;

    public string ProfileDisplayName => string.IsNullOrWhiteSpace(_profileName)
        ? _localizationService.GetString("Administrator")
        : _profileName;

    public string ProfileRoleLabel => _localizationService.GetString("AdministratorRole");
    public string UserProfileLabel => _localizationService.GetString("UserProfile");
    public string AccountSettingsLabel => _localizationService.GetString("AccountSettings");
    public string ChangePasswordLabel => _localizationService.GetString("ChangePasswordDisabled");
    public string LogoutLabel => _localizationService.GetString("LogoutDisabled");

    public RelayCommand NavigateToSettingsCommand { get; }

    public string FormattedFinancialYear
    {
        get
        {
            var now = DateTime.Now;
            var year = now.Month >= 4 ? now.Year : now.Year - 1;
            var fyKey = _localizationService.GetString("FinancialYearPrefix");
            return LocalizeDigits($"{fyKey} {year}-{((year + 1) % 100):D2}");
        }
    }

    public string FormattedTotalDeposits => LCur(TotalDeposits);
    public string FormattedTotalWithdrawals => LCur(TotalWithdrawals);
    public string FormattedOutstandingAmount => LCur(OutstandingAmount);
    public string FormattedTotalInterest => LCur(TotalInterest);
    public string FormattedLastUpdated => LastUpdated == DateTime.MinValue ? string.Empty : LDateTime(LastUpdated);

    public string ClockDateText => LDateTime(DateTime.Now, "ddd, dd MMM yyyy");
    public string ClockText => LTime(DateTime.Now);

    public string FormattedOpeningBalance => LCur(PeriodSummary.OpeningBalance);
    public string FormattedNewLoans => LCur(PeriodSummary.NewLoans);
    public string FormattedPayments => LCur(PeriodSummary.Payments);
    public string FormattedClosingBalance => LCur(PeriodSummary.ClosingBalance);

    public bool HasData => RecentTransactions.Count > 0;
    public bool HasHistoricalData => HistoricalOutstanding.Count > 0;

    public string PageSubtitle => _localizationService.GetString("DashboardSubtitle");
    public string TotalBorrowersLabel => _localizationService.GetString("TotalBorrowers");
    public string ActiveLabel => _localizationService.GetString("Active");
    public string ClosedLabel => _localizationService.GetString("Closed");
    public string TotalOutstandingLabel => _localizationService.GetString("TotalOutstanding");
    public string CurrentBalanceLabel => _localizationService.GetString("CurrentBalance");
    public string TotalDepositsLabel => _localizationService.GetString("TotalDeposits");
    public string CumulativeReceivedLabel => _localizationService.GetString("CumulativeReceived");
    public string TotalWithdrawalsLabel => _localizationService.GetString("TotalWithdrawals");
    public string CumulativeDisbursedLabel => _localizationService.GetString("CumulativeDisbursed");
    public string InterestEarnedLabel => _localizationService.GetString("InterestEarned");
    public string TotalAccruedLabel => _localizationService.GetString("TotalAccrued");
    public string OutstandingOverviewLabel => _localizationService.GetString("OutstandingOverview");
    public string ThisMonthLabel => _localizationService.GetString("ThisMonth");
    public string NoHistoricalDataLabel => _localizationService.GetString("NoHistoricalData");
    public string OpeningLabel => _localizationService.GetString("Opening");
    public string NewLoansLabel => _localizationService.GetString("NewLoans");
    public string PaymentsLabel => _localizationService.GetString("Payments");
    public string ClosingLabel => _localizationService.GetString("Closing");
    public string RecentTransactionsLabel => _localizationService.GetString("RecentTransactions");
    public string ViewAllLabel => _localizationService.GetString("ViewAll");
    public string NoRecentTransactionsLabel => _localizationService.GetString("NoRecentTransactions");
    public string NoRecentTransactionsDesc => _localizationService.GetString("NoRecentTransactionsDesc");
    public string QuickActionsLabel => _localizationService.GetString("QuickActions");
    public string AddBorrowerLabel => _localizationService.GetString("AddBorrower");
    public string NewTransactionLabel => _localizationService.GetString("NewTransaction");
    public string ReceivePaymentLabel => _localizationService.GetString("ReceivePayment");
    public string GivePaymentLabel => _localizationService.GetString("GivePayment");
    public string GiveAmountLabel => GivePaymentLabel;
    public string GenerateReportLabel => _localizationService.GetString("GenerateReport");
    public string BackupNowLabel => _localizationService.GetString("BackupNow");
    public string ReadyLabel => _localizationService.GetString("Ready");
    public string RetryLabel => _localizationService.GetString("Retry");
    public string SearchLabel => _localizationService.GetString("Search");
    public string SearchPlaceholder => _localizationService.GetString("SearchBorrowersPlaceholder");
    public string NoBorrowerFoundLabel => _localizationService.GetString("NoSearchResultsTitle");
    public string CloseSearchLabel => _localizationService.GetString("CloseSearch");
    public string CloseSearchToolTipLabel => _localizationService.GetString("CloseSearchToolTip");

    private System.Threading.CancellationTokenSource? _searchCts;

    public bool IsSearchExpanded
    {
        get => _isSearchExpanded;
        set
        {
            if (SetProperty(ref _isSearchExpanded, value))
            {
                if (!value)
                {
                    _searchCts?.Cancel();
                    _searchTerm = string.Empty;
                    SearchResults.Clear();
                    HasSearchResults = false;
                    HasNoSearchResults = false;
                    OnPropertyChanged(nameof(SearchTerm));
                }
                OnPropertyChanged(nameof(IsSearchPopupOpen));
            }
        }
    }

    public bool IsSearchPopupOpen => IsSearchExpanded && !string.IsNullOrWhiteSpace(SearchTerm);

    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (_searchTerm != value)
            {
                _searchTerm = value;
                OnPropertyChanged(nameof(IsSearchPopupOpen));
                _ = ExecuteSearchAsync();
            }
        }
    }

    public ObservableCollection<SearchResult> SearchResults
    {
        get => _searchResults;
        private set => SetProperty(ref _searchResults, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set => SetProperty(ref _isSearching, value);
    }

    public bool HasSearchResults
    {
        get => _hasSearchResults;
        private set => SetProperty(ref _hasSearchResults, value);
    }

    public bool HasNoSearchResults
    {
        get => _hasNoSearchResults;
        private set => SetProperty(ref _hasNoSearchResults, value);
    }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand RetryCommand { get; }
    public RelayCommand ToggleSearchCommand { get; }
    public RelayCommand CloseSearchCommand { get; }
    public RelayCommand<SearchResult> SelectSearchResultCommand { get; }
    public RelayCommand NavigateToBorrowersCommand { get; }
    public RelayCommand NavigateToLedgerCommand { get; }
    public RelayCommand NavigateToDepositsCommand { get; }
    public RelayCommand NavigateToWithdrawalsCommand { get; }
    public RelayCommand NavigateToInterestCommand { get; }
    public RelayCommand NavigateToTransactionsCommand { get; }
    public RelayCommand AddBorrowerCommand { get; }
    public RelayCommand NewTransactionCommand { get; }
    public RelayCommand ReceivePaymentCommand { get; }
    public RelayCommand GivePaymentCommand { get; }
    public RelayCommand GiveAmountCommand { get; }
    public RelayCommand GenerateReportCommand { get; }
    public RelayCommand BackupNowCommand { get; }
    public RelayCommand OpenScanQrCommand { get; }
    public Func<Task>? RequestScanQr { get; set; }
    public string ScanQrLabel => _localizationService.GetString("ScanQr");

    private async Task ExecuteSearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new System.Threading.CancellationTokenSource();
        var ct = _searchCts.Token;

        var term = SearchTerm;
        if (string.IsNullOrWhiteSpace(term))
        {
            SearchResults.Clear();
            HasSearchResults = false;
            HasNoSearchResults = false;
            IsSearching = false;
            return;
        }

        IsSearching = true;
        try
        {
            await Task.Delay(100, ct);

            var filter = new SearchFilter(
                term,
                "All",
                null,
                null,
                null,
                null,
                null,
                "Date",
                true,
                1,
                20);

            var page = await _searchService.SearchAsync(filter, ct);

            if (ct.IsCancellationRequested) return;

            SearchResults.Clear();
            foreach (var item in page.Items)
            {
                SearchResults.Add(new SearchResult(item.EntityType, item.Id, item.Title, item.Subtitle, item.Status, item.Date, item.Amount));
            }

            HasSearchResults = SearchResults.Count > 0;
            HasNoSearchResults = SearchResults.Count == 0;
        }
        catch (OperationCanceledException)
        {
            // Ignore search cancellation for newer keystrokes
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard search failed.");
            SearchResults.Clear();
            HasSearchResults = false;
            HasNoSearchResults = true;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsSearching = false;
            }
        }
    }

    private void OnSelectSearchResult(SearchResult? result)
    {
        if (result == null) return;

        IsSearchExpanded = false;

        if (Guid.TryParse(result.Id, out var borrowerId) && borrowerId != Guid.Empty)
        {
            _navigationService.Navigate(NavigationDestination.BorrowerDetails, borrowerId);
        }
        else if (string.Equals(result.EntityType, "Borrower", StringComparison.OrdinalIgnoreCase))
        {
            _navigationService.Navigate(NavigationDestination.Borrowers);
        }
        else if (string.Equals(result.EntityType, "Transaction", StringComparison.OrdinalIgnoreCase))
        {
            _navigationService.Navigate(NavigationDestination.Transactions);
        }
    }

    private IReadOnlyList<DashboardMonthOption> BuildMonthOptions()
    {
        var now = DateTime.Today;
        var options = new List<DashboardMonthOption>(6);
        for (int i = 5; i >= 0; i--)
        {
            var monthDate = new DateTime(now.Year, now.Month, 1).AddMonths(-i);
            options.Add(new DashboardMonthOption(
                _localizationService.ToLocalizedDate(monthDate, "MMM"),
                monthDate.Year,
                monthDate.Month,
                monthDate.Year == now.Year && monthDate.Month == now.Month));
        }

        return options;
    }

    public async Task LoadYearlyDataForSelectedYearAsync(int year)
    {
        await Task.WhenAll(
            LoadYearlyChartDataAsync(year),
            LoadPeriodSummaryForYearAsync(year)
        );
    }

    public async Task LoadPeriodSummaryForYearAsync(int year)
    {
        try
        {
            var summary = await _dashboardService.GetYearlyPeriodSummaryAsync(year);
            PeriodSummary = summary;
            OnPropertyChanged(nameof(PeriodSummary));
            OnPropertyChanged(nameof(FormattedOpeningBalance));
            OnPropertyChanged(nameof(FormattedNewLoans));
            OnPropertyChanged(nameof(FormattedPayments));
            OnPropertyChanged(nameof(FormattedClosingBalance));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load period summary for year {Year}.", year);
            PeriodSummary = new PeriodSummaryInfo(0m, 0m, 0m, 0m);
            OnPropertyChanged(nameof(PeriodSummary));
            OnPropertyChanged(nameof(FormattedOpeningBalance));
            OnPropertyChanged(nameof(FormattedNewLoans));
            OnPropertyChanged(nameof(FormattedPayments));
            OnPropertyChanged(nameof(FormattedClosingBalance));
        }
    }

    private async Task LoadPeriodSummaryForMonthAsync(DashboardMonthOption option)
    {
        try
        {
            var summary = await _dashboardService.GetMonthlyPeriodSummaryAsync(option.Year, option.Month);
            PeriodSummary = summary;
            OnPropertyChanged(nameof(FormattedOpeningBalance));
            OnPropertyChanged(nameof(FormattedNewLoans));
            OnPropertyChanged(nameof(FormattedPayments));
            OnPropertyChanged(nameof(FormattedClosingBalance));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load period summary for {Year}-{Month:00}.", option.Year, option.Month);
        }
    }

    public async Task LoadYearlyChartDataAsync(int year)
    {
        try
        {
            var data = await _dashboardService.GetYearlyChartDataAsync(year);
            YearlyChartData = data;
            MonthlyGroups = new ObservableCollection<MonthlyChartGroup>(data.MonthlyGroups);
            YAxisTicks = new ObservableCollection<ChartYAxisTick>(data.YAxisTicks);
            OnPropertyChanged(nameof(HasYearlyChartData));
            OnPropertyChanged(nameof(YearlyChartData));
            OnPropertyChanged(nameof(YAxisMaxLabel));
            OnPropertyChanged(nameof(YAxis75Label));
            OnPropertyChanged(nameof(YAxis50Label));
            OnPropertyChanged(nameof(YAxis25Label));
            OnPropertyChanged(nameof(YAxisMinLabel));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load yearly chart data for {Year}.", year);
        }
    }

    public Task LoadAsync() => LoadAsync(null, null);

    public Task LoadAsync(DashboardMonthOption? preservedMonthOption) => LoadAsync(preservedMonthOption, null);

    public async Task LoadAsync(DashboardMonthOption? preservedMonthOption, DashboardYearOption? preservedYearOption)
    {
        _logger.LogInformation("[LIFECYCLE] DashboardViewModel.LoadAsync started.");
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var profileName = await _profileService.GetProfileNameAsync();
            _profileName = profileName ?? string.Empty;

            var summary = await _dashboardService.GetSummaryAsync();

            TotalBorrowers = summary.TotalBorrowers;
            ActiveBorrowers = summary.ActiveBorrowers;
            InactiveBorrowers = summary.InactiveBorrowers;
            ClosedBorrowers = summary.ClosedBorrowers;
            TotalDeposits = summary.TotalDeposits;
            TotalWithdrawals = summary.TotalWithdrawals;
            OutstandingAmount = summary.OutstandingAmount;
            TotalInterest = summary.TotalInterest;

            var currentLang = _localizationService.CurrentLanguage;
            var recent = summary.RecentTransactions
                .OrderByDescending(rt => rt.TransactionDate)
                .ThenByDescending(rt => rt.Id)
                .Select(rt => new RecentTransactionSummary(
                    rt.Id,
                    rt.Reference,
                    rt.TransactionType,
                    rt.TransactionTypeKey,
                    rt.Amount,
                    rt.TransactionDate,
                    _localizationService.LocalizeText(rt.Description, currentLang),
                    _translationService?.Translate(rt.BorrowerName, currentLang) ?? rt.BorrowerName))
                .ToList();

            RecentTransactions = new ObservableCollection<RecentTransactionSummary>(recent);
            HistoricalOutstanding = new ObservableCollection<HistoricalOutstandingPoint>(summary.HistoricalOutstanding);
            LastUpdated = DateTime.Now;

            // Load Available Years for the Yearly Grouped Chart
            var availableYears = await _dashboardService.GetAvailableYearsAsync();
            var yearOpts = new List<DashboardYearOption>();
            var currentYear = DateTime.Today.Year;
            foreach (var y in availableYears)
            {
                yearOpts.Add(new DashboardYearOption(LocalizeDigits(y.ToString()), y, y == currentYear));
            }
            YearOptions = yearOpts;

            DashboardYearOption? targetYearOption = null;
            if (preservedYearOption != null)
            {
                targetYearOption = YearOptions.FirstOrDefault(o => o.Year == preservedYearOption.Year);
            }
            else if (SelectedYearOption != null)
            {
                targetYearOption = YearOptions.FirstOrDefault(o => o.Year == SelectedYearOption.Year);
            }

            if (targetYearOption == null)
            {
                targetYearOption = YearOptions.FirstOrDefault(o => o.IsCurrentYear) ?? YearOptions.FirstOrDefault();
            }

            _isSettingYearOption = true;
            try
            {
                SelectedYearOption = targetYearOption;
            }
            finally
            {
                _isSettingYearOption = false;
            }

            if (targetYearOption != null)
            {
                await LoadYearlyDataForSelectedYearAsync(targetYearOption.Year);
            }

            MonthOptions = BuildMonthOptions();

            DashboardMonthOption? targetOption = null;
            if (preservedMonthOption != null)
            {
                targetOption = MonthOptions.FirstOrDefault(o => o.Year == preservedMonthOption.Year && o.Month == preservedMonthOption.Month);
            }

            if (targetOption == null)
            {
                foreach (var option in MonthOptions)
                {
                    if (option.IsCurrentMonth)
                    {
                        targetOption = option;
                        break;
                    }
                }
            }

            targetOption ??= MonthOptions.LastOrDefault();

            _isSettingMonthOption = true;
            try
            {
                SelectedMonthOption = targetOption;
            }
            finally
            {
                _isSettingMonthOption = false;
            }

            if (targetOption != null)
            {
                if (targetOption.IsCurrentMonth)
                {
                    PeriodSummary = summary.PeriodSummary;
                }
                else
                {
                    await LoadPeriodSummaryForMonthAsync(targetOption);
                }
            }
            else
            {
                PeriodSummary = summary.PeriodSummary;
            }

            OnPropertyChanged(nameof(FormattedTotalDeposits));
            OnPropertyChanged(nameof(FormattedTotalWithdrawals));
            OnPropertyChanged(nameof(FormattedOutstandingAmount));
            OnPropertyChanged(nameof(FormattedTotalInterest));
            OnPropertyChanged(nameof(FormattedLastUpdated));
            OnPropertyChanged(nameof(FormattedOpeningBalance));
            OnPropertyChanged(nameof(FormattedNewLoans));
            OnPropertyChanged(nameof(FormattedPayments));
            OnPropertyChanged(nameof(FormattedClosingBalance));
            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(HasHistoricalData));
            OnPropertyChanged(nameof(HasYearlyChartData));
            OnPropertyChanged(nameof(PageTitle));

            _logger.LogInformation("[LIFECYCLE] Dashboard loaded successfully. Borrowers={TotalBorrowers}, Transactions={TransactionCount}.", TotalBorrowers, RecentTransactions.Count);

            // Asynchronously check for application updates in the background without blocking UI or startup
            _ = CheckForUpdatesInBackgroundAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[LIFECYCLE] Failed to load dashboard data.");
            HasError = true;
            ErrorMessage = _localizationService.GetString("DashboardLoadFailed");
        }
        finally
        {
            IsLoading = false;
            _logger.LogInformation("[LIFECYCLE] DashboardViewModel.LoadAsync finished.");
        }
    }

    #region Update Notification Bell

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set => SetProperty(ref _isUpdateAvailable, value);
    }

    public string AvailableUpdateVersion
    {
        get => _availableUpdateVersion;
        private set
        {
            if (SetProperty(ref _availableUpdateVersion, value))
            {
                OnPropertyChanged(nameof(UpdateBadgeSubtitle));
                OnPropertyChanged(nameof(AvailableUpdateText));
            }
        }
    }

    public string CurrentInstalledVersion
    {
        get => _currentInstalledVersion;
        private set
        {
            if (SetProperty(ref _currentInstalledVersion, value))
            {
                OnPropertyChanged(nameof(CurrentVersionText));
            }
        }
    }

    public string UpdateBadgeSubtitle => string.IsNullOrWhiteSpace(AvailableUpdateVersion)
        ? _localizationService.GetString("UpdateAvailableBadge")
        : string.Format(_localizationService.GetString("VersionAvailableFormat"), LocalizeDigits(AvailableUpdateVersion));

    public string AvailableUpdateText => string.IsNullOrWhiteSpace(AvailableUpdateVersion)
        ? _localizationService.GetString("NewVersionAvailableFormat")
        : string.Format(_localizationService.GetString("VersionIsAvailableFormat"), LocalizeDigits(AvailableUpdateVersion));

    public string CurrentVersionText => string.IsNullOrWhiteSpace(CurrentInstalledVersion)
        ? string.Empty
        : string.Format(_localizationService.GetString("CurrentVersionFormat"), LocalizeDigits(CurrentInstalledVersion));

    public RelayCommand ViewUpdateCommand { get; }

    public async Task CheckForUpdatesInBackgroundAsync()
    {
        if (_updateService == null) return;

        try
        {
            var update = await _updateService.CheckForUpdatesAsync(force: false).ConfigureAwait(false);
            if (update != null)
            {
                RunOnUiThread(() =>
                {
                    AvailableUpdateVersion = update.Version;
                    CurrentInstalledVersion = _updateService.CurrentVersion;
                    IsUpdateAvailable = true;
                });
            }
            else
            {
                RunOnUiThread(() =>
                {
                    IsUpdateAvailable = false;
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Non-fatal error during background update check.");
            RunOnUiThread(() =>
            {
                IsUpdateAvailable = false;
            });
        }
    }

    private void OpenReleasePage()
    {
        try
        {
            var url = "https://github.com/bhargav1822/DhirDhar-Releases/releases/latest";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open release page.");
        }
    }

    #endregion

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_transactionEventService != null)
        {
            _transactionEventService.TransactionChanged -= OnTransactionChanged;
        }

        if (_clockTimer != null)
        {
            _clockTimer.Stop();
            _clockTimer.Tick -= OnClockTick;
        }

        _localizationService.LanguageChanged -= OnLanguageChangedHandler;

        lock (_refreshLock)
        {
            _transactionRefreshDebounceCts?.Cancel();
            _transactionRefreshDebounceCts?.Dispose();
            _transactionRefreshDebounceCts = null;
        }

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }
}
