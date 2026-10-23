using Meridian.Contracts.DirectLending;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster.Schedules;

/// <summary>
/// Frozen copies of the legacy schedule generators the contractual schedule kernel replaces, taken
/// from base commit <c>7d072638</c>. Each copy keeps its source body as it was, adapting only what a
/// static test helper needs: instance state and the surrounding projection become parameters, and
/// the amount, DTO and payload work around the date loop is dropped. The parity and fidelity tests
/// compare the kernel (and, by reflection, the production generators) against these copies.
/// </summary>
/// <remarks>
/// The copies are never edited. Each is deleted once the generator it copies is retired onto the
/// kernel and its parity grid has been kept green across that change.
/// </remarks>
internal static class LegacyScheduleOracles
{
    /// <summary>
    /// The Security Master coupon-period roll: copied from
    /// <c>src/Meridian.Application/SecurityMaster/SecurityMasterCashFlowService.cs:728-755</c> at
    /// <c>7d072638</c> (<c>BuildPaymentPeriods</c>), unchanged. Every payment date is anchored on the
    /// issue date; the first anchored date on or after maturity closes the schedule at maturity.
    /// </summary>
    public static IEnumerable<(DateOnly AccrualStart, DateOnly PaymentDate)> SmIssueAnchored(
        DateOnly issueDate,
        DateOnly maturity,
        int periodMonths)
    {
        // A non-positive period would never advance the anchor and would spin forever. Both callers
        // clamp with Math.Max(1, ...), so this guards the contract rather than a reachable path.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(periodMonths);

        if (issueDate >= maturity)
        {
            yield break;
        }

        var accrualStart = issueDate;
        for (var period = 1; ; period++)
        {
            var paymentDate = issueDate.AddMonths(periodMonths * period);
            if (paymentDate >= maturity)
            {
                yield return (accrualStart, maturity);
                yield break;
            }

            yield return (accrualStart, paymentDate);
            accrualStart = paymentDate;
        }
    }

    /// <summary>
    /// The asset-operations bond coupon roll, date part only: copied from
    /// <c>src/Meridian.Instruments/AssetOperations/AssetOperationsReadService.cs:437-445</c> and the
    /// loop tail at <c>:560-565</c> at <c>7d072638</c> (<c>BuildBondProjectedCashFlows</c>). Each
    /// accrual end compounds from the previous one and is capped at maturity. The coupon, sinking-fund
    /// and maturity-principal work between them is dropped; <paramref name="periodMonths"/> is the
    /// value <c>:427</c> derives.
    /// </summary>
    public static IReadOnlyList<(DateOnly AccrualStart, DateOnly AccrualEnd)> AssetOpsCompounded(
        DateOnly issueDate,
        DateOnly maturity,
        int periodMonths)
    {
        var periods = new List<(DateOnly AccrualStart, DateOnly AccrualEnd)>();
        var accrualStart = issueDate;

        while (accrualStart < maturity)
        {
            var accrualEnd = accrualStart.AddMonths(periodMonths);
            if (accrualEnd > maturity)
            {
                accrualEnd = maturity;
            }

            periods.Add((accrualStart, accrualEnd));

            if (accrualEnd == maturity)
            {
                break;
            }

            accrualStart = accrualEnd;
        }

        return periods;
    }

