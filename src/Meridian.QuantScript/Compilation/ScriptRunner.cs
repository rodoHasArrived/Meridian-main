using System.Diagnostics;
using Meridian.QuantScript.Api;
using Meridian.QuantScript.Plotting;
using Meridian.QuantScript.Runtime;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Executes every QuantScript request in a dedicated, killable worker process. The host retains
/// only typed data-service RPC and result orchestration; Roslyn and user code never execute in the
/// UI/server process.
/// </summary>
public sealed class ScriptRunner : IScriptRunner
{
    private readonly IQuantDataContext _dataContext;
    private readonly QuantScriptOptions _options;
    private readonly ILogger<ScriptRunner> _logger;
    private readonly IQuantScriptWorkerClient _workerClient;
    private readonly SemaphoreSlim _workerSlots;
    private int _queuedWorkerRequests;

    public ScriptRunner(
        IQuantScriptCompiler compiler,
        IQuantDataContext dataContext,
        PlotQueue plotQueue,
        IOptions<QuantScriptOptions> options,
        ILogger<ScriptRunner> logger,
        Backtesting.Engine.BacktestEngine? backtestEngine = null)
        : this(
            compiler,
            dataContext,
            plotQueue,
            options,
            logger,
            backtestEngine,
            new QuantScriptWorkerClient(logger))
    {
    }

    internal ScriptRunner(
        IQuantScriptCompiler compiler,
        IQuantDataContext dataContext,
        PlotQueue plotQueue,
        IOptions<QuantScriptOptions> options,
        ILogger<ScriptRunner> logger,
        Backtesting.Engine.BacktestEngine? backtestEngine,
        IQuantScriptWorkerClient workerClient)
    {
        _ = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
        _ = plotQueue ?? throw new ArgumentNullException(nameof(plotQueue));
        _ = backtestEngine; // Worker-local backtests intentionally cannot retain a host engine object.
        _options = options?.Value ?? new QuantScriptOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _workerClient = workerClient ?? throw new ArgumentNullException(nameof(workerClient));
        _workerSlots = new SemaphoreSlim(_options.MaxConcurrentWorkers, _options.MaxConcurrentWorkers);
    }

    /// <inheritdoc/>
    public Task<ScriptRunResult> RunAsync(
        string source,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default)
        => ExecuteAsync(source, parameters, checkpoint: null, ct);

    /// <inheritdoc/>
    public Task<ScriptRunResult> ContinueWithAsync(
        string source,
        ScriptExecutionCheckpoint checkpoint,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return ExecuteAsync(source, parameters, checkpoint, ct);
    }

