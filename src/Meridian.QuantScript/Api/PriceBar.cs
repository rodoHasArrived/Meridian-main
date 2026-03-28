namespace Meridian.QuantScript.Api;

/// <summary>Single OHLCV bar.</summary>
public sealed record PriceBar(
    DateOnly Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);
