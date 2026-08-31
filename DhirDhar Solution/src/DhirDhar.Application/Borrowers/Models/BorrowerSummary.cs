using System;

namespace DhirDhar.Application.Borrowers.Models;

public sealed record BorrowerSummary(
    Guid Id,
    string BorrowerNumber,
    string Name,
    string? Contact,
    string Status,
    DateTime EntryDate,
    decimal TotalDeposits,
    decimal TotalWithdrawals,
    decimal OutstandingAmount,
    DateTime? LastTransactionDate,
    string? FatherName = null,
    string? Surname = null,
    string? Village = null,
    string? AadharNumber = null,
    string? BorrowerPhotoPath = null,
    string? OrnamentPhotoPath = null,
    string? LoanType = null,
    string? OrnamentType = null,
    decimal? OrnamentWeight = null,
    decimal? LoanAmount = null,
    DateTime? LoanDate = null,
    decimal? InterestRate = null,
    DateTime? ClosedDate = null,
    decimal? ClosingAmount = null,
    decimal? ClosedAccruedInterest = null)
{
    public string FullName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FatherName) && string.IsNullOrWhiteSpace(Surname))
            {
                return Name?.Trim() ?? string.Empty;
            }

            var trimmedName = Name?.Trim() ?? string.Empty;
            var trimmedFather = FatherName?.Trim() ?? string.Empty;
            var trimmedSurname = Surname?.Trim() ?? string.Empty;

            if (!string.IsNullOrEmpty(trimmedFather) && trimmedName.Contains(trimmedFather, StringComparison.OrdinalIgnoreCase))
            {
                return trimmedName;
            }

            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(trimmedName)) parts.Add(trimmedName);
            if (!string.IsNullOrWhiteSpace(trimmedFather)) parts.Add(trimmedFather);
            if (!string.IsNullOrWhiteSpace(trimmedSurname)) parts.Add(trimmedSurname);
            return parts.Count > 0 ? string.Join(" ", parts) : trimmedName;
        }
    }

    public string FormattedBorrowerNumber => string.IsNullOrWhiteSpace(BorrowerNumber)
        ? string.Empty
        : (BorrowerNumber.Trim().StartsWith("#") ? BorrowerNumber.Trim() : $"#{BorrowerNumber.Trim()}");
}
