using Meridian.Backtesting.Sdk;
using Meridian.Ledger;

namespace Meridian.Strategies.Live;

/// <summary>
/// Accumulates equity observations, fills, and cash flows for a live/paper session and
/// produces the canonical <see cref="BacktestResult"/> summary that the promotion workflow
/// evaluates. Metrics that require a full closed-trade ledger (win rate, profit factor,
/// XIRR) are reported as unavailable rather than fabricated; the result is marked
/// <see cref="BacktestResultKinds.SummaryOnly"/> with explicit coverage warnings.
/// </summary>
internal sealed class LiveRunMetricsTracker
{
    private readonly decimal _initialEquity;
    private readonly DateTimeOffset _startedAt;
    private readonly List<PortfolioSnapshot> _snapshots = [];
    private readonly List<CashFlowEntry> _cashFlows = [];
    private readonly List<FillEvent> _fills = [];
    private readonly List<CashFlowEntry> _currentDayCashFlows = [];

    private decimal _previousEquity;
    private decimal _peakEquity;
    private decimal _maxDrawdown;
    /// <summary>Fraction of peak equity (0.15 = a 15% drawdown), matching <see cref="BacktestMetrics.MaxDrawdownPercent"/>.</summary>
    private decimal _maxDrawdownFraction;
    private decimal _totalCommissions;

    public LiveRunMetricsTracker(decimal initialEquity, DateTimeOffset startedAt)
    {
        _initialEquity = initialEquity;
        _startedAt = startedAt;
        _previousEquity = initialEquity;
        _peakEquity = initialEquity;
    }

    public long EventsProcessed { get; set; }

    public void RecordFill(
        FillEvent fill,
        DateTimeOffset timestamp,
        bool usesFaceValuePercentageOfPar = false,
        decimal contractMultiplier = 1m)
    {
        _fills.Add(fill);

        // A face-value fill routes its quantity as face value and quotes its price as a
        // percentage of par (fixed income), so the booked cash movement scales the price to a
        // fraction of par before multiplying — the same convention TradeExecutedEvent.GrossValue
        // and the pre-trade risk gate use. Multiplying the two raw books 100,000 face at 101.25
        // as a $10,125,000 cash flow instead of $101,250. Quantity and Price stay as quoted;
        // Amount alone carries the economic cash impact, matching SimulatedPortfolio.
        // Otherwise the quoted price is scaled by the contract multiplier, the same money terms
        // the paper book uses: one option contract at 2.50 with multiplier 100 is a $250 outflow.
        var effectivePrice = usesFaceValuePercentageOfPar
            ? fill.FillPrice / 100m
            : fill.FillPrice * (contractMultiplier > 0m ? contractMultiplier : 1m);
        var tradeCashFlow = new TradeCashFlow(
            Timestamp: timestamp,
            Amount: -(fill.FilledQuantity * effectivePrice),
            Symbol: fill.Symbol,
            Quantity: fill.FilledQuantity,
            Price: fill.FillPrice,
            AccountId: fill.AccountId);
        _cashFlows.Add(tradeCashFlow);
        _currentDayCashFlows.Add(tradeCashFlow);

        if (fill.Commission != 0m)
        {
            _totalCommissions += Math.Abs(fill.Commission);
            var commissionCashFlow = new CommissionCashFlow(
                Timestamp: timestamp,
                Amount: -Math.Abs(fill.Commission),
                Symbol: fill.Symbol,
                OrderId: fill.OrderId,
                AccountId: fill.AccountId);
            _cashFlows.Add(commissionCashFlow);
            _currentDayCashFlows.Add(commissionCashFlow);
        }
    }

