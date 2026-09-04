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
/// The plan is fully resolved before construction, so this class makes no eligibility decision the
/// document did not state. Cross-sectional selection (rank, minSize, maxSize) re-runs whenever an
/// observation arrives and at each day close; a symbol whose window is not yet warm is not
/// eligible and is also not force-exited, because "no data yet" is not an exit signal.
/// </remarks>
internal sealed class DesignerDocumentStrategy : IBacktestStrategy
{
    private readonly DesignerStrategyPlan _plan;
    private readonly ILogger? _logger;
    private readonly Dictionary<string, DesignerLiveFields.SymbolWindow> _windows =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingOrders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _planUniverse;

    public DesignerDocumentStrategy(DesignerStrategyPlan plan, ILogger? logger = null)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _logger = logger;
        _planUniverse = new HashSet<string>(plan.Universe, StringComparer.OrdinalIgnoreCase);
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
        // Trades carry no session volume, so a document reading VOLUME_AVG_20D stays cold until
        // bars arrive rather than averaging in zeroes.
        Observe(trade.Symbol, trade.Price, volume: null, ctx);

    /// <inheritdoc/>
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx)
    {
        var mid = quote.BidPrice > 0m && quote.AskPrice > 0m
            ? (quote.BidPrice + quote.AskPrice) / 2m
            : 0m;
        if (mid > 0m)
        {
            Observe(quote.Symbol, mid, volume: null, ctx);
        }
    }

    /// <inheritdoc/>
    public void OnBar(HistoricalBar bar, IBacktestContext ctx) =>
        Observe(bar.Symbol, bar.Close, bar.Volume, ctx);

    /// <inheritdoc/>
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx)
    {
        // Level-2 depth carries no field the designer catalog exposes.
    }

    /// <inheritdoc/>
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) => _pendingOrders.Remove(fill.Symbol);

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

    private void Observe(string symbol, decimal price, long? volume, IBacktestContext ctx)
    {
        if (string.IsNullOrWhiteSpace(symbol) || price <= 0m || !_planUniverse.Contains(symbol))
        {
            return;
        }

        if (!_windows.TryGetValue(symbol, out var window))
        {
            window = new DesignerLiveFields.SymbolWindow();
            _windows[symbol] = window;
        }

        window.Observe(price, volume);
        Rebalance(ctx);
    }

    private void Rebalance(IBacktestContext ctx)
    {
        var eligible = new List<(string Symbol, decimal Score)>();

        foreach (var symbol in _plan.Universe)
        {
            if (!ctx.Universe.Contains(symbol))
            {
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
                    var value = _plan.RankExpression.Evaluate(fields);
                    if (!value.IsNumber)
                    {
                        continue;
                    }

                    score = value.Number;
                }
                catch (DesignerExpressionException)
                {
                    continue;
                }
            }

            eligible.Add((symbol, score));
        }

        // minSize is the document's statement that the strategy is only meaningful across a
        // breadth of names. Trading a thinner set would be a different strategy than the one
        // promoted, so nothing is entered until the breadth exists.
        var targets = _plan.MinimumUniverseSize is { } minimum && eligible.Count < minimum
            ? Array.Empty<string>()
            : eligible
                .OrderByDescending(static candidate => candidate.Score)
                .ThenBy(static candidate => candidate.Symbol, StringComparer.OrdinalIgnoreCase)
                .Take(_plan.MaximumPositions ?? eligible.Count)
                .Select(static candidate => candidate.Symbol)
                .ToArray();

        var targetSet = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in _plan.Universe)
        {
            var held = ctx.Positions.TryGetValue(symbol, out var position) ? position.Quantity : 0L;
            if (held == 0L || targetSet.Contains(symbol) || _pendingOrders.Contains(symbol))
            {
                continue;
            }

            _pendingOrders.Add(symbol);
            ctx.PlaceMarketOrder(symbol, -held);
            _logger?.LogInformation(
                "Designer document {DocumentId} exiting {Symbol}: entry conditions no longer hold",
                _plan.DocumentId,
                symbol);
        }

        foreach (var symbol in targets)
        {
            var held = ctx.Positions.TryGetValue(symbol, out var position) ? position.Quantity : 0L;
            if (held != 0L || _pendingOrders.Contains(symbol))
            {
                continue;
            }

            var quantity = ResolveQuantity(symbol, targets.Length, ctx);
            if (quantity == 0L)
            {
                continue;
            }

            _pendingOrders.Add(symbol);
            ctx.PlaceMarketOrder(symbol, quantity);
            _logger?.LogInformation(
                "Designer document {DocumentId} entering {Symbol} quantity {Quantity} via trade cell {TradeCellId}",
                _plan.DocumentId,
                symbol,
                quantity,
                _plan.Trade.CellId);
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

    private long ResolveQuantity(string symbol, int targetCount, IBacktestContext ctx)
    {
        var price = ctx.GetLastPrice(symbol)
            ?? (_windows.TryGetValue(symbol, out var window) ? window.LastPrice : 0m);
        if (price <= 0m)
        {
            return 0L;
        }

        var trade = _plan.Trade;
        var magnitude = trade.SizingMethod switch
        {
            DesignerSizingMethod.FixedShares => decimal.Floor(trade.SizingValue),
            DesignerSizingMethod.FixedNotional => decimal.Floor(trade.SizingValue / price),
            DesignerSizingMethod.PercentAum => decimal.Floor(ctx.PortfolioValue * trade.SizingValue / price),
            DesignerSizingMethod.EqualWeight => targetCount <= 0
                ? 0m
                : decimal.Floor(ctx.PortfolioValue / targetCount / price),
            _ => 0m
        };

        if (magnitude < 1m)
        {
            return 0L;
        }

        var quantity = (long)magnitude;
        return trade.Side == DesignerTradeSide.Short ? -quantity : quantity;
    }
}
