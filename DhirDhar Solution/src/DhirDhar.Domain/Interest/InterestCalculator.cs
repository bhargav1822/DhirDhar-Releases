using System;
using System.Collections.Generic;
using System.Linq;
using DhirDhar.Domain.Common;
using DhirDhar.Domain.Enums;

namespace DhirDhar.Domain.Interest;

public static class InterestCalculator
{
    public const int FixedDaysInMonthBasis = 30;

    public static (decimal RawInterest, int ApplicableDays, int DaysInMonth, bool IsFullMonth) CalculateMonthSegment(
        decimal principal,
        decimal monthlyRatePercent,
        DateTime segmentStart,
        DateTime segmentEnd)
    {
        if (principal <= 0m || monthlyRatePercent <= 0m || segmentStart.Date >= segmentEnd.Date)
        {
            return (0m, 0, FixedDaysInMonthBasis, false);
        }

        const int daysInMonth = FixedDaysInMonthBasis;
        var start = segmentStart.Date;
        var end = segmentEnd.Date;

        bool isFullMonth = (end == start.AddMonths(1)) ||
                           (end == start.AddMonths(1).AddDays(-1)) ||
                           (start.Day == 1 && end == new DateTime(start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month)));

        int applicableDays;
        if (isFullMonth)
        {
            applicableDays = FixedDaysInMonthBasis; // Always exactly 30 days
        }
        else
        {
            applicableDays = (int)(end - start).TotalDays;
        }

        decimal rawInterest = (principal * (monthlyRatePercent / 100m) * (decimal)applicableDays) / (decimal)FixedDaysInMonthBasis;

