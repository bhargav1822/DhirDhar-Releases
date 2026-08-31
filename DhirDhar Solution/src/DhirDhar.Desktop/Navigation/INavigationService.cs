namespace DhirDhar.Desktop.Navigation;

public interface INavigationService
{
    event EventHandler<NavigationState>? NavigationChanged;

    NavigationDestination CurrentDestination { get; }

    bool CanNavigate(NavigationDestination destination);

    void Navigate(NavigationDestination destination, object? parameter = null);

    void GoBack();
}
