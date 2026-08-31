using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Audit;
using DhirDhar.Application.Common.Exceptions;
using DhirDhar.Application.Transactions;
using DhirDhar.Application.Transactions.Models;
using DhirDhar.Application.Validation;
using DhirDhar.Domain.Common;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using DhirDhar.Application.Caching;
using DhirDhar.Application.Localization;

namespace DhirDhar.Infrastructure.Transactions;

public sealed class TransactionService : ITransactionService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFinancialValidationService _validationService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IAuditService _auditService;
    private readonly ILogger<TransactionService> _logger;
    private readonly ICacheService? _cacheService;
    private readonly ITransactionEventService? _transactionEventService;

    public TransactionService(
        IServiceScopeFactory scopeFactory,
        IFinancialValidationService validationService,
        IIdempotencyService idempotencyService,
        IAuditService auditService,
        ILogger<TransactionService> logger,
        ICacheService? cacheService = null,
        ITransactionEventService? transactionEventService = null)
    {
        _scopeFactory = scopeFactory;
        _validationService = validationService;
        _idempotencyService = idempotencyService;
        _auditService = auditService;
        _logger = logger;
        _cacheService = cacheService ?? scopeFactory.CreateScope().ServiceProvider.GetService<ICacheService>();
        _transactionEventService = transactionEventService ?? scopeFactory.CreateScope().ServiceProvider.GetService<ITransactionEventService>();
    }

    private void InvalidateCaches(Guid? borrowerId = null)
    {
        if (borrowerId.HasValue)
        {
            _cacheService?.Remove($"borrower_id_{borrowerId.Value}");
        }
        _cacheService?.RemoveByPrefix("borrowers_page_");
        _cacheService?.Remove("dashboard_summary");
        _cacheService?.RemoveByPrefix("search_query_");
    }

    public async Task<TransactionSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var transaction = await dbContext.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (transaction is null)
            {
                return null;
            }

            var bId = transaction.BorrowerId ?? transaction.FinancialPeriodId;
            var borrowerDetails = await GetBorrowerDetailsAsync(dbContext, bId, cancellationToken).ConfigureAwait(false);

            return new TransactionSummary(
                transaction.Id,
                borrowerDetails.Name,
                transaction.Type.ToString(),
                transaction.Amount.Amount,
                transaction.OccurredOn,
                transaction.Description,
                transaction.CreatedAt,
                borrowerDetails.Number,
                borrowerDetails.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to get transaction by ID '{TransactionId}'.", id);
            throw new InvalidOperationException("Failed to retrieve transaction.", exception);
        }
    }

    public async Task<TransactionListResult> GetListAsync(TransactionFilterRequest filter, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var query = dbContext.Transactions.AsNoTracking().AsQueryable();

            query = ApplyBorrowerFilter(query, filter.BorrowerId);
            query = ApplyTypeFilter(query, filter.TypeFilter);
            query = ApplyDateRangeFilter(query, filter.StartDate, filter.EndDate);
            query = ApplySearchFilter(query, filter.SearchTerm);

            var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

            var pageSize = Math.Clamp(filter.PageSize <= 0 ? DefaultPageSize : filter.PageSize, 1, MaxPageSize);
            var page = Math.Max(1, filter.Page);

            var transactions = await query
                .OrderByDescending(t => t.OccurredOn)
                .ThenByDescending(t => t.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var borrowerIds = transactions
                .Select(t => t.BorrowerId ?? t.FinancialPeriodId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            var borrowerList = await dbContext.Borrowers
                .AsNoTracking()
                .Where(b => borrowerIds.Contains(b.Id))
                .Select(b => new { b.Id, b.Name, b.BorrowerNumber })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var borrowerMap = borrowerList.ToDictionary(b => b.Id, b => (b.Name, b.BorrowerNumber));

            var summaries = new List<TransactionSummary>();
            foreach (var transaction in transactions)
            {
                var bId = transaction.BorrowerId ?? transaction.FinancialPeriodId;
                string borrowerName = "Unknown";
                string? borrowerNumber = null;

                if (bId != Guid.Empty && borrowerMap.TryGetValue(bId, out var info))
                {
                    borrowerName = info.Name;
                    borrowerNumber = info.BorrowerNumber;
                }
                else if (bId != Guid.Empty)
                {
                    var details = await GetBorrowerDetailsAsync(dbContext, bId, cancellationToken).ConfigureAwait(false);
                    borrowerName = details.Name;
                    borrowerNumber = details.Number;
                }

                summaries.Add(new TransactionSummary(
                    transaction.Id,
                    borrowerName,
                    transaction.Type.ToString(),
                    transaction.Amount.Amount,
                    transaction.OccurredOn,
                    transaction.Description,
                    transaction.CreatedAt,
                    borrowerNumber,
                    bId));
            }

            return new TransactionListResult(summaries, totalCount, page, pageSize);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to get transaction list.");
            throw new InvalidOperationException("Failed to retrieve transactions.", exception);
        }
    }

    public async Task<TransactionFinancials> GetFinancialsAsync(Guid? borrowerId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var query = dbContext.Transactions.AsNoTracking().AsQueryable();

            if (borrowerId.HasValue && borrowerId.Value != Guid.Empty)
            {
                query = query.Where(t => t.BorrowerId == borrowerId.Value || t.FinancialPeriodId == borrowerId.Value);
            }

            var depList = await query
                .Where(t => t.Type == TransactionType.Deposit)
                .Select(t => t.Amount.Amount)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var deposits = depList.Sum();

            var withList = await query
                .Where(t => t.Type == TransactionType.Withdrawal)
                .Select(t => t.Amount.Amount)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var withdrawals = withList.Sum();

            var count = await query.CountAsync(cancellationToken).ConfigureAwait(false);

            return new TransactionFinancials(deposits, withdrawals, deposits - withdrawals, count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to get transaction financials.");
            throw new InvalidOperationException("Failed to calculate financial totals.", exception);
        }
    }

    public async Task<TransactionSummary> CreateAsync(CreateTransactionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.BorrowerId == Guid.Empty)
        {
            throw new ValidationException("Borrower is required.");
        }

        // Perform financial validation
        var validation = request.Type switch
        {
            TransactionType.Deposit => await _validationService.ValidateDepositAsync(request.BorrowerId, request.Amount, request.TransactionDate, null, cancellationToken).ConfigureAwait(false),
            TransactionType.Withdrawal => await _validationService.ValidateWithdrawalAsync(request.BorrowerId, request.Amount, request.TransactionDate, null, cancellationToken).ConfigureAwait(false),
            _ => _validationService.ValidateAmount(request.Amount, requirePositive: true)
        };

        if (!validation.IsValid)
        {
            throw new ValidationException(string.Join("; ", validation.Errors));
        }

        // Check idempotency lock
        var idempotencyKey = $"tx:{request.BorrowerId}:{request.Type}:{request.Amount}:{request.TransactionDate:yyyyMMddHHmmss}";
        if (!_idempotencyService.TryAcquireLock(idempotencyKey))
        {
            throw new ValidationException("Duplicate transaction submission in progress.");
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var borrower = await dbContext.Borrowers
                .FirstOrDefaultAsync(b => b.Id == request.BorrowerId, cancellationToken)
                .ConfigureAwait(false);

            if (borrower is null)
            {
                throw new NotFoundException($"Borrower with ID '{request.BorrowerId}' not found.");
            }

            var periodId = await GetOrCreateCurrentPeriodAsync(dbContext, cancellationToken).ConfigureAwait(false);

            await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var transaction = new Transaction(
                    periodId,
                    Money.Create(request.Amount),
                    request.Type,
                    request.TransactionDate,
                    string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim());

                transaction.SetBorrowerId(borrower.Id);

                dbContext.Transactions.Add(transaction);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await dbTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                InvalidateCaches(request.BorrowerId);
                _idempotencyService.RegisterCompleted(idempotencyKey);
                _transactionEventService?.PublishTransactionChanged(new TransactionChangedEventArgs(transaction.Id, borrower.Id, TransactionMutationKind.Created));

                await _auditService.RecordAsync(new AuditEvent(
                    "TransactionCreated",
                    nameof(Transaction),
                    transaction.Id.ToString(),
                    $"Created {transaction.Type} transaction of {transaction.Amount.Amount:C2} for borrower '{borrower.Name}'.",
                    "SUCCESS"), cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Transaction created. ID='{TransactionId}', Type='{Type}', Amount={Amount}, BorrowerId='{BorrowerId}'.",
                    transaction.Id, request.Type, request.Amount, request.BorrowerId);

                return new TransactionSummary(
                    transaction.Id,
                    borrower.Name,
                    transaction.Type.ToString(),
                    transaction.Amount.Amount,
                    transaction.OccurredOn,
                    transaction.Description,
                    transaction.CreatedAt,
                    borrower.BorrowerNumber,
                    borrower.Id);
            }
            catch
            {
                await dbTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (NotFoundException)
        {
            throw;
        }
        catch (DomainValidationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to create transaction.");
            throw new InvalidOperationException("Failed to create transaction.", exception);
        }
        finally
        {
            _idempotencyService.ReleaseLock(idempotencyKey);
        }
    }

    public async Task<TransactionSummary?> GetLatestTransactionAsync(Guid borrowerId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var latestTransaction = await dbContext.Transactions
                .AsNoTracking()
                .Where(t => t.BorrowerId == borrowerId || t.FinancialPeriodId == borrowerId)
                .OrderByDescending(t => t.OccurredOn)
                .ThenByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (latestTransaction is null)
            {
                return null;
            }

            var bId = latestTransaction.BorrowerId ?? latestTransaction.FinancialPeriodId;
            var details = await GetBorrowerDetailsAsync(dbContext, bId, cancellationToken).ConfigureAwait(false);

            return new TransactionSummary(
                latestTransaction.Id,
                details.Name,
                latestTransaction.Type.ToString(),
                latestTransaction.Amount.Amount,
                latestTransaction.OccurredOn,
                latestTransaction.Description,
                latestTransaction.CreatedAt,
                details.Number,
                details.Id);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to get latest transaction for borrower '{BorrowerId}'.", borrowerId);
            return null;
        }
    }

    private static IQueryable<Transaction> ApplyBorrowerFilter(IQueryable<Transaction> query, Guid? borrowerId)
    {
        if (!borrowerId.HasValue || borrowerId.Value == Guid.Empty)
        {
            return query;
        }

        return query.Where(t => t.BorrowerId == borrowerId.Value || t.FinancialPeriodId == borrowerId.Value);
    }

    private static IQueryable<Transaction> ApplyTypeFilter(IQueryable<Transaction> query, TransactionTypeFilter typeFilter)
    {
        return typeFilter switch
        {
            TransactionTypeFilter.Deposit => query.Where(t => t.Type == Domain.Enums.TransactionType.Deposit),
            TransactionTypeFilter.Withdrawal => query.Where(t => t.Type == Domain.Enums.TransactionType.Withdrawal),
            _ => query
        };
    }

    private static IQueryable<Transaction> ApplyDateRangeFilter(IQueryable<Transaction> query, DateTime? startDate, DateTime? endDate)
    {
        if (startDate.HasValue)
        {
            query = query.Where(t => t.OccurredOn >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(t => t.OccurredOn <= endDate.Value);
        }

        return query;
    }

    private static IQueryable<Transaction> ApplySearchFilter(IQueryable<Transaction> query, string? searchTerm)
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

        return query.Where(t =>
            (t.Description != null && (
                EF.Functions.Like(t.Description.ToLower(), $"%{term}%") ||
                EF.Functions.Like(t.Description.ToLower(), $"%{englishTerm}%") ||
                EF.Functions.Like(t.Description, $"%{gujaratiTerm}%") ||
                EF.Functions.Like(t.Description, $"%{hindiTerm}%"))) ||
            (t.Reference != null && (
                EF.Functions.Like(t.Reference.ToLower(), $"%{term}%") ||
                EF.Functions.Like(t.Reference.ToLower(), $"%{englishTerm}%") ||
                EF.Functions.Like(t.Reference.ToLower(), $"%{asciiDigits}%"))) ||
            (t.Borrower != null && (
                EF.Functions.Like(t.Borrower.Name.ToLower(), $"%{term}%") ||
                EF.Functions.Like(t.Borrower.Name.ToLower(), $"%{englishTerm}%") ||
                EF.Functions.Like(t.Borrower.Name, $"%{gujaratiTerm}%") ||
                EF.Functions.Like(t.Borrower.Name, $"%{hindiTerm}%") ||
                EF.Functions.Like(t.Borrower.BorrowerNumber.ToLower(), $"%{term}%") ||
                EF.Functions.Like(t.Borrower.BorrowerNumber.ToLower(), $"%{englishTerm}%") ||
                EF.Functions.Like(t.Borrower.BorrowerNumber.ToLower(), $"%{asciiDigits}%") ||
                (t.Borrower.Phone != null && (
                    EF.Functions.Like(t.Borrower.Phone.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(t.Borrower.Phone.ToLower(), $"%{asciiDigits}%"))) ||
                (t.Borrower.Village != null && (
                    EF.Functions.Like(t.Borrower.Village.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(t.Borrower.Village.ToLower(), $"%{englishTerm}%") ||
                    EF.Functions.Like(t.Borrower.Village, $"%{gujaratiTerm}%") ||
                    EF.Functions.Like(t.Borrower.Village, $"%{hindiTerm}%"))) ||
                (t.Borrower.AadharNumber != null && (
                    EF.Functions.Like(t.Borrower.AadharNumber.ToLower(), $"%{term}%") ||
                    EF.Functions.Like(t.Borrower.AadharNumber.ToLower(), $"%{asciiDigits}%"))))));
    }

    private static async Task<(string Name, string? Number, Guid? Id)> GetBorrowerDetailsAsync(DhirDharDbContext dbContext, Guid? borrowerOrPeriodId, CancellationToken cancellationToken)
    {
        if (!borrowerOrPeriodId.HasValue || borrowerOrPeriodId.Value == Guid.Empty)
        {
            return ("Unknown", null, null);
        }

        var borrower = await dbContext.Borrowers
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == borrowerOrPeriodId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (borrower is not null)
        {
            return (borrower.Name, borrower.BorrowerNumber, borrower.Id);
        }

        var loan = await dbContext.Loans
            .AsNoTracking()
            .Include(l => l.Borrower)
            .FirstOrDefaultAsync(l => l.Id == borrowerOrPeriodId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (loan?.Borrower != null)
        {
            return (loan.Borrower.Name, loan.Borrower.BorrowerNumber, loan.Borrower.Id);
        }

        return ("Unknown", null, borrowerOrPeriodId);
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
