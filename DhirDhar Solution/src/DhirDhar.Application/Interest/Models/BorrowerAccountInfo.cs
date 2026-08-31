namespace DhirDhar.Application.Interest.Models;

public sealed record BorrowerAccountInfo(
    Guid BorrowerId,
    Domain.Enums.BorrowerStatus Status,
    decimal OpeningPrincipal,
    DateTime StartDate,
    decimal MonthlyInterestRate,
    DateTime? ClosedDate);
