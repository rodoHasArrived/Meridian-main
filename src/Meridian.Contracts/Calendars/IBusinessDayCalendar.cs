namespace Meridian.Contracts.Calendars;

/// <summary>
/// A named, content-addressed business-day calendar. Implementations are pure: they read no clock
/// and perform no I/O, so the same calendar answers the same question the same way on every node.
/// </summary>
/// <remarks>
/// Roll, add and count arithmetic lives in the <see cref="BusinessDayCalendars"/> extension methods,
/// so every implementation shares one definition of Following, Preceding and the modified variants.
/// </remarks>
public interface IBusinessDayCalendar
{
    /// <summary>The calendar's stable identifier, for example <c>WEEKENDS</c> or a joint id such as <c>A+B</c>.</summary>
    string CalendarId { get; }

    /// <summary>
    /// The canonical (lowercase hex) SHA-256 digest of the calendar's content, so evidence can show
    /// exactly which holiday data produced a schedule.
    /// </summary>
    string ContentHash { get; }

    /// <summary>The date range the calendar's holiday data is known to be complete for.</summary>
    CalendarCoverage Coverage { get; }

    /// <summary>Whether <paramref name="date"/> is a business day under this calendar.</summary>
    bool IsBusinessDay(DateOnly date);
}
