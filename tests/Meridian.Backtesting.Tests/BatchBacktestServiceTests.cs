using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Meridian.Backtesting;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;

namespace Meridian.Backtesting.Tests;

public sealed class BatchBacktestServiceTests
{
    [Fact]
    public async Task RunBatchAsync_UsesStrategyFactoryPerParameterSet_AndProducesDifferentiatedResults()
    {
        var service = new BatchBacktestService(
            NullLogger<BatchBacktestService>.Instance,
            _ => null!,
            (_, runRequest, strategy, _) => Task.FromResult(CreateResult(runRequest, ((ParameterizedTestStrategy)strategy).Weight)));

        var request = new BatchBacktestRequest
        {
            BaseRequest = CreateBaseRequest(),
            ParameterGrid =
            [
                new Dictionary<string, object> { ["Weight"] = 1m, [nameof(BacktestRequest.InitialCash)] = 1_000m },
                new Dictionary<string, object> { ["Weight"] = 3m, [nameof(BacktestRequest.InitialCash)] = 2_000m }
            ],
            MaxConcurrency = 1,
            StrategyDescriptor = "ParameterizedTestStrategy",
            StrategyFactory = static parameters => new ParameterizedTestStrategy(Convert.ToDecimal(parameters["Weight"]))
        };

        var summary = await service.RunBatchAsync(request, progress: null!, CancellationToken.None);

        Assert.Equal(2, summary.Runs.Count);
        Assert.Equal([1_000m, 2_000m], summary.Runs.Select(run => run.Result!.Request.InitialCash).ToArray());
        Assert.Equal([1_000m, 6_000m], summary.Runs.Select(run => run.Result!.Metrics.NetPnl).ToArray());
    }

