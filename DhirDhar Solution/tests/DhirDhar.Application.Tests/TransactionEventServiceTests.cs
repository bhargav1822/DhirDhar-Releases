using System;
using DhirDhar.Application.Transactions;
using Xunit;

namespace DhirDhar.Application.Tests;

public sealed class TransactionEventServiceTests
{
    [Fact]
    public void TransactionChangedEventArgs_InitializesWithDefaultValues()
    {
        var now = DateTime.UtcNow;
        var args = new TransactionChangedEventArgs(
            TransactionId: Guid.NewGuid(),
            BorrowerId: Guid.NewGuid(),
            MutationKind: TransactionMutationKind.Created,
            Timestamp: now);

        Assert.NotNull(args.TransactionId);
        Assert.NotNull(args.BorrowerId);
        Assert.Equal(TransactionMutationKind.Created, args.MutationKind);
        Assert.Equal(now, args.Timestamp);
    }

    [Theory]
    [InlineData(TransactionMutationKind.Created)]
    [InlineData(TransactionMutationKind.Updated)]
    [InlineData(TransactionMutationKind.Deleted)]
    [InlineData(TransactionMutationKind.Reversed)]
    [InlineData(TransactionMutationKind.Adjusted)]
    public void TransactionMutationKind_SupportsAllMutationTypes(TransactionMutationKind kind)
    {
        var args = new TransactionChangedEventArgs(MutationKind: kind);
        Assert.Equal(kind, args.MutationKind);
    }
}
