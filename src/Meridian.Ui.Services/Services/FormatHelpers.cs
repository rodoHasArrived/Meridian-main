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
