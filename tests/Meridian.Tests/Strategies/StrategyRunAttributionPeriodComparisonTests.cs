using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
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
        // Cumulative realized + unrealized P&L per symbol at each snapshot. MSFT is closed on Oct 20:
        // its closed lot realizes 70 more, so its cumulative P&L becomes 30 + 70 = 100, not its last mark (80).
        var run = BuildRun("attr-period-1", [
            (new DateOnly(2026, 9, 30), new() { ["AAPL"] = (40m, 60m), ["MSFT"] = (50m, 0m) }),
            (new DateOnly(2026, 10, 14), new() { ["AAPL"] = (80m, 50m), ["MSFT"] = (30m, 50m) }),
            (new DateOnly(2026, 10, 31), new() { ["AAPL"] = (100m, 60m) }),
            (new DateOnly(2026, 11, 20), new() { ["AAPL"] = (150m, 50m) })
        ],
        equity: new()
        {
            [new DateOnly(2026, 9, 30)] = 1_150m,
            [new DateOnly(2026, 10, 14)] = 1_210m,
            [new DateOnly(2026, 10, 31)] = 1_260m,
            [new DateOnly(2026, 11, 20)] = 1_300m
        },
        closedLots: [ClosedLong("MSFT", entry: 100m, close: 107m, new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero))]);
        var service = await CreateServiceAsync(run);

        var comparison = await service.GetAttributionPeriodComparisonAsync("attr-period-1", periodEnd: null);

        comparison.Should().NotBeNull();
        comparison!.Status.Should().Be(PeriodComparisonStatus.Ready);
        comparison.Current.Should().Be(new PeriodComparisonWindowDto("Nov 2026 (partial)", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 20), true));
        comparison.Baseline.Should().Be(new PeriodComparisonWindowDto("Oct 2026", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), false));
        // AAPL: Nov 200 - 160 = 40; Oct 160 - 100 = 60. MSFT: Nov 0; Oct 100 - 50 = 50. The equity change
        // matches (Nov 40, Oct 110), so there is no Other line.
        comparison.Lines.Should().BeEquivalentTo(new[]
        {
            PeriodComparisonLineDto.Create("total", "Net P&L", 40m, 110m, isTotal: true),
            PeriodComparisonLineDto.Create("symbol:AAPL", "AAPL", 40m, 60m),
            PeriodComparisonLineDto.Create("symbol:MSFT", "MSFT", 0m, 50m)
        }, options => options.WithStrictOrdering());
        comparison.Lines[0].PercentChange.Should().BeApproximately(-63.6363636363m, 0.0000001m);
    }

    [Fact]
    public async Task DeductsEachMonthsCommissionsOnceFromTheGrossSymbolPnl()
    {
        // Snapshot P&L is gross; commissions are dated by fill (UTC). Sep's fill is outside both windows.
        var run = BuildRun("attr-period-fees", [
            (new DateOnly(2026, 9, 30), new() { ["AAPL"] = (0m, 100m) }),
            (new DateOnly(2026, 10, 31), new() { ["AAPL"] = (0m, 160m) }),
            (new DateOnly(2026, 11, 20), new() { ["AAPL"] = (0m, 200m) })
        ], [
            Fill("AAPL", 7m, new DateTimeOffset(2026, 9, 30, 15, 0, 0, TimeSpan.Zero)),
            Fill("AAPL", 2m, new DateTimeOffset(2026, 10, 1, 14, 0, 0, TimeSpan.Zero)),
            Fill("AAPL", 3m, new DateTimeOffset(2026, 10, 31, 20, 0, 0, TimeSpan.Zero)),
            Fill("AAPL", 1.5m, new DateTimeOffset(2026, 11, 20, 23, 0, 0, TimeSpan.Zero)),
            Fill("AAPL", 9m, new DateTimeOffset(2026, 11, 21, 0, 30, 0, TimeSpan.FromHours(2)))
        ],
        equity: new()
        {
            [new DateOnly(2026, 9, 30)] = 1_093m,
            [new DateOnly(2026, 10, 31)] = 1_148m,
            [new DateOnly(2026, 11, 20)] = 1_177.5m
        });
        var service = await CreateServiceAsync(run);

        var comparison = await service.GetAttributionPeriodComparisonAsync("attr-period-fees", periodEnd: null);

        // The last fill is Nov 20 22:30 UTC, so it counts in November: 1.5 + 9 = 10.5. October: 2 + 3 = 5.
        comparison!.Lines.Should().BeEquivalentTo(new[]
        {
            PeriodComparisonLineDto.Create("total", "Net P&L", 29.5m, 55m, isTotal: true),
            PeriodComparisonLineDto.Create("symbol:AAPL", "AAPL", 40m, 60m),
            PeriodComparisonLineDto.Create("commissions", "Commissions", -10.5m, -5m)
        }, options => options.WithStrictOrdering());
        comparison.Notes.Should().Contain(note => note.Contains("before commissions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SplitsFinancingByMonthAndShowsTheUnexplainedEquityChangeAsOther()
    {
        var run = BuildRun("attr-period-financing", [
            (new DateOnly(2026, 9, 30), new() { ["AAPL"] = (0m, 100m) }),
            (new DateOnly(2026, 10, 15), new() { ["AAPL"] = (0m, 130m) }),
            (new DateOnly(2026, 10, 31), new() { ["AAPL"] = (0m, 160m) }),
            (new DateOnly(2026, 11, 10), new() { ["AAPL"] = (0m, 180m) }),
            (new DateOnly(2026, 11, 20), new() { ["AAPL"] = (0m, 200m) })
        ],
        equity: new()
        {
            [new DateOnly(2026, 9, 30)] = 1_100m,
            [new DateOnly(2026, 10, 15)] = 1_127m,
            [new DateOnly(2026, 10, 31)] = 1_157m,
            [new DateOnly(2026, 11, 10)] = 1_178m,
            [new DateOnly(2026, 11, 20)] = 1_198m
        },
        flows: new()
        {
            [new DateOnly(2026, 10, 15)] =
            [
                new MarginInterestCashFlow(At(2026, 10, 15), -4m, -2_000m, 0.05),
                new ShortRebateCashFlow(At(2026, 10, 15), 1m, "MSFT", 10, 0.02)
            ],
            [new DateOnly(2026, 11, 10)] =
            [
                new MarginInterestCashFlow(At(2026, 11, 10), -6m, -2_000m, 0.05),
                new CashInterestCashFlow(At(2026, 11, 10), 2m, 0.01),
                new DividendCashFlow(At(2026, 11, 10), 5m, "AAPL", 10, 0.5m)
            ]
        });
        var service = await CreateServiceAsync(run);

        var comparison = await service.GetAttributionPeriodComparisonAsync("attr-period-financing", periodEnd: null);

        // Nov: equity 1198 - 1157 = 41 = AAPL 40 - 6 margin + 2 cash interest + 5 Other (the dividend).
        // Oct: 1157 - 1100 = 57 = AAPL 60 - 4 margin + 1 rebate, with nothing left over.
        comparison!.Lines.Should().BeEquivalentTo(new[]
        {
            PeriodComparisonLineDto.Create("total", "Net P&L", 41m, 57m, isTotal: true),
            PeriodComparisonLineDto.Create("symbol:AAPL", "AAPL", 40m, 60m),
            PeriodComparisonLineDto.Create("margin-interest", "Margin interest", -6m, -4m),
            PeriodComparisonLineDto.Create("short-rebates", "Short rebates", 0m, 1m),
            PeriodComparisonLineDto.Create("cash-interest", "Cash interest", 2m, 0m),
            PeriodComparisonLineDto.Create("other", "Other (dividends, corporate actions, rounding)", 5m, 0m)
        }, options => options.WithStrictOrdering());
        comparison.Lines.Should().NotContain(line => line.Key == "commissions");
    }

    [Fact]
    public async Task StartsTheFirstMonthFromInitialCapitalAndCountsSymbolsOpenedAndClosedBetweenSnapshots()
    {
        // TSLA is bought and sold on Nov 3, between snapshots, so only its closed lot shows it.
        var run = BuildRun("attr-period-first", [
            (new DateOnly(2026, 10, 5), new() { ["AAPL"] = (0m, 10m) }),
            (new DateOnly(2026, 10, 31), new() { ["AAPL"] = (0m, 30m) }),
            (new DateOnly(2026, 11, 20), new() { ["AAPL"] = (0m, 50m) })
        ],
        equity: new()
        {
            [new DateOnly(2026, 10, 5)] = 1_010m,
            [new DateOnly(2026, 10, 31)] = 1_030m,
            [new DateOnly(2026, 11, 20)] = 1_070m
        },
        closedLots: [ClosedLong("TSLA", entry: 200m, close: 202m, new DateTimeOffset(2026, 11, 3, 16, 0, 0, TimeSpan.Zero))]);
        var service = await CreateServiceAsync(run);

        var comparison = await service.GetAttributionPeriodComparisonAsync("attr-period-first", periodEnd: null);

        comparison!.Baseline!.IsPartial.Should().BeTrue();
        comparison.Lines.Should().BeEquivalentTo(new[]
        {
            PeriodComparisonLineDto.Create("total", "Net P&L", 40m, 30m, isTotal: true),
            PeriodComparisonLineDto.Create("symbol:AAPL", "AAPL", 20m, 30m),
            PeriodComparisonLineDto.Create("symbol:TSLA", "TSLA", 20m, 0m)
        }, options => options.WithStrictOrdering());
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

    private static FillEvent Fill(string symbol, decimal commission, DateTimeOffset filledAt) =>
        new(Guid.NewGuid(), Guid.NewGuid(), symbol, 10, 100m, commission, filledAt);

    private static DateTimeOffset At(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private static ClosedLot ClosedLong(string symbol, decimal entry, decimal close, DateTimeOffset closedAt) =>
        new(Guid.NewGuid(), symbol, 10m, entry, closedAt.AddDays(-1), Guid.NewGuid(), close, closedAt, Guid.NewGuid(), "main")
        {
            UnitKind = SecurityLotUnitKind.Shares,
            Multiplier = 1m
        };

    /// <summary>
    /// A run with one snapshot per listed day. Equity defaults to 1,000 plus the P&amp;L of the positions
    /// listed that day; closed lots sit on the final snapshot, as a real run carries them.
    /// </summary>
    private static StrategyRunEntry BuildRun(
        string runId,
        IReadOnlyList<(DateOnly Day, Dictionary<string, (decimal Realized, decimal Unrealized)> Pnl)> days,
        IReadOnlyList<FillEvent>? fills = null,
        Dictionary<DateOnly, decimal>? equity = null,
        Dictionary<DateOnly, CashFlowEntry[]>? flows = null,
        IReadOnlyList<ClosedLot>? closedLots = null)
    {
        var finalDay = days.Count > 0 ? days[^1].Day : default;
        var snapshots = days.Select(day => new PortfolioSnapshot(
            Timestamp: new DateTimeOffset(day.Day.ToDateTime(new TimeOnly(21, 0)), TimeSpan.Zero),
            Date: day.Day,
            Cash: 1_000m,
            MarginBalance: 0m,
            LongMarketValue: 0m,
            ShortMarketValue: 0m,
            TotalEquity: equity?.GetValueOrDefault(day.Day) ?? 1_000m + day.Pnl.Values.Sum(static pnl => pnl.Realized + pnl.Unrealized),
            DailyReturn: 0m,
            Positions: day.Pnl.ToDictionary(
                static pair => pair.Key,
                static pair => new Position(pair.Key, 10, 100m, UnrealizedPnl: pair.Value.Unrealized, RealizedPnl: pair.Value.Realized),
                StringComparer.OrdinalIgnoreCase),
            Accounts: closedLots is { Count: > 0 } && day.Day == finalDay
                ? new Dictionary<string, FinancialAccountSnapshot>
                {
                    ["main"] = new("main", "Main", FinancialAccountKind.Brokerage, null, 1_000m, 0m, 0m, 0m, 1_000m,
                        new Dictionary<string, Position>(), new FinancialAccountRules(), ClosedLots: closedLots)
                }
                : new Dictionary<string, FinancialAccountSnapshot>(),
            DayCashFlows: flows?.GetValueOrDefault(day.Day) ?? [])).ToArray();
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
            Fills: fills ?? [],
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
