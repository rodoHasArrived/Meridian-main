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
internal sealed class DesignerDocumentStrategy : IBacktestStrategy, ILiveOrderOutcomeObserver
{
    /// <summary>
    /// How long a submitted order may work before the strategy asks for it to be cancelled. This
    /// is the backstop for an order the gateway never resolves: a terminal outcome that does
    /// arrive releases the symbol through <see cref="OnOrderTerminated"/> well before the bound.
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
    private readonly bool _readsSpotFields;

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

        // A plan that also reads a spot field can change its answer between sessions, so coalescing
        // to the session boundary would hold a position open while its PRICE gate is already
        // failing. Only a purely session-window plan can safely wait for the session to roll.
        _readsSpotFields = plan.RequiredFields.Any(static field =>
            field.Equals(DesignerLiveFields.Price, StringComparison.OrdinalIgnoreCase)
            || field.Equals(DesignerLiveFields.PortfolioWeight, StringComparison.OrdinalIgnoreCase));
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
        Observe(trade.Symbol, trade.Price, DesignerSessionClock.SessionDate(trade.Timestamp), ctx);

    /// <inheritdoc/>
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx)
    {
        var mid = quote.BidPrice > 0m && quote.AskPrice > 0m
            ? (quote.BidPrice + quote.AskPrice) / 2m
            : 0m;
        if (mid > 0m)
        {
            Observe(quote.Symbol, mid, DesignerSessionClock.SessionDate(quote.Timestamp), ctx);
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
    /// <remarks>
    /// A terminal outcome is the confirmation the pending marker was waiting for. Until one
    /// arrives the symbol stays blocked, because an unresolved order could still fill and a
    /// replacement would double it; once the gateway says the order ended without completing,
    /// holding the block would strand the symbol for the life of the run over one transient
    /// rejection. Whatever quantity filled before the order ended is already in
    /// <c>_ownedQuantities</c> via <see cref="OnOrderFill"/>, so the next pass re-decides from a
    /// true position. No context is available here, so that pass is the next market event or
    /// day end rather than an immediate rebalance.
    /// </remarks>
    public void OnOrderTerminated(Guid orderId, LiveOrderOutcome outcome)
    {
        string? released = null;
        foreach (var (symbol, pending) in _pendingOrders)
        {
            if (pending.OrderId == orderId)
            {
                released = symbol;
                break;
            }
        }

        if (released is null)
        {
            return;
        }

        _pendingOrders.Remove(released);
        _logger?.LogInformation(
            "Designer document {DocumentId} released {Symbol}: order {OrderId} ended as {Outcome} without "
            + "completing, so the symbol is eligible again on the next pass",
            _plan.DocumentId,
            released,
            orderId,
            outcome);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A plan reading session windows deliberately does <em>not</em> decide here. The live session
    /// raises this callback on seeing the first event of a new date, before that event is
    /// dispatched, so the session that just ended is still the in-progress one every session
    /// metric excludes: any decision taken now is computed from the session before last. Worse, a
    /// cross-sectional plan could queue an order from those stale closes, and the alignment check
    /// on the following tick returns without unwinding it, so the order routes anyway. The roll
    /// itself is the trigger for these plans, and <see cref="Observe"/> reports it.
    /// Stale pending orders are still swept, because that is time-based rather than signal-based.
    /// </remarks>
    public void OnDayEnd(DateOnly date, IBacktestContext ctx)
    {
        if (_needsSessionFields)
        {
            CancelStalePendingOrders(ctx, ctx.CurrentTime);
            return;
        }

        Rebalance(ctx);
    }

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

        var sessionRolled = window!.Observe(price, sessionDate);

        // Re-decide when the session rolls, and on every event for any plan whose answer depends on
        // a spot field. Only a plan reading session windows alone can wait for the boundary.
        //
        // The roll is reported by the window rather than inferred from its size. A saturated window
        // dequeues as it enqueues, so a session-count comparison stops detecting rolls after
        // MaxWindow sessions -- which is the moment 63-day momentum first has enough history to be
        // usable. A session-only plan would then re-decide solely from OnDayEnd, which the live
        // session raises before dispatching the first event of the new date, while the close that
        // just ended is still the in-progress session the metrics exclude. It would trade one full
        // session stale, indefinitely.
        if (!_needsSessionFields || _readsSpotFields || sessionRolled)
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

        var eligible = new List<(string Symbol, int Order, decimal Score, IReadOnlyDictionary<string, decimal> Fields)>();
        var indeterminate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Seeded flags rather than "is the date null": a window with no completed session yet
        // reports null, and treating null as "not seeded" would let the next symbol seed instead,
        // silently skipping the comparison between the two.
        var sessionAlignment = (Seeded: false, Date: (DateOnly?)null);
        var spotAlignment = (Seeded: false, Date: (DateOnly?)null);

        for (var order = 0; order < _plan.Universe.Count; order++)
        {
            var symbol = _plan.Universe[order];
            if (!ctx.Universe.Contains(symbol))
            {
                continue;
            }

            // Inventory this run cannot account for is not tradeable either way. After a host
            // restart the ownership map is empty while the broker still holds the run's earlier
            // fills, so entering would double the position; the same shape also covers a position
            // opened by another strategy or by hand, which is not this run's to unwind.
            if (!IsAttributable(ctx, symbol) || !_windows.TryGetValue(symbol, out var window))
            {
                // A cross-sectional document ranks or bounds the whole universe against itself, so
                // deciding on a partial cross-section is not a smaller version of the promoted
                // strategy -- it is a different one.
                if (IsCrossSectional)
                {
                    return;
                }

                indeterminate.Add(symbol);
                continue;
            }

            // Every symbol in a ranked or bounded selection has to be describing the same trading
            // session. The first tick of a new date rolls one symbol's window before the others,
            // and comparing a fresh session against stale ones is a comparison the document never
            // asked for.
            if (IsCrossSectional && _needsSessionFields)
            {
                if (!sessionAlignment.Seeded)
                {
                    sessionAlignment = (true, window.LastCompletedSessionDate);
                }
                else if (sessionAlignment.Date != window.LastCompletedSessionDate)
                {
                    return;
                }
            }

            // The same requirement, one timeframe down, for a plan ranking or bounding on a spot
            // field. A window keeps its last observation indefinitely, so a symbol that stopped
            // trading yesterday would still enter today's cross-section at yesterday's price --
            // out-ranking a symbol quoted seconds ago and sizing a market order from a price the
            // book no longer shows. Candidates must at least have been seen in the same session.
            if (IsCrossSectional && _readsSpotFields)
            {
                if (!spotAlignment.Seeded)
                {
                    spotAlignment = (true, window.CurrentSessionDate);
                }
                else if (spotAlignment.Date != window.CurrentSessionDate)
                {
                    return;
                }
            }

            var fields = window.CreateFieldView(ctx, symbol);

            var outcome = Evaluate(_plan.EntryGates, fields, symbol);
            if (outcome == GateOutcome.Pass)
            {
                outcome = Evaluate(_plan.RiskGuards, fields, symbol);
            }

            if (outcome == GateOutcome.Indeterminate)
            {
                if (IsCrossSectional)
                {
                    return;
                }

                indeterminate.Add(symbol);
                continue;
            }

            if (outcome == GateOutcome.Fail)
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
                    // cross-section -- the same defect the indeterminate handling above prevents.
                    _logger?.LogWarning(
                        "Designer document {DocumentId} could not score {Symbol}; the cross-section is "
                        + "indeterminate this pass: {Reason}",
                        _plan.DocumentId,
                        symbol,
                        ex.Message);
                    return;
                }
            }

            eligible.Add((symbol, order, score, fields));
        }

        var targets = _plan.MinimumUniverseSize is { } minimum && eligible.Count < minimum
            ? Array.Empty<(string Symbol, int Order, decimal Score, IReadOnlyDictionary<string, decimal> Fields)>()
            : eligible
                // Declared universe order breaks ties. Without a rank cell every score is zero, so
                // an alphabetical tie-break would let a bounded selection trade a subset the
                // operator never chose -- "top 5" of a ten-name document becoming the first five
                // alphabetically rather than the first five declared.
                .OrderByDescending(static candidate => candidate.Score)
                .ThenBy(static candidate => candidate.Order)
                .Take(_plan.MaximumPositions ?? eligible.Count)
                .ToArray();

        var targetSet = new HashSet<string>(
            targets.Select(static candidate => candidate.Symbol),
            StringComparer.OrdinalIgnoreCase);

        CancelObsoletePendingOrders(ctx, targetSet, indeterminate);
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

            SubmitOrder(ctx, symbol, -owned, isEntry: false, "exiting: entry conditions no longer hold");
        }
    }

    private void OpenTargetPositions(
        IBacktestContext ctx,
        IReadOnlyList<(string Symbol, int Order, decimal Score, IReadOnlyDictionary<string, decimal> Fields)> targets)
    {
        // A bounded plan rotating names must not hold both sides of the rotation. An exit and its
        // replacement entry are enqueued in the same pass, and on a live gateway the entry can fill
        // while the exit is still working, so a pending exit does not yet free a slot.
        var occupied = _plan.Universe.Count(candidate =>
            (_ownedQuantities.TryGetValue(candidate, out var ownedQuantity) && ownedQuantity != 0L)
            || (_pendingOrders.TryGetValue(candidate, out var pendingOrder) && pendingOrder.IsEntry));

        foreach (var (symbol, _, _, fields) in targets)
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
                SubmitOrder(ctx, symbol, -owned, isEntry: false, "closing a position opposite the document's trade side");
                continue;
            }

            if (owned != 0L)
            {
                continue;
            }

            if (_plan.MaximumPositions is { } cap && occupied >= cap)
            {
                _logger?.LogInformation(
                    "Designer document {DocumentId} is holding {Symbol} back: {Occupied} of {Cap} position slot(s) "
                    + "are held or pending, and an exit still working does not free one",
                    _plan.DocumentId,
                    symbol,
                    occupied,
                    cap);
                continue;
            }

            var quantity = ResolveQuantity(symbol, ctx);
            if (quantity == 0L || !RiskGuardsAllowEntry(symbol, quantity, fields, ctx))
            {
                continue;
            }

            occupied++;
            SubmitOrder(ctx, symbol, quantity, isEntry: true, $"entering via trade cell {_plan.Trade.CellId}");
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

        if (Evaluate(_plan.RiskGuards, projected, symbol) == GateOutcome.Pass)
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

    private void SubmitOrder(IBacktestContext ctx, string symbol, long quantity, bool isEntry, string reason)
    {
        if (quantity == 0L)
        {
            return;
        }

        var orderId = ctx.PlaceMarketOrder(symbol, quantity);
        _pendingOrders[symbol] = new PendingOrder(orderId, quantity, ctx.CurrentTime, isEntry, CancelRequested: false);
        _logger?.LogInformation(
            "Designer document {DocumentId} {Reason} for {Symbol} quantity {Quantity}",
            _plan.DocumentId,
            reason,
            symbol,
            quantity);
    }

    /// <summary>
    /// Cancels a working order whose intent this pass no longer wants.
    /// </summary>
    /// <remarks>
    /// An order parked for approval, or otherwise slow to fill, can still complete minutes after
    /// the strategy stopped wanting it — an entry filling into a symbol the gates have since
    /// rejected, or an exit completing after the symbol became eligible again. Waiting for the
    /// staleness timeout would leave that window open for its full duration. The marker is kept
    /// after cancelling, because a cancel request is not an outcome: the order can still fill in
    /// the race, and submitting a replacement before the gateway resolves it could double-fill.
    /// <see cref="OnOrderTerminated"/> releases the symbol once the outcome is known.
    /// </remarks>
    private void CancelObsoletePendingOrders(
        IBacktestContext ctx,
        IReadOnlySet<string> targetSet,
        IReadOnlySet<string> indeterminate)
    {
        foreach (var symbol in _pendingOrders.Keys.ToArray())
        {
            var pending = _pendingOrders[symbol];
            if (pending.CancelRequested || indeterminate.Contains(symbol))
            {
                continue;
            }

            var wanted = pending.IsEntry ? targetSet.Contains(symbol) : !targetSet.Contains(symbol);
            if (wanted)
            {
                continue;
            }

            CancelPending(ctx, symbol, pending, pending.IsEntry
                ? "the symbol is no longer a target"
                : "the symbol is a target again");
        }
    }

    /// <summary>
    /// Cancels an order that has been working past <see cref="PendingOrderTimeout"/> without
    /// completing, and leaves the symbol blocked.
    /// </summary>
    /// <remarks>
    /// The marker is deliberately <em>not</em> cleared here. Freeing the symbol at the moment the
    /// cancel is requested would let the next rebalance submit a replacement while the original is
    /// still live at the broker, and both could fill — doubling an entry or overselling an exit.
    /// The release comes from <see cref="OnOrderTerminated"/> instead, once the gateway confirms
    /// the order ended. A symbol whose order is never resolved either way stays blocked, and is
    /// logged at warning level rather than silently retried.
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


            CancelPending(
                ctx,
                symbol,
                pending,
                $"it has been working for over {PendingOrderTimeout.TotalMinutes} minutes without a completing fill");
        }
    }

    private void CancelPending(IBacktestContext ctx, string symbol, PendingOrder pending, string reason)
    {
        _pendingOrders[symbol] = pending with { CancelRequested = true };
        try
        {
            ctx.CancelOrder(pending.OrderId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Designer document {DocumentId} could not cancel order {OrderId} for {Symbol}",
                _plan.DocumentId,
                pending.OrderId,
                symbol);
        }

        _logger?.LogWarning(
            "Designer document {DocumentId} cancelled order {OrderId} for {Symbol} because {Reason}. {Symbol} stays "
            + "blocked until the order's terminal outcome arrives, because a cancel request is not an outcome and "
            + "submitting a replacement first risks a double fill",
            _plan.DocumentId,
            pending.OrderId,
            symbol,
            reason,
            symbol);
    }

    /// <summary>
    /// Evaluates a gate set. An evaluation fault is <see cref="GateOutcome.Indeterminate"/>, not a
    /// failure: a filter such as <c>PRICE / (PRICE - 50) &gt; 1</c> divides by zero exactly when
    /// price reaches 50, and reading that as "the gate did not pass" would liquidate a held
    /// position on an arithmetic accident rather than on a signal. A cold field arrives here the
    /// same way, because the field view resolves on access.
    /// </summary>
    private GateOutcome Evaluate(
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
                    return GateOutcome.Fail;
                }
            }
            catch (DesignerExpressionException ex)
            {
                _logger?.LogWarning(
                    "Designer document {DocumentId} could not evaluate gate {GateCellId} for {Symbol}; the symbol "
                    + "is indeterminate this pass: {Reason}",
                    _plan.DocumentId,
                    gate.CellId,
                    symbol,
                    ex.Message);
                return GateOutcome.Indeterminate;
            }
        }

        return GateOutcome.Pass;
    }

    private enum GateOutcome
    {
        Pass,
        Fail,
        Indeterminate
    }

    private bool IsAttributable(IBacktestContext ctx, string symbol)
    {
        // The unrounded size is what matters here: a 0.9-share holding belonging to someone else
        // rounds to zero in Position.Quantity and would look like no position at all.
        var held = ctx.Positions.TryGetValue(symbol, out var position) ? position.ExactQuantity : 0m;
        if (held == 0m)
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

    /// <summary>
    /// Price used for sizing and for the projected-weight risk check.
    /// </summary>
    /// <remarks>
    /// The window's own last observation is preferred over
    /// <see cref="IBacktestContext.GetLastPrice"/>: the live context returns the cached trade
    /// whenever one exists and does not compare it against a newer quote, so a quote-driven entry
    /// could gate at the current midpoint and then size off a stale trade price. The window holds
    /// whatever this strategy most recently saw, which is the price the gates just used.
    /// </remarks>
    private decimal ResolvePrice(string symbol, IBacktestContext ctx)
    {
        if (_windows.TryGetValue(symbol, out var window) && window.LastPrice > 0m)
        {
            return window.LastPrice;
        }

        return ctx.GetLastPrice(symbol) ?? 0m;
    }

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
        bool IsEntry,
        bool CancelRequested);
}
