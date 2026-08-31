using DhirDhar.Desktop.Configuration;
using DhirDhar.Desktop.Navigation;
using DhirDhar.Desktop.Services;
using DhirDhar.Desktop.Updates;
using DhirDhar.Desktop.Updates.UI;
using DhirDhar.Desktop.ViewModels;
using DhirDhar.Desktop.ViewModels.Borrowers;
using DhirDhar.Desktop.ViewModels.Backup;
using DhirDhar.Desktop.ViewModels.Interest;
using DhirDhar.Desktop.ViewModels.Ledger;
using DhirDhar.Desktop.ViewModels.Reports;
using DhirDhar.Desktop.ViewModels.Search;
using DhirDhar.Desktop.ViewModels.Security;
using DhirDhar.Desktop.ViewModels.Settings;
using DhirDhar.Desktop.ViewModels.Transactions;
using DhirDhar.Desktop.Views.Backup;
using DhirDhar.Desktop.Views.Borrowers;
using DhirDhar.Desktop.Views.Dashboard;
using DhirDhar.Desktop.Views.Integrity;
using DhirDhar.Desktop.Views.Interest;
using DhirDhar.Desktop.Views.Ledger;
using DhirDhar.Desktop.Views.Loading;
using DhirDhar.Desktop.Views.Reports;
using DhirDhar.Desktop.Views.Search;
using DhirDhar.Desktop.Views.Security;
using DhirDhar.Desktop.Views.Settings;
using DhirDhar.Desktop.Views.Shell;
using DhirDhar.Desktop.Views.Transactions;
using DhirDhar.Desktop.Views.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace DhirDhar.Desktop.DependencyInjection;

public static class DesktopServiceRegistration
{
    public static IServiceCollection AddDesktop(this IServiceCollection services)
    {
        services.AddSingleton(AppOptionsExtensions.LoadFromConfiguration);

        services.AddSingleton<IApplicationStartupService, ApplicationStartupService>();
        services.AddSingleton<IApplicationStateService, ApplicationStateService>();
        services.AddSingleton<IErrorHandler, ErrorHandler>();
        services.AddSingleton<DhirDharInputEngine>();
        services.AddSingleton<IDhirDharInputEngine>(sp => sp.GetRequiredService<DhirDharInputEngine>());
        services.AddSingleton<IInputLanguageService>(sp => sp.GetRequiredService<DhirDharInputEngine>());
        services.AddSingleton<IAppTypographyService, AppTypographyService>();
        services.AddSingleton<IImageCacheService, ImageCacheService>();

        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IUpdateDialogService, UpdateDialogService>();
        services.AddTransient<UpdateNotificationViewModel>();
        services.AddTransient<UpdateNotificationDialog>();

        services.AddSingleton<INavigationService, NavigationService>();

        services.AddTransient<LoadingViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();

        services.AddTransient<BorrowersViewModel>();
        services.AddTransient<BorrowerEditViewModel>();
        services.AddTransient<BorrowerDetailsViewModel>();

        services.AddTransient<TransactionsViewModel>();
        services.AddTransient<InterestViewModel>();
        services.AddTransient<LedgerViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<BackupRestoreViewModel>();
        services.AddTransient<SecurityViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<IntegrityViewModel>();
        services.AddTransient<DhirDhar.Desktop.ViewModels.Settings.SettingsViewModel>();
        services.AddTransient<DhirDhar.Desktop.ViewModels.License.LicenseViewModel>();

        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainPage>();
        services.AddTransient<LoadingPage>();
        services.AddTransient<DhirDhar.Desktop.Views.License.LicenseActivationPage>();
        services.AddTransient<DhirDhar.Desktop.Views.License.LicenseRenewalDialog>();

        services.AddTransient<DashboardPage>();
        services.AddTransient<BorrowersPage>();
        services.AddTransient<BorrowerEditPage>();
        services.AddTransient<BorrowerDetailsPage>();
        services.AddTransient<TransactionsPage>();
        services.AddTransient<InterestPage>();
        services.AddTransient<LedgerPage>();
        services.AddTransient<ReportsPage>();
        services.AddTransient<SearchPage>();
        services.AddTransient<BackupRestorePage>();
        services.AddTransient<SecurityPage>();
        services.AddTransient<IntegrityPage>();
        services.AddTransient<DhirDhar.Desktop.Views.Settings.SettingsPage>();

        return services;
    }

    private static class AppOptionsExtensions
    {
        public static AppOptions LoadFromConfiguration(IServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            return configuration.LoadAppOptions();
        }
    }
}
