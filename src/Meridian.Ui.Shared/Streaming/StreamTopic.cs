using System;
using System.Linq;

namespace Meridian.Ui.Shared.Streaming;

/// <summary>
/// Identifies a stream fan-out topic — the unit of subscription and one-build-per-tick sharing.
/// The <see cref="Key"/> encodes the topic kind and its argument: <c>quotes:&lt;symbols&gt;</c> for a
/// canonical, order- and case-independent symbol set (or the "all tracked symbols" sentinel), and
/// <c>report-run:&lt;runId&gt;</c> for a single reporting run. Topic equality is by <see cref="Key"/>, so
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
    /// Build a report-run topic keyed by the exact run id (trimmed). Run ids are opaque
    /// identifiers, so — unlike symbols — they are not case- or order-canonicalized. One run id
    /// maps to exactly one topic.
    /// </summary>
    public static StreamTopic ReportRun(string runId)
    {
        var trimmed = (runId ?? string.Empty).Trim();
        return new StreamTopic($"report-run:{trimmed}", trimmed);
    }

    public bool Equals(StreamTopic other) => string.Equals(Key, other.Key, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is StreamTopic other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);

    public override string ToString() => Key;

    public static bool operator ==(StreamTopic left, StreamTopic right) => left.Equals(right);

    public static bool operator !=(StreamTopic left, StreamTopic right) => !left.Equals(right);
}
