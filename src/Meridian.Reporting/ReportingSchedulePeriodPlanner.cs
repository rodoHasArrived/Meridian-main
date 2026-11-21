using Meridian.Contracts.Workstation;
using Meridian.Ledger;

namespace Meridian.Reporting;

/// <summary>The next occurrence of a period-close reporting schedule.</summary>
public readonly record struct ReportingSchedulePeriodOccurrence(
    string PeriodId,
    DateOnly PeriodEnd,
    DateTimeOffset DueAtUtc);

/// <summary>
/// Advances period-close reporting schedules ("as of period end, due N days later") using the
/// ledger's period boundaries, so the governed reporting scheduler and ledger report exports
/// share one definition of a monthly, quarterly, or annual period.
/// </summary>
public static class ReportingSchedulePeriodPlanner
{
    /// <summary>Largest supported gap between a period end and its due date.</summary>
    public const int MaximumDueDaysAfterPeriodEnd = 366;

    public static bool IsValid(ReportingSchedulePeriodCadenceDto cadence) =>
        cadence is not null
        && Enum.IsDefined(cadence.Frequency)
        && cadence.DueDaysAfterPeriodEnd is >= 0 and <= MaximumDueDaysAfterPeriodEnd;

    /// <summary>
    /// Returns the occurrence that follows the period ending on <paramref name="currentPeriodEnd"/>.
    /// The due time keeps the wall-clock time of <paramref name="currentDueAtUtc"/> in
    /// <paramref name="timeZone"/>, so a schedule due at 08:00 local stays at 08:00 local
    /// across daylight-saving changes. Pass <paramref name="intendedLocalTime"/> when the
    /// configured time of day is known: the previous due instant may itself have been moved
    /// past a spring-forward gap, and reusing it would shift every later run.
    /// </summary>
    public static ReportingSchedulePeriodOccurrence Next(
        ReportingSchedulePeriodCadenceDto cadence,
        DateOnly currentPeriodEnd,
        DateTimeOffset currentDueAtUtc,
        TimeZoneInfo timeZone,
        TimeOnly? intendedLocalTime = null)
    {
        ArgumentNullException.ThrowIfNull(cadence);
        ArgumentNullException.ThrowIfNull(timeZone);
        if (!IsValid(cadence))
        {
            throw new ArgumentException("The reporting schedule period cadence is invalid.", nameof(cadence));
        }

        var period = LedgerReportSchedulePlanner.PeriodStartingOn(
            ToLedgerFrequency(cadence.Frequency),
            currentPeriodEnd.AddDays(1));
        var localTimeOfDay = intendedLocalTime
            ?? TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(currentDueAtUtc, timeZone).DateTime);
        return new ReportingSchedulePeriodOccurrence(
            period.PeriodId,
            period.PeriodEnd,
            DueAtUtc(period.PeriodEnd, cadence.DueDaysAfterPeriodEnd, timeZone, localTimeOfDay));
    }

    /// <summary>
    /// The due instant for a period ending on <paramref name="periodEnd"/>: that many days later
    /// at <paramref name="localTimeOfDay"/> in <paramref name="timeZone"/>. Used directly when the
    /// period end comes from the ledger's durable accounting periods rather than the calendar.
    /// </summary>
    public static DateTimeOffset DueAtUtc(
        DateOnly periodEnd,
        int dueDaysAfterPeriodEnd,
        TimeZoneInfo timeZone,
        TimeOnly localTimeOfDay)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var localDue = periodEnd
            .AddDays(dueDaysAfterPeriodEnd)
            .ToDateTime(localTimeOfDay, DateTimeKind.Unspecified);
        // A wall time skipped by a spring-forward change does not exist; run at the first
        // valid time after the gap instead.
        while (timeZone.IsInvalidTime(localDue))
        {
            localDue = localDue.AddMinutes(30);
        }

        return new DateTimeOffset(localDue, timeZone.GetUtcOffset(localDue)).ToUniversalTime();
    }

    private static LedgerReportScheduleFrequency ToLedgerFrequency(
        ReportingSchedulePeriodFrequencyDto frequency) =>
        frequency switch
        {
            ReportingSchedulePeriodFrequencyDto.Monthly => LedgerReportScheduleFrequency.Monthly,
            ReportingSchedulePeriodFrequencyDto.Quarterly => LedgerReportScheduleFrequency.Quarterly,
            ReportingSchedulePeriodFrequencyDto.Annually => LedgerReportScheduleFrequency.Annually,
            _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Unsupported period frequency."),
        };
}
