using Meridian.Execution.Models;
using Meridian.Ledger;
using OrderRequest = Meridian.Backtesting.Sdk.OrderRequest;
using OrderType = Meridian.Backtesting.Sdk.OrderType;

namespace Meridian.Strategies.Live;

/// <summary>
/// Live-session context handed to strategy code. Implements <see cref="IBacktestContext"/> so the
/// same strategy callbacks that run under <c>BacktestEngine</c> can place orders against a live
/// session, and <see cref="IExecutionContext"/> so lifecycle-aware strategies see the gateway,
/// feed, and portfolio surfaces. Orders are queued and drained by the live engine each event,
/// mirroring the backtest engine's collect-then-route loop; they are then routed through the
/// governed order management system rather than filled locally.
/// </summary>
public sealed class LiveStrategyExecutionContext : IBacktestContext, IExecutionContext
{
    private readonly ILiveFeedAdapter _feed;
    private readonly IPortfolioState _portfolio;
    private readonly Lock _orderLock = new();
    private readonly List<Order> _pendingOrders = [];
    private readonly List<Guid> _pendingCancellations = [];
    private readonly string _defaultAccountId;
    private readonly Meridian.Ledger.Ledger _sessionLedger = new();

    public LiveStrategyExecutionContext(
        IOrderGateway gateway,
        ILiveFeedAdapter feed,
        IPortfolioState portfolio,
        IReadOnlySet<string> universe,
        string defaultAccountId = BacktestDefaults.DefaultBrokerageAccountId)
    {
        Gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _portfolio = portfolio ?? throw new ArgumentNullException(nameof(portfolio));
        Universe = universe ?? throw new ArgumentNullException(nameof(universe));
        _defaultAccountId = defaultAccountId;
        CurrentTime = DateTimeOffset.UtcNow;
        CurrentDate = DateOnly.FromDateTime(CurrentTime.UtcDateTime);
    }

    // ── IExecutionContext ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IOrderGateway Gateway { get; }

    /// <inheritdoc/>
    public ILiveFeedAdapter Feed => _feed;

    /// <inheritdoc/>
    public IPortfolioState Portfolio => _portfolio;

    /// <inheritdoc cref="IBacktestContext.Universe"/>
    public IReadOnlySet<string> Universe { get; }

    /// <inheritdoc cref="IBacktestContext.CurrentTime"/>
    public DateTimeOffset CurrentTime { get; private set; }

    /// <inheritdoc/>
    IReadOnlyLedger? IExecutionContext.Ledger => _sessionLedger;

    // ── IBacktestContext ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public DateOnly CurrentDate { get; private set; }

    /// <inheritdoc/>
    public decimal Cash => _portfolio.Cash;

    /// <inheritdoc/>
    public decimal PortfolioValue => _portfolio.PortfolioValue;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, Position> Positions =>
        _portfolio.Positions.ToDictionary(
            static pair => pair.Key,
            static pair => new Position(
                pair.Value.Symbol,
                pair.Value.Quantity,
                pair.Value.AverageCostBasis,
                pair.Value.UnrealizedPnl,
                pair.Value.RealizedPnl),
            StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, FinancialAccountSnapshot> Accounts
    {
        get
        {
            var positions = Positions;
            var snapshot = new FinancialAccountSnapshot(
                AccountId: _defaultAccountId,
                DisplayName: "Live Session Account",
                Kind: FinancialAccountKind.Brokerage,
                Institution: Gateway.BrokerName,
                Cash: _portfolio.Cash,
                MarginBalance: 0m,
                LongMarketValue: 0m,
                ShortMarketValue: 0m,
                Equity: _portfolio.PortfolioValue,
                Positions: positions,
                Rules: new FinancialAccountRules(AllowMargin: false, AllowShortSelling: false));
            return new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [_defaultAccountId] = snapshot
            };
        }
    }

    /// <inheritdoc/>
    public IReadOnlyLedger Ledger => _sessionLedger;