    /// <summary>
    /// The FaceValueLot regular coupon grid: copied from
    /// <c>src/Meridian.Contracts/SecurityMaster/FaceValueLot.cs:405-438</c> at <c>7d072638</c>
    /// (<c>RegularCouponDates</c>), with the lot's <c>AcquiredDate</c> as the
    /// <paramref name="acquiredDate"/> parameter. Returns null (or throws when
    /// <paramref name="throwWhenIrregular"/> is set) for a schedule that is not regular from
    /// acquisition.
    /// </summary>
    public static DateOnly[]? FaceValueLotRegular(DateOnly acquiredDate, DateOnly maturity, int paymentsPerYear, bool throwWhenIrregular)
    {
        if (paymentsPerYear is not (1 or 2 or 4 or 12))
            throw new ArgumentException("Constant-yield amortization requires an annual, semiannual, quarterly or monthly coupon frequency.");
        var monthsPerPeriod = 12 / paymentsPerYear;
        var months = (maturity.Year - acquiredDate.Year) * 12 + maturity.Month - acquiredDate.Month;
        if (months < monthsPerPeriod || months % monthsPerPeriod != 0 || months / monthsPerPeriod > 1200)
        {
            if (!throwWhenIrregular)
                return null;
            throw new ArgumentException("Constant-yield amortization requires one to 1200 level calendar coupon periods.");
        }

        // Endpoints at month end retain EOM through February and leap years. Otherwise
        // retain the contractual day, clipping only short months. Anchor each boundary
        // independently so a clipped February never shifts later coupon dates.
        var endOfMonth = acquiredDate.Day == DateTime.DaysInMonth(acquiredDate.Year, acquiredDate.Month)
            && maturity.Day == DateTime.DaysInMonth(maturity.Year, maturity.Month);
        var dayOfMonth = Math.Max(acquiredDate.Day, maturity.Day);
        var dates = new DateOnly[months / monthsPerPeriod + 1];
        for (var period = 0; period < dates.Length; period++)
        {
            var month = acquiredDate.AddMonths(period * monthsPerPeriod);
            var lastDay = DateTime.DaysInMonth(month.Year, month.Month);
            dates[period] = new DateOnly(month.Year, month.Month, endOfMonth ? lastDay : Math.Min(dayOfMonth, lastDay));
        }
        if (dates[0] != acquiredDate || dates[^1] != maturity)
        {
            if (!throwWhenIrregular)
                return null;
            throw new ArgumentException("Constant-yield amortization requires regular calendar coupon dates; odd periods are unsupported by this calculation version.");
        }
        return dates;
    }

    /// <summary>
    /// The FaceValueLot backward coupon grid: copied from
    /// <c>src/Meridian.Contracts/SecurityMaster/FaceValueLot.cs:561-577</c> at <c>7d072638</c>
    /// (<c>BackwardCouponDates</c>), with the lot's <c>AcquiredDate</c> as the
    /// <paramref name="acquiredDate"/> parameter. Element 0 is the last coupon date on or before
    /// acquisition: the quasi-coupon start of the stub period.
    /// </summary>
    public static DateOnly[] FaceValueLotBackward(DateOnly acquiredDate, DateOnly maturity, int paymentsPerYear)
    {
        var monthsPerPeriod = 12 / paymentsPerYear;
        var endOfMonth = maturity.Day == DateTime.DaysInMonth(maturity.Year, maturity.Month);
        var dates = new List<DateOnly> { maturity };
        while (dates[^1] > acquiredDate)
        {
            if (dates.Count > 1200)
                throw new ArgumentException("Constant-yield amortization requires one to 1200 calendar coupon periods.");
            var month = maturity.AddMonths(-dates.Count * monthsPerPeriod);
            var lastDay = DateTime.DaysInMonth(month.Year, month.Month);
            dates.Add(new DateOnly(month.Year, month.Month, endOfMonth ? lastDay : Math.Min(maturity.Day, lastDay)));
        }

        dates.Reverse();
        return dates.ToArray();
    }

    /// <summary>
    /// The obligation-timeline step functions: copied from
    /// <c>src/Meridian.Instruments/AssetOperations/AssetObligationProjectionService.cs:922-926</c> at
    /// <c>7d072638</c> (<c>NextMonthlyDate</c>), unchanged. The first of the month after
    /// <paramref name="asOf"/>'s month.
    /// </summary>
    public static DateOnly ObligationNextMonthlyDate(DateOnly asOf)
    {
        var monthStart = new DateOnly(asOf.Year, asOf.Month, 1);
        return monthStart.AddMonths(1);
    }

    /// <summary>
    /// Copied from <c>AssetObligationProjectionService.cs:928-938</c> at <c>7d072638</c>
    /// (<c>NextQuarterDate</c>), unchanged. The first calendar-quarter start after
    /// <paramref name="asOf"/>, compounded three months at a time from its quarter's start.
    /// </summary>
    public static DateOnly ObligationNextQuarterDate(DateOnly asOf)
    {
        var quarterStartMonth = (((asOf.Month - 1) / 3) * 3) + 1;
        var next = new DateOnly(asOf.Year, quarterStartMonth, 1);
        while (next <= asOf)
        {
            next = next.AddMonths(3);
        }

        return next;
    }

    /// <summary>
    /// Copied from <c>AssetObligationProjectionService.cs:940-944</c> at <c>7d072638</c>
    /// (<c>NextFrequencyDate</c>), unchanged. One frequency step after <paramref name="asOf"/>.
    /// </summary>
    public static DateOnly ObligationNextFrequencyDate(DateOnly asOf, string? paymentFrequency)
    {
        var months = ObligationNormalizeFrequencyMonths(paymentFrequency);
        return asOf.AddMonths(months);
    }

