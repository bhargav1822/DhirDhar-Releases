namespace DhirDhar.Application.Transactions.Models;

public sealed record TransactionFinancials(
    decimal TotalDeposits,
    decimal TotalWithdrawals,
    decimal Outstanding,
    int TransactionCount);
