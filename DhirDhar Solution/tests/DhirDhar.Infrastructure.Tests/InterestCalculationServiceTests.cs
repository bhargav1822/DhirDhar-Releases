using DhirDhar.Application.Interest;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.DependencyInjection;
using DhirDhar.Infrastructure.Interest;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DhirDhar.Infrastructure.Tests;

public class InterestCalculationServiceTests
{
    [Fact]
    public async Task CalculateAsync_UsesFirstLoanTransactionDate_AndThreePercentMonthlyRate()
    {
        using var temp = new Persistence.TempDatabase();
        await using var dbContext = new DhirDharDbContext(temp.CreateOptions());
        await dbContext.Database.EnsureCreatedAsync();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddSingleton(dbContext);
        services.AddScoped<IInterestCalculationService, InterestCalculationService>();
        using var provider = services.BuildServiceProvider();

        var loanDate = new DateTime(2023, 8, 12);
        var borrower = new Borrower("BN-333", "Ramesh Chand", "Sohan", "Chand", "Jaipur", "9876543210", "Address", "Notes", "123456789012", loanDate);
        dbContext.Borrowers.Add(borrower);
        await dbContext.SaveChangesAsync();

        var period = new FinancialPeriod("Test Period", loanDate.AddDays(-30), DateTime.UtcNow.AddDays(30));
        dbContext.FinancialPeriods.Add(period);
        await dbContext.SaveChangesAsync();

        var loanTxn = new Transaction(period.Id, Money.Create(100000m), TransactionType.Withdrawal, loanDate, "Initial Loan Amount");
        loanTxn.SetBorrowerId(borrower.Id);
        dbContext.Transactions.Add(loanTxn);
        await dbContext.SaveChangesAsync();

        var service = provider.GetRequiredService<IInterestCalculationService>();
        var endDate = new DateTime(2026, 8, 12);

        var result = await service.CalculateAsync(borrower.Id, endDate);

        Assert.Equal(loanDate, result.CalculationStartDate);
        Assert.Equal(36, result.CompletedMonths);
        Assert.Equal(3.0m, result.MonthlyInterestRate);
        Assert.Equal(3000m, result.MonthlyInterest);
        Assert.Equal(108000.00m, result.TotalInterest);
        Assert.Equal(100000.00m, result.ClosingPrincipal);
        Assert.Equal(208000.00m, result.TotalOutstanding);
    }

