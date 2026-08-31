using System;
using DhirDhar.Domain.Common;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;

namespace DhirDhar.Domain.Entities;

/// <summary>
/// A single money movement (deposit or withdrawal) recorded against a financial period.
/// The amount is always a positive magnitude; the <see cref="Type"/> carries the direction.
/// </summary>
public sealed class Transaction : AuditableEntity
{
    private Transaction()
    {
    }

    public Transaction(Guid financialPeriodId, Money amount, TransactionType type, DateTime occurredOn, string? description)
        : this(null, financialPeriodId, amount, type, occurredOn, description, null)
    {
    }

    public Transaction(Guid? borrowerId, Guid financialPeriodId, Money amount, TransactionType type, DateTime occurredOn, string? description, string? reference = null)
        : base(Guid.NewGuid())
    {
        var errors = new ValidationErrorCollector();

        if (amount.Amount <= 0m)
        {
            errors.Add(nameof(Amount), "Amount must be positive.");
        }

        errors.ThrowIfInvalid();

        EnumValidator.EnsureDefined(type, nameof(Type));

        BorrowerId = (borrowerId.HasValue && borrowerId.Value != Guid.Empty) ? borrowerId.Value : null;
        FinancialPeriodId = financialPeriodId;
        Amount = amount;
        Type = type;
        OccurredOn = occurredOn;
        Description = description;
        Reference = string.IsNullOrWhiteSpace(reference) ? $"TXN-{Id.ToString()[..8].ToUpperInvariant()}" : reference.Trim();
    }

    public Guid? BorrowerId { get; private set; }

    public Guid FinancialPeriodId { get; private set; }

    public string Reference { get; private set; } = string.Empty;

    public Money Amount { get; private set; } = Money.Zero;

    public TransactionType Type { get; private set; }

    public DateTime OccurredOn { get; private set; }

    public DateTime TransactionDate => OccurredOn;

    public string? Description { get; private set; }

    public Borrower? Borrower { get; set; }

    public void SetBorrowerId(Guid? borrowerId)
    {
        BorrowerId = (borrowerId.HasValue && borrowerId.Value != Guid.Empty) ? borrowerId.Value : null;
    }

    public void UpdateReference(string reference)
    {
        if (!string.IsNullOrWhiteSpace(reference))
        {
            Reference = reference.Trim();
        }
    }
}
