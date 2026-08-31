using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Common.Exceptions;
using DhirDhar.Application.Localization;
using DhirDhar.Domain.Common;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using DhirDhar.Application.Caching;
using DhirDhar.Application.Transactions;

namespace DhirDhar.Infrastructure.Borrowers;

public sealed class BorrowerService : IBorrowerService
{
    private static readonly SemaphoreSlim _numberLock = new(1, 1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BorrowerService> _logger;
    private readonly ICacheService? _cacheService;
    private readonly ITransactionEventService? _transactionEventService;

    public BorrowerService(
        IServiceScopeFactory scopeFactory,
        ILogger<BorrowerService> logger,
        ICacheService? cacheService = null,
        ITransactionEventService? transactionEventService = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cacheService = cacheService ?? scopeFactory.CreateScope().ServiceProvider.GetService<ICacheService>();
        _transactionEventService = transactionEventService ?? scopeFactory.CreateScope().ServiceProvider.GetService<ITransactionEventService>();
    }

    public void InvalidateBorrowerCaches(Guid? borrowerId = null, string? borrowerNumber = null)
    {
        if (borrowerId.HasValue)
        {
            _cacheService?.Remove($"borrower_id_{borrowerId.Value}");
        }
        if (!string.IsNullOrWhiteSpace(borrowerNumber))
        {
            var clean = borrowerNumber.Trim().TrimStart('#').Trim();
            _cacheService?.Remove($"borrower_num_{clean}");
            _cacheService?.Remove($"borrower_num_#{clean}");
        }
        _cacheService?.RemoveByPrefix("borrowers_page_");
        _cacheService?.Remove("dashboard_summary");
        _cacheService?.RemoveByPrefix("search_query_");

        _transactionEventService?.PublishTransactionChanged(new TransactionChangedEventArgs(null, borrowerId, TransactionMutationKind.Adjusted));
    }

    public async Task<string> GetNextBorrowerNumberAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
        return await GetNextBorrowerNumberInternalAsync(dbContext, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetBorrowerPrefixAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
        return await GetBorrowerPrefixInternalAsync(dbContext, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> GetBorrowerPrefixInternalAsync(DhirDharDbContext dbContext, CancellationToken cancellationToken)
    {
        var businessNameSetting = await dbContext.ApplicationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == SettingsService.BusinessNameKey, cancellationToken)
            .ConfigureAwait(false);

        var businessName = string.IsNullOrWhiteSpace(businessNameSetting?.Value)
            ? BorrowerNumberHelper.DefaultBusinessName
            : businessNameSetting.Value.Trim();

        return BorrowerNumberHelper.GeneratePrefixFromBusinessName(businessName);
    }

    public async Task<BorrowerSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"borrower_id_{id}";
        if (_cacheService != null)
        {
            var cached = _cacheService.Get<BorrowerSummary>(cacheKey);
            if (cached != null)
            {
                return cached;
            }
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var borrower = await dbContext.Borrowers
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (borrower is null)
            {
                return null;
            }

            var depList = await dbContext.Transactions
                .Where(t => t.BorrowerId == id && t.Type == TransactionType.Deposit)
                .Select(t => t.Amount.Amount)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var totalDeposits = depList.Sum();

            var withList = await dbContext.Transactions
                .Where(t => t.BorrowerId == id && t.Type == TransactionType.Withdrawal)
                .Select(t => t.Amount.Amount)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var totalWithdrawals = withList.Sum();

            var lastTransactionDate = await dbContext.Transactions
                .Where(t => t.BorrowerId == id)
                .OrderByDescending(t => t.OccurredOn)
                .Select(t => (DateTime?)t.OccurredOn)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var summary = new BorrowerSummary(
                borrower.Id,
                borrower.BorrowerNumber,
                borrower.Name,
                borrower.Contact,
                borrower.Status.ToString(),
                borrower.EntryDate,
                totalDeposits,
                totalWithdrawals,
                totalDeposits - totalWithdrawals,
                lastTransactionDate,
                borrower.FatherName,
                borrower.Surname,
                borrower.Village,
                borrower.AadharNumber,
                borrower.BorrowerPhotoPath,
                borrower.OrnamentPhotoPath,
                borrower.LoanType,
                borrower.OrnamentType,
                borrower.OrnamentWeight,
                borrower.LoanAmount,
                borrower.LoanDate,
                borrower.InterestRate,
                borrower.ClosedDate,
                borrower.ClosingAmount,
                borrower.ClosedAccruedInterest);

            _cacheService?.Set(cacheKey, summary, slidingExpiration: TimeSpan.FromMinutes(2), absoluteExpiration: TimeSpan.FromMinutes(10));
            if (!string.IsNullOrWhiteSpace(summary.BorrowerNumber))
            {
                var clean = summary.BorrowerNumber.Trim().TrimStart('#').Trim();
                _cacheService?.Set($"borrower_num_{clean}", summary, slidingExpiration: TimeSpan.FromMinutes(2), absoluteExpiration: TimeSpan.FromMinutes(10));
            }

            return summary;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to get borrower by ID '{BorrowerId}'.", id);
            throw new InvalidOperationException("Failed to retrieve borrower details.", exception);
        }
    }

    public async Task<BorrowerSummary?> GetByBorrowerNumberAsync(string borrowerNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(borrowerNumber))
        {
            return null;
        }

        var raw = borrowerNumber.Trim();
        var noHash = raw.TrimStart('#').Trim();
        var cacheKey = $"borrower_num_{noHash}";

        if (_cacheService != null)
        {
            var cached = _cacheService.Get<BorrowerSummary>(cacheKey);
            if (cached != null)
            {
                return cached;
            }
        }

        try
        {
            var withHash = $"#{noHash}";

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var borrower = await dbContext.Borrowers
                .AsNoTracking()
                .FirstOrDefaultAsync(b =>
                    b.BorrowerNumber == raw ||
                    b.BorrowerNumber == noHash ||
                    b.BorrowerNumber == withHash ||
                    EF.Functions.Like(b.BorrowerNumber, raw) ||
                    EF.Functions.Like(b.BorrowerNumber, noHash) ||
                    EF.Functions.Like(b.BorrowerNumber, withHash), cancellationToken)
                .ConfigureAwait(false);

            if (borrower is null)
            {
                return null;
            }

            var depList = await dbContext.Transactions
                .Where(t => t.BorrowerId == borrower.Id && t.Type == TransactionType.Deposit)
                .Select(t => t.Amount.Amount)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var totalDeposits = depList.Sum();

            var withList = await dbContext.Transactions
                .Where(t => t.BorrowerId == borrower.Id && t.Type == TransactionType.Withdrawal)
                .Select(t => t.Amount.Amount)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var totalWithdrawals = withList.Sum();

            var lastTransactionDate = await dbContext.Transactions
                .Where(t => t.BorrowerId == borrower.Id)
                .OrderByDescending(t => t.OccurredOn)
                .Select(t => (DateTime?)t.OccurredOn)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var summary = new BorrowerSummary(
                borrower.Id,
                borrower.BorrowerNumber,
                borrower.Name,
                borrower.Contact,
                borrower.Status.ToString(),
                borrower.EntryDate,
                totalDeposits,
                totalWithdrawals,
                totalDeposits - totalWithdrawals,
                lastTransactionDate,
                borrower.FatherName,
                borrower.Surname,
                borrower.Village,
                borrower.AadharNumber,
                borrower.BorrowerPhotoPath,
                borrower.OrnamentPhotoPath,
                borrower.LoanType,
                borrower.OrnamentType,
                borrower.OrnamentWeight,
                borrower.LoanAmount,
                borrower.LoanDate,
                borrower.InterestRate,
                borrower.ClosedDate,
                borrower.ClosingAmount,
                borrower.ClosedAccruedInterest);

            _cacheService?.Set(cacheKey, summary, slidingExpiration: TimeSpan.FromMinutes(2), absoluteExpiration: TimeSpan.FromMinutes(10));
            _cacheService?.Set($"borrower_id_{summary.Id}", summary, slidingExpiration: TimeSpan.FromMinutes(2), absoluteExpiration: TimeSpan.FromMinutes(10));

            return summary;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to get borrower by number '{BorrowerNumber}'.", borrowerNumber);
            throw new InvalidOperationException("Failed to retrieve borrower details by number.", exception);
        }
    }

