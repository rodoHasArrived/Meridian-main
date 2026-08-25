using System.Diagnostics;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;

namespace Meridian.Backtesting.WalkForward;

/// <summary>
/// Walk-forward / out-of-sample validation harness around the parameter-grid sweep.
/// </summary>
public interface IWalkForwardService
{
    /// <summary>
    /// Runs the full walk-forward sweep: per window, sweep the grid on the training range, select
    /// the best parameter set by the configured objective, run it once on the unseen test range,
    /// then stitch all test windows into aggregate out-of-sample metrics.
    /// </summary>
    Task<WalkForwardReport> RunAsync(
        WalkForwardRequest request,
        IProgress<WalkForwardProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="IWalkForwardService"/> implementation. Training sweeps reuse
/// <see cref="IBatchBacktestService"/>; out-of-sample runs go through the same parameter
/// application path (<see cref="BatchBacktestService.ApplyParameters"/>) so a parameter means the
/// same thing in-sample and out-of-sample.
/// </summary>
public sealed class WalkForwardService : IWalkForwardService
{
    private readonly ILogger<WalkForwardService> _logger;
    private readonly IBatchBacktestService _batchService;
    private readonly Func<BacktestRequest, BacktestEngine> _engineFactory;
    private readonly Func<BacktestEngine, BacktestRequest, IBacktestStrategy, CancellationToken, Task<BacktestResult>> _runExecutor;

    public WalkForwardService(
        ILogger<WalkForwardService> logger,
        IBatchBacktestService batchService,
        Func<BacktestRequest, BacktestEngine> engineFactory)
        : this(
            logger,
            batchService,
            engineFactory,
            static (engine, runRequest, strategy, ct) => engine.RunAsync(runRequest, strategy, null, ct))
    {
    }

    internal WalkForwardService(
        ILogger<WalkForwardService> logger,
        IBatchBacktestService batchService,
        Func<BacktestRequest, BacktestEngine> engineFactory,
        Func<BacktestEngine, BacktestRequest, IBacktestStrategy, CancellationToken, Task<BacktestResult>> runExecutor)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _batchService = batchService ?? throw new ArgumentNullException(nameof(batchService));
        _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
        _runExecutor = runExecutor ?? throw new ArgumentNullException(nameof(runExecutor));
    }

    /// <summary>
    /// Splits an inclusive date range into walk-forward windows. Exposed for testability and for
    /// UI layers that want to preview the window layout before launching a sweep.
    /// </summary>
    public static IReadOnlyList<WalkForwardWindow> BuildWindows(
        DateOnly from,
        DateOnly to,
        int trainingDays,
        int testDays,
        int stepDays = 0,
        bool anchoredTraining = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(trainingDays, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(testDays, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(stepDays);
        if (stepDays > 0 && stepDays < testDays)
        {
            throw new ArgumentException(
                $"Step of {stepDays} day(s) is shorter than the {testDays}-day test window, which would make " +
                "out-of-sample windows overlap and double-count days in the stitched OOS metrics. Use a step of " +
                "at least the test-window length (or 0 for contiguous windows).",
                nameof(stepDays));
        }
        if (to < from)
            throw new ArgumentException($"Walk-forward range end {to:yyyy-MM-dd} precedes start {from:yyyy-MM-dd}.", nameof(to));

        var step = stepDays > 0 ? stepDays : testDays;
        var windows = new List<WalkForwardWindow>();

        for (var index = 0; ; index++)
        {
            var offset = index * step;
            var trainFrom = anchoredTraining ? from : from.AddDays(offset);
            var trainTo = from.AddDays(offset + trainingDays - 1);
            var testFrom = trainTo.AddDays(1);
            if (testFrom > to)
                break;

            var testTo = testFrom.AddDays(testDays - 1);
            if (testTo > to)
                testTo = to;

            windows.Add(new WalkForwardWindow(index, trainFrom, trainTo, testFrom, testTo));

            if (testTo == to)
                break;
        }

        return windows;
    }

    public async Task<WalkForwardReport> RunAsync(
        WalkForwardRequest request,
        IProgress<WalkForwardProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.BaseRequest);
        ArgumentNullException.ThrowIfNull(request.ParameterGrid);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StrategyDescriptor);
        ArgumentNullException.ThrowIfNull(request.StrategyFactory);
        if (request.ParameterGrid.Count == 0)
            throw new ArgumentException("Walk-forward requires a non-empty parameter grid.", nameof(request));

        var windows = BuildWindows(
            request.BaseRequest.From,
            request.BaseRequest.To,
            request.TrainingDays,
            request.TestDays,
            request.StepDays,
            request.AnchoredTraining);

        if (windows.Count == 0)
        {
            throw new ArgumentException(
                $"Backtest range {request.BaseRequest.From:yyyy-MM-dd}..{request.BaseRequest.To:yyyy-MM-dd} is too short " +
                $"for a {request.TrainingDays}-day training window plus at least one out-of-sample test day.",
                nameof(request));
        }

        var objective = ResolveObjective(request);
        var parameterGridIndex = new ParameterGridIndex(request.ParameterGrid, ct);
        var sw = Stopwatch.StartNew();
        var windowResults = new List<WalkForwardWindowResult>(windows.Count);

        _logger.LogInformation(
            "Walk-forward sweep: {Windows} windows ({TrainingDays}d train / {TestDays}d test, anchored={Anchored}), {GridSize} parameter sets, objective {Objective}",
            windows.Count, request.TrainingDays, request.TestDays, request.AnchoredTraining, request.ParameterGrid.Count, request.Objective);

        foreach (var window in windows)
        {
            ct.ThrowIfCancellationRequested();
            var windowResult = await RunWindowAsync(
                    request,
                    window,
                    windows.Count,
                    objective,
                    parameterGridIndex,
                    progress,
                    ct)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            windowResults.Add(windowResult);
        }

        ct.ThrowIfCancellationRequested();
        sw.Stop();
        return BuildReport(request, windows, windowResults, sw.Elapsed);
    }

    // ── Per-window execution ─────────────────────────────────────────────────

    private async Task<WalkForwardWindowResult> RunWindowAsync(
        WalkForwardRequest request,
        WalkForwardWindow window,
        int windowCount,
        Func<BacktestMetrics, double> objective,
        ParameterGridIndex parameterGridIndex,
        IProgress<WalkForwardProgress>? progress,
        CancellationToken ct)
    {
        var trainRequest = request.BaseRequest with { From = window.TrainFrom, To = window.TrainTo };

        var batchProgress = progress is null
            ? null
            : new InlineProgress<BatchBacktestProgress>(p => progress.Report(new WalkForwardProgress
            {
                Phase = "training",
                WindowIndex = window.Index,
                WindowCount = windowCount,
                CompletedRuns = p.Completed,
                TotalRuns = p.Total,
                CurrentLabel = p.CurrentLabel
            }));

        var batchRequest = new BatchBacktestRequest
        {
            BaseRequest = trainRequest,
            ParameterGrid = request.ParameterGrid,
            MaxConcurrency = request.MaxConcurrency,
            StrategyDescriptor = request.StrategyDescriptor,
            StrategyFactory = request.StrategyFactory
        };
        var batch = await BacktestDependencyRunner.RunAsync(
                () => _batchService.RunBatchAsync(batchRequest, batchProgress!, ct),
                ct,
                ex => _logger.LogWarning(
                    ex,
                    "Walk-forward window {Index} training batch faulted after caller cancellation",
                    window.Index))
            .ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var succeeded = batch.Runs.Where(static run => run.Result is not null).ToList();
        var failed = batch.Runs.Count - succeeded.Count;

        var best = succeeded
            .Select(run => (
                Run: run,
                Objective: objective(run.Result!.Metrics),
                ParameterGridIndex: parameterGridIndex.Resolve(run.Parameters),
                ParameterLabel: BatchBacktestService.FormatParameterLabel(run.Parameters)))
            .Where(static candidate => !double.IsNaN(candidate.Objective))
            .OrderByDescending(static candidate => candidate.Objective)
            .ThenBy(static candidate => candidate.ParameterGridIndex)
            .ThenBy(static candidate => candidate.ParameterLabel, StringComparer.Ordinal)
            .FirstOrDefault();

        if (best.Run is null)
        {
            _logger.LogWarning(
                "Walk-forward window {Index} ({TrainFrom}..{TrainTo}): no training run produced a usable objective; skipping out-of-sample test",
                window.Index, window.TrainFrom, window.TrainTo);

            return new WalkForwardWindowResult
            {
                Window = window,
                TrainRunsSucceeded = succeeded.Count,
                TrainRunsFailed = failed,
                ErrorMessage = "No training run produced a usable objective value."
            };
        }

        progress?.Report(new WalkForwardProgress
        {
            Phase = "testing",
            WindowIndex = window.Index,
            WindowCount = windowCount,
            CurrentLabel = FormatParameterLabel(best.Run.Parameters)
        });
        ct.ThrowIfCancellationRequested();

        var testRequest = BatchBacktestService.ApplyParameters(
            request.BaseRequest with { From = window.TestFrom, To = window.TestTo },
            best.Run.Parameters);

        try
        {
            var testResult = await BacktestDependencyRunner.RunAsync(
                    () =>
                    {
                        var engine = _engineFactory(testRequest);
                        var strategy = request.StrategyFactory(best.Run.Parameters);
                        return _runExecutor(engine, testRequest, strategy, ct);
                    },
                    ct,
                    ex => _logger.LogWarning(
                        ex,
                        "Walk-forward window {Index} out-of-sample run faulted after caller cancellation",
                        window.Index))
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            return new WalkForwardWindowResult
            {
                Window = window,
                SelectedParameters = best.Run.Parameters,
                TrainMetrics = best.Run.Result!.Metrics,
                TrainObjective = best.Objective,
                TestResult = testResult,
                TestObjective = objective(testResult.Metrics),
                TrainRunsSucceeded = succeeded.Count,
                TrainRunsFailed = failed
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Walk-forward window {Index}: out-of-sample run {TestFrom}..{TestTo} failed",
                window.Index, window.TestFrom, window.TestTo);

            return new WalkForwardWindowResult
            {
                Window = window,
                SelectedParameters = best.Run.Parameters,
                TrainMetrics = best.Run.Result!.Metrics,
                TrainObjective = best.Objective,
                TrainRunsSucceeded = succeeded.Count,
                TrainRunsFailed = failed,
                ErrorMessage = ex.Message
            };
        }
    }

    // ── Aggregation ──────────────────────────────────────────────────────────

    private WalkForwardReport BuildReport(
        WalkForwardRequest request,
        IReadOnlyList<WalkForwardWindow> windows,
        IReadOnlyList<WalkForwardWindowResult> windowResults,
        TimeSpan elapsed)
    {
        var completed = windowResults.Where(static w => w.TestResult is not null).ToList();
        var oosMetrics = ComputeStitchedOosMetrics(completed, request.BaseRequest.RiskFreeRate);

        var meanTrain = completed.Count > 0 ? completed.Average(static w => w.TrainObjective) : double.NaN;
        var meanTest = completed.Count > 0 ? completed.Average(static w => w.TestObjective) : double.NaN;
        var degradation = meanTrain > 0 ? meanTest / meanTrain : double.NaN;

        var disclosures = new List<BiasDisclosureItem>
        {
            new(
                "walk-forward", BiasSeverity.Info, "Out-of-sample methodology",
                $"Parameters were re-selected on each of {windows.Count} training windows ({request.TrainingDays}d, " +
                $"{(request.AnchoredTraining ? "anchored" : "rolling")}) by {request.Objective} and evaluated once on the adjacent unseen " +
                $"{request.TestDays}d test window. Stitched OOS metrics re-normalize capital at every window boundary, so " +
                "cross-window compounding and drawdowns that span a boundary are approximated.")
        };

        var lastWindowDays = windows[^1].TestTo.DayNumber - windows[^1].TestFrom.DayNumber + 1;
        if (lastWindowDays < request.TestDays)
        {
            disclosures.Add(new BiasDisclosureItem(
                "walk-forward-truncated-window", BiasSeverity.Info, "Final test window truncated",
                $"The last test window covers only {lastWindowDays} of {request.TestDays} days because the requested range ends on {windows[^1].TestTo:yyyy-MM-dd}."));
        }

        var failedWindows = windowResults.Count - completed.Count;
        if (failedWindows > 0)
        {
            disclosures.Add(new BiasDisclosureItem(
                "walk-forward-failed-windows", BiasSeverity.Warning, $"{failedWindows} window(s) produced no out-of-sample result",
                "Stitched OOS metrics cover only the completed windows; the missing periods are unrepresented, which can flatter or damage the aggregate."));
        }

        if (!double.IsNaN(degradation) && degradation < 0.5)
        {
            disclosures.Add(new BiasDisclosureItem(
                "walk-forward-degradation", BiasSeverity.Warning, "Severe in-sample → out-of-sample degradation",
                $"The mean out-of-sample objective is {degradation:P0} of the mean training objective. The parameter grid is likely fitting noise rather than signal."));
        }

        _logger.LogInformation(
            "Walk-forward sweep complete in {Elapsed}: {Completed}/{Total} windows, mean train objective {MeanTrain:F3}, mean OOS objective {MeanTest:F3}",
            elapsed, completed.Count, windowResults.Count, meanTrain, meanTest);

        return new WalkForwardReport
        {
            Windows = windowResults,
            OosMetrics = oosMetrics,
            MeanTrainObjective = meanTrain,
            MeanTestObjective = meanTest,
            DegradationRatio = degradation,
            TotalDuration = elapsed,
            Disclosures = disclosures
        };
    }

    /// <summary>
    /// Computes aggregate OOS metrics from the daily-return series of every completed test window,
    /// concatenated in chronological order with capital re-normalized at each boundary. Sharpe uses
    /// the same calendar-day excess-return convention as <c>BacktestMetricsEngine</c>
    /// (annual rate / 365, annualized by √365).
    /// </summary>
    internal static WalkForwardOosMetrics? ComputeStitchedOosMetrics(
        IReadOnlyList<WalkForwardWindowResult> completedWindows,
        double annualRiskFreeRate)
    {
        var dailyReturns = new List<double>();
        var totalTrades = 0;
        var totalCommissions = 0m;

        foreach (var window in completedWindows.OrderBy(static w => w.Window.TestFrom))
        {
            var result = window.TestResult!;
            dailyReturns.AddRange(result.Snapshots.Select(static s => (double)s.DailyReturn));
            totalTrades += result.Metrics.TotalTrades;
            totalCommissions += result.Metrics.TotalCommissions;
        }

        if (dailyReturns.Count == 0)
            return null;

        var cumulative = 1.0;
        var peak = 1.0;
        var maxDrawdown = 0.0;
        foreach (var dailyReturn in dailyReturns)
        {
            cumulative *= 1.0 + dailyReturn;
            if (cumulative > peak)
                peak = cumulative;
            var drawdown = peak > 0 ? (peak - cumulative) / peak : 0.0;
            if (drawdown > maxDrawdown)
                maxDrawdown = drawdown;
        }

        var totalReturn = cumulative - 1.0;
        var annualizedReturn = cumulative > 0
            ? Math.Pow(cumulative, 365.0 / dailyReturns.Count) - 1.0
            : -1.0;

        var dailyRiskFree = annualRiskFreeRate / 365.0;
        var excess = dailyReturns.Select(r => r - dailyRiskFree).ToList();
        var mean = excess.Average();
        var std = excess.Count < 2
            ? 0.0
            : Math.Sqrt(excess.Sum(x => (x - mean) * (x - mean)) / (excess.Count - 1));
        var sharpe = std < 1e-10 ? 0.0 : mean / std * Math.Sqrt(365.0);

        return new WalkForwardOosMetrics(
            TotalReturn: (decimal)totalReturn,
            AnnualizedReturn: (decimal)annualizedReturn,
            SharpeRatio: sharpe,
            MaxDrawdownPercent: (decimal)maxDrawdown,
            TradingDays: dailyReturns.Count,
            TotalTrades: totalTrades,
            TotalCommissions: totalCommissions);
    }

    private static Func<BacktestMetrics, double> ResolveObjective(WalkForwardRequest request) =>
        request.ObjectiveSelector ?? request.Objective switch
        {
            WalkForwardObjective.SharpeRatio => static metrics => metrics.SharpeRatio,
            WalkForwardObjective.SortinoRatio => static metrics => metrics.SortinoRatio,
            WalkForwardObjective.CalmarRatio => static metrics => metrics.CalmarRatio,
            WalkForwardObjective.TotalReturn => static metrics => (double)metrics.TotalReturn,
            WalkForwardObjective.NetPnl => static metrics => (double)metrics.NetPnl,
            WalkForwardObjective.ProfitFactor => static metrics => metrics.ProfitFactor,
            _ => static metrics => metrics.SharpeRatio
        };

    private sealed class ParameterGridIndex
    {
        private readonly Dictionary<Dictionary<string, object>, int> _byReference =
            new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<IReadOnlyDictionary<string, object>, int> _byValue =
            new(ParameterSetComparer.Instance);

        public ParameterGridIndex(
            IReadOnlyList<Dictionary<string, object>> parameterGrid,
            CancellationToken ct)
        {
            for (var index = 0; index < parameterGrid.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var parameters = parameterGrid[index];
                _byReference.TryAdd(parameters, index);
                _byValue.TryAdd(parameters, index);
            }
        }

        public int Resolve(Dictionary<string, object> selectedParameters)
        {
            if (_byReference.TryGetValue(selectedParameters, out var index) ||
                _byValue.TryGetValue(selectedParameters, out index))
            {
                return index;
            }

            return int.MaxValue;
        }
    }

    private sealed class ParameterSetComparer : IEqualityComparer<IReadOnlyDictionary<string, object>>
    {
        public static ParameterSetComparer Instance { get; } = new();

        public bool Equals(
            IReadOnlyDictionary<string, object>? left,
            IReadOnlyDictionary<string, object>? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null || left.Count != right.Count)
                return false;

            using var leftEnumerator = left
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .GetEnumerator();
            using var rightEnumerator = right
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .GetEnumerator();

            while (leftEnumerator.MoveNext() && rightEnumerator.MoveNext())
            {
                if (!StringComparer.Ordinal.Equals(leftEnumerator.Current.Key, rightEnumerator.Current.Key) ||
                    !object.Equals(leftEnumerator.Current.Value, rightEnumerator.Current.Value))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(IReadOnlyDictionary<string, object> parameters)
        {
            unchecked
            {
                var hash = 17;
                foreach (var (key, value) in parameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(key);
                    hash = (hash * 31) + (value?.GetHashCode() ?? 0);
                }

                return hash;
            }
        }
    }

    private static string FormatParameterLabel(IReadOnlyDictionary<string, object> parameters) =>
        BatchBacktestService.FormatParameterLabel(parameters);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