    private async Task<ScriptRunResult> ExecuteAsync(
        string source,
        IReadOnlyDictionary<string, object?>? parameters,
        ScriptExecutionCheckpoint? checkpoint,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ct.ThrowIfCancellationRequested();
        var wallClock = Stopwatch.StartNew();

        if (checkpoint is { IsReplayable: false })
        {
            return CreateIsolationFailure(
                wallClock,
                checkpoint,
                "The supplied checkpoint predates isolated execution and cannot be replayed safely.");
        }

        WorkerScriptCell currentCell;
        string dataRoot;
        try
        {
            currentCell = CreateCell(source, parameters);
            dataRoot = Path.GetFullPath(_options.DefaultDataRoot);
        }
        catch (Exception ex) when (ex is WorkerProtocolException or ArgumentException or NotSupportedException or IOException)
        {
            return CreateIsolationFailure(wallClock, checkpoint, ex.Message);
        }

        var replayCells = checkpoint?.ReplayCells ?? Array.Empty<WorkerScriptCell>();
        var request = new WorkerExecutionRequest(
            replayCells,
            currentCell,
            new WorkerRunOptions(
                _options.CompilationTimeoutSeconds,
                _options.EnableUnsafeScripts,
                _options.MaxCachedCompilations,
                _options.MaxPlotsPerRun,
                dataRoot,
                _options.MaxRunElapsedMilliseconds,
                _options.MaxOutputItemsPerRun,
                _options.MaxWorkerProtocolBytes));

        WorkerExecutionOutcome outcome;
        var workerSlotAcquired = false;
        try
        {
            workerSlotAcquired = await TryAcquireWorkerSlotAsync(ct).ConfigureAwait(false);
            if (!workerSlotAcquired)
            {
                outcome = new WorkerExecutionOutcome(
                    WorkerCompletionKind.AdmissionRejected,
                    null,
                    0,
                    "QuantScript worker admission queue was full or its bounded wait expired.");
            }
            else
            {
                outcome = await _workerClient.ExecuteAsync(
                    request,
                    _dataContext,
                    _options,
                    ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = new WorkerExecutionOutcome(WorkerCompletionKind.Cancelled, null, 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QuantScript worker orchestration failed closed");
            outcome = new WorkerExecutionOutcome(
                WorkerCompletionKind.ProtocolFailure,
                null,
                0,
                ex.Message);
        }
        finally
        {
            if (workerSlotAcquired)
                _workerSlots.Release();
        }

        wallClock.Stop();
        if (outcome.Kind == WorkerCompletionKind.Completed && outcome.Result is not null)
        {
            var nextCheckpoint = outcome.Result.Success
                ? new ScriptExecutionCheckpoint(replayCells.Concat([currentCell]).ToArray())
                : checkpoint;
            return outcome.Result.ToScriptRunResult(outcome.PeakMemoryBytes, nextCheckpoint);
        }

        if (!string.IsNullOrWhiteSpace(outcome.Detail))
        {
            _logger.LogWarning(
                "QuantScript isolated worker ended with {Completion}: {Detail}",
                outcome.Kind,
                outcome.Detail);
        }

        return CreateTerminalResult(wallClock.Elapsed, checkpoint, outcome);
    }

    private static WorkerScriptCell CreateCell(
        string source,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        var values = new Dictionary<string, WorkerParameterValue>(StringComparer.OrdinalIgnoreCase);
        if (parameters is not null)
        {
            foreach (var (name, value) in parameters)
            {
                if (string.IsNullOrWhiteSpace(name))
                    throw new WorkerProtocolException("Script parameter names cannot be empty.");
                values[name] = WorkerParameterValue.FromObject(value, name);
            }
        }

        return new WorkerScriptCell(source, values);
    }

    private ScriptRunResult CreateTerminalResult(
        TimeSpan elapsed,
        ScriptExecutionCheckpoint? checkpoint,
        WorkerExecutionOutcome outcome)
    {
        var diagnostics = new List<ScriptDiagnostic>();
        string runtimeError;
        switch (outcome.Kind)
        {
            case WorkerCompletionKind.Cancelled:
                runtimeError = "Script cancelled by user.";
                break;
            case WorkerCompletionKind.TimedOut:
                runtimeError = "Script timed out.";
                break;
            case WorkerCompletionKind.MemoryLimitExceeded:
                diagnostics.Add(new ScriptDiagnostic(
                    "RuntimeLimit",
                    $"Worker-tree memory limit exceeded: observed {outcome.PeakMemoryBytes} bytes; " +
                    $"absolute limit {_options.MaxWorkerMemoryBytes} bytes; delta limit {_options.MaxMemoryDeltaBytes} bytes.",
                    0,
                    0));
                runtimeError = "Script exceeded configured worker-tree memory limit.";
                break;
            case WorkerCompletionKind.CpuLimitExceeded:
                diagnostics.Add(new ScriptDiagnostic(
                    "RuntimeLimit",
                    $"Worker-tree CPU-time limit exceeded: {_options.MaxWorkerCpuTimeSeconds} seconds.",
                    0,
                    0));
                runtimeError = "Script exceeded configured worker-tree CPU limit.";
                break;
            case WorkerCompletionKind.ProcessLimitExceeded:
                diagnostics.Add(new ScriptDiagnostic(
                    "RuntimeLimit",
                    $"Worker-tree process-count limit exceeded: {_options.MaxWorkerProcessCount} processes.",
                    0,
                    0));
                runtimeError = "Script exceeded configured worker-tree process limit.";
                break;
            case WorkerCompletionKind.HostRpcLimitExceeded:
                diagnostics.Add(new ScriptDiagnostic(
                    "RuntimeLimit",
                    outcome.Detail ?? "The script exceeded a configured host-data RPC quota.",
                    0,
                    0));
                runtimeError = "Script exceeded configured host-data RPC limits.";
                break;
            case WorkerCompletionKind.AdmissionRejected:
                diagnostics.Add(new ScriptDiagnostic(
                    "RuntimeLimit",
                    outcome.Detail ?? "The bounded QuantScript worker admission queue rejected the request.",
                    0,
                    0));
                runtimeError = "Script worker capacity is currently exhausted.";
                break;
            case WorkerCompletionKind.StandardOutputLimitExceeded:
                diagnostics.Add(new ScriptDiagnostic(
                    "RuntimeLimit",
                    $"Worker stdout limit exceeded: more than {_options.MaxWorkerStandardOutputBytes} bytes.",
                    0,
                    0));
                runtimeError = "Script worker exceeded configured standard-output limit.";
                break;
            case WorkerCompletionKind.StandardErrorLimitExceeded:
                diagnostics.Add(new ScriptDiagnostic(
                    "RuntimeLimit",
                    $"Worker stderr limit exceeded: more than {_options.MaxWorkerStandardErrorBytes} bytes.",
                    0,
                    0));
                runtimeError = "Script worker exceeded configured standard-error limit.";
                break;
            case WorkerCompletionKind.WorkerUnavailable:
                diagnostics.Add(new ScriptDiagnostic(
                    "RuntimeIsolation",
                    "The isolated QuantScript worker or its containment boundary was unavailable.",
                    0,
                    0));
                runtimeError = "Script worker containment is unavailable.";
                break;
            case WorkerCompletionKind.ProtocolFailure:
                diagnostics.Add(new ScriptDiagnostic(
                    "RuntimeIsolation",
                    string.IsNullOrWhiteSpace(outcome.Detail)
                        ? "The isolated QuantScript worker returned an invalid or oversized protocol response."
                        : outcome.Detail,
                    0,
                    0));
                runtimeError = "Script worker returned an invalid response.";
                break;
            default:
                diagnostics.Add(new ScriptDiagnostic(
                    "RuntimeIsolation",
                    "The isolated QuantScript worker exited before returning a complete result.",
                    0,
                    0));
                runtimeError = "Script worker exited before returning a result.";
                break;
        }

        return new ScriptRunResult(
            Success: false,
            Elapsed: elapsed,
            CompileTime: TimeSpan.Zero,
            PeakMemoryBytes: outcome.PeakMemoryBytes,
            CompilationErrors: Array.Empty<ScriptDiagnostic>(),
            RuntimeDiagnostics: diagnostics,
            RuntimeError: runtimeError,
            ConsoleOutput: string.Empty,
            Metrics: Array.Empty<KeyValuePair<string, string>>(),
            Plots: Array.Empty<PlotRequest>(),
            Trades: Array.Empty<ScriptTradeResult>(),
            CapturedBacktests: Array.Empty<Meridian.Backtesting.Sdk.BacktestResult>(),
            RuntimeParameters: Array.Empty<ParameterDescriptor>(),
            Checkpoint: checkpoint);
    }

    private static ScriptRunResult CreateIsolationFailure(
        Stopwatch wallClock,
        ScriptExecutionCheckpoint? checkpoint,
        string detail)
    {
        wallClock.Stop();
        return new ScriptRunResult(
            Success: false,
            Elapsed: wallClock.Elapsed,
            CompileTime: TimeSpan.Zero,
            PeakMemoryBytes: 0,
            CompilationErrors: Array.Empty<ScriptDiagnostic>(),
            RuntimeDiagnostics:
            [
                new ScriptDiagnostic("RuntimeIsolation", detail, 0, 0)
            ],
            RuntimeError: "Script parameters or checkpoint could not be isolated safely.",
            ConsoleOutput: string.Empty,
            Metrics: Array.Empty<KeyValuePair<string, string>>(),
            Plots: Array.Empty<PlotRequest>(),
            Trades: Array.Empty<ScriptTradeResult>(),
            CapturedBacktests: Array.Empty<Meridian.Backtesting.Sdk.BacktestResult>(),
            RuntimeParameters: Array.Empty<ParameterDescriptor>(),
            Checkpoint: checkpoint);
    }

    private async Task<bool> TryAcquireWorkerSlotAsync(CancellationToken ct)
    {
        if (_workerSlots.Wait(0))
            return true;

        if (_options.MaxQueuedWorkerRequests == 0)
            return false;

        var queued = Interlocked.Increment(ref _queuedWorkerRequests);
        if (queued > _options.MaxQueuedWorkerRequests)
        {
            Interlocked.Decrement(ref _queuedWorkerRequests);
            return false;
        }

        try
        {
            return await _workerSlots.WaitAsync(
                    _options.WorkerQueueWaitTimeoutMilliseconds,
                    ct)
                .ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _queuedWorkerRequests);
        }
    }
}
