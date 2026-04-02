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
    string? AccountId = null,
    /// <summary>
    /// Optional lot identifier for <see cref="LotSelectionMethod.SpecificId"/> matching.
    /// When set on a sell/cover fill the engine will close the nominated lot first.
    /// Ignored when the account uses a different <see cref="LotSelectionMethod"/>.
    /// </summary>
    Guid? TargetLotId = null);
