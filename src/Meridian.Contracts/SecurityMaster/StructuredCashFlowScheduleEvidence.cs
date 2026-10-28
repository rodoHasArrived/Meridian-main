using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>A stable coupon-period identity and its complete accrual, payment and reference dates.</summary>
public sealed record StructuredCashFlowPeriodEvidence(Guid PeriodId, SchedulePeriod Period);

/// <summary>
/// Value-only evidence of the declared inputs and calendar content used by one calculated stream.
/// Calendar implementations are never serialized; their stable ids and content hashes are retained.
/// The source-terms digest identifies the serialized retained-source payload, not a logical JSON normalization.
/// </summary>
public sealed record StructuredCashFlowScheduleEvidence(
    string StreamKey,
    string KernelVersion,
    long SourceSecurityVersion,
    int SourceTermsVersion,
    string SourceTermsHash,
    DateOnly EffectiveDate,
    DateOnly TerminationDate,
    string Tenor,
    string Direction,
    string RollConvention,
    string StubPolicy,
    DateOnly? FirstRegularDate,
    DateOnly? LastRegularDate,
    string BusinessDayConvention,
    bool AccrualAdjusted,
    int PaymentLagBusinessDays,
    string CouponRateUnit,
    string CouponCashBasis,
    string? DayCountConvention,
    IReadOnlyList<StructuredCashFlowPeriodEvidence> Periods,
    IReadOnlyList<ScheduleCalendarUsage> Calendars);
