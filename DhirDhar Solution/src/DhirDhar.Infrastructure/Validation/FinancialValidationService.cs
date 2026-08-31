using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DhirDhar.Application.Validation;
using DhirDhar.Application.Validation.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Validation;

public sealed class FinancialValidationService : IFinancialValidationService
{
    private const decimal MaxSupportedAmount = 999_999_999_999.99m;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIdempotencyService _idempotencyService;
    private readonly ILogger<FinancialValidationService> _logger;

    public FinancialValidationService(
        IServiceScopeFactory scopeFactory,
        IIdempotencyService idempotencyService,
        ILogger<FinancialValidationService> logger)
    {
        _scopeFactory = scopeFactory;
        _idempotencyService = idempotencyService;
        _logger = logger;
    }

    public FinancialValidationResult ValidateAmount(decimal amount, bool requirePositive = true, string fieldName = "Amount")
    {
        var errors = new List<string>();

        if (requirePositive && amount <= 0m)
        {
            errors.Add($"{fieldName} must be greater than zero.");
        }
        else if (!requirePositive && amount < 0m)
        {
            errors.Add($"{fieldName} cannot be negative.");
        }

        if (Math.Abs(amount) > MaxSupportedAmount)
        {
            errors.Add($"{fieldName} exceeds maximum supported limit of {MaxSupportedAmount:N2}.");
        }

        if (decimal.Round(amount, 2, MidpointRounding.AwayFromZero) != amount)
        {
            errors.Add($"{fieldName} precision cannot exceed 2 decimal places.");
        }

        return errors.Count == 0
            ? FinancialValidationResult.Success()
            : FinancialValidationResult.Failure(errors);
    }

    public FinancialValidationResult ValidatePrincipal(decimal principal)
    {
        return ValidateAmount(principal, requirePositive: true, fieldName: "Principal");
    }

    public FinancialValidationResult ValidateDates(DateTime entryDate, DateTime transactionDate, DateTime? closedDate)
    {
        var errors = new List<string>();

        if (entryDate == default)
        {
            errors.Add("Entry date is required.");
        }

        if (transactionDate == default)
        {
            errors.Add("Transaction date is required.");
        }

        if (transactionDate.Date < entryDate.Date)
        {
            errors.Add($"Transaction date ({transactionDate:yyyy-MM-dd}) cannot be earlier than borrower entry date ({entryDate:yyyy-MM-dd}).");
        }

        if (closedDate.HasValue)
        {
            if (closedDate.Value.Date < entryDate.Date)
            {
                errors.Add($"Account closed date ({closedDate.Value:yyyy-MM-dd}) cannot be earlier than entry date ({entryDate:yyyy-MM-dd}).");
            }

            if (transactionDate.Date > closedDate.Value.Date)
            {
                errors.Add($"Transaction date ({transactionDate:yyyy-MM-dd}) cannot be after account closed date ({closedDate.Value:yyyy-MM-dd}).");
            }
        }

        return errors.Count == 0
            ? FinancialValidationResult.Success()
            : FinancialValidationResult.Failure(errors);
    }

    public async Task<FinancialValidationResult> ValidateDepositAsync(
        Guid borrowerId,
        decimal amount,
        DateTime transactionDate,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var amountValidation = ValidateAmount(amount, requirePositive: true, fieldName: "Deposit amount");
        if (!amountValidation.IsValid)
        {
            return amountValidation;
        }

        if (!string.IsNullOrWhiteSpace(idempotencyKey) && _idempotencyService.IsDuplicateSubmission(idempotencyKey))
        {
            return FinancialValidationResult.Failure("Duplicate transaction detected.");
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

        var borrower = await dbContext.Borrowers
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == borrowerId, cancellationToken)
            .ConfigureAwait(false);

        if (borrower is null)
        {
            return FinancialValidationResult.Failure($"Borrower with ID '{borrowerId}' was not found.");
        }

        if (borrower.Status == BorrowerStatus.Closed || borrower.Status == BorrowerStatus.Archived)
        {
            return FinancialValidationResult.Failure("Borrower account is closed.");
        }

        return ValidateDates(borrower.LoanDate ?? borrower.EntryDate, transactionDate, borrower.ClosedDate);
    }

    public async Task<FinancialValidationResult> ValidateWithdrawalAsync(
        Guid borrowerId,
        decimal amount,
        DateTime transactionDate,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var amountValidation = ValidateAmount(amount, requirePositive: true, fieldName: "Withdrawal amount");
        if (!amountValidation.IsValid)
        {
            return amountValidation;
        }

        if (!string.IsNullOrWhiteSpace(idempotencyKey) && _idempotencyService.IsDuplicateSubmission(idempotencyKey))
        {
            return FinancialValidationResult.Failure("Duplicate transaction detected.");
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

        var borrower = await dbContext.Borrowers
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == borrowerId, cancellationToken)
            .ConfigureAwait(false);

        if (borrower is null)
        {
            return FinancialValidationResult.Failure($"Borrower with ID '{borrowerId}' was not found.");
        }

        if (borrower.Status == BorrowerStatus.Closed || borrower.Status == BorrowerStatus.Archived)
        {
            return FinancialValidationResult.Failure("Borrower account is closed.");
        }

        return ValidateDates(borrower.LoanDate ?? borrower.EntryDate, transactionDate, borrower.ClosedDate);
    }

    public async Task<FinancialValidationResult> ValidateAccountClosureAsync(
        Guid borrowerId,
        DateTime closedDate,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DhirDharDbContext>();

        var borrower = await dbContext.Borrowers
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == borrowerId, cancellationToken)
            .ConfigureAwait(false);

        if (borrower is null)
        {
            return FinancialValidationResult.Failure($"Borrower with ID '{borrowerId}' was not found.");
        }

        var loanStartDate = borrower.LoanDate ?? borrower.EntryDate;

        if (closedDate < loanStartDate.Date)
        {
            return FinancialValidationResult.Failure($"Closed date ({closedDate:yyyy-MM-dd}) cannot precede loan start date ({loanStartDate:yyyy-MM-dd}).");
        }

        return FinancialValidationResult.Success();
    }
}
