using System;
using System.Linq;

namespace Meridian.Ui.Shared.Streaming;

/// <summary>
/// Identifies a stream fan-out topic — the unit of subscription and one-build-per-tick sharing.
/// The <see cref="Key"/> encodes the topic kind and its argument: <c>quotes:&lt;symbols&gt;</c> for a
/// canonical, order- and case-independent symbol set (or the "all tracked symbols" sentinel), and
/// a length-prefixed tenant/company/run identity for a scoped reporting run. Topic equality is by
/// <see cref="Key"/>, so
/// equivalent requests share one topic (and therefore one snapshot build per fan-out tick).
/// </summary>
public readonly struct StreamTopic : IEquatable<StreamTopic>
{
    /// <summary>Topic key for "all tracked symbols".</summary>
    public const string AllQuotesKey = "quotes:*";

    private readonly string? _key;
    private readonly string? _argument;

    private StreamTopic(string key, string argument)
    {
        _key = key;
        _argument = argument;
    }

    /// <summary>
    /// Canonical, stable identifier for this topic. <c>default(StreamTopic)</c> resolves to
    /// <see cref="AllQuotesKey"/> so the default value behaves as <see cref="AllQuotes"/>.
    /// </summary>
    public string Key => _key ?? AllQuotesKey;

    /// <summary>
    /// The argument passed to this topic's snapshot builder: the comma-joined symbol filter for
    /// quote topics (empty string = "all tracked symbols"), or the run id for report-run topics.
    /// </summary>
    public string Argument => _argument ?? string.Empty;

    /// <summary>Back-compat alias for quote consumers: the symbol filter for a quotes topic.</summary>
    public string SymbolFilter => Argument;

    /// <summary>Topic covering every tracked symbol.</summary>
    public static StreamTopic AllQuotes { get; } = new(AllQuotesKey, string.Empty);

    /// <summary>
    /// Build a quotes topic from a raw or normalized symbol filter (comma-separated).
    /// Symbols are trimmed, upper-cased, de-duplicated, and sorted so equivalent
    /// requests collapse to the same topic. An empty/whitespace filter yields
    /// <see cref="AllQuotes"/>.
    /// </summary>
    public static StreamTopic Quotes(string? symbolFilter)
    {
        if (string.IsNullOrWhiteSpace(symbolFilter))
        {
            return AllQuotes;
        }

        var symbols = symbolFilter
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static symbol => symbol.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        if (symbols.Length == 0)
        {
            return AllQuotes;
        }

        var canonical = string.Join(',', symbols);
        return new StreamTopic($"quotes:{canonical}", canonical);
    }

    /// <summary>
    /// Build a legacy report-run topic keyed by the exact run id (trimmed). New tenant-bound
    /// subscriptions use the scoped overload below.
    /// </summary>
    public static StreamTopic ReportRun(string runId)
    {
        var trimmed = (runId ?? string.Empty).Trim();
        return new StreamTopic($"report-run:{trimmed}", trimmed);
    }

    public static StreamTopic ReportRun(
        string tenantId,
        string companyId,
        string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var tenant = tenantId.Trim();
        var company = companyId.Trim();
        var run = runId.Trim();
        var argument =
            $"{tenant.Length}:{tenant}{company.Length}:{company}{run.Length}:{run}";
        return new StreamTopic($"report-run-scoped:{argument}", argument);
    }

    public static bool TryParseScopedReportRun(
        string argument,
        out string tenantId,
        out string companyId,
        out string runId)
    {
        tenantId = string.Empty;
        companyId = string.Empty;
        runId = string.Empty;
        var cursor = 0;
        return TryReadLengthPrefixed(argument, ref cursor, out tenantId)
               && TryReadLengthPrefixed(argument, ref cursor, out companyId)
               && TryReadLengthPrefixed(argument, ref cursor, out runId)
               && cursor == argument.Length;
    }

    private static bool TryReadLengthPrefixed(
        string value,
        ref int cursor,
        out string part)
    {
        part = string.Empty;
        if (cursor >= value.Length)
        {
            return false;
        }

        var separator = value.IndexOf(':', cursor);
        if (separator <= cursor
            || !int.TryParse(
                value.AsSpan(cursor, separator - cursor),
                out var length)
            || length < 0
            || separator + 1 + length > value.Length)
        {
            return false;
        }

        cursor = separator + 1;
        part = value.Substring(cursor, length);
        cursor += length;
        return part.Length > 0;
    }

    public bool Equals(StreamTopic other) => string.Equals(Key, other.Key, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is StreamTopic other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);

    public override string ToString() => Key;

    public static bool operator ==(StreamTopic left, StreamTopic right) => left.Equals(right);

    public static bool operator !=(StreamTopic left, StreamTopic right) => !left.Equals(right);
}
