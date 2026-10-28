using System.Diagnostics.CodeAnalysis;

namespace Meridian.Contracts.SecurityMaster.Schedules;

/// <summary>Why the schedule kernel refused a <see cref="ScheduleSpec"/>.</summary>
public enum ScheduleFailureCode
{
    /// <summary><see cref="ScheduleSpec.MaxPeriods"/> is set below 1.</summary>
    InvalidPeriodLimit,

    /// <summary><see cref="ScheduleSpec.PaymentLagBusinessDays"/> is negative.</summary>
    NegativePaymentLag,

    /// <summary>A term tenor was given explicit regular dates.</summary>
    TermTenorDisallowsRegularDates,

    /// <summary>A day or week tenor was asked to generate backward.</summary>
    TenorNotAllowedForDirection,

    /// <summary>A day or week tenor was given a roll other than the anchor day.</summary>
    RollNotAllowedForTenor,

    /// <summary>Compounded mode was combined with backward generation, a roll, explicit regular dates or a non-short stub policy.</summary>
    CompoundedModeRestricted,

    /// <summary>An explicit regular date lies outside the schedule span or out of order.</summary>
    RegularDateOutOfRange,

    /// <summary>The generation direction conflicts with the explicit regular dates given.</summary>
    DirectionConflictsWithRegularDates,

    /// <summary>A business-day convention other than unadjusted has no calendar.</summary>
    AdjustmentRequiresCalendar,

    /// <summary>A payment lag was set without a payment calendar.</summary>
    PaymentLagRequiresCalendar,

    /// <summary>The generation anchor does not satisfy the roll convention.</summary>
    AnchorNotOnRollDay,

    /// <summary>The last regular date is not on the grid generated from the first regular date.</summary>
    RegularDatesNotOnGrid,

    /// <summary>The stub policy requires a regular grid and the span does not fit one.</summary>
    DoesNotFitRegularGrid,

    /// <summary>The schedule would exceed <see cref="ScheduleSpec.MaxPeriods"/>.</summary>
    PeriodLimitExceeded,

    /// <summary>Business-day adjustment made a period's accrual end fall on or before its start.</summary>
    AdjustedPeriodCollapsed,

    /// <summary>The spec is structurally invalid: a default tenor or an undefined enum value.</summary>
    InvalidSpecification,

    /// <summary>A business-day adjustment or payment lag could not be applied: the calendar is degenerate or the walk leaves the supported date range.</summary>
    BusinessDayAdjustmentFailed,
}

/// <summary>A refusal from the schedule kernel.</summary>
/// <param name="Code">The refusal code.</param>
/// <param name="Message">A human-readable explanation.</param>
/// <param name="Date">The date the refusal concerns, when there is one.</param>
public sealed record ScheduleFailure(ScheduleFailureCode Code, string Message, DateOnly? Date = null);

/// <summary>A non-fatal observation recorded while generating a schedule.</summary>
public enum ScheduleDiagnosticCode
{
    /// <summary>A calendar was asked about a date outside its declared coverage.</summary>
    CalendarCoverageExceeded,
}

/// <summary>A non-fatal observation recorded while generating a schedule.</summary>
/// <param name="Code">The diagnostic code.</param>
/// <param name="CalendarId">The calendar the diagnostic concerns.</param>
/// <param name="Date">The date the diagnostic concerns.</param>
/// <param name="Message">A human-readable explanation.</param>
public sealed record ScheduleDiagnostic(ScheduleDiagnosticCode Code, string CalendarId, DateOnly Date, string Message);

/// <summary>What a calendar was used for while generating a schedule.</summary>
public enum ScheduleCalendarPurpose
{
    /// <summary>Adjusting inner accrual boundaries.</summary>
    Accrual,

    /// <summary>Adjusting the effective date.</summary>
    EffectiveDate,

    /// <summary>Adjusting the termination date.</summary>
    TerminationDate,

    /// <summary>Adjusting or lagging payment dates.</summary>
    Payment,
}

/// <summary>Evidence of one calendar, identified by id and content hash, used for one purpose.</summary>
/// <param name="CalendarId">The calendar id.</param>
/// <param name="ContentHash">The calendar's content hash.</param>
/// <param name="Purpose">What the calendar was used for.</param>
public sealed record ScheduleCalendarUsage(string CalendarId, string ContentHash, ScheduleCalendarPurpose Purpose);

/// <summary>
/// The output of <see cref="ScheduleGenerator.Generate"/>: the periods on success, or a failure, plus
/// the diagnostics and calendar evidence of the run.
/// </summary>
/// <param name="Spec">The spec the schedule was generated from.</param>
/// <param name="Periods">The periods in order; empty on failure.</param>
/// <param name="Failure">The refusal, or null on success.</param>
/// <param name="Diagnostics">Non-fatal observations, such as calendar coverage being exceeded.</param>
/// <param name="CalendarsUsed">Each distinct calendar and purpose actually used.</param>
public sealed record ScheduleGenerationResult(
    ScheduleSpec Spec,
    IReadOnlyList<SchedulePeriod> Periods,
    ScheduleFailure? Failure,
    IReadOnlyList<ScheduleDiagnostic> Diagnostics,
    IReadOnlyList<ScheduleCalendarUsage> CalendarsUsed)
{
    /// <summary>Whether generation succeeded; when false, <see cref="Failure"/> is set.</summary>
    [MemberNotNullWhen(false, nameof(Failure))]
    public bool Succeeded => Failure is null;

    /// <summary>The kernel version that produced the schedule.</summary>
    public string KernelVersion => ScheduleGenerator.KernelVersion;
}
