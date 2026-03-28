namespace Meridian.QuantScript.API;

/// <summary>
/// Represents a trade fill produced by a backtest or execution run within a script.
/// </summary>
public sealed record ScriptTrade(
    string Symbol,
    string Side,
    decimal Quantity,
    decimal FillPrice,
    DateTime FilledAt,
    decimal Commission = 0m);
