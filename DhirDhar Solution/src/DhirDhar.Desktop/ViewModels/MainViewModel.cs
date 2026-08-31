using DhirDhar.Desktop.Configuration;
using DhirDhar.Desktop.Navigation;
using DhirDhar.Desktop.Services;

namespace DhirDhar.Desktop.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly DhirDhar.Application.Localization.ILocalizationService _localizationService;

    private string _applicationTitle = string.Empty;
    private NavigationDestination _currentDestination = NavigationDestination.Dashboard;
    private bool _isNavigationEnabled = true;
    private bool _isSidebarExpanded = true;

    public MainViewModel(
        AppOptions appOptions,
        INavigationService navigationService,
        IApplicationStateService stateService,
        DhirDhar.Application.Localization.ILocalizationService localizationService)
    {
        _navigationService = navigationService;
        _localizationService = localizationService;
        ApplicationTitle = appOptions.Name;
        ApplicationVersion = appOptions.Version;

        _navigationService.NavigationChanged += OnNavigationChanged;
        _localizationService.LanguageChanged += (s, e) => OnPropertyChanged(string.Empty);
    }

    public string ApplicationTitle
    {
        get => _applicationTitle;
        private set => SetProperty(ref _applicationTitle, value);
    }

    public string ApplicationVersion { get; }

    public string FormattedApplicationVersion => $"{_localizationService.GetString("Version")} {LocalizeDigits(ApplicationVersion)}";

    public string FormattedFinancialYear
    {
        get
        {
            var now = DateTime.Now;
            var year = now.Month >= 4 ? now.Year : now.Year - 1;
            return LocalizeDigits($"{_localizationService.GetString("FinancialYearPrefix")} {year}-{((year + 1) % 100):D2}");
        }
    }

    public NavigationDestination CurrentDestination
    {
        get => _currentDestination;
        private set => SetProperty(ref _currentDestination, value);
    }

    public bool IsNavigationEnabled
    {
        get => _isNavigationEnabled;
        private set => SetProperty(ref _isNavigationEnabled, value);
    }

    public string CurrentLanguage => _localizationService.CurrentLanguage;

    public string NavDashboardText => _localizationService.GetString("Dashboard");
    public string NavBorrowersText => _localizationService.GetString("Borrowers");
    public string NavTransactionsText => _localizationService.GetString("Transactions");
    public string NavInterestText => _localizationService.GetString("Interest");
    public string NavLedgerText => _localizationService.GetString("Ledger");
    public string NavReportsText => _localizationService.GetString("Reports");
    public string NavBackupText => _localizationService.GetString("Backup");
    public string NavSecurityText => _localizationService.GetString("Security");
    public string NavIntegrityText => _localizationService.GetString("Integrity");
    public string NavSettingsText => _localizationService.GetString("Settings");
    public string MenuLabel => _localizationService.GetString("Menu");
    public string BusinessOptionsLabel => _localizationService.GetString("BusinessOptions");
    public string RenewLicenseLabel => _localizationService.GetString("RenewLicense");

    public bool IsSidebarExpanded
    {
        get => _isSidebarExpanded;
        private set => SetProperty(ref _isSidebarExpanded, value);
    }

    public void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
    }

    public INavigationService NavigationService => _navigationService;

    private void OnNavigationChanged(object? sender, NavigationState state)
    {
        CurrentDestination = state.Destination;
    }
}
