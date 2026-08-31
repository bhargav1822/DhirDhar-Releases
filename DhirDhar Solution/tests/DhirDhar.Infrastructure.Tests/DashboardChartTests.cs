using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DhirDhar.Application.Dashboard;
using DhirDhar.Application.Dashboard.Models;
using DhirDhar.Domain.Entities;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.ValueObjects;
using DhirDhar.Infrastructure.Configuration;
using DhirDhar.Infrastructure.DependencyInjection;
using DhirDhar.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DhirDhar.Infrastructure.Tests;

public class DashboardChartTests
{
    private static ServiceProvider BuildProvider(DatabaseOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddInfrastructure(options);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task GetYearlyChartData_EmptyDatabase_Returns12MonthsWithZeroHeights()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        var result = await service.GetYearlyChartDataAsync(2021);

        Assert.NotNull(result);
        Assert.Equal(2021, result.Year);
        Assert.Equal(12, result.MonthlyGroups.Count);
        Assert.Equal(1000000m, result.MaxYAxisValue);
        Assert.NotEmpty(result.YAxisTicks);

        for (int i = 0; i < 12; i++)
        {
            var group = result.MonthlyGroups[i];
            Assert.Equal(i + 1, group.Month);
            Assert.Equal(2021, group.Year);
            Assert.Equal(4, group.Bars.Count);

            Assert.Equal("NewLoans", group.NewLoansBar.CategoryKey);
            Assert.Equal(0m, group.NewLoansBar.Amount);
            Assert.Equal(0.0, group.NewLoansBar.BarHeight);

            Assert.Equal("Withdrawals", group.WithdrawalsBar.CategoryKey);
            Assert.Equal(0m, group.WithdrawalsBar.Amount);
            Assert.Equal(0.0, group.WithdrawalsBar.BarHeight);

            Assert.Equal("Deposits", group.DepositsBar.CategoryKey);
            Assert.Equal(0m, group.DepositsBar.Amount);
            Assert.Equal(0.0, group.DepositsBar.BarHeight);

            Assert.Equal("InterestEarned", group.InterestEarnedBar.CategoryKey);
            Assert.Equal(0m, group.InterestEarnedBar.Amount);
            Assert.Equal(0.0, group.InterestEarnedBar.BarHeight);
        }
    }

    [Fact]
    public async Task GetYearlyPeriodSummary_ZeroTransactionYear_ReturnsAllZeros()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("2021-2024 Period", new DateTime(2021, 1, 1), new DateTime(2024, 12, 31));
        context.FinancialPeriods.Add(period);

        var borrower = new Borrower("DS 01", "Ramesh Patel", null, null, null, new DateTime(2021, 1, 1));
        borrower.CloseAccount(new DateTime(2024, 12, 31));
        context.Borrowers.Add(borrower);

