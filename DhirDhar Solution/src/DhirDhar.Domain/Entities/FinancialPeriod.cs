using DhirDhar.Domain.Common;
using DhirDhar.Domain.Enums;

namespace DhirDhar.Domain.Entities;

/// <summary>
/// An accounting window used to bucket transactions and reports.
/// A period is Open while entries are allowed and Closed once it becomes immutable.
/// </summary>
public sealed class FinancialPeriod : AuditableEntity
{
    private FinancialPeriod()
    {
    }

    public FinancialPeriod(string name, DateTime startDate, DateTime endDate)
        : base(Guid.NewGuid())
    {
        var errors = new ValidationErrorCollector();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(nameof(Name), "Name is required.");
        }
        else if (name.Trim().Length > 50)
        {
            errors.Add(nameof(Name), "Name must be 50 characters or fewer.");
        }

        if (endDate < startDate)
        {
            errors.Add(nameof(EndDate), "End date cannot be before the start date.");
        }

        errors.ThrowIfInvalid();

        Name = name.Trim();
        StartDate = startDate;
        EndDate = endDate;
        Status = FinancialPeriodStatus.Open;
    }

    public string Name { get; private set; } = string.Empty;

    public DateTime StartDate { get; private set; }

    public DateTime EndDate { get; private set; }

    public FinancialPeriodStatus Status { get; private set; }

    public bool Contains(DateTime date)
    {
        return date >= StartDate && date <= EndDate;
    }

    public void Close()
    {
        if (Status != FinancialPeriodStatus.Open)
        {
            throw new DomainException("Only an open period can be closed.");
        }

        Status = FinancialPeriodStatus.Closed;
        Touch();
    }

    public void Reopen()
    {
        if (Status != FinancialPeriodStatus.Closed)
        {
            throw new DomainException("Only a closed period can be reopened.");
        }

        Status = FinancialPeriodStatus.Open;
        Touch();
    }
}
