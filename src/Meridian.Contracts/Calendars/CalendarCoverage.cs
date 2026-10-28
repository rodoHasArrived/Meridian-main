namespace Meridian.Contracts.Calendars;

/// <summary>
/// The inclusive date range a business-day calendar's holiday data is known to be complete for.
/// A null bound is unbounded on that side, so <see cref="Unbounded"/> covers every date.
/// </summary>
/// <remarks>
/// A calendar still answers <see cref="IBusinessDayCalendar.IsBusinessDay"/> outside its coverage
/// (weekends remain weekends); coverage only says whether a holiday could be missing from the data.
/// The schedule kernel records every date it asks about outside coverage as a diagnostic rather than
/// refusing.
/// </remarks>
/// <param name="From">The first covered date, or null when coverage has no lower bound.</param>
/// <param name="To">The last covered date, or null when coverage has no upper bound.</param>
public readonly record struct CalendarCoverage(DateOnly? From, DateOnly? To)
{
    /// <summary>Coverage with neither bound: every date is covered.</summary>
    public static CalendarCoverage Unbounded => default;

    /// <summary>Whether <paramref name="date"/> lies inside the covered range.</summary>
    public bool Covers(DateOnly date) => (From is not { } f || date >= f) && (To is not { } t || date <= t);

    /// <summary>
    /// The range covered by both this coverage and <paramref name="other"/>: the later lower bound
    /// and the earlier upper bound, with null meaning unbounded. An empty intersection (lower bound
    /// after upper bound) covers nothing.
    /// </summary>
    public CalendarCoverage Intersect(CalendarCoverage other)
        => new(Later(From, other.From), Earlier(To, other.To));

    private static DateOnly? Later(DateOnly? left, DateOnly? right)
        => left is not { } l ? right : right is not { } r ? l : (l >= r ? l : r);

    private static DateOnly? Earlier(DateOnly? left, DateOnly? right)
        => left is not { } l ? right : right is not { } r ? l : (l <= r ? l : r);
}
