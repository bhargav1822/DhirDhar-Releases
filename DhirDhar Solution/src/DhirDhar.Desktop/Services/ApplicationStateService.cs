using DhirDhar.Application.Localization;
using DhirDhar.Desktop.Navigation;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.Services;

public sealed class ApplicationStateService : IApplicationStateService
{
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<ApplicationStateService> _logger;

    private bool _databaseReady;
    private bool _applicationReady;
    private NavigationDestination _currentNavigationDestination = NavigationDestination.Dashboard;

    public ApplicationStateService(
        ILocalizationService localizationService,
        ILogger<ApplicationStateService> logger)
    {
        _localizationService = localizationService;
        _logger = logger;
    }

    public bool DatabaseReady
    {
        get => _databaseReady;
        private set => _databaseReady = value;
    }

    public bool ApplicationReady
    {
        get => _applicationReady;
        private set => _applicationReady = value;
    }

    public NavigationDestination CurrentNavigationDestination
    {
        get => _currentNavigationDestination;
        private set => _currentNavigationDestination = value;
    }

    public string CurrentLanguage => _localizationService.CurrentLanguage;

    public void SetDatabaseReady()
    {
        DatabaseReady = true;
        _logger.LogInformation("Database marked as ready.");
    }

    public void SetApplicationReady()
    {
        ApplicationReady = true;
        _logger.LogInformation("Application marked as ready.");
    }

    public void SetCurrentNavigationDestination(NavigationDestination destination)
    {
        CurrentNavigationDestination = destination;
        _logger.LogDebug("Current navigation destination set to '{Destination}'.", destination);
    }
}
