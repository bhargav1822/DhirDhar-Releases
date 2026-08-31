using System;
using System.Collections.Generic;
using System.Linq;
using DhirDhar.Domain.Enums;
using DhirDhar.Domain.Interest;
using Xunit;

namespace DhirDhar.Domain.Tests.Interest;

public class InterestCalculatorTests
{
    private static InterestRatePeriod CreateRate(decimal rate, DateTime from, DateTime? to = null)
    {
        return new InterestRatePeriod(rate, from, to);
    }

    private static FinancialEvent CreateEvent(DateTime date, string type, decimal amount, int sequence = 0, string? description = null)
    {
        return new FinancialEvent(date, type, amount, description, sequence);
    }

    // =========================================================================
    // 16 REQUIRED TEST SUITE SCENARIOS (A through P) & SECTION 16 REQUIREMENTS
    // =========================================================================

    [Fact]
    public void Test_A_LoanWithNoEventsForSeveralMonths_CalculatesContinuouslyWithoutResettingPrincipal()
    {
        // Loan: ₹10,000, Rate: 3%, Start: 20/08/2026, End: 20/11/2026 (3 complete months)
        // Verify:
        // - Does NOT treat 20/09, 20/10 as events
        // - Principal remains ₹10,000 at each monthly boundary (does not compound)
        // - Interest accumulates: Month 1 = ₹300, Month 2 = ₹300, Month 3 = ₹300 -> Total = ₹900
        // - Total outstanding at end = ₹10,000 + ₹900 = ₹10,900
        var startDate = new DateTime(2026, 8, 20);
        var endDate = new DateTime(2026, 11, 20);
        var principal = 10000m;
        var rate = CreateRate(3.0m, startDate);

        var result = InterestCalculator.Calculate(Guid.NewGuid(), principal, startDate, endDate, [], [rate], AccountStatus.Active, null);

        Assert.Equal(3, result.Segments.Count);

        // Segment 1: 20/08 -> 20/09 (30/30)
        var seg1 = result.Segments[0];
        Assert.Equal(new DateTime(2026, 8, 20), seg1.SegmentStartDate);
        Assert.Equal(new DateTime(2026, 9, 20), seg1.SegmentEndDate);
        Assert.Equal(30, seg1.ElapsedDays);
        Assert.Equal(10000m, seg1.OpeningPrincipal);
        Assert.Equal(300.00m, seg1.CalculatedInterest);
        Assert.Null(seg1.TransactionType);
        Assert.Equal(10000m, seg1.ClosingPrincipal); // Monthly boundary does NOT reset principal

        // Segment 2: 20/09 -> 20/10 (30/30)
        var seg2 = result.Segments[1];
        Assert.Equal(new DateTime(2026, 9, 20), seg2.SegmentStartDate);
        Assert.Equal(new DateTime(2026, 10, 20), seg2.SegmentEndDate);
        Assert.Equal(30, seg2.ElapsedDays);
        Assert.Equal(10000m, seg2.OpeningPrincipal);
        Assert.Equal(300.00m, seg2.CalculatedInterest);
        Assert.Null(seg2.TransactionType);
        Assert.Equal(10000m, seg2.ClosingPrincipal);

        // Segment 3: 20/10 -> 20/11 (30/30)
        var seg3 = result.Segments[2];
        Assert.Equal(new DateTime(2026, 10, 20), seg3.SegmentStartDate);
        Assert.Equal(new DateTime(2026, 11, 20), seg3.SegmentEndDate);
        Assert.Equal(30, seg3.ElapsedDays);
        Assert.Equal(10000m, seg3.OpeningPrincipal);
        Assert.Equal(300.00m, seg3.CalculatedInterest);
        Assert.Null(seg3.TransactionType);
        Assert.Equal(10000m, seg3.ClosingPrincipal);

        Assert.Equal(900.00m, result.TotalInterest);
        Assert.Equal(10000m, result.ClosingPrincipal);
        Assert.Equal(900.00m, result.UncapitalizedInterest);
        Assert.Equal(10900.00m, result.TotalOutstanding);
    }

