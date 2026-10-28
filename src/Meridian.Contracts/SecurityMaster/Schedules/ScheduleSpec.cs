using System.Text.Json.Serialization;
using Meridian.Contracts.Calendars;

namespace Meridian.Contracts.SecurityMaster.Schedules;

/// <summary>Which end of the schedule the regular grid is generated from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScheduleDirection>))]
public enum ScheduleDirection
{
    /// <summary>Generate from the effective date (or first regular date) towards termination; any stub is at the end.</summary>
    Forward,

    /// <summary>Generate from the termination date (or last regular date) towards the effective date; any stub is at the start.</summary>
    Backward,
}

/// <summary>How successive grid dates are derived from the anchor.</summary>
public enum ScheduleAnchorMode
{
    /// <summary>Every grid date is the anchor plus k tenors, so month-end clamping never accumulates.</summary>
    Anchored,

    /// <summary>
    /// Every grid date is the previous grid date plus one tenor, so a clamp into a short month carries
    /// forward. This reproduces the legacy compounded generators and is restricted to forward,
    /// anchor-day, short-stub schedules without explicit regular dates.
    /// </summary>
    Compounded,
}

/// <summary>What happens when the regular grid does not fit the schedule's span exactly.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScheduleStubPolicy>))]
public enum ScheduleStubPolicy
{
    /// <summary>The remainder becomes a short stub.</summary>
    Short,

    /// <summary>The remainder is merged into the adjacent regular period, giving a long stub.</summary>
    Long,

    /// <summary>A remainder is refused with <see cref="ScheduleFailureCode.DoesNotFitRegularGrid"/>.</summary>
    RequireRegular,
}

/// <summary>
/// The inputs of the contractual schedule kernel: the span, tenor, generation direction and mode,
/// roll, stub policy, optional explicit regular dates, business-day adjustments and payment lag.
/// </summary>
/// <remarks>
/// <para>
/// A generated stub sits where the generation direction ends: a final stub when
/// <see cref="ScheduleDirection.Forward"/>, an initial stub when <see cref="ScheduleDirection.Backward"/>.
/// An explicit <see cref="FirstRegularDate"/> or <see cref="LastRegularDate"/> declares a stub at that
/// end instead.
/// </para>
/// <para>
/// A null adjustment means unadjusted. <see cref="EffectiveDateAdjustment"/> and
/// <see cref="TerminationDateAdjustment"/> default to <see cref="AccrualAdjustment"/>. A payment lag
/// counts business days of the <see cref="PaymentAdjustment"/> calendar from the adjusted accrual end.
/// </para>
/// </remarks>
/// <param name="EffectiveDate">The unadjusted start of the first period.</param>
/// <param name="TerminationDate">The unadjusted end of the last period.</param>
/// <param name="Tenor">The regular period length.</param>
/// <param name="Direction">Which end the grid is generated from.</param>
/// <param name="AnchorMode">Whether grid dates are anchored or compounded.</param>
/// <param name="Roll">Where month-based grid dates fall within their month.</param>
/// <param name="StubPolicy">How a remainder that does not fit the grid is handled.</param>
/// <param name="FirstRegularDate">The start of the first regular period, declaring an initial stub before it.</param>
/// <param name="LastRegularDate">The end of the last regular period, declaring a final stub after it.</param>
/// <param name="AccrualAdjustment">The adjustment for inner accrual boundaries.</param>
/// <param name="PaymentAdjustment">The adjustment (and calendar) for payment dates.</param>
/// <param name="EffectiveDateAdjustment">The adjustment for the effective date; defaults to <paramref name="AccrualAdjustment"/>.</param>
/// <param name="TerminationDateAdjustment">The adjustment for the termination date; defaults to <paramref name="AccrualAdjustment"/>.</param>
/// <param name="PaymentLagBusinessDays">Business days between the adjusted accrual end and payment.</param>
/// <param name="MaxPeriods">The most periods the schedule may have, or null for no limit.</param>
public sealed record ScheduleSpec(
    DateOnly EffectiveDate,
    DateOnly TerminationDate,
    ScheduleTenor Tenor,
    ScheduleDirection Direction = ScheduleDirection.Forward,
    ScheduleAnchorMode AnchorMode = ScheduleAnchorMode.Anchored,
    ScheduleRoll Roll = default,
    ScheduleStubPolicy StubPolicy = ScheduleStubPolicy.Short,
    DateOnly? FirstRegularDate = null,
    DateOnly? LastRegularDate = null,
    BusinessDayAdjustment? AccrualAdjustment = null,
    BusinessDayAdjustment? PaymentAdjustment = null,
    BusinessDayAdjustment? EffectiveDateAdjustment = null,
    BusinessDayAdjustment? TerminationDateAdjustment = null,
    int PaymentLagBusinessDays = 0,
    int? MaxPeriods = ScheduleSpec.DefaultMaxPeriods)
{
    /// <summary>The default period limit, matching the 1200-period bound of the legacy constant-yield generators.</summary>
    public const int DefaultMaxPeriods = 1200;

    /// <summary>
    /// The Security Master coupon-period roll: forward from the issue date in anchored mode, anchor-day
    /// roll, a short final stub, no adjustments and no period limit.
    /// </summary>
    /// <remarks>
    /// Every payment date is anchored on the issue date rather than compounded from the previous
    /// payment date. <see cref="DateOnly.AddMonths"/> clamps a month-end anchor into shorter months
    /// (31 Jan plus three months is 30 Apr), and compounding that clamp walks the schedule earlier
    /// every period. For a bond issued on the 31st that drift lands the final period one day before
    /// maturity and emits a spurious one-day stub, so a one-year quarterly bond yields five payments
    /// instead of four. Anchoring keeps each period on the issue day-of-month, clamping only where
    /// the target month is shorter.
    /// </remarks>
    public static ScheduleSpec IssueAnchoredForward(DateOnly issueDate, DateOnly maturity, ScheduleTenor tenor)
        => new(
            issueDate,
            maturity,
            tenor,
            ScheduleDirection.Forward,
            ScheduleAnchorMode.Anchored,
            ScheduleRoll.AnchorDay,
            ScheduleStubPolicy.Short,
            MaxPeriods: null);

    /// <summary>
    /// The legacy compounded roll: forward from <paramref name="start"/>, each date one tenor after the
    /// previous one, anchor-day roll, a short final stub, no adjustments and no period limit.
    /// </summary>
    public static ScheduleSpec CompoundedForward(DateOnly start, DateOnly termination, ScheduleTenor tenor)
        => new(
            start,
            termination,
            tenor,
            ScheduleDirection.Forward,
            ScheduleAnchorMode.Compounded,
            ScheduleRoll.AnchorDay,
            ScheduleStubPolicy.Short,
            MaxPeriods: null);

    /// <summary>
    /// The market bond convention: generated backward from <paramref name="termination"/> in anchored
    /// mode with <paramref name="roll"/>, any stub at the start, no adjustments and the default period
    /// limit.
    /// </summary>
    public static ScheduleSpec BackwardFromTermination(
        DateOnly effective,
        DateOnly termination,
        ScheduleTenor tenor,
        ScheduleRoll roll,
        ScheduleStubPolicy stubPolicy = ScheduleStubPolicy.Short)
        => new(
            effective,
            termination,
            tenor,
            ScheduleDirection.Backward,
            ScheduleAnchorMode.Anchored,
            roll,
            stubPolicy);
}