        // Add 2021, 2022, 2023, 2024 transactions
        context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(500000m), TransactionType.Withdrawal, new DateTime(2021, 5, 1), "2021 Loan"));
        context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(200000m), TransactionType.Withdrawal, new DateTime(2022, 3, 1), "2022 Loan"));
        context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(100000m), TransactionType.Deposit, new DateTime(2023, 8, 1), "2023 Repayment"));
        context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(50000m), TransactionType.Deposit, new DateTime(2024, 11, 1), "2024 Repayment"));
        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        // Act - Select 2025 (which has NO transactions)
        var summary2025 = await service.GetYearlyPeriodSummaryAsync(2025);

        // Assert - All four cards must be strictly ₹0.00
        Assert.Equal(0m, summary2025.OpeningBalance);
        Assert.Equal(0m, summary2025.NewLoans);
        Assert.Equal(0m, summary2025.Payments);
        Assert.Equal(0m, summary2025.ClosingBalance);

        // Chart for 2025 must also be all zero
        var chart2025 = await service.GetYearlyChartDataAsync(2025);
        Assert.All(chart2025.MonthlyGroups, g =>
        {
            Assert.Equal(0m, g.NewLoansBar.Amount);
            Assert.Equal(0.0, g.NewLoansBar.BarHeight);
            Assert.Equal(0m, g.WithdrawalsBar.Amount);
            Assert.Equal(0.0, g.WithdrawalsBar.BarHeight);
            Assert.Equal(0m, g.DepositsBar.Amount);
            Assert.Equal(0.0, g.DepositsBar.BarHeight);
            Assert.Equal(0m, g.InterestEarnedBar.Amount);
            Assert.Equal(0.0, g.InterestEarnedBar.BarHeight);
        });
    }

    [Fact]
    public async Task GetYearlyPeriodSummary_YearWithTransactions_CalculatesStrictYearlyValues()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("All Years", new DateTime(2020, 1, 1), new DateTime(2026, 12, 31));
        context.FinancialPeriods.Add(period);

        var borrower = new Borrower("DS 01", "Suresh Shah", null, null, null, new DateTime(2021, 1, 1));
        context.Borrowers.Add(borrower);

        // 2021: 200,000 deposits, 50,000 withdrawals => Net end of 2021 = 150,000
        context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(200000m), TransactionType.Deposit, new DateTime(2021, 2, 1), "Prior Deposit"));
        context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(50000m), TransactionType.Withdrawal, new DateTime(2021, 6, 1), "Prior Loan"));

        // 2022: New Loans = 100,000 (Withdrawals), Payments = 30,000 (Deposits)
        context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(100000m), TransactionType.Withdrawal, new DateTime(2022, 4, 15), "2022 Loan"));
        context.Transactions.Add(new Transaction(borrower.Id, period.Id, Money.Create(30000m), TransactionType.Deposit, new DateTime(2022, 9, 20), "2022 Payment"));

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        // Act - Select 2022
        var summary2022 = await service.GetYearlyPeriodSummaryAsync(2022);

        // Assert
        // Opening = Deposits Before (200k) - Withdrawals Before (50k) = 150,000
        Assert.Equal(150000m, summary2022.OpeningBalance);
        // New Loans in 2022 = 100,000
        Assert.Equal(100000m, summary2022.NewLoans);
        // Payments in 2022 = 30,000
        Assert.Equal(30000m, summary2022.Payments);
        // Closing = Opening (150k) + Payments (30k) - New Loans (100k) = 80,000
        Assert.Equal(80000m, summary2022.ClosingBalance);
    }

    [Fact]
    public async Task GetYearlyChartData_Required2021Scenario_CalculatesAccurateAmountsAndProportionalHeights()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("2021 Period", new DateTime(2021, 1, 1), new DateTime(2021, 12, 31));
        context.FinancialPeriods.Add(period);

        // Create test borrower
        var borrower = new Borrower("DS 01", "Ramesh Patel", "9876543210", "123 Main St", "Notes", new DateTime(2021, 1, 1));
        borrower.SetPhotosAndLoanType(null, null, "Personal", null, null, 100000m, new DateTime(2021, 1, 1), 3.0m);
        context.Borrowers.Add(borrower);
        await context.SaveChangesAsync();

        // 1. January: New Loan ₹100,000 (Withdrawal with Initial Loan)
        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(100000m),
            TransactionType.Withdrawal,
            new DateTime(2021, 1, 15),
            "Initial Loan Amount",
            "INIT-001"));

        // 2. March: Deposit ₹40,000
        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(40000m),
            TransactionType.Deposit,
            new DateTime(2021, 3, 10),
            "Monthly Repayment",
            "REC-001"));

        // 3. May: Withdrawal ₹20,000 (regular withdrawal/give payment)
        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(20000m),
            TransactionType.Withdrawal,
            new DateTime(2021, 5, 20),
            "Additional Cash Advance",
            "ADV-001"));

        // 4. August: New Loan ₹200,000 (New borrower / initial loan)
        var borrower2 = new Borrower("DS 02", "Suresh Shah", "9876543211", "456 Market St", "Notes", new DateTime(2021, 8, 1));
        borrower2.SetPhotosAndLoanType(null, null, "Business", null, null, 200000m, new DateTime(2021, 8, 1), 3.0m);
        context.Borrowers.Add(borrower2);
        await context.SaveChangesAsync();

        context.Transactions.Add(new Transaction(
            borrower2.Id,
            period.Id,
            Money.Create(200000m),
            TransactionType.Withdrawal,
            new DateTime(2021, 8, 5),
            "Initial Loan Amount",
            "INIT-002"));

        // 5. October: Interest ₹5,000
        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(5000m),
            TransactionType.Deposit,
            new DateTime(2021, 10, 12),
            "Interest Payment Received",
            "INT-001"));

        // 6. December: Deposit ₹75,000
        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(75000m),
            TransactionType.Deposit,
            new DateTime(2021, 12, 28),
            "Principal Settlement",
            "REC-002"));

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        // Query chart data for 2021
        var chart = await service.GetYearlyChartDataAsync(2021);

        Assert.NotNull(chart);
        Assert.Equal(2021, chart.Year);
        Assert.Equal(12, chart.MonthlyGroups.Count);
        Assert.Equal(200000m, chart.MaxAmount);
        Assert.True(chart.MaxYAxisValue >= 200000m);

        // Check January (Month 1): New Loan = 100,000, others 0
        var jan = chart.MonthlyGroups[0];
        Assert.Equal(1, jan.Month);
        Assert.Equal(100000m, jan.NewLoansBar.Amount);
        Assert.Equal(0m, jan.WithdrawalsBar.Amount);
        Assert.Equal(0m, jan.DepositsBar.Amount);
        Assert.True(jan.NewLoansBar.BarHeight > 0);

        // Check February (Month 2): All 0
        var feb = chart.MonthlyGroups[1];
        Assert.Equal(2, feb.Month);
        Assert.Equal(0m, feb.NewLoansBar.Amount);
        Assert.Equal(0.0, feb.NewLoansBar.BarHeight);
        Assert.Equal(0m, feb.WithdrawalsBar.Amount);
        Assert.Equal(0.0, feb.WithdrawalsBar.BarHeight);

        // Check March (Month 3): Deposit = 40,000
        var mar = chart.MonthlyGroups[2];
        Assert.Equal(3, mar.Month);
        Assert.Equal(40000m, mar.DepositsBar.Amount);
        Assert.True(mar.DepositsBar.BarHeight > 0);

        // Check May (Month 5): Withdrawal = 20,000
        var may = chart.MonthlyGroups[4];
        Assert.Equal(5, may.Month);
        Assert.Equal(20000m, may.WithdrawalsBar.Amount);
        Assert.True(may.WithdrawalsBar.BarHeight > 0);

        // Check August (Month 8): New Loan = 200,000 (Maximum amount)
        var aug = chart.MonthlyGroups[7];
        Assert.Equal(8, aug.Month);
        Assert.Equal(200000m, aug.NewLoansBar.Amount);
        Assert.True(aug.NewLoansBar.BarHeight > jan.NewLoansBar.BarHeight);
        // Bar height proportionality: Aug (200k) should be double Jan (100k)
        Assert.Equal(aug.NewLoansBar.BarHeight, jan.NewLoansBar.BarHeight * 2, precision: 1);

        // Check October (Month 10): Interest Earned >= 5,000
        var oct = chart.MonthlyGroups[9];
        Assert.Equal(10, oct.Month);
        Assert.True(oct.InterestEarnedBar.Amount >= 5000m);
        Assert.True(oct.InterestEarnedBar.BarHeight > 0);

        // Check December (Month 12): Deposit = 75,000
        var dec = chart.MonthlyGroups[11];
        Assert.Equal(12, dec.Month);
        Assert.Equal(75000m, dec.DepositsBar.Amount);
        Assert.True(dec.DepositsBar.BarHeight > mar.DepositsBar.BarHeight);

        // Check Tooltip formatting on populated bars
        Assert.Contains("January", jan.NewLoansBar.TooltipText);
        Assert.Contains("New Loans", jan.NewLoansBar.TooltipText);
        Assert.Contains("100,000", jan.NewLoansBar.TooltipText);

        Assert.Contains("March", mar.DepositsBar.TooltipText);
        Assert.Contains("Deposits", mar.DepositsBar.TooltipText);
        Assert.Contains("40,000", mar.DepositsBar.TooltipText);
    }

    [Fact]
    public async Task GetYearlyChartData_MultiYearIsolation_DoesNotBleedDataBetweenYears()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("Global Period", new DateTime(2020, 1, 1), new DateTime(2025, 12, 31));
        context.FinancialPeriods.Add(period);

        var borrower = new Borrower("DS 01", "Anita Sharma", "9876543212", "789 Ring Rd", null, new DateTime(2021, 1, 1));
        context.Borrowers.Add(borrower);
        await context.SaveChangesAsync();

        // 2021 transaction: New Loan 50,000
        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(50000m),
            TransactionType.Withdrawal,
            new DateTime(2021, 6, 15),
            "Initial Loan Amount",
            "INIT-2021"));

        // 2022 transaction: Deposit 30,000
        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(30000m),
            TransactionType.Deposit,
            new DateTime(2022, 4, 10),
            "Payment 2022",
            "REC-2022"));

        // 2023 transaction: Withdrawal 15,000
        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(15000m),
            TransactionType.Withdrawal,
            new DateTime(2023, 9, 20),
            "Payment 2023",
            "ADV-2023"));

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        // Query 2021
        var chart2021 = await service.GetYearlyChartDataAsync(2021);
        Assert.Equal(50000m, chart2021.MonthlyGroups[5].NewLoansBar.Amount);
        Assert.Equal(0m, chart2021.MonthlyGroups[3].DepositsBar.Amount);
        Assert.Equal(0m, chart2021.MonthlyGroups[8].WithdrawalsBar.Amount);

        // Query 2022
        var chart2022 = await service.GetYearlyChartDataAsync(2022);
        Assert.Equal(0m, chart2022.MonthlyGroups[5].NewLoansBar.Amount);
        Assert.Equal(30000m, chart2022.MonthlyGroups[3].DepositsBar.Amount);
        Assert.Equal(0m, chart2022.MonthlyGroups[8].WithdrawalsBar.Amount);

        // Query 2023
        var chart2023 = await service.GetYearlyChartDataAsync(2023);
        Assert.Equal(0m, chart2023.MonthlyGroups[5].NewLoansBar.Amount);
        Assert.Equal(0m, chart2023.MonthlyGroups[3].DepositsBar.Amount);
        Assert.Equal(15000m, chart2023.MonthlyGroups[8].WithdrawalsBar.Amount);
    }

    [Fact]
    public async Task GetAvailableYears_ReturnsDistinctYearsDescending()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("Global Period", new DateTime(2018, 1, 1), new DateTime(2030, 12, 31));
        context.FinancialPeriods.Add(period);

        var borrower = new Borrower("DS 01", "Kiran Rao", "9876543213", "Main St", null, new DateTime(2019, 5, 1));
        context.Borrowers.Add(borrower);
        await context.SaveChangesAsync();

        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(10000m),
            TransactionType.Withdrawal,
            new DateTime(2020, 2, 1),
            "Loan",
            "INIT-1"));

        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(5000m),
            TransactionType.Deposit,
            new DateTime(2024, 7, 1),
            "Repayment",
            "REC-1"));

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        var years = await service.GetAvailableYearsAsync();

        Assert.NotEmpty(years);
        Assert.Contains(2019, years);
        Assert.Contains(2020, years);
        Assert.Contains(2024, years);
        Assert.True(years.SequenceEqual(years.OrderByDescending(y => y)));
    }

    [Fact]
    public async Task GetYearlyChartData_DynamicScaleAndProportionalHeights_AdaptsToLargeAmounts()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("2025 Period", new DateTime(2025, 1, 1), new DateTime(2025, 12, 31));
        context.FinancialPeriods.Add(period);

        var borrower = new Borrower("DS 01", "Vikram Patel", "9876543214", "Main St", null, new DateTime(2025, 1, 1));
        context.Borrowers.Add(borrower);
        await context.SaveChangesAsync();

        // Jan: 500,000 loan
        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(500000m),
            TransactionType.Withdrawal,
            new DateTime(2025, 1, 10),
            "Initial Loan",
            "INIT-01"));

        // Jul: 1,000,000 loan
        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(1000000m),
            TransactionType.Withdrawal,
            new DateTime(2025, 7, 15),
            "Initial Loan",
            "INIT-02"));

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        var chart = await service.GetYearlyChartDataAsync(2025);

        Assert.Equal(1000000m, chart.MaxAmount);
        Assert.True(chart.MaxYAxisValue >= 1000000m);
        Assert.True(chart.YAxisTicks.Count >= 4);

        var janHeight = chart.MonthlyGroups[0].NewLoansBar.BarHeight;
        var julHeight = chart.MonthlyGroups[6].NewLoansBar.BarHeight;

        Assert.True(janHeight > 0);
        Assert.True(julHeight > 0);
        // Jul (1M) must be exactly double Jan (500k)
        Assert.Equal(julHeight, janHeight * 2, precision: 1);
    }

    [Fact]
    public async Task GetYearlyChartData_LiveTransactionMutation_UpdatesChartImmediately()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("2023 Period", new DateTime(2023, 1, 1), new DateTime(2023, 12, 31));
        context.FinancialPeriods.Add(period);

        var borrower = new Borrower("DS 01", "Pooja Mehta", "9876543215", "Station Rd", null, new DateTime(2023, 1, 1));
        context.Borrowers.Add(borrower);
        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        // Before transaction: March deposits = 0
        var initialChart = await service.GetYearlyChartDataAsync(2023);
        Assert.Equal(0m, initialChart.MonthlyGroups[2].DepositsBar.Amount);

        // Add transaction for March 2023
        await using (var mutateContext = new DhirDharDbContext(temp.CreateOptions()))
        {
            mutateContext.Transactions.Add(new Transaction(
                borrower.Id,
                period.Id,
                Money.Create(65000m),
                TransactionType.Deposit,
                new DateTime(2023, 3, 20),
                "Deposit via QR",
                "REC-099"));
            await mutateContext.SaveChangesAsync();
        }

        // After transaction: March deposits = 65,000 immediately reflected
        var updatedChart = await service.GetYearlyChartDataAsync(2023);
        Assert.Equal(65000m, updatedChart.MonthlyGroups[2].DepositsBar.Amount);
        Assert.True(updatedChart.MonthlyGroups[2].DepositsBar.BarHeight > 0);
    }

    [Fact]
    public async Task Test1_OneActiveBorrower_ShowsCalculatedInterest()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("2025 Period", new DateTime(2025, 1, 1), new DateTime(2025, 12, 31));
        context.FinancialPeriods.Add(period);

        var borrower = new Borrower("DS 01", "Active Borrower", null, null, null, new DateTime(2025, 1, 1));
        borrower.SetPhotosAndLoanType(null, null, "Personal", null, null, 100000m, new DateTime(2025, 1, 1), 3.0m);
        context.Borrowers.Add(borrower);

        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(100000m),
            TransactionType.Withdrawal,
            new DateTime(2025, 1, 1),
            "Initial Loan Amount",
            "INIT-01"));

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        var chart = await service.GetYearlyChartDataAsync(2025);

        Assert.NotNull(chart);
        Assert.Equal(12, chart.MonthlyGroups.Count);
        // All 12 months should have 3,000 interest (100k @ 3%)
        foreach (var group in chart.MonthlyGroups)
        {
            Assert.Equal(3000m, group.InterestEarnedBar.Amount);
            Assert.True(group.InterestEarnedBar.BarHeight > 0);
            Assert.Equal("#3B82F6", group.InterestEarnedBar.HexColor);
        }
    }

    [Fact]
    public async Task Test2_OneClosedBorrower_ContributesZeroInterest()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("2024-2025 Period", new DateTime(2024, 1, 1), new DateTime(2025, 12, 31));
        context.FinancialPeriods.Add(period);

        var closedBorrower = new Borrower("DS 01", "Closed Borrower", null, null, null, new DateTime(2024, 1, 1));
        closedBorrower.SetPhotosAndLoanType(null, null, "Personal", null, null, 100000m, new DateTime(2024, 1, 1), 3.0m);
        closedBorrower.CloseAccount(new DateTime(2024, 12, 31), 0m, 0m);
        context.Borrowers.Add(closedBorrower);

        context.Transactions.Add(new Transaction(
            closedBorrower.Id,
            period.Id,
            Money.Create(100000m),
            TransactionType.Withdrawal,
            new DateTime(2024, 1, 1),
            "Initial Loan Amount",
            "INIT-01"));

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        var chart = await service.GetYearlyChartDataAsync(2025);

        Assert.NotNull(chart);
        foreach (var group in chart.MonthlyGroups)
        {
            Assert.Equal(0m, group.InterestEarnedBar.Amount);
            Assert.Equal(0.0, group.InterestEarnedBar.BarHeight);
        }
    }

    [Fact]
    public async Task Test3_TwoActiveBorrowers_ShowsCombinedInterest()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("2025 Period", new DateTime(2025, 1, 1), new DateTime(2025, 12, 31));
        context.FinancialPeriods.Add(period);

        // Borrower A: 100,000 @ 3% = 3,000/mo
        var bA = new Borrower("DS 01", "Borrower A", null, null, null, new DateTime(2025, 1, 1));
        bA.SetPhotosAndLoanType(null, null, "Personal", null, null, 100000m, new DateTime(2025, 1, 1), 3.0m);
        context.Borrowers.Add(bA);

        context.Transactions.Add(new Transaction(
            bA.Id,
            period.Id,
            Money.Create(100000m),
            TransactionType.Withdrawal,
            new DateTime(2025, 1, 1),
            "Initial Loan Amount",
            "INIT-A"));

        // Borrower B: 50,000 @ 2% = 1,000/mo
        var bB = new Borrower("DS 02", "Borrower B", null, null, null, new DateTime(2025, 1, 1));
        bB.SetPhotosAndLoanType(null, null, "Personal", null, null, 50000m, new DateTime(2025, 1, 1), 2.0m);
        context.Borrowers.Add(bB);

        context.Transactions.Add(new Transaction(
            bB.Id,
            period.Id,
            Money.Create(50000m),
            TransactionType.Withdrawal,
            new DateTime(2025, 1, 1),
            "Initial Loan Amount",
            "INIT-B"));

        // Borrower C: Closed, 200,000 @ 5% => Excluded
        var bC = new Borrower("DS 03", "Borrower C", null, null, null, new DateTime(2025, 1, 1));
        bC.SetPhotosAndLoanType(null, null, "Personal", null, null, 200000m, new DateTime(2025, 1, 1), 5.0m);
        bC.CloseAccount(new DateTime(2025, 1, 1), 0m, 0m);
        context.Borrowers.Add(bC);

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        var chart = await service.GetYearlyChartDataAsync(2025);

        Assert.NotNull(chart);
        // Combined interest: 3,000 + 1,000 = 4,000
        foreach (var group in chart.MonthlyGroups)
        {
            Assert.Equal(4000m, group.InterestEarnedBar.Amount);
            Assert.True(group.InterestEarnedBar.BarHeight > 0);
        }
    }

    [Fact]
    public async Task Test4_ActiveBorrowerPayment_ReducesSubsequentInterest()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("2025 Period", new DateTime(2025, 1, 1), new DateTime(2025, 12, 31));
        context.FinancialPeriods.Add(period);

        var borrower = new Borrower("DS 01", "Borrower", null, null, null, new DateTime(2025, 1, 1));
        borrower.SetPhotosAndLoanType(null, null, "Personal", null, null, 100000m, new DateTime(2025, 1, 1), 3.0m);
        context.Borrowers.Add(borrower);

        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(100000m),
            TransactionType.Withdrawal,
            new DateTime(2025, 1, 1),
            "Initial Loan Amount",
            "INIT-01"));

        // Payment of 25,000 on 1st March 2025
        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(25000m),
            TransactionType.Deposit,
            new DateTime(2025, 3, 1),
            "Repayment",
            "REC-01"));

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        var chart = await service.GetYearlyChartDataAsync(2025);

        // January (Month 1): 100,000 * 3% = 3,000
        Assert.Equal(3000m, chart.MonthlyGroups[0].InterestEarnedBar.Amount);
        // February (Month 2): 100,000 * 3% = 3,000
        Assert.Equal(3000m, chart.MonthlyGroups[1].InterestEarnedBar.Amount);
        // After March 1 payment: Principal becomes reduced, so subsequent months have reduced interest
        Assert.True(chart.MonthlyGroups[3].InterestEarnedBar.Amount < 3000m);
    }

    [Fact]
    public async Task Test5_BorrowerBecomesClosed_NoFurtherInterestAccrues()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("2025 Period", new DateTime(2025, 1, 1), new DateTime(2025, 12, 31));
        context.FinancialPeriods.Add(period);

        var borrower = new Borrower("DS 01", "Closing Borrower", null, null, null, new DateTime(2025, 1, 1));
        borrower.SetPhotosAndLoanType(null, null, "Personal", null, null, 100000m, new DateTime(2025, 1, 1), 3.0m);
        context.Borrowers.Add(borrower);

        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(100000m),
            TransactionType.Withdrawal,
            new DateTime(2025, 1, 1),
            "Initial Loan Amount",
            "INIT-01"));

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        // When active: interest is present
        var activeChart = await service.GetYearlyChartDataAsync(2025);
        Assert.Equal(3000m, activeChart.MonthlyGroups[0].InterestEarnedBar.Amount);

        // Close borrower
        borrower.CloseAccount(new DateTime(2025, 6, 1));
        await context.SaveChangesAsync();

        // After closure: closed borrower is excluded from outstanding interest
        var closedChart = await service.GetYearlyChartDataAsync(2025);
        Assert.All(closedChart.MonthlyGroups, g => Assert.Equal(0m, g.InterestEarnedBar.Amount));
    }

    [Fact]
    public async Task Test6_YearWithNoActiveBorrowers_ReturnsZeroInterest()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("2026 Period", new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
        context.FinancialPeriods.Add(period);

        var borrower = new Borrower("DS 01", "Future Borrower", null, null, null, new DateTime(2026, 1, 1));
        borrower.SetPhotosAndLoanType(null, null, "Personal", null, null, 100000m, new DateTime(2026, 1, 1), 3.0m);
        context.Borrowers.Add(borrower);

        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(100000m),
            TransactionType.Withdrawal,
            new DateTime(2026, 1, 1),
            "Initial Loan Amount",
            "INIT-01"));

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        // Query 2024 (prior to loan)
        var chart2024 = await service.GetYearlyChartDataAsync(2024);
        Assert.All(chart2024.MonthlyGroups, g => Assert.Equal(0m, g.InterestEarnedBar.Amount));
    }

    [Fact]
    public async Task Test7_ConsistencyCheck_OutstandingOverviewMatchesInterestPage()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("2025 Period", new DateTime(2025, 1, 1), new DateTime(2025, 12, 31));
        context.FinancialPeriods.Add(period);

        var borrower = new Borrower("DS 01", "Consistency Borrower", null, null, null, new DateTime(2025, 1, 1));
        borrower.SetPhotosAndLoanType(null, null, "Personal", null, null, 100000m, new DateTime(2025, 1, 1), 3.0m);
        context.Borrowers.Add(borrower);

        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(100000m),
            TransactionType.Withdrawal,
            new DateTime(2025, 1, 1),
            "Initial Loan Amount",
            "INIT-01"));

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var dashboardService = scope.ServiceProvider.GetRequiredService<IDashboardService>();
        var interestService = scope.ServiceProvider.GetRequiredService<DhirDhar.Application.Interest.IInterestCalculationService>();

        // Chart interest for 2025
        var chart = await dashboardService.GetYearlyChartDataAsync(2025);
        var totalChartInterest = chart.MonthlyGroups.Sum(g => g.InterestEarnedBar.Amount);

        // Interest page authoritative calculation for 2025-12-31
        var interestPageResult = await interestService.CalculateAsync(borrower.Id, new DateTime(2025, 12, 31));

        Assert.Equal(interestPageResult.TotalInterest, totalChartInterest);
    }

    [Fact]
    public async Task Test8_VerifyJanuaryThroughDecember_AllocatedToCorrectMonths()
    {
        using var temp = new Persistence.TempDatabase();
        await using var context = new DhirDharDbContext(temp.CreateOptions());
        await context.Database.EnsureCreatedAsync();

        var period = new FinancialPeriod("2025 Period", new DateTime(2025, 1, 1), new DateTime(2025, 12, 31));
        context.FinancialPeriods.Add(period);

        var borrower = new Borrower("DS 01", "Annual Borrower", null, null, null, new DateTime(2025, 1, 1));
        borrower.SetPhotosAndLoanType(null, null, "Personal", null, null, 100000m, new DateTime(2025, 1, 1), 3.0m);
        context.Borrowers.Add(borrower);

        context.Transactions.Add(new Transaction(
            borrower.Id,
            period.Id,
            Money.Create(100000m),
            TransactionType.Withdrawal,
            new DateTime(2025, 1, 1),
            "Initial Loan Amount",
            "INIT-01"));

        await context.SaveChangesAsync();

        using var provider = BuildProvider(temp.CreateDatabaseOptions());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IDashboardService>();

        var chart = await service.GetYearlyChartDataAsync(2025);

        Assert.Equal(12, chart.MonthlyGroups.Count);
        for (int m = 1; m <= 12; m++)
        {
            var group = chart.MonthlyGroups[m - 1];
            Assert.Equal(m, group.Month);
            Assert.Equal(3000m, group.InterestEarnedBar.Amount);
            Assert.True(group.InterestEarnedBar.BarHeight > 0);
        }
    }
}
