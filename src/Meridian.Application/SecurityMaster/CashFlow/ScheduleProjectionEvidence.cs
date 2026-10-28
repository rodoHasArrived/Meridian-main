using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Application.SecurityMaster.CashFlow;

internal static class ScheduleProjectionEvidence
{
    internal static StructuredCashFlowScheduleEvidence Create(
        SecurityDetailDto security, string streamKey, ScheduleSpecFactory.Plan plan, string? dayCount)
    {
        var spec = plan.Schedule.Spec;
        var source = JsonSerializer.Serialize(new { security.AssetClass, security.CommonTerms, security.AssetSpecificTerms });
        return new StructuredCashFlowScheduleEvidence(
            streamKey, plan.Schedule.KernelVersion, security.Version,
            SecurityTermsEnvelope.ResolveAssetClassTermsVersion(security.AssetSpecificTerms),
            Sha256Digest.ComputeUtf8(source), spec.EffectiveDate, spec.TerminationDate,
            spec.Tenor.ToString(), spec.Direction.ToString(), spec.Roll.ToString(), spec.StubPolicy.ToString(),
            spec.FirstRegularDate, spec.LastRegularDate,
            (spec.PaymentAdjustment?.Convention ?? Meridian.Contracts.Calendars.BusinessDayConvention.Unadjusted).ToString(),
            spec.AccrualAdjustment is { IsUnadjusted: false }, spec.PaymentLagBusinessDays,
            plan.RateIsPercent ? "Percent" : "Fraction", plan.LevelCash ? "Level" : "DayCount", dayCount,
            plan.Schedule.Periods.Select(period => new StructuredCashFlowPeriodEvidence(
                SchedulePeriodId.Derive(security.SecurityId, streamKey, period), period)).ToArray(),
            plan.Schedule.CalendarsUsed.ToArray());
    }
}
