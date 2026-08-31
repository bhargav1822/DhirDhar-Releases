using System;
using DhirDhar.Application.Transactions;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Transactions;

public sealed class TransactionEventService : ITransactionEventService
{
    private readonly ILogger<TransactionEventService> _logger;

    public TransactionEventService(ILogger<TransactionEventService> logger)
    {
        _logger = logger;
    }

    public event EventHandler<TransactionChangedEventArgs>? TransactionChanged;

    public void PublishTransactionChanged(TransactionChangedEventArgs? args = null)
    {
        try
        {
            var eventArgs = args ?? new TransactionChangedEventArgs(Timestamp: DateTime.UtcNow);
            if (eventArgs.Timestamp == null)
            {
                eventArgs = eventArgs with { Timestamp = DateTime.UtcNow };
            }

            _logger.LogInformation(
                "Central transaction event published: Kind={Kind}, TxnId={TxnId}, BorrowerId={BorrowerId}",
                eventArgs.MutationKind, eventArgs.TransactionId, eventArgs.BorrowerId);

            TransactionChanged?.Invoke(this, eventArgs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while broadcasting TransactionChanged event to subscribers.");
        }
    }
}
