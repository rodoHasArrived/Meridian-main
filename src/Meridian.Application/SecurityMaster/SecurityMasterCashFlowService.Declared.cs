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
        var evidence = new List<StructuredCashFlowScheduleEvidence>();
        StructuredCashFlowProjectionDto Result(IReadOnlyList<StructuredCashFlowScheduleEntry> entries,
            IReadOnlyList<StructuredCashFlowLegSchedule>? legs = null) => new(
                security.SecurityId, source, scenario, asOf, entries, staleness, updated,
                terms.FactorSchedule, terms, legs,
                BlockedReason: "Declared schedules are preview-only until calendar and convention evidence is retained.",
                ScheduleEvidence: evidence);
        if (terms.HasLegs)
        {
            var legs = terms.Legs!;
            var plans = legs.Select((leg, index) => ScheduleSpecFactory.Create(
                ScheduleSpecFactory.ReadBlock(security, index), issue, maturity,
                leg.PaymentFrequency ?? terms.PaymentFrequency, _calendars)).ToArray();
            if (plans.All(plan => plan is null))
                return null;
            if (legs.GroupBy(leg => leg.LegId.Trim(), StringComparer.Ordinal).Any(group => group.Count() > 1))
                throw DeclaredRefusal("Declared cash-flow leg identities must be unique within the projection.");
            var schedules = new List<StructuredCashFlowLegSchedule>();
            for (var index = 0; index < legs.Count; index++)
            {
                var leg = legs[index];
                if (plans[index] is { } declaredPlan)
                    evidence.Add(ScheduleProjectionEvidence.Create(security, leg.LegId, declaredPlan,
                        leg.DayCountConvention ?? terms.DayCountConvention));
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
        evidence.Add(ScheduleProjectionEvidence.Create(security, "coupon", single, terms.DayCountConvention));
        if (terms.HasStepCouponSchedule || terms.InflationIndex is not null)
            throw DeclaredRefusal("Step-coupon and inflation-linked terms require an authoritative schedule.");
        if (terms.CouponRate is not { } coupon)
            throw DeclaredRefusal("Contractual coupon rate is missing.");
        if (terms.HasPrincipalSchedule)
        {
            if (terms.PrincipalFace is not > 0m)
                throw DeclaredRefusal("Dated principal amounts require a retained principal basis; normalized 100 is not contractual face.");
            return Result(DeclaredPrincipalEntries(security, terms, single, asOfDate, issue, maturity,
                Math.Max(0m, DeclaredRate(coupon, single) + ScenarioRateShift(scenario))));
        }
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

    private static decimal DeclaredFraction(
        ScheduleSpecFactory.Plan plan, SchedulePeriod period, string? dayCount,
        DateOnly? from = null, DateOnly? through = null)
    {
        if (plan.LevelCash)
        {
            // Level cash is nominal: a regular period pays annual coupon / frequency regardless
            // of holiday moves. Stubs and principal sub-periods prorate on the unadjusted grid's
            // reference periods using the canonical ICMA overlap calculation.
            var context = new DayCountPeriodContext(period.ReferencePeriods, plan.Schedule.Spec.Tenor.PeriodsPerYear);
            if (!DayCountConventions.TryFraction(DayCountConvention.ActualActualIcma,
                from ?? period.UnadjustedStart, through ?? period.UnadjustedEnd, context,
                out var levelFraction, out var levelRefusal))
                throw DeclaredRefusal(levelRefusal);
            return levelFraction;
        }
        if (!DayCountConventions.TryParse(dayCount, out var convention))
            throw DeclaredRefusal("DayCount coupon cash requires a known day-count convention.");
        if (!ScheduleDayCount.TryFraction(convention, period, plan.Schedule,
            from ?? period.AccrualStart, through ?? period.AccrualEnd, out var fraction, out var refusal))
            throw DeclaredRefusal(refusal);
        return fraction;
    }

    private static IReadOnlyList<StructuredCashFlowScheduleEntry> DeclaredPrincipalEntries(
        SecurityDetailDto security, StructuredCashFlowTerms terms, ScheduleSpecFactory.Plan plan,
        DateOnly asOf, DateOnly issue, DateOnly maturity, decimal rate)
    {
        if (plan.Schedule.Periods.Count == 0)
            return [];
        var periods = plan.Schedule.Periods.Where(period => period.PaymentDate >= asOf).ToArray();
        var firstStart = periods.Length == 0 ? asOf : WindowStart(periods[0]);
        var basis = terms.PrincipalFace!.Value;
        var opening = SecurityPrincipalBalance.OpeningOutstanding(security, terms, asOf);
        var principal = terms.PrincipalSchedule!
            .Where(entry => entry.PaymentDate >= issue && entry.PaymentDate <= maturity && entry.PaymentDate >= firstStart)
            .GroupBy(entry => entry.PaymentDate)
            .Select(group => new StructuredPrincipalScheduleEntry(group.Key, RoundCash(group.Sum(entry => entry.Amount))))
            .OrderBy(entry => entry.PaymentDate)
            .ToArray();
        var completed = principal.Where(entry => entry.PaymentDate < asOf).Sum(entry => entry.Amount);
        var accrualOpening = Math.Min(Math.Max(basis, opening), opening + completed);
        var applied = new List<StructuredPrincipalScheduleEntry>(principal.Length);
        var balance = accrualOpening;
        foreach (var entry in principal)
        {
            var amount = Math.Min(balance, entry.Amount);
            applied.Add(new(entry.PaymentDate, amount));
            balance = RoundCash(balance - amount);
        }
        if (RoundCash(accrualOpening - applied.Where(entry => entry.PaymentDate < asOf).Sum(entry => entry.Amount)) != opening)
            throw DeclaredRefusal("Current factor and completed principal payments do not establish a consistent accrual balance.");

        // Dated factors are additional retained evidence, not permission to invent principal
        // movements missing from this cash contract. Refuse a balance history that the dates and
        // amounts cannot explain, including future changes the projection would otherwise ignore.
        // The shared balance reader dates the scalar only from asset terms (including profile
        // fields), not commonTerms; preserve that same precedence when checking its evidence.
        var factorDate = SecurityTermReader.ReadDate(
            StructuredCashFlowTermsResolver.EnumerateTermSources(security).SkipLast(1).ToArray(), ["factorDate", "currentFactorDate"]);
        var datedFactors = terms.FactorSchedule
            .Select(entry => (Date: entry.AsOfDate, entry.Factor)).ToList();
        if (terms.CurrentFactor is >= 0m && factorDate is { } scalarDate
            && !datedFactors.Any(entry => entry.Date <= scalarDate))
            datedFactors.Add((scalarDate, terms.CurrentFactor.Value));
        if (datedFactors.Any(entry => entry.Date <= firstStart)
            && SecurityPrincipalBalance.OpeningOutstanding(security, terms, firstStart) != accrualOpening)
            throw DeclaredRefusal("Retained factor history contradicts the reconstructed opening accrual balance.");
        foreach (var factor in datedFactors.Where(entry => entry.Date >= firstStart && entry.Date <= maturity))
        {
            var expected = RoundCash(accrualOpening - applied.Where(entry => entry.PaymentDate <= factor.Date).Sum(entry => entry.Amount));
            if (expected != RoundCash(basis * factor.Factor))
                throw DeclaredRefusal("Retained factor changes require matching dated principal movements.");
        }

        var byDate = new SortedDictionary<DateOnly, (decimal Principal, decimal Interest)>();
        foreach (var entry in applied.Where(entry => entry.PaymentDate >= asOf && entry.Amount > 0m))
            Add(entry.PaymentDate, entry.Amount, 0m);
        foreach (var period in periods)
        {
            var start = WindowStart(period);
            var cursor = start;
            var end = plan.LevelCash ? period.UnadjustedEnd : period.AccrualEnd;
            var accrualBalance = RoundCash(accrualOpening - applied.Where(entry => entry.PaymentDate <= cursor).Sum(entry => entry.Amount));
            var interest = 0m;
            foreach (var entry in applied.Where(entry => entry.PaymentDate > start && entry.PaymentDate < end))
            {
                interest += accrualBalance * rate * DeclaredFraction(plan, period, terms.DayCountConvention, cursor, entry.PaymentDate);
                accrualBalance = RoundCash(accrualBalance - entry.Amount);
                cursor = entry.PaymentDate;
            }
            interest += accrualBalance * rate * DeclaredFraction(plan, period, terms.DayCountConvention, cursor, end);
            Add(period.PaymentDate, 0m, RoundCash(interest));
        }

        // Explicit principal rows already state cash dates and never inherit a coupon adjustment
        // or lag. Only the unscheduled redemption follows the final coupon's payment date.
        var finalPayment = plan.Schedule.Periods[^1].PaymentDate;
        if (finalPayment >= asOf)
            Add(finalPayment, RoundCash(opening - applied.Where(entry => entry.PaymentDate >= asOf).Sum(entry => entry.Amount)), 0m);
        balance = opening;
        var output = new List<StructuredCashFlowScheduleEntry>(byDate.Count);
        foreach (var (date, cash) in byDate)
        {
            balance = RoundCash(balance - cash.Principal);
            output.Add(new(ToUtcDateTimeOffset(date), cash.Principal, cash.Interest, RoundCash(balance / basis)));
        }
        return output;

        DateOnly WindowStart(SchedulePeriod period) => plan.LevelCash ? period.UnadjustedStart : period.AccrualStart;

        void Add(DateOnly date, decimal principalAmount, decimal interest)
        {
            byDate.TryGetValue(date, out var existing);
            byDate[date] = (existing.Principal + principalAmount, existing.Interest + interest);
        }
    }

    private static ScheduleSpecFactory.RefusalException DeclaredRefusal(string reason) => new(reason);
}
