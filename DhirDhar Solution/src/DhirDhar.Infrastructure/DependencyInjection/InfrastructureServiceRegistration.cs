using DhirDhar.Application.Abstractions.Persistence;
using DhirDhar.Application.Abstractions.Persistence.Repositories;
using DhirDhar.Application.Abstractions.Services;
using DhirDhar.Application.Dashboard;
using DhirDhar.Application.Backup;
using DhirDhar.Application.Interest;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Ledger;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Notifications;
using DhirDhar.Application.Reports;
using DhirDhar.Application.Search;
using DhirDhar.Application.Security;
using DhirDhar.Application.Transactions;
using DhirDhar.Application.Validation;
using DhirDhar.Application.Profiles;
using DhirDhar.Application.Licensing;
using DhirDhar.Application.QrCode;
using DhirDhar.Application.Caching;
using DhirDhar.Infrastructure.Caching;
using DhirDhar.Infrastructure.Backup;
using DhirDhar.Infrastructure.Borrowers;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.Dashboard;
using DhirDhar.Infrastructure.Interest;
using DhirDhar.Infrastructure.Ledger;
using DhirDhar.Infrastructure.Localization;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Persistence.Repositories;
using DhirDhar.Infrastructure.QrCode;
using DhirDhar.Infrastructure.Reports;
using DhirDhar.Infrastructure.Search;
using DhirDhar.Infrastructure.Security;
using DhirDhar.Infrastructure.Services;
using DhirDhar.Infrastructure.Transactions;
using DhirDhar.Infrastructure.Validation;
using DhirDhar.Infrastructure.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DhirDhar.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring infrastructure services in the DI container.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<BackupOptions>(configuration.GetSection(BackupOptions.SectionName));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.Configure<LoggingOptions>(configuration.GetSection(LoggingOptions.SectionName));
        services.Configure<LocalizationOptions>(configuration.GetSection(LocalizationOptions.SectionName));

        return services.AddPersistenceServices();
    }

    /// <summary>
    /// Registers infrastructure-layer services using a manually supplied options instance,
    /// useful for tests that do not need a full configuration root.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, DatabaseOptions databaseOptions)
    {
        ArgumentNullException.ThrowIfNull(databaseOptions);

        services.AddSingleton<IOptions<DatabaseOptions>>(new OptionsWrapper<DatabaseOptions>(databaseOptions));
        services.AddSingleton<IOptions<BackupOptions>>(new OptionsWrapper<BackupOptions>(new BackupOptions()));

        return services.AddPersistenceServices();
    }

    private static IServiceCollection AddPersistenceServices(this IServiceCollection services)
    {
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 10000;
        });
        services.AddSingleton<ICacheService, MemoryCacheService>();

        services.AddSingleton<IDateTimeService, DateTimeService>();
        services.AddSingleton<IDatabasePathService, DatabasePathService>();
        services.AddSingleton<IDatabaseLifecycleService, DatabaseLifecycleService>();

        services.AddDbContext<DhirDharDbContext>((serviceProvider, optionsBuilder) =>
        {
            var pathService = serviceProvider.GetRequiredService<IDatabasePathService>();
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            DbContextOptionsFactory.Apply(optionsBuilder, pathService, databaseOptions);
        });

        services.AddDbContextFactory<DhirDharDbContext>((serviceProvider, optionsBuilder) =>
        {
            var pathService = serviceProvider.GetRequiredService<IDatabasePathService>();
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            DbContextOptionsFactory.Apply(optionsBuilder, pathService, databaseOptions);
        });

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();
        services.AddTransient<IDashboardService, DashboardService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IInterestCalculationService, InterestCalculationService>();
        services.AddScoped<ILedgerService, LedgerService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IPdfExportService, PdfExportService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IGoogleDriveService, GoogleDriveService>();
        services.AddSingleton<DhirDhar.Application.Backup.IBackupSchedulerService, DhirDhar.Infrastructure.Backup.BackupSchedulerService>();
        services.AddSingleton<IDateLocalizationService, DateLocalizationService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ITranslationService, TranslationService>();
        services.AddSingleton<IIndicTransliterationService, IndicTransliterationService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<DhirDhar.Application.Settings.ISettingsService, DhirDhar.Infrastructure.Settings.SettingsService>();
        services.AddSingleton<ISecurityService, SecurityService>();
        services.AddSingleton<IIdempotencyService, DhirDhar.Infrastructure.Validation.IdempotencyService>();
        services.AddSingleton<INotificationService, DhirDhar.Infrastructure.Notifications.NotificationService>();
        services.AddSingleton<DhirDhar.Application.Audit.IAuditService, DhirDhar.Infrastructure.Audit.AuditService>();
        services.AddScoped<IFinancialValidationService, FinancialValidationService>();
        services.AddSingleton<IIntegrityService, DhirDhar.Infrastructure.Validation.IntegrityService>();
        services.AddScoped<IBorrowerService, BorrowerService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddSingleton<DhirDhar.Application.Security.Cryptography.ICryptoService, DhirDhar.Infrastructure.Security.Cryptography.CryptoService>();
        services.AddSingleton<DhirDhar.Application.Security.Keys.IKeyManagementService, DhirDhar.Infrastructure.Security.Keys.KeyManagementService>();
        services.AddSingleton<DhirDhar.Application.Security.IDataEncryptionService, DhirDhar.Infrastructure.Security.DataEncryptionService>();
        services.AddSingleton<DhirDhar.Application.Security.IPhotoEncryptionService, DhirDhar.Infrastructure.Security.PhotoEncryptionService>();
        services.AddScoped<DhirDhar.Application.Security.IEncryptionMigrationService, DhirDhar.Infrastructure.Security.EncryptionMigrationService>();

        services.AddSingleton<IDeviceFingerprintService, DhirDhar.Infrastructure.Licensing.DeviceFingerprintService>();
        services.AddSingleton<DhirDhar.Application.Licensing.ILicenseStorageService, DhirDhar.Infrastructure.Licensing.LicenseStorageService>();
        services.AddSingleton<DhirDhar.Application.Licensing.ILicenseManager, DhirDhar.Infrastructure.Licensing.LicenseManager>();
        services.AddSingleton<DhirDhar.Application.Security.Integrity.IApplicationIntegrityService, DhirDhar.Infrastructure.Security.Integrity.ApplicationIntegrityService>();
        services.AddSingleton<IQrCodeService, QrCodeService>();
        services.AddSingleton<DhirDhar.Application.Printing.IPrintService, DhirDhar.Infrastructure.Printing.WindowsPrinterService>();
        services.AddSingleton<ITransactionEventService, TransactionEventService>();

        return services;
    }
}
