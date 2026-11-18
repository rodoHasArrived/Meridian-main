using System;
using System.Threading;

namespace Meridian.Ui.Services;

/// <summary>
/// The time zone desktop timestamps are shown in: the operator's display time zone from their host
/// preferences (the same setting as the browser workstation's Settings > Preferences > Appearance),
/// or UTC when none is set or this machine cannot resolve it. Read by
/// <see cref="FormatHelpers.FormatDisplayTimestamp(DateTimeOffset, string)"/>. Display only: it never
/// changes stored, audit or ledger values.
/// </summary>
public static class DisplayTimeZone
{
    private static TimeZoneInfo _current = TimeZoneInfo.Utc;

    /// <summary>The zone timestamps are shown in; UTC by default.</summary>
    public static TimeZoneInfo Current => Volatile.Read(ref _current);

    /// <summary>Raised after <see cref="Current"/> changes, so open screens can reformat.</summary>
    public static event EventHandler? Changed;

    /// <summary>Sets the display zone; null restores UTC. Raises <see cref="Changed"/> when it differs.</summary>
    public static void Set(TimeZoneInfo? zone)
    {
        var next = zone ?? TimeZoneInfo.Utc;
        var previous = Interlocked.Exchange(ref _current, next);
        if (!string.Equals(previous.Id, next.Id, StringComparison.Ordinal))
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// True for UTC itself and its aliases (such as "Etc/UTC"), not for zones that merely sit at
    /// +00:00 for part of the year, such as Europe/London.
    /// </summary>
    public static bool IsUtc(TimeZoneInfo zone)
        => ReferenceEquals(zone, TimeZoneInfo.Utc)
           || zone.Id is "UTC" or "Etc/UTC" or "Etc/UCT" or "Etc/Universal" or "Etc/Zulu" or "UCT" or "Universal" or "Zulu";
}
