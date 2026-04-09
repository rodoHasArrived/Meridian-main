using System;
using System.Collections.Generic;
using System.Linq;

namespace Meridian.Backtesting.Sdk.Strategies.AdvancedCarry;

/// <summary>
/// A carry-trade backtest strategy that uses <see cref="AdvancedCarryDecisionEngine"/> to optimise
/// portfolio weights each day.  Historical daily returns are accumulated on a rolling
/// <see cref="RollingWindowDays"/>-day window and used to estimate per-symbol volatility and
/// expected return.  The engine then determines target weights, generates rebalance instructions,
/// and places the required market orders through the backtest context.
/// </summary>
public sealed class CarryTradeBacktestStrategy : IBacktestStrategy
{
    private const int RollingWindowDays = 63; // ~3 months of trading days
    private const double DefaultCarryYield = 0.04;  // 4% assumed carry yield when unknown
    private const double DefaultDuration = 1.0;     // 1-year duration for equity proxies
    private const double DefaultBidAskBps = 10.0;   // 10 bps assumed spread
    private const double DefaultAdv = 1_000_000.0;  // $1M assumed average daily volume

    private readonly AdvancedCarryDecisionEngine _engine;
    private readonly AdvancedCarryConfiguration _configuration;
    private readonly int _rebalanceFrequencyDays;

    // State tracked across events
    private readonly Dictionary<string, Queue<double>> _returnsHistory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, decimal> _prevDayClose = new(StringComparer.OrdinalIgnoreCase);
    private int _daysSinceRebalance;

    /// <inheritdoc />
    public string Name => "Advanced Carry Trade";

    /// <summary>
    /// Creates a new carry trade strategy.
    /// </summary>
    /// <param name="configuration">Carry configuration including optimisation method and risk options.</param>
    /// <param name="rebalanceFrequencyDays">How many trading days to wait between rebalances (default: 5 = weekly).</param>
    /// <param name="forecastOverlay">Optional forecast overlay applied before each decision.</param>
    public CarryTradeBacktestStrategy(
        AdvancedCarryConfiguration? configuration = null,
        int rebalanceFrequencyDays = 5,
        ICarryForecastOverlay? forecastOverlay = null)
    {
        _configuration = configuration ?? new AdvancedCarryConfiguration();
        _rebalanceFrequencyDays = Math.Max(1, rebalanceFrequencyDays);
        _engine = new AdvancedCarryDecisionEngine(forecastOverlay);
    }

    /// <inheritdoc />
    public void Initialize(IBacktestContext ctx)
    {
        _returnsHistory.Clear();
        _prevDayClose.Clear();
        _daysSinceRebalance = _rebalanceFrequencyDays; // trigger rebalance on first available day
    }

    /// <inheritdoc />
    public void OnBar(HistoricalBar bar, IBacktestContext ctx)
    {
        // Track closing prices for daily return estimation.
        if (!_prevDayClose.TryGetValue(bar.Symbol, out var prev))
        {
            _prevDayClose[bar.Symbol] = bar.Close;
            return;
        }

        if (prev > 0m)
        {
            var dailyReturn = (double)((bar.Close - prev) / prev);
            if (!_returnsHistory.TryGetValue(bar.Symbol, out var queue))
            {
                queue = new Queue<double>(RollingWindowDays + 1);
                _returnsHistory[bar.Symbol] = queue;
            }
            queue.Enqueue(dailyReturn);
            while (queue.Count > RollingWindowDays)
                queue.Dequeue();
        }

        _prevDayClose[bar.Symbol] = bar.Close;
    }

    /// <inheritdoc />
    public void OnTrade(Trade trade, IBacktestContext ctx) { }

    /// <inheritdoc />
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }

    /// <inheritdoc />
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }

    /// <inheritdoc />
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }

    /// <inheritdoc />
    public void OnDayEnd(DateOnly date, IBacktestContext ctx)
    {
        _daysSinceRebalance++;
        if (_daysSinceRebalance < _rebalanceFrequencyDays)
            return;

        // Build snapshots only for symbols that have at least a price and some return history.
        var snapshots = BuildSnapshots(ctx);
        if (snapshots.Count < 2)
            return; // Need at least 2 assets for diversified carry optimisation.

        var portfolio = new CarryPortfolioState(
            ctx.PortfolioValue,
            ctx.Positions.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Quantity,
                StringComparer.OrdinalIgnoreCase));

        AdvancedCarryDecision decision;
        try
        {
            var input = new AdvancedCarryInput(snapshots, portfolio, AsOf: new DateTimeOffset(date, TimeOnly.MinValue, TimeSpan.Zero));
            decision = _engine.BuildDecision(input, _configuration);
        }
        catch (Exception)
        {
            // Engine can throw when constraints are infeasible; skip this rebalance.
            return;
        }

        // Execute the rebalance instructions as market orders.
        foreach (var instruction in decision.RebalanceInstructions)
        {
            if (instruction.DeltaQuantity == 0)
                continue;

            ctx.PlaceMarketOrder(instruction.Symbol, instruction.DeltaQuantity);
        }

        _daysSinceRebalance = 0;
    }

    /// <inheritdoc />
    public void OnFinished(IBacktestContext ctx) { }

    // ── Private helpers ─────────────────────────────────────────────────────

    private List<CarryAssetSnapshot> BuildSnapshots(IBacktestContext ctx)
    {
        var snapshots = new List<CarryAssetSnapshot>();

        foreach (var symbol in ctx.Universe)
        {
            var price = ctx.GetLastPrice(symbol);
            if (price is null || price <= 0m)
                continue;

            _returnsHistory.TryGetValue(symbol, out var returnsQueue);
            var returns = returnsQueue is { Count: >= 5 }
                ? returnsQueue.ToArray()
                : Array.Empty<double>();

            var (annualVol, annualReturn) = EstimateVolatilityAndReturn(returns);

            snapshots.Add(new CarryAssetSnapshot(
                Symbol: symbol,
                LastPrice: price.Value,
                ExpectedAnnualReturn: annualReturn,
                AnnualCarryYield: DefaultCarryYield,
                AnnualPriceReturn: annualReturn - DefaultCarryYield,
                AnnualVolatility: annualVol,
                DurationYears: DefaultDuration,
                SpreadDurationYears: DefaultDuration,
                AverageDailyVolume: DefaultAdv,
                BidAskSpreadBps: DefaultBidAskBps,
                HistoricalDailyReturns: returns.Length > 0 ? returns : null));
        }

        return snapshots;
    }

    private static (double annualVol, double annualReturn) EstimateVolatilityAndReturn(double[] returns)
    {
        if (returns.Length < 2)
            return (0.20, 0.08); // fallback: 20% vol, 8% expected return

        var mean = returns.Average();
        var variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Length - 1);
        var annualVol = Math.Sqrt(variance * 252.0);
        var annualReturn = mean * 252.0;

        // Clamp to reasonable bounds
        annualVol = Math.Max(0.01, Math.Min(annualVol, 2.0));
        annualReturn = Math.Max(-0.50, Math.Min(annualReturn, 2.0));

        return (annualVol, annualReturn);
    }
}