    /// <summary>
    /// Copied from <c>AssetObligationProjectionService.cs:946-955</c> at <c>7d072638</c>
    /// (<c>AdvanceAfter</c>), unchanged. The first date after <paramref name="asOf"/>, compounded
    /// <paramref name="months"/> at a time from <paramref name="anchor"/>.
    /// </summary>
    public static DateOnly ObligationAdvanceAfter(DateOnly anchor, DateOnly asOf, int months)
    {
        var next = anchor;
        while (next <= asOf)
        {
            next = next.AddMonths(months);
        }

        return next;
    }

    /// <summary>
    /// Copied from <c>AssetObligationProjectionService.cs:957-984</c> at <c>7d072638</c>
    /// (<c>NormalizeFrequencyMonths</c>), unchanged. Months per obligation step for a frequency
    /// spelling; quarterly when the spelling is blank or unknown.
    /// </summary>
    public static int ObligationNormalizeFrequencyMonths(string? paymentFrequency)
    {
        if (string.IsNullOrWhiteSpace(paymentFrequency))
        {
            return 3;
        }

        var normalized = paymentFrequency
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
        if (int.TryParse(normalized, out var periodsPerYear) && periodsPerYear > 0)
        {
            return Math.Max(1, 12 / Math.Clamp(periodsPerYear, 1, 12));
        }

        return normalized switch
        {
            "MONTHLY" => 1,
            "QUARTERLY" => 3,
            "SEMIANNUAL" or "SEMIANNUALLY" or "SEMIYEARLY" => 6,
            "ANNUAL" or "ANNUALLY" or "YEARLY" => 12,
            "BULLET" => 12,
            _ => 3
        };
    }

    /// <summary>
    /// The Postgres direct-lending projection roll, dates plus the interval switch: copied from
    /// <c>src/Meridian.Application/DirectLending/PostgresDirectLendingCommandService.cs:1346-1385</c>
    /// at <c>7d072638</c> (<c>BuildProjectedFlows</c>). <c>contract.CurrentTerms.PaymentFrequency</c>
    /// and <c>MaturityDate</c> become parameters; the day count, interest amount and flow DTO are
    /// dropped. Each due date compounds from the previous one, starting at the projection as-of.
    /// </summary>
    public static IReadOnlyList<(DateOnly AccrualStart, DateOnly DueDate)> LoanPostgresAsOf(
        DateOnly asOf,
        DateOnly maturityDate,
        PaymentFrequency paymentFrequency)
    {
        var results = new List<(DateOnly AccrualStart, DateOnly DueDate)>();
        var dueDate = asOf;
        var intervalMonths = paymentFrequency switch
        {
            PaymentFrequency.Monthly => 1,
            PaymentFrequency.Quarterly => 3,
            PaymentFrequency.SemiAnnual => 6,
            PaymentFrequency.Annual => 12,
            _ => 12
        };

        while (dueDate < maturityDate)
        {
            var nextDue = dueDate.AddMonths(intervalMonths);
            if (nextDue > maturityDate)
            {
                nextDue = maturityDate;
            }

            results.Add((dueDate, nextDue));

            dueDate = nextDue;
        }

        return results;
    }

    /// <summary>
    /// The in-memory direct-lending projection roll, dates only: copied from
    /// <c>src/Meridian.Application/DirectLending/InMemoryDirectLendingService.Workflows.cs:404-418</c>
    /// at <c>7d072638</c> (<c>BuildFlows</c>). The activation (or effective) date, the latest terms'
    /// maturity and the run's projection as-of become parameters; the accrual amount and flow DTO are
    /// dropped. Monthly dates compound from the start and stop at maturity or at the as-of.
    /// </summary>
    public static IReadOnlyList<(DateOnly AccrualStart, DateOnly DueDate)> LoanInMemoryMonthly(
        DateOnly startDate,
        DateOnly maturityDate,
        DateOnly projectionAsOf)
    {
        var flows = new List<(DateOnly AccrualStart, DateOnly DueDate)>();
        var currentDate = startDate;
        while (currentDate < maturityDate && currentDate < projectionAsOf)
        {
            var nextDate = currentDate.AddMonths(1);
            if (nextDate > maturityDate)
                nextDate = maturityDate;
            flows.Add((currentDate, nextDate));
            currentDate = nextDate;
        }

        return flows;
    }

