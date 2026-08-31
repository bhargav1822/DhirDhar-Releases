using System;
using System.Threading.Tasks;
using DhirDhar.Application.Validation;
using DhirDhar.Application.Validation.Models;
using DhirDhar.Infrastructure.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Application.Tests;

public class Phase29ValidationTests
{
    [Fact]
    public void ValidateAmount_ValidatesLimitsAndPrecision()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyService>(new DhirDhar.Infrastructure.Validation.IdempotencyService());
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var validationService = new FinancialValidationService(
            scopeFactory,
            sp.GetRequiredService<IIdempotencyService>(),
            NullLogger<FinancialValidationService>.Instance);

        // Valid amounts
        var valid1 = validationService.ValidateAmount(100.50m);
        Assert.True(valid1.IsValid);

        // Invalid precision (> 2 decimal places)
        var invalidPrecision = validationService.ValidateAmount(100.555m);
        Assert.False(invalidPrecision.IsValid);
        Assert.Contains(invalidPrecision.Errors, e => e.Contains("decimal places"));

        // Invalid negative amount when positive required
        var invalidNegative = validationService.ValidateAmount(-50m, requirePositive: true);
        Assert.False(invalidNegative.IsValid);
        Assert.Contains(invalidNegative.Errors, e => e.Contains("greater than zero"));
    }

    [Fact]
    public void ValidateDates_ValidatesEntryAndTransactionAndClosedDates()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyService>(new DhirDhar.Infrastructure.Validation.IdempotencyService());
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var validationService = new FinancialValidationService(
            scopeFactory,
            sp.GetRequiredService<IIdempotencyService>(),
            NullLogger<FinancialValidationService>.Instance);

        var entryDate = new DateTime(2026, 1, 1);
        var txnDateValid = new DateTime(2026, 1, 15);
        var closedDate = new DateTime(2026, 2, 1);

        var valid = validationService.ValidateDates(entryDate, txnDateValid, closedDate);
        Assert.True(valid.IsValid);

        var postClosureTxn = new DateTime(2026, 2, 15);
        var invalidPostClosure = validationService.ValidateDates(entryDate, postClosureTxn, closedDate);
        Assert.False(invalidPostClosure.IsValid);
        Assert.Contains(invalidPostClosure.Errors, e => e.Contains("closed date"));
    }

    [Fact]
    public void IdempotencyService_PreventsConcurrentOrDuplicateSubmission()
    {
        IIdempotencyService service = new DhirDhar.Infrastructure.Validation.IdempotencyService();
        var key = "tx:test-key-123";

        Assert.True(service.TryAcquireLock(key));
        Assert.True(service.IsDuplicateSubmission(key));

        // Re-acquire fails while locked
        Assert.False(service.TryAcquireLock(key));

        service.ReleaseLock(key);
        Assert.False(service.IsDuplicateSubmission(key));
    }
}
