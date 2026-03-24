namespace Meridian.Execution.Sdk;

/// <summary>Execution mode determines whether orders are routed to live brokers or simulated.</summary>
public enum ExecutionMode
{
    /// <summary>Orders are simulated locally (no broker connection).</summary>
    Paper,

    /// <summary>Orders are routed to a live broker for real execution.</summary>
    Live,

    /// <summary>Orders are simulated using historical replay data.</summary>
    Simulation
}

/// <summary>Order side.</summary>
public enum OrderSide
{
    Buy,
    Sell
}

/// <summary>Order type.</summary>
public enum OrderType
{
    Market,
    Limit,
    StopMarket,
    StopLimit
}

/// <summary>Order time-in-force.</summary>
public enum TimeInForce
{
    Day,
    GoodTilCancelled,
    ImmediateOrCancel,
    FillOrKill
}

/// <summary>Current state of an order in the OMS.</summary>
public enum OrderStatus
{
    PendingNew,
    Accepted,
    PartiallyFilled,
    Filled,
    PendingCancel,
    Cancelled,
    Rejected,
    Expired
}

/// <summary>Execution report type.</summary>
public enum ExecutionReportType
{
    New,
    Fill,
    PartialFill,
    Cancelled,
    Rejected,
    Modified,
    Expired
}

/// <summary>Request to place a new order.</summary>
public sealed record OrderRequest
{
    public required string Symbol { get; init; }
    public required OrderSide Side { get; init; }
    public required OrderType Type { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? LimitPrice { get; init; }
    public decimal? StopPrice { get; init; }
    public TimeInForce TimeInForce { get; init; } = TimeInForce.Day;
    public string? ClientOrderId { get; init; }
    public string? StrategyId { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Request to modify an existing order.</summary>
public sealed record OrderModification
{
    public decimal? NewQuantity { get; init; }
    public decimal? NewLimitPrice { get; init; }
    public decimal? NewStopPrice { get; init; }
}

/// <summary>Report from the execution gateway about an order event.</summary>
public sealed record ExecutionReport
{
    public required string OrderId { get; init; }
    public required ExecutionReportType ReportType { get; init; }
    public required string Symbol { get; init; }
    public required OrderSide Side { get; init; }
    public required OrderStatus OrderStatus { get; init; }
    public decimal OrderQuantity { get; init; }
    public decimal FilledQuantity { get; init; }
    public decimal? FillPrice { get; init; }
    public decimal? Commission { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? RejectReason { get; init; }
    public string? GatewayOrderId { get; init; }
    /// <summary>The client-provided order ID from the original <see cref="OrderRequest"/>.</summary>
    public string? ClientOrderId { get; init; }
}

/// <summary>Result of an order management operation.</summary>
public sealed record OrderResult
{
    public required bool Success { get; init; }
    public required string OrderId { get; init; }
    public string? ErrorMessage { get; init; }
    public OrderState? OrderState { get; init; }
}

/// <summary>Current state of an order tracked by the OMS.</summary>
public sealed record OrderState
{
    public required string OrderId { get; init; }
    public required string Symbol { get; init; }
    public required OrderSide Side { get; init; }
    public required OrderType Type { get; init; }
    public required decimal Quantity { get; init; }
    public decimal FilledQuantity { get; init; }
    public decimal? LimitPrice { get; init; }
    public decimal? StopPrice { get; init; }
    public required OrderStatus Status { get; init; }
    public decimal? AverageFillPrice { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastUpdatedAt { get; init; }
    public string? StrategyId { get; init; }
}

/// <summary>Current position state for a symbol.</summary>
public sealed record PositionState
{
    public required string Symbol { get; init; }
    public decimal Quantity { get; init; }
    public decimal AverageCostBasis { get; init; }
    public decimal MarketPrice { get; init; }
    public decimal UnrealizedPnl => (MarketPrice - AverageCostBasis) * Quantity;
    public decimal RealizedPnl { get; init; }
    public decimal MarketValue => MarketPrice * Math.Abs(Quantity);
    public DateTimeOffset LastUpdated { get; init; }
}
