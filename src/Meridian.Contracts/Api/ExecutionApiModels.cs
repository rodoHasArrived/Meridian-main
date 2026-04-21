namespace Meridian.Contracts.Api;

/// <summary>
/// Snapshot used by broker-aware execution surfaces such as the desktop position blotter.
/// </summary>
public sealed record ExecutionBlotterSnapshotResponse(
    IReadOnlyList<ExecutionPositionDetailResponse> Positions,
    bool IsBrokerBacked,
    bool IsLive,
    string Source,
    string StatusMessage,
    DateTimeOffset AsOf);

/// <summary>
/// Rich position details for broker-backed and paper-backed execution views.
/// </summary>
public sealed record ExecutionPositionDetailResponse(
    string PositionKey,
    string Symbol,
    string UnderlyingSymbol,
    string ProductDescription,
    string? TradeId,
    decimal Quantity,
    decimal AverageCostBasis,
    decimal MarketPrice,
    decimal MarketValue,
    decimal UnrealisedPnl,
    decimal RealisedPnl,
    string AssetClass,
    string Side,
    DateOnly? Expiration = null,
    decimal? Strike = null,
    string? Right = null,
    bool SupportsClose = true,
    bool SupportsUpsize = true,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Request for a blotter-driven execution action against a specific position.
/// </summary>
public sealed record ExecutionPositionActionRequest(
    string PositionKey,
    decimal? Quantity = null);
