using DhirDhar.Domain.Common;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;

namespace DhirDhar.Domain.Entities;

/// <summary>
/// A loan issued to a borrower. Captures the principal, interest configuration,
/// and lifecycle state (not yet repaid / fully repaid).
/// </summary>
public sealed class Loan : AuditableEntity
{
    private Loan()
    {
    }

    public Loan(
        Guid borrowerId,
        Money principal,
        decimal interestRatePercent,
        InterestFrequency interestFrequency,
        DateTime issueDate)
        : this(Guid.NewGuid(), borrowerId, principal, interestRatePercent, interestFrequency, issueDate, false)
    {
    }

    private Loan(
        Guid id,
        Guid borrowerId,
        Money principal,
        decimal interestRatePercent,
        InterestFrequency interestFrequency,
        DateTime issueDate,
        bool isRepaid)
        : base(id)
    {
        var errors = new ValidationErrorCollector();

        if (borrowerId == Guid.Empty)
        {
            errors.Add(nameof(BorrowerId), "Borrower is required.");
        }

        if (principal.Amount <= 0m)
        {
            errors.Add(nameof(Principal), "Principal must be positive.");
        }

        if (interestRatePercent < 0m)
        {
            errors.Add(nameof(InterestRatePercent), "Interest rate cannot be negative.");
        }

        if (interestRatePercent > 100m)
        {
            errors.Add(nameof(InterestRatePercent), "Interest rate cannot exceed 100 percent.");
        }

        errors.ThrowIfInvalid();

        EnumValidator.EnsureDefined(interestFrequency, nameof(InterestFrequency));

        BorrowerId = borrowerId;
        Principal = principal;
        InterestRatePercent = interestRatePercent;
        InterestFrequency = interestFrequency;
        IssueDate = issueDate;
        IsRepaid = isRepaid;
    }

    public Guid BorrowerId { get; private set; }

    public Borrower? Borrower { get; set; }

    public Money Principal { get; private set; } = Money.Zero;

    public decimal InterestRatePercent { get; private set; }

    public InterestFrequency InterestFrequency { get; private set; }

    public DateTime IssueDate { get; private set; }

    public bool IsRepaid { get; private set; }

    public void MarkRepaid()
    {
        if (IsRepaid)
        {
            throw new DomainException("Loan is already marked as repaid.");
        }

        IsRepaid = true;
        Touch();
    }

    public void MarkNotRepaid()
    {
        if (!IsRepaid)
        {
            throw new DomainException("Loan is already not repaid.");
        }

        IsRepaid = false;
        Touch();
    }
}