    [Fact]
    public void Test_B_Section16_LoanAndDepositAfterSeveralMonths_ExactUserPromptExample()
    {
        // SPECIFIC REQUIRED TEST EXAMPLE (Sections 5 & 16 of prompt):
        // Loan: ₹10,000, Rate: 3%, Start: 20/08/2026
        // Deposit: ₹2,000 on 05/12/2026
        //
        // Continuous calculation without treating 20/09, 20/10, 20/11 as events:
        // 20/08 -> 20/09: Month 1 = ₹10,000 × 3% = ₹300
        // 20/09 -> 20/10: Month 2 = ₹10,000 × 3% = ₹300
        // 20/10 -> 20/11: Month 3 = ₹10,000 × 3% = ₹300
        // 20/11 -> 05/12: Partial (15 days) = ₹10,000 × 3% × 15/30 = ₹150
        // Total accumulated interest = ₹1,050
        // Amount before event = ₹10,000 + ₹1,050 = ₹11,050
        // Deposit: −₹2,000
        // New Principal = ₹9,050
        // New Interest Start Date = 05/12/2026
        var loanDate = new DateTime(2026, 8, 20);
        var depositDate = new DateTime(2026, 12, 5);
        var principal = 10000m;
        var rate = CreateRate(3.0m, loanDate);

        var events = new List<FinancialEvent>
        {
            CreateEvent(depositDate, "Deposit", 2000m, 1)
        };

        var result = InterestCalculator.Calculate(Guid.NewGuid(), principal, loanDate, depositDate, events, [rate], AccountStatus.Active, null);

        Assert.Equal(4, result.Segments.Count);

        // Seg 1: 20/08 -> 20/09
        Assert.Equal(new DateTime(2026, 8, 20), result.Segments[0].SegmentStartDate);
        Assert.Equal(new DateTime(2026, 9, 20), result.Segments[0].SegmentEndDate);
        Assert.Equal(30, result.Segments[0].ElapsedDays);
        Assert.Equal(300.00m, result.Segments[0].CalculatedInterest);
        Assert.Equal(10000m, result.Segments[0].OpeningPrincipal);
        Assert.Null(result.Segments[0].TransactionType);
        Assert.Equal(10000m, result.Segments[0].ClosingPrincipal);

        // Seg 2: 20/09 -> 20/10
        Assert.Equal(new DateTime(2026, 9, 20), result.Segments[1].SegmentStartDate);
        Assert.Equal(new DateTime(2026, 10, 20), result.Segments[1].SegmentEndDate);
        Assert.Equal(30, result.Segments[1].ElapsedDays);
        Assert.Equal(300.00m, result.Segments[1].CalculatedInterest);
        Assert.Equal(10000m, result.Segments[1].OpeningPrincipal);
        Assert.Null(result.Segments[1].TransactionType);
        Assert.Equal(10000m, result.Segments[1].ClosingPrincipal);

        // Seg 3: 20/10 -> 20/11
        Assert.Equal(new DateTime(2026, 10, 20), result.Segments[2].SegmentStartDate);
        Assert.Equal(new DateTime(2026, 11, 20), result.Segments[2].SegmentEndDate);
        Assert.Equal(30, result.Segments[2].ElapsedDays);
        Assert.Equal(300.00m, result.Segments[2].CalculatedInterest);
        Assert.Equal(10000m, result.Segments[2].OpeningPrincipal);
        Assert.Null(result.Segments[2].TransactionType);
        Assert.Equal(10000m, result.Segments[2].ClosingPrincipal);

        // Seg 4: 20/11 -> 05/12 (Partial: 15 days)
        var seg4 = result.Segments[3];
        Assert.Equal(new DateTime(2026, 11, 20), seg4.SegmentStartDate);
        Assert.Equal(new DateTime(2026, 12, 5), seg4.SegmentEndDate);
        Assert.Equal(15, seg4.ElapsedDays);
        Assert.Equal(150.00m, seg4.CalculatedInterest);
        Assert.Equal(10000m, seg4.OpeningPrincipal);
        Assert.Equal("Deposit", seg4.TransactionType);
        Assert.Equal(2000m, seg4.TransactionAmount);
        Assert.Equal(9050.00m, seg4.ClosingPrincipal); // (10,000 + 1,050) - 2,000 = 9,050

        Assert.Equal(1050.00m, result.TotalInterest);
        Assert.Equal(9050.00m, result.ClosingPrincipal);
        Assert.Equal(0m, result.UncapitalizedInterest);
        Assert.Equal(9050.00m, result.TotalOutstanding);
    }

