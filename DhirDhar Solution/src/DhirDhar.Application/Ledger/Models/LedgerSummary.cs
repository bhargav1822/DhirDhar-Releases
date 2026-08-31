namespace DhirDhar.Application.Ledger.Models;

public sealed record LedgerSummary(
    Guid BorrowerId,
    string BorrowerName,
    decimal OpeningBalance,
    decimal TotalDeposits,
    decimal TotalWithdrawals,
    decimal TotalInterest,
    decimal CurrentOutstanding,
    string AccountStatus,
    DateTime? ClosedDate);
