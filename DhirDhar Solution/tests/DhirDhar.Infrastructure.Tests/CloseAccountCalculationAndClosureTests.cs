using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Borrowers.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.Interest;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Borrowers;
using DhirDhar.Infrastructure.Interest;
using DhirDhar.Infrastructure.Persistence;
using DhirDhar.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class CloseAccountCalculationAndClosureTests
{
    private static (IServiceScopeFactory ScopeFactory, BorrowerService BorrowerService, InterestCalculationService InterestService, DbContextOptions<DhirDharDbContext> Options, TempDatabase TempDb) CreateTestEnvironment()
    {
        var tempDb = new TempDatabase();
        var options = tempDb.CreateOptions();
        using (var initContext = new DhirDharDbContext(options))
        {
            initContext.Database.EnsureCreatedAsync().GetAwaiter().GetResult();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new DhirDharDbContext(options));
        services.AddScoped<DhirDhar.Application.Interest.IInterestCalculationService, InterestCalculationService>();
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var borrowerService = new BorrowerService(scopeFactory, NullLogger<BorrowerService>.Instance);
        var interestService = (InterestCalculationService)sp.GetRequiredService<DhirDhar.Application.Interest.IInterestCalculationService>();

        return (scopeFactory, borrowerService, interestService, options, tempDb);
    }

    private static CreateBorrowerRequest CreateRequest(
        string number,
        string name,
        DateTime loanDate,
        decimal loanAmount,
        decimal interestRate)
    {
        return new CreateBorrowerRequest(
            BorrowerNumber: number,
            Name: name,
            FatherName: "Father",
            Surname: "Surname",
            Village: "Ahmedabad",
            Contact: "9876543210",
            Address: "Main Street",
            AadharNumber: "123456789012",
            EntryDate: loanDate,
            LoanAmount: loanAmount,
            LoanDate: loanDate,
            Notes: "Test Loan",
            LoanType: "Cash",
            OrnamentType: null,
            OrnamentWeight: null,
            InterestRate: interestRate);
    }

    // =========================================================================
    // 1. LOAN WITH NO EVENTS
    // =========================================================================
    [Fact]
    public async Task Scenario01_LoanWithNoEvents_CalculatesAccruedInterestAndSavesExactClosingAmount()
    {
        var env = CreateTestEnvironment();
        using var tempDb = env.TempDb;

        // Loan: ₹45,000, 3% per month, Start: 01/01/2026, Close: 01/04/2026 (3 complete months)
        var startDate = new DateTime(2026, 1, 1);
        var closeDate = new DateTime(2026, 4, 1);

        var request = CreateRequest("B-TEST-01", "Ramesh Patel", startDate, 45000.00m, 3.0m);
        var created = await env.BorrowerService.CreateAsync(request);

        // Calculate before close
        var calc = await env.InterestService.CalculateAsync(created.Id, closeDate);
        // 45,000 * 3% = 1,350 per month * 3 = 4,050
        Assert.Equal(4050.00m, calc.TotalInterest);
        Assert.Equal(45000.00m, calc.ClosingPrincipal);
        Assert.Equal(49050.00m, calc.TotalOutstanding);

        // Close Account
        await env.BorrowerService.CloseAccountAsync(created.Id, closeDate, calc.TotalOutstanding, calc.TotalInterest);

        var closed = await env.BorrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(closed);
        Assert.Equal(BorrowerStatus.Closed.ToString(), closed.Status);
        Assert.Equal(closeDate, closed.ClosedDate);
        Assert.Equal(49050.00m, closed.ClosingAmount);
        Assert.Equal(4050.00m, closed.ClosedAccruedInterest);

        // Verification after closing requesting a far future date
        var futureCalc = await env.InterestService.CalculateAsync(created.Id, new DateTime(2027, 1, 1));
        Assert.Equal(closeDate, futureCalc.CalculationEndDate);
        Assert.Equal(4050.00m, futureCalc.TotalInterest);
        Assert.Equal(49050.00m, futureCalc.TotalOutstanding);
    }

    // =========================================================================
    // 2. LOAN WITH DEPOSIT BEFORE CLOSING
    // =========================================================================
    [Fact]
    public async Task Scenario02_LoanWithDepositBeforeClosing_CapitalizesInterestAndComputesClosingAmount()
    {
        var env = CreateTestEnvironment();
        using var tempDb = env.TempDb;

        // Loan: ₹10,000, 3% per month, Start: 20/08/2026
        // Deposit: ₹2,000 on 05/12/2026 (3 months + 15 days)
        // Interest to deposit: 300 + 300 + 300 + 150 = 1,050
        // New Principal: 10,000 + 1,050 - 2,000 = 9,050
        // Close on: 05/01/2027 (1 month after deposit)
        // Interest on 9,050 for 1 month = 271.50
        // Expected Closing Amount: 9,050 + 271.50 = 9,321.50
        var loanDate = new DateTime(2026, 8, 20);
        var depositDate = new DateTime(2026, 12, 5);
        var closeDate = new DateTime(2027, 1, 5);

        var request = CreateRequest("B-TEST-02", "Suresh Shah", loanDate, 10000.00m, 3.0m);
        var created = await env.BorrowerService.CreateAsync(request);

        // Add Deposit transaction
        await using (var context = new DhirDharDbContext(env.Options))
        {
            var period = new FinancialPeriod("P1", loanDate, closeDate);
            context.FinancialPeriods.Add(period);
            await context.SaveChangesAsync();

            var txn = new Transaction(created.Id, period.Id, Money.Create(2000.00m), TransactionType.Deposit, depositDate, "Part Repayment");
            context.Transactions.Add(txn);
            await context.SaveChangesAsync();
        }

        var calc = await env.InterestService.CalculateAsync(created.Id, closeDate);
        Assert.Equal(9050.00m, calc.ClosingPrincipal);
        Assert.Equal(271.50m, calc.UncapitalizedInterest);
        Assert.Equal(9321.50m, calc.TotalOutstanding);

        await env.BorrowerService.CloseAccountAsync(created.Id, closeDate, calc.TotalOutstanding, calc.TotalInterest);

        var closed = await env.BorrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(closed);
        Assert.Equal(9321.50m, closed.ClosingAmount);
        Assert.Equal(calc.TotalInterest, closed.ClosedAccruedInterest);
    }

    // =========================================================================
    // 3. LOAN WITH WITHDRAWAL BEFORE CLOSING
    // =========================================================================
    [Fact]
    public async Task Scenario03_LoanWithWithdrawalBeforeClosing_CapitalizesInterestAndComputesClosingAmount()
    {
        var env = CreateTestEnvironment();
        using var tempDb = env.TempDb;

        // Loan: ₹20,000, 2% per month, Start: 01/01/2026
        // Withdrawal: ₹5,000 on 01/03/2026 (2 months)
        // Interest to withdrawal: 400 + 400 = 800
        // New Principal: 20,000 + 800 + 5,000 = 25,800
        // Close on: 01/05/2026 (2 months after withdrawal)
        // Interest on 25,800 for 2 months: 25,800 * 2% * 2 = 1,032
        // Expected Closing Amount: 25,800 + 1,032 = 26,832.00
        var loanDate = new DateTime(2026, 1, 1);
        var withDate = new DateTime(2026, 3, 1);
        var closeDate = new DateTime(2026, 5, 1);

        var request = CreateRequest("B-TEST-03", "Mahesh Joshi", loanDate, 20000.00m, 2.0m);
        var created = await env.BorrowerService.CreateAsync(request);

        await using (var context = new DhirDharDbContext(env.Options))
        {
            var period = new FinancialPeriod("P1", loanDate, closeDate);
            context.FinancialPeriods.Add(period);
            await context.SaveChangesAsync();

            var txn = new Transaction(created.Id, period.Id, Money.Create(5000.00m), TransactionType.Withdrawal, withDate, "Additional Loan");
            context.Transactions.Add(txn);
            await context.SaveChangesAsync();
        }

        var calc = await env.InterestService.CalculateAsync(created.Id, closeDate);
        Assert.Equal(25800.00m, calc.ClosingPrincipal);
        Assert.Equal(1032.00m, calc.UncapitalizedInterest);
        Assert.Equal(26832.00m, calc.TotalOutstanding);

        await env.BorrowerService.CloseAccountAsync(created.Id, closeDate, calc.TotalOutstanding, calc.TotalInterest);

        var closed = await env.BorrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(closed);
        Assert.Equal(26832.00m, closed.ClosingAmount);
    }

    // =========================================================================
    // 4. MULTIPLE DEPOSIT / WITHDRAWAL EVENTS
    // =========================================================================
    [Fact]
    public async Task Scenario04_MultipleDepositAndWithdrawalEvents_CalculatesFinalClosingAmountCorrectly()
    {
        var env = CreateTestEnvironment();
        using var tempDb = env.TempDb;

        // Loan: ₹50,000, 2% / month, Start: 01/01/2026
        // Event 1: Deposit ₹10,000 on 01/02/2026 (1 month, int = 1,000 -> New Princ = 50,000 + 1,000 - 10,000 = 41,000)
        // Event 2: Withdrawal ₹4,000 on 01/03/2026 (1 month, int = 41,000 * 2% = 820 -> New Princ = 41,000 + 820 + 4,000 = 45,820)
        // Event 3: Deposit ₹5,820 on 01/04/2026 (1 month, int = 45,820 * 2% = 916.40 -> New Princ = 45,820 + 916.40 - 5,820 = 40,916.40)
        // Close on: 01/05/2026 (1 month, int = 40,916.40 * 2% = 818.33 -> Closing Amount = 40,916.40 + 818.33 = 41,734.73)
        var dStart = new DateTime(2026, 1, 1);
        var d1 = new DateTime(2026, 2, 1);
        var d2 = new DateTime(2026, 3, 1);
        var d3 = new DateTime(2026, 4, 1);
        var dClose = new DateTime(2026, 5, 1);

        var request = CreateRequest("B-TEST-04", "Kishore Kumar", dStart, 50000.00m, 2.0m);
        var created = await env.BorrowerService.CreateAsync(request);

        await using (var context = new DhirDharDbContext(env.Options))
        {
            var period = new FinancialPeriod("P1", dStart, dClose);
            context.FinancialPeriods.Add(period);
            await context.SaveChangesAsync();

            context.Transactions.Add(new Transaction(created.Id, period.Id, Money.Create(10000.00m), TransactionType.Deposit, d1, "Deposit 1"));
            context.Transactions.Add(new Transaction(created.Id, period.Id, Money.Create(4000.00m), TransactionType.Withdrawal, d2, "Withdrawal 1"));
            context.Transactions.Add(new Transaction(created.Id, period.Id, Money.Create(5820.00m), TransactionType.Deposit, d3, "Deposit 2"));
            await context.SaveChangesAsync();
        }

        var calc = await env.InterestService.CalculateAsync(created.Id, dClose);
        Assert.Equal(40916.40m, calc.ClosingPrincipal);
        Assert.Equal(818.33m, calc.UncapitalizedInterest);
        Assert.Equal(41734.73m, calc.TotalOutstanding);

        await env.BorrowerService.CloseAccountAsync(created.Id, dClose);
        var closed = await env.BorrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(closed);
        Assert.Equal(41734.73m, closed.ClosingAmount);
    }

    // =========================================================================
    // 5. CLOSING EXACTLY ON A MONTHLY ANNIVERSARY
    // =========================================================================
    [Fact]
    public async Task Scenario05_ClosingExactlyOnMonthlyAnniversary_CalculatesFullMonthsAt30DaysEach()
    {
        var env = CreateTestEnvironment();
        using var tempDb = env.TempDb;

        // Start: 15/01/2026, Close: 15/05/2026 (Exactly 4 complete months)
        // Loan: ₹60,000, 2.5% / month
        // Monthly interest = 60,000 * 2.5% = 1,500 * 4 = 6,000
        // Closing Amount = 66,000
        var dStart = new DateTime(2026, 1, 15);
        var dClose = new DateTime(2026, 5, 15);

        var request = CreateRequest("B-TEST-05", "Gopaldas", dStart, 60000.00m, 2.5m);
        var created = await env.BorrowerService.CreateAsync(request);

        var calc = await env.InterestService.CalculateAsync(created.Id, dClose);
        var interestSegments = calc.Segments.Where(s => s.ElapsedDays > 0).ToList();
        Assert.Equal(4, interestSegments.Count);
        Assert.All(interestSegments, s => Assert.Equal(30, s.ElapsedDays));
        Assert.Equal(6000.00m, calc.TotalInterest);
        Assert.Equal(66000.00m, calc.TotalOutstanding);

        await env.BorrowerService.CloseAccountAsync(created.Id, dClose, calc.TotalOutstanding, calc.TotalInterest);
        var closed = await env.BorrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(closed);
        Assert.Equal(66000.00m, closed.ClosingAmount);
    }

    // =========================================================================
    // 6. CLOSING BEFORE ONE COMPLETE MONTH
    // =========================================================================
    [Fact]
    public async Task Scenario06_ClosingBeforeOneCompleteMonth_Applies30DayRatioRule()
    {
        var env = CreateTestEnvironment();
        using var tempDb = env.TempDb;

        // Start: 10/01/2026, Close: 25/01/2026 (15 days)
        // Loan: ₹30,000, 3% / month
        // Monthly full interest = 900. 15 days = 15/30 * 900 = 450.
        // Closing Amount = 30,450.00
        var dStart = new DateTime(2026, 1, 10);
        var dClose = new DateTime(2026, 1, 25);

        var request = CreateRequest("B-TEST-06", "Anand Verma", dStart, 30000.00m, 3.0m);
        var created = await env.BorrowerService.CreateAsync(request);

        var calc = await env.InterestService.CalculateAsync(created.Id, dClose);
        var interestSegments = calc.Segments.Where(s => s.ElapsedDays > 0).ToList();
        Assert.Single(interestSegments);
        Assert.Equal(15, interestSegments[0].ElapsedDays);
        Assert.Equal(450.00m, calc.TotalInterest);
        Assert.Equal(30450.00m, calc.TotalOutstanding);

        await env.BorrowerService.CloseAccountAsync(created.Id, dClose);
        var closed = await env.BorrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(closed);
        Assert.Equal(30450.00m, closed.ClosingAmount);
    }

    // =========================================================================
    // 7. CLOSING AFTER ONE COMPLETE MONTH PLUS PARTIAL DAYS
    // =========================================================================
    [Fact]
    public async Task Scenario07_ClosingAfterCompleteMonthPlusPartialDays_CalculatesBothCorrectly()
    {
        var env = CreateTestEnvironment();
        using var tempDb = env.TempDb;

        // Start: 10/01/2026, Close: 25/02/2026 (1 month + 15 days)
        // Loan: ₹30,000, 3% / month
        // Month 1: 900. Partial 15 days: 450. Total = 1,350.
        // Closing Amount = 31,350.00
        var dStart = new DateTime(2026, 1, 10);
        var dClose = new DateTime(2026, 2, 25);

        var request = CreateRequest("B-TEST-07", "Chetan Bhagat", dStart, 30000.00m, 3.0m);
        var created = await env.BorrowerService.CreateAsync(request);

        var calc = await env.InterestService.CalculateAsync(created.Id, dClose);
        var interestSegments = calc.Segments.Where(s => s.ElapsedDays > 0).ToList();
        Assert.Equal(2, interestSegments.Count);
        Assert.Equal(30, interestSegments[0].ElapsedDays);
        Assert.Equal(15, interestSegments[1].ElapsedDays);
        Assert.Equal(1350.00m, calc.TotalInterest);
        Assert.Equal(31350.00m, calc.TotalOutstanding);

        await env.BorrowerService.CloseAccountAsync(created.Id, dClose);
        var closed = await env.BorrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(closed);
        Assert.Equal(31350.00m, closed.ClosingAmount);
    }

    // =========================================================================
    // 8. FEBRUARY CLOSING (NON-LEAP YEAR)
    // =========================================================================
    [Fact]
    public async Task Scenario08_FebruaryClosing_TreatedAsFull30DaysForCompleteMonth()
    {
        var env = CreateTestEnvironment();
        using var tempDb = env.TempDb;

        // Start: 01/02/2025, Close: 01/03/2025 (February in 2025 has 28 days, treated as full month 30/30)
        // Loan: ₹100,000, 2% / month -> Interest = 2,000
        // Closing Amount = 102,000.00
        var dStart = new DateTime(2025, 2, 1);
        var dClose = new DateTime(2025, 3, 1);

        var request = CreateRequest("B-TEST-08", "Nitin Gadkari", dStart, 100000.00m, 2.0m);
        var created = await env.BorrowerService.CreateAsync(request);

        var calc = await env.InterestService.CalculateAsync(created.Id, dClose);
        var interestSegments = calc.Segments.Where(s => s.ElapsedDays > 0).ToList();
        Assert.Single(interestSegments);
        Assert.Equal(30, interestSegments[0].ElapsedDays);
        Assert.Equal(2000.00m, calc.TotalInterest);
        Assert.Equal(102000.00m, calc.TotalOutstanding);

        await env.BorrowerService.CloseAccountAsync(created.Id, dClose);
        var closed = await env.BorrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(closed);
        Assert.Equal(102000.00m, closed.ClosingAmount);
    }

    // =========================================================================
    // 9. LEAP-YEAR FEBRUARY CLOSING
    // =========================================================================
    [Fact]
    public async Task Scenario09_LeapYearFebruaryClosing_TreatedAsFull30DaysForCompleteMonth()
    {
        var env = CreateTestEnvironment();
        using var tempDb = env.TempDb;

        // Start: 01/02/2028, Close: 01/03/2028 (2028 is leap year, 29 days in Feb -> treated as full month 30/30)
        // Loan: ₹50,000, 3% / month -> Interest = 1,500
        // Closing Amount = 51,500.00
        var dStart = new DateTime(2028, 2, 1);
        var dClose = new DateTime(2028, 3, 1);

        var request = CreateRequest("B-TEST-09", "Pravin Bhai", dStart, 50000.00m, 3.0m);
        var created = await env.BorrowerService.CreateAsync(request);

        var calc = await env.InterestService.CalculateAsync(created.Id, dClose);
        var interestSegments = calc.Segments.Where(s => s.ElapsedDays > 0).ToList();
        Assert.Single(interestSegments);
        Assert.Equal(30, interestSegments[0].ElapsedDays);
        Assert.Equal(1500.00m, calc.TotalInterest);
        Assert.Equal(51500.00m, calc.TotalOutstanding);

        await env.BorrowerService.CloseAccountAsync(created.Id, dClose);
        var closed = await env.BorrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(closed);
        Assert.Equal(51500.00m, closed.ClosingAmount);
    }

    // =========================================================================
    // 10. CLOSING DATE EQUAL TO LOAN START DATE
    // =========================================================================
    [Fact]
    public async Task Scenario10_ClosingDateEqualToLoanStartDate_ZeroInterestAndClosingAmountEqualsPrincipal()
    {
        var env = CreateTestEnvironment();
        using var tempDb = env.TempDb;

        // Start: 21/08/2026, Close: 21/08/2026
        // Loan: ₹45,000, 3% / month
        // Interest = 0, Closing Amount = 45,000.00
        var dSame = new DateTime(2026, 8, 21);

        var request = CreateRequest("B-TEST-10", "Jayesh Patel", dSame, 45000.00m, 3.0m);
        var created = await env.BorrowerService.CreateAsync(request);

        var calc = await env.InterestService.CalculateAsync(created.Id, dSame);
        Assert.Equal(0m, calc.TotalInterest);
        Assert.Equal(45000.00m, calc.ClosingPrincipal);
        Assert.Equal(45000.00m, calc.TotalOutstanding);

        await env.BorrowerService.CloseAccountAsync(created.Id, dSame);
        var closed = await env.BorrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(closed);
        Assert.Equal(45000.00m, closed.ClosingAmount);
        Assert.Equal(0m, closed.ClosedAccruedInterest);
    }

    // =========================================================================
    // 11. CLOSING DATE AFTER MULTIPLE EVENT CYCLES
    // =========================================================================
    [Fact]
    public async Task Scenario11_ClosingDateAfterMultipleEventCycles_MatchesEngineCalculation()
    {
        var env = CreateTestEnvironment();
        using var tempDb = env.TempDb;

        var dStart = new DateTime(2026, 1, 1);
        var d1 = new DateTime(2026, 3, 1);
        var d2 = new DateTime(2026, 6, 1);
        var dClose = new DateTime(2026, 9, 1);

        var request = CreateRequest("B-TEST-11", "Arvind Joshi", dStart, 100000.00m, 2.0m);
        var created = await env.BorrowerService.CreateAsync(request);

        await using (var context = new DhirDharDbContext(env.Options))
        {
            var period = new FinancialPeriod("P1", dStart, dClose);
            context.FinancialPeriods.Add(period);
            await context.SaveChangesAsync();

            // 01/01 -> 01/03: 2 months int on 100,000 = 4,000. Deposit 20,000 -> New Princ = 84,000
            context.Transactions.Add(new Transaction(created.Id, period.Id, Money.Create(20000.00m), TransactionType.Deposit, d1, "Deposit 1"));
            // 01/03 -> 01/06: 3 months int on 84,000 = 5,040. Withdrawal 10,000 -> New Princ = 84,000 + 5,040 + 10,000 = 99,040
            context.Transactions.Add(new Transaction(created.Id, period.Id, Money.Create(10000.00m), TransactionType.Withdrawal, d2, "Withdrawal 1"));
            await context.SaveChangesAsync();
        }

        // 01/06 -> 01/09: 3 months int on 99,040 = 99,040 * 2% * 3 = 5,942.40
        // Expected Closing Amount: 99,040 + 5,942.40 = 104,982.40
        var calc = await env.InterestService.CalculateAsync(created.Id, dClose);
        Assert.Equal(99040.00m, calc.ClosingPrincipal);
        Assert.Equal(5942.40m, calc.UncapitalizedInterest);
        Assert.Equal(104982.40m, calc.TotalOutstanding);

        await env.BorrowerService.CloseAccountAsync(created.Id, dClose);
        var closed = await env.BorrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(closed);
        Assert.Equal(104982.40m, closed.ClosingAmount);
    }

    // =========================================================================
    // 12. ALREADY CLOSED BORROWER IMMUTABILITY & REOPENING
    // =========================================================================
    [Fact]
    public async Task Scenario12_AlreadyClosedBorrower_MaintainsHistoricalClosingAmountAndStopsFutureInterest()
    {
        var env = CreateTestEnvironment();
        using var tempDb = env.TempDb;

        var dStart = new DateTime(2026, 1, 1);
        var dClose = new DateTime(2026, 4, 1);

        var request = CreateRequest("B-TEST-12", "Bhavik Shah", dStart, 50000.00m, 3.0m);
        var created = await env.BorrowerService.CreateAsync(request);

        // Close on 01/04/2026 (3 months, int = 4,500, total = 54,500)
        var calc = await env.InterestService.CalculateAsync(created.Id, dClose);
        Assert.Equal(54500.00m, calc.TotalOutstanding);

        await env.BorrowerService.CloseAccountAsync(created.Id, dClose, calc.TotalOutstanding, calc.TotalInterest);

        var closed1 = await env.BorrowerService.GetByIdAsync(created.Id);
        Assert.NotNull(closed1);
        Assert.Equal(BorrowerStatus.Closed.ToString(), closed1.Status);
        Assert.Equal(54500.00m, closed1.ClosingAmount);

        // Calling calculate for future dates (1 year later) must never change the closing amount or accrue more interest
        var futureCalc = await env.InterestService.CalculateAsync(created.Id, new DateTime(2030, 1, 1));
        Assert.Equal(dClose, futureCalc.CalculationEndDate);
        Assert.Equal(54500.00m, futureCalc.TotalOutstanding);
        Assert.Equal(4500.00m, futureCalc.TotalInterest);

        // Reopen Account
        var reopened = await env.BorrowerService.ChangeStatusAsync(created.Id, BorrowerStatus.Active);
        Assert.Equal(BorrowerStatus.Active.ToString(), reopened.Status);
        Assert.Null(reopened.ClosedDate);
        Assert.Null(reopened.ClosingAmount);

        // After reopening, calculating for a future date continues calculating interest normally
        var afterReopenCalc = await env.InterestService.CalculateAsync(created.Id, new DateTime(2026, 7, 1));
        // 6 months on 50,000 * 3% = 9,000 -> Total = 59,000
        Assert.Equal(9000.00m, afterReopenCalc.TotalInterest);
        Assert.Equal(59000.00m, afterReopenCalc.TotalOutstanding);
    }
}