    /// <inheritdoc/>
    public decimal? GetLastPrice(string symbol)
    {
        if (_feed.GetLastTrade(symbol) is { Price: > 0m } trade)
        {
            return trade.Price;
        }

        if (_feed.GetLastQuote(symbol) is { } quote)
        {
            var mid = quote.MidPrice ?? (quote.BidPrice + quote.AskPrice) / 2m;
            if (mid > 0m)
            {
                return mid;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public Guid PlaceOrder(OrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TakeProfitPrice.HasValue || request.StopLossPrice.HasValue)
        {
            throw new NotSupportedException(
                "Contingent take-profit/stop-loss exits are not supported by the live trading engine yet. " +
                "Place explicit exit orders instead of attached brackets.");
        }

        return Enqueue(new Order(
            OrderId: Guid.NewGuid(),
            Symbol: request.Symbol,
            Type: request.Type,
            Quantity: request.Quantity,
            LimitPrice: request.LimitPrice,
            StopPrice: request.StopPrice,
            SubmittedAt: CurrentTime,
            TimeInForce: request.TimeInForce,
            ExecutionModel: request.ExecutionModel,
            AllowPartialFills: request.AllowPartialFills,
            ProviderParameters: request.ProviderParameters,
            AccountId: request.AccountId ?? _defaultAccountId));
    }

    /// <inheritdoc/>
    public Guid PlaceBracketOrder(BracketOrderRequest request)
        => throw new NotSupportedException(
            "Bracket orders are not supported by the live trading engine yet. " +
            "Place explicit entry and exit orders instead.");

    /// <inheritdoc/>
    public Guid PlaceMarketOrder(string symbol, long quantity) =>
        PlaceMarketOrder(symbol, quantity, _defaultAccountId);

    /// <inheritdoc/>
    public Guid PlaceMarketOrder(string symbol, long quantity, string accountId) =>
        PlaceOrder(new OrderRequest(symbol, quantity, OrderType.Market, AccountId: accountId));

    /// <inheritdoc/>
    public Guid PlaceLimitOrder(string symbol, long quantity, decimal limitPrice) =>
        PlaceLimitOrder(symbol, quantity, limitPrice, _defaultAccountId);

    /// <inheritdoc/>
    public Guid PlaceLimitOrder(string symbol, long quantity, decimal limitPrice, string accountId) =>
        PlaceOrder(new OrderRequest(symbol, quantity, OrderType.Limit, LimitPrice: limitPrice, AccountId: accountId));

    /// <inheritdoc/>
    public Guid PlaceStopMarketOrder(string symbol, long quantity, decimal stopPrice) =>
        PlaceStopMarketOrder(symbol, quantity, stopPrice, _defaultAccountId);

    /// <inheritdoc/>
    public Guid PlaceStopMarketOrder(string symbol, long quantity, decimal stopPrice, string accountId) =>
        PlaceOrder(new OrderRequest(symbol, quantity, OrderType.StopMarket, StopPrice: stopPrice, AccountId: accountId));

    /// <inheritdoc/>
    public Guid PlaceStopLimitOrder(string symbol, long quantity, decimal stopPrice, decimal limitPrice) =>
        PlaceStopLimitOrder(symbol, quantity, stopPrice, limitPrice, _defaultAccountId);

    /// <inheritdoc/>
    public Guid PlaceStopLimitOrder(string symbol, long quantity, decimal stopPrice, decimal limitPrice, string accountId) =>
        PlaceOrder(new OrderRequest(
            symbol, quantity, OrderType.StopLimit, LimitPrice: limitPrice, StopPrice: stopPrice, AccountId: accountId));

    /// <inheritdoc/>
    public void CancelOrder(Guid orderId)
    {
        lock (_orderLock)
        {
            // An order still queued locally can be cancelled before it ever reaches the OMS.
            var queuedIndex = _pendingOrders.FindIndex(order => order.OrderId == orderId);
            if (queuedIndex >= 0)
            {
                _pendingOrders.RemoveAt(queuedIndex);
                return;
            }

            _pendingCancellations.Add(orderId);
        }
    }

    /// <inheritdoc/>
    public void CancelContingentOrders(Guid parentOrderId)
    {
        // Contingent exits cannot be created in live sessions (PlaceBracketOrder and
        // take-profit/stop-loss requests are rejected), so there is nothing to cancel.
    }

    // ── Engine surface ───────────────────────────────────────────────────────

    /// <summary>Advances the session clock to the timestamp of the event being processed.</summary>
    internal void Advance(DateTimeOffset time)
    {
        CurrentTime = time;
        CurrentDate = DateOnly.FromDateTime(time.UtcDateTime);
    }

    /// <summary>Returns and clears the orders queued by strategy callbacks since the last drain.</summary>
    internal IReadOnlyList<Order> DrainPendingOrders()
    {
        lock (_orderLock)
        {
            if (_pendingOrders.Count == 0)
            {
                return [];
            }

            var drained = _pendingOrders.ToArray();
            _pendingOrders.Clear();
            return drained;
        }
    }

    /// <summary>Returns and clears the cancellation requests queued since the last drain.</summary>
    internal IReadOnlyList<Guid> DrainPendingCancellations()
    {
        lock (_orderLock)
        {
            if (_pendingCancellations.Count == 0)
            {
                return [];
            }

            var drained = _pendingCancellations.ToArray();
            _pendingCancellations.Clear();
            return drained;
        }
    }

    private Guid Enqueue(Order order)
    {
        if (order.Quantity == 0)
        {
            throw new ArgumentException("Order quantity cannot be zero.", nameof(order));
        }

        lock (_orderLock)
        {
            _pendingOrders.Add(order);
        }

        return order.OrderId;
    }
}
