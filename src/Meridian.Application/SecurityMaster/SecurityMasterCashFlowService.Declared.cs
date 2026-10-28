using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterCashFlowService
{
    private StructuredCashFlowProjectionDto? BuildDeclaredProjection(
        SecurityDetailDto security, StructuredCashFlowTerms terms, StructuredCashFlowSourceKind source,
        StructuredCashFlowScenario scenario, StructuredCashFlowStaleness staleness,
        DateTimeOffset? updated, DateTimeOffset asOf, DateOnly issue, DateOnly maturity,
        IReadOnlyDictionary<string, SecurityForwardCurve>? curves)
    {
        var asOfDate = DateOnly.FromDateTime(asOf.UtcDateTime);
        StructuredCashFlowProjectionDto Result(IReadOnlyList<StructuredCashFlowScheduleEntry> entries,
            IReadOnlyList<StructuredCashFlowLegSchedule>? legs = null) => new(
                security.SecurityId, source, scenario, asOf, entries, staleness, updated,
                terms.FactorSchedule, terms, legs,
                BlockedReason: "Declared schedules are preview-only until calendar and convention evidence is retained.");
        if (terms.HasLegs)
        {
            var legs = terms.Legs!;
            var plans = legs.Select((leg, index) => ScheduleSpecFactory.Create(
                ScheduleSpecFactory.ReadBlock(security, index), issue, maturity,
                leg.PaymentFrequency ?? terms.PaymentFrequency, _calendars)).ToArray();
            if (plans.All(plan => plan is null))
                return null;
            var schedules = new List<StructuredCashFlowLegSchedule>();
            for (var index = 0; index < legs.Count; index++)
            {
                var leg = legs[index];
                var curve = ResolveCurve(leg, curves);
                if (IsLegRateMissing(leg, curve))
                    throw DeclaredRefusal("A leg is missing its contractual rate or fixing.");
                var entries = plans[index] is not { } plan
                    ? BuildLegSchedule(leg, terms, scenario, issue, maturity, asOfDate, curve)
                    : DeclaredLegEntries(leg, terms, plan, scenario, asOfDate, curve);
                schedules.Add(new(leg.LegId, leg.RateKind, leg.Direction, entries, DescribeRateSource(leg, curve)));
            }
            return Result(legs.Count == 1 || legs.All(leg => leg.Direction is not null)
                ? BuildNetSchedule(legs, schedules) : [], schedules);
        }

        var single = ScheduleSpecFactory.Create(ScheduleSpecFactory.ReadBlock(security), issue, maturity,
            terms.PaymentFrequency, _calendars);
        if (single is null)
            return null;
        if (terms.HasStepCouponSchedule || terms.InflationIndex is not null)
            throw DeclaredRefusal("Step-coupon and inflation-linked terms require an authoritative schedule.");
        if (terms.CouponRate is not { } coupon)
            throw DeclaredRefusal("Contractual coupon rate is missing.");
        // A principal schedule contains dated cash amounts; shifting its dates would change that
        // separate contract. Until that integration is defined, never project it as a bullet.
        if (terms.HasPrincipalSchedule)
            throw DeclaredRefusal("Dated principal schedules require an authoritative schedule with declared conventions.");
        var periods = single.Schedule.Periods.Where(period => period.PaymentDate >= asOfDate).ToArray();
        var basis = terms.PrincipalFace is > 0m ? terms.PrincipalFace.Value : 100m;
        var balance = SecurityPrincipalBalance.OpeningOutstanding(security, terms, asOfDate);
        var rate = Math.Max(0m, DeclaredRate(coupon, single) + ScenarioRateShift(scenario));
        var sinker = source == StructuredCashFlowSourceKind.CalculatedSinker && terms.PrincipalSchedule is null;
        var reduction = periods.Length > 0 && sinker ? RoundCash(balance / periods.Length) : 0m;
        var output = new List<StructuredCashFlowScheduleEntry>();
        foreach (var period in periods)
        {
            var interest = RoundCash(balance * rate * DeclaredFraction(single, period, terms.DayCountConvention));
            var principal = period.IsFinal ? balance : sinker ? Math.Min(balance, reduction) : 0m;
            balance = RoundCash(balance - principal);
            output.Add(new(ToUtcDateTimeOffset(period.PaymentDate), principal, interest, RoundCash(balance / basis)));
        }
        return Result(output);
    }

    private static IReadOnlyList<StructuredCashFlowScheduleEntry> DeclaredLegEntries(
        StructuredCashFlowLeg leg, StructuredCashFlowTerms terms, ScheduleSpecFactory.Plan plan,
        StructuredCashFlowScenario scenario, DateOnly asOf, SecurityForwardCurve? curve)
    {
        var notional = leg.Notional ?? terms.PrincipalFace ?? 100m;
        if (notional <= 0m)
            throw DeclaredRefusal("Leg notional must be positive.");
        var output = new List<StructuredCashFlowScheduleEntry>();
        foreach (var period in plan.Schedule.Periods.Where(period => period.PaymentDate >= asOf))
        {
            var balance = leg.NotionalOn(period.AccrualStart, notional);
            decimal rate;
            if (leg.RateKind == CashFlowLegRateKind.Fixed)
                rate = DeclaredRate(leg.FixedRate!.Value, plan);
            else
            {
                var recorded = period.AccrualStart <= asOf ? leg.FixingOn(period.AccrualStart) : null;
                var alreadyReset = period.AccrualStart <= asOf && leg.CurrentIndexRate is not null;
                var projected = recorded is null && !alreadyReset ? curve?.RateAt(period.AccrualStart) : null;
                var index = recorded ?? projected ?? leg.CurrentIndexRate ?? throw DeclaredRefusal("Leg fixing is missing.");
                // Forward curves retain their existing rate convention; contract rates use the declared unit.
                rate = projected is not null ? NormalizeAnnualRate(index) : DeclaredRate(index, plan);
                rate += (leg.SpreadBps ?? 0m) / 10_000m + ScenarioRateShift(scenario);
            }
            var interest = RoundCash(balance * Math.Max(0m, rate) * DeclaredFraction(plan, period,
                leg.DayCountConvention ?? terms.DayCountConvention));
            var principal = !leg.ExchangesPrincipal ? 0m : period.IsFinal ? RoundCash(balance)
                : RoundCash(Math.Max(0m, balance - leg.NotionalOn(period.AccrualEnd, notional)));
            output.Add(new(ToUtcDateTimeOffset(period.PaymentDate), principal, interest,
                leg.ExchangesPrincipal && period.IsFinal ? 0m : 1m));
        }
        return output;
    }

    private static decimal DeclaredRate(decimal rate, ScheduleSpecFactory.Plan plan)
        => plan.RateIsPercent ? rate / 100m : rate;

    private static decimal DeclaredFraction(ScheduleSpecFactory.Plan plan, SchedulePeriod period, string? dayCount)
    {
        if (plan.LevelCash)
            return 1m / plan.Schedule.Spec.Tenor.PeriodsPerYear!.Value;
        if (!DayCountConventions.TryParse(dayCount, out var convention))
            throw DeclaredRefusal("DayCount coupon cash requires a known day-count convention.");
        if (!ScheduleDayCount.TryPeriodFraction(convention, period, plan.Schedule, out var fraction, out var refusal))
            throw DeclaredRefusal(refusal);
        return fraction;
    }

    private static ScheduleSpecFactory.RefusalException DeclaredRefusal(string reason) => new(reason);
}
