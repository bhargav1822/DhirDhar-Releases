using System;
using System.Collections.Generic;
using DhirDhar.Domain.Common;
using DhirDhar.Domain.Enums;

namespace DhirDhar.Domain.Entities;

/// <summary>
/// A person who borrows money. Carries the borrower lifecycle
/// (active/inactive/archived/closed) and basic contact details.
/// </summary>
public sealed class Borrower : AuditableEntity
{
    private Borrower()
    {
    }

    public Borrower(string name, string? phone, string? address, string? notes)
        : this($"B-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}", name, null, null, null, phone, address, notes, null, null)
    {
    }

    public Borrower(string borrowerNumber, string name, string? phone, string? address, string? notes, DateTime? entryDate = null)
        : this(Guid.NewGuid(), borrowerNumber, name, null, null, null, phone, address, notes, null, BorrowerStatus.Active, entryDate ?? DateTime.UtcNow)
    {
    }

    public Borrower(
        string borrowerNumber,
        string name,
        string? fatherName,
        string? surname,
        string? village,
        string? phone,
        string? address,
        string? notes,
        string? aadharNumber,
        DateTime? entryDate = null)
        : this(Guid.NewGuid(), borrowerNumber, name, fatherName, surname, village, phone, address, notes, aadharNumber, BorrowerStatus.Active, entryDate ?? DateTime.UtcNow)
    {
    }

    private Borrower(
        Guid id,
        string borrowerNumber,
        string name,
        string? fatherName,
        string? surname,
        string? village,
        string? phone,
        string? address,
        string? notes,
        string? aadharNumber,
        BorrowerStatus status,
        DateTime entryDate,
        decimal? loanAmount = null,
        DateTime? loanDate = null,
        decimal? interestRate = null)
        : base(id)
    {
        BorrowerNumber = string.IsNullOrWhiteSpace(borrowerNumber) ? $"B-{id.ToString()[..8].ToUpperInvariant()}" : borrowerNumber.Trim();
        SetName(name);
        FatherName = fatherName;
        Surname = surname;
        Village = village;
        Phone = phone;
        Address = address;
        Notes = notes;
        AadharNumber = aadharNumber;
        Status = status;
        if (loanDate.HasValue && loanDate.Value.Date < entryDate.Date)
        {
            entryDate = loanDate.Value.Date;
        }
        EntryDate = entryDate;
        LoanAmount = loanAmount;
        LoanDate = loanDate;
        InterestRate = interestRate;
    }

    public string BorrowerNumber { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? FatherName { get; private set; }

    public string? Surname { get; private set; }

    public string? Village { get; private set; }

    public string? Phone { get; private set; }

    public string? Contact
    {
        get => Phone;
        private set => Phone = value;
    }

    public string? Address { get; private set; }

    public string? Notes { get; private set; }

    public string? AadharNumber { get; private set; }

    public string? BorrowerPhotoPath { get; private set; }

    public string? OrnamentPhotoPath { get; private set; }

    public string? LoanType { get; private set; }

    public string? OrnamentType { get; private set; }

    public decimal? OrnamentWeight { get; private set; }

    public decimal? LoanAmount { get; private set; }

    public DateTime? LoanDate { get; private set; }

    public decimal? InterestRate { get; private set; }

    public BorrowerStatus Status { get; private set; }

    public DateTime EntryDate { get; private set; }

    public DateTime? ClosedDate { get; private set; }

    public decimal? ClosingAmount { get; private set; }

    public decimal? ClosedAccruedInterest { get; private set; }

    public ICollection<Loan> Loans { get; private set; } = new List<Loan>();

    public ICollection<Transaction> Transactions { get; private set; } = new List<Transaction>();

    public void SetBorrowerNumber(string borrowerNumber)
    {
        if (string.IsNullOrWhiteSpace(borrowerNumber))
        {
            throw new ArgumentException("Borrower number cannot be empty.", nameof(borrowerNumber));
        }
        BorrowerNumber = borrowerNumber.Trim();
        Touch();
    }

    public void SetPhotosAndLoanType(string? borrowerPhotoPath, string? ornamentPhotoPath, string? loanType, string? ornamentType = null, decimal? ornamentWeight = null, decimal? loanAmount = null, DateTime? loanDate = null, decimal? interestRate = null)
    {
        BorrowerPhotoPath = borrowerPhotoPath;
        OrnamentPhotoPath = ornamentPhotoPath;
        LoanType = loanType;
        OrnamentType = ornamentType;
        OrnamentWeight = ornamentWeight;
        LoanAmount = loanAmount;
        LoanDate = loanDate;
        InterestRate = interestRate;
        if (loanDate.HasValue && loanDate.Value.Date < EntryDate.Date)
        {
            EntryDate = loanDate.Value.Date;
        }
        Touch();
    }

    public void SetEntryDate(DateTime entryDate)
    {
        EntryDate = entryDate;
        Touch();
    }

    public void UpdateDetails(
        string name,
        string? fatherName,
        string? surname,
        string? village,
        string? phone,
        string? address,
        string? notes,
        string? aadharNumber)
    {
        SetName(name);
        FatherName = fatherName;
        Surname = surname;
        Village = village;
        Phone = phone;
        Address = address;
        Notes = notes;
        AadharNumber = aadharNumber;
        Touch();
    }

    public void Activate()
    {
        SetStatus(BorrowerStatus.Active);
        ClosedDate = null;
        ClosingAmount = null;
        ClosedAccruedInterest = null;
    }

    public void Deactivate()
    {
        SetStatus(BorrowerStatus.Inactive);
    }

    public void Archive()
    {
        SetStatus(BorrowerStatus.Archived);
    }

    public void CloseAccount(DateTime closedDate, decimal? closingAmount = null, decimal? closedAccruedInterest = null)
    {
        Status = BorrowerStatus.Closed;
        ClosedDate = closedDate;
        ClosingAmount = closingAmount;
        ClosedAccruedInterest = closedAccruedInterest;
        Touch();
    }

    public void ReopenAccount()
    {
        SetStatus(BorrowerStatus.Active);
        ClosedDate = null;
        ClosingAmount = null;
        ClosedAccruedInterest = null;
    }

    private void SetName(string name)
    {
        var errors = new ValidationErrorCollector();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(nameof(Name), "Name is required.");
        }
        else if (name.Trim().Length > 100)
        {
            errors.Add(nameof(Name), "Name must be 100 characters or fewer.");
        }

        errors.ThrowIfInvalid();

        Name = name.Trim();
    }

    private void SetStatus(BorrowerStatus status)
    {
        EnumValidator.EnsureDefined(status, nameof(Status));
        Status = status;
        Touch();
    }
}
