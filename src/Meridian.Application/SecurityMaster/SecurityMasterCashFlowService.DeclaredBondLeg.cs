using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterCashFlowService
{
    private static StructuredCashFlowProjectionDto BuildDeclaredBondLegProjection(
        SecurityDetailDto security, StructuredCashFlowSourceKind sourceKind, StructuredCashFlowScenario scenario,
        StructuredCashFlowStaleness staleness, DateTimeOffset? sourceLastUpdatedUtc, StructuredCashFlowTerms terms,
        DeclaredScheduleResolution declared, DateTimeOffset asOf, DateOnly asOfDate,
        IReadOnlyDictionary<string, SecurityForwardCurve>? curves, bool unifiedEconomics = false)
    {
        StructuredCashFlowProjectionDto Blocked(string reason) => new(security.SecurityId, sourceKind, scenario,
            asOf, [], staleness, sourceLastUpdatedUtc, terms.FactorSchedule, terms, BlockedReason: reason);
        if (terms.Legs is not { Count: 1 } || terms.Legs[0].LegId != "bond-coupon")
            return Blocked("Declared leg schedules currently require one canonical floating bond coupon leg.");
        if (terms.HasPrincipalSchedule || terms.CurrentFactor is not null && terms.CurrentFactor != 1m || terms.HasFactorSchedule)
            return Blocked("Declared floating bond principal schedules and paydown factors require a supported principal-event model.");
        var leg = terms.Legs[0];
        if (leg.NotionalSchedule is { Count: > 0 })
            return Blocked("Declared floating bond notional changes require a supported principal-event model.");
        if (!DayCountConventions.TryParse(leg.DayCountConvention ?? terms.DayCountConvention, out var dayCount))
            return Blocked("Declared floating bond interest requires a recognized day-count convention.");
        if (unifiedEconomics && leg.RateUnit == SecurityCouponRateUnit.Unspecified)
            return Blocked("Declared floating coupons require explicit Percent or Fraction rate units.");
        if (unifiedEconomics && leg.ResetSchedule?.GroupBy(row => row.ResetDate).Any(group => group.Count() > 1) == true)
            return Blocked("Floating reset dates must be unique.");
        var notional = leg.Notional ?? terms.PrincipalFace;
        if (notional is not > 0m)
            return Blocked("Declared floating bond interest requires a retained positive principal basis.");
        var schedule = ScheduleGenerator.Generate(declared.Spec!);
        if (!schedule.Succeeded)
            return Blocked("Declared floating bond schedule could not be generated: " + schedule.Failure.Message);
        var curve = ResolveCurve(leg, curves);
        var entries = new List<StructuredCashFlowScheduleEntry>();
        var windows = new List<(DateOnly Start, DateOnly End)>();
        foreach (var period in schedule.Periods.Where(period => period.PaymentDate >= asOfDate))
        {
            var alreadyReset = period.AccrualStart <= asOfDate;
            var exactReset = leg.ResetSchedule?.FirstOrDefault(row => row.ResetDate == period.AccrualStart);
            var retainedScalar = !leg.HasDeclaredResetSchedule && leg.CurrentIndexResetDate == period.AccrualStart
                && !string.IsNullOrWhiteSpace(leg.CurrentIndexRateSource) ? leg.CurrentIndexRate : null;
            var index = alreadyReset
                ? unifiedEconomics ? exactReset is not null ? exactReset.FixingRate : retainedScalar
                    : leg.FixingOn(period.AccrualStart) ?? leg.CurrentIndexRate
                : curve?.RateAt(period.AccrualStart);
            // A projection is not allowed to turn an unobserved past fixing into a forecast,
            // or silently carry today's fixing into future periods without a forecast curve.
            if (index is null)
                return Blocked(alreadyReset
                ? "An observed fixing is missing for a floating bond period that has already reset."
                : "A retained forward curve is required for future floating bond resets.");
            var indexFraction = curve is not null && !alreadyReset ? index.Value
                : leg.RateUnit == SecurityCouponRateUnit.Percent ? index.Value / 100m
                : leg.RateUnit == SecurityCouponRateUnit.Fraction ? index.Value
                : NormalizeAnnualRate(index.Value);
            var rate = indexFraction + leg.SpreadBps.GetValueOrDefault() / 10_000m + (alreadyReset ? 0m : ScenarioRateShift(scenario));
            if (leg.CapRate is { } cap)
                rate = Math.Min(rate, leg.RateUnit == SecurityCouponRateUnit.Percent ? cap / 100m : cap);
            if (leg.FloorRate is { } floor)
                rate = Math.Max(rate, leg.RateUnit == SecurityCouponRateUnit.Percent ? floor / 100m : floor);
            if (rate < 0m)
                return Blocked("Negative floating coupon cash flows require a separately supported signed-interest model.");
            if (!ScheduleDayCount.TryPeriodFraction(dayCount, period, schedule, out var fraction, out var refusal))
                return Blocked("Declared floating bond interest could not be computed: " + refusal);
            BondFloatingRateEvidence? rateEvidence = null;
            if (unifiedEconomics)
            {
                var source = alreadyReset
                    ? exactReset is not null ? $"SecurityTerms:{security.SecurityId:D}:{security.Version}"
                        : leg.CurrentIndexRateSource!
                    : curve!.RetainedInput?.ContentSha256 ?? Sha256Digest.ComputeUtf8(
                        System.Text.Json.JsonSerializer.Serialize(curve));
                rateEvidence = new(period.AccrualStart, alreadyReset, leg.IndexName ?? string.Empty,
                    indexFraction, leg.SpreadBps.GetValueOrDefault() / 10_000m,
                    leg.CapRate is { } c ? leg.RateUnit == SecurityCouponRateUnit.Percent ? c / 100m : c : null,
                    leg.FloorRate is { } f ? leg.RateUnit == SecurityCouponRateUnit.Percent ? f / 100m : f : null,
                    notional.Value, fraction, leg.RateUnit, source);
                if (!rateEvidence.TryCoupon(ScenarioRateShift(scenario), out _, out var evidenceRefusal))
                    return Blocked(evidenceRefusal!);
                // A scenario receipt retains the shifted forecast itself so a later consumer never
                // unknowingly applies its original shift twice.
                if (!alreadyReset)
                    rateEvidence = rateEvidence with { IndexRate = indexFraction + ScenarioRateShift(scenario) };
            }
            if (dayCount == DayCountConvention.Business252)
                windows.Add((period.AccrualStart, period.AccrualEnd));
            entries.Add(new StructuredCashFlowScheduleEntry(ToUtcDateTimeOffset(period.PaymentDate),
                period.IsFinal && leg.ExchangesPrincipal ? notional.Value : 0m,
                RoundCash(notional.Value * rate * fraction), period.IsFinal ? 0m : 1m)
            {
                UnadjustedStart = period.UnadjustedStart,
                UnadjustedEnd = period.UnadjustedEnd,
                AccrualStart = period.AccrualStart,
                AccrualEnd = period.AccrualEnd,
                RateEvidence = rateEvidence
            });
        }
        var calendar = declared.Spec!.AccrualAdjustment?.Calendar ?? declared.Spec.PaymentAdjustment?.Calendar;
        return new StructuredCashFlowProjectionDto(security.SecurityId, sourceKind, scenario, asOf, entries,
            staleness, sourceLastUpdatedUtc, terms.FactorSchedule, terms,
            [new StructuredCashFlowLegSchedule(leg.LegId, leg.RateKind, leg.Direction, entries, DescribeRateSource(leg, curve))])
        {
            ScheduleEvidence = BuildScheduleEvidence(schedule, windows.Count > 0 ? calendar : null, windows)
        };
    }
}
