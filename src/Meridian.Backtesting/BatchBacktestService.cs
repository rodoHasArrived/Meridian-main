using System.Diagnostics;
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
}

/// <summary>
/// Batch backtest service implementation using SemaphoreSlim for concurrency control.
/// Runs multiple backtests in parallel, catching per-run exceptions without aborting the batch.
/// </summary>
public sealed class BatchBacktestService(
    ILogger<BatchBacktestService> logger,
    BacktestEngine engine) : IBatchBacktestService
{
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

        var sw = Stopwatch.StartNew();
        var total = request.ParameterGrid.Count;
        var completedCounter = new int[1];
        var runs = new List<BatchBacktestRun>();
        var semaphore = new SemaphoreSlim(request.MaxConcurrency, request.MaxConcurrency);

        logger.LogInformation("Starting batch backtest with {Total} runs, max concurrency {MaxConcurrency}",
            total, request.MaxConcurrency);

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

        logger.LogInformation("Batch backtest completed in {Duration}. {Completed} succeeded, {Failed} failed",
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

            logger.LogInformation("Starting run {Index}/{Total}: {Label}", index + 1, total, label);

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
                // Create a simple strategy that does nothing (caller can extend with parameter-driven logic)
                var strategy = new NoOpStrategy();

                result = await Task.Run(async () =>
                    await engine.RunAsync(request.BaseRequest, strategy, null, ct), ct);

                logger.LogInformation("Run {Index}/{Total} succeeded in {Duration}ms",
                    index + 1, total, runSw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                logger.LogWarning(ex, "Run {Index}/{Total} failed: {Error}", index + 1, total, ex.Message);
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
    /// Minimal no-op strategy for batch runs (caller can subclass to add parameter-driven logic).
    /// </summary>
    private sealed class NoOpStrategy : IBacktestStrategy
    {
        public string Name => "NoOp";

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
