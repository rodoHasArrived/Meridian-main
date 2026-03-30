using System.Diagnostics;
using Meridian.QuantScript.Api;
using Meridian.QuantScript.Plotting;
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
        Backtesting.Engine.BacktestEngine? backtestEngine,
        IOptions<QuantScriptOptions> options,
        ILogger<ScriptRunner> logger)
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        parameters ??= new Dictionary<string, object?>();

        var wallClock = Stopwatch.StartNew();
        var memBefore = GC.GetTotalMemory(false);

        // Step 1 — Compile (uses SHA-256 cache in RoslynScriptCompiler)
        var compilationResult = await _compiler.CompileAsync(source, ct).ConfigureAwait(false);

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
                TradesSummary: Array.Empty<string>());
        }

        // Step 2 — Apply per-run timeout
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        runCts.CancelAfter(TimeSpan.FromSeconds(_options.RunTimeoutSeconds));
        var runCt = runCts.Token;

        // Step 3 — Get compiled Script<object> (from cache if available, avoids recompilation)
        Script<object>? script = null;
        if (_compiler is RoslynScriptCompiler rsc)
            script = rsc.GetCachedScript(source) ?? rsc.BuildScript(source);
        else
        {
            // Fallback: create a temporary RoslynScriptCompiler to build the script
            var tmp = new RoslynScriptCompiler(
                Microsoft.Extensions.Options.Options.Create(_options),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RoslynScriptCompiler>.Instance);
            script = tmp.BuildScript(source);
        }

        // Step 4 — Set up per-run globals
        var ctProvider = () => runCt;
        var dataProxy = new DataProxy(_dataContext, ctProvider);
        var backtestProxy = new BacktestProxy(_backtestEngine, _options);
        var globals = new QuantScriptGlobals(dataProxy, backtestProxy, runCt, parameters);

        // Step 5 — Run on a thread-pool thread so blocking data calls don't deadlock the UI
        string? runtimeError = null;
        var runPlotQueue = _plotQueue;

        await Task.Run(async () =>
        {
            // Set the ambient plot queue so that ReturnSeries.Plot() / PriceSeries.Plot()
            // can enqueue without needing an explicit dependency injection.
            ScriptContext.PlotQueue = runPlotQueue;
            try
            {
                _logger.LogInformation(
                    "Executing QuantScript (timeout {Timeout}s)", _options.RunTimeoutSeconds);
                await script.RunAsync(globals, runCt).ConfigureAwait(false);
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
                // Clear the ambient reference so thread-pool threads are not polluted across runs.
                ScriptContext.PlotQueue = null;
            }
        }, ct).ConfigureAwait(false);

        wallClock.Stop();
        var peakMemory = Math.Max(0, GC.GetTotalMemory(false) - memBefore);
        var plots = runPlotQueue.DrainRemaining();

        return new ScriptRunResult(
            Success: runtimeError is null,
            Elapsed: wallClock.Elapsed,
            CompileTime: compilationResult.CompilationTime,
            PeakMemoryBytes: peakMemory,
            CompilationErrors: Array.Empty<ScriptDiagnostic>(),
            RuntimeError: runtimeError,
            ConsoleOutput: globals.GetConsoleOutput(),
            Metrics: globals.GetMetrics(),
            Plots: plots,
            TradesSummary: Array.Empty<string>());
    }
}