    [Fact]
    public async Task RunBatchAsync_ConcurrentRuns_ReturnsResultsInParameterGridOrder()
    {
        var releases = Enumerable.Range(1, 3).ToDictionary(
            static index => index,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var completed = Enumerable.Range(1, 3).ToDictionary(
            static index => index,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedCount = 0;
        var service = new BatchBacktestService(
            NullLogger<BatchBacktestService>.Instance,
            _ => null!,
            async (_, runRequest, strategy, ct) =>
            {
                var weight = decimal.ToInt32(((ParameterizedTestStrategy)strategy).Weight);
                if (Interlocked.Increment(ref startedCount) == releases.Count)
                    allStarted.TrySetResult();

                await releases[weight].Task.WaitAsync(ct);
                completed[weight].TrySetResult();
                return CreateResult(runRequest, weight);
            });

        var batchTask = service.RunBatchAsync(new BatchBacktestRequest
        {
            BaseRequest = CreateBaseRequest(),
            ParameterGrid = Enumerable.Range(1, 3)
                .Select(static weight => new Dictionary<string, object> { ["Weight"] = (decimal)weight })
                .ToArray(),
            MaxConcurrency = 3,
            StrategyDescriptor = "ordered-grid",
            StrategyFactory = static parameters => new ParameterizedTestStrategy(Convert.ToDecimal(parameters["Weight"]))
        }, progress: null!, CancellationToken.None);

        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var weight in new[] { 3, 2, 1 })
        {
            releases[weight].TrySetResult();
            await completed[weight].Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var summary = await batchTask;

        Assert.Equal(
            [1m, 2m, 3m],
            summary.Runs.Select(static run => Convert.ToDecimal(run.Parameters["Weight"])).ToArray());
    }

    [Fact]
    public async Task RunBatchAsync_PreCancelledCaller_DoesNotStartAnyRun()
    {
        var executionCount = 0;
        var service = new BatchBacktestService(
            NullLogger<BatchBacktestService>.Instance,
            _ => null!,
            (_, runRequest, _, _) =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.FromResult(CreateResult(runRequest, 1m));
            });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.RunBatchAsync(CreateBatchRequest(runCount: 2, maxConcurrency: 1), null!, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        Assert.Equal(0, executionCount);
    }

    [Fact]
    public async Task RunBatchAsync_PreCancelledEmptyGrid_StillObservesCancellation()
    {
        var executionCount = 0;
        var service = new BatchBacktestService(
            NullLogger<BatchBacktestService>.Instance,
            _ => null!,
            (_, runRequest, _, _) =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.FromResult(CreateResult(runRequest, 1m));
            });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.RunBatchAsync(CreateBatchRequest(runCount: 0, maxConcurrency: 1), null!, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        Assert.Equal(0, executionCount);
    }

    [Fact]
    public async Task RunBatchAsync_EmptyGrid_PreservesSuccessfulEmptySummaryContract()
    {
        var executionCount = 0;
        var service = new BatchBacktestService(
            NullLogger<BatchBacktestService>.Instance,
            _ => null!,
            (_, runRequest, _, _) =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.FromResult(CreateResult(runRequest, 1m));
            });

        var summary = await service.RunBatchAsync(
            CreateBatchRequest(runCount: 0, maxConcurrency: 1),
            progress: null!,
            CancellationToken.None);

        Assert.Empty(summary.Runs);
        Assert.Equal(0, executionCount);
    }

    [Fact]
    public async Task RunBatchAsync_CancelledDuringExecution_PropagatesAndCancelsQueuedAdmission()
    {
        var executionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionCount = 0;
        var service = new BatchBacktestService(
            NullLogger<BatchBacktestService>.Instance,
            _ => null!,
            async (_, runRequest, _, ct) =>
            {
                Interlocked.Increment(ref executionCount);
                executionStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return CreateResult(runRequest, 1m);
            });
        using var cts = new CancellationTokenSource();

        var batchTask = service.RunBatchAsync(CreateBatchRequest(runCount: 2, maxConcurrency: 1), null!, cts.Token);
        await executionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => batchTask);
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task RunBatchAsync_CancelledWhileExecutorIgnoresToken_DoesNotReturnSuccess()
    {
        var executionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executionFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExecution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new BatchBacktestService(
            NullLogger<BatchBacktestService>.Instance,
            _ => null!,
            async (_, runRequest, _, _) =>
            {
                executionStarted.TrySetResult();
                try
                {
                    await releaseExecution.Task;
                    return CreateResult(runRequest, 1m);
                }
                finally
                {
                    executionFinished.TrySetResult();
                }
            });
        using var cts = new CancellationTokenSource();

        var batchTask = service.RunBatchAsync(
            CreateBatchRequest(runCount: 1, maxConcurrency: 1),
            progress: null!,
            cts.Token);
        await executionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => batchTask.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(releaseExecution.Task.IsCompleted);
        }
        finally
        {
            releaseExecution.TrySetResult();
            await executionFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task BacktestDependencyRunner_CancelledWait_ObservesLateFault()
    {
        var dependencyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dependency = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedFault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();

        var runnerTask = BacktestDependencyRunner.RunAsync(
            () =>
            {
                dependencyStarted.TrySetResult();
                return dependency.Task;
            },
            cts.Token,
            ex => observedFault.TrySetResult(ex));

        await dependencyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => runnerTask.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(dependency.Task.IsCompleted);

            dependency.TrySetException(new InvalidOperationException("late dependency fault"));
            var observed = await observedFault.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("late dependency fault", observed.Message);
        }
        finally
        {
            dependency.TrySetCanceled();
        }
    }

    [Fact]
    public async Task RunBatchAsync_ConcurrentProgressReports_AreSerializedAndMonotonic()
    {
        const int runCount = 8;
        var executionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExecutions = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedCount = 0;
        var progress = new ConcurrentProgressProbe();
        var service = new BatchBacktestService(
            NullLogger<BatchBacktestService>.Instance,
            _ => null!,
            async (_, runRequest, _, ct) =>
            {
                if (Interlocked.Increment(ref startedCount) == runCount)
                    executionStarted.TrySetResult();

                await releaseExecutions.Task.WaitAsync(ct);
                return CreateResult(runRequest, 1m);
            });

        var batchTask = service.RunBatchAsync(
            CreateBatchRequest(runCount, maxConcurrency: runCount),
            progress,
            CancellationToken.None);
        await executionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseExecutions.TrySetResult();

        await batchTask;

        var completedValues = progress.CompletedValues.ToArray();
        Assert.Equal(1, progress.MaximumConcurrentReports);
        Assert.Equal(runCount * 2, completedValues.Length);
        Assert.Equal(runCount, completedValues[^1]);
        Assert.True(completedValues.SequenceEqual(completedValues.Order()));
    }

    [Fact]
    public async Task RunBatchAsync_BlockingProgressCallback_DoesNotHoldCoordinatorLockOrPreventAnotherRunFromStarting()
    {
        var firstReportEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstReport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var anotherRunStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reportCount = 0;
        var progress = new RecordingProgress<BatchBacktestProgress>(_ =>
        {
            if (Interlocked.Increment(ref reportCount) != 1)
                return;

            firstReportEntered.TrySetResult();
            releaseFirstReport.Task.GetAwaiter().GetResult();
        });
        var service = new BatchBacktestService(
            NullLogger<BatchBacktestService>.Instance,
            _ => null!,
            (_, runRequest, _, _) =>
            {
                anotherRunStarted.TrySetResult();
                return Task.FromResult(CreateResult(runRequest, 1m));
            });

        var batchTask = Task.Run(() => service.RunBatchAsync(
            CreateBatchRequest(runCount: 2, maxConcurrency: 2),
            progress,
            CancellationToken.None));

        await firstReportEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await anotherRunStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseFirstReport.TrySetResult();
        }

        await batchTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RunBatchAsync_ParameterLabelFormattingFails_RecordsOnlyThatRunAsFailed()
    {
        var executionCount = 0;
        var service = new BatchBacktestService(
            NullLogger<BatchBacktestService>.Instance,
            _ => null!,
            (_, runRequest, _, _) =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.FromResult(CreateResult(runRequest, 1m));
            });
        var request = new BatchBacktestRequest
        {
            BaseRequest = CreateBaseRequest(),
            ParameterGrid =
            [
                new Dictionary<string, object>
                {
                    ["ExplosiveLabel"] = new ThrowingFormattable(),
                    ["Weight"] = 1m
                },
                new Dictionary<string, object> { ["Weight"] = 2m }
            ],
            MaxConcurrency = 2,
            StrategyDescriptor = "label-isolation",
            StrategyFactory = static parameters => new ParameterizedTestStrategy(Convert.ToDecimal(parameters["Weight"]))
        };

        var summary = await service.RunBatchAsync(request, progress: null!, CancellationToken.None);

        Assert.Equal(2, summary.Runs.Count);
        Assert.Null(summary.Runs[0].Result);
        Assert.Equal("label formatting failed", summary.Runs[0].ErrorMessage);
        Assert.NotNull(summary.Runs[1].Result);
        Assert.Null(summary.Runs[1].ErrorMessage);
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public void ApplyParameters_NumericStrings_AreParsedUsingInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var request = BatchBacktestService.ApplyParameters(
                CreateBaseRequest(),
                new Dictionary<string, object>
                {
                    [nameof(BacktestRequest.InitialCash)] = "1.234",
                    [nameof(BacktestRequest.AnnualMarginRate)] = "0.125"
                });

            Assert.Equal(1.234m, request.InitialCash);
            Assert.Equal(0.125, request.AnnualMarginRate);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static BatchBacktestRequest CreateBatchRequest(int runCount, int maxConcurrency) => new()
    {
        BaseRequest = CreateBaseRequest(),
        ParameterGrid = Enumerable.Range(1, runCount)
            .Select(static weight => new Dictionary<string, object> { ["Weight"] = (decimal)weight })
            .ToArray(),
        MaxConcurrency = maxConcurrency,
        StrategyDescriptor = "cancellation-test",
        StrategyFactory = static parameters => new ParameterizedTestStrategy(Convert.ToDecimal(parameters["Weight"]))
    };

    private static BacktestRequest CreateBaseRequest()
        => new(DateOnly.Parse("2025-01-01"), DateOnly.Parse("2025-01-10"), ["SPY"], 500m, 0.08, DataRoot: "/tmp");

    private static BacktestResult CreateResult(BacktestRequest request, decimal weight)
    {
        var netPnl = request.InitialCash * weight;
        var metrics = new BacktestMetrics(
            request.InitialCash,
            request.InitialCash + netPnl,
            netPnl,
            netPnl,
            request.InitialCash == 0 ? 0 : netPnl / request.InitialCash,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            new Dictionary<string, SymbolAttribution>());

        return new BacktestResult(request, new HashSet<string>(), [], [], [], metrics, new BacktestLedger(), TimeSpan.Zero, 0);
    }

    private sealed class ParameterizedTestStrategy(decimal weight) : IBacktestStrategy
    {
        public decimal Weight { get; } = weight;
        public string Name => $"Weighted-{Weight}";
        public void Initialize(IBacktestContext ctx) { }
        public void OnTrade(Trade trade, IBacktestContext ctx) { }
        public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
        public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
        public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
        public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
        public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
        public void OnFinished(IBacktestContext ctx) { }
    }

    private sealed class ConcurrentProgressProbe : IProgress<BatchBacktestProgress>
    {
        private readonly ConcurrentQueue<int> _completedValues = new();
        private int _activeReports;
        private int _maximumConcurrentReports;

        public IReadOnlyCollection<int> CompletedValues => _completedValues;
        public int MaximumConcurrentReports => Volatile.Read(ref _maximumConcurrentReports);

        public void Report(BatchBacktestProgress value)
        {
            var activeReports = Interlocked.Increment(ref _activeReports);
            try
            {
                UpdateMaximum(activeReports);
                _completedValues.Enqueue(value.Completed);
                Thread.Sleep(TimeSpan.FromMilliseconds(10));
            }
            finally
            {
                Interlocked.Decrement(ref _activeReports);
            }
        }

        private void UpdateMaximum(int candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maximumConcurrentReports);
                if (candidate <= current ||
                    Interlocked.CompareExchange(ref _maximumConcurrentReports, candidate, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class ThrowingFormattable : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) =>
            throw new FormatException("label formatting failed");

        public override string ToString() => throw new FormatException("label formatting failed");
    }

    private sealed class RecordingProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
