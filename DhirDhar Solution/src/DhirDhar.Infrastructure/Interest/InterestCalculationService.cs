using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Interest;
using DhirDhar.Application.Interest.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.Interest;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Interest;

public sealed class InterestCalculationService : IInterestCalculationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InterestCalculationService> _logger;

    public InterestCalculationService(
        IServiceScopeFactory scopeFactory,
        ILogger<InterestCalculationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<InterestCalculationResult> CalculateAsync(
        Guid borrowerId,
        DateTime requestedEndDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var borrower = await dbContext.Borrowers
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == borrowerId, cancellationToken)
                .ConfigureAwait(false);

            if (borrower is null)
            {
                throw new InvalidOperationException($"Borrower with ID '{borrowerId}' not found.");
            }

            var loan = await dbContext.Loans
                .AsNoTracking()
                .Where(l => l.BorrowerId == borrowerId)
                .OrderBy(l => l.IssueDate)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var transactions = await dbContext.Transactions
                .AsNoTracking()
                .Where(t => t.BorrowerId == borrowerId || t.FinancialPeriodId == borrowerId)
                .OrderBy(t => t.OccurredOn)
                .ThenBy(t => t.CreatedAt)
                .Select(t => new
                {
                    t.Id,
                    t.Type,
                    t.Amount.Amount,
                    t.OccurredOn,
                    t.Description,
                    t.Reference,
                    t.CreatedAt
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var firstLoanTxn = transactions
                .Where(t => t.Type == TransactionType.Withdrawal || string.Equals(t.Description, "Initial Loan Amount", StringComparison.OrdinalIgnoreCase) || (t.Reference != null && t.Reference.StartsWith("INIT-")))
                .OrderBy(t => t.OccurredOn)
                .ThenBy(t => t.CreatedAt)
                .FirstOrDefault();

            var hasInitialTxn = firstLoanTxn != null;
            var principal = hasInitialTxn ? 0m : (loan?.Principal.Amount ?? (borrower.LoanAmount ?? 0m));
            var startDate = firstLoanTxn != null ? firstLoanTxn.OccurredOn : (borrower.LoanDate ?? loan?.IssueDate ?? borrower.EntryDate);

            decimal monthlyRate = 0m;
            if (borrower.InterestRate.HasValue && borrower.InterestRate.Value > 0m)
            {
                monthlyRate = borrower.InterestRate.Value;
            }
            else if (loan != null && loan.InterestRatePercent > 0m)
            {
                monthlyRate = loan.InterestRatePercent;
            }
            else
            {
                var setting = await dbContext.ApplicationSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == "DefaultInterestRate", cancellationToken)
                    .ConfigureAwait(false);

                if (setting != null && decimal.TryParse(setting.Value, out var parsedRate) && parsedRate > 0m)
                {
                    monthlyRate = parsedRate;
                }
                else
                {
                    monthlyRate = 3.0m; // Default monthly interest rate (3% / month)
                }
            }

            var accountStatus = MapAccountStatus(borrower.Status);
            var closedDate = borrower.ClosedDate ?? (borrower.Status == BorrowerStatus.Closed ? borrower.UpdatedAt : (DateTime?)null);

            var events = new List<FinancialEvent>();
            var sequence = 0;
            foreach (var txn in transactions)
            {
                events.Add(new FinancialEvent(
                    txn.OccurredOn,
                    txn.Type.ToString(),
                    txn.Amount,
                    txn.Description,
                    sequence++));
            }

            var ratePeriods = new List<InterestRatePeriod>
            {
                new(monthlyRate, startDate, null)
            };

            var result = InterestCalculator.Calculate(
                borrowerId,
                principal,
                startDate,
                requestedEndDate,
                events,
                ratePeriods,
                accountStatus,
                closedDate);

            _logger.LogInformation(
                "--- DEBUG INTEREST CALCULATION FOR BORROWER '{BorrowerId}' --- (Rate={Rate}%)",
                borrowerId, monthlyRate);

            foreach (var seg in result.Segments)
            {
                _logger.LogInformation(
                    "BorrowerId={BorrowerId} | {PrevDate:dd/MM/yyyy} -> {CurrDate:dd/MM/yyyy} | PrincipalBeforeEvent={Principal} | InterestRate={Rate}% | Month={Month} | DaysCalculated={Days} | DaysInMonth={DaysInMonth} | InterestCalculated={Interest} | TransactionApplied={TxnType} {TxnAmount}",
                    borrowerId,
                    seg.SegmentStartDate,
                    seg.SegmentEndDate,
                    seg.OpeningPrincipal,
                    seg.ApplicableMonthlyRate,
                    seg.SegmentEndDate.ToString("MMMM yyyy"),
                    seg.ElapsedDays,
                    seg.DaysInMonth,
                    seg.CalculatedInterest,
                    seg.TransactionType ?? "None",
                    seg.TransactionAmount?.ToString("N2") ?? "0.00");
            }

            _logger.LogInformation(
                "Interest calculated for borrower '{BorrowerId}'. TotalInterest={TotalInterest}, ClosingPrincipal={ClosingPrincipal}.",
                borrowerId, result.TotalInterest, result.ClosingPrincipal);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to calculate interest for borrower '{BorrowerId}'.", borrowerId);
            throw;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> CalculateBatchAsync(
        IReadOnlyList<Guid> borrowerIds,
        DateTime requestedEndDate,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<Guid, decimal>();
        if (borrowerIds == null || borrowerIds.Count == 0)
        {
            return results;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            decimal defaultSettingRate = 3.0m;
            var setting = await dbContext.ApplicationSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "DefaultInterestRate", cancellationToken)
                .ConfigureAwait(false);

            if (setting != null && decimal.TryParse(setting.Value, out var parsedRate) && parsedRate > 0m)
            {
                defaultSettingRate = parsedRate;
            }

            const int chunkSize = 500;
            for (int i = 0; i < borrowerIds.Count; i += chunkSize)
            {
                var chunk = borrowerIds.Skip(i).Take(chunkSize).ToList();

                var borrowers = await dbContext.Borrowers
                    .AsNoTracking()
                    .Where(b => chunk.Contains(b.Id))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var loans = await dbContext.Loans
                    .AsNoTracking()
                    .Where(l => chunk.Contains(l.BorrowerId))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var loansDict = loans.GroupBy(l => l.BorrowerId).ToDictionary(g => g.Key, g => g.OrderBy(l => l.IssueDate).FirstOrDefault());

                var txns = await dbContext.Transactions
                    .AsNoTracking()
                    .Where(t => t.BorrowerId.HasValue && chunk.Contains(t.BorrowerId.Value))
                    .OrderBy(t => t.OccurredOn)
                    .ThenBy(t => t.CreatedAt)
                    .Select(t => new
                    {
                        BorrowerId = t.BorrowerId!.Value,
                        t.Type,
                        Amount = t.Amount.Amount,
                        t.OccurredOn,
                        t.Description,
                        t.Reference,
                        t.CreatedAt
                    })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var txnsByBorrower = txns.GroupBy(t => t.BorrowerId).ToDictionary(g => g.Key, g => g.ToList());

                foreach (var borrower in borrowers)
                {
                    loansDict.TryGetValue(borrower.Id, out var loan);
                    txnsByBorrower.TryGetValue(borrower.Id, out var borrowerTxns);
                    borrowerTxns ??= new();

                    var firstLoanTxn = borrowerTxns
                        .Where(t => t.Type == TransactionType.Withdrawal || string.Equals(t.Description, "Initial Loan Amount", StringComparison.OrdinalIgnoreCase) || (t.Reference != null && t.Reference.StartsWith("INIT-")))
                        .OrderBy(t => t.OccurredOn)
                        .ThenBy(t => t.CreatedAt)
                        .FirstOrDefault();

                    var hasInitialTxn = firstLoanTxn != null;
                    var principal = hasInitialTxn ? 0m : (loan?.Principal.Amount ?? (borrower.LoanAmount ?? 0m));
                    var startDate = firstLoanTxn != null ? firstLoanTxn.OccurredOn : (borrower.LoanDate ?? loan?.IssueDate ?? borrower.EntryDate);

                    decimal monthlyRate = borrower.InterestRate.HasValue && borrower.InterestRate.Value > 0m
                        ? borrower.InterestRate.Value
                        : (loan != null && loan.InterestRatePercent > 0m ? loan.InterestRatePercent : defaultSettingRate);

                    var accountStatus = MapAccountStatus(borrower.Status);
                    var closedDate = borrower.ClosedDate ?? (borrower.Status == BorrowerStatus.Closed ? borrower.UpdatedAt : (DateTime?)null);

                    var events = new List<FinancialEvent>(borrowerTxns.Count);
                    int seq = 0;
                    foreach (var txn in borrowerTxns)
                    {
                        events.Add(new FinancialEvent(txn.OccurredOn, txn.Type.ToString(), txn.Amount, txn.Description, seq++));
                    }

                    var ratePeriods = new List<InterestRatePeriod>
                    {
                        new(monthlyRate, startDate, null)
                    };

                    var calc = InterestCalculator.Calculate(
                        borrower.Id,
                        principal,
                        startDate,
                        requestedEndDate,
                        events,
                        ratePeriods,
                        accountStatus,
                        closedDate);

                    results[borrower.Id] = calc.TotalInterest;
                }
            }

            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate batch interest for {Count} borrowers.", borrowerIds.Count);
            return results;
        }
    }

    private static AccountStatus MapAccountStatus(BorrowerStatus status)
    {
        return status switch
        {
            BorrowerStatus.Active => AccountStatus.Active,
            BorrowerStatus.Inactive => AccountStatus.Inactive,
            BorrowerStatus.Archived => AccountStatus.Archived,
            BorrowerStatus.Closed => AccountStatus.Closed,
            _ => AccountStatus.Active
        };
    }
}
