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

    [Fact]
    public void BuildWindows_StepShorterThanTestWindow_ThrowsToPreventOverlappingOosWindows()
    {
        var act = () => WalkForwardService.BuildWindows(
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 20), trainingDays: 5, testDays: 5, stepDays: 3);

        act.Should().Throw<ArgumentException>().WithMessage("*overlap*",
            "overlapping test windows would double-count days in the stitched OOS metrics");
    }

    [Fact]
    public void BuildWindows_NegativeStep_Throws()
    {
        var act = () => WalkForwardService.BuildWindows(
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 20), trainingDays: 5, testDays: 5, stepDays: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BuildWindows_StepLargerThanTestWindow_LeavesGapsButIsAccepted()
    {
        var windows = WalkForwardService.BuildWindows(
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31), trainingDays: 5, testDays: 3, stepDays: 10);

        windows.Should().NotBeEmpty();
        windows.Should().OnlyContain(w => w.TestTo.DayNumber - w.TestFrom.DayNumber + 1 <= 3);
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
    public async Task RunAsync_EqualTrainingObjectives_SelectsEarliestParameterGridEntry()
    {
        var service = new WalkForwardService(
            NullLogger<WalkForwardService>.Instance,
            new ReverseOrderedTieBatchService(),
            _ => null!,
            (_, runRequest, _, _) => Task.FromResult(CreateResult(runRequest, netPnl: 1m, dailyReturns: [0.01])));

        var report = await service.RunAsync(new WalkForwardRequest
        {
            BaseRequest = CreateBaseRequest(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 10)),
            ParameterGrid =
            [
                new Dictionary<string, object> { ["Weight"] = 1m },
                new Dictionary<string, object> { ["Weight"] = 2m }
            ],
            StrategyDescriptor = "wf-equal-objective",
            StrategyFactory = static parameters => new StubStrategy(Convert.ToDecimal(parameters["Weight"])),
            TrainingDays = 5,
            TestDays = 5,
            Objective = WalkForwardObjective.NetPnl,
            MaxConcurrency = 2
        });

        report.Windows.Should().ContainSingle();
        Convert.ToDecimal(report.Windows[0].SelectedParameters!["Weight"]).Should().Be(1m,
            "equal objectives must resolve by the original parameter-grid order, not completion order");
    }

    [Fact]
    public async Task RunAsync_ClonedTieParameters_UsePreindexedStructuralFallback()
    {
        const int gridSize = 20;
        var equalityCounter = new EqualityCounter();
        var parameterGrid = Enumerable.Range(0, gridSize)
            .Select(index => new Dictionary<string, object>
            {
                ["Marker"] = new CountingValue(index, equalityCounter),
                ["Weight"] = 1m
            })
            .ToArray();
        var service = new WalkForwardService(
            NullLogger<WalkForwardService>.Instance,
            new CloningReverseOrderedTieBatchService(),
            _ => null!,
            (_, runRequest, _, _) => Task.FromResult(CreateResult(runRequest, netPnl: 1m, dailyReturns: [0.01])));

        var report = await service.RunAsync(CreateSingleWindowRequest(parameterGrid));

        var selectedParameters = report.Windows.Should().ContainSingle().Which.SelectedParameters;
        selectedParameters.Should().NotBeNull();
        ((CountingValue)selectedParameters!["Marker"]).Id.Should().Be(0);
        equalityCounter.Count.Should().BeInRange(1, gridSize * 4,
            "parameter-grid lookup should be preindexed rather than scan the full grid for every result");
    }

    [Fact]
    public async Task RunAsync_CancelledWhileBatchIgnoresToken_StopsBeforeOutOfSampleRun()
    {
        var batchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batchFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outOfSampleExecutions = 0;
        var service = new WalkForwardService(
            NullLogger<WalkForwardService>.Instance,
            new DelegatingBatchService(async (request, _, _) =>
            {
                batchStarted.TrySetResult();
                try
                {
                    await releaseBatch.Task;
                    return CreateTrainingSummary(request);
                }
                finally
                {
                    batchFinished.TrySetResult();
                }
            }),
            _ => null!,
            (_, runRequest, _, _) =>
            {
                Interlocked.Increment(ref outOfSampleExecutions);
                return Task.FromResult(CreateResult(runRequest, netPnl: 1m, dailyReturns: [0.01]));
            });
        using var cts = new CancellationTokenSource();

        var runTask = service.RunAsync(CreateSingleWindowRequest(), ct: cts.Token);
        await batchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => runTask.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(releaseBatch.Task.IsCompleted);
            Assert.Equal(0, outOfSampleExecutions);
        }
        finally
        {
            releaseBatch.TrySetResult();
            await batchFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task RunAsync_CancelledWhileFinalOutOfSampleRunIgnoresToken_DoesNotReturnSuccess()
    {
        var testStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var testFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new WalkForwardService(
            NullLogger<WalkForwardService>.Instance,
            new StubBatchService(),
            _ => null!,
            async (_, runRequest, _, _) =>
            {
                testStarted.TrySetResult();
                try
                {
                    await releaseTest.Task;
                    return CreateResult(runRequest, netPnl: 1m, dailyReturns: [0.01]);
                }
                finally
                {
                    testFinished.TrySetResult();
                }
            });
        using var cts = new CancellationTokenSource();

        var runTask = service.RunAsync(CreateSingleWindowRequest(), ct: cts.Token);
        await testStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => runTask.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(releaseTest.Task.IsCompleted);
        }
        finally
        {
            releaseTest.TrySetResult();
            await testFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void RunAsync_ProgressForwarding_PreservesTrainingBeforeTesting()
    {
        var reports = new List<WalkForwardProgress>();
        var deferredContext = new DeferredSynchronizationContext();
        var originalContext = SynchronizationContext.Current;
        var service = new WalkForwardService(
            NullLogger<WalkForwardService>.Instance,
            new DelegatingBatchService((request, progress, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(new BatchBacktestProgress
                {
                    Completed = 0,
                    Total = 1,
                    CurrentLabel = "training-started"
                });
                progress.Report(new BatchBacktestProgress
                {
                    Completed = 1,
                    Total = 1,
                    CurrentLabel = "training-completed"
                });
                return Task.FromResult(CreateTrainingSummary(request));
            }),
            _ => null!,
            (_, runRequest, _, _) => Task.FromResult(CreateResult(runRequest, netPnl: 1m, dailyReturns: [0.01])));

        try
        {
            SynchronizationContext.SetSynchronizationContext(deferredContext);
            service.RunAsync(
                    CreateSingleWindowRequest(),
                    new RecordingProgress<WalkForwardProgress>(reports.Add))
                .GetAwaiter()
                .GetResult();
            deferredContext.Drain();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }

        reports.Select(static report => report.Phase)
            .Should().Equal("training", "training", "testing");
        reports.Select(static report => report.WindowIndex).Should().OnlyContain(index => index == 0);
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

    private static WalkForwardRequest CreateSingleWindowRequest(
        IReadOnlyList<Dictionary<string, object>>? parameterGrid = null)
        => new()
        {
            BaseRequest = CreateBaseRequest(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 10)),
            ParameterGrid = parameterGrid ??
                new[] { new Dictionary<string, object> { ["Weight"] = 1m } },
            StrategyDescriptor = "single-window-test",
            StrategyFactory = static parameters => new StubStrategy(Convert.ToDecimal(parameters["Weight"])),
            TrainingDays = 5,
            TestDays = 5,
            Objective = WalkForwardObjective.NetPnl,
            MaxConcurrency = 2
        };

    private static BatchBacktestSummary CreateTrainingSummary(BatchBacktestRequest request)
    {
        var runs = request.ParameterGrid
            .Select(parameters => new BatchBacktestRun
            {
                Parameters = parameters,
                Result = CreateResult(
                    request.BaseRequest,
                    netPnl: Convert.ToDecimal(parameters["Weight"]),
                    dailyReturns: []),
                DurationMs = 1
            })
            .ToList();

        return new BatchBacktestSummary
        {
            Runs = runs,
            TotalDuration = TimeSpan.FromMilliseconds(runs.Count)
        };
    }

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
            return Task.FromResult(CreateTrainingSummary(request));
        }
    }

    private sealed class ReverseOrderedTieBatchService : IBatchBacktestService
    {
        public Task<BatchBacktestSummary> RunBatchAsync(
            BatchBacktestRequest request,
            IProgress<BatchBacktestProgress> progress,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var runs = request.ParameterGrid
                .Reverse()
                .Select(parameters => new BatchBacktestRun
                {
                    Parameters = parameters,
                    Result = CreateResult(request.BaseRequest, netPnl: 10m, dailyReturns: []),
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

    private sealed class CloningReverseOrderedTieBatchService : IBatchBacktestService
    {
        public Task<BatchBacktestSummary> RunBatchAsync(
            BatchBacktestRequest request,
            IProgress<BatchBacktestProgress> progress,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var runs = request.ParameterGrid
                .Reverse()
                .Select(parameters =>
                {
                    var cloned = parameters.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value is CountingValue value ? value.Clone() : pair.Value,
                        StringComparer.Ordinal);
                    return new BatchBacktestRun
                    {
                        Parameters = cloned,
                        Result = CreateResult(request.BaseRequest, netPnl: 10m, dailyReturns: []),
                        DurationMs = 1
                    };
                })
                .ToList();

            return Task.FromResult(new BatchBacktestSummary
            {
                Runs = runs,
                TotalDuration = TimeSpan.FromMilliseconds(runs.Count)
            });
        }
    }

    private sealed class DelegatingBatchService(
        Func<BatchBacktestRequest, IProgress<BatchBacktestProgress>, CancellationToken, Task<BatchBacktestSummary>> run)
        : IBatchBacktestService
    {
        public Task<BatchBacktestSummary> RunBatchAsync(
            BatchBacktestRequest request,
            IProgress<BatchBacktestProgress> progress,
            CancellationToken ct) => run(request, progress, ct);
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

    private sealed class EqualityCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private sealed class CountingValue(int id, EqualityCounter counter)
    {
        public int Id { get; } = id;

        public CountingValue Clone() => new(Id, counter);

        public override bool Equals(object? obj)
        {
            counter.Increment();
            return obj is CountingValue other && Id == other.Id;
        }

        public override int GetHashCode() => Id;
        public override string ToString() => Id.ToString();
    }

    private sealed class RecordingProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class DeferredSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            _callbacks.Enqueue((d, state));
        }

        public void Drain()
        {
            while (_callbacks.TryDequeue(out var callback))
                callback.Callback(callback.State);
        }
    }
}
