using System;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Validation.Models;

namespace DhirDhar.Application.Validation;

public interface IFinancialValidationService
{
    FinancialValidationResult ValidateAmount(decimal amount, bool requirePositive = true, string fieldName = "Amount");

    FinancialValidationResult ValidatePrincipal(decimal principal);

    FinancialValidationResult ValidateDates(DateTime entryDate, DateTime transactionDate, DateTime? closedDate);

    Task<FinancialValidationResult> ValidateDepositAsync(
        Guid borrowerId,
        decimal amount,
        DateTime transactionDate,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<FinancialValidationResult> ValidateWithdrawalAsync(
        Guid borrowerId,
        decimal amount,
        DateTime transactionDate,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<FinancialValidationResult> ValidateAccountClosureAsync(
        Guid borrowerId,
        DateTime closedDate,
        CancellationToken cancellationToken = default);
}
