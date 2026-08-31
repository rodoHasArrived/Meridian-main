namespace Meridian.Contracts.Domain;

/// <summary>
/// Canonical market-data source identifiers stamped on <c>MarketEvent.Source</c> and on
/// adapter ingress updates. Streaming adapters must stamp their real vendor identity at
/// origin; collectors refuse to fabricate one. Spellings match the provider keys used by
/// the canonicalization tables in <c>config/condition-codes.json</c> and
/// <c>config/venue-mics.json</c> (lookups normalize case, storage partitions do not —
/// keep these spellings stable).
/// </summary>
public static class MarketDataSources
{
    /// <summary>
    /// Honest sentinel for events whose provenance genuinely cannot be determined.
    /// Never use this to paper over a missing adapter stamp on a live ingress path.
    /// </summary>
    public const string Unknown = "UNKNOWN";

    /// <summary>Interactive Brokers.</summary>
    public const string Ib = "IB";

    /// <summary>Alpaca Markets (equity, options, crypto, and news streams).</summary>
    public const string Alpaca = "ALPACA";

    /// <summary>Polygon.io.</summary>
    public const string Polygon = "POLYGON";

    /// <summary>NYSE direct feed.</summary>
    public const string Nyse = "NYSE";

    /// <summary>Robinhood quote polling.</summary>
    public const string Robinhood = "ROBINHOOD";

    /// <summary>Repository sample-data generator and demo collector harness.</summary>
    public const string Sample = "SAMPLE";

    /// <summary>
    /// True when the value cannot serve as a source identity (null, empty, or whitespace).
    /// </summary>
    public static bool IsMissing(string? source) => string.IsNullOrWhiteSpace(source);
}
