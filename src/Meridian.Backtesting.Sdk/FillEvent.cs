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
    /// When <see cref="LotSelectionMethod.SpecificId"/> is in effect, designates the
    /// <see cref="OpenLot.LotId"/> that should be closed by this fill.
    /// Ignored for buy fills and when no matching lot is found (falls back to FIFO).
    /// </summary>
    Guid? TargetLotId = null);
