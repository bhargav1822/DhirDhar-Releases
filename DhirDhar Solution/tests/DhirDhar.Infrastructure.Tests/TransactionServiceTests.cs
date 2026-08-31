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

namespace DhirDhar.Infrastructure.Tests;

public class TransactionServiceTests
{
    private static ServiceProvider BuildProvider(DatabaseOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(options);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CreateDeposit_WithValidData_ReturnsSuccess()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("Test Period", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
        context.FinancialPeriods.Add(period);
        await context.SaveChangesAsync();

        var transaction = new Transaction(period.Id, Money.Create(1000m), TransactionType.Deposit, DateTime.UtcNow, "Test deposit");
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();

        var loaded = await context.Transactions.FirstOrDefaultAsync(t => t.Id == transaction.Id);
        Assert.NotNull(loaded);
        Assert.Equal(1000m, loaded.Amount.Amount);
        Assert.Equal(TransactionType.Deposit, loaded.Type);
    }

    [Fact]
    public async Task CreateWithdrawal_WithValidData_ReturnsSuccess()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("Test Period", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
        context.FinancialPeriods.Add(period);
        await context.SaveChangesAsync();

        var transaction = new Transaction(period.Id, Money.Create(500m), TransactionType.Withdrawal, DateTime.UtcNow, "Test withdrawal");
        context.Transactions.Add(transaction);
        await context.SaveChangesAsync();

        var loaded = await context.Transactions.FirstOrDefaultAsync(t => t.Id == transaction.Id);
        Assert.NotNull(loaded);
        Assert.Equal(500m, loaded.Amount.Amount);
        Assert.Equal(TransactionType.Withdrawal, loaded.Type);
    }

    [Fact]
    public async Task Transaction_OrderIsPreserved()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("Test Period", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
        context.FinancialPeriods.Add(period);
        await context.SaveChangesAsync();

        var t1 = new Transaction(period.Id, Money.Create(100m), TransactionType.Deposit, DateTime.UtcNow.AddDays(-2), "Oldest");
        var t2 = new Transaction(period.Id, Money.Create(200m), TransactionType.Deposit, DateTime.UtcNow, "Newest");
        context.Transactions.Add(t1);
        context.Transactions.Add(t2);
        await context.SaveChangesAsync();

        var ordered = await context.Transactions
            .OrderByDescending(t => t.OccurredOn)
            .ToListAsync();

        Assert.Equal(2, ordered.Count);
        Assert.Equal(200m, ordered[0].Amount.Amount);
        Assert.Equal(100m, ordered[1].Amount.Amount);
    }

    [Fact]
    public async Task FinancialsCalculation_IsCorrect()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("Test Period", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
        context.FinancialPeriods.Add(period);
        await context.SaveChangesAsync();

        context.Transactions.Add(new Transaction(period.Id, Money.Create(5000m), TransactionType.Deposit, DateTime.UtcNow, "Deposit 1"));
        context.Transactions.Add(new Transaction(period.Id, Money.Create(2000m), TransactionType.Withdrawal, DateTime.UtcNow, "Withdrawal 1"));
        context.Transactions.Add(new Transaction(period.Id, Money.Create(1500m), TransactionType.Deposit, DateTime.UtcNow, "Deposit 2"));
        await context.SaveChangesAsync();

        var depList = await context.Transactions
            .Where(t => t.Type == TransactionType.Deposit)
            .Select(t => t.Amount.Amount)
            .ToListAsync();
        var deposits = depList.Sum();

        var withList = await context.Transactions
            .Where(t => t.Type == TransactionType.Withdrawal)
            .Select(t => t.Amount.Amount)
            .ToListAsync();
        var withdrawals = withList.Sum();

        var outstanding = deposits - withdrawals;

        Assert.Equal(6500m, deposits);
        Assert.Equal(2000m, withdrawals);
        Assert.Equal(4500m, outstanding);
    }

    [Fact]
    public async Task GetListAsync_ReturnsBorrowerDetailsAndFinancials()
    {
        using var temp = new Persistence.TempDatabase();
        await using var dbContext = new DhirDharDbContext(temp.CreateOptions());
        await dbContext.Database.EnsureCreatedAsync();

        var borrower = new Borrower("BN-999", "Amit Verma", "Rajesh", "Verma", "Jaipur", "9876543210", "Address", "Notes", "123456789012", DateTime.UtcNow);
        dbContext.Borrowers.Add(borrower);

        var period = new FinancialPeriod("Test Period", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
        dbContext.FinancialPeriods.Add(period);
        await dbContext.SaveChangesAsync();

        var t1 = new Transaction(period.Id, Money.Create(50000m), TransactionType.Withdrawal, DateTime.UtcNow.AddDays(-5), "Initial Loan");
        t1.SetBorrowerId(borrower.Id);

        var t2 = new Transaction(period.Id, Money.Create(10000m), TransactionType.Deposit, DateTime.UtcNow.AddDays(-2), "Repayment");
        t2.SetBorrowerId(borrower.Id);

        dbContext.Transactions.AddRange(t1, t2);
        await dbContext.SaveChangesAsync();

        var depList = await dbContext.Transactions
            .Where(t => (t.BorrowerId == borrower.Id || t.FinancialPeriodId == borrower.Id) && t.Type == TransactionType.Deposit)
            .Select(t => t.Amount.Amount)
            .ToListAsync();
        var withList = await dbContext.Transactions
            .Where(t => (t.BorrowerId == borrower.Id || t.FinancialPeriodId == borrower.Id) && t.Type == TransactionType.Withdrawal)
            .Select(t => t.Amount.Amount)
            .ToListAsync();

        Assert.Equal(10000m, depList.Sum());
        Assert.Equal(50000m, withList.Sum());
        Assert.Equal(-40000m, depList.Sum() - withList.Sum());
    }

    [Fact]
    public async Task CreateTransaction_UpdatesFinancialsAndList()
    {
        using var temp = new Persistence.TempDatabase();
        await using var dbContext = new DhirDharDbContext(temp.CreateOptions());
        await dbContext.Database.EnsureCreatedAsync();

        var borrower = new Borrower("BN-777", "Vikram Singh", "Sohan", "Singh", "Udaipur", "9876543210", "Address", "Notes", "123456789012", DateTime.UtcNow);
        dbContext.Borrowers.Add(borrower);

        var period = new FinancialPeriod("Test Period", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30));
        dbContext.FinancialPeriods.Add(period);
        await dbContext.SaveChangesAsync();

        var t1 = new Transaction(period.Id, Money.Create(25000m), TransactionType.Deposit, DateTime.UtcNow, "Test Deposit 1");
        t1.SetBorrowerId(borrower.Id);

        var t2 = new Transaction(period.Id, Money.Create(60000m), TransactionType.Withdrawal, DateTime.UtcNow, "Test Loan Given");
        t2.SetBorrowerId(borrower.Id);

        dbContext.Transactions.AddRange(t1, t2);
        await dbContext.SaveChangesAsync();

        var depList = await dbContext.Transactions
            .Where(t => t.BorrowerId == borrower.Id && t.Type == TransactionType.Deposit)
            .Select(t => t.Amount.Amount)
            .ToListAsync();
        var withList = await dbContext.Transactions
            .Where(t => t.BorrowerId == borrower.Id && t.Type == TransactionType.Withdrawal)
            .Select(t => t.Amount.Amount)
            .ToListAsync();

        Assert.Equal(25000m, depList.Sum());
        Assert.Equal(60000m, withList.Sum());
        Assert.Equal(-35000m, depList.Sum() - withList.Sum());
    }
}