    [Fact]
    public void Test_C_LoanAndWithdrawalAfterSeveralMonths()
    {
        // Loan: ₹10,000 @ 3% on 20/08/2026. Withdrawal: ₹2,000 on 05/12/2026
        // Total interest from 20/08 to 05/12 = ₹1,050
        // Amount before withdrawal = ₹10,000 + ₹1,050 = ₹11,050
        // Withdrawal: +₹2,000
        // New Principal = ₹13,050
        var loanDate = new DateTime(2026, 8, 20);
        var withdrawalDate = new DateTime(2026, 12, 5);
        var principal = 10000m;
        var rate = CreateRate(3.0m, loanDate);

        var events = new List<FinancialEvent>
        {
            CreateEvent(withdrawalDate, "Withdrawal", 2000m, 1)
        };

        var result = InterestCalculator.Calculate(Guid.NewGuid(), principal, loanDate, withdrawalDate, events, [rate], AccountStatus.Active, null);

        Assert.Equal(4, result.Segments.Count);
        Assert.Equal(1050.00m, result.TotalInterest);
        Assert.Equal(13050.00m, result.ClosingPrincipal);
        Assert.Equal(0m, result.UncapitalizedInterest);
        Assert.Equal(13050.00m, result.TotalOutstanding);
    }

    [Fact]
    public void Test_D_DepositFollowedByWithdrawal_Section6Example()
    {
        // Section 6 from user prompt:
        // Loan: ₹10,000 @ 3% on 20/08/2026
        // Deposit: ₹2,000 on 05/12/2026 -> New Principal = ₹9,050 (Start = 05/12/2026)
        // Next transaction: Withdrawal ₹1,000 on 15/01/2027
        //
        // From 05/12/2026 -> 15/01/2027 using new principal ₹9,050:
        // 05/12/2026 -> 05/01/2027: Month 1 = ₹9,050 × 3% = ₹271.50
        // 05/01/2027 -> 15/01/2027: Partial (10 days) = ₹9,050 × 3% × 10/30 = ₹90.50
        // Accumulated interest = ₹271.50 + ₹90.50 = ₹362.00
        // Amount before event = ₹9,050 + ₹362.00 = ₹9,412.00
        // Withdrawal: +₹1,000
        // New Principal = ₹9,412.00 + ₹1,000 = ₹10,412.00
        // New Interest Start Date = 15/01/2027
        var loanDate = new DateTime(2026, 8, 20);
        var depDate = new DateTime(2026, 12, 5);
        var withDate = new DateTime(2027, 1, 15);
        var principal = 10000m;
        var rate = CreateRate(3.0m, loanDate);

        var events = new List<FinancialEvent>
        {
            CreateEvent(depDate, "Deposit", 2000m, 1),
            CreateEvent(withDate, "Withdrawal", 1000m, 2)
        };

        var result = InterestCalculator.Calculate(Guid.NewGuid(), principal, loanDate, withDate, events, [rate], AccountStatus.Active, null);

        Assert.Equal(6, result.Segments.Count);

        // Epoch 0 segments (20/08 -> 05/12): 4 segments
        Assert.Equal(300m, result.Segments[0].CalculatedInterest);
        Assert.Equal(300m, result.Segments[1].CalculatedInterest);
        Assert.Equal(300m, result.Segments[2].CalculatedInterest);
        Assert.Equal(150m, result.Segments[3].CalculatedInterest);
        Assert.Equal("Deposit", result.Segments[3].TransactionType);
        Assert.Equal(9050m, result.Segments[3].ClosingPrincipal);

        // Epoch 1 segments (05/12 -> 15/01): 2 segments
        // Seg 5: 05/12/2026 -> 05/01/2027 (30/30)
        var seg5 = result.Segments[4];
        Assert.Equal(new DateTime(2026, 12, 5), seg5.SegmentStartDate);
        Assert.Equal(new DateTime(2027, 1, 5), seg5.SegmentEndDate);
        Assert.Equal(30, seg5.ElapsedDays);
        Assert.Equal(9050m, seg5.OpeningPrincipal);
        Assert.Equal(271.50m, seg5.CalculatedInterest);
        Assert.Null(seg5.TransactionType);
        Assert.Equal(9050m, seg5.ClosingPrincipal);

        // Seg 6: 05/01/2027 -> 15/01/2027 (10 days)
        var seg6 = result.Segments[5];
        Assert.Equal(new DateTime(2027, 1, 5), seg6.SegmentStartDate);
        Assert.Equal(new DateTime(2027, 1, 15), seg6.SegmentEndDate);
        Assert.Equal(10, seg6.ElapsedDays);
        Assert.Equal(9050m, seg6.OpeningPrincipal);
        Assert.Equal(90.50m, seg6.CalculatedInterest);
        Assert.Equal("Withdrawal", seg6.TransactionType);
        Assert.Equal(1000m, seg6.TransactionAmount);
        Assert.Equal(10412.00m, seg6.ClosingPrincipal); // (9,050 + 362) + 1,000 = 10,412.00

        Assert.Equal(1412.00m, result.TotalInterest); // 1,050 + 362 = 1,412.00
        Assert.Equal(10412.00m, result.ClosingPrincipal);
        Assert.Equal(0m, result.UncapitalizedInterest);
        Assert.Equal(10412.00m, result.TotalOutstanding);
    }

