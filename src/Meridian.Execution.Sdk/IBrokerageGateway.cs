namespace Meridian.Execution.Sdk;

/// <summary>
/// Extended execution gateway contract for full brokerage providers.
/// Adds account querying, position sync, and connection lifecycle on top of
/// <see cref="IExecutionGateway"/>. Each broker (Alpaca, IB, StockSharp, etc.)
/// implements this interface to expose its full trading capabilities.
/// </summary>
public interface IBrokerageGateway : IExecutionGateway, IAsyncDisposable
{
    /// <summary>Gets the human-readable broker display name (e.g., "Alpaca Markets").</summary>
    string BrokerDisplayName { get; }

    /// <summary>Gets the declared brokerage capabilities for this gateway.</summary>
    BrokerageCapabilities BrokerageCapabilities { get; }

    /// <summary>Gets the current account balance and buying power from the broker.</summary>
    Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default);

    /// <summary>Queries current positions held at the broker.</summary>
    Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default);

    /// <summary>Queries open orders at the broker.</summary>
    Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks whether the gateway can currently accept orders (credentials valid,
    /// connection alive, market open, etc.).
    /// </summary>
    Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default);
}

/// <summary>
/// Describes the brokerage-specific capabilities exposed by a gateway.
/// </summary>
public sealed record BrokerageCapabilities
{
    /// <summary>Supported order types.</summary>
    public required IReadOnlySet<OrderType> SupportedOrderTypes { get; init; }

    /// <summary>Supported time-in-force values.</summary>
    public required IReadOnlySet<TimeInForce> SupportedTimeInForce { get; init; }

    /// <summary>Supports order modification (amend price/qty on working orders).</summary>
    public bool SupportsOrderModification { get; init; }

    /// <summary>Supports partial fills.</summary>
    public bool SupportsPartialFills { get; init; }

    /// <summary>Supports short selling.</summary>
    public bool SupportsShortSelling { get; init; }

    /// <summary>Supports fractional shares.</summary>
    public bool SupportsFractionalShares { get; init; }

    /// <summary>Supports extended/after-hours trading.</summary>
    public bool SupportsExtendedHours { get; init; }

    /// <summary>Supported asset classes (e.g., "equity", "option", "crypto", "futures").</summary>
    public IReadOnlyList<string> SupportedAssetClasses { get; init; } = ["equity"];

    /// <summary>Supported markets/exchanges.</summary>
    public IReadOnlyList<string> SupportedMarkets { get; init; } = ["US"];

    /// <summary>Provider-specific extension hints (e.g., "maxOrdersPerSecond").</summary>
    public IReadOnlyDictionary<string, string> Extensions { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Standard brokerage capabilities for US equity brokers.</summary>
    public static BrokerageCapabilities UsEquity(
        bool modification = true,
        bool partialFills = true,
        bool shortSelling = true,
        bool fractional = false,
        bool extendedHours = false) => new()
        {
            SupportedOrderTypes = new HashSet<OrderType>
        {
            OrderType.Market,
            OrderType.Limit,
            OrderType.StopMarket,
            OrderType.StopLimit
        },
            SupportedTimeInForce = new HashSet<TimeInForce>
        {
            TimeInForce.Day,
            TimeInForce.GoodTilCancelled,
            TimeInForce.ImmediateOrCancel,
            TimeInForce.FillOrKill
        },
            SupportsOrderModification = modification,
            SupportsPartialFills = partialFills,
            SupportsShortSelling = shortSelling,
            SupportsFractionalShares = fractional,
            SupportsExtendedHours = extendedHours,
        };

    /// <summary>
    /// Brokerage capabilities for brokers that support both US equities and fixed income
    /// instruments (US Treasuries, corporate bonds, municipal bonds).
    /// Fixed income orders may use notional (dollar-amount) sizing in addition to share qty.
    /// </summary>
    public static BrokerageCapabilities UsEquityAndFixedIncome(
        bool modification = true,
        bool partialFills = true,
        bool shortSelling = true,
        bool fractional = true,
        bool extendedHours = false) => new()
        {
            SupportedOrderTypes = new HashSet<OrderType>
        {
            OrderType.Market,
            OrderType.Limit,
            OrderType.StopMarket,
            OrderType.StopLimit
        },
            SupportedTimeInForce = new HashSet<TimeInForce>
        {
            TimeInForce.Day,
            TimeInForce.GoodTilCancelled,
            TimeInForce.ImmediateOrCancel,
            TimeInForce.FillOrKill
        },
            SupportsOrderModification = modification,
            SupportsPartialFills = partialFills,
            SupportsShortSelling = shortSelling,
            SupportsFractionalShares = fractional,
            SupportsExtendedHours = extendedHours,
            SupportedAssetClasses = ["equity", "us_treasury", "bond"],
            Extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["supportsNotionalOrders"] = "true",
            }
        };
}

