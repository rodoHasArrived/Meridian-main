using System.Globalization;

namespace Meridian.Ui.Services;

/// <summary>
/// Shared formatting utilities for UI services and views.
/// </summary>
public static class FormatHelpers
{
    /// <summary>
    /// Standard ISO date format string (yyyy-MM-dd).
    /// </summary>
    public const string IsoDateFormat = "yyyy-MM-dd";

    private static readonly string[] ByteSizes = { "B", "KB", "MB", "GB", "TB" };

    /// <summary>
    /// Formats a byte count into a human-readable string (e.g., "1.5 GB").
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < ByteSizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:F1} {ByteSizes[order]}";
    }

    /// <summary>
    /// Formats a last-update timestamp into a human-readable stale indicator.
    /// Returns the display text and whether the data should be considered stale.
    /// </summary>
    /// <param name="secondsSinceUpdate">Seconds since the last successful update, or null if never updated.</param>
    /// <param name="staleThresholdSeconds">Number of seconds after which data is considered stale (default 10).</param>
    public static StaleIndicatorResult FormatStaleIndicator(double? secondsSinceUpdate, double staleThresholdSeconds = 10)
    {
        if (!secondsSinceUpdate.HasValue)
        {
            return new StaleIndicatorResult("Never updated", true);
        }

        var seconds = secondsSinceUpdate.Value;

        if (seconds < 5)
        {
            return new StaleIndicatorResult("Just now", false);
        }

        if (seconds < 60)
        {
            return new StaleIndicatorResult($"{seconds:F0}s ago", seconds > staleThresholdSeconds);
        }

        if (seconds < 3600)
        {
            var minutes = seconds / 60;
            return new StaleIndicatorResult($"{minutes:F0}m ago", true);
        }

        var hours = seconds / 3600;
        return new StaleIndicatorResult($"{hours:F1}h ago", true);
    }

    /// <summary>
    /// Formats a number using compact notation (e.g., 1.2K, 3.5M).
    /// </summary>
    public static string FormatCompactNumber(long number)
    {
        return number switch
        {
            >= 1_000_000_000 => $"{number / 1_000_000_000.0:F1}B",
            >= 1_000_000 => $"{number / 1_000_000.0:F1}M",
            >= 1_000 => $"{number / 1_000.0:F1}K",
            _ => number.ToString("N0")
        };
    }

    /// <summary>
    /// Format of a UTC timestamp label, e.g. "2026-07-05 12:00 UTC". Workstation screens show
    /// times in UTC, not on the operator's local clock.
    /// </summary>
    public const string UtcTimestampFormat = "yyyy-MM-dd HH:mm 'UTC'";

    /// <summary>
    /// Formats a timestamp in UTC, e.g. "2026-07-05 12:00 UTC", whatever offset it carries.
    /// </summary>
    public static string FormatUtcTimestamp(DateTimeOffset value)
        => value.UtcDateTime.ToString(UtcTimestampFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a timestamp in UTC, e.g. "2026-07-05 12:00 UTC". See <see cref="AsUtc"/> for how
    /// the value's <see cref="DateTime.Kind"/> is read.
    /// </summary>
    public static string FormatUtcTimestamp(DateTime value)
        => AsUtc(value).ToString(UtcTimestampFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats an optional timestamp in UTC, or returns <paramref name="fallback"/> when it is absent.
    /// </summary>
    public static string FormatUtcTimestamp(DateTimeOffset? value, string fallback)
        => value is { } timestamp ? FormatUtcTimestamp(timestamp) : fallback;

    /// <summary>
    /// Formats an optional timestamp in UTC, or returns <paramref name="fallback"/> when it is absent.
    /// </summary>
    public static string FormatUtcTimestamp(DateTime? value, string fallback)
        => value is { } timestamp ? FormatUtcTimestamp(timestamp) : fallback;

    /// <summary>
    /// Reads back a label written by <see cref="FormatUtcTimestamp(DateTimeOffset)"/>, e.g.
    /// "2026-07-05 12:00 UTC", to minute precision. Returns false for any other text.
    /// </summary>
    public static bool TryParseUtcTimestamp(string? text, out DateTimeOffset value)
        => DateTimeOffset.TryParseExact(
            text,
            UtcTimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out value);

    /// <summary>Date and time part of a display timestamp, e.g. "2026-07-05 13:00".</summary>
    public const string DisplayTimestampFormat = "yyyy-MM-dd HH:mm";

    /// <summary>
    /// Formats an instant in the operator's display time zone (<see cref="DisplayTimeZone"/>) with
    /// <paramref name="pattern"/> for the date and time, followed by the zone label: "UTC" when the
    /// display zone is UTC (so "2026-07-05 12:00 UTC", exactly <see cref="FormatUtcTimestamp(DateTimeOffset)"/>),
    /// otherwise the zone's offset at that instant, e.g. "2026-07-05 13:00 UTC+01:00". The offset is
    /// shown rather than a zone abbreviation so every label is unambiguous and can be read back.
    /// Display only: stored, audit and ledger values keep their own UTC form.
    /// </summary>
    public static string FormatDisplayTimestamp(DateTimeOffset value, string pattern = DisplayTimestampFormat)
    {
        var zone = DisplayTimeZone.Current;
        if (DisplayTimeZone.IsUtc(zone))
        {
            return $"{value.UtcDateTime.ToString(pattern, CultureInfo.InvariantCulture)} UTC";
        }

        var local = TimeZoneInfo.ConvertTime(value, zone);
        return $"{local.ToString(pattern, CultureInfo.InvariantCulture)} {FormatOffsetLabel(local.Offset)}";
    }

    /// <summary>
    /// Formats an instant in the operator's display time zone. See <see cref="AsUtc"/> for how the
    /// value's <see cref="DateTime.Kind"/> is read.
    /// </summary>
    public static string FormatDisplayTimestamp(DateTime value, string pattern = DisplayTimestampFormat)
        => FormatDisplayTimestamp(new DateTimeOffset(AsUtc(value)), pattern);

    /// <summary>Formats an optional instant in the display time zone, or returns <paramref name="fallback"/>.</summary>
    public static string FormatDisplayTimestamp(DateTimeOffset? value, string fallback)
        => value is { } timestamp ? FormatDisplayTimestamp(timestamp) : fallback;

    /// <summary>Formats an optional instant in the display time zone, or returns <paramref name="fallback"/>.</summary>
    public static string FormatDisplayTimestamp(DateTime? value, string fallback)
        => value is { } timestamp ? FormatDisplayTimestamp(timestamp) : fallback;

    /// <summary>
    /// The label for a span of two instants on one line, e.g. "13:00 - 14:30 UTC+01:00": both times in
    /// the display time zone with <paramref name="pattern"/>, and the zone label once (from the start).
    /// </summary>
    public static string FormatDisplayTimeRange(DateTimeOffset start, DateTimeOffset end, string pattern = "HH:mm")
    {
        var startLabel = FormatDisplayTimestamp(start, pattern);
        var separator = startLabel.LastIndexOf(' ');
        var endLabel = FormatDisplayTimestamp(end, pattern);
        var endTime = endLabel[..endLabel.LastIndexOf(' ')];
        return $"{startLabel[..separator]} - {endTime}{startLabel[separator..]}";
    }

    /// <summary>
    /// Reads back a label written by <see cref="FormatDisplayTimestamp(DateTimeOffset, string)"/> with the
    /// default pattern, in either zone form ("2026-07-05 12:00 UTC" or "2026-07-05 13:00 UTC+01:00"),
    /// to minute precision. Returns false for any other text.
    /// </summary>
    public static bool TryParseDisplayTimestamp(string? text, out DateTimeOffset value)
    {
        if (TryParseUtcTimestamp(text, out value))
        {
            return true;
        }

        if (text is { Length: > 0 }
            && DateTimeOffset.TryParseExact(
                text,
                $"{DisplayTimestampFormat} 'UTC'zzz",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            value = parsed.ToUniversalTime();
            return true;
        }

        value = default;
        return false;
    }

    private static string FormatOffsetLabel(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var magnitude = offset.Duration();
        return $"UTC{sign}{magnitude.Hours:00}:{magnitude.Minutes:00}";
    }

    /// <summary>
    /// The UTC instant of a <see cref="DateTime"/>. A local value is converted. A value with no
    /// kind is read as UTC, because the API's timestamps are UTC and a JSON value without an
    /// offset carries no local meaning.
    /// </summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

/// <summary>
/// Result of stale data indicator formatting.
/// Used by pages to display consistent "last update" text with stale/fresh coloring.
/// </summary>
public sealed class StaleIndicatorResult
{
    public string DisplayText { get; }
    public bool IsStale { get; }

    public StaleIndicatorResult(string displayText, bool isStale)
    {
        DisplayText = displayText;
        IsStale = isStale;
    }
}
