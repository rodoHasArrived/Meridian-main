using System.Text.Json;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Contracts.FixedIncome;

/// <summary>Versioned, pure per-100 contractual cash, accrual and discount-time calculations.</summary>
public static class BondContractualEconomics
{
    public const string ModelVersion = "bond-contractual-economics-v1";
    public static bool TryBuild(Guid securityId, StructuredCashFlowTerms terms,
        DeclaredScheduleResolution declared, ScheduleGenerationResult schedule, DateOnly settlement,
        decimal annualRate, bool zeroCoupon, BondYieldConvention yieldConvention,
        out IReadOnlyList<BondAnalyticsCashFlow> flows, out decimal accrued, out string? refusal)
    {
        flows = [];
        accrued = 0m;
        refusal = null;
        var result = new List<BondAnalyticsCashFlow>();
        var zero = zeroCoupon;
        var dayCount = DayCountConventions.Parse(terms.DayCountConvention);
        if (!zero && dayCount == DayCountConvention.Unknown)
        {
            refusal = "A recognized coupon day-count convention is required.";
            return false;
        }
        foreach (var period in schedule.Periods)
        {
            if (period.PaymentDate <= settlement)
                continue;
            if (period.AccrualEnd <= settlement && !zero)
            {
                refusal = "Settlement within a delayed-payment coupon window needs separate entitlement rules.";
                return false;
            }
            if (!TryCoupon(period, schedule, dayCount, annualRate, declared.Conventions.CouponCashBasis,
                    out var interest, out refusal))
                return false;
            if (period.AccrualStart <= settlement && settlement < period.AccrualEnd && !zero)
            {
                if (!TryAccruedCoupon(period, schedule, dayCount, annualRate,
                        declared.Conventions.CouponCashBasis, settlement, out accrued, out refusal))
                    return false;
            }
            var discountDate = !zero && yieldConvention == BondYieldConvention.NominalPeriodic
                && period.PaymentDate > period.AccrualEnd ? period.AccrualEnd : period.PaymentDate;
            if (!TryTime(schedule, settlement, discountDate, dayCount, zero,
                    yieldConvention, out var time, out refusal))
                return false;
            if (discountDate < period.PaymentDate)
                time += (period.PaymentDate.DayNumber - discountDate.DayNumber) / 365m;
            if (interest > 0m || period.IsFinal)
                result.Add(new(SchedulePeriodId.Derive(securityId, "bond-analytics", period), period.PaymentDate,
                    period.AccrualStart, period.AccrualEnd, interest, period.IsFinal ? 100m : 0m, time));
        }
        if (result.Count == 0)
        {
            refusal = "No payments remain after settlement.";
            return false;
        }
        accrued = decimal.Round(accrued, 12, MidpointRounding.AwayFromZero);
        flows = result;
        return true;
    }

    public static bool TryCoupon(SchedulePeriod period, ScheduleGenerationResult schedule,
        DayCountConvention convention, decimal annualRate, string? cashBasis,
        out decimal coupon, out string? refusal)
    {
        coupon = 0m;
        refusal = null;
        if (annualRate == 0m)
            return true;
        if (!ScheduleDayCount.TryPeriodFraction(convention, period, schedule, out var fraction, out refusal))
            return false;
        if (cashBasis == "Level")
        {
            if (schedule.Spec.Tenor.PeriodsPerYear is not { } frequency)
            {
                refusal = "Level coupon cash requires a periodic coupon frequency.";
                return false;
            }
            if (!TryReferenceUnits(period, schedule, convention, period.AccrualStart, period.AccrualEnd,
                    out var units, out refusal))
                return false;
            coupon = 100m * annualRate / frequency * units;
        }
        else
            coupon = 100m * annualRate * fraction;
        return true;
    }

