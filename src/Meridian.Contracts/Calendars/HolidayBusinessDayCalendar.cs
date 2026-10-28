using System.Globalization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Text;

namespace Meridian.Contracts.Calendars;

/// <summary>
/// A business-day calendar defined by a weekend mask and an explicit holiday set, versioned and
/// content-hashed so schedule evidence can name the exact calendar data it used.
/// </summary>
/// <remarks>
/// <para>
/// Validation follows the reconciliation floor's calendar: an undefined <see cref="DayOfWeek"/>, or a
/// weekend of all seven days, throws <see cref="ArgumentException"/>.
/// </para>
/// <para>
/// <see cref="ContentHash"/> is the canonical SHA-256 of an invariant rendering of the id, version,
/// sorted weekend days, sorted distinct holidays and coverage, so it does not depend on the order the
/// holidays or weekend days were supplied in.
/// </para>
/// </remarks>
public sealed class HolidayBusinessDayCalendar : IBusinessDayCalendar
{
    private const string HashPrefix = "meridian/business-day-calendar/v1";

    private readonly bool[] _weekend = new bool[7];
    private readonly HashSet<DateOnly> _holidays;

    /// <summary>Creates a calendar.</summary>
    /// <param name="calendarId">The calendar id; must be non-blank and is trimmed.</param>
    /// <param name="version">The calendar data version; must be at least 1.</param>
    /// <param name="holidays">Holiday dates; duplicates are ignored. Null means no holidays.</param>
    /// <param name="weekendDays">Weekend days; null means Saturday and Sunday.</param>
    /// <param name="coverage">The range the holiday data is complete for; default is unbounded.</param>
    /// <exception cref="ArgumentException">The id is blank, a weekend day is undefined, or every day of the week is a weekend day.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is less than 1.</exception>
    public HolidayBusinessDayCalendar(
        string calendarId,
        int version,
        IEnumerable<DateOnly>? holidays = null,
        IEnumerable<DayOfWeek>? weekendDays = null,
        CalendarCoverage coverage = default)
    {
        CalendarId = TextPrimitives.RequireText(calendarId);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        Version = version;

        var weekend = weekendDays?.Distinct().ToArray() ?? [DayOfWeek.Saturday, DayOfWeek.Sunday];
        foreach (var day in weekend)
        {
            if (!Enum.IsDefined(day))
            {
                throw new ArgumentException($"'{(int)day}' is not a valid day of the week.", nameof(weekendDays));
            }
        }

        if (weekend.Length >= 7)
        {
            throw new ArgumentException("A business calendar requires at least one working day of the week.", nameof(weekendDays));
        }

        foreach (var day in weekend)
        {
            _weekend[(int)day] = true;
        }

        WeekendDays = weekend.OrderBy(static day => (int)day).ToArray();
        Holidays = (holidays ?? []).Distinct().Order().ToArray();
        _holidays = [.. Holidays];
        Coverage = coverage;
        ContentHash = Sha256Digest.ComputeUtf8(string.Join(
            "|",
            HashPrefix,
            CalendarId,
            Version.ToString(CultureInfo.InvariantCulture),
            string.Join(",", WeekendDays.Select(static day => ((int)day).ToString(CultureInfo.InvariantCulture))),
            string.Join(",", Holidays.Select(FormatDate)),
            coverage.From is { } from ? FormatDate(from) : string.Empty,
            coverage.To is { } to ? FormatDate(to) : string.Empty));
    }

    /// <inheritdoc />
    public string CalendarId { get; }

    /// <summary>The calendar data version, at least 1. A changed holiday set ships under a new version.</summary>
    public int Version { get; }

    /// <summary>The weekend days, sorted by their numeric <see cref="DayOfWeek"/> value.</summary>
    public IReadOnlyList<DayOfWeek> WeekendDays { get; }

    /// <summary>The distinct holiday dates, ascending.</summary>
    public IReadOnlyList<DateOnly> Holidays { get; }

    /// <inheritdoc />
    public CalendarCoverage Coverage { get; }

    /// <inheritdoc />
    public string ContentHash { get; }

    /// <inheritdoc />
    public bool IsBusinessDay(DateOnly date) => !_weekend[(int)date.DayOfWeek] && !_holidays.Contains(date);

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
