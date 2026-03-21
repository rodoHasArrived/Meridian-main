namespace Meridian.Backtesting.Sdk;

/// <summary>Order type classification.</summary>
public enum OrderType
{
    Market,
    Limit,
    StopMarket,
    StopLimit
}

/// <summary>Controls how long an order should remain eligible for execution.</summary>
public enum TimeInForce
{
    Day,
    GoodTilCancelled,
    ImmediateOrCancel,
    FillOrKill
}

/// <summary>
/// Provider-independent execution model selection used by the historical simulator.
/// <see cref="Auto"/> lets the engine pick the most detailed model available for the
/// current market event, while the remaining values force a specific fill approach.
/// </summary>
public enum ExecutionModel
{
    Auto,
    BarMidpoint,
    OrderBook
}

/// <summary>Lifecycle state of a simulated order.</summary>
public enum OrderStatus
{
    Pending,
    PartiallyFilled,
    Filled,
    Cancelled,
    Rejected,
    Expired
}

/// <summary>
/// Provider-independent order request accepted by the backtest context.
/// The simulator interprets the common fields directly and preserves any
/// provider-specific extensions in <see cref="ProviderParameters"/>.
/// </summary>
public sealed record OrderRequest(
    string Symbol,
    long Quantity,
    OrderType Type,
    decimal? LimitPrice = null,
    decimal? StopPrice = null,
    TimeInForce TimeInForce = TimeInForce.Day,
    ExecutionModel ExecutionModel = ExecutionModel.Auto,
    bool AllowPartialFills = true,
    IReadOnlyDictionary<string, string>? ProviderParameters = null,
    string? AccountId = null);

/// <summary>Immutable order record submitted to the backtest context.</summary>
public sealed record Order(
    Guid OrderId,
    string Symbol,
    OrderType Type,
    long Quantity,          // positive = buy; negative = sell / short
    decimal? LimitPrice,
    decimal? StopPrice,
    DateTimeOffset SubmittedAt,
    TimeInForce TimeInForce = TimeInForce.Day,
    ExecutionModel ExecutionModel = ExecutionModel.Auto,
    bool AllowPartialFills = true,
    IReadOnlyDictionary<string, string>? ProviderParameters = null,
    string? AccountId = null,
    OrderStatus Status = OrderStatus.Pending,
    long FilledQuantity = 0,
    bool IsTriggered = false)
{
    /// <summary>Absolute quantity that still needs to be executed.</summary>
    public long RemainingQuantity => Math.Max(0L, Math.Abs(Quantity) - Math.Abs(FilledQuantity));

    /// <summary>True when the order has no remaining quantity to execute.</summary>
    public bool IsComplete => RemainingQuantity == 0;

    /// <summary>Signed quantity still open, retaining the original side of the order.</summary>
    public long RemainingSignedQuantity => RemainingQuantity == 0 ? 0 : Math.Sign(Quantity) * RemainingQuantity;
}
