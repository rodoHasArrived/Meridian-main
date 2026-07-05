using System;
using System.Linq;

namespace Meridian.Ui.Shared.Streaming;

/// <summary>
/// Identifies a quote-stream fan-out topic: the canonical, order- and case-independent
/// set of symbols a subscriber wants, or the "all tracked symbols" sentinel. Topic
/// equality is by <see cref="Key"/>, so subscribers requesting the same symbols — in any
/// order or case — share one topic (and therefore one snapshot build per fan-out tick).
/// </summary>
public readonly struct StreamTopic : IEquatable<StreamTopic>
{
    /// <summary>Topic key for "all tracked symbols".</summary>
    public const string AllQuotesKey = "quotes:*";

    private StreamTopic(string key, string symbolFilter)
    {
        Key = key;
        SymbolFilter = symbolFilter;
    }

    /// <summary>Canonical, stable identifier for this topic.</summary>
    public string Key { get; }

    /// <summary>
    /// The comma-joined symbol filter to pass to the snapshot builder, or empty string
    /// for "all tracked symbols".
    /// </summary>
    public string SymbolFilter { get; }

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

    public bool Equals(StreamTopic other) => string.Equals(Key, other.Key, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is StreamTopic other && Equals(other);

    public override int GetHashCode() => Key is null ? 0 : StringComparer.Ordinal.GetHashCode(Key);

    public override string ToString() => Key ?? AllQuotesKey;
}