    public async Task<BorrowerListResult> GetListAsync(
        BorrowerFilter filter,
        string? searchTerm = null,
        int page = 1,
        int pageSize = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var query = dbContext.Borrowers.AsNoTracking().AsQueryable();

            query = ApplyFilter(query, filter);
            query = ApplySearch(query, searchTerm);

            var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

            IQueryable<Domain.Entities.Borrower> pagedQuery = query
                .OrderBy(b => b.BorrowerNumber.Length)
                .ThenBy(b => b.BorrowerNumber);

            if (pageSize > 0)
            {
                page = Math.Max(1, page);
                pagedQuery = pagedQuery.Skip((page - 1) * pageSize).Take(pageSize);
            }
            else
            {
                page = 1;
                pageSize = totalCount;
            }

            var borrowers = await pagedQuery
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var borrowerIds = borrowers.Select(b => b.Id).ToList();

            var depositsDict = new Dictionary<Guid, decimal>();
            var withdrawalsDict = new Dictionary<Guid, decimal>();
            var lastTxnDatesDict = new Dictionary<Guid, DateTime?>();

            if (borrowerIds.Count > 0)
            {
                const int chunkSize = 500;
                for (int i = 0; i < borrowerIds.Count; i += chunkSize)
                {
                    var chunk = borrowerIds.Skip(i).Take(chunkSize).ToList();
                    var txns = await dbContext.Transactions
                        .AsNoTracking()
                        .Where(t => t.BorrowerId.HasValue && chunk.Contains(t.BorrowerId.Value))
                        .Select(t => new
                        {
                            BorrowerId = t.BorrowerId!.Value,
                            t.Type,
                            Amount = t.Amount.Amount,
                            t.OccurredOn
                        })
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    foreach (var g in txns.GroupBy(t => t.BorrowerId))
                    {
                        var depSum = g.Where(t => t.Type == TransactionType.Deposit).Sum(t => t.Amount);
                        var withSum = g.Where(t => t.Type == TransactionType.Withdrawal).Sum(t => t.Amount);
                        var maxDate = g.OrderByDescending(t => t.OccurredOn).Select(t => (DateTime?)t.OccurredOn).FirstOrDefault();

                        depositsDict[g.Key] = depSum;
                        withdrawalsDict[g.Key] = withSum;
                        lastTxnDatesDict[g.Key] = maxDate;
                    }
                }
            }

            var summaries = new List<BorrowerSummary>(borrowers.Count);
            foreach (var borrower in borrowers)
            {
                var totalDeposits = depositsDict.GetValueOrDefault(borrower.Id, 0m);
                var totalWithdrawals = withdrawalsDict.GetValueOrDefault(borrower.Id, 0m);
                var lastTransactionDate = lastTxnDatesDict.GetValueOrDefault(borrower.Id, null);

                summaries.Add(new BorrowerSummary(
                    borrower.Id,
                    borrower.BorrowerNumber,
                    borrower.Name,
                    borrower.Contact,
                    borrower.Status.ToString(),
                    borrower.EntryDate,
                    totalDeposits,
                    totalWithdrawals,
                    totalDeposits - totalWithdrawals,
                    lastTransactionDate,
                    borrower.FatherName,
                    borrower.Surname,
                    borrower.Village,
                    borrower.AadharNumber,
                    borrower.BorrowerPhotoPath,
                    borrower.OrnamentPhotoPath,
                    borrower.LoanType,
                    borrower.OrnamentType,
                    borrower.OrnamentWeight,
                    borrower.LoanAmount,
                    borrower.LoanDate,
                    borrower.InterestRate,
                    borrower.ClosedDate,
                    borrower.ClosingAmount,
                    borrower.ClosedAccruedInterest));
            }

            return new BorrowerListResult(summaries, totalCount, page, pageSize);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to get borrower list.");
            throw new InvalidOperationException("Failed to retrieve borrower list.", exception);
        }
    }

    public async Task<BorrowerSummary> CreateAsync(CreateBorrowerRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Borrower name is required.");
        }

        ValidateCreateRequest(request);

        await _numberLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var prefix = await GetBorrowerPrefixInternalAsync(dbContext, cancellationToken).ConfigureAwait(false);
            string finalBorrowerNumber;
            long assignedNumericValue = 0;

            if (!string.IsNullOrWhiteSpace(request.BorrowerNumber))
            {
                // User entered or confirmed a specific borrower sequence or number (e.g., "1002", "DS 1002")
                if (BorrowerNumberHelper.TryParseSequence(request.BorrowerNumber, prefix, out var parsedSeq) && parsedSeq > 0)
                {
                    var formattedNumber = BorrowerNumberHelper.FormatBorrowerNumber(prefix, parsedSeq);

                    bool exists = await dbContext.Borrowers
                        .AnyAsync(b => b.BorrowerNumber == formattedNumber, cancellationToken)
                        .ConfigureAwait(false);

                    if (exists)
                    {
                        throw new ValidationException("Borrower number already exists.");
                    }

                    finalBorrowerNumber = formattedNumber;
                    assignedNumericValue = parsedSeq;
                }
                else
                {
                    // Fallback to auto-generation
                    finalBorrowerNumber = await GetNextBorrowerNumberInternalAsync(dbContext, cancellationToken).ConfigureAwait(false);
                    BorrowerNumberHelper.TryParseSequence(finalBorrowerNumber, prefix, out assignedNumericValue);
                }
            }
            else
            {
                finalBorrowerNumber = await GetNextBorrowerNumberInternalAsync(dbContext, cancellationToken).ConfigureAwait(false);
                BorrowerNumberHelper.TryParseSequence(finalBorrowerNumber, prefix, out assignedNumericValue);
            }

            // Update persistent numeric watermark for prefix
            if (assignedNumericValue > 0)
            {
                var sequenceSettingKey = $"BorrowerSequence_{prefix}";
                var sequenceSetting = await dbContext.ApplicationSettings
                    .FirstOrDefaultAsync(s => s.Key == sequenceSettingKey, cancellationToken)
                    .ConfigureAwait(false);

                if (sequenceSetting == null)
                {
                    dbContext.ApplicationSettings.Add(new ApplicationSetting(
                        sequenceSettingKey,
                        assignedNumericValue.ToString(CultureInfo.InvariantCulture)));
                }
                else if (long.TryParse(sequenceSetting.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var currentWatermark))
                {
                    if (assignedNumericValue > currentWatermark)
                    {
                        sequenceSetting.UpdateValue(assignedNumericValue.ToString(CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    sequenceSetting.UpdateValue(assignedNumericValue.ToString(CultureInfo.InvariantCulture));
                }
            }

            var effectiveEntryDate = request.EntryDate != default
                ? (request.LoanDate.HasValue && request.LoanDate.Value.Date < request.EntryDate.Date ? request.LoanDate.Value.Date : request.EntryDate)
                : (request.LoanDate ?? DateTime.Today);

            var borrower = new Borrower(
                finalBorrowerNumber,
                request.Name.Trim(),
                request.FatherName?.Trim(),
                request.Surname?.Trim(),
                request.Village?.Trim(),
                NormalizeDigits(request.Contact),
                request.Address?.Trim(),
                request.Notes?.Trim(),
                NormalizeDigits(request.AadharNumber),
                effectiveEntryDate);

            borrower.SetPhotosAndLoanType(request.BorrowerPhotoPath, request.OrnamentPhotoPath, request.LoanType, request.OrnamentType, request.OrnamentWeight, request.LoanAmount, request.LoanDate, request.InterestRate);

            dbContext.Borrowers.Add(borrower);

            if (request.LoanAmount.HasValue && request.LoanAmount.Value > 0m && request.LoanDate.HasValue)
            {
                var periodId = await GetOrCreateCurrentPeriodAsync(dbContext, cancellationToken).ConfigureAwait(false);
                var initialLoanTxn = new Transaction(
                    borrower.Id,
                    periodId,
                    Money.Create(request.LoanAmount.Value),
                    TransactionType.Withdrawal,
                    request.LoanDate.Value,
                    "Initial Loan Amount",
                    $"INIT-{borrower.BorrowerNumber}");
                dbContext.Transactions.Add(initialLoanTxn);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            InvalidateBorrowerCaches(borrower.Id, borrower.BorrowerNumber);
            _logger.LogInformation("Borrower created. ID='{BorrowerId}', Number='{BorrowerNumber}'.", borrower.Id, borrower.BorrowerNumber);

            return new BorrowerSummary(
                borrower.Id,
                borrower.BorrowerNumber,
                borrower.Name,
                borrower.Contact,
                borrower.Status.ToString(),
                borrower.EntryDate,
                0m, 0m, 0m, null,
                borrower.FatherName,
                borrower.Surname,
                borrower.Village,
                borrower.AadharNumber,
                borrower.BorrowerPhotoPath,
                borrower.OrnamentPhotoPath,
                borrower.LoanType,
                borrower.OrnamentType,
                borrower.OrnamentWeight,
                borrower.LoanAmount,
                borrower.LoanDate,
                borrower.InterestRate,
                null,
                null,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true ||
                                            dbEx.Message.Contains("IX_Borrowers_BorrowerNumber", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(dbEx, "Database uniqueness violation creating borrower.");
            throw new ValidationException("Borrower number already exists.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to create borrower.");
            throw new InvalidOperationException("Failed to create borrower.", exception);
        }
        finally
        {
            _numberLock.Release();
        }
    }

    public async Task<BorrowerSummary> UpdateAsync(UpdateBorrowerRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Borrower name is required.");
        }

        ValidateUpdateRequest(request);

        await _numberLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var borrower = await dbContext.Borrowers
                .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
                .ConfigureAwait(false);

            if (borrower is null)
            {
                throw new NotFoundException($"Borrower with ID '{request.Id}' not found.");
            }

            var oldBorrowerNumber = borrower.BorrowerNumber;

            // Handle borrower number update if user explicitly supplied or modified it
            if (!string.IsNullOrWhiteSpace(request.BorrowerNumber) &&
                !string.Equals(request.BorrowerNumber.Trim(), borrower.BorrowerNumber, StringComparison.OrdinalIgnoreCase))
            {
                var prefix = await GetBorrowerPrefixInternalAsync(dbContext, cancellationToken).ConfigureAwait(false);

                if (!BorrowerNumberHelper.TryParseSequence(request.BorrowerNumber, prefix, out var parsedSeq) || parsedSeq <= 0)
                {
                    throw new ValidationException("Borrower number must contain only numeric digits.");
                }

                var formattedNumber = BorrowerNumberHelper.FormatBorrowerNumber(prefix, parsedSeq);

                bool isDuplicate = await dbContext.Borrowers
                    .AnyAsync(b => b.Id != request.Id && b.BorrowerNumber == formattedNumber, cancellationToken)
                    .ConfigureAwait(false);

                if (isDuplicate)
                {
                    throw new ValidationException("Borrower number already exists.");
                }

                borrower.SetBorrowerNumber(formattedNumber);

                // Update persistent watermark if new number is higher
                var sequenceSettingKey = $"BorrowerSequence_{prefix}";
                var sequenceSetting = await dbContext.ApplicationSettings
                    .FirstOrDefaultAsync(s => s.Key == sequenceSettingKey, cancellationToken)
                    .ConfigureAwait(false);

                if (sequenceSetting == null)
                {
                    dbContext.ApplicationSettings.Add(new ApplicationSetting(
                        sequenceSettingKey,
                        parsedSeq.ToString(CultureInfo.InvariantCulture)));
                }
                else if (long.TryParse(sequenceSetting.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var currentWatermark))
                {
                    if (parsedSeq > currentWatermark)
                    {
                        sequenceSetting.UpdateValue(parsedSeq.ToString(CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    sequenceSetting.UpdateValue(parsedSeq.ToString(CultureInfo.InvariantCulture));
                }
            }

            borrower.UpdateDetails(
                request.Name.Trim(),
                request.FatherName?.Trim(),
                request.Surname?.Trim(),
                request.Village?.Trim(),
                NormalizeDigits(request.Phone),
                request.Address?.Trim(),
                request.Notes?.Trim(),
                NormalizeDigits(request.AadharNumber));

            borrower.SetPhotosAndLoanType(request.BorrowerPhotoPath, request.OrnamentPhotoPath, request.LoanType, request.OrnamentType, request.OrnamentWeight, request.LoanAmount, request.LoanDate, request.InterestRate);
            if (request.LoanDate.HasValue && request.LoanDate.Value.Date < borrower.EntryDate.Date)
            {
                borrower.SetEntryDate(request.LoanDate.Value.Date);
            }

            if (request.LoanAmount.HasValue && request.LoanAmount.Value > 0m && request.LoanDate.HasValue)
            {
                var existingInitialTxn = await dbContext.Transactions
                    .FirstOrDefaultAsync(t => t.BorrowerId == request.Id && (t.Reference.StartsWith("INIT-") || t.Description == "Initial Loan Amount"), cancellationToken)
                    .ConfigureAwait(false);

                if (existingInitialTxn != null)
                {
                    if (existingInitialTxn.Amount.Amount != request.LoanAmount.Value ||
                        existingInitialTxn.TransactionDate != request.LoanDate.Value ||
                        existingInitialTxn.Reference != $"INIT-{borrower.BorrowerNumber}")
                    {
                        dbContext.Transactions.Remove(existingInitialTxn);
                        var periodId = await GetOrCreateCurrentPeriodAsync(dbContext, cancellationToken).ConfigureAwait(false);
                        var updatedInitialTxn = new Transaction(
                            request.Id,
                            periodId,
                            Money.Create(request.LoanAmount.Value),
                            TransactionType.Withdrawal,
                            request.LoanDate.Value,
                            "Initial Loan Amount",
                            $"INIT-{borrower.BorrowerNumber}");
                        dbContext.Transactions.Add(updatedInitialTxn);
                    }
                }
                else
                {
                    var periodId = await GetOrCreateCurrentPeriodAsync(dbContext, cancellationToken).ConfigureAwait(false);
                    var updatedInitialTxn = new Transaction(
                        request.Id,
                        periodId,
                        Money.Create(request.LoanAmount.Value),
                        TransactionType.Withdrawal,
                        request.LoanDate.Value,
                        "Initial Loan Amount",
                        $"INIT-{borrower.BorrowerNumber}");
                    dbContext.Transactions.Add(updatedInitialTxn);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            InvalidateBorrowerCaches(borrower.Id, oldBorrowerNumber);
            if (oldBorrowerNumber != borrower.BorrowerNumber)
            {
                InvalidateBorrowerCaches(borrower.Id, borrower.BorrowerNumber);
            }

            var depList = await dbContext.Transactions
                .Where(t => t.BorrowerId == request.Id && t.Type == TransactionType.Deposit)
                .Select(t => t.Amount.Amount)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var totalDeposits = depList.Sum();

            var withList = await dbContext.Transactions
                .Where(t => t.BorrowerId == request.Id && t.Type == TransactionType.Withdrawal)
                .Select(t => t.Amount.Amount)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var totalWithdrawals = withList.Sum();

            _logger.LogInformation("Borrower updated. ID='{BorrowerId}', Number='{BorrowerNumber}'.", borrower.Id, borrower.BorrowerNumber);

            return new BorrowerSummary(
                borrower.Id,
                borrower.BorrowerNumber,
                borrower.Name,
                borrower.Contact,
                borrower.Status.ToString(),
                borrower.EntryDate,
                totalDeposits,
                totalWithdrawals,
                totalDeposits - totalWithdrawals,
                null,
                borrower.FatherName,
                borrower.Surname,
                borrower.Village,
                borrower.AadharNumber,
                borrower.BorrowerPhotoPath,
                borrower.OrnamentPhotoPath,
                borrower.LoanType,
                borrower.OrnamentType,
                borrower.OrnamentWeight,
                borrower.LoanAmount,
                borrower.LoanDate,
                borrower.InterestRate,
                borrower.ClosedDate,
                borrower.ClosingAmount,
                borrower.ClosedAccruedInterest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotFoundException)
        {
            throw;
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true ||
                                            dbEx.Message.Contains("IX_Borrowers_BorrowerNumber", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(dbEx, "Database uniqueness violation updating borrower.");
            throw new ValidationException("Borrower number already exists.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to update borrower '{BorrowerId}'.", request.Id);
            throw new InvalidOperationException("Failed to update borrower.", exception);
        }
        finally
        {
            _numberLock.Release();
        }
    }

    public async Task<BorrowerSummary> ChangeStatusAsync(Guid id, BorrowerStatus status, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var borrower = await dbContext.Borrowers
                .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (borrower is null)
            {
                throw new NotFoundException($"Borrower with ID '{id}' not found.");
            }

            switch (status)
            {
                case BorrowerStatus.Active:
                    borrower.Activate();
                    break;
                case BorrowerStatus.Inactive:
                    borrower.Deactivate();
                    break;
                case BorrowerStatus.Archived:
                    borrower.Archive();
                    break;
                case BorrowerStatus.Closed:
                    borrower.CloseAccount(DateTime.Today);
                    break;
                default:
                    throw new ValidationException($"Invalid status '{status}'.");
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            InvalidateBorrowerCaches(borrower.Id, borrower.BorrowerNumber);
            _logger.LogInformation("Borrower status changed. ID='{BorrowerId}', Status='{Status}'.", borrower.Id, status);

            return new BorrowerSummary(
                borrower.Id,
                borrower.BorrowerNumber,
                borrower.Name,
                borrower.Contact,
                borrower.Status.ToString(),
                borrower.EntryDate,
                0m, 0m, 0m, null,
                borrower.FatherName,
                borrower.Surname,
                borrower.Village,
                borrower.AadharNumber,
                borrower.BorrowerPhotoPath,
                borrower.OrnamentPhotoPath,
                borrower.LoanType,
                borrower.OrnamentType,
                borrower.OrnamentWeight,
                borrower.LoanAmount,
                borrower.LoanDate,
                borrower.InterestRate,
                borrower.ClosedDate,
                borrower.ClosingAmount,
                borrower.ClosedAccruedInterest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotFoundException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to change borrower status '{BorrowerId}'.", id);
            throw new InvalidOperationException("Failed to change borrower status.", exception);
        }
    }

    public async Task CloseAccountAsync(Guid borrowerId, DateTime closedDate, decimal? closingAmount = null, decimal? closingInterest = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var borrower = await dbContext.Borrowers
                .FirstOrDefaultAsync(b => b.Id == borrowerId, cancellationToken)
                .ConfigureAwait(false);

            if (borrower is null)
            {
                throw new NotFoundException($"Borrower with ID '{borrowerId}' not found.");
            }

            if (!closingAmount.HasValue || !closingInterest.HasValue)
            {
                var interestService = scope.ServiceProvider.GetService<DhirDhar.Application.Interest.IInterestCalculationService>();
                if (interestService != null)
                {
                    var calculation = await interestService.CalculateAsync(borrowerId, closedDate, cancellationToken).ConfigureAwait(false);
                    closingAmount ??= calculation.TotalOutstanding;
                    closingInterest ??= calculation.TotalInterest;
                }
            }

            borrower.CloseAccount(closedDate, closingAmount, closingInterest);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            InvalidateBorrowerCaches(borrower.Id, borrower.BorrowerNumber);
            _logger.LogInformation("Account closed. ID='{BorrowerId}', ClosedDate='{ClosedDate}', ClosingAmount={ClosingAmount}, ClosingInterest={ClosingInterest}.", borrowerId, closedDate, closingAmount, closingInterest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotFoundException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to close account '{BorrowerId}'.", borrowerId);
            throw new InvalidOperationException("Failed to close account.", exception);
        }
    }

    private static async Task<string> GetNextBorrowerNumberInternalAsync(DhirDharDbContext dbContext, CancellationToken cancellationToken)
    {
        var prefix = await GetBorrowerPrefixInternalAsync(dbContext, cancellationToken).ConfigureAwait(false);

        var sequenceSettingKey = $"BorrowerSequence_{prefix}";
        var sequenceSetting = await dbContext.ApplicationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == sequenceSettingKey, cancellationToken)
            .ConfigureAwait(false);

        long storedSeq = 0;
        if (sequenceSetting != null && long.TryParse(sequenceSetting.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSeq))
        {
            storedSeq = parsedSeq;
        }

        // Query all existing borrower numbers from database for this prefix and calculate MAX sequence
        var existingNumbers = await dbContext.Borrowers
            .AsNoTracking()
            .Select(b => b.BorrowerNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        long maxDbSeq = 0;
        foreach (var num in existingNumbers)
        {
            if (BorrowerNumberHelper.TryParseSequence(num, prefix, out var seq))
            {
                if (seq > maxDbSeq) maxDbSeq = seq;
            }
        }

        long currentHighest = Math.Max(storedSeq, maxDbSeq);
        long nextSeq = currentHighest == 0 ? 1 : currentHighest + 1;
        var finalBorrowerNumber = BorrowerNumberHelper.FormatBorrowerNumber(prefix, nextSeq);

        while (await dbContext.Borrowers.AnyAsync(b => b.BorrowerNumber == finalBorrowerNumber, cancellationToken).ConfigureAwait(false))
        {
            nextSeq++;
            finalBorrowerNumber = BorrowerNumberHelper.FormatBorrowerNumber(prefix, nextSeq);
        }

        return finalBorrowerNumber;
    }

    private static IQueryable<Domain.Entities.Borrower> ApplyFilter(IQueryable<Domain.Entities.Borrower> query, BorrowerFilter filter)
    {
        return filter switch
        {
            BorrowerFilter.Active => query.Where(b => b.Status == BorrowerStatus.Active),
            BorrowerFilter.Inactive => query.Where(b => b.Status == BorrowerStatus.Inactive),
            BorrowerFilter.Closed => query.Where(b => b.Status == BorrowerStatus.Closed || b.Status == BorrowerStatus.Archived),
            _ => query
        };
    }

    private static IQueryable<Domain.Entities.Borrower> ApplySearch(IQueryable<Domain.Entities.Borrower> query, string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return query;
        }

        var rawTerm = searchTerm.Trim();
        if (rawTerm.StartsWith("DHIRDHAR|ACCOUNT|", StringComparison.OrdinalIgnoreCase))
        {
            rawTerm = rawTerm.Substring("DHIRDHAR|ACCOUNT|".Length).Trim();
        }
        else if (rawTerm.StartsWith("DHIRDHAR|", StringComparison.OrdinalIgnoreCase))
        {
            var parts = rawTerm.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 3) rawTerm = parts[2];
        }
        var cleanTerm = rawTerm.TrimStart('#').Trim();
        var term = cleanTerm.ToLowerInvariant();
        var englishTerm = ScriptTranslator.ToEnglish(cleanTerm).Trim().ToLowerInvariant();
        var gujaratiTerm = ScriptTranslator.ToGujarati(cleanTerm).Trim();
        var hindiTerm = ScriptTranslator.ToHindi(cleanTerm).Trim();
        var asciiDigits = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(cleanTerm);

        return query.Where(b =>
            EF.Functions.Like(b.Name.ToLower(), $"%{term}%") ||
            EF.Functions.Like(b.Name.ToLower(), $"%{englishTerm}%") ||
            EF.Functions.Like(b.Name, $"%{gujaratiTerm}%") ||
            EF.Functions.Like(b.Name, $"%{hindiTerm}%") ||
            (b.FatherName != null && (
                EF.Functions.Like(b.FatherName.ToLower(), $"%{term}%") ||
                EF.Functions.Like(b.FatherName.ToLower(), $"%{englishTerm}%") ||
                EF.Functions.Like(b.FatherName, $"%{gujaratiTerm}%") ||
                EF.Functions.Like(b.FatherName, $"%{hindiTerm}%"))) ||
            (b.Surname != null && (
                EF.Functions.Like(b.Surname.ToLower(), $"%{term}%") ||
                EF.Functions.Like(b.Surname.ToLower(), $"%{englishTerm}%") ||
                EF.Functions.Like(b.Surname, $"%{gujaratiTerm}%") ||
                EF.Functions.Like(b.Surname, $"%{hindiTerm}%"))) ||
            (b.Village != null && (
                EF.Functions.Like(b.Village.ToLower(), $"%{term}%") ||
                EF.Functions.Like(b.Village.ToLower(), $"%{englishTerm}%") ||
                EF.Functions.Like(b.Village, $"%{gujaratiTerm}%") ||
                EF.Functions.Like(b.Village, $"%{hindiTerm}%"))) ||
            EF.Functions.Like(b.BorrowerNumber.ToLower(), $"%{term}%") ||
            EF.Functions.Like(b.BorrowerNumber.ToLower(), $"%{englishTerm}%") ||
            EF.Functions.Like(b.BorrowerNumber.ToLower(), $"%{asciiDigits}%") ||
            (b.Phone != null && (
                EF.Functions.Like(b.Phone.ToLower(), $"%{term}%") ||
                EF.Functions.Like(b.Phone.ToLower(), $"%{asciiDigits}%"))) ||
            (b.AadharNumber != null && (
                EF.Functions.Like(b.AadharNumber.ToLower(), $"%{term}%") ||
                EF.Functions.Like(b.AadharNumber.ToLower(), $"%{asciiDigits}%"))) ||
            (b.Address != null && (
                EF.Functions.Like(b.Address.ToLower(), $"%{term}%") ||
                EF.Functions.Like(b.Address.ToLower(), $"%{englishTerm}%") ||
                EF.Functions.Like(b.Address, $"%{gujaratiTerm}%"))) ||
            (b.Notes != null && (
                EF.Functions.Like(b.Notes.ToLower(), $"%{term}%") ||
                EF.Functions.Like(b.Notes.ToLower(), $"%{englishTerm}%") ||
                EF.Functions.Like(b.Notes, $"%{gujaratiTerm}%"))));
    }

    private static void ValidateCreateRequest(CreateBorrowerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Full Name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Village))
        {
            throw new ValidationException("Village is required.");
        }

        if (!request.LoanAmount.HasValue || request.LoanAmount.Value <= 0m)
        {
            throw new ValidationException("Loan amount is required and must be greater than zero.");
        }

        if (!request.LoanDate.HasValue)
        {
            throw new ValidationException("Loan date is required.");
        }

        if (!request.InterestRate.HasValue || request.InterestRate.Value <= 0m)
        {
            throw new ValidationException("Interest rate is required and must be greater than zero.");
        }

        ValidateMobileNumber(request.Contact);
        ValidateAadharNumber(request.AadharNumber);
    }

    private static void ValidateUpdateRequest(UpdateBorrowerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Full Name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Village))
        {
            throw new ValidationException("Village is required.");
        }

        if (!request.LoanAmount.HasValue || request.LoanAmount.Value <= 0m)
        {
            throw new ValidationException("Loan amount is required and must be greater than zero.");
        }

        if (!request.LoanDate.HasValue)
        {
            throw new ValidationException("Loan date is required.");
        }

        if (!request.InterestRate.HasValue || request.InterestRate.Value <= 0m)
        {
            throw new ValidationException("Interest rate is required and must be greater than zero.");
        }

        ValidateMobileNumber(request.Phone);
        ValidateAadharNumber(request.AadharNumber);
    }

    private static void ValidateMobileNumber(string? mobileNumber)
    {
        if (string.IsNullOrWhiteSpace(mobileNumber))
        {
            return;
        }

        var digits = NormalizeDigits(mobileNumber);
        if (digits is null || digits.Length != 10 || digits[0] < '6' || digits[0] > '9')
        {
            throw new ValidationException("Invalid mobile number. It must be 10 digits starting with 6-9.");
        }
    }

    private static void ValidateAadharNumber(string? aadharNumber)
    {
        if (string.IsNullOrWhiteSpace(aadharNumber))
        {
            return;
        }

        var digits = NormalizeDigits(aadharNumber);
        if (digits is null || digits.Length != 12)
        {
            throw new ValidationException("Aadhar number must contain 12 digits.");
        }
    }

    private static string? NormalizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var asciiText = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(value);
        var digits = new string(asciiText.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? digits : null;
    }

    private static async Task<Guid> GetOrCreateCurrentPeriodAsync(DhirDharDbContext dbContext, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var currentPeriod = await dbContext.FinancialPeriods
            .FirstOrDefaultAsync(p => p.StartDate <= now && p.EndDate >= now && p.Status == FinancialPeriodStatus.Open, cancellationToken)
            .ConfigureAwait(false);

        if (currentPeriod is not null)
        {
            return currentPeriod.Id;
        }

        var period = new FinancialPeriod(
            $"{now:yyyy-MM}",
            new DateTime(now.Year, now.Month, 1),
            new DateTime(now.Year, now.Month, 1).AddMonths(1).AddDays(-1));

        dbContext.FinancialPeriods.Add(period);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return period.Id;
    }
}
