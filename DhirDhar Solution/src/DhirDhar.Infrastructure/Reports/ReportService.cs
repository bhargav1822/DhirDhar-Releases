using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Application.Interest;
using DhirDhar.Application.Ledger;
using DhirDhar.Application.Ledger.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Reports;
using DhirDhar.Application.Reports.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Reports;

public sealed class ReportService : IReportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        IServiceScopeFactory scopeFactory,
        ILogger<ReportService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<BorrowerStatementReport> GenerateBorrowerStatementAsync(
        Guid borrowerId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        if (fromDate > toDate)
        {
            throw new ArgumentException("From date cannot be after To date.");
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var localization = scope.ServiceProvider.GetService<ILocalizationService>();

            var borrower = await dbContext.Borrowers
                .AsNoTracking()
                .Include(b => b.Loans)
                .FirstOrDefaultAsync(b => b.Id == borrowerId, cancellationToken)
                .ConfigureAwait(false);

            if (borrower is null)
            {
                throw new InvalidOperationException($"Borrower with ID '{borrowerId}' not found.");
            }

            var loan = borrower.Loans.OrderBy(l => l.IssueDate).FirstOrDefault();
            var interestRate = (borrower.InterestRate.HasValue && borrower.InterestRate.Value > 0m)
                ? borrower.InterestRate.Value
                : (loan != null && loan.InterestRatePercent > 0m ? loan.InterestRatePercent : 3.0m);

            var entries = await scope.ServiceProvider
                .GetRequiredService<ILedgerService>()
                .GetEntriesAsync(borrowerId, fromDate, toDate, "All", null, cancellationToken)
                .ConfigureAwait(false);

            var interestResult = await scope.ServiceProvider
                .GetRequiredService<IInterestCalculationService>()
                .CalculateAsync(borrowerId, toDate, cancellationToken)
                .ConfigureAwait(false);

            var initialLoanAmount = loan?.Principal.Amount ?? (borrower.LoanAmount ?? 0m);
            var initialLoanDate = borrower.LoanDate ?? loan?.IssueDate ?? borrower.EntryDate;

            decimal openingPrincipal;
            if (fromDate <= initialLoanDate)
            {
                openingPrincipal = initialLoanAmount;
            }
            else if (entries.Count > 0 && entries[0].OpeningPrincipal > 0)
            {
                openingPrincipal = entries[0].OpeningPrincipal;
            }
            else
            {
                try
                {
                    var priorResult = await scope.ServiceProvider
                        .GetRequiredService<IInterestCalculationService>()
                        .CalculateAsync(borrowerId, fromDate.AddDays(-1), cancellationToken)
                        .ConfigureAwait(false);
                    openingPrincipal = priorResult.TotalOutstanding;
                }
                catch
                {
                    openingPrincipal = initialLoanAmount;
                }
            }

            var totalDeposits = entries.Where(e => e.EventType.Equals("Deposit", StringComparison.OrdinalIgnoreCase)).Sum(e => e.TransactionAmount ?? 0m);
            var totalWithdrawals = entries.Where(e => e.EventType.Equals("Withdrawal", StringComparison.OrdinalIgnoreCase)).Sum(e => e.TransactionAmount ?? 0m);
            
            var totalInterest = entries.Where(e => e.EventType.Equals("Interest", StringComparison.OrdinalIgnoreCase)).Sum(e => e.InterestAmount ?? 0m);
            if (totalInterest == 0m && entries.Count == 0 && interestResult.TotalInterest > 0m)
            {
                totalInterest = 0m;
            }

            var finalOutstanding = interestResult.TotalOutstanding;

            var currentLang = localization?.CurrentLanguage ?? "gu-IN";
            var translationService = scope.ServiceProvider.GetService<ITranslationService>();
            var displayName = translationService != null ? translationService.Translate(borrower.Name, currentLang) : borrower.Name;

            var borrowerNumber = borrower.BorrowerNumber ?? borrower.Id.ToString("N").Substring(0, 8).ToUpperInvariant();
            var contactNumber = borrower.Contact ?? borrower.Phone ?? string.Empty;
            var closedDate = borrower.ClosedDate ?? (borrower.Status == BorrowerStatus.Closed || borrower.Status == BorrowerStatus.Archived ? borrower.UpdatedAt : (DateTime?)null);

            return new BorrowerStatementReport(
                borrowerNumber,
                displayName,
                contactNumber,
                borrower.Status.ToString(),
                borrower.EntryDate != default ? borrower.EntryDate : borrower.CreatedAt,
                closedDate,
                interestRate,
                fromDate,
                toDate,
                openingPrincipal,
                totalDeposits,
                totalWithdrawals,
                totalInterest,
                finalOutstanding,
                entries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to generate borrower statement for '{BorrowerId}'.", borrowerId);
            throw;
        }
    }

    public async Task<TransactionReport> GenerateTransactionReportAsync(
        DateTime fromDate,
        DateTime toDate,
        Guid? borrowerId,
        string transactionTypeFilter,
        CancellationToken cancellationToken = default)
    {
        if (fromDate > toDate)
        {
            throw new ArgumentException("From date cannot be after To date.");
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var localization = scope.ServiceProvider.GetService<ILocalizationService>();

            var query = dbContext.Transactions
                .AsNoTracking()
                .Include(t => t.Borrower)
                .Where(t => t.OccurredOn >= fromDate && t.OccurredOn <= toDate);

            if (borrowerId.HasValue && borrowerId.Value != Guid.Empty)
            {
                query = query.Where(t => t.BorrowerId == borrowerId.Value || t.FinancialPeriodId == borrowerId.Value);
            }

            if (string.Equals(transactionTypeFilter, "Deposit", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.Type == TransactionType.Deposit);
            }
            else if (string.Equals(transactionTypeFilter, "Withdrawal", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.Type == TransactionType.Withdrawal);
            }

            var currentLang = localization?.CurrentLanguage ?? "gu-IN";
            var translationService = scope.ServiceProvider.GetService<ITranslationService>();

            var transactions = await query
                .OrderBy(t => t.OccurredOn)
                .ThenBy(t => t.CreatedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var items = new List<TransactionReportItem>();
            decimal runningBalance = 0m;

            foreach (var txn in transactions)
            {
                var amt = txn.Amount.Amount;
                if (txn.Type == TransactionType.Withdrawal)
                {
                    runningBalance += amt;
                }
                else
                {
                    runningBalance -= amt;
                }

                var rawName = txn.Borrower?.Name ?? "Unknown";
                var bName = translationService != null ? translationService.Translate(rawName, currentLang) : rawName;
                var bNum = txn.Borrower?.BorrowerNumber ?? (txn.BorrowerId.HasValue && txn.BorrowerId.Value != Guid.Empty ? txn.BorrowerId.Value.ToString("N").Substring(0, 8).ToUpperInvariant() : "-");

                items.Add(new TransactionReportItem(
                    txn.OccurredOn,
                    bNum,
                    bName,
                    txn.Type.ToString(),
                    amt,
                    runningBalance,
                    txn.Description ?? string.Empty));
            }

            var totalDeposits = transactions.Where(t => t.Type == TransactionType.Deposit).Sum(t => t.Amount.Amount);
            var totalWithdrawals = transactions.Where(t => t.Type == TransactionType.Withdrawal).Sum(t => t.Amount.Amount);

            string borrowerDisplayName = "All Borrowers";
            if (borrowerId.HasValue && borrowerId.Value != Guid.Empty)
            {
                var b = await dbContext.Borrowers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == borrowerId.Value, cancellationToken).ConfigureAwait(false);
                if (b != null)
                {
                    borrowerDisplayName = translationService != null ? translationService.Translate(b.Name, currentLang) : b.Name;
                }
            }

            return new TransactionReport(
                fromDate,
                toDate,
                transactionTypeFilter,
                borrowerDisplayName,
                items,
                totalDeposits,
                totalWithdrawals,
                totalDeposits - totalWithdrawals);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to generate transaction report.");
            throw;
        }
    }

    public async Task<InterestReport> GenerateInterestReportAsync(
        Guid? borrowerId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        if (fromDate > toDate)
        {
            throw new ArgumentException("From date cannot be after To date.");
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var localization = scope.ServiceProvider.GetService<ILocalizationService>();
            var translationService = scope.ServiceProvider.GetService<ITranslationService>();
            var interestService = scope.ServiceProvider.GetRequiredService<IInterestCalculationService>();
            var currentLang = localization?.CurrentLanguage ?? "gu-IN";

            var borrowersQuery = dbContext.Borrowers.AsNoTracking().AsQueryable();
            if (borrowerId.HasValue && borrowerId.Value != Guid.Empty)
            {
                borrowersQuery = borrowersQuery.Where(b => b.Id == borrowerId.Value);
            }
            else
            {
                borrowersQuery = borrowersQuery.Where(b => b.Status == BorrowerStatus.Active || b.Status == BorrowerStatus.Inactive);
            }

            var borrowers = await borrowersQuery.ToListAsync(cancellationToken).ConfigureAwait(false);

            var allSegments = new List<InterestReportSegment>();
            decimal grandOpening = 0m;
            decimal grandClosing = 0m;
            decimal grandInterest = 0m;

            foreach (var borrower in borrowers)
            {
                try
                {
                    var calcResult = await interestService.CalculateAsync(borrower.Id, toDate, cancellationToken).ConfigureAwait(false);
                    var bName = translationService != null ? translationService.Translate(borrower.Name, currentLang) : borrower.Name;

                    foreach (var s in calcResult.Segments)
                    {
                        if (s.SegmentEndDate >= fromDate && s.SegmentStartDate <= toDate)
                        {
                            allSegments.Add(new InterestReportSegment(
                                s.SegmentStartDate,
                                s.SegmentEndDate,
                                bName,
                                s.OpeningPrincipal,
                                s.ApplicableMonthlyRate,
                                s.ElapsedDays,
                                s.DaysInMonth,
                                s.CalculatedInterest,
                                s.TransactionType,
                                s.ClosingPrincipal));
                        }
                    }

                    grandOpening += calcResult.OpeningPrincipal;
                    grandClosing += calcResult.ClosingPrincipal;
                    grandInterest += calcResult.TotalInterest;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to calculate interest for borrower {BorrowerId}", borrower.Id);
                }
            }

            string displayName = "All Borrowers";
            string accountStatus = "Active";
            DateTime? closedDate = null;

            if (borrowerId.HasValue && borrowerId.Value != Guid.Empty && borrowers.Count > 0)
            {
                var single = borrowers.First();
                displayName = translationService != null ? translationService.Translate(single.Name, currentLang) : single.Name;
                accountStatus = single.Status.ToString();
                closedDate = single.ClosedDate ?? (single.Status == BorrowerStatus.Closed || single.Status == BorrowerStatus.Archived ? single.UpdatedAt : null);
            }

            return new InterestReport(
                borrowerId,
                displayName,
                fromDate,
                toDate,
                grandOpening,
                grandClosing,
                grandInterest,
                accountStatus,
                closedDate,
                allSegments);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to generate interest report.");
            throw;
        }
    }

    public async Task<OutstandingReport> GenerateOutstandingReportAsync(
        Guid? borrowerId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var localization = scope.ServiceProvider.GetService<ILocalizationService>();
            var translationService = scope.ServiceProvider.GetService<ITranslationService>();
            var currentLang = localization?.CurrentLanguage ?? "gu-IN";

            var query = dbContext.Borrowers.AsNoTracking().Include(b => b.Loans).AsQueryable();
            if (borrowerId.HasValue && borrowerId.Value != Guid.Empty)
            {
                query = query.Where(b => b.Id == borrowerId.Value);
            }
            else
            {
                query = query.Where(b => b.Status == BorrowerStatus.Active || b.Status == BorrowerStatus.Inactive);
            }

            var borrowers = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

            var items = new List<OutstandingReportItem>();
            decimal grandPrincipal = 0m;
            decimal grandInterest = 0m;
            decimal grandOutstanding = 0m;

            foreach (var borrower in borrowers.OrderBy(b => (b.BorrowerNumber ?? string.Empty).Length).ThenBy(b => b.BorrowerNumber ?? string.Empty))
            {
                var loan = borrower.Loans.OrderBy(l => l.IssueDate).FirstOrDefault();
                var principal = loan?.Principal.Amount ?? 0m;

                decimal interest = 0m;
                decimal outstanding = principal;
                if (borrower.Status == BorrowerStatus.Active || borrower.Status == BorrowerStatus.Inactive)
                {
                    try
                    {
                        var calc = await scope.ServiceProvider
                            .GetRequiredService<IInterestCalculationService>()
                            .CalculateAsync(borrower.Id, DateTime.Today, cancellationToken)
                            .ConfigureAwait(false);
                        interest = calc.TotalInterest;
                        principal = calc.ClosingPrincipal;
                        outstanding = calc.TotalOutstanding;
                    }
                    catch
                    {
                        interest = 0m;
                    }
                }

                var bName = translationService != null ? translationService.Translate(borrower.Name, currentLang) : borrower.Name;
                var bNum = borrower.BorrowerNumber ?? borrower.Id.ToString("N")[..8].ToUpperInvariant();

                var lastTxn = await dbContext.Transactions
                    .Where(t => t.BorrowerId == borrower.Id || t.FinancialPeriodId == borrower.Id)
                    .OrderByDescending(t => t.OccurredOn)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                items.Add(new OutstandingReportItem(
                    bNum,
                    bName,
                    borrower.Phone ?? string.Empty,
                    principal,
                    interest,
                    outstanding,
                    borrower.Status.ToString(),
                    lastTxn?.OccurredOn ?? borrower.CreatedAt));

                grandPrincipal += principal;
                grandInterest += interest;
                grandOutstanding += outstanding;
            }

            return new OutstandingReport(
                DateTime.Now,
                items,
                grandPrincipal,
                grandInterest,
                grandOutstanding);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to generate outstanding report.");
            throw;
        }
    }

    public async Task<BorrowerSummaryReport> GenerateBorrowerSummaryAsync(
        Guid? borrowerId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            var localization = scope.ServiceProvider.GetService<ILocalizationService>();
            var translationService = scope.ServiceProvider.GetService<ITranslationService>();
            var currentLang = localization?.CurrentLanguage ?? "gu-IN";

            var query = dbContext.Borrowers.AsNoTracking().AsQueryable();
            if (borrowerId.HasValue && borrowerId.Value != Guid.Empty)
            {
                query = query.Where(b => b.Id == borrowerId.Value);
            }

            var borrowers = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

            var totalBorrowers = borrowers.Count;
            var activeBorrowers = borrowers.Count(b => b.Status == BorrowerStatus.Active);
            var inactiveBorrowers = borrowers.Count(b => b.Status == BorrowerStatus.Inactive);
            var closedBorrowers = borrowers.Count(b => b.Status == BorrowerStatus.Closed || b.Status == BorrowerStatus.Archived);

            var items = new List<BorrowerSummaryItem>();
            decimal grandDeposits = 0m;
            decimal grandWithdrawals = 0m;
            decimal grandInterest = 0m;
            decimal grandOutstanding = 0m;

            foreach (var borrower in borrowers.OrderBy(b => (b.BorrowerNumber ?? string.Empty).Length).ThenBy(b => b.BorrowerNumber ?? string.Empty))
            {
                var txns = await dbContext.Transactions
                    .Where(t => t.BorrowerId == borrower.Id || t.FinancialPeriodId == borrower.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var dep = txns.Where(t => t.Type == TransactionType.Deposit).Sum(t => t.Amount.Amount);
                var with = txns.Where(t => t.Type == TransactionType.Withdrawal).Sum(t => t.Amount.Amount);
                var currentBal = with - dep;

                decimal interest = 0m;
                decimal totalOut = currentBal;
                try
                {
                    var calc = await scope.ServiceProvider
                        .GetRequiredService<IInterestCalculationService>()
                        .CalculateAsync(borrower.Id, DateTime.Today, cancellationToken)
                        .ConfigureAwait(false);
                    interest = calc.TotalInterest;
                    totalOut = calc.TotalOutstanding;
                }
                catch
                {
                    interest = 0m;
                }
                var lastTxn = txns.OrderByDescending(t => t.OccurredOn).FirstOrDefault();
                var bName = translationService != null ? translationService.Translate(borrower.Name, currentLang) : borrower.Name;
                var bNum = borrower.BorrowerNumber ?? borrower.Id.ToString("N")[..8].ToUpperInvariant();

                items.Add(new BorrowerSummaryItem(
                    bNum,
                    bName,
                    borrower.Phone ?? string.Empty,
                    with,
                    dep,
                    interest,
                    currentBal,
                    totalOut,
                    borrower.Status.ToString(),
                    lastTxn?.OccurredOn ?? borrower.CreatedAt));

                grandDeposits += dep;
                grandWithdrawals += with;
                grandInterest += interest;
                grandOutstanding += totalOut;
            }

            return new BorrowerSummaryReport(
                DateTime.Now,
                totalBorrowers,
                activeBorrowers,
                inactiveBorrowers,
                closedBorrowers,
                items,
                grandDeposits,
                grandWithdrawals,
                grandInterest,
                grandOutstanding);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to generate borrower summary report.");
            throw;
        }
    }
}
