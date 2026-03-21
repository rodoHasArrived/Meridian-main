namespace Meridian.Backtesting.Sdk;

/// <summary>Represents a complete or partial execution of a simulated order.</summary>
public sealed record FillEvent(
    Guid FillId,
    Guid OrderId,
    string Symbol,
    long FilledQuantity,    // positive = bought; negative = sold/shorted
    decimal FillPrice,
    decimal Commission,
    DateTimeOffset FilledAt,
    string? AccountId = null);
