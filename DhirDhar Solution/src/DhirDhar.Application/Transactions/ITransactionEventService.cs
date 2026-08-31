using System;

namespace DhirDhar.Application.Transactions;

public enum TransactionMutationKind
{
    Created = 1,
    Updated = 2,
    Deleted = 3,
    Reversed = 4,
    Adjusted = 5
}

public sealed record TransactionChangedEventArgs(
    Guid? TransactionId = null,
    Guid? BorrowerId = null,
    TransactionMutationKind MutationKind = TransactionMutationKind.Created,
    DateTime? Timestamp = null);

public interface ITransactionEventService
{
    event EventHandler<TransactionChangedEventArgs>? TransactionChanged;

    void PublishTransactionChanged(TransactionChangedEventArgs? args = null);
}
