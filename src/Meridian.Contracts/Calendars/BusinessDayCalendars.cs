using System.Globalization;

namespace Meridian.Contracts.Calendars;

/// <summary>
/// The built-in weekends-only calendar, the joint-calendar factory, and the roll, add and count
/// arithmetic shared by every <see cref="IBusinessDayCalendar"/>.
/// </summary>
/// <remarks>
/// <para>
/// The roll, add and count methods mirror the reconciliation floor's accounting calendar: a roll
/// walks at most <see cref="MaxRollDays"/> days, adding business days counts from the day after the
/// start date, and the business-day count is signed over <c>(from, to]</c>. A walk that exceeds its
/// bound means the calendar is degenerate (for example every day marked as a holiday) and throws
/// <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// Modified conventions compare the calendar month of the rolled date with the original date's:
/// Modified Following falls back to Preceding when Following leaves the month, and Modified Preceding
/// falls forward to Following when Preceding leaves it.
/// </para>
/// </remarks>
public static class BusinessDayCalendars
{
    /// <summary>The id of the built-in weekends-only calendar.</summary>
    public const string WeekendsOnlyId = "WEEKENDS";

    /// <summary>
    /// The longest walk, in days, a roll may take before the calendar is treated as degenerate: a
    /// full leap decade, matching the reconciliation floor's accounting calendar.
    /// </summary>
    public const int MaxRollDays = 3660;

    /// <summary>
    /// The built-in calendar with Saturday and Sunday as weekend days, no holidays, version 1 and
    /// unbounded coverage. It is never substituted for a missing calendar.
    /// </summary>
    public static IBusinessDayCalendar WeekendsOnly { get; } = new HolidayBusinessDayCalendar(WeekendsOnlyId, 1);

    /// <summary>
    /// The joint of <paramref name="calendars"/>: a single calendar is returned as itself, and two or
    /// more become a <see cref="JointBusinessDayCalendar"/>.
    /// </summary>
    /// <exception cref="ArgumentException">No calendar is supplied, or two share an id.</exception>
    public static IBusinessDayCalendar Joint(IEnumerable<IBusinessDayCalendar> calendars)
    {
        ArgumentNullException.ThrowIfNull(calendars);
        var list = calendars.ToArray();
        if (list.Length == 0)
        {
            throw new ArgumentException("At least one calendar is required.", nameof(calendars));
        }

        if (list.Length == 1)
        {
            ArgumentNullException.ThrowIfNull(list[0], nameof(calendars));
            return list[0];
        }

        return new JointBusinessDayCalendar(list);
    }

    /// <summary>
    /// The id <see cref="Joint"/> would give the calendars named <paramref name="calendarIds"/>, without
    /// resolving them: blank ids dropped, ids trimmed and de-duplicated ignoring case (the first
    /// spelling kept), then sorted ordinally and joined with <c>+</c>. A single id is returned as
    /// itself and no id as <see langword="null"/>. Terms that name payment calendars record their
    /// joint calendar under this id.
    /// </summary>
    public static string? JointCalendarId(IEnumerable<string?> calendarIds)
    {
        ArgumentNullException.ThrowIfNull(calendarIds);
        var ids = calendarIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        return ids.Length == 0 ? null : string.Join("+", ids);
    }

    /// <summary>Returns <paramref name="date"/> when it is a business day, otherwise the next one.</summary>
    /// <exception cref="InvalidOperationException">No business day lies within <see cref="MaxRollDays"/> days.</exception>
    public static DateOnly NextOrSameBusinessDay(this IBusinessDayCalendar calendar, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return TryRoll(calendar, date, step: 1, out var result, out var error) ? result : throw new InvalidOperationException(error);
    }

    /// <summary>Returns <paramref name="date"/> when it is a business day, otherwise the previous one.</summary>
    /// <exception cref="InvalidOperationException">No business day lies within <see cref="MaxRollDays"/> days.</exception>
    public static DateOnly PreviousOrSameBusinessDay(this IBusinessDayCalendar calendar, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return TryRoll(calendar, date, step: -1, out var result, out var error) ? result : throw new InvalidOperationException(error);
    }

