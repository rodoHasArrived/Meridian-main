using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Contracts.FixedIncome;

public sealed record BondWorkflowOvernightConvention(IBusinessDayCalendar Calendar, int LookbackBusinessDays,
    bool ObservationShift, bool InterestPeriodWeighting, int LockoutBusinessDays);
public sealed record BondWorkflowInflationConvention(string IndexName, decimal BaseIndex, int LagMonths,
    bool InterpolateMonthly, decimal? CouponFloorRatio = null, decimal? RedemptionFloorRatio = null);
public sealed record BondWorkflowFixing(decimal Value, bool IsObserved, string Source);
public sealed record BondWorkflowProjectionTerms(string CouponKind, decimal CouponRate, decimal Spread,
    decimal? Cap, decimal? Floor, DayCountConvention DayCount, string CouponCashBasis,
    decimal OriginalFace, decimal OpeningFactor, IReadOnlyList<StructuredPrincipalScheduleEntry> Principal,
    BondWorkflowOvernightConvention? Overnight = null, BondWorkflowInflationConvention? Inflation = null);

/// <summary>New signed-cash kernel. No clock, persistence, ledger or legacy-arithmetic dependency.</summary>
public static class BondWorkflowProjection
{
    public const string ModelVersion = "bond-workflow-cash-v1";

    public static IReadOnlyList<BondWorkflowCashFlow> Project(ScheduleGenerationResult schedule,
        BondWorkflowProjectionTerms terms, DateOnly from, Func<DateOnly, BondWorkflowFixing>? fixing = null,
        Func<DateOnly, BondWorkflowFixing>? inflation = null)
    {
        if (!schedule.Succeeded || terms.OriginalFace <= 0m || terms.OpeningFactor is < 0m or > 1m)
            throw new ArgumentException("A declared schedule, positive original face and factor in [0,1] are required.");
        if (terms.Cap is { } cap && terms.Floor is { } floor && floor > cap)
            throw new ArgumentException("Coupon floor exceeds cap.");
        if (terms.Principal.Any(p => p.Amount < 0m) || terms.Principal.Sum(p => p.Amount) > terms.OriginalFace)
            throw new ArgumentException("Principal repayments must be nonnegative and cannot exceed original face.");
        var opening = terms.OriginalFace * terms.OpeningFactor;
        var relevant = terms.Principal.Where(p => p.PaymentDate > from).OrderBy(p => p.PaymentDate).ToArray();
        if (relevant.Sum(p => p.Amount) > opening)
            throw new ArgumentException("Future contractual repayments exceed the retained outstanding face.");
        var rows = new List<BondWorkflowCashFlow>();
        foreach (var period in schedule.Periods.Where(p => p.PaymentDate >= from))
        {
            var initial = opening + terms.Principal.Where(p => p.PaymentDate > period.AccrualStart && p.PaymentDate <= from).Sum(p => p.Amount)
                - relevant.Where(p => p.PaymentDate <= period.AccrualStart).Sum(p => p.Amount);
            if (initial < 0m)
                throw new ArgumentException("Principal evidence produces a negative accrual balance.");
            var changes = terms.Principal.Where(p => p.PaymentDate > period.AccrualStart && p.PaymentDate < period.AccrualEnd)
                .OrderBy(p => p.PaymentDate).ToArray();
            if (terms.Overnight is not null && changes.Length != 0)
                throw new ArgumentException("Overnight amortization requires principal changes at coupon boundaries.");
            var interest = 0m;
            var observations = new List<BondWorkflowObservation>();
            var cursor = period.AccrualStart;
            var balance = initial;
            foreach (var repayment in changes)
            {
                interest += Coupon(schedule, period, terms, cursor, repayment.PaymentDate, balance, fixing, observations);
                cursor = repayment.PaymentDate;
                balance -= repayment.Amount;
            }
            interest += Coupon(schedule, period, terms, cursor, period.AccrualEnd, balance, fixing, observations);
            var ratio = 1m;
            var indexForecast = false;
            if (terms.Inflation is { } convention)
            {
                var indexed = IndexRatio(period.AccrualEnd, convention, inflation!);
                ratio = indexed.Value;
                indexForecast = !indexed.IsObserved;
                interest *= Math.Max(ratio, convention.CouponFloorRatio ?? ratio);
            }
            var outstanding = opening - relevant.Where(p => p.PaymentDate <= period.PaymentDate).Sum(p => p.Amount);
            var hasFraction = ScheduleDayCount.TryPeriodFraction(terms.DayCount, period, schedule, out var fullFraction, out _);
            var hasUnits = BondContractualEconomics.TryReferenceUnits(period, schedule, terms.DayCount,
                period.AccrualStart, period.AccrualEnd, out var periodUnits, out _);
            rows.Add(new(period.PaymentDate, period.AccrualStart, period.AccrualEnd, interest, 0m,
                outstanding, ratio, indexForecast || observations.Any(o => !o.IsObserved), observations)
            { AccrualFraction = hasFraction ? fullFraction : null, PeriodUnits = hasUnits ? periodUnits : null });
        }
        foreach (var principal in relevant)
        {
            var ratio = terms.Inflation is { } convention ? IndexRatio(principal.PaymentDate, convention, inflation!).Value : 1m;
            var appliedRatio = terms.Inflation is { RedemptionFloorRatio: { } redemptionFloor } ? Math.Max(redemptionFloor, ratio) : ratio;
            rows.Add(new(principal.PaymentDate, principal.PaymentDate, principal.PaymentDate, 0m,
                principal.Amount * appliedRatio, opening - relevant.Where(p => p.PaymentDate <= principal.PaymentDate).Sum(p => p.Amount),
                ratio, terms.Inflation is not null && !IndexRatio(principal.PaymentDate, terms.Inflation, inflation!).IsObserved, [])
            { AccrualFraction = 0m, PeriodUnits = 0m });
        }
        var residual = opening - relevant.Sum(p => p.Amount);
        var final = schedule.Periods[^1].PaymentDate;
        if (residual != 0m && final >= from)
        {
            var indexed = terms.Inflation is { } convention ? IndexRatio(final, convention, inflation!) : new BondWorkflowFixing(1m, true, "contract");
            var ratio = Math.Max(indexed.Value, terms.Inflation?.RedemptionFloorRatio ?? indexed.Value);
            rows.Add(new(final, final, final, 0m, residual * ratio, 0m, indexed.Value, !indexed.IsObserved, [])
            { AccrualFraction = 0m, PeriodUnits = 0m });
        }
        return rows.OrderBy(r => r.PaymentDate).ThenBy(r => r.AccrualStart).ToArray();
    }