    public static bool TryAccruedCoupon(SchedulePeriod period, ScheduleGenerationResult schedule,
        DayCountConvention convention, decimal annualRate, string? cashBasis, DateOnly date,
        out decimal accrued, out string? refusal)
    {
        accrued = 0m;
        refusal = null;
        if (date <= period.AccrualStart || annualRate == 0m)
            return true;
        var end = date < period.AccrualEnd ? date : period.AccrualEnd;
        if (cashBasis == "Level")
        {
            if (schedule.Spec.Tenor.PeriodsPerYear is not { } frequency)
            { refusal = "Level coupon cash requires a periodic frequency."; return false; }
            if (!TryReferenceUnits(period, schedule, convention, period.AccrualStart, end, out var units, out refusal))
                return false;
            accrued = 100m * annualRate / frequency * units;
        }
        else
        {
            if (!ScheduleDayCount.TryFraction(convention, period, schedule, period.AccrualStart, end,
                    out var fraction, out refusal))
                return false;
            accrued = 100m * annualRate * fraction;
        }
        return true;
    }

    /// <summary>
    /// Periodic yields use contractual coupon periods, including reference-period fractions for
    /// stubs. Annual-effective/simple yields and zero coupons use Actual/365 Fixed time.
    /// Payment dates outside the accrual grid add their actual calendar-day lag over 365.
    /// </summary>
    public static bool TryTime(ScheduleGenerationResult schedule, DateOnly settlement, DateOnly payment,
        DayCountConvention dayCount, bool zeroCoupon, BondYieldConvention convention,
        out decimal time, out string? refusal)
    {
        time = 0m;
        refusal = null;
        if (payment <= settlement)
        {
            refusal = "Discounted payment must be after settlement.";
            return false;
        }
        if (zeroCoupon || convention != BondYieldConvention.NominalPeriodic)
        {
            time = (payment.DayNumber - settlement.DayNumber) / 365m;
            return true;
        }
        if (schedule.Spec.Tenor.PeriodsPerYear is not { } frequency)
        {
            refusal = "Periodic yield needs a contractual coupon frequency.";
            return false;
        }
        foreach (var period in schedule.Periods)
        {
            var start = settlement > period.AccrualStart ? settlement : period.AccrualStart;
            var end = payment < period.AccrualEnd ? payment : period.AccrualEnd;
            if (end <= start)
                continue;
            if (!TryReferenceUnits(period, schedule, dayCount, start, end, out var units, out refusal))
                return false;
            time += units / frequency;
        }
        var lastAccrual = schedule.Periods[^1].AccrualEnd;
        if (payment > lastAccrual)
            time += (payment.DayNumber - Math.Max(settlement.DayNumber, lastAccrual.DayNumber)) / 365m;
        if (time <= 0m)
        {
            refusal = "Payment has no positive time on the declared coupon grid.";
            return false;
        }
        return true;
    }

    public static bool TryReferenceUnits(SchedulePeriod period, ScheduleGenerationResult schedule,
        DayCountConvention convention, DateOnly start, DateOnly end, out decimal units, out string? refusal)
    {
        units = 0m;
        refusal = null;
        if (!period.IsStub)
        {
            if (!ScheduleDayCount.TryFraction(convention, period, schedule, start, end, out var part, out refusal)
                || !ScheduleDayCount.TryPeriodFraction(convention, period, schedule, out var full, out refusal))
                return false;
            if (full <= 0m)
            {
                refusal = "Coupon period has no positive day-count fraction.";
                return false;
            }
            units = part / full;
            return true;
        }
        var context = ScheduleDayCount.ContextFor(period, schedule);
        if (period.ReferencePeriods.Count == 0 || start < period.ReferencePeriods[0].Start
            || end > period.ReferencePeriods[^1].End)
        {
            refusal = "Stub reference periods do not cover the requested accrual window.";
            return false;
        }
        foreach (var reference in period.ReferencePeriods)
        {
            var segmentStart = start > reference.Start ? start : reference.Start;
            var segmentEnd = end < reference.End ? end : reference.End;
            if (segmentEnd <= segmentStart)
                continue;
            if (!DayCountConventions.TryFraction(convention, segmentStart, segmentEnd, context, out var part, out refusal)
                || !DayCountConventions.TryFraction(convention, reference.Start, reference.End, context, out var full, out refusal))
                return false;
            if (full <= 0m)
            {
                refusal = "Stub reference coupon period has no positive fraction.";
                return false;
            }
            units += part / full;
        }
        return true;
    }

    public static string? Text(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var member)
            && member.ValueKind == JsonValueKind.String ? member.GetString() : null;
}
