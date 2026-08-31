using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Audit;
using DhirDhar.Application.Backup;
using DhirDhar.Application.Interest;
using DhirDhar.Application.Ledger;
using DhirDhar.Application.Notifications;
using DhirDhar.Application.Security.Keys;
using DhirDhar.Application.Validation;
using DhirDhar.Application.Validation.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Validation;

public sealed class IntegrityService : IIntegrityService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAuditService _auditService;
    private readonly INotificationService _notificationService;
    private readonly IBackupService _backupService;
    private readonly ILogger<IntegrityService> _logger;

    public IntegrityService(
        IServiceScopeFactory scopeFactory,
        IAuditService auditService,
        INotificationService notificationService,
        IBackupService backupService,
        ILogger<IntegrityService> logger)
    {
        _scopeFactory = scopeFactory;
        _auditService = auditService;
        _notificationService = notificationService;
        _backupService = backupService;
        _logger = logger;
    }

    public async Task<IntegrityScanReport> RunIntegrityScanAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting database integrity scan.");

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
        var interestService = scope.ServiceProvider.GetRequiredService<IInterestCalculationService>();
        var ledgerService = scope.ServiceProvider.GetRequiredService<ILedgerService>();
        var keyManagementService = scope.ServiceProvider.GetService<IKeyManagementService>();

        var borrowers = await dbContext.Borrowers
            .AsNoTracking()
            .Include(b => b.Loans)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var transactions = await dbContext.Transactions
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var borrowerIssues = new List<IntegrityIssue>();
        var transactionIssues = new List<IntegrityIssue>();
        var ledgerIssues = new List<IntegrityIssue>();
        var interestIssues = new List<IntegrityIssue>();
        var financialTotalIssues = new List<IntegrityIssue>();
        var relationshipIssues = new List<IntegrityIssue>();
        var securityIssues = new List<IntegrityIssue>();

        // 1. Borrower Integrity Checks
        var duplicateNumbers = borrowers
            .Where(b => !string.IsNullOrWhiteSpace(b.BorrowerNumber))
            .GroupBy(b => b.BorrowerNumber.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in duplicateNumbers)
        {
            foreach (var b in group)
            {
                borrowerIssues.Add(new IntegrityIssue(
                    "Borrowers",
                    IntegritySeverityLevel.Critical,
                    nameof(Borrower),
                    b.Id.ToString(),
                    $"Duplicate Borrower Number '{b.BorrowerNumber}' detected.",
                    "ERR_DUPLICATE_BORROWER_NUMBER",
                    $"Borrower ID: {b.Id}, Name: {b.Name}, Number: {b.BorrowerNumber}",
                    "Ensure every borrower has a unique borrower number. Update duplicate numbers in Borrower Profile.",
                    "Duplicate Borrower Number",
                    b.BorrowerNumber,
                    false,
                    null));
            }
        }

        foreach (var b in borrowers)
        {
            var effectiveStartDate = (b.LoanDate ?? b.EntryDate).Date;

            if (b.ClosedDate.HasValue && b.ClosedDate.Value.Date < effectiveStartDate)
            {
                borrowerIssues.Add(new IntegrityIssue(
                    "Borrowers",
                    IntegritySeverityLevel.High,
                    nameof(Borrower),
                    b.Id.ToString(),
                    $"Closed date ({b.ClosedDate.Value:yyyy-MM-dd}) pre-dates account start date ({effectiveStartDate:yyyy-MM-dd}).",
                    "ERR_INVALID_CLOSED_DATE",
                    $"Borrower '{b.Name}' ({b.BorrowerNumber}), EntryDate: {b.EntryDate:yyyy-MM-dd}, LoanDate: {b.LoanDate:yyyy-MM-dd}, ClosedDate: {b.ClosedDate.Value:yyyy-MM-dd}",
                    "Update closed date to be on or after account loan/entry date.",
                    "Invalid Closed Date",
                    b.BorrowerNumber,
                    false,
                    null));
            }

            if (b.Status == BorrowerStatus.Closed && !b.ClosedDate.HasValue)
            {
                borrowerIssues.Add(new IntegrityIssue(
                    "Borrowers",
                    IntegritySeverityLevel.Warning,
                    nameof(Borrower),
                    b.Id.ToString(),
                    "Borrower status is Closed but ClosedDate is missing.",
                    "ERR_MISSING_CLOSED_DATE",
                    $"Borrower '{b.Name}' ({b.BorrowerNumber}), UpdatedAt: {b.UpdatedAt:yyyy-MM-dd}",
                    "Set valid ClosedDate on closed borrower account.",
                    "Missing Closed Date",
                    b.BorrowerNumber,
                    true,
                    "FIX_MISSING_CLOSED_DATE"));
            }
        }

        // 2. Transaction Integrity Checks
        foreach (var t in transactions)
        {
            var borrower = t.BorrowerId.HasValue ? borrowers.FirstOrDefault(b => b.Id == t.BorrowerId.Value) : null;

            if (t.Amount.Amount <= 0m)
            {
                transactionIssues.Add(new IntegrityIssue(
                    "Transactions",
                    IntegritySeverityLevel.Critical,
                    nameof(Transaction),
                    t.Id.ToString(),
                    $"Invalid non-positive transaction amount '{t.Amount.Amount}'.",
                    "ERR_INVALID_TRANSACTION_AMOUNT",
                    $"Transaction ID: {t.Id}, Reference: {t.Reference}, Type: {t.Type}, Amount: {t.Amount.Amount}",
                    "Financial transaction amounts must be strictly positive.",
                    "Invalid Transaction Amount",
                    borrower?.BorrowerNumber,
                    false,
                    null));
            }

            if (decimal.Round(t.Amount.Amount, 2, MidpointRounding.AwayFromZero) != t.Amount.Amount)
            {
                transactionIssues.Add(new IntegrityIssue(
                    "Transactions",
                    IntegritySeverityLevel.High,
                    nameof(Transaction),
                    t.Id.ToString(),
                    $"Transaction amount precision exceeds 2 decimal places: '{t.Amount.Amount}'.",
                    "ERR_INVALID_AMOUNT_PRECISION",
                    $"Transaction ID: {t.Id}, Reference: {t.Reference}, Amount: {t.Amount.Amount}",
                    "Financial amounts must be rounded to standard 2-decimal scale.",
                    "Invalid Amount Precision",
                    borrower?.BorrowerNumber,
                    false,
                    null));
            }

            if (t.BorrowerId.HasValue)
            {
                if (borrower is null)
                {
                    relationshipIssues.Add(new IntegrityIssue(
                        "Relationships",
                        IntegritySeverityLevel.Critical,
                        nameof(Transaction),
                        t.Id.ToString(),
                        $"Orphan transaction references non-existent Borrower ID '{t.BorrowerId.Value}'.",
                        "ERR_ORPHAN_TRANSACTION",
                        $"Transaction ID: {t.Id}, Reference: {t.Reference}, Borrower ID: {t.BorrowerId.Value}",
                        "Re-associate transaction with a valid borrower.",
                        "Orphan Transaction",
                        null,
                        false,
                        null));
                }
                else
                {
                    var effectiveStartDate = (borrower.LoanDate ?? borrower.EntryDate).Date;
                    if (t.OccurredOn.Date < effectiveStartDate)
                    {
                        transactionIssues.Add(new IntegrityIssue(
                            "Transactions",
                            IntegritySeverityLevel.High,
                            nameof(Transaction),
                            t.Id.ToString(),
                            $"Transaction date ({t.OccurredOn:yyyy-MM-dd}) is earlier than borrower account start date ({effectiveStartDate:yyyy-MM-dd}).",
                            "ERR_TRANSACTION_DATE_ORDERING",
                            $"Transaction '{t.Reference}' on {t.OccurredOn:yyyy-MM-dd} for Borrower '{borrower.Name}' (Start: {effectiveStartDate:yyyy-MM-dd})",
                            "Adjust transaction date to be on or after borrower entry/loan date.",
                            "Transaction Pre-Dates Loan Start",
                            borrower.BorrowerNumber,
                            true,
                            "ALIGN_ENTRY_DATE"));
                    }

                    // A transaction on the account closure date (e.g. final payoff/settlement) is valid.
                    // Only transactions strictly after the account closure calendar date are post-closure violations.
                    if (borrower.Status == BorrowerStatus.Closed && borrower.ClosedDate.HasValue && t.OccurredOn.Date > borrower.ClosedDate.Value.Date)
                    {
                        transactionIssues.Add(new IntegrityIssue(
                            "Transactions",
                            IntegritySeverityLevel.Critical,
                            nameof(Transaction),
                            t.Id.ToString(),
                            $"Post-closure transaction detected on ({t.OccurredOn:yyyy-MM-dd}) after account closed date ({borrower.ClosedDate.Value:yyyy-MM-dd}).",
                            "ERR_POST_CLOSURE_TRANSACTION",
                            $"Transaction '{t.Reference}' on {t.OccurredOn:yyyy-MM-dd} for Borrower '{borrower.Name}' ({borrower.BorrowerNumber}, Closed: {borrower.ClosedDate.Value:yyyy-MM-dd})",
                            "Financial transactions post-account closure are prohibited.",
                            "Post-Closure Transaction Detected",
                            borrower.BorrowerNumber,
                            false,
                            null));
                    }
                }
            }
        }

        // 3. Ledger, Interest & Financial Consistency Checks
        foreach (var b in borrowers)
        {
            try
            {
                var interestResult = await interestService.CalculateAsync(b.Id, DateTime.Today, cancellationToken).ConfigureAwait(false);
                var ledgerSummary = await ledgerService.GetSummaryAsync(b.Id, cancellationToken).ConfigureAwait(false);

                if (b.Status == BorrowerStatus.Closed && b.ClosedDate.HasValue)
                {
                    var postClosureSegments = interestResult.Segments
                        .Where(s => s.SegmentStartDate.Date > b.ClosedDate.Value.Date && s.CalculatedInterest > 0m)
                        .ToList();

                    if (postClosureSegments.Count > 0)
                    {
                        interestIssues.Add(new IntegrityIssue(
                            "Interest",
                            IntegritySeverityLevel.Critical,
                            nameof(Borrower),
                            b.Id.ToString(),
                            $"Post-closure interest calculated for closed borrower '{b.Name}'.",
                            "ERR_POST_CLOSURE_INTEREST",
                            $"Post-closure interest segments count: {postClosureSegments.Count}, ClosedDate: {b.ClosedDate.Value:yyyy-MM-dd}",
                            "Interest calculation engine must halt interest accumulation on account closed date.",
                            "Post-Closure Interest Accrual",
                            b.BorrowerNumber,
                            false,
                            null));
                    }
                }

                // Check ledger representation
                var borrowerTxns = transactions.Where(t => t.BorrowerId == b.Id).ToList();
                var depSum = borrowerTxns.Where(t => t.Type == TransactionType.Deposit).Sum(t => t.Amount.Amount);
                var withSum = borrowerTxns.Where(t => t.Type == TransactionType.Withdrawal).Sum(t => t.Amount.Amount);

                if (ledgerSummary.TotalDeposits != depSum || ledgerSummary.TotalWithdrawals != withSum)
                {
                    ledgerIssues.Add(new IntegrityIssue(
                        "Ledger",
                        IntegritySeverityLevel.Critical,
                        nameof(Borrower),
                        b.Id.ToString(),
                        $"Ledger totals mismatch for Borrower '{b.Name}'. Ledger Deposits: {ledgerSummary.TotalDeposits}, Txn Deposits: {depSum}; Ledger Withdrawals: {ledgerSummary.TotalWithdrawals}, Txn Withdrawals: {withSum}.",
                        "ERR_LEDGER_REPRESENTATION_MISMATCH",
                        $"Borrower ID: {b.Id}, Name: {b.Name}, Number: {b.BorrowerNumber}",
                        "Re-sync ledger entries with authoritative transaction history.",
                        "Ledger Representation Mismatch",
                        b.BorrowerNumber,
                        false,
                        null));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed financial consistency check for borrower '{BorrowerId}'", b.Id);
                financialTotalIssues.Add(new IntegrityIssue(
                    "FinancialState",
                    IntegritySeverityLevel.High,
                    nameof(Borrower),
                    b.Id.ToString(),
                    $"Failed to verify financial state for Borrower '{b.Name}': {ex.Message}",
                    "ERR_FINANCIAL_CALCULATION_FAILED",
                    $"Borrower ID: {b.Id}, Error: {ex.Message}",
                    "Verify interest rate periods and loan configurations for this borrower.",
                    "Financial State Verification Error",
                    b.BorrowerNumber,
                    false,
                    null));
            }
        }

        // 4. Security & Encryption Integrity Checks
        if (keyManagementService != null)
        {
            try
            {
                if (!keyManagementService.IsMasterKeyInitialized())
                {
                    securityIssues.Add(new IntegrityIssue(
                        "Security",
                        IntegritySeverityLevel.Warning,
                        "MasterKey",
                        "MasterKeyStorage",
                        "Master encryption key is not yet initialized.",
                        "ERR_ENCRYPTION_KEY_NOT_INITIALIZED",
                        "Master encryption key file is missing from secure local storage.",
                        "Initialize encryption keys in Settings -> Security & Encryption.",
                        "Encryption Key Not Initialized",
                        null,
                        true,
                        "INITIALIZE_ENCRYPTION"));
                }
                else
                {
                    var isCryptValid = await keyManagementService.VerifyEncryptionIntegrityAsync(cancellationToken).ConfigureAwait(false);
                    if (!isCryptValid)
                    {
                        securityIssues.Add(new IntegrityIssue(
                            "Security",
                            IntegritySeverityLevel.Critical,
                            "MasterKey",
                            "MasterKeyStorage",
                            "Cryptographic integrity verification failed for local master key.",
                            "ERR_ENCRYPTION_VERIFICATION_FAILED",
                            "Failed to decrypt or authenticate test vector with local DPAPI key.",
                            "Verify user account permissions or recover master key via Recovery Key.",
                            "Encryption Verification Failed",
                            null,
                            false,
                            null));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to verify security encryption status.");
            }
        }

        // Build Category Reports
        var categories = new List<IntegrityCategoryReport>
        {
            BuildCategoryReport("Borrowers", borrowers.Count, borrowerIssues),
            BuildCategoryReport("Transactions", transactions.Count, transactionIssues),
            BuildCategoryReport("Ledger", transactions.Count, ledgerIssues),
            BuildCategoryReport("Interest", borrowers.Count, interestIssues),
            BuildCategoryReport("Financial State", borrowers.Count, financialTotalIssues),
            BuildCategoryReport("Relationships", borrowers.Count + transactions.Count, relationshipIssues),
            BuildCategoryReport("Security & Encryption", 1, securityIssues)
        };

        var overallStatus = categories.Select(c => c.Status).DefaultIfEmpty(IntegrityStatus.Pass).Max();
        stopwatch.Stop();

        var report = new IntegrityScanReport(
            overallStatus,
            borrowers.Count,
            transactions.Count,
            transactions.Count,
            categories.Sum(c => c.IssueCount),
            categories,
            DateTime.UtcNow,
            stopwatch.Elapsed);

        // Audit Record
        await _auditService.RecordAsync(new AuditEvent(
            "IntegrityScanExecuted",
            "Database",
            null,
            $"Integrity scan completed. Status: {overallStatus}, Issues: {report.TotalIssuesFound}.",
            overallStatus == IntegrityStatus.Pass ? "SUCCESS" : "WARNING",
            null,
            JsonSerializer.Serialize(new { report.OverallStatus, report.TotalIssuesFound, ElapsedMs = stopwatch.ElapsedMilliseconds })),
            cancellationToken).ConfigureAwait(false);

        // Notification for High / Critical Issues
        if (overallStatus >= IntegrityStatus.High)
        {
            await _notificationService.SendNotificationAsync(
                "Data Integrity Issue Detected",
                $"Integrity scan found {report.TotalIssuesFound} issue(s). Overall Status: {overallStatus}.",
                overallStatus == IntegrityStatus.Critical ? NotificationSeverity.Critical : NotificationSeverity.High,
                "Integrity",
                cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Integrity scan finished in {ElapsedMs}ms with status '{OverallStatus}'.", stopwatch.ElapsedMilliseconds, overallStatus);
        return report;
    }

    public async Task<FinancialValidationResult> RepairIssueAsync(string repairActionKey, string entityId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repairActionKey))
        {
            return FinancialValidationResult.Failure("Repair action key is required.");
        }

        _logger.LogInformation("Starting safe repair operation '{ActionKey}' for entity '{EntityId}'.", repairActionKey, entityId);

        // 1. Create safety backup first to prevent any accidental data loss
        try
        {
            var backup = await _backupService.CreateSafetyBackupAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Created safety backup '{BackupFile}' prior to repair.", backup.Location);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create safety backup prior to repair.");
            return FinancialValidationResult.Failure($"Failed to create safety backup before repair: {ex.Message}");
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

        switch (repairActionKey)
        {
            case "INITIALIZE_ENCRYPTION":
            {
                var keyMgmt = scope.ServiceProvider.GetRequiredService<IKeyManagementService>();
                await keyMgmt.InitializeMasterKeyAsync(cancellationToken).ConfigureAwait(false);
                return FinancialValidationResult.Success();
            }

            case "FIX_MISSING_CLOSED_DATE":
            {
                if (!Guid.TryParse(entityId, out var borrowerId))
                {
                    return FinancialValidationResult.Failure("Invalid borrower entity ID.");
                }

                var borrower = await dbContext.Borrowers
                    .Include(b => b.Transactions)
                    .FirstOrDefaultAsync(b => b.Id == borrowerId, cancellationToken)
                    .ConfigureAwait(false);

                if (borrower == null)
                {
                    return FinancialValidationResult.Failure($"Borrower '{entityId}' not found.");
                }

                if (borrower.Status == BorrowerStatus.Closed && !borrower.ClosedDate.HasValue)
                {
                    var latestTxnDate = borrower.Transactions.Count > 0
                        ? borrower.Transactions.Max(t => t.OccurredOn).Date
                        : borrower.UpdatedAt.Date;

                    borrower.CloseAccount(latestTxnDate);
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await _auditService.RecordAsync(new AuditEvent(
                        "BorrowerRepaired",
                        "Borrowers",
                        borrower.Id.ToString(),
                        $"Set missing ClosedDate to '{latestTxnDate:yyyy-MM-dd}' for closed borrower '{borrower.Name}' ({borrower.BorrowerNumber}).",
                        "SUCCESS",
                        null,
                        null), cancellationToken).ConfigureAwait(false);

                    return FinancialValidationResult.Success();
                }

                return FinancialValidationResult.Success();
            }

            case "ALIGN_ENTRY_DATE":
            {
                if (!Guid.TryParse(entityId, out var borrowerId))
                {
                    return FinancialValidationResult.Failure("Invalid borrower entity ID.");
                }

                var borrower = await dbContext.Borrowers
                    .Include(b => b.Transactions)
                    .FirstOrDefaultAsync(b => b.Id == borrowerId, cancellationToken)
                    .ConfigureAwait(false);

                if (borrower == null)
                {
                    return FinancialValidationResult.Failure($"Borrower '{entityId}' not found.");
                }

                var earliestTxnDate = borrower.Transactions.Count > 0 ? borrower.Transactions.Min(t => t.OccurredOn) : (DateTime?)null;
                var earliestDate = borrower.LoanDate.HasValue
                    ? (earliestTxnDate.HasValue && earliestTxnDate.Value < borrower.LoanDate.Value ? earliestTxnDate.Value : borrower.LoanDate.Value)
                    : earliestTxnDate;

                if (earliestDate.HasValue && borrower.EntryDate.Date > earliestDate.Value.Date)
                {
                    borrower.SetEntryDate(earliestDate.Value.Date);
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    await _auditService.RecordAsync(new AuditEvent(
                        "BorrowerRepaired",
                        "Borrowers",
                        borrower.Id.ToString(),
                        $"Aligned entry date to earliest transaction date '{earliestDate.Value:yyyy-MM-dd}' for borrower '{borrower.Name}' ({borrower.BorrowerNumber}).",
                        "SUCCESS",
                        null,
                        null), cancellationToken).ConfigureAwait(false);

                    return FinancialValidationResult.Success();
                }

                return FinancialValidationResult.Success();
            }

            default:
                return FinancialValidationResult.Failure($"Unsupported repair action key '{repairActionKey}'.");
        }
    }

    public Task<FinancialValidationResult> ValidateImportPayloadAsync(string rawPayload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return Task.FromResult(FinancialValidationResult.Failure("Import payload cannot be empty."));
        }

        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            var root = doc.RootElement;

            var errors = new List<string>();
            if (root.ValueKind != JsonValueKind.Object && root.ValueKind != JsonValueKind.Array)
            {
                errors.Add("Import payload must be a valid JSON object or array.");
            }

            return Task.FromResult(errors.Count == 0
                ? FinancialValidationResult.Success()
                : FinancialValidationResult.Failure(errors));
        }
        catch (JsonException ex)
        {
            return Task.FromResult(FinancialValidationResult.Failure($"Invalid JSON format in import payload: {ex.Message}"));
        }
    }

    public Task<FinancialValidationResult> ValidateRestorePackageAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            return Task.FromResult(FinancialValidationResult.Failure("Backup file path is invalid or file does not exist."));
        }

        var errors = new List<string>();
        var fileInfo = new FileInfo(backupPath);
        if (fileInfo.Length == 0)
        {
            errors.Add("Backup file is empty (0 bytes).");
        }

        var ext = Path.GetExtension(backupPath).ToLowerInvariant();
        if (ext != ".zip" && ext != ".db" && ext != ".bak" && ext != ".ddbackup")
        {
            errors.Add($"Unsupported backup format '{ext}'. Expected .ddbackup, .zip, .db, or .bak.");
        }

        return Task.FromResult(errors.Count == 0
            ? FinancialValidationResult.Success()
            : FinancialValidationResult.Failure(errors));
    }

    private static IntegrityCategoryReport BuildCategoryReport(string categoryName, int totalChecked, List<IntegrityIssue> issues)
    {
        var maxSeverity = issues.Select(i => i.Severity).DefaultIfEmpty(IntegritySeverityLevel.Info).Max();
        var status = maxSeverity switch
        {
            IntegritySeverityLevel.Critical => IntegrityStatus.Critical,
            IntegritySeverityLevel.High => IntegrityStatus.High,
            IntegritySeverityLevel.Warning => IntegrityStatus.Warning,
            _ => IntegrityStatus.Pass
        };

        return new IntegrityCategoryReport(
            categoryName,
            status,
            totalChecked,
            issues.Count,
            issues);
    }
}
