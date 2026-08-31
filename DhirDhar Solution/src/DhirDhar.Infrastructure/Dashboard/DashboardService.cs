using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Caching;
using DhirDhar.Application.Dashboard;
using DhirDhar.Application.Dashboard.Models;
using DhirDhar.Application.Localization;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.Interest;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Dashboard;

public sealed class DashboardService : IDashboardService
{
    private const int RecentTransactionLimit = 5;
    private const string DashboardSummaryCacheKey = "dashboard_summary";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DashboardService> _logger;
    private readonly ILocalizationService _localizationService;
    private readonly ICacheService? _cacheService;

    public DashboardService(
        IServiceScopeFactory scopeFactory,
        ILogger<DashboardService> logger,
        ILocalizationService localizationService,
        ICacheService? cacheService = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _localizationService = localizationService;
        _cacheService = cacheService ?? scopeFactory.CreateScope().ServiceProvider.GetService<ICacheService>();
    }

    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (_cacheService != null)
        {
            var cached = _cacheService.Get<DashboardSummary>(DashboardSummaryCacheKey);
            if (cached != null)
            {
                return cached;
            }
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var totalBorrowers = await dbContext.Borrowers.CountAsync(cancellationToken).ConfigureAwait(false);
            var activeBorrowers = await dbContext.Borrowers.CountAsync(b => b.Status == BorrowerStatus.Active, cancellationToken).ConfigureAwait(false);
            var inactiveBorrowers = await dbContext.Borrowers.CountAsync(b => b.Status == BorrowerStatus.Inactive, cancellationToken).ConfigureAwait(false);
            var closedBorrowers = await dbContext.Borrowers.CountAsync(b => b.Status == BorrowerStatus.Closed || b.Status == BorrowerStatus.Archived, cancellationToken).ConfigureAwait(false);

            var activeBorrowerIds = await dbContext.Borrowers
                .AsNoTracking()
                .Where(b => b.Status == BorrowerStatus.Active)
                .Select(b => b.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            decimal deposits = 0m;
            decimal withdrawals = 0m;
            decimal totalInterest = 0m;

            if (activeBorrowerIds.Count > 0)
            {
                const int chunkSize = 500;
                for (int i = 0; i < activeBorrowerIds.Count; i += chunkSize)
                {
                    var chunk = activeBorrowerIds.Skip(i).Take(chunkSize).ToList();
                    var txns = await dbContext.Transactions
                        .AsNoTracking()
                        .Where(t => t.BorrowerId.HasValue && chunk.Contains(t.BorrowerId.Value))
                        .Select(t => new { t.Type, Amount = t.Amount.Amount })
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);

                    deposits += txns.Where(t => t.Type == TransactionType.Deposit).Sum(t => t.Amount);
                    withdrawals += txns.Where(t => t.Type == TransactionType.Withdrawal).Sum(t => t.Amount);
                }

                var interestService = scope.ServiceProvider.GetService<DhirDhar.Application.Interest.IInterestCalculationService>();
                if (interestService != null)
                {
                    try
                    {
                        var batchInterest = await interestService.CalculateBatchAsync(activeBorrowerIds, DateTime.Today, cancellationToken).ConfigureAwait(false);
                        totalInterest = batchInterest.Values.Sum();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to calculate batch interest during dashboard load.");
                    }
                }
            }

            decimal outstanding = deposits - withdrawals;

            var recentTransactions = await GetRecentTransactionsAsync(dbContext, cancellationToken).ConfigureAwait(false);
            var today = DateTime.Today;
            var periodSummary = await GetMonthlyPeriodSummaryCoreAsync(dbContext, today.Year, today.Month, cancellationToken).ConfigureAwait(false);
            var historicalOutstanding = await GetHistoricalOutstandingAsync(dbContext, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Dashboard summary loaded. Borrowers={TotalBorrowers}, Active={ActiveBorrowers}, Deposits={TotalDeposits}, Withdrawals={TotalWithdrawals}, Outstanding={Outstanding}, Interest={TotalInterest}.",
                totalBorrowers, activeBorrowers, deposits, withdrawals, outstanding, totalInterest);

            var summary = new DashboardSummary(
                totalBorrowers,
                activeBorrowers,
                inactiveBorrowers,
                closedBorrowers,
                deposits,
                withdrawals,
                outstanding,
                totalInterest,
                recentTransactions,
                periodSummary,
                historicalOutstanding);

            _cacheService?.Set(DashboardSummaryCacheKey, summary, slidingExpiration: TimeSpan.FromSeconds(60), absoluteExpiration: TimeSpan.FromMinutes(5));
            return summary;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load dashboard summary.");
            return DashboardSummary.Empty;
        }
    }

    private async Task<IReadOnlyList<RecentTransactionSummary>> GetRecentTransactionsAsync(
        DhirDharDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var transactionData = await dbContext.Transactions
            .AsNoTracking()
            .OrderByDescending(t => t.OccurredOn)
            .ThenByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(RecentTransactionLimit)
            .Select(t => new
            {
                t.Id,
                t.FinancialPeriodId,
                t.Type,
                t.Amount.Amount,
                t.OccurredOn,
                t.Description,
                t.BorrowerId
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (transactionData.Count == 0)
        {
            return Array.Empty<RecentTransactionSummary>();
        }

        var borrowerIds = transactionData
            .Where(t => t.BorrowerId.HasValue && t.BorrowerId.Value != Guid.Empty)
            .Select(t => t.BorrowerId!.Value)
            .Distinct()
            .ToHashSet();

        var fallbackCandidateIds = transactionData
            .Where(t => (!t.BorrowerId.HasValue || t.BorrowerId.Value == Guid.Empty) && t.FinancialPeriodId != Guid.Empty)
            .Select(t => t.FinancialPeriodId)
            .Distinct()
            .ToHashSet();

        var allQueryIds = borrowerIds.Union(fallbackCandidateIds).ToList();

        var allBorrowers = await dbContext.Borrowers
            .AsNoTracking()
            .Where(b => allQueryIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var borrowerNames = allBorrowers.ToDictionary(b => b.Id, x => x.Name);

        // Check if any remaining IDs point to loans
        var missingIds = allQueryIds.Where(id => !borrowerNames.ContainsKey(id)).ToList();
        if (missingIds.Count > 0)
        {
            var loanBorrowers = await dbContext.Loans
                .AsNoTracking()
                .Include(l => l.Borrower)
                .Where(l => missingIds.Contains(l.Id) && l.Borrower != null)
                .Select(l => new { l.Id, l.Borrower!.Name })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var lb in loanBorrowers)
            {
                borrowerNames[lb.Id] = lb.Name;
            }
        }

        return transactionData
            .Select(t =>
            {
                string borrowerName = string.Empty;
                var effectiveId = t.BorrowerId ?? t.FinancialPeriodId;
                if (effectiveId != Guid.Empty && borrowerNames.TryGetValue(effectiveId, out string? bName))
                {
                    borrowerName = bName;
                }

                string typeKey = t.Type.ToString();
                return new RecentTransactionSummary(
                    t.Id,
                    t.FinancialPeriodId.ToString("N")[..8].ToUpperInvariant(),
                    _localizationService.GetString(typeKey),
                    typeKey,
                    t.Amount,
                    t.OccurredOn,
                    t.Description ?? string.Empty,
                    borrowerName);
            })
            .ToList();
    }

    public async Task<PeriodSummaryInfo> GetMonthlyPeriodSummaryAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            return await GetMonthlyPeriodSummaryCoreAsync(dbContext, year, month, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to get monthly period summary for {Year}-{Month:00}.", year, month);
            return new PeriodSummaryInfo(0m, 0m, 0m, 0m);
        }
    }

    public async Task<PeriodSummaryInfo> GetYearlyPeriodSummaryAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();
            return await GetYearlyPeriodSummaryCoreAsync(dbContext, year, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to get yearly period summary for {Year}.", year);
            return new PeriodSummaryInfo(0m, 0m, 0m, 0m);
        }
    }

    private static async Task<PeriodSummaryInfo> GetYearlyPeriodSummaryCoreAsync(
        DhirDharDbContext dbContext,
        int year,
        CancellationToken cancellationToken)
    {
        var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var yearEnd = new DateTime(year, 12, 31, 23, 59, 59, 999, DateTimeKind.Unspecified);

        var yearTxns = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.OccurredOn >= yearStart && t.OccurredOn <= yearEnd)
            .Select(t => new
            {
                t.Type,
                Amount = t.Amount.Amount,
                t.Description,
                t.Reference
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var borrowersWithLoanInYear = await dbContext.Borrowers
            .AsNoTracking()
            .Where(b => b.LoanAmount.HasValue && b.LoanAmount.Value > 0m &&
                        b.LoanDate.HasValue && b.LoanDate.Value >= yearStart && b.LoanDate.Value <= yearEnd)
            .Select(b => new
            {
                b.Id,
                LoanAmount = b.LoanAmount ?? 0m,
                LoanDate = b.LoanDate ?? yearStart
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // If the selected year has NO transactions and no loans at all, return all zeros
        if (yearTxns.Count == 0 && borrowersWithLoanInYear.Count == 0)
        {
            return new PeriodSummaryInfo(0m, 0m, 0m, 0m);
        }

        var depositsBefore = (await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Type == TransactionType.Deposit && t.OccurredOn < yearStart)
            .Select(t => t.Amount.Amount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).Sum();

        var withdrawalsBefore = (await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Type == TransactionType.Withdrawal && t.OccurredOn < yearStart)
            .Select(t => t.Amount.Amount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).Sum();

        var opening = depositsBefore - withdrawalsBefore;

        var newLoans = yearTxns
            .Where(t => t.Type == TransactionType.Withdrawal)
            .Sum(t => t.Amount);

        var initialWithdrawalCount = yearTxns
            .Count(t => t.Type == TransactionType.Withdrawal &&
                        (string.Equals(t.Description, "Initial Loan Amount", StringComparison.OrdinalIgnoreCase) ||
                         (t.Reference != null && t.Reference.StartsWith("INIT-"))));
        if (initialWithdrawalCount == 0 && borrowersWithLoanInYear.Count > 0)
        {
            newLoans += borrowersWithLoanInYear.Sum(b => b.LoanAmount);
        }

        var payments = yearTxns
            .Where(t => t.Type == TransactionType.Deposit)
            .Sum(t => t.Amount);

        var closing = opening + payments - newLoans;

        return new PeriodSummaryInfo(opening, newLoans, payments, closing);
    }

    private static async Task<PeriodSummaryInfo> GetMonthlyPeriodSummaryCoreAsync(
        DhirDharDbContext dbContext,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateTime(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var monthEnd = new DateTime(year, month, daysInMonth, 23, 59, 59, 999);

        var depositsBefore = (await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Type == TransactionType.Deposit && t.OccurredOn < monthStart)
            .Select(t => t.Amount.Amount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).Sum();

        var withdrawalsBefore = (await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Type == TransactionType.Withdrawal && t.OccurredOn < monthStart)
            .Select(t => t.Amount.Amount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).Sum();

        var depositsInMonth = (await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Type == TransactionType.Deposit && t.OccurredOn >= monthStart && t.OccurredOn <= monthEnd)
            .Select(t => t.Amount.Amount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).Sum();

        var withdrawalsInMonth = (await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Type == TransactionType.Withdrawal && t.OccurredOn >= monthStart && t.OccurredOn <= monthEnd)
            .Select(t => t.Amount.Amount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).Sum();

        var opening = depositsBefore - withdrawalsBefore;
        var closing = opening + depositsInMonth - withdrawalsInMonth;

        return new PeriodSummaryInfo(opening, withdrawalsInMonth, depositsInMonth, closing);
    }

    private async Task<IReadOnlyList<HistoricalOutstandingPoint>> GetHistoricalOutstandingAsync(
        DhirDharDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.Today;
            var currentMonthStart = new DateTime(now.Year, now.Month, 1);
            var rawPoints = new List<(string Label, decimal Amount)>();

            for (int i = 5; i >= 0; i--)
            {
                var monthStart = currentMonthStart.AddMonths(-i);
                var daysInMonth = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
                var monthEnd = new DateTime(monthStart.Year, monthStart.Month, daysInMonth, 23, 59, 59, 999);

                var depList = await dbContext.Transactions
                    .AsNoTracking()
                    .Where(t => t.Type == TransactionType.Deposit && t.OccurredOn <= monthEnd)
                    .Select(t => t.Amount.Amount)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var deposits = depList.Sum();

                var withList = await dbContext.Transactions
                    .AsNoTracking()
                    .Where(t => t.Type == TransactionType.Withdrawal && t.OccurredOn <= monthEnd)
                    .Select(t => t.Amount.Amount)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var withdrawals = withList.Sum();

                var net = Math.Abs(deposits - withdrawals);
                rawPoints.Add((_localizationService.ToLocalizedDate(monthStart, "MMM"), net));
            }

            var maxAmount = rawPoints.Count > 0 ? rawPoints.Max(p => p.Amount) : 0m;
            const double maxBarHeight = 140.0;
            const double minActiveBarHeight = 8.0;
            const double baselineEmptyHeight = 4.0;

            return rawPoints.Select(p =>
            {
                double height = baselineEmptyHeight;
                if (maxAmount > 0m && p.Amount > 0m)
                {
                    double ratio = (double)(p.Amount / maxAmount);
                    height = minActiveBarHeight + ratio * (maxBarHeight - minActiveBarHeight);
                }

                if (double.IsNaN(height) || double.IsInfinity(height) || height < baselineEmptyHeight)
                {
                    height = baselineEmptyHeight;
                }
                if (height > maxBarHeight)
                {
                    height = maxBarHeight;
                }

                return new HistoricalOutstandingPoint(p.Label, p.Amount, height);
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate historical outstanding points.");
            return Array.Empty<HistoricalOutstandingPoint>();
        }
    }

    public async Task<IReadOnlyList<int>> GetAvailableYearsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var txnYears = await dbContext.Transactions
                .AsNoTracking()
                .Select(t => t.OccurredOn.Year)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var borrowerLoanYears = await dbContext.Borrowers
                .AsNoTracking()
                .Where(b => b.LoanDate.HasValue)
                .Select(b => b.LoanDate!.Value.Year)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var borrowerEntryYears = await dbContext.Borrowers
                .AsNoTracking()
                .Select(b => b.EntryDate.Year)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var years = new HashSet<int>(txnYears);
            foreach (var y in borrowerLoanYears) years.Add(y);
            foreach (var y in borrowerEntryYears) years.Add(y);

            var currentYear = DateTime.Today.Year;
            years.Add(currentYear);
            years.Add(currentYear - 1);
            years.Add(currentYear - 2);

            return years.Where(y => y >= 1900 && y <= 2100).OrderByDescending(y => y).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load available years for dashboard.");
            var currentYear = DateTime.Today.Year;
            return new List<int> { currentYear, currentYear - 1, currentYear - 2 };
        }
    }

    public async Task<YearlyOutstandingChartData> GetYearlyChartDataAsync(int year, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

            var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var yearEnd = new DateTime(year, 12, 31, 23, 59, 59, 999, DateTimeKind.Unspecified);

            var yearTransactions = await dbContext.Transactions
                .AsNoTracking()
                .Where(t => t.OccurredOn >= yearStart && t.OccurredOn <= yearEnd)
                .Select(t => new
                {
                    t.Id,
                    t.BorrowerId,
                    t.Type,
                    Amount = t.Amount.Amount,
                    t.OccurredOn,
                    t.Description,
                    t.Reference,
                    t.CreatedAt
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var borrowersWithLoanInYear = await dbContext.Borrowers
                .AsNoTracking()
                .Where(b => b.LoanAmount.HasValue && b.LoanAmount.Value > 0m &&
                            b.LoanDate.HasValue && b.LoanDate.Value >= yearStart && b.LoanDate.Value <= yearEnd)
                .Select(b => new
                {
                    b.Id,
                    LoanAmount = b.LoanAmount ?? 0m,
                    LoanDate = b.LoanDate ?? yearStart
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // Fetch only currently ACTIVE borrowers for outstanding interest calculation
            var activeBorrowers = await dbContext.Borrowers
                .AsNoTracking()
                .Where(b => b.Status == BorrowerStatus.Active)
                .Select(b => new
                {
                    b.Id,
                    b.Status,
                    b.EntryDate,
                    b.LoanDate,
                    b.LoanAmount,
                    b.InterestRate,
                    b.ClosedDate,
                    b.UpdatedAt
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var activeBorrowerIds = activeBorrowers.Select(b => b.Id).ToList();

            var loans = activeBorrowerIds.Count > 0
                ? await dbContext.Loans
                    .AsNoTracking()
                    .Where(l => activeBorrowerIds.Contains(l.BorrowerId))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false)
                : new List<Loan>();

            var loansDict = loans
                .GroupBy(l => l.BorrowerId)
                .ToDictionary(g => g.Key, g => g.OrderBy(l => l.IssueDate).FirstOrDefault());

            var allTxnsUpToYearEnd = activeBorrowerIds.Count > 0
                ? await dbContext.Transactions
                    .AsNoTracking()
                    .Where(t => t.OccurredOn <= yearEnd && t.BorrowerId.HasValue && activeBorrowerIds.Contains(t.BorrowerId.Value))
                    .Select(t => new
                    {
                        t.Id,
                        BorrowerId = t.BorrowerId!.Value,
                        t.Type,
                        Amount = t.Amount.Amount,
                        t.OccurredOn,
                        t.Description,
                        t.Reference,
                        t.CreatedAt
                    })
                    .OrderBy(t => t.OccurredOn)
                    .ThenBy(t => t.CreatedAt)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false)
                : new();

            var txnsByBorrower = allTxnsUpToYearEnd
                .GroupBy(t => t.BorrowerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            decimal defaultSettingRate = 3.0m;
            var setting = await dbContext.ApplicationSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "DefaultInterestRate", cancellationToken)
                .ConfigureAwait(false);
            if (setting != null && decimal.TryParse(setting.Value, out var parsedRate) && parsedRate > 0m)
            {
                defaultSettingRate = parsedRate;
            }

            var borrowerInterestByMonth = new Dictionary<int, decimal>();
            var explicitInterestByMonth = new Dictionary<int, decimal>();
            for (int m = 1; m <= 12; m++)
            {
                borrowerInterestByMonth[m] = 0m;
                explicitInterestByMonth[m] = 0m;
            }

            foreach (var txn in yearTransactions)
            {
                if (txn.Description != null && txn.Description.Contains("Interest", StringComparison.OrdinalIgnoreCase))
                {
                    explicitInterestByMonth[txn.OccurredOn.Month] += txn.Amount;
                }
            }

            foreach (var b in activeBorrowers)
            {
                loansDict.TryGetValue(b.Id, out var loan);
                txnsByBorrower.TryGetValue(b.Id, out var bTxns);
                bTxns ??= new();

                var firstLoanTxn = bTxns
                    .Where(t => t.Type == TransactionType.Withdrawal ||
                                string.Equals(t.Description, "Initial Loan Amount", StringComparison.OrdinalIgnoreCase) ||
                                (t.Reference != null && t.Reference.StartsWith("INIT-")))
                    .FirstOrDefault();

                var hasInitialTxn = firstLoanTxn != null;
                var principal = hasInitialTxn ? 0m : (loan?.Principal.Amount ?? (b.LoanAmount ?? 0m));
                var startDate = firstLoanTxn != null ? firstLoanTxn.OccurredOn : (b.LoanDate ?? loan?.IssueDate ?? b.EntryDate);

                if (startDate > yearEnd)
                {
                    continue;
                }

                decimal monthlyRate = (b.InterestRate.HasValue && b.InterestRate.Value > 0m)
                    ? b.InterestRate.Value
                    : (loan != null && loan.InterestRatePercent > 0m ? loan.InterestRatePercent : defaultSettingRate);

                var accountStatus = AccountStatus.Active;
                var closedDate = (DateTime?)null;

                var events = new List<FinancialEvent>(bTxns.Count);
                int seq = 0;
                foreach (var txn in bTxns)
                {
                    events.Add(new FinancialEvent(txn.OccurredOn, txn.Type.ToString(), txn.Amount, txn.Description, seq++));
                }

                var ratePeriods = new List<InterestRatePeriod>
                {
                    new(monthlyRate, startDate, null)
                };

                var calcResult = InterestCalculator.Calculate(
                    b.Id,
                    principal,
                    startDate,
                    yearEnd,
                    events,
                    ratePeriods,
                    accountStatus,
                    closedDate);

                foreach (var seg in calcResult.Segments)
                {
                    if (seg.SegmentStartDate.Year == year && seg.SegmentStartDate.Month >= 1 && seg.SegmentStartDate.Month <= 12)
                    {
                        borrowerInterestByMonth[seg.SegmentStartDate.Month] += seg.CalculatedInterest;
                    }
                }
            }

            var monthlyRawData = new List<(int Month, decimal NewLoans, decimal Withdrawals, decimal Deposits, decimal InterestEarned)>();

            for (int m = 1; m <= 12; m++)
            {
                var initialTxnBorrowerIdsInMonth = yearTransactions
                    .Where(t => t.OccurredOn.Month == m && t.BorrowerId.HasValue &&
                                (string.Equals(t.Description, "Initial Loan Amount", StringComparison.OrdinalIgnoreCase) ||
                                 (t.Reference != null && t.Reference.StartsWith("INIT-")) ||
                                 (t.Description != null && t.Description.Contains("Loan", StringComparison.OrdinalIgnoreCase) && !t.Description.Contains("Repayment", StringComparison.OrdinalIgnoreCase))))
                    .Select(t => t.BorrowerId!.Value)
                    .ToHashSet();

                var newLoanTxnsAmount = yearTransactions
                    .Where(t => t.OccurredOn.Month == m && t.Type == TransactionType.Withdrawal &&
                                (string.Equals(t.Description, "Initial Loan Amount", StringComparison.OrdinalIgnoreCase) ||
                                 (t.Reference != null && t.Reference.StartsWith("INIT-")) ||
                                 (t.Description != null && t.Description.Contains("Loan", StringComparison.OrdinalIgnoreCase) && !t.Description.Contains("Repayment", StringComparison.OrdinalIgnoreCase))))
                    .Sum(t => t.Amount);

                var borrowerLoanAmount = borrowersWithLoanInYear
                    .Where(b => b.LoanDate.Month == m && !initialTxnBorrowerIdsInMonth.Contains(b.Id))
                    .Sum(b => b.LoanAmount);

                decimal newLoans = newLoanTxnsAmount + borrowerLoanAmount;

                decimal withdrawals = yearTransactions
                    .Where(t => t.OccurredOn.Month == m && t.Type == TransactionType.Withdrawal &&
                                !(string.Equals(t.Description, "Initial Loan Amount", StringComparison.OrdinalIgnoreCase) ||
                                  (t.Reference != null && t.Reference.StartsWith("INIT-")) ||
                                  (t.Description != null && t.Description.Contains("Loan", StringComparison.OrdinalIgnoreCase) && !t.Description.Contains("Repayment", StringComparison.OrdinalIgnoreCase))))
                    .Sum(t => t.Amount);

                decimal deposits = yearTransactions
                    .Where(t => t.OccurredOn.Month == m && t.Type == TransactionType.Deposit &&
                                !(t.Description != null && t.Description.Contains("Interest", StringComparison.OrdinalIgnoreCase)))
                    .Sum(t => t.Amount);

                decimal interestEarned = Math.Max(borrowerInterestByMonth[m], explicitInterestByMonth[m]);

                monthlyRawData.Add((m, newLoans, withdrawals, deposits, interestEarned));
            }

            decimal maxAmount = 0m;
            foreach (var item in monthlyRawData)
            {
                if (item.NewLoans > maxAmount) maxAmount = item.NewLoans;
                if (item.Withdrawals > maxAmount) maxAmount = item.Withdrawals;
                if (item.Deposits > maxAmount) maxAmount = item.Deposits;
                if (item.InterestEarned > maxAmount) maxAmount = item.InterestEarned;
            }

            if (maxAmount <= 0m && yearTransactions.Count == 0 && borrowersWithLoanInYear.Count == 0)
            {
                return CreateEmptyYearlyChartData(year);
            }

            var (maxYAxisValue, tickValues) = CalculateNiceScale(maxAmount);

            var yAxisTicks = new List<ChartYAxisTick>();
            foreach (var tickVal in tickValues)
            {
                double normalized = maxYAxisValue > 0 ? (double)(tickVal / maxYAxisValue) : 0.0;
                yAxisTicks.Add(new ChartYAxisTick(tickVal, FormatYAxisLabel(tickVal), normalized));
            }

            const double maxPlotHeight = 140.0;
            const string orangeColor = "#F59E0B";
            const string redColor = "#EF4444";
            const string greenColor = "#10B981";
            const string blueColor = "#3B82F6";

            var newLoansLabel = _localizationService.GetString("NewLoans");
            if (string.IsNullOrWhiteSpace(newLoansLabel) || newLoansLabel == "NewLoans") newLoansLabel = "New Loans";

            var withdrawalsLabel = _localizationService.GetString("Withdrawals");
            if (string.IsNullOrWhiteSpace(withdrawalsLabel) || withdrawalsLabel == "Withdrawals") withdrawalsLabel = _localizationService.GetString("TotalWithdrawals");
            if (string.IsNullOrWhiteSpace(withdrawalsLabel) || withdrawalsLabel == "TotalWithdrawals") withdrawalsLabel = "Withdrawals";

            var depositsLabel = _localizationService.GetString("Deposits");
            if (string.IsNullOrWhiteSpace(depositsLabel) || depositsLabel == "Deposits") depositsLabel = _localizationService.GetString("TotalDeposits");
            if (string.IsNullOrWhiteSpace(depositsLabel) || depositsLabel == "TotalDeposits") depositsLabel = "Deposits";

            var interestEarnedLabel = _localizationService.GetString("InterestEarned");
            if (string.IsNullOrWhiteSpace(interestEarnedLabel) || interestEarnedLabel == "InterestEarned") interestEarnedLabel = "Interest Earned";

            var monthlyGroups = new List<MonthlyChartGroup>();

            foreach (var row in monthlyRawData)
            {
                var monthDt = new DateTime(year, row.Month, 1);
                var monthShort = _localizationService.ToLocalizedDate(monthDt, "MMM");
                var monthFull = _localizationService.ToLocalizedDate(monthDt, "MMMM yyyy");

                double hNewLoans = (maxYAxisValue > 0 && row.NewLoans > 0) ? Math.Max(2.0, (double)(row.NewLoans / maxYAxisValue) * maxPlotHeight) : 0.0;
                double hWithdrawals = (maxYAxisValue > 0 && row.Withdrawals > 0) ? Math.Max(2.0, (double)(row.Withdrawals / maxYAxisValue) * maxPlotHeight) : 0.0;
                double hDeposits = (maxYAxisValue > 0 && row.Deposits > 0) ? Math.Max(2.0, (double)(row.Deposits / maxYAxisValue) * maxPlotHeight) : 0.0;
                double hInterest = (maxYAxisValue > 0 && row.InterestEarned > 0) ? Math.Max(2.0, (double)(row.InterestEarned / maxYAxisValue) * maxPlotHeight) : 0.0;

                var fNewLoans = _localizationService.ToLocalizedCurrency(row.NewLoans);
                var fWithdrawals = _localizationService.ToLocalizedCurrency(row.Withdrawals);
                var fDeposits = _localizationService.ToLocalizedCurrency(row.Deposits);
                var fInterest = _localizationService.ToLocalizedCurrency(row.InterestEarned);

                var tipNewLoans = $"{monthFull}\n{newLoansLabel}\n{fNewLoans}";
                var tipWithdrawals = $"{monthFull}\n{withdrawalsLabel}\n{fWithdrawals}";
                var tipDeposits = $"{monthFull}\n{depositsLabel}\n{fDeposits}";
                var tipInterest = $"{monthFull}\n{interestEarnedLabel}\n{fInterest}";

                var newLoansBar = new MonthlyChartBar("NewLoans", newLoansLabel, row.NewLoans, fNewLoans, hNewLoans, orangeColor, tipNewLoans);
                var withdrawalsBar = new MonthlyChartBar("Withdrawals", withdrawalsLabel, row.Withdrawals, fWithdrawals, hWithdrawals, redColor, tipWithdrawals);
                var depositsBar = new MonthlyChartBar("Deposits", depositsLabel, row.Deposits, fDeposits, hDeposits, greenColor, tipDeposits);
                var interestBar = new MonthlyChartBar("InterestEarned", interestEarnedLabel, row.InterestEarned, fInterest, hInterest, blueColor, tipInterest);

                monthlyGroups.Add(new MonthlyChartGroup(row.Month, year, monthShort, monthFull, newLoansBar, withdrawalsBar, depositsBar, interestBar));
            }

            return new YearlyOutstandingChartData(year, maxAmount, maxYAxisValue, yAxisTicks, monthlyGroups);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load yearly chart data for year {Year}.", year);
            return CreateEmptyYearlyChartData(year);
        }
    }

    private YearlyOutstandingChartData CreateEmptyYearlyChartData(int year)
    {
        var (maxY, ticks) = CalculateNiceScale(0m);
        var yAxisTicks = ticks.Select(t => new ChartYAxisTick(t, FormatYAxisLabel(t), (double)(t / maxY))).ToList();
        var monthlyGroups = new List<MonthlyChartGroup>();
        for (int m = 1; m <= 12; m++)
        {
            var dt = new DateTime(year, m, 1);
            var mShort = _localizationService.ToLocalizedDate(dt, "MMM");
            var mFull = _localizationService.ToLocalizedDate(dt, "MMMM yyyy");
            var zeroF = _localizationService.ToLocalizedCurrency(0m);

            var bar1 = new MonthlyChartBar("NewLoans", "New Loans", 0m, zeroF, 0.0, "#F59E0B", $"{mFull}\nNew Loans\n{zeroF}");
            var bar2 = new MonthlyChartBar("Withdrawals", "Withdrawals", 0m, zeroF, 0.0, "#EF4444", $"{mFull}\nWithdrawals\n{zeroF}");
            var bar3 = new MonthlyChartBar("Deposits", "Deposits", 0m, zeroF, 0.0, "#10B981", $"{mFull}\nDeposits\n{zeroF}");
            var bar4 = new MonthlyChartBar("InterestEarned", "Interest Earned", 0m, zeroF, 0.0, "#3B82F6", $"{mFull}\nInterest Earned\n{zeroF}");

            monthlyGroups.Add(new MonthlyChartGroup(m, year, mShort, mFull, bar1, bar2, bar3, bar4));
        }
        return new YearlyOutstandingChartData(year, 0m, maxY, yAxisTicks, monthlyGroups);
    }

    private static (decimal MaxY, List<decimal> Ticks) CalculateNiceScale(decimal maxVal, int tickCount = 5)
    {
        if (maxVal <= 0m)
        {
            return (1000000m, new List<decimal> { 0m, 200000m, 400000m, 600000m, 800000m, 1000000m });
        }

        if (maxVal <= 100000m)
        {
            return (100000m, new List<decimal> { 0m, 20000m, 40000m, 60000m, 80000m, 100000m });
        }
        if (maxVal <= 200000m)
        {
            return (200000m, new List<decimal> { 0m, 50000m, 100000m, 150000m, 200000m });
        }
        if (maxVal <= 500000m)
        {
            return (500000m, new List<decimal> { 0m, 100000m, 200000m, 300000m, 400000m, 500000m });
        }
        if (maxVal <= 1000000m)
        {
            return (1000000m, new List<decimal> { 0m, 200000m, 400000m, 600000m, 800000m, 1000000m });
        }

        double range = (double)maxVal;
        double roughStep = range / (tickCount - 1);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1.0, roughStep))));
        double residual = roughStep / magnitude;

        double niceStep;
        if (residual <= 1.0) niceStep = 1.0 * magnitude;
        else if (residual <= 2.0) niceStep = 2.0 * magnitude;
        else if (residual <= 2.5) niceStep = 2.5 * magnitude;
        else if (residual <= 5.0) niceStep = 5.0 * magnitude;
        else niceStep = 10.0 * magnitude;

        decimal step = (decimal)niceStep;
        if (step <= 0m) step = 100000m;

        var ticks = new List<decimal>();
        decimal current = 0m;
        while (current < maxVal || ticks.Count < tickCount)
        {
            ticks.Add(current);
            current += step;
            if (ticks.Count > 10) break;
        }
        ticks.Add(current);
        decimal maxY = current;
        return (maxY, ticks);
    }

    private string FormatYAxisLabel(decimal val)
    {
        if (val == 0m)
        {
            return "₹0";
        }
        if (val >= 10000000m)
        {
            return $"₹{val / 10000000m:0.#}Cr";
        }
        if (val >= 100000m)
        {
            return $"₹{val / 100000m:0.#}L";
        }
        if (val >= 1000m)
        {
            return $"₹{val / 1000m:0.#}K";
        }
        return $"₹{val:0}";
    }
}