    [Fact]
    public void Test_E_WithdrawalFollowedByDeposit()
    {
        // Loan ₹10,000 @ 3% on 20/08/2026
        // Withdrawal ₹2,000 on 05/09/2026 (16 days):
        // Interest: ₹10,000 × 3% × 16/30 = ₹160 -> New Principal = ₹10,000 + ₹160 + ₹2,000 = ₹12,160
        // Deposit ₹1,000 on 20/09/2026 (15 days from 05/09):
        // Interest: ₹12,160 × 3% × 15/30 = ₹182.40 -> New Principal = ₹12,160 + ₹182.40 − ₹1,000 = ₹11,342.40
        var startDate = new DateTime(2026, 8, 20);
        var withDate = new DateTime(2026, 9, 5);
        var depDate = new DateTime(2026, 9, 20);
        var rate = CreateRate(3.0m, startDate);

        var events = new List<FinancialEvent>
        {
            CreateEvent(withDate, "Withdrawal", 2000m, 1),
            CreateEvent(depDate, "Deposit", 1000m, 2)
        };

        var result = InterestCalculator.Calculate(Guid.NewGuid(), 10000m, startDate, depDate, events, [rate], AccountStatus.Active, null);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(160.00m, result.Segments[0].CalculatedInterest);
        Assert.Equal(12160.00m, result.Segments[0].ClosingPrincipal);

        Assert.Equal(182.40m, result.Segments[1].CalculatedInterest);
        Assert.Equal(11342.40m, result.Segments[1].ClosingPrincipal);

        Assert.Equal(342.40m, result.TotalInterest);
        Assert.Equal(11342.40m, result.ClosingPrincipal);
        Assert.Equal(11342.40m, result.TotalOutstanding);
    }

    [Fact]
    public void Test_F_MultipleDepositsAcrossMonths()
    {
        // Loan ₹10,000 @ 3% on 20/08/2026
        // Deposit 1: ₹1,000 on 05/09/2026 (16 days) -> New Principal = ₹9,160
        // Deposit 2: ₹2,000 on 20/09/2026 (15 days) -> New Principal = (9,160 + 137.40) - 2,000 = ₹7,297.40
        var startDate = new DateTime(2026, 8, 20);
        var dep1Date = new DateTime(2026, 9, 5);
        var dep2Date = new DateTime(2026, 9, 20);
        var rate = CreateRate(3.0m, startDate);

        var events = new List<FinancialEvent>
        {
            CreateEvent(dep1Date, "Deposit", 1000m, 1),
            CreateEvent(dep2Date, "Deposit", 2000m, 2)
        };

        var result = InterestCalculator.Calculate(Guid.NewGuid(), 10000m, startDate, dep2Date, events, [rate], AccountStatus.Active, null);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(160.00m, result.Segments[0].CalculatedInterest);
        Assert.Equal(9160.00m, result.Segments[0].ClosingPrincipal);

        Assert.Equal(137.40m, result.Segments[1].CalculatedInterest);
        Assert.Equal(7297.40m, result.Segments[1].ClosingPrincipal);

        Assert.Equal(297.40m, result.TotalInterest);
        Assert.Equal(7297.40m, result.ClosingPrincipal);
        Assert.Equal(7297.40m, result.TotalOutstanding);
    }