    /// <summary>
    /// Moves <paramref name="businessDays"/> business days from <paramref name="date"/>, counting from
    /// the day after it (or before it, for a negative count), so a non-zero result is always a
    /// business day. Zero returns <paramref name="date"/> unchanged.
    /// </summary>
    /// <exception cref="InvalidOperationException">The walk exceeds <see cref="MaxRollDays"/> plus seven days per business day, or leaves the <see cref="DateOnly"/> range.</exception>
    public static DateOnly AddBusinessDays(this IBusinessDayCalendar calendar, DateOnly date, int businessDays)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return TryAddBusinessDays(calendar, date, businessDays, out var result, out var error)
            ? result
            : throw new InvalidOperationException(error);
    }

    /// <summary>
    /// Signed business-day distance: the number of business days in <c>(from, to]</c> when
    /// <paramref name="to"/> is later, negated when earlier, and zero for the same date.
    /// </summary>
    public static int CountBusinessDaysBetween(this IBusinessDayCalendar calendar, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (from == to)
        {
            return 0;
        }

        var (start, end, sign) = from < to ? (from, to, 1) : (to, from, -1);
        var count = 0;
        for (var day = start; day < end;)
        {
            day = day.AddDays(1);
            if (calendar.IsBusinessDay(day))
            {
                count++;
            }
        }

        return count * sign;
    }

    /// <summary>Applies <paramref name="convention"/> to <paramref name="date"/> under this calendar.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="convention"/> is not a defined convention.</exception>
    /// <exception cref="InvalidOperationException">The calendar is degenerate around <paramref name="date"/>.</exception>
    public static DateOnly Adjust(this IBusinessDayCalendar calendar, DateOnly date, BusinessDayConvention convention)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (!Enum.IsDefined(convention))
        {
            throw new ArgumentOutOfRangeException(nameof(convention), convention, "Unknown business-day convention.");
        }

        return TryAdjust(calendar, date, convention, out var result, out var error) ? result : throw new InvalidOperationException(error);
    }

    /// <summary>
    /// Non-throwing <see cref="Adjust"/> for the schedule kernel: returns false with a reason when the
    /// calendar is degenerate or the walk would leave the <see cref="DateOnly"/> range.
    /// </summary>
    internal static bool TryAdjust(IBusinessDayCalendar calendar, DateOnly date, BusinessDayConvention convention, out DateOnly result, out string? error)
    {
        switch (convention)
        {
            case BusinessDayConvention.Unadjusted:
                result = date;
                error = null;
                return true;
            case BusinessDayConvention.Following:
                return TryRoll(calendar, date, 1, out result, out error);
            case BusinessDayConvention.Preceding:
                return TryRoll(calendar, date, -1, out result, out error);
            case BusinessDayConvention.ModifiedFollowing:
                if (!TryRoll(calendar, date, 1, out result, out error))
                {
                    return false;
                }

                return SameMonth(result, date) || TryRoll(calendar, date, -1, out result, out error);
            case BusinessDayConvention.ModifiedPreceding:
                if (!TryRoll(calendar, date, -1, out result, out error))
                {
                    return false;
                }

                return SameMonth(result, date) || TryRoll(calendar, date, 1, out result, out error);
            default:
                result = date;
                error = $"'{convention}' is not a defined business-day convention.";
                return false;
        }
    }

    /// <summary>Non-throwing <see cref="AddBusinessDays"/> for the schedule kernel.</summary>
    internal static bool TryAddBusinessDays(IBusinessDayCalendar calendar, DateOnly date, int businessDays, out DateOnly result, out string? error)
    {
        result = date;
        error = null;
        if (businessDays == 0)
        {
            return true;
        }

        var step = businessDays > 0 ? 1 : -1;
        var remaining = Math.Abs((long)businessDays);
        var limit = MaxRollDays + (7L * remaining);
        var current = date;
        var walked = 0L;
        while (remaining > 0)
        {
            if (!TryStep(current, step, out current))
            {
                error = $"Adding {businessDays} business days to {Format(date)} leaves the supported date range.";
                return false;
            }

            if (calendar.IsBusinessDay(current))
            {
                remaining--;
            }

            if (++walked > limit)
            {
                error = $"Could not advance {businessDays} business days from {Format(date)}; the calendar's holiday set is degenerate.";
                return false;
            }
        }

        result = current;
        return true;
    }

    private static bool TryRoll(IBusinessDayCalendar calendar, DateOnly date, int step, out DateOnly result, out string? error)
    {
        var current = date;
        for (var i = 0; i < MaxRollDays; i++)
        {
            if (calendar.IsBusinessDay(current))
            {
                result = current;
                error = null;
                return true;
            }

            if (!TryStep(current, step, out current))
            {
                result = date;
                error = $"Rolling {Format(date)} to a business day leaves the supported date range.";
                return false;
            }
        }

        result = date;
        error = $"No business day found within {MaxRollDays} days of {Format(date)}; the calendar's holiday set is degenerate.";
        return false;
    }

    private static bool TryStep(DateOnly date, int step, out DateOnly next)
    {
        var dayNumber = (long)date.DayNumber + step;
        if (dayNumber < DateOnly.MinValue.DayNumber || dayNumber > DateOnly.MaxValue.DayNumber)
        {
            next = date;
            return false;
        }

        next = DateOnly.FromDayNumber((int)dayNumber);
        return true;
    }

    private static bool SameMonth(DateOnly left, DateOnly right) => left.Year == right.Year && left.Month == right.Month;

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