        return (rawInterest, applicableDays, daysInMonth, isFullMonth);
    }

    public static InterestCalculationResult Calculate(
        Guid borrowerId,
        decimal startingPrincipal,
        DateTime startDate,
        DateTime requestedEndDate,
        IReadOnlyList<FinancialEvent> events,
        IReadOnlyList<InterestRatePeriod> ratePeriods,
        AccountStatus accountStatus,
        DateTime? closedDate)
    {
        var effectiveEndDate = GetEffectiveEndDate(requestedEndDate, closedDate, accountStatus).Date;
        var currentStartDate = startDate.Date;

        var orderedEvents = events
            .Where(e => e.Date.Date >= currentStartDate && e.Date.Date <= effectiveEndDate)
            .OrderBy(e => e.Date.Date)
            .ThenBy(e => e.SequenceOrder)
            .ToList();

        var remainingEvents = new Queue<FinancialEvent>(orderedEvents);
        var segments = new List<InterestCalculationSegment>();
        var currentPrincipal = startingPrincipal;

        // Process any events occurring exactly on currentStartDate (e.g. Initial Loan / Withdrawal / Deposit on start date)
        var eventsOnStart = new List<FinancialEvent>();
        while (remainingEvents.Count > 0 && remainingEvents.Peek().Date.Date == currentStartDate)
        {
            eventsOnStart.Add(remainingEvents.Dequeue());
        }

        foreach (var evt in eventsOnStart)
        {
            var opBalance = FinancialRounding.RoundMonetary(currentPrincipal);
            if (string.Equals(evt.Type, "Deposit", StringComparison.OrdinalIgnoreCase))
            {
                currentPrincipal = Math.Max(0m, currentPrincipal - evt.Amount);
            }
            else if (string.Equals(evt.Type, "Withdrawal", StringComparison.OrdinalIgnoreCase))
            {
                currentPrincipal = currentPrincipal + evt.Amount;
            }

            currentPrincipal = FinancialRounding.RoundMonetary(currentPrincipal);

            segments.Add(new InterestCalculationSegment(
                currentStartDate,
                currentStartDate,
                opBalance,
                GetApplicableRate(currentStartDate, ratePeriods),
                0,
                FixedDaysInMonthBasis,
                0m,
                evt.Type,
                evt.Amount,
                currentPrincipal));
        }

        if (currentStartDate >= effectiveEndDate)
        {
            currentPrincipal = FinancialRounding.RoundMonetary(currentPrincipal);
            return new InterestCalculationResult(
                borrowerId,
                currentStartDate,
                effectiveEndDate,
                startingPrincipal,
                currentPrincipal,
                0m,
                accountStatus.ToString(),
                accountStatus == AccountStatus.Closed,
                closedDate,
                segments,
                0m);
        }

        // Epoch tracking:
        // An epoch starts at currentEpochStartDate with active principal currentPrincipal.
        // It advances milestone by milestone (monthly calculation boundaries, rate changes, or real events).
        // Monthly calculation boundaries do NOT reset currentPrincipal or reset the cycle.
        // ONLY Deposit / Receive and Withdrawal / Give constitute real events that reset the epoch start and principal.
        var currentPosition = currentStartDate;
        var currentEpochStartDate = currentStartDate;
        var monthsCompletedInEpoch = 0;
        var accruedInterestInEpoch = 0m;

        while (currentPosition < effectiveEndDate)
        {
            var currentRate = GetApplicableRate(currentPosition, ratePeriods);

            // Determine milestone targetDate
            // 1. Next monthly calculation boundary in current epoch
            var nextMonthlyBoundary = currentEpochStartDate.AddMonths(monthsCompletedInEpoch + 1);

            // 2. Next event date
            DateTime? nextEventDate = remainingEvents.Count > 0 ? remainingEvents.Peek().Date.Date : null;

            // 3. Next rate change date
            DateTime? nextRateChangeDate = null;
            foreach (var rp in ratePeriods)
            {
                if (rp.EffectiveFrom.Date > currentPosition && rp.EffectiveFrom.Date <= effectiveEndDate)
                {
                    if (nextRateChangeDate == null || rp.EffectiveFrom.Date < nextRateChangeDate.Value)
                    {
                        nextRateChangeDate = rp.EffectiveFrom.Date;
                    }
                }
            }

            var targetDate = effectiveEndDate;
            if (nextMonthlyBoundary < targetDate)
            {
                targetDate = nextMonthlyBoundary;
            }
            if (nextEventDate.HasValue && nextEventDate.Value < targetDate)
            {
                targetDate = nextEventDate.Value;
            }
            if (nextRateChangeDate.HasValue && nextRateChangeDate.Value < targetDate)
            {
                targetDate = nextRateChangeDate.Value;
            }

            // Calculate interest for this segment [currentPosition, targetDate]
            bool isEpochMonthlyStep = (targetDate == nextMonthlyBoundary) &&
                                     (currentPosition == currentEpochStartDate.AddMonths(monthsCompletedInEpoch));

            decimal rawInterest;
            int applicableDays;
            int daysInMonth = FixedDaysInMonthBasis;

            if (isEpochMonthlyStep)
            {
                applicableDays = FixedDaysInMonthBasis; // Fixed 30/30
                rawInterest = currentPrincipal * (currentRate / 100m);
            }
            else
            {
                var segRes = CalculateMonthSegment(currentPrincipal, currentRate, currentPosition, targetDate);
                rawInterest = segRes.RawInterest;
                applicableDays = segRes.ApplicableDays;
                daysInMonth = segRes.DaysInMonth;
            }

            var roundedInterest = FinancialRounding.RoundMonetary(rawInterest);
            accruedInterestInEpoch += roundedInterest;

            // Check if any real events occur at targetDate
            var eventsOnTarget = new List<FinancialEvent>();
            while (remainingEvents.Count > 0 && remainingEvents.Peek().Date.Date == targetDate)
            {
                eventsOnTarget.Add(remainingEvents.Dequeue());
            }

            if (eventsOnTarget.Count > 0)
            {
                // A REAL EVENT OCCURRED:
                // 1. Calculate complete accumulated interest from previous real event date up to this event date.
                // 2. Amount Before Event = Current Principal + Accumulated Interest
                // 3. New Principal = Amount Before Event +/- Transaction Amount
                // 4. Target date becomes the NEW interest start date.
                for (int i = 0; i < eventsOnTarget.Count; i++)
                {
                    var evt = eventsOnTarget[i];
                    decimal interestForSegment = (i == 0) ? roundedInterest : 0m;
                    int daysForSegment = (i == 0) ? applicableDays : 0;
                    DateTime segStart = (i == 0) ? currentPosition : targetDate;
                    decimal segOpening = (i == 0) ? currentPrincipal : currentPrincipal;

                    var amountBeforeEvent = currentPrincipal + (i == 0 ? accruedInterestInEpoch : 0m);

                    decimal newPrincipal;
                    if (string.Equals(evt.Type, "Deposit", StringComparison.OrdinalIgnoreCase))
                    {
                        newPrincipal = Math.Max(0m, amountBeforeEvent - evt.Amount);
                    }
                    else if (string.Equals(evt.Type, "Withdrawal", StringComparison.OrdinalIgnoreCase))
                    {
                        newPrincipal = amountBeforeEvent + evt.Amount;
                    }
                    else
                    {
                        newPrincipal = amountBeforeEvent;
                    }

                    newPrincipal = FinancialRounding.RoundMonetary(newPrincipal);

                    segments.Add(new InterestCalculationSegment(
                        segStart,
                        targetDate,
                        segOpening,
                        currentRate,
                        daysForSegment,
                        FixedDaysInMonthBasis,
                        interestForSegment,
                        evt.Type,
                        evt.Amount,
                        newPrincipal));

                    currentPrincipal = newPrincipal;
                }

                // Reset epoch for subsequent calculations
                currentEpochStartDate = targetDate;
                monthsCompletedInEpoch = 0;
                accruedInterestInEpoch = 0m;
            }
            else
            {
                // NO EVENT OCCURRED:
                // Monthly calculation boundary, rate change, or effectiveEndDate.
                // Do NOT reset principal. Do NOT reset epoch cycle.
                if (applicableDays > 0 || roundedInterest > 0m || segments.Count == 0)
                {
                    segments.Add(new InterestCalculationSegment(
                        currentPosition,
                        targetDate,
                        currentPrincipal,
                        currentRate,
                        applicableDays,
                        FixedDaysInMonthBasis,
                        roundedInterest,
                        null,
                        null,
                        currentPrincipal));
                }

                if (targetDate == nextMonthlyBoundary)
                {
                    monthsCompletedInEpoch++;
                }
            }

            currentPosition = targetDate;
        }

        currentPrincipal = FinancialRounding.RoundMonetary(currentPrincipal);
        var totalInterest = FinancialRounding.RoundMonetary(segments.Sum(s => s.CalculatedInterest));
        var uncapitalizedInterest = FinancialRounding.RoundMonetary(accruedInterestInEpoch);

        return new InterestCalculationResult(
            borrowerId,
            startDate.Date,
            effectiveEndDate,
            startingPrincipal,
            currentPrincipal,
            totalInterest,
            accountStatus.ToString(),
            accountStatus == AccountStatus.Closed,
            closedDate,
            segments,
            uncapitalizedInterest);
    }

    public static DateTime GetEffectiveEndDate(
        DateTime requestedEndDate,
        DateTime? closedDate,
        AccountStatus accountStatus)
    {
        if (accountStatus == AccountStatus.Closed && closedDate.HasValue)
        {
            return requestedEndDate < closedDate.Value
                ? requestedEndDate
                : closedDate.Value;
        }

        return requestedEndDate;
    }

    public static decimal CalculatePeriodInterest(
        decimal principal,
        DateTime startDate,
        DateTime endDate,
        IReadOnlyList<InterestRatePeriod> ratePeriods)
    {
        if (principal <= 0m || startDate >= endDate)
        {
            return 0m;
        }

        var result = Calculate(
            Guid.Empty,
            principal,
            startDate,
            endDate,
            Array.Empty<FinancialEvent>(),
            ratePeriods,
            AccountStatus.Active,
            null);

        return result.TotalInterest;
    }

    private static decimal GetApplicableRate(DateTime date, IReadOnlyList<InterestRatePeriod> ratePeriods)
    {
        foreach (var period in ratePeriods)
        {
            if (date >= period.EffectiveFrom &&
                (!period.EffectiveTo.HasValue || date <= period.EffectiveTo.Value))
            {
                return period.MonthlyRatePercent;
            }
        }

        return 0m;
    }
}