    [Fact]
    public void Test_G_MultipleWithdrawalsAcrossMonths()
    {
        // Loan ₹10,000 @ 3% on 20/08/2026
        // Withdrawal 1: ₹1,000 on 05/09/2026 (16 days) -> New Principal = ₹11,160
        // Withdrawal 2: ₹2,000 on 20/09/2026 (15 days) -> New Principal = (11,160 + 167.40) + 2,000 = ₹13,327.40
        var startDate = new DateTime(2026, 8, 20);
        var with1Date = new DateTime(2026, 9, 5);
        var with2Date = new DateTime(2026, 9, 20);
        var rate = CreateRate(3.0m, startDate);

        var events = new List<FinancialEvent>
        {
            CreateEvent(with1Date, "Withdrawal", 1000m, 1),
            CreateEvent(with2Date, "Withdrawal", 2000m, 2)
        };

        var result = InterestCalculator.Calculate(Guid.NewGuid(), 10000m, startDate, with2Date, events, [rate], AccountStatus.Active, null);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(160.00m, result.Segments[0].CalculatedInterest);
        Assert.Equal(11160.00m, result.Segments[0].ClosingPrincipal);

        Assert.Equal(167.40m, result.Segments[1].CalculatedInterest);
        Assert.Equal(13327.40m, result.Segments[1].ClosingPrincipal);

        Assert.Equal(327.40m, result.TotalInterest);
        Assert.Equal(13327.40m, result.ClosingPrincipal);
        Assert.Equal(13327.40m, result.TotalOutstanding);
    }

    [Fact]
    public void Test_H_DepositAndWithdrawalInSameMonth()
    {
        // Loan ₹10,000 @ 3% on 20/08/2026
        // Deposit: ₹2,000 on 05/09/2026 (16 days) -> ₹160 interest, New Principal = ₹8,160
        // Withdrawal: ₹1,000 on 15/09/2026 (10 days on ₹8,160) -> ₹8,160 × 3% × 10/30 = ₹81.60 -> New Principal = ₹8,160 + ₹81.60 + ₹1,000 = ₹9,241.60
        var startDate = new DateTime(2026, 8, 20);
        var depDate = new DateTime(2026, 9, 5);
        var withDate = new DateTime(2026, 9, 15);
        var rate = CreateRate(3.0m, startDate);

        var events = new List<FinancialEvent>
        {
            CreateEvent(depDate, "Deposit", 2000m, 1),
            CreateEvent(withDate, "Withdrawal", 1000m, 2)
        };

        var result = InterestCalculator.Calculate(Guid.NewGuid(), 10000m, startDate, withDate, events, [rate], AccountStatus.Active, null);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(160.00m, result.Segments[0].CalculatedInterest);
        Assert.Equal(8160.00m, result.Segments[0].ClosingPrincipal);

        Assert.Equal(81.60m, result.Segments[1].CalculatedInterest);
        Assert.Equal(9241.60m, result.Segments[1].ClosingPrincipal);

        Assert.Equal(241.60m, result.TotalInterest);
        Assert.Equal(9241.60m, result.ClosingPrincipal);
        Assert.Equal(9241.60m, result.TotalOutstanding);
    }

