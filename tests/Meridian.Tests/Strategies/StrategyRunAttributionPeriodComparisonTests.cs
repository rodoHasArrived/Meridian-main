using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Xunit;

namespace Meridian.Tests.Strategies;

/// <summary>Per-symbol P&amp;L for a month of a run against the month before (UX Phase E).</summary>
public sealed class StrategyRunAttributionPeriodComparisonTests
{
    [Fact]
    public async Task ComparesEachSymbolsMonthlyPnlFromCumulativeSnapshots()
    {
        // Cumulative realized + unrealized P&L per symbol at each snapshot. MSFT is closed after Oct 14.
        var run = BuildRun("attr-period-1", [
            (new DateOnly(2026, 9, 30), new() { ["AAPL"] = (40m, 60m), ["MSFT"] = (50m, 0m) }),
            (new DateOnly(2026, 10, 14), new() { ["AAPL"] = (80m, 50m), ["MSFT"] = (30m, 50m) }),
            (new DateOnly(2026, 10, 31), new() { ["AAPL"] = (100m, 60m) }),
            (new DateOnly(2026, 11, 20), new() { ["AAPL"] = (150m, 50m) })
        ]);
        var service = await CreateServiceAsync(run);

        var comparison = await service.GetAttributionPeriodComparisonAsync("attr-period-1", periodEnd: null);

        comparison.Should().NotBeNull();
        comparison!.Status.Should().Be(PeriodComparisonStatus.Ready);
        comparison.Current.Should().Be(new PeriodComparisonWindowDto("Nov 2026 (partial)", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 20), true));
        comparison.Baseline.Should().Be(new PeriodComparisonWindowDto("Oct 2026", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), false));
        // AAPL: Nov 200 - 160 = 40; Oct 160 - 100 = 60. MSFT keeps its last cumulative (80): Nov 0; Oct 80 - 50 = 30.
        comparison.Lines.Should().BeEquivalentTo(new[]
        {
            PeriodComparisonLineDto.Create("total", "Total P&L", 40m, 90m, isTotal: true),
            PeriodComparisonLineDto.Create("symbol:AAPL", "AAPL", 40m, 60m),
            PeriodComparisonLineDto.Create("symbol:MSFT", "MSFT", 0m, 30m)
        }, options => options.WithStrictOrdering());
        comparison.Lines[0].PercentChange.Should().BeApproximately(-55.5555555555m, 0.0000001m);
    }

    [Fact]
    public async Task UsesTheMonthContainingTheRequestedPeriodEnd()
    {
        var run = BuildRun("attr-period-2", [
            (new DateOnly(2026, 8, 31), new() { ["AAPL"] = (0m, 10m) }),
            (new DateOnly(2026, 9, 30), new() { ["AAPL"] = (0m, 30m) }),
            (new DateOnly(2026, 10, 31), new() { ["AAPL"] = (0m, 20m) })
        ]);
        var service = await CreateServiceAsync(run);

        var comparison = await service.GetAttributionPeriodComparisonAsync("attr-period-2", new DateOnly(2026, 9, 30));

        comparison!.Current.Label.Should().Be("Sep 2026");
        comparison.Lines.Single(line => line.Key == "symbol:AAPL").Should().Be(PeriodComparisonLineDto.Create("symbol:AAPL", "AAPL", 20m, 10m));
    }

    [Fact]
    public async Task IsUnavailableWithoutDailySnapshotsOrBeforeAPriorMonthExists()
    {
        var summaryOnly = BuildRun("attr-period-3", []);
        var lateStart = BuildRun("attr-period-4", [(new DateOnly(2026, 11, 3), new() { ["AAPL"] = (0m, 5m) })]);
        var service = await CreateServiceAsync(summaryOnly, lateStart);

        var noSnapshots = await service.GetAttributionPeriodComparisonAsync("attr-period-3", null);
        noSnapshots!.Status.Should().Be(PeriodComparisonStatus.Unavailable);
        noSnapshots.Reason.Should().Contain("summary results only");

        var noPriorMonth = await service.GetAttributionPeriodComparisonAsync("attr-period-4", null);
        noPriorMonth!.Status.Should().Be(PeriodComparisonStatus.Unavailable);
        noPriorMonth.Reason.Should().Contain("after the prior month ends");

        (await service.GetAttributionPeriodComparisonAsync("missing-run", null)).Should().BeNull();
    }

    private static async Task<StrategyRunReadService> CreateServiceAsync(params StrategyRunEntry[] runs)
    {
        var store = new StrategyRunStore();
        foreach (var run in runs)
        {
            await store.RecordRunAsync(run);
        }

        return new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
    }

    private static StrategyRunEntry BuildRun(string runId, IReadOnlyList<(DateOnly Day, Dictionary<string, (decimal Realized, decimal Unrealized)> Pnl)> days)
    {
        var snapshots = days.Select(day => new PortfolioSnapshot(
            Timestamp: new DateTimeOffset(day.Day.ToDateTime(new TimeOnly(21, 0)), TimeSpan.Zero),
            Date: day.Day,
            Cash: 1_000m,
            MarginBalance: 0m,
            LongMarketValue: 0m,
            ShortMarketValue: 0m,
            TotalEquity: 1_000m,
            DailyReturn: 0m,
            Positions: day.Pnl.ToDictionary(
                static pair => pair.Key,
                static pair => new Position(pair.Key, 10, 100m, UnrealizedPnl: pair.Value.Unrealized, RealizedPnl: pair.Value.Realized),
                StringComparer.OrdinalIgnoreCase),
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
            DayCashFlows: [])).ToArray();
        var from = days.Count > 0 ? days[0].Day : new DateOnly(2026, 11, 1);
        var to = days.Count > 0 ? days[^1].Day : new DateOnly(2026, 11, 20);
        var metrics = new BacktestMetrics(
            InitialCapital: 1_000m, FinalEquity: 1_000m, GrossPnl: 0m, NetPnl: 0m, TotalReturn: 0m, AnnualizedReturn: 0m,
            SharpeRatio: 0, SortinoRatio: 0, CalmarRatio: 0, MaxDrawdown: 0m, MaxDrawdownPercent: 0m, MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 0, WinRate: 0, TotalTrades: 0, WinningTrades: 0, LosingTrades: 0, TotalCommissions: 0m,
            TotalMarginInterest: 0m, TotalShortRebates: 0m, Xirr: 0, SymbolAttribution: new Dictionary<string, SymbolAttribution>());
        var result = new BacktestResult(
            Request: new BacktestRequest(From: from, To: to, Symbols: ["AAPL"], InitialCash: 1_000m),
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AAPL" },
            Snapshots: snapshots,
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(1),
            TotalEventsProcessed: 1);
        var startedAt = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return new StrategyRunEntry(
            RunId: runId,
            StrategyId: "attr-period",
            StrategyName: "Period Strategy",
            RunType: RunType.Backtest,
            StartedAt: startedAt,
            EndedAt: startedAt.AddDays(1),
            Metrics: result);
    }
}