    public static decimal Accrued(ScheduleGenerationResult schedule, BondWorkflowProjectionTerms terms,
        DateOnly settlement, Func<DateOnly, BondWorkflowFixing>? fixing = null, Func<DateOnly, BondWorkflowFixing>? inflation = null)
    {
        var period = schedule.Periods.FirstOrDefault(p => p.AccrualStart < settlement && p.PaymentDate > settlement);
        if (period is null)
            return 0m;
        var end = settlement < period.AccrualEnd ? settlement : period.AccrualEnd;
        var balance = terms.OriginalFace * terms.OpeningFactor
            + terms.Principal.Where(p => p.PaymentDate > period.AccrualStart && p.PaymentDate <= settlement).Sum(p => p.Amount);
        var cursor = period.AccrualStart;
        var amount = 0m;
        foreach (var repayment in terms.Principal.Where(p => p.PaymentDate > cursor && p.PaymentDate < end).OrderBy(p => p.PaymentDate))
        {
            amount += Coupon(schedule, period, terms, cursor, repayment.PaymentDate, balance, fixing, []);
            balance -= repayment.Amount;
            cursor = repayment.PaymentDate;
        }
        amount += Coupon(schedule, period, terms, cursor, end, balance, fixing, []);
        if (terms.Inflation is { } convention)
        {
            var ratio = IndexRatio(settlement, convention, inflation!).Value;
            amount *= Math.Max(ratio, convention.CouponFloorRatio ?? ratio);
        }
        return amount;
    }

    public static BondWorkflowFixing IndexRatio(DateOnly date, BondWorkflowInflationConvention terms,
        Func<DateOnly, BondWorkflowFixing> observation)
    {
        if (terms.BaseIndex <= 0m || terms.LagMonths is < 0 or > 24)
            throw new ArgumentException("Inflation needs positive base index and an explicit lag from 0 to 24 months.");
        var reference = new DateOnly(date.Year, date.Month, 1).AddMonths(-terms.LagMonths);
        var first = observation(reference);
        var next = terms.InterpolateMonthly ? observation(reference.AddMonths(1)) : first;
        if (first.Value <= 0m || next.Value <= 0m)
            throw new ArgumentException("Inflation index levels must be positive.");
        var weight = terms.InterpolateMonthly ? (decimal)(date.Day - 1) / DateTime.DaysInMonth(date.Year, date.Month) : 0m;
        return new((first.Value + (next.Value - first.Value) * weight) / terms.BaseIndex,
            first.IsObserved && next.IsObserved, first.Source + (terms.InterpolateMonthly ? ";" + next.Source : string.Empty));
    }

