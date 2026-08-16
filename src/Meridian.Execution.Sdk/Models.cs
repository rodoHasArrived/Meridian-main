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
    StopLimit,
    MarketOnOpen,
    MarketOnClose,
    LimitOnOpen,
    LimitOnClose,
    TrailingStop
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

/// <summary>Option leg position intent for option orders and multi-leg strategies.</summary>
public enum PositionIntent
{
    BuyToOpen,
    BuyToClose,
    SellToOpen,
    SellToClose
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
    public decimal? TrailPrice { get; init; }
    public decimal? TrailPercent { get; init; }
    public TimeInForce TimeInForce { get; init; } = TimeInForce.Day;
    public string? ClientOrderId { get; init; }
    public string? StrategyId { get; init; }
    public Guid? FundAccountId { get; init; }
    public PositionIntent? PositionIntent { get; init; }
    public OptionContractIdentity? OptionContract { get; init; }
    public IReadOnlyList<OrderLeg>? Legs { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>One leg in a multi-leg options order.</summary>
public sealed record OrderLeg
{
    public required string Symbol { get; init; }
    public required OrderSide Side { get; init; }
    public required decimal RatioQuantity { get; init; }
    public PositionIntent? PositionIntent { get; init; }
    public OptionContractIdentity? OptionContract { get; init; }
}

/// <summary>Canonical identity for options contracts across broker adapters and execution events.</summary>
public sealed record OptionContractIdentity
{
    public string? UnderlyingSymbol { get; init; }
    public DateOnly? ExpirationDate { get; init; }
    public decimal? StrikePrice { get; init; }
    /// <summary>Normalized right code (C/P) where available.</summary>
    public string? Right { get; init; }
    public string? Multiplier { get; init; }
    public string? ContractMonth { get; init; }
    public string? TradingClass { get; init; }
    public string? LocalSymbol { get; init; }
}

/// <summary>Request to modify an existing order.</summary>
public sealed record OrderModification
{
    public decimal? NewQuantity { get; init; }
    public decimal? NewLimitPrice { get; init; }
    public decimal? NewStopPrice { get; init; }
    public decimal? NewTrail { get; init; }
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

    /// <summary>Regulatory/exchange fees for this fill, when the gateway models them.</summary>
    public decimal? Fees { get; init; }

    /// <summary>Explicit slippage cash cost for this fill, when the gateway models it.</summary>
    public decimal? SlippageCost { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string? RejectReason { get; init; }
    /// <summary>Broker-assigned order ID (may differ from the client-provided <see cref="OrderId"/>).</summary>
    public string? GatewayOrderId { get; init; }
    /// <summary>The client-provided order ID from the original <see cref="OrderRequest"/>.</summary>
    public string? ClientOrderId { get; init; }
    public OptionContractIdentity? OptionContract { get; init; }
    public IReadOnlyList<OrderLeg>? Legs { get; init; }
    public ExecutionDiagnostics? Diagnostics { get; init; }
}

/// <summary>Normalized operational diagnostics derived from broker status/reject payloads.</summary>
public sealed record ExecutionDiagnostics
{
    public string? BrokerStatus { get; init; }
    public string? BrokerRejectReason { get; init; }
    public string? RejectCode { get; init; }
    public string? Category { get; init; }
    public string? Severity { get; init; }
    public string? RecommendedAction { get; init; }
}

/// <summary>Result of an order management operation.</summary>
public sealed record OrderResult
{
    public required bool Success { get; init; }
    public required string OrderId { get; init; }
    public string? ErrorMessage { get; init; }
    public OrderState? OrderState { get; init; }

    /// <summary>
    /// Non-blocking risk flags raised while the order was approved (e.g. concentration
    /// observe-band or warning-severity rule breaches), surfaced so callers and operators
    /// see them even though the order routed.
    /// </summary>
    public IReadOnlyList<string>? RiskWarnings { get; init; }

    /// <summary>
    /// True when the order did not route because a risk escalation parked it for governed
    /// approval rather than rejecting it outright; <see cref="EscalationId"/> identifies
    /// the approval-queue entry an operator can approve or deny.
    /// </summary>
    public bool RequiresApproval { get; init; }

    /// <summary>Approval-queue entry id when <see cref="RequiresApproval"/> is true.</summary>
    public string? EscalationId { get; init; }

    /// <summary>
    /// Pre-trade risk findings for this submission, when the risk gate produced any. Populated on
    /// both the admitted and the rejected path so an order ticket can render the decision without
    /// a second query — the asynchronous decision history offers no deterministic read-back for
    /// the submission that just returned.
    /// </summary>
    /// <remarks>
    /// Structured counterpart to <see cref="RiskWarnings"/>: the same breaches carrying the rule
    /// name, severity, and stable code rather than a rendered sentence. Both are populated because
    /// <see cref="RiskWarnings"/> is already on the wire and rendered by shipped clients.
    /// </remarks>
    public RiskDecisionSummary? RiskDecision { get; init; }
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

    /// <summary>Accounting scope the order was submitted under, when fund-scoped.</summary>
    public Guid? FundAccountId { get; init; }

    /// <summary>
    /// Broker-native routed notional when the order is sized in dollars rather than
    /// quantity (see <see cref="BrokerNotionalMetadata"/>). Exposure reserves for working
    /// orders must value these at the routed dollars, not quantity x price.
    /// </summary>
    public decimal? RoutedNotional { get; init; }

    /// <summary>
    /// The active gateway routes <see cref="Quantity"/> as face value and quotes the order price
    /// as a percentage of par. Retained so working-order reserves and amendments use the same
    /// economic sizing semantics as initial validation.
    /// </summary>
    public bool UsesFaceValuePercentageOfPar { get; init; }

    /// <summary>
    /// Contract multiplier for a derivative order: the notional one unit of
    /// <see cref="Quantity"/> represents. 1 for outright instruments, 100 for standard
    /// equity option contracts. A working-order exposure reserve that ignores it holds back
    /// a hundredth of what 100 contracts will actually execute.
    /// </summary>
    public decimal ContractMultiplier { get; init; } = 1m;

    /// <summary>
    /// Derivative identity of the working order, retained so its exposure reserve and any
    /// amendment are valued the same way the pre-trade gate valued it.
    /// </summary>
    public OptionContractIdentity? OptionContract { get; init; }

    /// <summary>
    /// Legs of a working combination order. A spread's reserve is the sum of its legs'
    /// absolute notionals, not its net package price — without the legs, a $1 net-debit
    /// spread reserves $100 where the gate charged $1,900.
    /// </summary>
    public IReadOnlyList<OrderLeg>? Legs { get; init; }
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
