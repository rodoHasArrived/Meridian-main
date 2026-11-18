using System;
using System.Globalization;

namespace Meridian.Ui.Services;

/// <summary>
/// Quiet-hours arithmetic shared by the desktop notification service and its settings screen. Times
/// are wall-clock <c>HH:mm</c> values in the operator's display time zone. A window whose start is
/// later than its end runs past midnight; the end minute is not quiet; an equal start and end is an
/// empty window, so it never silences anything.
/// </summary>
public static class QuietHours
{
    public const string ClockFormat = "hh\\:mm";

    /// <summary>The default quiet window, 22:00 to 07:00.</summary>
    public static TimeSpan DefaultStart { get; } = new(22, 0, 0);

    public static TimeSpan DefaultEnd { get; } = new(7, 0, 0);

    /// <summary>True when <paramref name="nowUtc"/>, read in <paramref name="zone"/>, falls inside the window.</summary>
    public static bool IsActive(DateTimeOffset nowUtc, TimeZoneInfo zone, TimeSpan start, TimeSpan end)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (start == end)
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(nowUtc, zone).TimeOfDay;
        var minute = new TimeSpan(local.Hours, local.Minutes, 0);
        return start < end
            ? minute >= start && minute < end
            : minute >= start || minute < end;
    }

    /// <summary>Reads a 24-hour <c>HH:mm</c> time such as <c>22:00</c>.</summary>
    public static bool TryParseClock(string? text, out TimeSpan time)
    {
        time = default;
        var trimmed = text?.Trim();
        if (trimmed is not { Length: 5 }
            || !TimeSpan.TryParseExact(trimmed, ClockFormat, CultureInfo.InvariantCulture, out var parsed)
            || parsed < TimeSpan.Zero
            || parsed >= TimeSpan.FromDays(1))
        {
            return false;
        }

        time = parsed;
        return true;
    }

    /// <summary>Writes a time of day as <c>HH:mm</c>.</summary>
    public static string FormatClock(TimeSpan time)
        => new TimeSpan(time.Hours, time.Minutes, 0).ToString(ClockFormat, CultureInfo.InvariantCulture);
}
