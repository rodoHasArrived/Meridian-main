using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.Calendars;

/// <summary>
/// The joint of two or more business-day calendars: a day is a business day only when it is a
/// business day in every member, as in a schedule that must settle in several financial centres.
/// </summary>
/// <remarks>
/// Members are held sorted by ordinal id, so the joint's id (<c>A+B</c>) and content hash do not
/// depend on the order the members were supplied in. Coverage is the intersection of the members'
/// coverage.
/// </remarks>
public sealed class JointBusinessDayCalendar : IBusinessDayCalendar
{
    private const string HashPrefix = "meridian/joint-business-day-calendar/v1";

    private readonly IBusinessDayCalendar[] _members;

    /// <summary>Creates the joint of <paramref name="members"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="members"/> or one of its entries is null.</exception>
    /// <exception cref="ArgumentException">Fewer than two members are supplied, or two members share an id (ignoring case).</exception>
    public JointBusinessDayCalendar(IEnumerable<IBusinessDayCalendar> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var list = members.ToArray();
        foreach (var member in list)
        {
            ArgumentNullException.ThrowIfNull(member, nameof(members));
        }

        if (list.Length < 2)
        {
            throw new ArgumentException("A joint business-day calendar requires at least two member calendars.", nameof(members));
        }

        if (list.Select(static member => member.CalendarId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Length)
        {
            throw new ArgumentException("A joint business-day calendar requires member calendars with distinct ids.", nameof(members));
        }

        _members = list.OrderBy(static member => member.CalendarId, StringComparer.Ordinal).ToArray();
        CalendarId = string.Join("+", _members.Select(static member => member.CalendarId));
        ContentHash = Sha256Digest.ComputeUtf8(
            HashPrefix + "|" + string.Join("|", _members.Select(static member => member.CalendarId + ":" + member.ContentHash)));

        var coverage = CalendarCoverage.Unbounded;
        foreach (var member in _members)
        {
            coverage = coverage.Intersect(member.Coverage);
        }

        Coverage = coverage;
    }

    /// <summary>The member calendars, sorted by ordinal id.</summary>
    public IReadOnlyList<IBusinessDayCalendar> Members => _members;

    /// <inheritdoc />
    public string CalendarId { get; }

    /// <inheritdoc />
    public string ContentHash { get; }

    /// <inheritdoc />
    public CalendarCoverage Coverage { get; }

    /// <inheritdoc />
    public bool IsBusinessDay(DateOnly date)
    {
        foreach (var member in _members)
        {
            if (!member.IsBusinessDay(date))
            {
                return false;
            }
        }

        return true;
    }
}
