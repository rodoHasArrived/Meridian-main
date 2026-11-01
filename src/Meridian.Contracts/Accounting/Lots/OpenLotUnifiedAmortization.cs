using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>
/// V5 fixed-rate bullet amortization on explicitly declared contractual periods, priced through the
/// shared <see cref="BondContractualEconomics"/> kernel so lot basis, analytics and cash projections
/// agree. Supports odd first and final periods without changing any v1/v2/v3 replay. Unretained
/// business-day calendars are refused.
/// </summary>
internal static class OpenLotUnifiedAmortization
{
    public static decimal Project(FaceValueLot lot, SecurityDetailDto security, StructuredCashFlowTerms terms,
        DayCountConvention convention, DateOnly asOf, BondAmortizationMethod method, decimal? retainedYield)
    {
        var declared = ScheduleSpecFactory.Resolve(security, terms, calendars: null);
        if (declared.Status != DeclaredScheduleStatus.Declared)
            throw new ArgumentException($"Declared amortization schedule is unavailable: {declared.Refusal?.Message}");
        if (declared.Conventions.CouponRateUnit is not ("Percent" or "Fraction")
            || declared.Conventions.CouponCashBasis is not ("Level" or "DayCount"))
            throw new ArgumentException("Declared amortization requires explicit couponRateUnit and couponCashBasis.");
        var spec = declared.Spec!;
        if (spec.AccrualAdjustment is { IsUnadjusted: false } || spec.PaymentAdjustment is { IsUnadjusted: false }
            || spec.PaymentLagBusinessDays != 0)
            throw new ArgumentException("Declared amortization requires retained unadjusted payment and accrual dates; calendar-adjusted accounting needs retained calendar evidence.");
        var schedule = ScheduleGenerator.Generate(spec);
        if (!schedule.Succeeded || schedule.Periods.Count == 0 || schedule.Periods.Count > 1200
            || lot.AcquiredDate < spec.EffectiveDate || lot.AcquiredDate >= spec.TerminationDate)
            throw new ArgumentException($"Declared amortization requires one to 1200 contractual periods spanning acquisition: {schedule.Failure?.Message}");
        if (method == BondAmortizationMethod.StraightLine)
        {
            var life = AccrualBetween(lot.AcquiredDate, spec.TerminationDate, schedule, convention);
            if (life <= 0m)
                throw new ArgumentException("Declared amortization requires a positive contractual accrual life.");
            var elapsed = AccrualBetween(lot.AcquiredDate, asOf, schedule, convention);
            return lot.CostBasis + (lot.OriginalFace - lot.CostBasis) * elapsed / life;
        }
        if (method != BondAmortizationMethod.ConstantYield || retainedYield is not { } annualYield)
            throw new ArgumentException("Declared amortization supports retained straight-line and constant-yield instructions only.");
        var frequency = spec.Tenor.PeriodsPerYear;
        if (frequency is null || frequency <= 0)
            throw new ArgumentException("Declared constant-yield amortization requires an explicit periodic coupon frequency.");
        var rate = terms.CouponRate ?? throw new ArgumentException("Declared amortization requires a contractual coupon rate, including explicit zero-coupon structure.");
        var annualRate = declared.Conventions.CouponRateUnit == "Percent" ? rate / 100m : rate;
        if (annualRate < 0m)
            throw new ArgumentException("Declared amortization does not support negative coupon cash flows.");
        decimal SharedClean(DateOnly date)
        {
            if (!BondContractualEconomics.TryBuild(security.SecurityId, terms, declared, schedule, date,
                    annualRate, terms.CouponKind == SecurityCouponKind.ZeroCoupon, BondYieldConvention.NominalPeriodic,
                    out var flows, out var accrued, out var reason)
                || !BondPricingMath.TryPrice(flows.Select(flow => new BondPricingCashFlow(flow.Interest + flow.Principal,
                        flow.TimeInYears)).ToArray(), annualYield, BondYieldConvention.NominalPeriodic, frequency.Value,
                    out var measures, out reason))
                throw new ArgumentException($"Declared amortization cannot calculate shared economics: {reason}");
            return (measures!.DirtyPrice - accrued) / 100m;
        }
        var acquisitionPrice = SharedClean(lot.AcquiredDate);
        // The shared price engine rounds fractional powers to twelve decimal places; reconcile at
        // its declared numerical boundary, not the tighter pure-decimal v2 solver boundary.
        if (Math.Abs(acquisitionPrice - lot.PricePercentOfPar / lot.ParBasis) > 0.0000000001m)
            throw new ArgumentException("Retained effective yield does not reconcile to acquisition clean price and declared contractual periods.");
        if (asOf >= spec.TerminationDate)
            return lot.OriginalFace;
        return SharedClean(asOf) * lot.OriginalFace;
    }

    private static decimal AccrualBetween(DateOnly start, DateOnly end, ScheduleGenerationResult schedule, DayCountConvention convention)
        => schedule.Periods.Where(period => period.AccrualEnd > start && period.AccrualStart < end)
            .Sum(period => Fraction(start > period.AccrualStart ? start : period.AccrualStart,
                end < period.AccrualEnd ? end : period.AccrualEnd, period, schedule, convention));

    private static decimal Fraction(DateOnly start, DateOnly end, SchedulePeriod period,
        ScheduleGenerationResult schedule, DayCountConvention convention)
    {
        if (!ScheduleDayCount.TryFraction(convention, period, schedule, start, end, out var fraction, out var refusal))
            throw new ArgumentException($"Declared amortization day count is unavailable: {refusal}");
        return fraction;
    }
}