    [Fact]
    public void Test_I_DepositAndWithdrawalOnSameDate()
    {
        // Loan ₹10,000 @ 3% on 20/08/2026
        // On 05/09/2026 (16 days, ₹160 interest):
        // Event 1: Deposit ₹2,000 -> New Principal = (10,000 + 160) - 2,000 = ₹8,160
        // Event 2: Withdrawal ₹500 -> New Principal = 8,160 + 0 + 500 = ₹8,660
        var startDate = new DateTime(2026, 8, 20);
        var eventDate = new DateTime(2026, 9, 5);
        var rate = CreateRate(3.0m, startDate);

        var events = new List<FinancialEvent>
        {
            CreateEvent(eventDate, "Deposit", 2000m, 1),
            CreateEvent(eventDate, "Withdrawal", 500m, 2)
        };

        var result = InterestCalculator.Calculate(Guid.NewGuid(), 10000m, startDate, eventDate, events, [rate], AccountStatus.Active, null);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(160.00m, result.Segments[0].CalculatedInterest);
        Assert.Equal(8160.00m, result.Segments[0].ClosingPrincipal);

        Assert.Equal(0m, result.Segments[1].CalculatedInterest);
        Assert.Equal(8660.00m, result.Segments[1].ClosingPrincipal);

        Assert.Equal(160.00m, result.TotalInterest);
        Assert.Equal(8660.00m, result.ClosingPrincipal);
        Assert.Equal(8660.00m, result.TotalOutstanding);
    }

    [Fact]
    public void Test_J_EventExactlyOnMonthlyCalculationBoundary()
    {
        // Loan ₹10,000 @ 3% on 20/08/2026
        // Deposit ₹2,000 on 20/09/2026 (exact 1 month anniversary):
        // 20/08 -> 20/09: Complete month (30/30) = ₹300 interest
        // Amount before event = ₹10,000 + ₹300 = ₹10,300
        // Deposit = −₹2,000 -> New Principal = ₹8,300
        var startDate = new DateTime(2026, 8, 20);
        var depositDate = new DateTime(2026, 9, 20);
        var principal = 10000m;
        var rate = CreateRate(3.0m, startDate);

        var events = new List<FinancialEvent>
        {
            CreateEvent(depositDate, "Deposit", 2000m, 1)
        };

        var result = InterestCalculator.Calculate(Guid.NewGuid(), principal, startDate, depositDate, events, [rate], AccountStatus.Active, null);

        Assert.Single(result.Segments);
        var seg = result.Segments[0];
        Assert.Equal(30, seg.ElapsedDays);
        Assert.Equal(300.00m, seg.CalculatedInterest);
        Assert.Equal("Deposit", seg.TransactionType);
        Assert.Equal(2000m, seg.TransactionAmount);
        Assert.Equal(8300.00m, seg.ClosingPrincipal);

        Assert.Equal(300.00m, result.TotalInterest);
        Assert.Equal(8300.00m, result.ClosingPrincipal);
        Assert.Equal(8300.00m, result.TotalOutstanding);
    }

    [Fact]
    public void Test_K_EventInMiddleOfMonthlyPeriod()
    {
        // Loan ₹10,000 @ 3% on 20/08/2026
        // Deposit ₹2,000 on 05/09/2026 (16 days):
        // Interest = ₹10,000 × 3% × 16/30 = ₹160
        // New Principal = (10,000 + 160) - 2,000 = ₹8,160
        var loanDate = new DateTime(2026, 8, 20);
        var depositDate = new DateTime(2026, 9, 5);
        var principal = 10000m;
        var rate = CreateRate(3.0m, loanDate);

        var events = new List<FinancialEvent>
        {
            CreateEvent(depositDate, "Deposit", 2000m, 1)
        };

        var result = InterestCalculator.Calculate(Guid.NewGuid(), principal, loanDate, depositDate, events, [rate], AccountStatus.Active, null);

        Assert.Single(result.Segments);
        var seg = result.Segments[0];
        Assert.Equal(16, seg.ElapsedDays);
        Assert.Equal(160.00m, seg.CalculatedInterest);
        Assert.Equal(8160.00m, seg.ClosingPrincipal);
    }

