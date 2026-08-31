using System;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Reports.Models;

namespace DhirDhar.Application.Reports;

public interface IReportService
{
    Task<BorrowerStatementReport> GenerateBorrowerStatementAsync(Guid borrowerId, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    Task<TransactionReport> GenerateTransactionReportAsync(DateTime fromDate, DateTime toDate, Guid? borrowerId, string transactionTypeFilter, CancellationToken cancellationToken = default);

    Task<InterestReport> GenerateInterestReportAsync(Guid? borrowerId, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    Task<OutstandingReport> GenerateOutstandingReportAsync(Guid? borrowerId = null, CancellationToken cancellationToken = default);

    Task<BorrowerSummaryReport> GenerateBorrowerSummaryAsync(Guid? borrowerId = null, CancellationToken cancellationToken = default);
}
