using Meridian.Execution.Models;
using Meridian.Execution.Interfaces;
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
    private readonly IPortfolioState _visiblePortfolio;
    private readonly Lock _orderLock = new();
    private readonly List<Order> _pendingOrders = [];
    private readonly List<Guid> _pendingCancellations = [];
    private readonly List<Guid> _locallyCancelledOrders = [];
    private readonly string _defaultAccountId;
    private readonly string? _accountingFinancialAccountId;
    private readonly string? _accountingStrategyRunId;
    private readonly Meridian.Ledger.Ledger _sessionLedger = new();

    public LiveStrategyExecutionContext(
        IOrderGateway gateway,
        ILiveFeedAdapter feed,
        IPortfolioState portfolio,
        IReadOnlySet<string> universe,
        string defaultAccountId = BacktestDefaults.DefaultBrokerageAccountId,
        string? accountingFinancialAccountId = null,
        string? accountingStrategyRunId = null)
    {
        Gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _portfolio = portfolio ?? throw new ArgumentNullException(nameof(portfolio));
        Universe = universe ?? throw new ArgumentNullException(nameof(universe));
        _defaultAccountId = defaultAccountId;
        _accountingFinancialAccountId = string.IsNullOrWhiteSpace(accountingFinancialAccountId) ? null : accountingFinancialAccountId;
        _accountingStrategyRunId = string.IsNullOrWhiteSpace(accountingStrategyRunId) ? null : accountingStrategyRunId;
        _visiblePortfolio = _accountingFinancialAccountId is null ? _portfolio : new ScopedPortfolioView(this);
        CurrentTime = DateTimeOffset.UtcNow;
        CurrentDate = DateOnly.FromDateTime(CurrentTime.UtcDateTime);
    }

    // ── IExecutionContext ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IOrderGateway Gateway { get; }

    /// <inheritdoc/>
    public ILiveFeedAdapter Feed => _feed;

    /// <inheritdoc/>
    public IPortfolioState Portfolio => _visiblePortfolio;

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
    public decimal Cash => _accountingFinancialAccountId is null ? _portfolio.Cash : ReadScopedAccountSnapshot().Cash;

    /// <inheritdoc/>
    public decimal PortfolioValue => _accountingFinancialAccountId is null ? _portfolio.PortfolioValue
        : RequireEquity(ReadScopedAccountSnapshot());

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, Position> Positions => ToStrategyPositions(_accountingFinancialAccountId is null
        ? _portfolio.Positions.Values : ReadScopedAccountSnapshot().Positions);

    private static IReadOnlyDictionary<string, Position> ToStrategyPositions(IEnumerable<Meridian.Execution.Sdk.IPosition> positions) =>
        positions.ToDictionary(
            static item => item.Symbol,
            static item => new Position(item.Symbol, item.Quantity, item.AverageCostBasis, item.UnrealizedPnl, item.RealizedPnl)
            {
                // The unrounded size travels with the position: a strategy attributing ownership
                // cannot tell a foreign fractional holding from no holding without it.
                ExactQuantity = item.ExactQuantity
            },
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Signed market value from what <see cref="Meridian.Execution.Sdk.IPosition"/> exposes: open cost plus unrealised
    /// P&amp;L equals quantity × multiplier × mark for an ordinary position. A variation-margin
    /// position reports only its unsettled change as unrealised, so this approximates its exposure.
    /// </summary>
    private static decimal SignedMarketValue(Meridian.Execution.Sdk.IPosition position)
    {
        var (quantity, multiplier) = position is ExecutionPosition execution
            ? (execution.ExactQuantity, execution.ContractMultiplier)
            : ((decimal)position.Quantity, 1m);
        return (quantity * multiplier * position.AverageCostBasis) + position.UnrealizedPnl;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, FinancialAccountSnapshot> Accounts
    {
        get
        {
            if (_accountingFinancialAccountId is not null)
                return ToStrategyAccount(ReadScopedAccountSnapshot());
            var positions = Positions;
            var longMarketValue = 0m;
            var shortMarketValue = 0m;
            var holdsShort = false;
            foreach (var position in _portfolio.Positions.Values)
            {
                var value = SignedMarketValue(position);
                if (position.Quantity < 0)
                {
                    holdsShort = true;
                    shortMarketValue += value;
                }
                else
                {
                    longMarketValue += value;
                }
            }

            var cash = _portfolio.Cash;
            var snapshot = new FinancialAccountSnapshot(
                AccountId: _defaultAccountId,
                DisplayName: "Live Session Account",
                Kind: FinancialAccountKind.Brokerage,
                Institution: Gateway.BrokerName,
                Cash: cash,
                MarginBalance: Math.Min(0m, cash),
                LongMarketValue: longMarketValue,
                ShortMarketValue: shortMarketValue,
                Equity: _portfolio.PortfolioValue,
                Positions: positions,
                // The order gateway publishes no short-selling or margin capability, so infer
                // what the state proves: margin-aware portfolios report buying power above cash,
                // and an account holding a short evidently permits short selling. Otherwise stay
                // conservative and report the capability as unavailable.
                Rules: new FinancialAccountRules(
                    AllowMargin: _portfolio.BuyingPower > cash,
                    AllowShortSelling: holdsShort));
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

                // This cancellation is already terminal and produces no execution report, so a
                // strategy tracking the order would never hear about it. Recorded here so the run
                // session can tell it, the same way it reports outcomes that do come back from the
                // gateway.
                _locallyCancelledOrders.Add(orderId);
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

    /// <summary>Captures one native receipt for one authorized account without rebuilding money from quotes.</summary>
    internal PositionAccountingEvidenceSnapshot? CapturePositionAccountingEvidence()
    {
        if (_accountingFinancialAccountId is null || _accountingStrategyRunId is null
            || _portfolio is not IPositionAccountingEvidenceSource source)
            return null;
        var captured = source.CapturePositionAccountingEvidence();
        if (!captured.CoveredFinancialAccountIds.Contains(_accountingFinancialAccountId, StringComparer.OrdinalIgnoreCase))
            return null;
        var positions = captured.Positions
            .Where(position => string.Equals(position.FinancialAccountId, _accountingFinancialAccountId, StringComparison.OrdinalIgnoreCase))
            .Where(position => position.StrategyRunId is null || position.StrategyRunId == _accountingStrategyRunId)
            .Select(position => position with
            {
                Quantity = position.StrategyRunId is null ? null : position.Quantity,
                MonetaryBasis = position.StrategyRunId is null ? null : position.MonetaryBasis,
                CoverageFailure = position.StrategyRunId is null
                    ? $"The captured account position has missing or mixed strategy-run acquisition history. {position.CoverageFailure}".TrimEnd()
                    : position.CoverageFailure,
                EvidenceReferences = position.EvidenceReferences is null ? null : Array.AsReadOnly(position.EvidenceReferences.ToArray())
            }).ToArray();
        return captured with
        {
            Positions = Array.AsReadOnly(positions),
            CoveredFinancialAccountIds = Array.AsReadOnly(new[] { _accountingFinancialAccountId }),
            Accounts = Array.AsReadOnly(captured.Accounts.Where(account =>
                string.Equals(account.AccountId, _accountingFinancialAccountId, StringComparison.OrdinalIgnoreCase)).ToArray())
        };
    }

    /// <summary>One native capture supplies every metric in this observation.</summary>
    internal LivePortfolioObservation CapturePortfolioObservation()
    {
        if (_accountingFinancialAccountId is null)
            return new(Cash, PortfolioValue, Positions, Accounts, null);
        var captured = CapturePositionAccountingEvidence();
        if (captured is null && _portfolio is IPositionAccountingEvidenceSource)
            throw new InvalidOperationException("The native source does not cover the run's authorized financial-account observation.");
        var account = captured is not null ? SelectCapturedAccount(captured)
            : (_portfolio as IMultiAccountPortfolioState)?.GetAccount(_accountingFinancialAccountId)?.TakeSnapshot()
                ?? throw new InvalidOperationException("The run's authorized financial-account partition is unavailable.");
        return new(account.Cash, RequireEquity(account), ToStrategyPositions(account.Positions), ToStrategyAccount(account), captured);
    }

    private ExecutionAccountDetailSnapshot ReadScopedAccountSnapshot()
    {
        if (_portfolio is IPositionAccountingEvidenceSource source)
            return SelectCapturedAccount(source.CapturePositionAccountingEvidence());
        return (_portfolio as IMultiAccountPortfolioState)?.GetAccount(_accountingFinancialAccountId!)?.TakeSnapshot()
            ?? throw new InvalidOperationException("The run's authorized financial-account partition is unavailable.");
    }

    private ExecutionAccountDetailSnapshot SelectCapturedAccount(PositionAccountingEvidenceSnapshot captured)
    {
        if (!captured.CoveredFinancialAccountIds.Contains(_accountingFinancialAccountId!, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The native source does not cover the run's authorized financial account.");
        var accounts = captured.Accounts.Where(account =>
            string.Equals(account.AccountId, _accountingFinancialAccountId, StringComparison.OrdinalIgnoreCase)).ToArray();
        return accounts.Length == 1 ? accounts[0]
            : throw new InvalidOperationException("The native account observation is missing or ambiguous.");
    }

    private static decimal RequireEquity(ExecutionAccountDetailSnapshot account) => account.AccountingEquity
        ?? throw new InvalidOperationException("The native account has no retained accounting equity measurement.");

    private IReadOnlyDictionary<string, FinancialAccountSnapshot> ToStrategyAccount(ExecutionAccountDetailSnapshot account)
    {
        var snapshot = new FinancialAccountSnapshot(account.AccountId, account.DisplayName,
            account.Kind == AccountKind.Bank ? FinancialAccountKind.Bank : FinancialAccountKind.Brokerage,
            Gateway.BrokerName, account.Cash, -account.MarginBalance,
            account.AccountingLongMarketValue ?? throw new InvalidOperationException("Native long carrying market value is unavailable."),
            account.AccountingShortMarketValue ?? throw new InvalidOperationException("Native short carrying market value is unavailable."),
            RequireEquity(account), ToStrategyPositions(account.Positions),
            new FinancialAccountRules(AllowMargin: account.BuyingPower > account.Cash,
                AllowShortSelling: account.Positions.Any(static position => position.ExactQuantity < 0m)))
        { FunctionalCurrency = account.FunctionalCurrency };
        return new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase) { [account.AccountId] = snapshot };
    }

    private sealed class ScopedPortfolioView(LiveStrategyExecutionContext context) : IPortfolioState
    {
        public decimal Cash => context.Cash;
        public decimal PortfolioValue => context.PortfolioValue;
        public decimal UnrealisedPnl => context.ReadScopedAccountSnapshot().UnrealisedPnl;
        public decimal RealisedPnl => context.ReadScopedAccountSnapshot().RealisedPnl;
        public decimal BuyingPower => context.ReadScopedAccountSnapshot().BuyingPower;
        public IReadOnlyDictionary<string, Meridian.Execution.Sdk.IPosition> Positions => context.ReadScopedAccountSnapshot().Positions
            .ToDictionary(static position => position.Symbol, static position => (Meridian.Execution.Sdk.IPosition)position,
                StringComparer.OrdinalIgnoreCase);
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

    /// <summary>
    /// Returns and clears the orders cancelled before they were ever submitted. These are terminal
    /// on the spot: no gateway ever saw them, so no execution report will report their end.
    /// </summary>
    internal IReadOnlyList<Guid> DrainLocallyCancelledOrders()
    {
        lock (_orderLock)
        {
            if (_locallyCancelledOrders.Count == 0)
            {
                return [];
            }

            var drained = _locallyCancelledOrders.ToArray();
            _locallyCancelledOrders.Clear();
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