    [Fact]
    public void Test_L_February_NonLeapYear_28Days_Returns30Over30()
    {
        // Complete February 2025 (28 calendar days): Must be 30/30 = 100% monthly interest
        // ₹50,000 @ 3% / month: ₹50,000 × 3% × 30/30 = ₹1,500.00
        var principal = 50000m;
        var ratePercent = 3.0m;
        var startDate = new DateTime(2025, 2, 1);
        var endDate = new DateTime(2025, 2, 28);

        var (rawInterest, applicableDays, daysInMonth, isFullMonth) = InterestCalculator.CalculateMonthSegment(
            principal, ratePercent, startDate, endDate);

        Assert.True(isFullMonth);
        Assert.Equal(30, applicableDays);
        Assert.Equal(30, daysInMonth);
        Assert.Equal(1500.00m, rawInterest);

        var result = InterestCalculator.Calculate(Guid.NewGuid(), principal, startDate, endDate, [], [CreateRate(ratePercent, startDate)], AccountStatus.Active, null);
        Assert.Equal(1500.00m, result.TotalInterest);
        Assert.Single(result.Segments);
        Assert.Equal(30, result.Segments[0].ElapsedDays);
        Assert.Equal(30, result.Segments[0].DaysInMonth);
    }

    [Fact]
    public void Test_M_February_LeapYear_29Days_Returns30Over30()
    {
        // Complete February 2024 (29 calendar days, leap year): Must be 30/30 = 100% monthly interest
        // ₹50,000 @ 3% / month: ₹50,000 × 3% × 30/30 = ₹1,500.00
        var principal = 50000m;
        var ratePercent = 3.0m;
        var startDate = new DateTime(2024, 2, 1);
        var endDate = new DateTime(2024, 2, 29);

        var (rawInterest, applicableDays, daysInMonth, isFullMonth) = InterestCalculator.CalculateMonthSegment(
            principal, ratePercent, startDate, endDate);

        Assert.True(isFullMonth);
        Assert.Equal(30, applicableDays);
        Assert.Equal(30, daysInMonth);
        Assert.Equal(1500.00m, rawInterest);

        var result = InterestCalculator.Calculate(Guid.NewGuid(), principal, startDate, endDate, [], [CreateRate(ratePercent, startDate)], AccountStatus.Active, null);
        Assert.Equal(1500.00m, result.TotalInterest);
        Assert.Single(result.Segments);
        Assert.Equal(30, result.Segments[0].ElapsedDays);
        Assert.Equal(30, result.Segments[0].DaysInMonth);
    }

    [Fact]
    public void Test_N_31DayMonth_AugustAndDecember_Returns30Over30()
    {
        // Complete August 2024 (31 calendar days): Must be 30/30 = ₹1,500.00
        var principal = 50000m;
        var ratePercent = 3.0m;
        var startDate = new DateTime(2024, 8, 1);
        var endDate = new DateTime(2024, 8, 31);

        var (rawAugust, daysAugust, dimAugust, fullAugust) = InterestCalculator.CalculateMonthSegment(
            principal, ratePercent, startDate, endDate);

        Assert.True(fullAugust);
        Assert.Equal(30, daysAugust);
        Assert.Equal(30, dimAugust);
        Assert.Equal(1500.00m, rawAugust);

        // Complete December 2024 (31 calendar days): Must be 30/30 = ₹1,500.00
        var (rawDec, daysDec, dimDec, fullDec) = InterestCalculator.CalculateMonthSegment(
            principal, ratePercent, new DateTime(2024, 12, 1), new DateTime(2024, 12, 31));

        Assert.True(fullDec);
        Assert.Equal(30, daysDec);
        Assert.Equal(30, dimDec);
        Assert.Equal(1500.00m, rawDec);
    }

    [Fact]
    public void Test_O_AccountClosureWithNoPreviousEvent()
    {
        // Loan ₹10,000 @ 3% on 20/08/2026. Account closed on 05/09/2026 (16 days).
        // Calculation requested for far in future (31/12/2026).
        // Result must halt at closedDate (05/09/2026):
        // Interest: ₹10,000 × 3% × 16/30 = ₹160
        // ClosingPrincipal = ₹10,000
        // TotalOutstanding = ₹10,160
        var startDate = new DateTime(2026, 8, 20);
        var closeDate = new DateTime(2026, 9, 5);
        var futureDate = new DateTime(2026, 12, 31);
        var principal = 10000m;
        var rate = CreateRate(3.0m, startDate);

        var result = InterestCalculator.Calculate(Guid.NewGuid(), principal, startDate, futureDate, [], [rate], AccountStatus.Closed, closeDate);

        Assert.Single(result.Segments);
        Assert.Equal(closeDate, result.CalculationEndDate);
        Assert.True(result.IsClosed);
        Assert.Equal(16, result.Segments[0].ElapsedDays);
        Assert.Equal(160.00m, result.Segments[0].CalculatedInterest);
        Assert.Equal(10000m, result.ClosingPrincipal);
        Assert.Equal(160.00m, result.UncapitalizedInterest);
        Assert.Equal(10160.00m, result.TotalOutstanding);
    }