    private static decimal Coupon(ScheduleGenerationResult schedule, SchedulePeriod period,
        BondWorkflowProjectionTerms terms, DateOnly start, DateOnly end, decimal principal,
        Func<DateOnly, BondWorkflowFixing>? fixing, List<BondWorkflowObservation> observations)
    {
        if (end <= start || principal == 0m || terms.CouponKind == "ZeroCoupon")
            return 0m;
        if (!ScheduleDayCount.TryFraction(terms.DayCount, period, schedule, start, end, out var fraction, out var reason))
            throw new ArgumentException(reason);
        if (terms.Overnight is { } overnight)
            return OvernightCoupon(start, end, period.AccrualEnd, principal, terms, overnight, fixing!, observations, fraction);
        if (terms.CouponKind == "Floating")
        {
            var rate = fixing!(period.AccrualStart);
            observations.Add(new(start, period.AccrualStart, end.DayNumber - start.DayNumber, rate.Value, rate.IsObserved, rate.Source));
            return principal * Clamp(rate.Value + terms.Spread, terms) * fraction;
        }
        if (!BondContractualEconomics.TryAccruedCoupon(period, schedule, terms.DayCount, terms.CouponRate,
                terms.CouponCashBasis, start, out var before, out reason)
            || !BondContractualEconomics.TryAccruedCoupon(period, schedule, terms.DayCount, terms.CouponRate,
                terms.CouponCashBasis, end, out var after, out reason))
            throw new ArgumentException(reason);
        return principal * (after - before) / 100m;
    }

    private static decimal OvernightCoupon(DateOnly start, DateOnly end, DateOnly couponEnd, decimal principal,
        BondWorkflowProjectionTerms terms, BondWorkflowOvernightConvention convention,
        Func<DateOnly, BondWorkflowFixing> fixing, List<BondWorkflowObservation> observations, decimal accrualFraction)
    {
        if (terms.DayCount is not (DayCountConvention.Actual360 or DayCountConvention.Actual365))
            throw new ArgumentException("Overnight coupons require explicit Actual360 or Actual365Fixed.");
        if (convention.LookbackBusinessDays is < 0 or > 30 || convention.LockoutBusinessDays is < 0 or > 30)
            throw new ArgumentException("Lookback and lockout must be between 0 and 30 business days.");
        var calendar = convention.Calendar;
        var denominator = terms.DayCount == DayCountConvention.Actual360 ? 360m : 365m;
        var obsStart = convention.ObservationShift ? calendar.AddBusinessDays(start, -convention.LookbackBusinessDays) : start;
        var obsEnd = convention.ObservationShift ? calendar.AddBusinessDays(end, -convention.LookbackBusinessDays) : end;
        var fullObsEnd = convention.ObservationShift ? calendar.AddBusinessDays(couponEnd, -convention.LookbackBusinessDays) : couponEnd;
        var lockStart = convention.LockoutBusinessDays == 0 ? fullObsEnd : calendar.AddBusinessDays(fullObsEnd, -convention.LockoutBusinessDays);
        if (convention.LockoutBusinessDays > 0 && lockStart <= obsStart)
            throw new ArgumentException("Lockout consumes the whole observation period.");
        var lockFixing = calendar.AddBusinessDays(lockStart, -1);
        var product = 1m;
        var cursor = obsStart;
        while (cursor < obsEnd)
        {
            if (!calendar.Coverage.Covers(cursor))
                throw new ArgumentException("Fixing calendar coverage is missing.");
            var business = calendar.Adjust(cursor, BusinessDayConvention.Preceding);
            var next = calendar.AddBusinessDays(business, 1);
            if (next > obsEnd)
                next = obsEnd;
            var reference = convention.LockoutBusinessDays > 0 && cursor >= lockStart ? lockFixing : business;
            if (!convention.ObservationShift)
                reference = calendar.AddBusinessDays(reference, -convention.LookbackBusinessDays);
            if (!calendar.Coverage.Covers(reference))
                throw new ArgumentException("Fixing calendar coverage is missing for an observation date.");
            var rate = fixing(reference);
            var days = next.DayNumber - cursor.DayNumber;
            var factor = 1m + rate.Value * days / denominator;
            if (factor <= 0m)
                throw new ArgumentException("An overnight compounding factor is nonpositive.");
            product = checked(product * factor);
            var accrualDate = convention.ObservationShift
                ? calendar.AddBusinessDays(cursor, convention.LookbackBusinessDays) : cursor;
            observations.Add(new(accrualDate, reference, days, rate.Value, rate.IsObserved, rate.Source));
            cursor = next;
        }
        var observationFraction = (obsEnd.DayNumber - obsStart.DayNumber) / denominator;
        var annualBenchmark = (product - 1m) / observationFraction;
        var paymentFraction = convention.ObservationShift && !convention.InterestPeriodWeighting ? observationFraction : accrualFraction;
        return principal * Clamp(annualBenchmark + terms.Spread, terms) * paymentFraction;
    }

    private static decimal Clamp(decimal rate, BondWorkflowProjectionTerms terms)
    {
        if (terms.Cap is { } cap)
            rate = Math.Min(rate, cap);
        if (terms.Floor is { } floor)
            rate = Math.Max(rate, floor);
        return rate;
    }
}
