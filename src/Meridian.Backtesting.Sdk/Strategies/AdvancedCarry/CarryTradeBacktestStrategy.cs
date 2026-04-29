using System;
using System.Collections.Generic;
using System.Linq;

namespace Meridian.Backtesting.Sdk.Strategies.AdvancedCarry;

/// <summary>
/// A carry-trade backtest strategy that supports three carry signal modes:
/// <list type="bullet">
///   <item><see cref="YieldCarryMode.ClassicCarry"/> — price-momentum carry (original behaviour).</item>
///   <item><see cref="YieldCarryMode.YieldSpread"/> — long assets whose yield exceeds the risk-free rate;
///         underweight or exclude low-spread assets.  The carry signal is entirely driven by
///         <c>assetYield − riskFreeRate</c>.</item>
///   <item><see cref="YieldCarryMode.YieldRotation"/> — ranks assets by current yield each period and
///         rotates into the top-50 % yielders.  Applies an additional momentum tilt when an
///         asset's implied yield has risen vs its 20-day average.</item>
/// </list>
/// Per-symbol yields can be provided explicitly (dividend yield, bond coupon rate, FX carry rate).
/// When not provided the strategy estimates a proxy yield from rolling price history.
/// </summary>
public sealed class CarryTradeBacktestStrategy : IBacktestStrategy
{
    private const int RollingWindowDays = 63;     // ~3 months
    private const int YieldMaWindowDays = 20;     // MA for rotation signal
    private const double FallbackVol = 0.20;
    private const double FallbackReturn = 0.08;
    private const double DefaultAdv = 1_000_000.0;
    private const double DefaultBidAskBps = 10.0;
    private const double DefaultProxyYield = 0.03;
    private const double YieldProxyMultiplier = 0.40;   // fraction of ann. return used as proxy yield
    private const double MaxProxyYield = 0.20;   // cap proxy yield at 20 %
    private const double YieldMaThreshold = 0.001;  // 10 bps: min rise above MA to trigger tilt
    private const double YieldMomentumMultiplier = 2.0;    // scale factor for yield-rise tilt
    private const double MaxYieldMomentumContrib = 0.05;   // cap tilt contribution at 5 %

    private readonly AdvancedCarryDecisionEngine _engine;
    private readonly AdvancedCarryConfiguration _configuration;
    private readonly YieldCarryMode _yieldCarryMode;
    private readonly int _rebalanceFrequencyDays;
    private readonly double _minYieldSpreadToLong;
    private readonly IReadOnlyDictionary<string, double> _explicitYields;

    private readonly Dictionary<string, Queue<double>> _returnsHistory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Queue<double>> _yieldHistory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, decimal> _prevDayClose = new(StringComparer.OrdinalIgnoreCase);
    private int _daysSinceRebalance;

    /// <inheritdoc />
    public string Name => _yieldCarryMode switch
    {
        YieldCarryMode.YieldSpread => "Yield-Spread Carry Trade",
        YieldCarryMode.YieldRotation => "Yield-Rotation Carry Trade",
        _ => "Advanced Carry Trade"
    };

    /// <summary>Creates a new carry trade strategy.</summary>
    /// <param name="configuration">Optimisation and risk parameters.</param>
    /// <param name="yieldCarryMode">Carry signal mode.</param>
    /// <param name="rebalanceFrequencyDays">Trading days between rebalances (default 5 = weekly).</param>
    /// <param name="explicitYields">
    ///   Optional per-symbol annual yield overrides (e.g. 0.035 for 3.5 % dividend yield,
    ///   0.045 for a bond coupon).  Symbol keys are case-insensitive.
    /// </param>
    /// <param name="minYieldSpreadToLong">
    ///   Minimum net yield spread (assetYield − riskFreeRate) required to include an asset.
    ///   Assets below this threshold are excluded in YieldSpread mode.  Default 0.
    /// </param>
    /// <param name="forecastOverlay">Optional carry forecast overlay.</param>
    public CarryTradeBacktestStrategy(
        AdvancedCarryConfiguration? configuration = null,
        YieldCarryMode yieldCarryMode = YieldCarryMode.YieldSpread,
        int rebalanceFrequencyDays = 5,
        IReadOnlyDictionary<string, double>? explicitYields = null,
        double minYieldSpreadToLong = 0.0,
        ICarryForecastOverlay? forecastOverlay = null)
    {
        _configuration = configuration ?? new AdvancedCarryConfiguration();
        _yieldCarryMode = yieldCarryMode;
        _rebalanceFrequencyDays = Math.Max(1, rebalanceFrequencyDays);
        _explicitYields = explicitYields
                                  ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        _minYieldSpreadToLong = minYieldSpreadToLong;
        _engine = new AdvancedCarryDecisionEngine(forecastOverlay);
    }

