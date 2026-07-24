using System.Diagnostics;
using Meridian.Backtesting.Sdk;
using Meridian.QuantScript.Api;
using Meridian.QuantScript.Plotting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Compiles and executes .csx scripts in a sandboxed Roslyn environment.
/// Each run creates fresh <see cref="QuantScriptGlobals"/> with its own cancellation scope.
/// </summary>
public sealed class ScriptRunner : IScriptRunner
{
    private readonly IQuantScriptCompiler _compiler;
    private readonly IQuantDataContext _dataContext;
    private readonly Backtesting.Engine.BacktestEngine? _backtestEngine;
    private readonly QuantScriptOptions _options;
    private readonly ILogger<ScriptRunner> _logger;

    public ScriptRunner(
        IQuantScriptCompiler compiler,
        IQuantDataContext dataContext,
        PlotQueue plotQueue,
        IOptions<QuantScriptOptions> options,
        ILogger<ScriptRunner> logger,
        Backtesting.Engine.BacktestEngine? backtestEngine = null)
    {
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
        _ = plotQueue ?? throw new ArgumentNullException(nameof(plotQueue)); // retained for DI compatibility; per-run queues are now local
        _backtestEngine = backtestEngine; // null is valid — backtest is optional
        _options = options?.Value ?? new QuantScriptOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ScriptRunResult> RunAsync(
        string source,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default)
        => await ExecuteAsync(source, parameters, checkpoint: null, ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<ScriptRunResult> ContinueWithAsync(
        string source,
        ScriptExecutionCheckpoint checkpoint,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return await ExecuteAsync(source, parameters, checkpoint, ct).ConfigureAwait(false);
    }

    private async Task<ScriptRunResult> ExecuteAsync(
        string source,
        IReadOnlyDictionary<string, object?> parameters,
        ScriptExecutionCheckpoint? checkpoint,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        parameters ??= new Dictionary<string, object?>();
        ct.ThrowIfCancellationRequested();

        var wallClock = Stopwatch.StartNew();
        var memBefore = GC.GetTotalMemory(false);
        TimeSpan compileTime;

        if (checkpoint is null)
        {
            ScriptCompilationResult compilationResult;
            try
            {
                compilationResult = await _compiler.CompileAsync(source, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return CreateCancelledResult(wallClock, checkpoint);
            }

            compileTime = compilationResult.CompilationTime;
            if (ct.IsCancellationRequested)
            {
                return CreateCancelledResult(wallClock, checkpoint);
            }

            if (!compilationResult.Success)
            {
                return new ScriptRunResult(
                    Success: false,
                    Elapsed: wallClock.Elapsed,
                    CompileTime: compilationResult.CompilationTime,
                    PeakMemoryBytes: 0,
                    CompilationErrors: compilationResult.Diagnostics,
                    RuntimeDiagnostics: Array.Empty<ScriptDiagnostic>(),
                    RuntimeError: null,
                    ConsoleOutput: string.Empty,
                    Metrics: Array.Empty<KeyValuePair<string, string>>(),
                    Plots: Array.Empty<PlotRequest>(),
                    Trades: Array.Empty<ScriptTradeResult>(),
                    CapturedBacktests: Array.Empty<BacktestResult>(),
                    RuntimeParameters: Array.Empty<ParameterDescriptor>(),
                    Checkpoint: checkpoint);
            }
        }
        else
        {
            compileTime = TimeSpan.Zero;

            if (!_options.EnableUnsafeScripts && RoslynScriptCompiler.TryCreateSafeModeDiagnostic(source) is { } diagnostic)
            {
                return new ScriptRunResult(
                    Success: false,
                    Elapsed: wallClock.Elapsed,
                    CompileTime: compileTime,
                    PeakMemoryBytes: 0,
                    CompilationErrors: [diagnostic],
                    RuntimeDiagnostics: Array.Empty<ScriptDiagnostic>(),
                    RuntimeError: null,
                    ConsoleOutput: string.Empty,
                    Metrics: Array.Empty<KeyValuePair<string, string>>(),
                    Plots: Array.Empty<PlotRequest>(),
                    Trades: Array.Empty<ScriptTradeResult>(),
                    CapturedBacktests: Array.Empty<BacktestResult>(),
                    RuntimeParameters: Array.Empty<ParameterDescriptor>(),
                    Checkpoint: checkpoint);
            }

            // Safe-mode source checks above mirror fresh compilation; remaining
            // continuations rely on Roslyn diagnostics from ContinueWithAsync.
        }

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        runCts.CancelAfter(TimeSpan.FromSeconds(_options.RunTimeoutSeconds));
        var runCt = runCts.Token;
        var ctProvider = () => runCt;
        var dataProxy = new DataProxy(_dataContext, ctProvider);
        var backtestProxy = new BacktestProxy(_backtestEngine, _options);
        var globals = checkpoint?.Globals ?? new QuantScriptGlobals(dataProxy, backtestProxy, ctProvider, parameters);
        globals.UpdateExecutionContext(parameters, ctProvider);

        Script<object>? script = null;
        if (checkpoint is null)
        {
            if (_compiler is RoslynScriptCompiler rsc)
                script = rsc.GetCachedScript(source) ?? rsc.BuildScript(source);
            else
            {
                var tmp = new RoslynScriptCompiler(
                    Microsoft.Extensions.Options.Options.Create(_options),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<RoslynScriptCompiler>.Instance);
                script = tmp.BuildScript(source);
            }
        }

        string? runtimeError = null;
        IReadOnlyList<ScriptDiagnostic> continuationDiagnostics = Array.Empty<ScriptDiagnostic>();
        var runtimeDiagnostics = new List<ScriptDiagnostic>();
        ScriptExecutionCheckpoint? nextCheckpoint = checkpoint;
        var runPlotQueue = new PlotQueue();

        await Task.Run(async () =>
        {
            ScriptContext.PlotQueue = runPlotQueue;
            try
            {
                _logger.LogInformation(
                    "Executing QuantScript (timeout {Timeout}s, mode {Mode})",
                    _options.RunTimeoutSeconds,
                    checkpoint is null ? "fresh" : "continue");

                ScriptState<object> scriptState;
                if (checkpoint is null)
                {
                    scriptState = await script!.RunAsync(globals, runCt).ConfigureAwait(false);
                }
                else
                {
                    scriptState = await checkpoint.ScriptState
                        .ContinueWithAsync(source, cancellationToken: runCt)
                        .ConfigureAwait(false);
                }

                nextCheckpoint = new ScriptExecutionCheckpoint(scriptState, globals);
            }
            catch (OperationCanceledException)
            {
                runtimeError = ct.IsCancellationRequested
                    ? "Script cancelled by user."
                    : "Script timed out.";
                _logger.LogWarning("Script run terminated: {Reason}", runtimeError);
            }
            catch (CompilationErrorException ex)
            {
                continuationDiagnostics = ex.Diagnostics
                    .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(MapDiagnostic)
                    .ToList();
                _logger.LogWarning("QuantScript continuation failed with {Count} compilation error(s)", continuationDiagnostics.Count);
            }
            catch (Exception ex)
            {
                runtimeError = ex.Message;
                _logger.LogWarning(ex, "Script runtime exception");
            }
            finally
            {
                runPlotQueue.Complete();
                ScriptContext.PlotQueue = null;
            }
        }, CancellationToken.None).ConfigureAwait(false);

        wallClock.Stop();
        var peakMemory = Math.Max(0, GC.GetTotalMemory(false) - memBefore);
        var plots = runPlotQueue.DrainRemaining();
        var metrics = globals.DrainMetrics();
        var capturedBacktests = globals.Backtest.DrainCapturedResults();
        IReadOnlyList<ScriptTradeResult> trades = capturedBacktests.Count == 0
            ? Array.Empty<ScriptTradeResult>()
            : globals.Backtest.CapturedFills
                .OrderBy(static fill => fill.FilledAt)
                .Select(static fill => new ScriptTradeResult(
                    fill.FilledAt,
                    fill.Symbol,
                    fill.FilledQuantity >= 0 ? "Buy" : "Sell",
                    Math.Abs(fill.FilledQuantity),
                    fill.FillPrice,
                    fill.Commission))
                .ToList();

        var outputItemsCount = metrics.Count + plots.Count + capturedBacktests.Count + trades.Count;
        if (_options.MaxMemoryDeltaBytes > 0 && peakMemory > _options.MaxMemoryDeltaBytes)
        {
            runtimeDiagnostics.Add(new ScriptDiagnostic(
                "RuntimeLimit",
                $"Memory delta limit exceeded: {peakMemory} bytes > {_options.MaxMemoryDeltaBytes} bytes.",
                0,
                0));
            runtimeError ??= "Script exceeded configured memory delta limit.";
        }

        if (_options.MaxRunElapsedMilliseconds > 0 && wallClock.ElapsedMilliseconds > _options.MaxRunElapsedMilliseconds)
        {
            runtimeDiagnostics.Add(new ScriptDiagnostic(
                "RuntimeLimit",
                $"Elapsed limit exceeded: {wallClock.ElapsedMilliseconds} ms > {_options.MaxRunElapsedMilliseconds} ms.",
                0,
                0));
            runtimeError ??= "Script exceeded configured elapsed-time limit.";
        }

        if (_options.MaxOutputItemsPerRun > 0 && outputItemsCount > _options.MaxOutputItemsPerRun)
        {
            runtimeDiagnostics.Add(new ScriptDiagnostic(
                "RuntimeLimit",
                $"Output item limit exceeded: {outputItemsCount} items > {_options.MaxOutputItemsPerRun} items.",
                0,
                0));
            runtimeError ??= "Script exceeded configured output-item limit.";
        }

        var resultSuccess = runtimeError is null && continuationDiagnostics.Count == 0 && runtimeDiagnostics.Count == 0;
        return new ScriptRunResult(
            Success: resultSuccess,
            Elapsed: wallClock.Elapsed,
            CompileTime: compileTime,
            PeakMemoryBytes: peakMemory,
            CompilationErrors: continuationDiagnostics,
            RuntimeDiagnostics: runtimeDiagnostics,
            RuntimeError: runtimeError,
            ConsoleOutput: globals.DrainConsoleOutput(),
            Metrics: metrics,
            Plots: plots,
            Trades: trades,
            CapturedBacktests: capturedBacktests,
            RuntimeParameters: globals.SnapshotRuntimeParameters(),
            Checkpoint: resultSuccess ? nextCheckpoint : checkpoint);
    }

    private static ScriptDiagnostic MapDiagnostic(Diagnostic diagnostic)
    {
        var lineSpan = diagnostic.Location.GetLineSpan();
        return new ScriptDiagnostic(
            "Error",
            diagnostic.GetMessage(),
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1);
    }

    private static ScriptRunResult CreateCancelledResult(
        Stopwatch wallClock,
        ScriptExecutionCheckpoint? checkpoint)
    {
        wallClock.Stop();
        return new ScriptRunResult(
            Success: false,
            Elapsed: wallClock.Elapsed,
            CompileTime: TimeSpan.Zero,
            PeakMemoryBytes: 0,
            CompilationErrors: Array.Empty<ScriptDiagnostic>(),
            RuntimeDiagnostics: Array.Empty<ScriptDiagnostic>(),
            RuntimeError: "Script cancelled by user.",
            ConsoleOutput: string.Empty,
            Metrics: Array.Empty<KeyValuePair<string, string>>(),
            Plots: Array.Empty<PlotRequest>(),
            Trades: Array.Empty<ScriptTradeResult>(),
            CapturedBacktests: Array.Empty<BacktestResult>(),
            RuntimeParameters: Array.Empty<ParameterDescriptor>(),
            Checkpoint: checkpoint);
    }
}
