using System.Globalization;

namespace Meridian.Infrastructure.Utilities;

/// <summary>
/// Centralized date and timestamp parsing utilities for provider adapters, eliminating
/// the per-adapter parsing helpers that were previously reimplemented across NYSE, Polygon,
/// Finnhub, Interactive Brokers, Robinhood, Edgar, Plaid, and the inline parsing in
/// Alpha Vantage, Twelve Data, Nasdaq Data Link, Tiingo, FRED, and Stooq.
/// </summary>
/// <remarks>
/// Parsing is always culture-invariant. Provider payloads are machine-generated (typically
/// ISO-8601), so interpreting them under the host's current culture is a latent locale bug;
/// <see cref="CultureInfo.InvariantCulture"/> keeps behavior deterministic regardless of the
/// machine the code runs on.
/// </remarks>
public static class ProviderDateParsing
{
    /// <summary>
    /// Attempts to parse a provider-supplied date using flexible, culture-invariant parsing.
    /// Handles common ISO-8601 date and date-time strings; the time component (if any) is dropped.
    /// </summary>
    public static bool TryParseProviderDate(string? value, out DateOnly result)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
        {
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Attempts to parse a provider-supplied date using a single exact format
    /// (for example <c>"yyyy-MM-dd"</c> or <c>"yyyyMMdd"</c>), culture-invariant.
    /// </summary>
    public static bool TryParseProviderDate(string? value, string format, out DateOnly result)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
        {
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Attempts to parse a provider-supplied date against an ordered set of exact formats,
    /// culture-invariant. The first format that matches wins.
    /// </summary>
    public static bool TryParseProviderDate(string? value, string[] formats, out DateOnly result)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
        {
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Parses a provider-supplied date with flexible, culture-invariant parsing,
    /// returning <see langword="null"/> when the value is missing or unparseable.
    /// </summary>
    public static DateOnly? ParseProviderDateOrNull(string? value)
        => TryParseProviderDate(value, out var result) ? result : null;

    /// <summary>
    /// Parses a provider-supplied date with an exact format, culture-invariant,
    /// returning <see langword="null"/> when the value is missing or unparseable.
    /// </summary>
    public static DateOnly? ParseProviderDateOrNull(string? value, string format)
        => TryParseProviderDate(value, format, out var result) ? result : null;

    /// <summary>
    /// Attempts to parse a provider-supplied timestamp using flexible, culture-invariant parsing.
    /// Values without an explicit offset are treated as UTC.
    /// </summary>
    public static bool TryParseProviderTimestamp(string? value, out DateTimeOffset result)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
                out result))
        {
            return true;
        }

        result = default;
        return false;
    }
}
