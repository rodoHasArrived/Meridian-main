using System.Diagnostics;
using Meridian.QuantScript.Api;
using Meridian.QuantScript.Plotting;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging.Abstractions;

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
        Backtesting.Engine.BacktestEngine? backtestEngine,
        IOptions<QuantScriptOptions> options,
        ILogger<ScriptRunner> logger)
    {
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
        _backtestEngine = backtestEngine; // null is valid — backtest is optional
        _options = options?.Value ?? new QuantScriptOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ScriptRunner(
        IQuantScriptCompiler compiler,
        IQuantDataContext dataContext,
        IOptions<QuantScriptOptions> options,
        ILogger<ScriptRunner> logger)
        : this(compiler, dataContext, null, options, logger)
    {
    }

    /// <inheritdoc/>
    public async Task<ScriptRunResult> RunAsync(
        string source,
        IReadOnlyDictionary<string, object?> parameters,
        ScriptExecutionCheckpoint? previousCheckpoint = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        parameters ??= new Dictionary<string, object?>();

        var wallClock = Stopwatch.StartNew();
        var memBefore = GC.GetTotalMemory(false);
        var compileTime = TimeSpan.Zero;

        Script<object>? script = null;
        if (previousCheckpoint is null)
        {
            var compilationResult = await _compiler.CompileAsync(source, ct).ConfigureAwait(false);
            compileTime = compilationResult.CompilationTime;

            if (!compilationResult.Success)
            {
                return new ScriptRunResult
                {
                    Success = false,
                    Elapsed = wallClock.Elapsed,
                    CompileTime = compilationResult.CompilationTime,
                    PeakMemoryBytes = 0,
                    CompilationErrors = compilationResult.Diagnostics
                };
            }

            if (_compiler is RoslynScriptCompiler cachedCompiler)
            {
                script = cachedCompiler.GetCachedScript(source) ?? cachedCompiler.BuildScript(source);
            }
            else
            {
                var fallbackCompiler = new RoslynScriptCompiler(
                    Microsoft.Extensions.Options.Options.Create(_options),
                    NullLogger<RoslynScriptCompiler>.Instance);
                script = fallbackCompiler.BuildScript(source);
            }
        }

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        runCts.CancelAfter(TimeSpan.FromSeconds(_options.RunTimeoutSeconds));
        var runCt = runCts.Token;

        var ctProvider = () => runCt;
        var dataProxy = new DataProxy(_dataContext, ctProvider);
        var backtestProxy = new BacktestProxy(_backtestEngine, _options);
        var globals = previousCheckpoint?.Globals
            ?? new QuantScriptGlobals(dataProxy, backtestProxy, runCt, parameters);
        var outputStartIndex = globals.GetOutputCount();
        globals.PrepareForExecution(dataProxy, backtestProxy, runCt, parameters);

        string? runtimeError = null;
        ScriptExecutionCheckpoint? checkpoint = null;
        IReadOnlyList<ScriptDiagnostic> compilationErrors = Array.Empty<ScriptDiagnostic>();
        var runPlotQueue = new PlotQueue();
        await Task.Run(async () =>
        {
            ScriptContext.PlotQueue = runPlotQueue;
            try
            {
                _logger.LogInformation(
                    "Executing QuantScript (timeout {Timeout}s)", _options.RunTimeoutSeconds);

                ScriptState<object> state;
                if (previousCheckpoint?.ScriptState is { } priorState)
                {
                    var compileStopwatch = Stopwatch.StartNew();
                    state = await priorState
                        .ContinueWithAsync(
                            source,
                            RoslynScriptCompiler.CreateScriptOptions(),
                            cancellationToken: runCt)
                        .ConfigureAwait(false);
                    compileStopwatch.Stop();
                    compileTime = compileStopwatch.Elapsed;
                }
                else
                {
                    state = await script!.RunAsync(globals, runCt).ConfigureAwait(false);
                }

                checkpoint = new ScriptExecutionCheckpoint(state, globals);
            }
            catch (CompilationErrorException ex)
            {
                compilationErrors = ex.Diagnostics
                    .Select(d =>
                    {
                        var span = d.Location.GetLineSpan();
                        return new ScriptDiagnostic(
                            "Error",
                            d.GetMessage(),
                            span.StartLinePosition.Line + 1,
                            span.StartLinePosition.Character + 1);
                    })
                    .ToList();
                _logger.LogWarning("Script compilation failed with {Count} diagnostic(s)", compilationErrors.Count);
            }
            catch (OperationCanceledException)
            {
                runtimeError = ct.IsCancellationRequested
                    ? "Script cancelled by user."
                    : "Script timed out.";
                _logger.LogWarning("Script run terminated: {Reason}", runtimeError);
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
        var consoleEntries = globals.GetConsoleEntries().Skip(outputStartIndex).ToList();
        var metrics = consoleEntries
            .Where(entry => entry.IsMetric)
            .Select(entry => new KeyValuePair<string, string>(entry.MetricLabel ?? string.Empty, entry.Text))
            .ToList();

        return new ScriptRunResult
        {
            Success = runtimeError is null && compilationErrors.Count == 0,
            Elapsed = wallClock.Elapsed,
            CompileTime = compileTime,
            PeakMemoryBytes = peakMemory,
            CompilationErrors = compilationErrors,
            RuntimeError = runtimeError,
            ConsoleEntries = consoleEntries,
            Metrics = metrics,
            Plots = plots,
            TradesSummary = Array.Empty<string>(),
            Checkpoint = checkpoint
        };
    }
}