    /// <inheritdoc />
    public void Initialize(IBacktestContext ctx)
    {
        _returnsHistory.Clear();
        _yieldHistory.Clear();
        _prevDayClose.Clear();
        _daysSinceRebalance = _rebalanceFrequencyDays; // trigger rebalance immediately
    }

    /// <inheritdoc />
    public void OnBar(HistoricalBar bar, IBacktestContext ctx)
    {
        if (!_prevDayClose.TryGetValue(bar.Symbol, out var prev))
        {
            _prevDayClose[bar.Symbol] = bar.Close;
            return;
        }

        if (prev > 0m)
        {
            var dailyReturn = (double)((bar.Close - prev) / prev);

            // Rolling return history
            if (!_returnsHistory.TryGetValue(bar.Symbol, out var rq))
            {
                rq = new Queue<double>(RollingWindowDays + 1);
                _returnsHistory[bar.Symbol] = rq;
            }
            rq.Enqueue(dailyReturn);
            while (rq.Count > RollingWindowDays)
                rq.Dequeue();

            // Rolling implied-yield history (used by YieldRotation MA signal)
            var impliedYield = ComputeImpliedYield(bar.Symbol);
            if (!_yieldHistory.TryGetValue(bar.Symbol, out var yq))
            {
                yq = new Queue<double>(YieldMaWindowDays + 1);
                _yieldHistory[bar.Symbol] = yq;
            }
            yq.Enqueue(impliedYield);
            while (yq.Count > YieldMaWindowDays)
                yq.Dequeue();
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

        var snapshots = BuildSnapshots(ctx);
        if (snapshots.Count == 0)
            return;

        // Apply yield-based filtering / ranking for non-classic modes
        if (_yieldCarryMode != YieldCarryMode.ClassicCarry)
            snapshots = FilterByYield(snapshots);

        if (snapshots.Count < 1)
            return;

        var portfolio = new CarryPortfolioState(
            ctx.PortfolioValue,
            ctx.Positions.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Quantity,
                StringComparer.OrdinalIgnoreCase));

        AdvancedCarryDecision decision;
        try
        {
            var input = new AdvancedCarryInput(
                snapshots,
                portfolio,
                AsOf: new DateTimeOffset(date, TimeOnly.MinValue, TimeSpan.Zero));
            decision = _engine.BuildDecision(input, _configuration);
        }
        catch (Exception)
        {
            return; // infeasible constraints — skip rebalance
        }

        foreach (var instruction in decision.RebalanceInstructions)
        {
            if (instruction.DeltaQuantity != 0)
                ctx.PlaceMarketOrder(instruction.Symbol, instruction.DeltaQuantity);
        }

        _daysSinceRebalance = 0;
    }

    /// <inheritdoc />
    public void OnFinished(IBacktestContext ctx) { }

    // ── Snapshot construction ────────────────────────────────────────────────

    private List<CarryAssetSnapshot> BuildSnapshots(IBacktestContext ctx)
    {
        var result = new List<CarryAssetSnapshot>();
        foreach (var symbol in ctx.Universe)
        {
            var price = ctx.GetLastPrice(symbol);
            if (price is null || price <= 0m)
                continue;

            _returnsHistory.TryGetValue(symbol, out var rq);
            var returns = rq is { Count: >= 5 } ? rq.ToArray() : Array.Empty<double>();
            var (annVol, priceReturn) = EstimateVolAndReturn(returns);

            var (carryYield, expectedReturn) = _yieldCarryMode switch
            {
                YieldCarryMode.YieldSpread => SignalYieldSpread(symbol, priceReturn),
                YieldCarryMode.YieldRotation => SignalYieldRotation(symbol, priceReturn),
                _ => SignalClassicCarry(symbol, priceReturn)
            };

            result.Add(new CarryAssetSnapshot(
                Symbol: symbol,
                LastPrice: price.Value,
                ExpectedAnnualReturn: expectedReturn,
                AnnualCarryYield: carryYield,
                AnnualPriceReturn: priceReturn,
                AnnualVolatility: annVol,
                DurationYears: 1.0,
                SpreadDurationYears: 1.0,
                AverageDailyVolume: DefaultAdv,
                BidAskSpreadBps: DefaultBidAskBps,
                HistoricalDailyReturns: returns.Length > 0 ? returns : null));
        }
        return result;
    }