    [Fact]
    public async Task CalculateAsync_MultipleBorrowers_CalculatesIndependently_WithCustomRatesAndEventIntervals()
    {
        using var temp = new Persistence.TempDatabase();
        await using var dbContext = new DhirDharDbContext(temp.CreateOptions());
        await dbContext.Database.EnsureCreatedAsync();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddSingleton(dbContext);
        services.AddScoped<IInterestCalculationService, InterestCalculationService>();
        using var provider = services.BuildServiceProvider();

        var period = new FinancialPeriod("Global Period", new DateTime(2023, 1, 1), new DateTime(2027, 1, 1));
        dbContext.FinancialPeriods.Add(period);

        // Borrower A: 2.0% / month, Loan ₹100,000 on 12/08/2023, Deposit ₹20,000 on 12/08/2024
        var dateA = new DateTime(2023, 8, 12);
        var borrowerA = new Borrower("B-A", "Borrower A", "FatherA", "SurnameA", "VillageA", "9000000001", "AddA", "NoteA", "111122223333", dateA);
        borrowerA.SetPhotosAndLoanType(null, null, "Gold", "Ring", 10m, 100000m, dateA, 2.0m);
        dbContext.Borrowers.Add(borrowerA);

        var txnA1 = new Transaction(period.Id, Money.Create(100000m), TransactionType.Withdrawal, dateA, "Initial Loan Amount");
        txnA1.SetBorrowerId(borrowerA.Id);
        var depDateA = new DateTime(2024, 8, 12);
        var txnA2 = new Transaction(period.Id, Money.Create(20000m), TransactionType.Deposit, depDateA, "Repayment");
        txnA2.SetBorrowerId(borrowerA.Id);
        dbContext.Transactions.AddRange(txnA1, txnA2);

        // Borrower B: 4.0% / month, Loan ₹50,000 on 15/01/2024, Withdrawal ₹10,000 on 15/04/2024
        var dateB = new DateTime(2024, 1, 15);
        var borrowerB = new Borrower("B-B", "Borrower B", "FatherB", "SurnameB", "VillageB", "9000000002", "AddB", "NoteB", "444455556666", dateB);
        borrowerB.SetPhotosAndLoanType(null, null, "Silver", "Chain", 50m, 50000m, dateB, 4.0m);
        dbContext.Borrowers.Add(borrowerB);

        var txnB1 = new Transaction(period.Id, Money.Create(50000m), TransactionType.Withdrawal, dateB, "Initial Loan Amount");
        txnB1.SetBorrowerId(borrowerB.Id);
        var withDateB = new DateTime(2024, 4, 15);
        var txnB2 = new Transaction(period.Id, Money.Create(10000m), TransactionType.Withdrawal, withDateB, "Topup");
        txnB2.SetBorrowerId(borrowerB.Id);
        dbContext.Transactions.AddRange(txnB1, txnB2);

        await dbContext.SaveChangesAsync();

        var service = provider.GetRequiredService<IInterestCalculationService>();

        // Calculate Borrower A
        var resultA = await service.CalculateAsync(borrowerA.Id, new DateTime(2024, 8, 31));
        Assert.Equal(2.0m, resultA.MonthlyInterestRate);
        var segA = resultA.Segments.FirstOrDefault(s => s.SegmentEndDate == depDateA && s.TransactionType == "Deposit");
        Assert.NotNull(segA);
        Assert.True(segA.ClosingPrincipal > 0m);

        // Calculate Borrower B
        var resultB = await service.CalculateAsync(borrowerB.Id, new DateTime(2024, 4, 30));
        Assert.Equal(4.0m, resultB.MonthlyInterestRate);
        var segB = resultB.Segments.FirstOrDefault(s => s.SegmentEndDate == withDateB && s.TransactionType == "Withdrawal");
        Assert.NotNull(segB);
        Assert.True(segB.ClosingPrincipal > 50000m);
    }

    [Fact]
    public async Task CalculateAsync_ClosedAccount_StopsInterestCalculationAtClosedDatePermanently()
    {
        using var temp = new Persistence.TempDatabase();
        await using var dbContext = new DhirDharDbContext(temp.CreateOptions());
        await dbContext.Database.EnsureCreatedAsync();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddSingleton(dbContext);
        services.AddScoped<IInterestCalculationService, InterestCalculationService>();
        using var provider = services.BuildServiceProvider();

        var loanDate = new DateTime(2023, 8, 12);
        var closedDate = new DateTime(2026, 8, 12);

        var borrower = new Borrower("BN-444", "Closed Borrower", "Father", "Surname", "Village", "9999999999", "Address", "Notes", "123456789012", loanDate);
        borrower.SetPhotosAndLoanType(null, null, "Cash", null, null, 50000m, loanDate, 3.0m);
        borrower.CloseAccount(closedDate);

        dbContext.Borrowers.Add(borrower);

        var period = new FinancialPeriod("Test Period", loanDate.AddDays(-30), DateTime.UtcNow.AddDays(3650));
        dbContext.FinancialPeriods.Add(period);
        await dbContext.SaveChangesAsync();

        var loanTxn = new Transaction(period.Id, Money.Create(50000m), TransactionType.Withdrawal, loanDate, "Initial Loan");
        loanTxn.SetBorrowerId(borrower.Id);
        dbContext.Transactions.Add(loanTxn);
        await dbContext.SaveChangesAsync();

        var service = provider.GetRequiredService<IInterestCalculationService>();

        // Calculate at ClosedDate (12/08/2026)
        var resultOnClosedDate = await service.CalculateAsync(borrower.Id, closedDate);

        // Calculate far in future (31/12/2030)
        var resultFarInFuture = await service.CalculateAsync(borrower.Id, new DateTime(2030, 12, 31));

        Assert.Equal(closedDate, resultOnClosedDate.CalculationEndDate);
        Assert.Equal(closedDate, resultFarInFuture.CalculationEndDate);
        Assert.Equal(resultOnClosedDate.TotalInterest, resultFarInFuture.TotalInterest);
        Assert.Equal(resultOnClosedDate.ClosingPrincipal, resultFarInFuture.ClosingPrincipal);
        Assert.True(resultFarInFuture.IsClosed);
    }
}
