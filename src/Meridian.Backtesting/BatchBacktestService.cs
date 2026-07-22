using System.Diagnostics;
using System.Globalization;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;

namespace Meridian.Backtesting;

/// <summary>
/// Batch backtest service contract for parameter grid sweeping.
/// </summary>
public interface IBatchBacktestService
{
    /// <summary>
    /// Runs multiple backtests with different parameter combinations in parallel, respecting MaxConcurrency.
    /// </summary>
    Task<BatchBacktestSummary> RunBatchAsync(BatchBacktestRequest request,
        IProgress<BatchBacktestProgress> progress, CancellationToken ct);
}

/// <summary>
/// Single run within a batch backtest sweep.
/// </summary>
public sealed class BatchBacktestRun
{
    /// <summary>Parameter values used in this run.</summary>
    public required Dictionary<string, object> Parameters { get; init; }

    /// <summary>Backtest result, or null if the run failed.</summary>
    public BacktestResult? Result { get; init; }

    /// <summary>Duration in milliseconds.</summary>
    public long DurationMs { get; init; }

    /// <summary>Error message if the run failed, null otherwise.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Summary of a completed batch backtest.
/// </summary>
public sealed class BatchBacktestSummary
{
    /// <summary>All runs in the batch.</summary>
    public required IReadOnlyList<BatchBacktestRun> Runs { get; init; }

    /// <summary>Total elapsed time for the entire batch.</summary>
    public required TimeSpan TotalDuration { get; init; }
}

/// <summary>
/// Progress report during batch backtest execution.
/// </summary>
public sealed class BatchBacktestProgress
{
    /// <summary>Number of completed runs.</summary>
    public int Completed { get; init; }

    /// <summary>Total number of runs in the batch.</summary>
    public int Total { get; init; }

    /// <summary>Human-readable label for the current run.</summary>
    public string CurrentLabel { get; init; } = "";
}

/// <summary>
/// Request to run multiple backtests with a parameter grid.
/// </summary>
public sealed class BatchBacktestRequest
{
    /// <summary>Base backtest request (will be cloned and parameters applied per run).</summary>
    public required BacktestRequest BaseRequest { get; init; }

    /// <summary>List of parameter dictionaries, one per backtest run.</summary>
    public required IReadOnlyList<Dictionary<string, object>> ParameterGrid { get; init; }

    /// <summary>Maximum number of concurrent backtests. Defaults to ProcessorCount - 1.</summary>
    public int MaxConcurrency { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>Human-readable descriptor for the strategy used in each run.</summary>
    public required string StrategyDescriptor { get; init; }

    /// <summary>Builds a strategy instance for each parameter set in the sweep.</summary>
    public required Func<IReadOnlyDictionary<string, object>, IBacktestStrategy> StrategyFactory { get; init; }
}

/// <summary>
/// Batch backtest service implementation using SemaphoreSlim for concurrency control.
/// Runs multiple backtests in parallel, catching per-run exceptions without aborting the batch.
/// </summary>
public sealed class BatchBacktestService : IBatchBacktestService
{
    private readonly Func<BacktestEngine, BacktestRequest, IBacktestStrategy, CancellationToken, Task<BacktestResult>> _runExecutor;
    private readonly ILogger<BatchBacktestService> _logger;
    private readonly Func<BacktestRequest, BacktestEngine> _engineFactory;

    public BatchBacktestService(
        ILogger<BatchBacktestService> logger,
        BacktestEngine engine)
        : this(logger, _ => engine)
    {
    }

    public BatchBacktestService(
        ILogger<BatchBacktestService> logger,
        Func<BacktestRequest, BacktestEngine> engineFactory)
        : this(
            logger,
            engineFactory,
            static (engine, runRequest, strategy, ct) => engine.RunAsync(runRequest, strategy, null, ct))
    {
    }

    internal BatchBacktestService(
        ILogger<BatchBacktestService> logger,
        Func<BacktestRequest, BacktestEngine> engineFactory,
        Func<BacktestEngine, BacktestRequest, IBacktestStrategy, CancellationToken, Task<BacktestResult>> runExecutor)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
        _runExecutor = runExecutor ?? throw new ArgumentNullException(nameof(runExecutor));
    }