    [Fact]
    public void Test_P_AccountClosureAfterMultipleRealEvents()
    {
        // Loan ₹10,000 @ 3% on 20/08/2026
        // Deposit ₹2,000 on 05/12/2026 -> New Principal = ₹9,050
        // Account closed on 15/01/2027:
        // Interest from 05/12 to 15/01 (1 month + 10 days on ₹9,050) = ₹271.50 + ₹90.50 = ₹362.00
        // TotalInterest = ₹1,050 + ₹362.00 = ₹1,412.00
        // ClosingPrincipal = ₹9,050
        // TotalOutstanding = ₹9,050 + ₹362.00 = ₹9,412.00
        // Calculation requested for 31/12/2030 must stop at 15/01/2027.
        var loanDate = new DateTime(2026, 8, 20);
        var depDate = new DateTime(2026, 12, 5);
        var closeDate = new DateTime(2027, 1, 15);
        var futureDate = new DateTime(2030, 12, 31);
        var principal = 10000m;
        var rate = CreateRate(3.0m, loanDate);

        var events = new List<FinancialEvent>
        {
            CreateEvent(depDate, "Deposit", 2000m, 1)
        };

        var result = InterestCalculator.Calculate(Guid.NewGuid(), principal, loanDate, futureDate, events, [rate], AccountStatus.Closed, closeDate);

        Assert.Equal(closeDate, result.CalculationEndDate);
        Assert.True(result.IsClosed);
        Assert.Equal(6, result.Segments.Count); // 4 for epoch 0 + 2 for epoch 1
        Assert.Equal(1412.00m, result.TotalInterest);
        Assert.Equal(9050m, result.ClosingPrincipal);
        Assert.Equal(362.00m, result.UncapitalizedInterest);
        Assert.Equal(9412.00m, result.TotalOutstanding);
    }

    [Fact]
    public void Test_InterestRateChange_BetweenRealEvents()
    {
        // Start @ 3% on 01/01/2026, Rate changes to 4% on 01/02/2026, requested end 01/03/2026
        // No deposit/withdrawal transactions:
        // Month 1 @ 3%: ₹10,000 × 3% = ₹300 (Opening: 10,000, Closing: 10,000)
        // Month 2 @ 4%: ₹10,000 × 4% = ₹400 (Opening: 10,000, Closing: 10,000)
        // Total Interest = ₹700. ClosingPrincipal = ₹10,000. TotalOutstanding = ₹10,700
        var startDate = new DateTime(2026, 1, 1);
        var rateChangeDate = new DateTime(2026, 2, 1);
        var endDate = new DateTime(2026, 3, 1);
        var principal = 10000m;

        var rates = new List<InterestRatePeriod>
        {
            new(3.0m, startDate, new DateTime(2026, 1, 31)),
            new(4.0m, rateChangeDate, null)
        };

        var result = InterestCalculator.Calculate(Guid.NewGuid(), principal, startDate, endDate, [], rates, AccountStatus.Active, null);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(3.0m, result.Segments[0].ApplicableMonthlyRate);
        Assert.Equal(300.00m, result.Segments[0].CalculatedInterest);
        Assert.Equal(10000m, result.Segments[0].ClosingPrincipal);

        Assert.Equal(4.0m, result.Segments[1].ApplicableMonthlyRate);
        Assert.Equal(400.00m, result.Segments[1].CalculatedInterest);
        Assert.Equal(10000m, result.Segments[1].ClosingPrincipal);

        Assert.Equal(700.00m, result.TotalInterest);
        Assert.Equal(10000m, result.ClosingPrincipal);
        Assert.Equal(10700.00m, result.TotalOutstanding);
    }
}
