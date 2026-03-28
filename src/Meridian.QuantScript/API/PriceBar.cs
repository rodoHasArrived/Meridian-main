namespace Meridian.QuantScript.API;

/// <summary>
/// A single OHLCV price bar returned by a historical data provider.
/// </summary>
public sealed record PriceBar(
    DateTime Timestamp,
    double Open,
    double High,
    double Low,
    double Close,
    long Volume);