    /// <summary>
    /// Runs a batch of backtests with parameter sweeping.
    /// Each run exception is caught and recorded; the batch continues to completion.
    /// </summary>
    public async Task<BatchBacktestSummary> RunBatchAsync(BatchBacktestRequest request,
        IProgress<BatchBacktestProgress> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.BaseRequest);
        ArgumentNullException.ThrowIfNull(request.ParameterGrid);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StrategyDescriptor);
        ArgumentNullException.ThrowIfNull(request.StrategyFactory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.MaxConcurrency);

        var sw = Stopwatch.StartNew();
        var total = request.ParameterGrid.Count;
        var completedCounter = new int[1];
        var runs = new List<BatchBacktestRun>();
        var semaphore = new SemaphoreSlim(request.MaxConcurrency, request.MaxConcurrency);

        _logger.LogInformation("Starting batch backtest with {Total} runs, strategy {Strategy}, max concurrency {MaxConcurrency}",
            total, request.StrategyDescriptor, request.MaxConcurrency);

        var tasks = request.ParameterGrid.Select((paramSet, index) =>
            RunSingleBacktestAsync(index, paramSet, request, semaphore, runs, completedCounter, total, progress, ct)
        ).ToList();

        await Task.WhenAll(tasks);

        sw.Stop();

        var summary = new BatchBacktestSummary
        {
            Runs = runs.AsReadOnly(),
            TotalDuration = sw.Elapsed
        };

        _logger.LogInformation("Batch backtest completed in {Duration}. {Completed} succeeded, {Failed} failed",
            sw.Elapsed,
            summary.Runs.Count(r => r.Result != null),
            summary.Runs.Count(r => r.ErrorMessage != null));

