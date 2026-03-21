namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Describes a scheduled asset-level event that should be applied during a backtest to improve
/// portfolio accounting realism beyond pure trade and price data.
/// </summary>
/// <param name="EffectiveAt">Timestamp when the event becomes effective.</param>
/// <param name="Symbol">Primary source symbol affected by the event.</param>
/// <param name="EventType">Type of economic event to apply.</param>
/// <param name="CashPerShare">
/// Cash paid or charged per currently-held unit. Positive values credit holders and debit shorts.
/// Negative values model fees, assessments, or other cash outflows.
/// </param>
/// <param name="PositionFactor">
/// Share-conversion factor applied to the current position. Examples: 2.0 for a 2-for-1 split,
/// 0.5 for a 1-for-2 reverse split, 0.75 for stock-for-stock merger consideration.
/// </param>
/// <param name="TargetSymbol">
/// Optional destination symbol for mergers, acquisitions, renames, and other symbol migrations.
/// When omitted, the existing symbol is retained.
/// </param>
/// <param name="ReferencePrice">
/// Optional price used for cash-in-lieu calculations and transformed last-price updates.
/// </param>
/// <param name="Description">Optional human-readable description for reporting and ledger entries.</param>
public sealed record AssetEvent(
    DateTimeOffset EffectiveAt,
    string Symbol,
    AssetEventType EventType,
    decimal CashPerShare = 0m,
    decimal PositionFactor = 1m,
    string? TargetSymbol = null,
    decimal? ReferencePrice = null,
    string? Description = null)
{
    /// <summary>Resolved destination symbol after any rename, merger, or acquisition conversion.</summary>
    public string DestinationSymbol => string.IsNullOrWhiteSpace(TargetSymbol) ? Symbol : TargetSymbol;

    /// <summary>True when the event alters share count, symbol identity, or both.</summary>
    public bool HasPositionTransformation => PositionFactor != 1m || !DestinationSymbol.Equals(Symbol, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Portfolio-affecting asset event types supported by the backtesting engine.
/// </summary>
public enum AssetEventType : byte
{
    Dividend = 0,
    Coupon = 1,
    Payment = 2,
    CashDistribution = 3,
    ReturnOfCapital = 4,
    Fee = 5,
    Split = 6,
    ReverseSplit = 7,
    Merger = 8,
    Acquisition = 9,
    SymbolChange = 10,
    SpinOff = 11,
    Delisting = 12,
    Other = 13
}
