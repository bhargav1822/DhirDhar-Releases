using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Audit;
using DhirDhar.Application.Backup;
using DhirDhar.Application.Security;
using DhirDhar.Application.Security.Keys;
using DhirDhar.Application.Security.Models;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Security;

public sealed class EncryptionMigrationService : IEncryptionMigrationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IKeyManagementService _keyManagementService;
    private readonly IDataEncryptionService _dataEncryptionService;
    private readonly IPhotoEncryptionService _photoEncryptionService;
    private readonly IBackupService _backupService;
    private readonly IAuditService _auditService;
    private readonly ILogger<EncryptionMigrationService> _logger;

    public EncryptionMigrationService(
        IServiceScopeFactory scopeFactory,
        IKeyManagementService keyManagementService,
        IDataEncryptionService dataEncryptionService,
        IPhotoEncryptionService photoEncryptionService,
        IBackupService backupService,
        IAuditService auditService,
        ILogger<EncryptionMigrationService> logger)
    {
        _scopeFactory = scopeFactory;
        _keyManagementService = keyManagementService;
        _dataEncryptionService = dataEncryptionService;
        _photoEncryptionService = photoEncryptionService;
        _backupService = backupService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<bool> IsMigrationRequiredAsync(CancellationToken cancellationToken = default)
    {
        if (!_keyManagementService.IsMasterKeyInitialized())
        {
            return true;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

        var migrationFlag = await db.ApplicationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == "Security.EncryptionMigrationCompleted", cancellationToken)
            .ConfigureAwait(false);

        return migrationFlag == null || !string.Equals(migrationFlag.Value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<MigrationResult> MigrateExistingDataAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        _logger.LogInformation("Starting End-to-End Encryption migration for existing database records.");

        // 1. Safety Backup
        string? backupPath = null;
        try
        {
            var safetyBackup = await _backupService.CreateSafetyBackupAsync(cancellationToken).ConfigureAwait(false);
            backupPath = safetyBackup.Location;
            _logger.LogInformation("Created pre-encryption safety backup at '{Path}'.", backupPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create pre-encryption safety backup. Aborting migration.");
            return new MigrationResult(false, 0, 0, 0, null, new[] { $"Failed to create pre-encryption safety backup: {ex.Message}" });
        }

        // 2. Initialize Keys
        try
        {
            await _keyManagementService.InitializeMasterKeyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize master encryption keys.");
            return new MigrationResult(false, 0, 0, 0, backupPath, new[] { $"Key initialization failed: {ex.Message}" });
        }

        int borrowersCount = 0;
        int transactionsCount = 0;
        int photosCount = 0;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

        try
        {
            var borrowers = await db.Borrowers.ToListAsync(cancellationToken).ConfigureAwait(false);
            borrowersCount = borrowers.Count;

            foreach (var b in borrowers)
            {
                // Migrate photos if plaintext files exist
                if (!string.IsNullOrWhiteSpace(b.BorrowerPhotoPath) && File.Exists(b.BorrowerPhotoPath) && !_photoEncryptionService.IsPhotoEncrypted(b.BorrowerPhotoPath))
                {
                    try
                    {
                        var encPath = await _photoEncryptionService.EncryptAndStorePhotoAsync(b.BorrowerPhotoPath, "borrower", cancellationToken).ConfigureAwait(false);
                        b.SetPhotosAndLoanType(encPath, b.OrnamentPhotoPath, b.LoanType, b.OrnamentType, b.OrnamentWeight, b.LoanAmount, b.LoanDate, b.InterestRate);
                        photosCount++;
                    }
                    catch (Exception pEx)
                    {
                        _logger.LogWarning(pEx, "Failed to encrypt borrower photo '{Path}' during migration.", b.BorrowerPhotoPath);
                    }
                }

                if (!string.IsNullOrWhiteSpace(b.OrnamentPhotoPath) && File.Exists(b.OrnamentPhotoPath) && !_photoEncryptionService.IsPhotoEncrypted(b.OrnamentPhotoPath))
                {
                    try
                    {
                        var encPath = await _photoEncryptionService.EncryptAndStorePhotoAsync(b.OrnamentPhotoPath, "ornament", cancellationToken).ConfigureAwait(false);
                        b.SetPhotosAndLoanType(b.BorrowerPhotoPath, encPath, b.LoanType, b.OrnamentType, b.OrnamentWeight, b.LoanAmount, b.LoanDate, b.InterestRate);
                        photosCount++;
                    }
                    catch (Exception pEx)
                    {
                        _logger.LogWarning(pEx, "Failed to encrypt ornament photo '{Path}' during migration.", b.OrnamentPhotoPath);
                    }
                }
            }

            var transactions = await db.Transactions.ToListAsync(cancellationToken).ConfigureAwait(false);
            transactionsCount = transactions.Count;

            // Mark migration complete setting
            var setting = await db.ApplicationSettings.FirstOrDefaultAsync(s => s.Key == "Security.EncryptionMigrationCompleted", cancellationToken).ConfigureAwait(false);
            if (setting == null)
            {
                db.ApplicationSettings.Add(new Domain.Entities.ApplicationSetting("Security.EncryptionMigrationCompleted", "true"));
            }
            else
            {
                setting.UpdateValue("true");
            }

            var algoSetting = await db.ApplicationSettings.FirstOrDefaultAsync(s => s.Key == "Security.EncryptionAlgorithm", cancellationToken).ConfigureAwait(false);
            if (algoSetting == null)
            {
                db.ApplicationSettings.Add(new Domain.Entities.ApplicationSetting("Security.EncryptionAlgorithm", "AES-256-GCM / PBKDF2-SHA256"));
            }
            else
            {
                algoSetting.UpdateValue("AES-256-GCM / PBKDF2-SHA256");
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await _auditService.RecordAsync(new AuditEvent(
                "EncryptionMigrationCompleted",
                "Security",
                null,
                $"E2EE data migration completed successfully. {borrowersCount} borrowers and {transactionsCount} transactions verified.",
                "SUCCESS",
                null,
                null), cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Encryption migration completed successfully. Borrowers: {Borrowers}, Transactions: {Transactions}, Photos: {Photos}.", borrowersCount, transactionsCount, photosCount);
            return new MigrationResult(true, borrowersCount, transactionsCount, photosCount, backupPath, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Encryption migration encountered an error.");
            errors.Add(ex.Message);
            return new MigrationResult(false, borrowersCount, transactionsCount, photosCount, backupPath, errors);
        }
    }
}
