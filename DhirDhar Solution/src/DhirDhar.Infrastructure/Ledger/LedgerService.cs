using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Interest;
using DhirDhar.Application.Ledger;
using DhirDhar.Application.Ledger.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Transactions;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.Interest;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Ledger;

public sealed class LedgerService : ILedgerService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LedgerService> _logger;

    public LedgerService(
        IServiceScopeFactory scopeFactory,
        ILogger<LedgerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<LedgerSummary> GetSummaryAsync(Guid borrowerId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            Borrower? borrower = null;
            if (borrowerId != Guid.Empty)
            {
                borrower = await dbContext.Borrowers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == borrowerId, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (borrower is null)
            {
                borrower = await dbContext.Borrowers
                    .AsNoTracking()
                    .OrderByDescending(b => b.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (borrower is null)
            {
                return new LedgerSummary(
                    Guid.Empty,
                    "No Borrower",
                    0m,
                    0m,
                    0m,
                    0m,
                    0m,
                    "Active",
                    null);
            }

            borrowerId = borrower.Id;

            var loan = await dbContext.Loans
                .AsNoTracking()
                .Where(l => l.BorrowerId == borrowerId)
                .OrderBy(l => l.IssueDate)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var hasInitialTxn = await dbContext.Transactions
                .AnyAsync(t => t.BorrowerId == borrowerId && (t.Reference.StartsWith("INIT-") || t.Description == "Initial Loan Amount"), cancellationToken)
                .ConfigureAwait(false);

            var openingBalance = loan?.Principal.Amount ?? (hasInitialTxn ? 0m : (borrower.LoanAmount ?? 0m));
            var interestResult = await scope.ServiceProvider
                .GetRequiredService<IInterestCalculationService>()
                .CalculateAsync(borrowerId, DateTime.Today, cancellationToken)
                .ConfigureAwait(false);

            var depositsList = await dbContext.Transactions
                .Where(t => t.BorrowerId == borrowerId && t.Type == TransactionType.Deposit)
                .Select(t => t.Amount.Amount)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var totalDeposits = depositsList.Sum();

            var withdrawalsList = await dbContext.Transactions
                .Where(t => t.BorrowerId == borrowerId && t.Type == TransactionType.Withdrawal)
                .Select(t => t.Amount.Amount)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var totalWithdrawals = withdrawalsList.Sum();

            var isClosed = borrower.Status == BorrowerStatus.Closed || borrower.Status == BorrowerStatus.Archived;
            var closedDate = borrower.ClosedDate ?? (isClosed ? borrower.UpdatedAt : (DateTime?)null);

            return new LedgerSummary(
                borrowerId,
                borrower.Name,
                openingBalance,
                totalDeposits,
                totalWithdrawals,
                interestResult.TotalInterest,
                interestResult.TotalOutstanding,
                borrower.Status.ToString(),
                closedDate);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to get ledger summary for borrower ID='{BorrowerId}'.", borrowerId);
            throw new InvalidOperationException($"Failed to get ledger summary for borrower ID='{borrowerId}'.", exception);
        }
    }

    public async Task<IReadOnlyList<LedgerEntryDto>> GetEntriesAsync(
        Guid borrowerId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? eventTypeFilter = null,
        string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            Borrower? borrower = null;
            if (borrowerId != Guid.Empty)
            {
                borrower = await dbContext.Borrowers
                    .FirstOrDefaultAsync(b => b.Id == borrowerId, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (borrower is null)
            {
                borrower = await dbContext.Borrowers
                    .OrderByDescending(b => b.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (borrower is null)
            {
                return Array.Empty<LedgerEntryDto>();
            }

            borrowerId = borrower.Id;

            var effectiveEndDate = endDate ?? DateTime.Today;
            var cDate = borrower.ClosedDate ?? (borrower.Status == BorrowerStatus.Closed ? borrower.UpdatedAt : (DateTime?)null);
            if (cDate.HasValue && cDate.Value < effectiveEndDate)
            {
                effectiveEndDate = cDate.Value;
            }

            var calculationResult = await scope.ServiceProvider
                .GetRequiredService<IInterestCalculationService>()
                .CalculateAsync(borrowerId, effectiveEndDate, cancellationToken)
                .ConfigureAwait(false);

            var entries = BuildLedgerEntries(calculationResult);

            if (startDate.HasValue)
            {
                entries = entries.Where(e => e.Date >= startDate.Value).ToList();
            }

            if (endDate.HasValue)
            {
                entries = entries.Where(e => e.Date <= endDate.Value).ToList();
            }

            if (!string.IsNullOrWhiteSpace(eventTypeFilter) && eventTypeFilter != "All")
            {
                entries = entries.Where(e => e.EventType == eventTypeFilter).ToList();
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var rawTerm = searchTerm.Trim();
                var term = rawTerm.ToLowerInvariant();
                var englishTerm = ScriptTranslator.ToEnglish(rawTerm).Trim();
                var gujaratiTerm = ScriptTranslator.ToGujarati(rawTerm).Trim();
                var hindiTerm = ScriptTranslator.ToHindi(rawTerm).Trim();
                var asciiDigits = DhirDhar.Infrastructure.Localization.LocalizationService.NormalizeDigitsToAscii(rawTerm);

                entries = entries.Where(e =>
                    (!string.IsNullOrEmpty(e.Description) && (
                        e.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(englishTerm) && e.Description.Contains(englishTerm, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(gujaratiTerm) && e.Description.Contains(gujaratiTerm, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(hindiTerm) && e.Description.Contains(hindiTerm, StringComparison.OrdinalIgnoreCase)))) ||
                    (!string.IsNullOrEmpty(e.Reference) && (
                        e.Reference.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(englishTerm) && e.Reference.Contains(englishTerm, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(asciiDigits) && e.Reference.Contains(asciiDigits, StringComparison.OrdinalIgnoreCase)))) ||
                    (!string.IsNullOrEmpty(e.EventType) && (
                        e.EventType.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(englishTerm) && e.EventType.Contains(englishTerm, StringComparison.OrdinalIgnoreCase))))).ToList();
            }

            return entries;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to get ledger entries for borrower '{BorrowerId}'.", borrowerId);
            return Array.Empty<LedgerEntryDto>();
        }
    }

    private static List<LedgerEntryDto> BuildLedgerEntries(InterestCalculationResult result)
    {
        var entries = new List<LedgerEntryDto>();

        foreach (var segment in result.Segments)
        {
            entries.Add(new LedgerEntryDto(
                segment.SegmentEndDate,
                "Interest",
                $"Interest for {segment.SegmentStartDate:dd-MMM} to {segment.SegmentEndDate:dd-MMM}",
                null,
                segment.CalculatedInterest,
                segment.ApplicableMonthlyRate,
                segment.OpeningPrincipal,
                segment.ClosingPrincipal,
                $"{segment.ElapsedDays}d/{segment.DaysInMonth}d",
                "Accrued"));

            if (!string.IsNullOrEmpty(segment.TransactionType) && segment.TransactionAmount.HasValue)
            {
                entries.Add(new LedgerEntryDto(
                    segment.SegmentEndDate,
                    segment.TransactionType,
                    $"{segment.TransactionType} transaction",
                    segment.TransactionAmount,
                    null,
                    segment.ApplicableMonthlyRate,
                    segment.OpeningPrincipal + segment.CalculatedInterest,
                    segment.ClosingPrincipal,
                    segment.TransactionType == "Deposit" ? "CR" : "DR",
                    "Applied"));
            }
        }

        return entries.OrderBy(e => e.Date).ThenBy(e => e.EventType == "Interest" ? 0 : 1).ToList();
    }
}
