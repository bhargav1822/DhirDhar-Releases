using System;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers;
using DhirDhar.Application.Localization;
using DhirDhar.Application.Transactions;
using DhirDhar.Application.Transactions.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.DependencyInjection;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class TransactionAmountValidationTests
{
    private static ServiceProvider BuildProvider(DatabaseOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(options);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CreateTransaction_ExactScenario_vinchandraPanchal_25000Withdrawal_SavesSuccessfully()
    {
        using var temp = new Persistence.TempDatabase();
        var provider = BuildProvider(temp.CreateDatabaseOptions());
        var context = provider.GetRequiredService<DhirDharDbContext>();
        await context.Database.EnsureCreatedAsync();

        var transactionService = provider.GetRequiredService<ITransactionService>();

        // Setup financial period
        var period = new FinancialPeriod("2026-2027", new DateTime(2026, 4, 1), new DateTime(2027, 3, 31));
        context.FinancialPeriods.Add(period);

        // Setup borrower vinchandra Panchal (#B20260815100850)
        var borrower = new Borrower(
            "B20260815100850",
            "vinchandra Panchal",
            "Panchal",
            "Panchal",
            "Village",
            "9876543210",
            "Address",
            "",
            "123456789012",
            new DateTime(2026, 8, 15));
        context.Borrowers.Add(borrower);
        await context.SaveChangesAsync();

        // 1. Test Amount input parsing: "25000" -> 25000.00m
        string inputAmountText = "25000";
        Assert.True(MonetaryAmountParser.TryParse(inputAmountText, out var parsedAmount));
        Assert.Equal(25000.00m, parsedAmount);

        // 2. Exact scenario:
        // Borrower: vinchandra Panchal (#B20260815100850)
        // Type: Withdrawal
        // Date: 18-Aug-2026
        // Amount: 25000
        // Notes: blank
        var txnDate = new DateTime(2026, 8, 18);
        var request = new CreateTransactionRequest(
            borrower.Id,
            TransactionType.Withdrawal,
            parsedAmount,
            txnDate,
            null);

        var result = await transactionService.CreateAsync(request);

        Assert.NotNull(result);
        Assert.Equal(borrower.Id, result.BorrowerId);
        Assert.Equal(25000.00m, result.Amount);
        Assert.Equal("Withdrawal", result.TransactionType);
        Assert.Equal(txnDate, result.TransactionDate);

        // Verify in database
        var savedTxn = await context.Transactions.FirstOrDefaultAsync(t => t.Id == result.Id);
        Assert.NotNull(savedTxn);
        Assert.Equal(25000.00m, savedTxn.Amount.Amount);
        Assert.Equal(TransactionType.Withdrawal, savedTxn.Type);
    }

    [Theory]
    [InlineData("1", 1.00)]
    [InlineData("100", 100.00)]
    [InlineData("25000.50", 25000.50)]
    [InlineData("100000", 100000.00)]
    [InlineData("₹1", 1.00)]
    [InlineData("₹100", 100.00)]
    [InlineData("₹25,000.50", 25000.50)]
    [InlineData("₹100,000", 100000.00)]
    public async Task CreateTransaction_VariousAmounts_AllSaveCorrectly(string amountInput, decimal expectedAmount)
    {
        using var temp = new Persistence.TempDatabase();
        var provider = BuildProvider(temp.CreateDatabaseOptions());
        var context = provider.GetRequiredService<DhirDharDbContext>();
        await context.Database.EnsureCreatedAsync();

        var transactionService = provider.GetRequiredService<ITransactionService>();

        var period = new FinancialPeriod("2026-2027", new DateTime(2026, 4, 1), new DateTime(2027, 3, 31));
        context.FinancialPeriods.Add(period);

        var borrower = new Borrower(
            "B20260815100850",
            "vinchandra Panchal",
            "Panchal",
            "Panchal",
            "Village",
            "9876543210",
            "Address",
            "",
            "123456789012",
            new DateTime(2026, 8, 15));
        context.Borrowers.Add(borrower);
        await context.SaveChangesAsync();

        Assert.True(MonetaryAmountParser.TryParse(amountInput, out var parsedDecimal));
        Assert.Equal(expectedAmount, parsedDecimal);

        var request = new CreateTransactionRequest(
            borrower.Id,
            TransactionType.Withdrawal,
            parsedDecimal,
            new DateTime(2026, 8, 18),
            "Test withdrawal amount");

        var result = await transactionService.CreateAsync(request);
        Assert.NotNull(result);
        Assert.Equal(expectedAmount, result.Amount);

        var dbTxn = await context.Transactions.FirstOrDefaultAsync(t => t.Id == result.Id);
        Assert.NotNull(dbTxn);
        Assert.Equal(expectedAmount, dbTxn.Amount.Amount);
    }
}