/// <summary>
/// Account balance and buying power information from a broker.
/// </summary>
public sealed record AccountInfo
{
    /// <summary>Account identifier at the broker.</summary>
    public required string AccountId { get; init; }

    /// <summary>Total equity value (cash + positions at mark).</summary>
    public decimal Equity { get; init; }

    /// <summary>Available cash balance.</summary>
    public decimal Cash { get; init; }

    /// <summary>Buying power available for new orders.</summary>
    public decimal BuyingPower { get; init; }

    /// <summary>Currency code (e.g., "USD").</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>Account status (e.g., "active", "restricted").</summary>
    public string Status { get; init; } = "active";

    /// <summary>Timestamp when this info was retrieved.</summary>
    public DateTimeOffset RetrievedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A position held at the broker.
/// </summary>
public sealed record BrokerPosition
{
    /// <summary>Stable provider-side identifier for this position.</summary>
    public string? PositionId { get; init; }

    /// <summary>Ticker symbol.</summary>
    public required string Symbol { get; init; }

    /// <summary>Underlying symbol for derivatives; defaults to <see cref="Symbol"/> for spot assets.</summary>
    public string? UnderlyingSymbol { get; init; }

    /// <summary>Human-readable description suitable for blotter-style UIs.</summary>
    public string? Description { get; init; }

    /// <summary>Signed quantity (negative for short positions).</summary>
    public decimal Quantity { get; init; }

    /// <summary>Average entry price.</summary>
    public decimal AverageEntryPrice { get; init; }

    /// <summary>Current market price.</summary>
    public decimal MarketPrice { get; init; }

    /// <summary>Market value (absolute quantity * market price).</summary>
    public decimal MarketValue { get; init; }

    /// <summary>Unrealized P&amp;L.</summary>
    public decimal UnrealizedPnl { get; init; }

    /// <summary>Asset class (e.g., "equity", "us_treasury", "bond").</summary>
    public string AssetClass { get; init; } = "equity";

<<<<<<< HEAD
    /// <summary>Expiration date for derivative positions when available.</summary>
    public DateOnly? Expiration { get; init; }

    /// <summary>Strike price for option positions when available.</summary>
    public decimal? Strike { get; init; }

    /// <summary>Option right or derivative-side classification (e.g., "call", "put").</summary>
    public string? Right { get; init; }

    /// <summary>Provider-specific metadata required to manage the position.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
=======
    /// <summary>
    /// Accrued interest for fixed income positions (bonds, treasuries).
    /// Null for non-fixed-income positions.
    /// </summary>
    public decimal? AccruedInterest { get; init; }
>>>>>>> d5ab6a6bf3983ec9a9f290c5b8296eeb2fbc46a3
}

/// <summary>
/// An open order at the broker.
/// </summary>
public sealed record BrokerOrder
{
    /// <summary>Broker-assigned order ID.</summary>
    public required string OrderId { get; init; }

    /// <summary>Client-assigned order ID.</summary>
    public string? ClientOrderId { get; init; }

    /// <summary>Ticker symbol.</summary>
    public required string Symbol { get; init; }

    /// <summary>Order side.</summary>
    public required OrderSide Side { get; init; }

    /// <summary>Order type.</summary>
    public required OrderType Type { get; init; }

    /// <summary>Total requested quantity.</summary>
    public decimal Quantity { get; init; }

    /// <summary>Quantity filled so far.</summary>
    public decimal FilledQuantity { get; init; }

    /// <summary>Limit price (if applicable).</summary>
    public decimal? LimitPrice { get; init; }

    /// <summary>Stop price (if applicable).</summary>
    public decimal? StopPrice { get; init; }

    /// <summary>Current order status.</summary>
    public required OrderStatus Status { get; init; }

    /// <summary>When the order was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Health check result for a brokerage gateway.
/// </summary>
public sealed record BrokerHealthStatus
{
    /// <summary>Whether the gateway is healthy and can accept orders.</summary>
    public required bool IsHealthy { get; init; }

    /// <summary>Whether the connection to the broker is alive.</summary>
    public bool IsConnected { get; init; }

    /// <summary>Whether the market is currently open for trading.</summary>
    public bool? IsMarketOpen { get; init; }

    /// <summary>Human-readable status message.</summary>
    public string? Message { get; init; }

    /// <summary>Timestamp of the health check.</summary>
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;

    public static BrokerHealthStatus Healthy(string? message = null) =>
        new() { IsHealthy = true, IsConnected = true, Message = message };

    public static BrokerHealthStatus Unhealthy(string reason) =>
        new() { IsHealthy = false, IsConnected = false, Message = reason };
}
