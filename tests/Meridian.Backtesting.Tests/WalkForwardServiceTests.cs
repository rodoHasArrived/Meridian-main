using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Backtesting.WalkForward;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Backtesting.Tests;

public sealed class WalkForwardServiceTests
{
    // ────────────────────────────────────────────────────────────────────────
    // Window construction
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildWindows_RollingTraining_ProducesContiguousNonOverlappingTestWindows()
    {
        var windows = WalkForwardService.BuildWindows(
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 20), trainingDays: 5, testDays: 5);

        windows.Should().HaveCount(3);

        windows[0].Should().Be(new WalkForwardWindow(0,
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5),
            new DateOnly(2024, 1, 6), new DateOnly(2024, 1, 10)));

        windows[1].Should().Be(new WalkForwardWindow(1,
            new DateOnly(2024, 1, 6), new DateOnly(2024, 1, 10),
            new DateOnly(2024, 1, 11), new DateOnly(2024, 1, 15)));

        windows[2].Should().Be(new WalkForwardWindow(2,
            new DateOnly(2024, 1, 11), new DateOnly(2024, 1, 15),
            new DateOnly(2024, 1, 16), new DateOnly(2024, 1, 20)));
    }

    [Fact]
    public void BuildWindows_AnchoredTraining_ExpandsTrainingFromFixedOrigin()
    {
        var windows = WalkForwardService.BuildWindows(
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 20), trainingDays: 5, testDays: 5,
            anchoredTraining: true);

        windows.Should().HaveCount(3);
        windows.Should().OnlyContain(w => w.TrainFrom == new DateOnly(2024, 1, 1),
            "anchored training always starts at the full-period origin");
        windows[1].TrainTo.Should().Be(new DateOnly(2024, 1, 10));
        windows[2].TrainTo.Should().Be(new DateOnly(2024, 1, 15));
    }

    [Fact]
    public void BuildWindows_TruncatesFinalTestWindowAtRangeEnd()
    {
        var windows = WalkForwardService.BuildWindows(
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 18), trainingDays: 5, testDays: 5);

        windows.Should().HaveCount(3);
        windows[2].TestFrom.Should().Be(new DateOnly(2024, 1, 16));
        windows[2].TestTo.Should().Be(new DateOnly(2024, 1, 18), "the final window is clipped to the requested range");
    }

    [Fact]
    public void BuildWindows_RangeTooShortForAnyTestDay_ReturnsEmpty()
    {
        var windows = WalkForwardService.BuildWindows(
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5), trainingDays: 5, testDays: 5);

        windows.Should().BeEmpty("five days of data leave no room for an out-of-sample day after a 5-day training window");
    }

    // ────────────────────────────────────────────────────────────────────────
    // End-to-end selection + OOS aggregation (stubbed runs)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_SelectsBestTrainingParametersAndEvaluatesOutOfSample()
    {
        // Training stub: objective (NetPnl) equals the parameter weight, so weight=3 must win
        // every window. OOS stub: each test run returns two +1% days.
        var service = new WalkForwardService(
            NullLogger<WalkForwardService>.Instance,
            new StubBatchService(),
            _ => null!,
            (_, runRequest, _, _) => Task.FromResult(CreateResult(runRequest, netPnl: 42m, dailyReturns: [0.01, 0.01])));

        var report = await service.RunAsync(new WalkForwardRequest
        {
            BaseRequest = CreateBaseRequest(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 20)),
            ParameterGrid =
            [
                new Dictionary<string, object> { ["Weight"] = 1m },
                new Dictionary<string, object> { ["Weight"] = 3m }
            ],
            StrategyDescriptor = "wf-test",
            StrategyFactory = static parameters => new StubStrategy(Convert.ToDecimal(parameters["Weight"])),
            TrainingDays = 5,
            TestDays = 5,
            Objective = WalkForwardObjective.NetPnl,
            MaxConcurrency = 1
        });

        report.Windows.Should().HaveCount(3);
        report.Windows.Should().OnlyContain(w => w.SelectedParameters != null);
        report.Windows.Should().OnlyContain(w => Convert.ToDecimal(w.SelectedParameters!["Weight"]) == 3m,
            "the parameter set with the highest training objective must be carried into every test window");
        report.Windows.Should().OnlyContain(w => w.TestResult != null);
        report.Windows.Should().OnlyContain(w => w.TrainObjective == 3.0);

        report.OosMetrics.Should().NotBeNull();
        // 3 windows × 2 daily returns of +1% = 6 stitched days
        report.OosMetrics!.TradingDays.Should().Be(6);
        report.OosMetrics.TotalReturn.Should().BeApproximately((decimal)(Math.Pow(1.01, 6) - 1), 0.0001m);
        report.OosMetrics.MaxDrawdownPercent.Should().Be(0m, "returns are monotonically positive");

        report.MeanTrainObjective.Should().Be(3.0);
        report.Disclosures.Should().Contain(item => item.Code == "walk-forward");
    }

    [Fact]
    public async Task RunAsync_TrainingObjectiveDegradesOutOfSample_FlagsOverfitWarning()
    {
        // Train objective = 3 (weight), OOS NetPnl = 0.3 → degradation ratio 0.1 << 0.5.
        var service = new WalkForwardService(
            NullLogger<WalkForwardService>.Instance,
            new StubBatchService(),
            _ => null!,
            (_, runRequest, _, _) => Task.FromResult(CreateResult(runRequest, netPnl: 0.3m, dailyReturns: [-0.01])));

        var report = await service.RunAsync(new WalkForwardRequest
        {
            BaseRequest = CreateBaseRequest(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 20)),
            ParameterGrid = [new Dictionary<string, object> { ["Weight"] = 3m }],
            StrategyDescriptor = "wf-degradation",
            StrategyFactory = static parameters => new StubStrategy(Convert.ToDecimal(parameters["Weight"])),
            TrainingDays = 5,
            TestDays = 5,
            Objective = WalkForwardObjective.NetPnl,
            MaxConcurrency = 1
        });

        report.DegradationRatio.Should().BeApproximately(0.1, 0.0001);
        report.Disclosures.Should().Contain(item => item.Code == "walk-forward-degradation" && item.Severity == BiasSeverity.Warning);
    }

    [Fact]
    public async Task RunAsync_RangeTooShort_Throws()
    {
        var service = new WalkForwardService(
            NullLogger<WalkForwardService>.Instance,
            new StubBatchService(),
            _ => null!,
            (_, runRequest, _, _) => Task.FromResult(CreateResult(runRequest, netPnl: 0m, dailyReturns: [])));

        var act = () => service.RunAsync(new WalkForwardRequest
        {
            BaseRequest = CreateBaseRequest(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5)),
            ParameterGrid = [new Dictionary<string, object> { ["Weight"] = 1m }],
            StrategyDescriptor = "wf-short",
            StrategyFactory = static parameters => new StubStrategy(Convert.ToDecimal(parameters["Weight"])),
            TrainingDays = 5,
            TestDays = 5
        });

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*too short*");
    }

    [Fact]
    public async Task RunAsync_OutOfSampleRunFails_RecordsWindowErrorAndKeepsGoing()
    {
        var failedOnce = false;
        var service = new WalkForwardService(
            NullLogger<WalkForwardService>.Instance,
            new StubBatchService(),
            _ => null!,
            (_, runRequest, _, _) =>
            {
                if (!failedOnce)
                {
                    failedOnce = true;
                    throw new InvalidOperationException("boom");
                }
                return Task.FromResult(CreateResult(runRequest, netPnl: 1m, dailyReturns: [0.01]));
            });

        var report = await service.RunAsync(new WalkForwardRequest
        {
            BaseRequest = CreateBaseRequest(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 20)),
            ParameterGrid = [new Dictionary<string, object> { ["Weight"] = 1m }],
            StrategyDescriptor = "wf-failure",
            StrategyFactory = static parameters => new StubStrategy(Convert.ToDecimal(parameters["Weight"])),
            TrainingDays = 5,
            TestDays = 5,
            Objective = WalkForwardObjective.NetPnl,
            MaxConcurrency = 1
        });

        report.Windows.Should().HaveCount(3);
        report.Windows.Count(static w => w.TestResult is null).Should().Be(1);
        report.Windows[0].ErrorMessage.Should().Be("boom");
        report.Disclosures.Should().Contain(item => item.Code == "walk-forward-failed-windows");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private static BacktestRequest CreateBaseRequest(DateOnly from, DateOnly to)
        => new(from, to, ["SPY"], 1_000m, DataRoot: "/tmp");

    /// <summary>
    /// Training-side stub: every parameter set "succeeds" with NetPnl equal to its Weight, so
    /// objective-based selection is fully deterministic without running a real engine.
    /// </summary>
    private sealed class StubBatchService : IBatchBacktestService
    {
        public Task<BatchBacktestSummary> RunBatchAsync(
            BatchBacktestRequest request,
            IProgress<BatchBacktestProgress> progress,
            CancellationToken ct)
        {
            var runs = request.ParameterGrid
                .Select(parameters => new BatchBacktestRun
                {
                    Parameters = parameters,
                    Result = CreateResult(request.BaseRequest, netPnl: Convert.ToDecimal(parameters["Weight"]), dailyReturns: []),
                    DurationMs = 1
                })
                .ToList();

            return Task.FromResult(new BatchBacktestSummary
            {
                Runs = runs,
                TotalDuration = TimeSpan.FromMilliseconds(runs.Count)
            });
        }
    }

    private static BacktestResult CreateResult(BacktestRequest request, decimal netPnl, IReadOnlyList<double> dailyReturns)
    {
        var metrics = new BacktestMetrics(
            request.InitialCash,
            request.InitialCash + netPnl,
            netPnl,
            netPnl,
            request.InitialCash == 0 ? 0 : netPnl / request.InitialCash,
            0, 0, 0, 0, 0, 0, 0, 0, 0,
            TotalTrades: 1,
            WinningTrades: 1,
            LosingTrades: 0,
            TotalCommissions: 0m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());

        var snapshots = dailyReturns
            .Select((dailyReturn, index) => new PortfolioSnapshot(
                new DateTimeOffset(request.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(index),
                request.From.AddDays(index),
                Cash: request.InitialCash,
                MarginBalance: 0m,
                LongMarketValue: 0m,
                ShortMarketValue: 0m,
                TotalEquity: request.InitialCash,
                DailyReturn: (decimal)dailyReturn,
                Positions: new Dictionary<string, Position>(),
                Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
                DayCashFlows: []))
            .ToList();

        return new BacktestResult(request, new HashSet<string>(), snapshots, [], [], metrics, new BacktestLedger(), TimeSpan.Zero, 0);
    }

    private sealed class StubStrategy(decimal weight) : IBacktestStrategy
    {
        public string Name => $"Stub-{weight}";
        public void Initialize(IBacktestContext ctx) { }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }
}