    // ── Carry signal factories ───────────────────────────────────────────────

    /// <summary>
    /// Yield-spread: carry signal = assetYield − riskFreeRate.
    /// Expected return = netSpread + 30 % of price return.
    /// </summary>
    private (double carryYield, double expectedReturn) SignalYieldSpread(
        string symbol, double priceReturn)
    {
        var yield = GetAssetYield(symbol);
        var netSpread = yield - _configuration.RiskFreeRate;
        var expected = netSpread + 0.30 * priceReturn;
        return (yield, Clamp(expected));
    }

    /// <summary>
    /// Yield-rotation: yield spread + a momentum tilt when current implied yield has risen
    /// above its 20-day moving average (asset has cheapened — good entry signal).
    /// </summary>
    private (double carryYield, double expectedReturn) SignalYieldRotation(
        string symbol, double priceReturn)
    {
        var yield = GetAssetYield(symbol);
        var netSpread = yield - _configuration.RiskFreeRate;

        var yieldMomentum = 0.0;
        if (_yieldHistory.TryGetValue(symbol, out var yq) && yq.Count >= 5)
        {
            var yieldMa = yq.Average();
            if (yield > yieldMa + 0.001)                       // at least 10 bps above MA
                yieldMomentum = Math.Min((yield - yieldMa) * 2.0, 0.05); // cap at 5 %
        }

        var expected = netSpread + yieldMomentum + 0.20 * priceReturn;
        return (yield, Clamp(expected));
    }

    /// <summary>Classic: price momentum + carry yield.</summary>
    private (double carryYield, double expectedReturn) SignalClassicCarry(
        string symbol, double priceReturn)
    {
        var yield = GetAssetYield(symbol);
        var expected = priceReturn + yield;
        return (yield, Clamp(expected));
    }

    // ── Yield filtering / ranking ────────────────────────────────────────────

    private List<CarryAssetSnapshot> FilterByYield(List<CarryAssetSnapshot> snapshots)
    {
        if (_yieldCarryMode == YieldCarryMode.YieldRotation)
        {
            // Keep the top 50 % by yield (minimum 1)
            var sorted = snapshots.OrderByDescending(s => s.AnnualCarryYield).ToList();
            var keepCount = Math.Max(1, (int)Math.Ceiling(sorted.Count * 0.5));
            return sorted.Take(keepCount).ToList();
        }

        // YieldSpread: exclude assets below minimum spread threshold
        return snapshots
            .Where(s => s.AnnualCarryYield - _configuration.RiskFreeRate >= _minYieldSpreadToLong)
            .ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns annual yield for a symbol.  Uses explicit override when provided, otherwise
    /// derives a proxy yield from rolling returns (40 % of trailing annualised return,
    /// floored at 0 %, capped at 20 %).
    /// </summary>
    private double GetAssetYield(string symbol)
    {
        if (_explicitYields.TryGetValue(symbol, out var y))
            return Math.Max(0.0, y);

        if (_returnsHistory.TryGetValue(symbol, out var rq) && rq.Count >= 10)
        {
            var annReturn = rq.Average() * 252.0;
            return Math.Max(0.0, Math.Min(annReturn * 0.40, 0.20));
        }

        return DefaultProxyYield;
    }

    /// <summary>Derives the current implied yield using available history.</summary>
    private double ComputeImpliedYield(string symbol)
    {
        if (_explicitYields.TryGetValue(symbol, out var y))
            return y;
        if (_returnsHistory.TryGetValue(symbol, out var rq) && rq.Count >= 5)
            return Math.Max(0.0, Math.Min(rq.Average() * 252.0 * 0.40, 0.20));
        return DefaultProxyYield;
    }

    private static (double annVol, double annReturn) EstimateVolAndReturn(double[] returns)
    {
        if (returns.Length < 2)
            return (FallbackVol, FallbackReturn);

        var mean = returns.Average();
        var variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Length - 1);
        var vol = Math.Max(0.01, Math.Min(Math.Sqrt(variance * 252.0), 2.0));
        var ret = Math.Max(-0.50, Math.Min(mean * 252.0, 2.0));
        return (vol, ret);
    }

    private static double Clamp(double v) => Math.Max(-1.0, Math.Min(v, 2.0));
}
