using System.Diagnostics;
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
    private readonly PlotQueue _plotQueue;
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
        _plotQueue = plotQueue ?? throw new ArgumentNullException(nameof(plotQueue));
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

        var wallClock = Stopwatch.StartNew();
        var memBefore = GC.GetTotalMemory(false);
        TimeSpan compileTime;

        if (checkpoint is null)
        {
            var compilationResult = await _compiler.CompileAsync(source, ct).ConfigureAwait(false);
            compileTime = compilationResult.CompilationTime;

            if (!compilationResult.Success)
            {
                return new ScriptRunResult(
                    Success: false,
                    Elapsed: wallClock.Elapsed,
                    CompileTime: compilationResult.CompilationTime,
                    PeakMemoryBytes: 0,
                    CompilationErrors: compilationResult.Diagnostics,
                    RuntimeError: null,
                    ConsoleOutput: string.Empty,
                    Metrics: Array.Empty<KeyValuePair<string, string>>(),
                    Plots: Array.Empty<PlotRequest>(),
                    TradesSummary: Array.Empty<string>(),
                    Checkpoint: checkpoint);
            }
        }
        else
        {
            // Continuations rely on Roslyn continuation diagnostics from ContinueWithAsync.
            compileTime = TimeSpan.Zero;
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
        ScriptExecutionCheckpoint? nextCheckpoint = checkpoint;
        var runPlotQueue = _plotQueue;
        TimeSpan continuationCompileTime = TimeSpan.Zero;

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
                    var continuationCompileWatch = Stopwatch.StartNew();
                    scriptState = await checkpoint.ScriptState
                        .ContinueWithAsync(source, cancellationToken: runCt)
                        .ConfigureAwait(false);
                    continuationCompileWatch.Stop();
                    continuationCompileTime = continuationCompileWatch.Elapsed;
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
                if (checkpoint is not null)
                {
                    continuationCompileTime = continuationCompileTime == TimeSpan.Zero
                        ? wallClock.Elapsed
                        : continuationCompileTime;
                }
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
        }, ct).ConfigureAwait(false);

        wallClock.Stop();
        var peakMemory = Math.Max(0, GC.GetTotalMemory(false) - memBefore);
        var plots = runPlotQueue.DrainRemaining();
        var resultSuccess = runtimeError is null && continuationDiagnostics.Count == 0;
        if (checkpoint is not null)
            compileTime = continuationCompileTime;

        return new ScriptRunResult(
            Success: resultSuccess,
            Elapsed: wallClock.Elapsed,
            CompileTime: compileTime,
            PeakMemoryBytes: peakMemory,
            CompilationErrors: continuationDiagnostics,
            RuntimeError: runtimeError,
            ConsoleOutput: globals.DrainConsoleOutput(),
            Metrics: globals.DrainMetrics(),
            Plots: plots,
            TradesSummary: Array.Empty<string>(),
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
}
