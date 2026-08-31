using DhirDhar.Application.Transactions.Models;

namespace DhirDhar.Application.Transactions;

public interface ITransactionService
{
    Task<TransactionSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TransactionListResult> GetListAsync(TransactionFilterRequest filter, CancellationToken cancellationToken = default);

    Task<TransactionFinancials> GetFinancialsAsync(Guid? borrowerId = null, CancellationToken cancellationToken = default);

    Task<TransactionSummary> CreateAsync(CreateTransactionRequest request, CancellationToken cancellationToken = default);

    Task<TransactionSummary?> GetLatestTransactionAsync(Guid borrowerId, CancellationToken cancellationToken = default);
}
