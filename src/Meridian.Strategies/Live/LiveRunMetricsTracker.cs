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
    private decimal _maxDrawdownPercent;
    private double _sumReturns;
    private double _sumSquaredReturns;
    private double _sumSquaredDownsideReturns;
    private int _tradingDays;
    private decimal _totalCommissions;

    public LiveRunMetricsTracker(decimal initialEquity, DateTimeOffset startedAt)
    {
        _initialEquity = initialEquity;
        _startedAt = startedAt;
        _previousEquity = initialEquity;
        _peakEquity = initialEquity;
    }

    public long EventsProcessed { get; set; }

    public void RecordFill(FillEvent fill, DateTimeOffset timestamp)
    {
        _fills.Add(fill);
        var tradeCashFlow = new TradeCashFlow(
            Timestamp: timestamp,
            Amount: -(fill.FilledQuantity * fill.FillPrice),
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
        _tradingDays++;

        var dailyReturnDouble = (double)dailyReturn;
        _sumReturns += dailyReturnDouble;
        _sumSquaredReturns += dailyReturnDouble * dailyReturnDouble;
        if (dailyReturnDouble < 0)
        {
            _sumSquaredDownsideReturns += dailyReturnDouble * dailyReturnDouble;
        }

        if (equity > _peakEquity)
        {
            _peakEquity = equity;
        }

        var drawdown = _peakEquity - equity;
        if (drawdown > _maxDrawdown)
        {
            _maxDrawdown = drawdown;
            _maxDrawdownPercent = _peakEquity > 0m ? drawdown / _peakEquity * 100m : 0m;
        }

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
        var annualizationFactor = _tradingDays > 0 ? 252m / _tradingDays : 0m;
        var annualizedReturn = totalReturn * annualizationFactor;

        double sharpe = 0, sortino = 0;
        if (_tradingDays >= 2)
        {
            var mean = _sumReturns / _tradingDays;
            var variance = (_sumSquaredReturns / _tradingDays) - (mean * mean);
            var stdDev = variance > 0 ? Math.Sqrt(variance) : 0;
            sharpe = stdDev > 0 ? mean / stdDev * Math.Sqrt(252) : 0;
            var downsideDev = Math.Sqrt(_sumSquaredDownsideReturns / _tradingDays);
            sortino = downsideDev > 0 ? mean / downsideDev * Math.Sqrt(252) : 0;
        }

        var calmar = _maxDrawdownPercent > 0m ? (double)(annualizedReturn / (_maxDrawdownPercent / 100m)) : 0;

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
            MaxDrawdownPercent: _maxDrawdownPercent,
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
}