        return summary;
    }

    private async Task RunSingleBacktestAsync(
        int index,
        Dictionary<string, object> paramSet,
        BatchBacktestRequest request,
        SemaphoreSlim semaphore,
        List<BatchBacktestRun> runs,
        int[] completedCounter,
        int total,
        IProgress<BatchBacktestProgress> progress,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);

        try
        {
            var runSw = Stopwatch.StartNew();
            var label = FormatParameterLabel(paramSet);

            _logger.LogInformation("Starting run {Index}/{Total}: {Label}", index + 1, total, label);

            progress?.Report(new BatchBacktestProgress
            {
                Completed = completedCounter[0],
                Total = total,
                CurrentLabel = label
            });

            BacktestResult? result = null;
            string? errorMessage = null;

            try
            {
                var runRequest = ApplyParameters(request.BaseRequest, paramSet);
                var engine = _engineFactory(runRequest);
                var strategy = request.StrategyFactory(paramSet);

                result = await Task.Run(async () =>
                    await _runExecutor(engine, runRequest, strategy, ct).ConfigureAwait(false), ct)
                    .ConfigureAwait(false);

                _logger.LogInformation("Run {Index}/{Total} succeeded in {Duration}ms",
                    index + 1, total, runSw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                _logger.LogWarning(ex, "Run {Index}/{Total} failed: {Error}", index + 1, total, ex.Message);
            }

            runSw.Stop();
            var currentCompleted = completedCounter[0];

            lock (runs)
            {
                runs.Add(new BatchBacktestRun
                {
                    Parameters = paramSet,
                    Result = result,
                    DurationMs = runSw.ElapsedMilliseconds,
                    ErrorMessage = errorMessage
                });

                completedCounter[0]++;
                currentCompleted = completedCounter[0];
            }

            progress?.Report(new BatchBacktestProgress
            {
                Completed = currentCompleted,
                Total = total,
                CurrentLabel = label
            });
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static string FormatParameterLabel(Dictionary<string, object> paramSet)
    {
        var parts = paramSet.Select(kvp => $"{kvp.Key}={kvp.Value}");
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Applies a parameter dictionary to a base request, returning a new request. Shared with the
    /// walk-forward harness so training and out-of-sample runs interpret parameters identically.
    /// </summary>
    internal static BacktestRequest ApplyParameters(
        BacktestRequest baseRequest,
        IReadOnlyDictionary<string, object> parameters)
    {
        var request = baseRequest;

        foreach (var (key, value) in parameters)
        {
            request = key switch
            {
                nameof(BacktestRequest.InitialCash) => request with { InitialCash = ToDecimal(value) },
                nameof(BacktestRequest.AnnualMarginRate) => request with { AnnualMarginRate = ToDouble(value) },
                nameof(BacktestRequest.AnnualShortRebateRate) => request with { AnnualShortRebateRate = ToDouble(value) },
                nameof(BacktestRequest.SlippageBasisPoints) => request with { SlippageBasisPoints = ToDecimal(value) },
                nameof(BacktestRequest.CommissionRate) => request with { CommissionRate = ToDecimal(value) },
                nameof(BacktestRequest.CommissionMinimum) => request with { CommissionMinimum = ToDecimal(value) },
                nameof(BacktestRequest.CommissionMaximum) => request with { CommissionMaximum = ToDecimal(value) },
                nameof(BacktestRequest.MarketImpactCoefficient) => request with { MarketImpactCoefficient = ToDecimal(value) },
                nameof(BacktestRequest.RiskFreeRate) => request with { RiskFreeRate = ToDouble(value) },
                nameof(BacktestRequest.MaxParticipationRate) => request with { MaxParticipationRate = ToDecimal(value) },
                nameof(BacktestRequest.DefaultExecutionModel) => request with { DefaultExecutionModel = ToEnum<ExecutionModel>(value) },
                nameof(BacktestRequest.CommissionKind) => request with { CommissionKind = ToEnum<BacktestCommissionKind>(value) },
                nameof(BacktestRequest.AdjustForCorporateActions) => request with { AdjustForCorporateActions = ToBool(value) },
                nameof(BacktestRequest.FailOnUnknownSymbols) => request with { FailOnUnknownSymbols = ToBool(value) },
                nameof(BacktestRequest.FillTiming) => request with { FillTiming = ToEnum<FillTiming>(value) },
                nameof(BacktestRequest.FillConservatism) => request with { FillConservatism = ToEnum<FillConservatism>(value) },
                nameof(BacktestRequest.DelistingPolicy) => request with { DelistingPolicy = ToEnum<DelistingPolicy>(value) },
                nameof(BacktestRequest.DelistingHaircutPercent) => request with { DelistingHaircutPercent = ToDecimal(value) },
                nameof(BacktestRequest.DelistingGraceDays) => request with { DelistingGraceDays = Convert.ToInt32(value, CultureInfo.InvariantCulture) },
                _ => request
            };
        }

        return request;
    }

    private static decimal ToDecimal(object value)
        => value switch
        {
            decimal decimalValue => decimalValue,
            double doubleValue => (decimal)doubleValue,
            float floatValue => (decimal)floatValue,
            int intValue => intValue,
            long longValue => longValue,
            string stringValue when decimal.TryParse(stringValue, out var parsed) => parsed,
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
        };

    private static double ToDouble(object value)
        => value switch
        {
            double doubleValue => doubleValue,
            decimal decimalValue => decimal.ToDouble(decimalValue),
            float floatValue => floatValue,
            string stringValue when double.TryParse(stringValue, out var parsed) => parsed,
            _ => Convert.ToDouble(value, CultureInfo.InvariantCulture)
        };

    private static bool ToBool(object value)
        => value switch
        {
            bool boolValue => boolValue,
            string stringValue when bool.TryParse(stringValue, out var parsed) => parsed,
            _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture)
        };

    private static TEnum ToEnum<TEnum>(object value)
        where TEnum : struct, Enum
        => value switch
        {
            TEnum typedValue => typedValue,
            string stringValue when Enum.TryParse<TEnum>(stringValue, ignoreCase: true, out var parsed) => parsed,
            _ => (TEnum)Enum.ToObject(typeof(TEnum), value)
        };

}
