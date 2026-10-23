using System.Diagnostics.CodeAnalysis;
using Meridian.Contracts.Text;

namespace Meridian.Contracts.Calendars;

/// <summary>
/// Resolves business-day calendars by id. A source never substitutes the weekends-only calendar for
/// an id it does not know: an unknown id fails to resolve, so a missing holiday set is visible
/// instead of silently producing weekend-only dates.
/// </summary>
public interface IBusinessDayCalendarSource
{
    /// <summary>Looks up one calendar by id (ordinal, ignoring case).</summary>
    bool TryGet(string calendarId, [NotNullWhen(true)] out IBusinessDayCalendar? calendar);

    /// <summary>
    /// Resolves a list of calendar ids: one id yields that calendar, several yield their joint, and
    /// an empty list or any unknown id fails.
    /// </summary>
    bool TryResolve(IReadOnlyList<string> calendarIds, [NotNullWhen(true)] out IBusinessDayCalendar? calendar);
}

/// <summary>
/// An in-memory <see cref="IBusinessDayCalendarSource"/> over a fixed set of calendars, for tests
/// and for callers that already hold their calendars.
/// </summary>
public sealed class StaticBusinessDayCalendarSource : IBusinessDayCalendarSource
{
    private readonly Dictionary<string, IBusinessDayCalendar> _calendars = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a source over <paramref name="calendars"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="calendars"/> or one of its entries is null.</exception>
    /// <exception cref="ArgumentException">Two calendars share an id (ignoring case).</exception>
    public StaticBusinessDayCalendarSource(IEnumerable<IBusinessDayCalendar> calendars)
    {
        ArgumentNullException.ThrowIfNull(calendars);
        foreach (var calendar in calendars)
        {
            ArgumentNullException.ThrowIfNull(calendar, nameof(calendars));
            if (!_calendars.TryAdd(calendar.CalendarId, calendar))
            {
                throw new ArgumentException($"Duplicate business-day calendar id '{calendar.CalendarId}'.", nameof(calendars));
            }
        }
    }

    /// <inheritdoc />
    public bool TryGet(string calendarId, [NotNullWhen(true)] out IBusinessDayCalendar? calendar)
    {
        calendar = null;
        return TextPrimitives.NormalizeOptional(calendarId) is { } id && _calendars.TryGetValue(id, out calendar);
    }

    /// <inheritdoc />
    public bool TryResolve(IReadOnlyList<string> calendarIds, [NotNullWhen(true)] out IBusinessDayCalendar? calendar)
    {
        calendar = null;
        if (calendarIds is null || calendarIds.Count == 0)
        {
            return false;
        }

        var resolved = new List<IBusinessDayCalendar>(calendarIds.Count);
        foreach (var calendarId in calendarIds)
        {
            if (!TryGet(calendarId, out var member))
            {
                return false;
            }

            // The same id listed twice names one calendar, not two members of a joint.
            if (!resolved.Contains(member))
            {
                resolved.Add(member);
            }
        }

        calendar = BusinessDayCalendars.Joint(resolved);
        return true;
    }
}
