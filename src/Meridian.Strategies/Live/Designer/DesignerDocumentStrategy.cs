using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Meridian.Strategies.Live.Designer;

/// <summary>
/// Executes a compiled <see cref="DesignerStrategyPlan"/> as an ordinary
/// <see cref="IBacktestStrategy"/>, so a promoted Strategy Designer document reaches paper and
/// live execution through the same <see cref="BacktestStrategyLiveAdapter"/>, order gateway, and
/// fill path as a hand-written or plugin strategy — with real fills, not a simulated lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// The plan is fully resolved before construction, so this class makes no eligibility decision the
/// document did not state.
/// </para>
/// <para>
/// Two ownership rules keep a designer run from reaching past its own mandate. First, positions
/// are attributed: <see cref="IBacktestContext.Positions"/> is the shared live portfolio, so exits
/// are sized from what <em>this</em> run has filled rather than from the whole symbol quantity —
/// otherwise activating a document naming SPY could liquidate SPY held by another strategy or by
/// hand. The cost is that a resumed run does not recognise a position opened in a previous
/// session; declining to trade someone else's inventory is the safer side of that trade.
/// Second, a symbol whose fields are not yet warm is <em>indeterminate</em>: it is excluded from
/// entries and from exits alike, because "no data yet" is not an exit signal and a restart must
/// not liquidate valid holdings while history warms.
/// </para>
/// </remarks>
internal sealed class DesignerDocumentStrategy : IBacktestStrategy
{
    /// <summary>
    /// How long a submitted order may stay pending before the symbol is retried. The strategy sees
    /// fills but not rejections, cancellations, or submission failures, so without this bound one
    /// rejected order would block a symbol from being entered or exited for the life of the run.
    /// </summary>
    private static readonly TimeSpan PendingOrderTimeout = TimeSpan.FromMinutes(5);

    private readonly DesignerStrategyPlan _plan;
    private readonly ILogger? _logger;
    private readonly Dictionary<string, DesignerLiveFields.SymbolWindow> _windows =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingOrder> _pendingOrders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _ownedQuantities = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reportedUnattributed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _planUniverse;
    private readonly bool _needsSessionFields;

    public DesignerDocumentStrategy(DesignerStrategyPlan plan, ILogger? logger = null)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _logger = logger;
        _planUniverse = new HashSet<string>(plan.Universe, StringComparer.OrdinalIgnoreCase);

        // A plan reading only spot fields can decide on every event cheaply. One reading a session
        // window cannot change its answer until the session rolls, so re-running the full
        // cross-section per quote would be pure cost.
        _needsSessionFields = plan.RequiredFields.Any(static field =>
            field.Equals(DesignerLiveFields.Momentum63D, StringComparison.OrdinalIgnoreCase)
            || field.Equals(DesignerLiveFields.Volatility20D, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public string Name => $"Designer: {_plan.Name}";

    /// <inheritdoc/>
    public void Initialize(IBacktestContext ctx) =>
        _logger?.LogInformation(
            "Designer document {DocumentId} activated with {EntryGateCount} entry gate(s), " +
            "{RiskGuardCount} risk guard(s), universe {UniverseSize}, ranked {Ranked}",
            _plan.DocumentId,
            _plan.EntryGates.Count,
            _plan.RiskGuards.Count,
            _plan.Universe.Count,
            _plan.RankExpression is not null);

    /// <inheritdoc/>
    public void OnTrade(Trade trade, IBacktestContext ctx) =>
        Observe(trade.Symbol, trade.Price, DateOnly.FromDateTime(trade.Timestamp.UtcDateTime), ctx);

    /// <inheritdoc/>
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx)
    {
        var mid = quote.BidPrice > 0m && quote.AskPrice > 0m
            ? (quote.BidPrice + quote.AskPrice) / 2m
            : 0m;
        if (mid > 0m)
        {
            Observe(quote.Symbol, mid, DateOnly.FromDateTime(quote.Timestamp.UtcDateTime), ctx);
        }
    }

    /// <inheritdoc/>
    public void OnBar(HistoricalBar bar, IBacktestContext ctx) =>
        Observe(bar.Symbol, bar.Close, bar.SessionDate, ctx);

    /// <inheritdoc/>
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx)
    {
        // Level-2 depth carries no field the designer catalog exposes.
    }

