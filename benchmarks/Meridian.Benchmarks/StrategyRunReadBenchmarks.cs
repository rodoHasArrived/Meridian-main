using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;

namespace Meridian.Benchmarks;

/// <summary>
/// Benchmarks the indexed strategy-run repository and read service on realistic run counts.
/// Covers the three dominant workstation read scenarios added in the April 22, 2026 pass:
/// top-N history retrieval, single run detail lookup, and selected-id comparison reads.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class StrategyRunReadBenchmarks
{
    private StrategyRunReadService _service = null!;
    private string _detailRunId = null!;
    private IReadOnlyList<string> _comparisonRunIds = null!;
    private StrategyRunHistoryQuery _historyQuery = null!;
    private BacktestResult _strongResult = null!;
    private BacktestResult _steadyResult = null!;

    [Params(1_000, 10_000)]
    public int RunCount;

    [GlobalSetup]
    public async Task Setup()
    {
        var store = new StrategyRunStore();
        _service = new StrategyRunReadService(
            store,
            new PortfolioReadService(),
            new LedgerReadService());

        _strongResult = CreateBacktestResult(
            finalEquity: 126_500m,
            netPnl: 26_500m,
            totalReturn: 0.265m,
            realizedPnl: 11_000m,
            unrealizedPnl: 15_500m,
            fillCount: 4,
            sharpeRatio: 1.44,
            maxDrawdown: 5_400m,
            symbol: "AAPL");
        _steadyResult = CreateBacktestResult(
            finalEquity: 108_250m,
            netPnl: 8_250m,
            totalReturn: 0.0825m,
            realizedPnl: 3_250m,
            unrealizedPnl: 5_000m,
            fillCount: 2,
            sharpeRatio: 0.81,
            maxDrawdown: 8_200m,
            symbol: "MSFT");

        var startedAt = new DateTimeOffset(2026, 4, 1, 13, 30, 0, TimeSpan.Zero);
        var comparisonIds = new List<string>(16);

        for (var i = 0; i < RunCount; i++)
        {
            var runId = $"run-{i:D5}";
            var strategyId = $"strategy-{i % 64:D2}";
            var strategyName = $"Strategy {i % 64:D2}";
            var runType = (i % 10) switch
            {
                <= 5 => RunType.Backtest,
                <= 7 => RunType.Paper,
                _ => RunType.Live
            };

            var entry = CreateRunEntry(
                runId,
                strategyId,
                strategyName,
                runType,
                startedAt.AddMinutes(i),
                i);

            await store.RecordRunAsync(entry).ConfigureAwait(false);

            if (runType == RunType.Backtest)
            {
                _detailRunId ??= runId;
                if (comparisonIds.Count < 16)
                {
                    comparisonIds.Add(runId);
                }
            }
        }

        _comparisonRunIds = comparisonIds;
        _historyQuery = new StrategyRunHistoryQuery(
            Modes: [StrategyRunMode.Backtest, StrategyRunMode.Paper, StrategyRunMode.Live],
            Limit: 50);
    }

    [Benchmark(Baseline = true)]
    public Task<IReadOnlyList<StrategyRunSummary>> TopHistoryQuery_Top50()
    {
        return _service.GetRunsAsync(_historyQuery);
    }

    [Benchmark]
    public Task<StrategyRunDetail?> SingleRunDetail_ById()
    {
        return _service.GetRunDetailAsync(_detailRunId);
    }

    [Benchmark]
    public Task<IReadOnlyList<RunComparisonDto>> ComparisonLookup_SelectedIds()
    {
        return _service.GetRunComparisonDtosAsync(_comparisonRunIds);
    }

    private StrategyRunEntry CreateRunEntry(
        string runId,
        string strategyId,
        string strategyName,
        RunType runType,
        DateTimeOffset startedAt,
        int ordinal)
    {
        var isCompleted = runType == RunType.Backtest || ordinal % 4 == 0;
        DateTimeOffset? endedAt = isCompleted ? startedAt.AddMinutes(30 + (ordinal % 180)) : null;
        StrategyRunStatus? terminalStatus = ordinal % 29 == 0
            ? StrategyRunStatus.Failed
            : ordinal % 37 == 0
                ? StrategyRunStatus.Cancelled
                : null;

        return new StrategyRunEntry(
            RunId: runId,
            StrategyId: strategyId,
            StrategyName: strategyName,
            RunType: runType,
            StartedAt: startedAt,
            EndedAt: terminalStatus.HasValue ? endedAt ?? startedAt.AddMinutes(5) : endedAt,
            Metrics: runType == RunType.Backtest ? (ordinal % 2 == 0 ? _strongResult : _steadyResult) : null,
            DatasetReference: runType == RunType.Backtest ? "dataset/us-equities/2026-q2" : null,
            FeedReference: runType switch
            {
                RunType.Backtest => "polygon:stocks",
                RunType.Paper => "synthetic:paper",
                _ => "ib:live"
            },
            PortfolioId: $"{strategyId}-{runType.ToString().ToLowerInvariant()}-portfolio",
            LedgerReference: runType == RunType.Backtest ? $"{strategyId}-{runId}-ledger" : null,
            AuditReference: $"{runId}-audit",
            Engine: runType switch
            {
                RunType.Backtest => "MeridianNative",
                RunType.Paper => "BrokerPaper",
                RunType.Live => "BrokerLive",
                _ => "Unknown"
            },
            ParameterSet: runType == RunType.Backtest
                ? new Dictionary<string, string>
                {
                    ["lookback"] = "20",
                    ["threshold"] = "1.5"
                }
                : null,
            TerminalStatus: terminalStatus);
    }

    private static BacktestResult CreateBacktestResult(
        decimal finalEquity,
        decimal netPnl,
        decimal totalReturn,
        decimal realizedPnl,
        decimal unrealizedPnl,
        int fillCount,
        double sharpeRatio,
        decimal maxDrawdown,
        string symbol)
    {
        var startedAt = new DateTimeOffset(2026, 4, 1, 13, 30, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddHours(2);

        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            [symbol] = new(symbol, 100, 450m, unrealizedPnl, realizedPnl)
        };

        var account = FinancialAccount.CreateDefaultBrokerage(100_000m, 0.05, 0.02);
        var accountSnapshots = new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
        {
            [account.AccountId] = new FinancialAccountSnapshot(
                AccountId: account.AccountId,
                DisplayName: account.DisplayName,
                Kind: account.Kind,
                Institution: account.Institution,
                Cash: 40_000m,
                MarginBalance: -5_000m,
                LongMarketValue: 80_000m,
                ShortMarketValue: -5_000m,
                Equity: finalEquity,
                Positions: positions,
                Rules: account.Rules!)
        };

        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 40_000m,
            MarginBalance: -5_000m,
            LongMarketValue: 80_000m,
            ShortMarketValue: -5_000m,
            TotalEquity: finalEquity,
            DailyReturn: totalReturn,
            Positions: positions,
            Accounts: accountSnapshots,
            DayCashFlows: Array.Empty<CashFlowEntry>());

        var ledger = new Meridian.Ledger.Ledger();
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var equity = new LedgerAccount("Owner's Equity", LedgerAccountType.Equity);
        var tradingGains = new LedgerAccount("Trading Gains", LedgerAccountType.Revenue, Symbol: symbol);
        var commissions = new LedgerAccount("Commissions", LedgerAccountType.Expense, Symbol: symbol);

        ledger.PostLines(startedAt, "initial-capital", new[]
        {
            (cash, 100_000m, 0m),
            (equity, 0m, 100_000m),
        });

        ledger.PostLines(completedAt, "close-run", new[]
        {
            (cash, netPnl, 0m),
            (tradingGains, 0m, netPnl),
            (commissions, 125m, 0m),
            (cash, 0m, 125m),
        });

        var fills = Enumerable.Range(0, fillCount)
            .Select(index => new FillEvent(
                FillId: Guid.NewGuid(),
                OrderId: Guid.NewGuid(),
                Symbol: symbol,
                FilledQuantity: 50,
                FillPrice: 455m + index,
                Commission: 62.5m,
                FilledAt: startedAt.AddMinutes(index + 1),
                AccountId: account.AccountId))
            .ToArray();

        var request = new BacktestRequest(
            From: DateOnly.FromDateTime(startedAt.UtcDateTime),
            To: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Symbols: [symbol],
            InitialCash: 100_000m,
            DataRoot: "./data");

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m,
            FinalEquity: finalEquity,
            GrossPnl: netPnl + 125m,
            NetPnl: netPnl,
            TotalReturn: totalReturn,
            AnnualizedReturn: totalReturn,
            SharpeRatio: sharpeRatio,
            SortinoRatio: sharpeRatio,
            CalmarRatio: sharpeRatio / 2,
            MaxDrawdown: maxDrawdown,
            MaxDrawdownPercent: maxDrawdown / 100_000m,
            MaxDrawdownRecoveryDays: 3,
            ProfitFactor: 1.8,
            WinRate: 0.65,
            TotalTrades: fillCount,
            WinningTrades: fillCount,
            LosingTrades: 0,
            TotalCommissions: 125m,
            TotalMarginInterest: 50m,
            TotalShortRebates: 15m,
            Xirr: 0.19,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>
            {
                [symbol] = new(symbol, realizedPnl, unrealizedPnl, fillCount, 125m, 35m)
            });

        return new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { symbol },
            Snapshots: [snapshot],
            CashFlows: Array.Empty<CashFlowEntry>(),
            Fills: fills,
            Metrics: metrics,
            Ledger: ledger,
            ElapsedTime: TimeSpan.FromMinutes(12),
            TotalEventsProcessed: 2_500);
    }
}