    /// <summary>
    /// The fund-structure income forecast roll: copied from
    /// <c>src/Meridian.Application/FundStructure/InMemoryFundStructureService.cs:2351-2374</c> (the
    /// windowed loop) and <c>:2377-2419</c> (<c>TryAdvanceByFrequency</c>) at <c>7d072638</c>. The
    /// resolved anchor, frequency and projection window become parameters; the blank-frequency
    /// single-date branch before the loop is not a roll and is not copied. Each date compounds from the
    /// previous one, for at most 256 iterations.
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> FundStructureCompounded(
        DateOnly? anchorDate,
        string? frequency,
        DateTimeOffset projectionWindowStart,
        DateTimeOffset projectionWindowEnd)
    {
        var schedule = new List<DateTimeOffset>();
        var nextDate = anchorDate ?? DateOnly.FromDateTime(projectionWindowStart.UtcDateTime);
        var guard = 0;
        while (guard < 256)
        {
            guard++;
            var eventDate = FundStructureToDateTimeOffset(nextDate);
            if (eventDate >= projectionWindowStart && eventDate < projectionWindowEnd)
            {
                schedule.Add(eventDate);
            }

            if (eventDate >= projectionWindowEnd)
            {
                break;
            }

            if (!FundStructureTryAdvanceByFrequency(nextDate, frequency, out nextDate))
            {
                break;
            }
        }

        return schedule;
    }

    /// <summary>
    /// Copied from <c>InMemoryFundStructureService.cs:2377-2419</c> at <c>7d072638</c>
    /// (<c>TryAdvanceByFrequency</c>), unchanged. One frequency step after
    /// <paramref name="current"/>, or false for an unknown spelling.
    /// </summary>
    public static bool FundStructureTryAdvanceByFrequency(DateOnly current, string? frequency, out DateOnly next)
    {
        next = current;
        if (string.IsNullOrWhiteSpace(frequency))
        {
            return false;
        }

        switch (frequency.Trim().ToLowerInvariant())
        {
            case "daily":
                next = current.AddDays(1);
                return true;
            case "weekly":
                next = current.AddDays(7);
                return true;
            case "biweekly":
            case "bi-weekly":
                next = current.AddDays(14);
                return true;
            case "monthly":
                next = current.AddMonths(1);
                return true;
            case "quarterly":
                next = current.AddMonths(3);
                return true;
            case "semiannual":
            case "semi-annual":
            case "semiannually":
            case "semi-annually":
            case "halfyearly":
            case "half-yearly":
                next = current.AddMonths(6);
                return true;
            case "annual":
            case "annually":
            case "yearly":
                next = current.AddYears(1);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Copied from <c>InMemoryFundStructureService.cs:2644-2645</c> at <c>7d072638</c>
    /// (<c>ToDateTimeOffset</c>), unchanged: UTC midnight of <paramref name="date"/>.
    /// </summary>
    public static DateTimeOffset FundStructureToDateTimeOffset(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TimeSpan.Zero);

    /// <summary>
    /// The Security Master coupon cadence: copied from
    /// <c>src/Meridian.Application/SecurityMaster/SecurityMasterCashFlowService.cs:757-791</c> at
    /// <c>7d072638</c> (<c>ResolvePaymentFrequencyPerYear</c>), unchanged. Both Security Master call
    /// sites derive <c>periodMonths = Math.Max(1, 12 / …)</c> from it.
    /// </summary>
    public static int LegacyCouponPaymentsPerYear(string? paymentFrequency)
    {
        if (string.IsNullOrWhiteSpace(paymentFrequency))
        {
            return 1;
        }

        // The write path admits only the cadences the shared convention recognizes; read them the
        // same way (this adds BiMonthly and tenor spellings such as "3M"). Anything else keeps the
        // legacy interpretation below so stored values read exactly as before.
        if (SecurityTermConventions.CouponPaymentsPerYear(paymentFrequency) is { } paymentsPerYear)
        {
            return paymentsPerYear;
        }

        var normalized = paymentFrequency
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
        if (int.TryParse(normalized, out var parsed) && parsed > 0)
        {
            return Math.Clamp(parsed, 1, 12);
        }

        return normalized switch
        {
            "MONTHLY" => 12,
            "QUARTERLY" => 4,
            "SEMIANNUAL" or "SEMIANNUALLY" or "SEMIYEARLY" or "HALFYEARLY" => 2,
            "ANNUAL" or "ANNUALLY" or "YEARLY" => 1,
            _ => 1
        };
    }
}
