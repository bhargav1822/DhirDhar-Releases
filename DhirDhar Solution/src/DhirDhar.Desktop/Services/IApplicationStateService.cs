namespace DhirDhar.Desktop.Services;

public interface IApplicationStateService
{
    bool DatabaseReady { get; }
    bool ApplicationReady { get; }
    Navigation.NavigationDestination CurrentNavigationDestination { get; }
    string CurrentLanguage { get; }

    void SetDatabaseReady();
    void SetApplicationReady();
    void SetCurrentNavigationDestination(Navigation.NavigationDestination destination);
}