    public void RecordDayEnd(
        DateOnly date,
        decimal cash,
        decimal equity,
        IReadOnlyDictionary<string, Position> positions,
        IReadOnlyDictionary<string, FinancialAccountSnapshot> accounts)
    {
        var dailyReturn = _previousEquity > 0m ? (equity - _previousEquity) / _previousEquity : 0m;

        if (equity > _peakEquity)
        {
            _peakEquity = equity;
        }

        var drawdown = _peakEquity - equity;
        // Currency and percentage losses can reach their maxima in different episodes.
        // A later $100 decline from $1,000 must not replace an earlier 50% decline from $100.
        _maxDrawdown = Math.Max(_maxDrawdown, drawdown);
        var drawdownFraction = _peakEquity > 0m ? drawdown / _peakEquity : 0m;
        _maxDrawdownFraction = Math.Max(_maxDrawdownFraction, drawdownFraction);

        _snapshots.Add(new PortfolioSnapshot(
            Timestamp: new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero),
            Date: date,
            Cash: cash,
            MarginBalance: 0m,
            LongMarketValue: Math.Max(0m, equity - cash),
            ShortMarketValue: Math.Min(0m, equity - cash),
            TotalEquity: equity,
            DailyReturn: dailyReturn,
            Positions: positions,
            Accounts: accounts,
            DayCashFlows: _currentDayCashFlows.ToArray()));
        _currentDayCashFlows.Clear();
        _previousEquity = equity;
    }

    public BacktestResult Build(
        string engineId,
        IReadOnlySet<string> universe,
        decimal finalEquity,
        IReadOnlyLedger ledger,
        DateTimeOffset endedAt)
    {
        var netPnl = finalEquity - _initialEquity;
        var totalReturn = _initialEquity > 0m ? netPnl / _initialEquity : 0m;
        var elapsed = endedAt - _startedAt;
        // Same formulas as BacktestMetricsEngine (risk-free rate zero), so live and backtest runs
        // emitting BacktestMetrics are comparable: geometric annualisation over calendar time,
        // sample standard deviation for Sharpe, and downside deviation over losing days for Sortino.
        var annualizedReturn = ComputeAnnualizedReturn(totalReturn, elapsed);
        var dailyReturns = _snapshots.Select(static snapshot => (double)snapshot.DailyReturn).ToArray();
        double sharpe = 0, sortino = 0;
        if (dailyReturns.Length >= 2)
        {
            var mean = dailyReturns.Average();
            var variance = dailyReturns.Sum(r => (r - mean) * (r - mean)) / (dailyReturns.Length - 1);
            var stdDev = Math.Sqrt(variance);
            sharpe = stdDev < 1e-10 ? 0 : mean / stdDev * Math.Sqrt(252.0);
            var downside = dailyReturns.Where(static r => r < 0).ToArray();
            // The backtest engine reports +Infinity with no losing day; live metrics are persisted
            // and must stay finite, so that case is reported as zero.
            if (downside.Length > 0)
            {
                var downsideDev = Math.Sqrt(downside.Average(static r => r * r));
                sortino = downsideDev < 1e-10 ? 0 : mean / downsideDev * Math.Sqrt(252.0);
            }
        }

        var calmar = _maxDrawdownFraction > 0m ? (double)(annualizedReturn / _maxDrawdownFraction) : 0;

        var metrics = new BacktestMetrics(
            InitialCapital: _initialEquity,
            FinalEquity: finalEquity,
            GrossPnl: netPnl + _totalCommissions,
            NetPnl: netPnl,
            TotalReturn: totalReturn,
            AnnualizedReturn: annualizedReturn,
            SharpeRatio: sharpe,
            SortinoRatio: sortino,
            CalmarRatio: calmar,
            MaxDrawdown: _maxDrawdown,
            MaxDrawdownPercent: _maxDrawdownFraction,
            MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 0,
            WinRate: 0,
            TotalTrades: _fills.Count,
            WinningTrades: 0,
            LosingTrades: 0,
            TotalCommissions: _totalCommissions,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: double.NaN,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>(StringComparer.OrdinalIgnoreCase));

        var startDate = DateOnly.FromDateTime(_startedAt.UtcDateTime);
        var endDate = DateOnly.FromDateTime(endedAt.UtcDateTime);
        var request = new BacktestRequest(
            From: startDate,
            To: endDate < startDate ? startDate : endDate,
            Symbols: universe.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            InitialCash: _initialEquity,
            DataRoot: "live-feed",
            CommissionKind: BacktestCommissionKind.Free);

        return new BacktestResult(
            Request: request,
            Universe: universe,
            Snapshots: _snapshots.ToArray(),
            CashFlows: _cashFlows.ToArray(),
            Fills: _fills.ToArray(),
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: elapsed,
            TotalEventsProcessed: EventsProcessed,
            EngineMetadata: new BacktestEngineMetadata(
                EngineId: engineId,
                ResultKind: BacktestResultKinds.SummaryOnly,
                CoverageWarnings:
                [
                    "Win-rate, profit-factor, drawdown-recovery, and XIRR require closed-trade accounting not yet tracked by the live engine.",
                ]));
    }
    private static decimal ComputeAnnualizedReturn(decimal totalReturn, TimeSpan elapsed)
    {
        // A loss of 100% or more has no real annualised root; report it as a total loss.
        if (totalReturn <= -1m)
            return -1m;
        // The return is earned from the session start (initial equity) to its end, so annualise
        // over that interval. Snapshot dates would miss the first day: sessions record no
        // start-of-run snapshot, so the first end-of-day snapshot already includes a day's return.
        var years = Math.Max(elapsed.TotalDays / 365.0, 1.0 / 365.0);
        var annualized = Math.Pow(1.0 + (double)totalReturn, 1.0 / years) - 1.0;
        // Compounding a short run's gain over a year overflows decimal (900% in three days is
        // ~1e116), which threw from Build. Cap the figure; it is reported, not traded on.
        return double.IsFinite(annualized) && annualized < MaxAnnualizedReturn
            ? (decimal)annualized
            : (decimal)MaxAnnualizedReturn;
    }

    private const double MaxAnnualizedReturn = 1e12;
}
