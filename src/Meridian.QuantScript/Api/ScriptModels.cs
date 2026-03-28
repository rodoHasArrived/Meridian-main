namespace Meridian.QuantScript.Api;

/// <summary>Lightweight trade tick for script consumption.</summary>
public sealed record ScriptTrade(DateTimeOffset Timestamp, decimal Price, long Size, string Side);

/// <summary>Lightweight order book snapshot for script consumption.</summary>
public sealed record ScriptOrderBook(
    DateTimeOffset Timestamp,
    IReadOnlyList<(decimal Price, long Size)> Bids,
    IReadOnlyList<(decimal Price, long Size)> Asks);
