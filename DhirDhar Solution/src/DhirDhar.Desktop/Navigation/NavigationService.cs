using Microsoft.Extensions.Logging;

namespace DhirDhar.Desktop.Navigation;

public sealed class NavigationService : INavigationService
{
    private readonly ILogger<NavigationService> _logger;

    private NavigationDestination _currentDestination = NavigationDestination.Dashboard;
    private readonly Stack<NavigationDestination> _history = new();
    private readonly HashSet<NavigationDestination> _registeredDestinations =
    [
        NavigationDestination.Dashboard,
        NavigationDestination.Borrowers,
        NavigationDestination.BorrowerDetails,
        NavigationDestination.Transactions,
        NavigationDestination.Interest,
        NavigationDestination.Ledger,
        NavigationDestination.Reports,
        NavigationDestination.Search,
        NavigationDestination.BackupRestore,
        NavigationDestination.Security,
        NavigationDestination.Integrity,
        NavigationDestination.Settings
    ];

    public NavigationService(ILogger<NavigationService> logger)
    {
        _logger = logger;
    }

    public event EventHandler<NavigationState>? NavigationChanged;

    public NavigationDestination CurrentDestination => _currentDestination;

    public bool CanNavigate(NavigationDestination destination)
    {
        return _registeredDestinations.Contains(destination);
    }

    public void Navigate(NavigationDestination destination, object? parameter = null)
    {
        if (!CanNavigate(destination))
        {
            _logger.LogWarning("Navigation to destination '{Destination}' is not registered.", destination);
            return;
        }

        if (_currentDestination != destination)
        {
            _history.Push(_currentDestination);
            _currentDestination = destination;
        }

        _logger.LogInformation("Navigated to '{Destination}'.", destination);
        NavigationChanged?.Invoke(this, new NavigationState(destination, parameter));
    }

    public void GoBack()
    {
        if (_history.Count == 0)
        {
            return;
        }

        var previous = _history.Pop();
        _currentDestination = previous;

        _logger.LogInformation("Navigated back to '{Destination}'.", previous);
        NavigationChanged?.Invoke(this, new NavigationState(previous, null));
    }
}