    /// <inheritdoc/>
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx)
    {
        if (string.IsNullOrWhiteSpace(fill.Symbol))
        {
            return;
        }

        _ownedQuantities.TryGetValue(fill.Symbol, out var owned);
        var updated = owned + fill.FilledQuantity;
        if (updated == 0L)
        {
            _ownedQuantities.Remove(fill.Symbol);
        }
        else
        {
            _ownedQuantities[fill.Symbol] = updated;
        }

        // A partial fill leaves the rest of the order working; clearing the pending marker here
        // would let the next rebalance submit a second order for the same intent and oversell if
        // both complete.
        if (!_pendingOrders.TryGetValue(fill.Symbol, out var pending))
        {
            return;
        }

        var remaining = pending.Remaining - fill.FilledQuantity;
        if (remaining == 0L || Math.Sign(remaining) != Math.Sign(pending.Remaining))
        {
            _pendingOrders.Remove(fill.Symbol);
        }
        else
        {
            _pendingOrders[fill.Symbol] = pending with { Remaining = remaining };
        }
    }

    /// <inheritdoc/>
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) => Rebalance(ctx);

    /// <inheritdoc/>
    public void OnFinished(IBacktestContext ctx)
    {
    }

    /// <summary>
    /// True when the plan selects across the universe rather than symbol by symbol — a rank score,
    /// a minimum breadth, or a cap on concurrent names.
    /// </summary>
    private bool IsCrossSectional =>
        _plan.RankExpression is not null
        || _plan.MinimumUniverseSize is not null
        || _plan.MaximumPositions is not null;

    /// <summary>
    /// Records one observation and re-decides when the answer could have changed.
    /// </summary>
    /// <remarks>
    /// The session date comes from the event itself rather than from a bar, because the trading
    /// engine's market-event tap withholds <see cref="HistoricalBar"/> from the strategy hub: on
    /// the live path a strategy sees only trades and quotes, so a bar-only session clock would
    /// leave every session window permanently cold and the document silently unable to trade.
    /// </remarks>
    private void Observe(string symbol, decimal price, DateOnly sessionDate, IBacktestContext ctx)
    {
        if (!TryGetWindow(symbol, out var window) || price <= 0m)
        {
            return;
        }

        var sessionsBefore = window!.SessionCount;
        window.Observe(price, sessionDate);

        // A session-window plan can only change its mind when the session rolls; a spot-only plan
        // can change on any event.
        if (!_needsSessionFields || window.SessionCount != sessionsBefore)
        {
            Rebalance(ctx);
        }
    }

    private bool TryGetWindow(string symbol, out DesignerLiveFields.SymbolWindow? window)
    {
        window = null;
        if (string.IsNullOrWhiteSpace(symbol) || !_planUniverse.Contains(symbol))
        {
            return false;
        }

        if (!_windows.TryGetValue(symbol, out var existing))
        {
            existing = new DesignerLiveFields.SymbolWindow();
            _windows[symbol] = existing;
        }

        window = existing;
        return true;
    }

    private void Rebalance(IBacktestContext ctx)
    {
        CancelStalePendingOrders(ctx, ctx.CurrentTime);

        var eligible = new List<(string Symbol, decimal Score, IReadOnlyDictionary<string, decimal> Fields)>();
        var indeterminate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in _plan.Universe)
        {
            if (!ctx.Universe.Contains(symbol))
            {
                continue;
            }

            // Inventory this run cannot account for is not tradeable either way. After a host
            // restart the ownership map is empty while the broker still holds the run's earlier
            // fills, so entering would double the position; the same shape also covers a position
            // opened by another strategy or by hand, which is not this run's to unwind. The two are
            // indistinguishable from here, and both answers are "do not trade this symbol".
            if (!IsAttributable(ctx, symbol))
            {
                // Same treatment as a cold field: for a cross-sectional plan a name that cannot be
                // traded cannot be silently dropped from the comparison either, or the remaining
                // names get ranked against a set the document did not describe.
                if (IsCrossSectional)
                {
                    return;
                }

                indeterminate.Add(symbol);
                continue;
            }

            if (!_windows.TryGetValue(symbol, out var window)
                || !window.TryResolve(_plan.RequiredFields, ctx, symbol, out var fields, out _))
            {
                // A cross-sectional document ranks or bounds the whole universe against itself, so
                // deciding on a partial cross-section is not a smaller version of the promoted
                // strategy -- it is a different one. "Top 2 of 3" evaluated after the first symbol
                // arrives would buy that symbol regardless of where it ranks. Wait for the picture.
                if (IsCrossSectional)
                {
                    return;
                }

                indeterminate.Add(symbol);
                continue;
            }

            if (!PassesAll(_plan.EntryGates, fields, symbol) || !PassesAll(_plan.RiskGuards, fields, symbol))
            {
                continue;
            }

            var score = 0m;
            if (_plan.RankExpression is not null)
            {
                try
                {
                    score = _plan.RankExpression.Evaluate(fields).Number;
                }
                catch (DesignerExpressionException ex)
                {
                    // Dropping just this candidate would rank the rest against an incomplete
                    // cross-section -- the same defect the cold-field guard above exists to prevent.
                    // A score the document cannot produce for one name invalidates the comparison.
                    _logger?.LogWarning(
                        "Designer document {DocumentId} could not score {Symbol}; the cross-section is "
                        + "indeterminate this pass: {Reason}",
                        _plan.DocumentId,
                        symbol,
                        ex.Message);
                    return;
                }
            }

            eligible.Add((symbol, score, fields));
        }

        // minSize is the document's statement that the strategy is only meaningful across a
        // breadth of names. Trading a thinner set would be a different strategy than the one
        // promoted, so nothing is entered until the breadth exists.
        var targets = _plan.MinimumUniverseSize is { } minimum && eligible.Count < minimum
            ? Array.Empty<(string Symbol, decimal Score, IReadOnlyDictionary<string, decimal> Fields)>()
            : eligible
                .OrderByDescending(static candidate => candidate.Score)
                .ThenBy(static candidate => candidate.Symbol, StringComparer.OrdinalIgnoreCase)
                .Take(_plan.MaximumPositions ?? eligible.Count)
                .ToArray();

        var targetSet = new HashSet<string>(
            targets.Select(static candidate => candidate.Symbol),
            StringComparer.OrdinalIgnoreCase);

        CloseUnwantedPositions(ctx, targetSet, indeterminate);
        OpenTargetPositions(ctx, targets);
    }

    private void CloseUnwantedPositions(
        IBacktestContext ctx,
        IReadOnlySet<string> targetSet,
        IReadOnlySet<string> indeterminate)
    {
        foreach (var symbol in _plan.Universe)
        {
            if (targetSet.Contains(symbol)
                || indeterminate.Contains(symbol)
                || _pendingOrders.ContainsKey(symbol)
                || !_ownedQuantities.TryGetValue(symbol, out var owned)
                || owned == 0L)
            {
                continue;
            }

            SubmitOrder(ctx, symbol, -owned, "exiting: entry conditions no longer hold");
        }
    }

    private void OpenTargetPositions(
        IBacktestContext ctx,
        IReadOnlyList<(string Symbol, decimal Score, IReadOnlyDictionary<string, decimal> Fields)> targets)
    {
        foreach (var (symbol, _, fields) in targets)
        {
            if (_pendingOrders.ContainsKey(symbol))
            {
                continue;
            }

            _ownedQuantities.TryGetValue(symbol, out var owned);
            var desiredSign = _plan.Trade.Side == DesignerTradeSide.Short ? -1 : 1;

            // A holding on the wrong side is not the promoted position. Closing it first is the
            // only way a long document inheriting its own short (or the reverse) converges.
            if (owned != 0L && Math.Sign(owned) != desiredSign)
            {
                SubmitOrder(ctx, symbol, -owned, "closing a position opposite the document's trade side");
                continue;
            }

            if (owned != 0L)
            {
                continue;
            }

            var quantity = ResolveQuantity(symbol, ctx);
            if (quantity == 0L || !RiskGuardsAllowEntry(symbol, quantity, fields, ctx))
            {
                continue;
            }

            SubmitOrder(ctx, symbol, quantity, $"entering via trade cell {_plan.Trade.CellId}");
        }
    }

    /// <summary>
    /// Re-checks the document's risk guards against the position the order would create.
    /// </summary>
    /// <remarks>
    /// Guards are first evaluated on current state, where a flat symbol reports
    /// <c>PORTFOLIO_WEIGHT</c> of zero and an exposure cap such as <c>PORTFOLIO_WEIGHT &lt;= 0.10</c>
    /// passes trivially — then sizing could submit far more than the cap allows. Evaluating the
    /// projected weight is what makes the promoted control actually bind on entry.
    /// </remarks>
    private bool RiskGuardsAllowEntry(
        string symbol,
        long quantity,
        IReadOnlyDictionary<string, decimal> fields,
        IBacktestContext ctx)
    {
        if (_plan.RiskGuards.Count == 0
            || !fields.ContainsKey(DesignerLiveFields.PortfolioWeight)
            || ctx.PortfolioValue <= 0m)
        {
            return true;
        }

        var price = ResolvePrice(symbol, ctx);
        if (price <= 0m)
        {
            return false;
        }

        var projected = new Dictionary<string, decimal>(fields, StringComparer.OrdinalIgnoreCase)
        {
            [DesignerLiveFields.PortfolioWeight] = quantity * price / ctx.PortfolioValue
        };

        if (PassesAll(_plan.RiskGuards, projected, symbol))
        {
            return true;
        }

        _logger?.LogInformation(
            "Designer document {DocumentId} declined an entry in {Symbol}: the order would breach a risk guard "
            + "at the projected portfolio weight",
            _plan.DocumentId,
            symbol);
        return false;
    }

    private void SubmitOrder(IBacktestContext ctx, string symbol, long quantity, string reason)
    {
        if (quantity == 0L)
        {
            return;
        }

        var orderId = ctx.PlaceMarketOrder(symbol, quantity);
        _pendingOrders[symbol] = new PendingOrder(orderId, quantity, ctx.CurrentTime, CancelRequested: false);
        _logger?.LogInformation(
            "Designer document {DocumentId} {Reason} for {Symbol} quantity {Quantity}",
            _plan.DocumentId,
            reason,
            symbol,
            quantity);
    }

    /// <summary>
    /// Cancels an order that has been working past <see cref="PendingOrderTimeout"/> without
    /// completing, and leaves the symbol blocked.
    /// </summary>
    /// <remarks>
    /// The marker is deliberately <em>not</em> cleared. Freeing the symbol would let the next
    /// rebalance submit a replacement while the original is still live at the broker, and both
    /// could fill — doubling an entry or overselling an exit. <see cref="IBacktestStrategy"/>
    /// surfaces fills but no terminal order status, so there is no signal here that a cancel
    /// succeeded; refusing to send a second order is the only safe reading. A symbol stuck this way
    /// is logged at warning level rather than silently retried.
    /// </remarks>
    private void CancelStalePendingOrders(IBacktestContext ctx, DateTimeOffset now)
    {
        if (_pendingOrders.Count == 0)
        {
            return;
        }

        foreach (var symbol in _pendingOrders.Keys.ToArray())
        {
            var pending = _pendingOrders[symbol];
            if (pending.CancelRequested || now - pending.PlacedAt <= PendingOrderTimeout)
            {
                continue;
            }

            _pendingOrders[symbol] = pending with { CancelRequested = true };
            try
            {
                ctx.CancelOrder(pending.OrderId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Designer document {DocumentId} could not cancel the stale order for {Symbol}",
                    _plan.DocumentId,
                    symbol);
            }

            _logger?.LogWarning(
                "Designer document {DocumentId} cancelled order {OrderId} for {Symbol} after {TimeoutMinutes} "
                + "minutes with no completing fill. {Symbol} stays blocked: without a terminal order status the "
                + "engine cannot confirm the cancel, and submitting a replacement risks a double fill",
                _plan.DocumentId,
                pending.OrderId,
                symbol,
                PendingOrderTimeout.TotalMinutes,
                symbol);
        }
    }

    private bool PassesAll(
        IReadOnlyList<DesignerGate> gates,
        IReadOnlyDictionary<string, decimal> fields,
        string symbol)
    {
        foreach (var gate in gates)
        {
            try
            {
                if (!gate.Expression.EvaluateCondition(fields))
                {
                    return false;
                }
            }
            catch (DesignerExpressionException ex)
            {
                // A gate that cannot be evaluated is not a gate that passed. Refusing the symbol
                // keeps an evaluation fault from opening a position the document never authorised.
                _logger?.LogWarning(
                    "Designer document {DocumentId} could not evaluate gate {GateCellId} for {Symbol}: {Reason}",
                    _plan.DocumentId,
                    gate.CellId,
                    symbol,
                    ex.Message);
                return false;
            }
        }

        return true;
    }

    private bool IsAttributable(IBacktestContext ctx, string symbol)
    {
        var held = ctx.Positions.TryGetValue(symbol, out var position) ? position.Quantity : 0L;
        if (held == 0L)
        {
            return true;
        }

        _ownedQuantities.TryGetValue(symbol, out var owned);
        if (owned == held)
        {
            return true;
        }

        if (_reportedUnattributed.Add(symbol))
        {
            _logger?.LogWarning(
                "Designer document {DocumentId} is not trading {Symbol}: the portfolio holds {Held} share(s) but "
                + "this run accounts for {Owned}. The remainder belongs to another strategy or to a session before "
                + "a restart, and entering or exiting against it would act on inventory this run does not own",
                _plan.DocumentId,
                symbol,
                held,
                owned);
        }

        return false;
    }

    private decimal ResolvePrice(string symbol, IBacktestContext ctx) =>
        ctx.GetLastPrice(symbol)
        ?? (_windows.TryGetValue(symbol, out var window) ? window.LastPrice : 0m);

    private long ResolveQuantity(string symbol, IBacktestContext ctx)
    {
        var price = ResolvePrice(symbol, ctx);
        if (price <= 0m)
        {
            return 0L;
        }

        var trade = _plan.Trade;
        var magnitude = trade.SizingMethod switch
        {
            DesignerSizingMethod.FixedShares => trade.SizingValue,
            DesignerSizingMethod.FixedNotional => decimal.Floor(trade.SizingValue / price),
            DesignerSizingMethod.PercentAum => decimal.Floor(ctx.PortfolioValue * trade.SizingValue / price),
            _ => 0m
        };

        // Portfolio-derived sizing is unbounded input at runtime, so the conversion is guarded
        // rather than allowed to throw OverflowException inside a market-event callback.
        if (magnitude < 1m)
        {
            return 0L;
        }

        if (magnitude > long.MaxValue)
        {
            _logger?.LogWarning(
                "Designer document {DocumentId} computed an out-of-range quantity for {Symbol}; no order placed",
                _plan.DocumentId,
                symbol);
            return 0L;
        }

        var quantity = (long)magnitude;
        return trade.Side == DesignerTradeSide.Short ? -quantity : quantity;
    }

    private readonly record struct PendingOrder(
        Guid OrderId,
        long Remaining,
        DateTimeOffset PlacedAt,
        bool CancelRequested);
}
