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
    /// across daylight-saving changes.
    /// </summary>
    public static ReportingSchedulePeriodOccurrence Next(
        ReportingSchedulePeriodCadenceDto cadence,
        DateOnly currentPeriodEnd,
        DateTimeOffset currentDueAtUtc,
        TimeZoneInfo timeZone)
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
        var localTimeOfDay = TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(currentDueAtUtc, timeZone).DateTime);
        var localDue = period.PeriodEnd
            .AddDays(cadence.DueDaysAfterPeriodEnd)
            .ToDateTime(localTimeOfDay, DateTimeKind.Unspecified);
        // A wall time skipped by a spring-forward change does not exist; run at the first
        // valid time after the gap instead.
        while (timeZone.IsInvalidTime(localDue))
        {
            localDue = localDue.AddMinutes(30);
        }

        var dueAtUtc = new DateTimeOffset(localDue, timeZone.GetUtcOffset(localDue)).ToUniversalTime();
        return new ReportingSchedulePeriodOccurrence(period.PeriodId, period.PeriodEnd, dueAtUtc);
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
