using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Meridian.QuantScript.API;
using Meridian.QuantScript.Plotting;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Compiles and executes QuantScript source files, returning structured results.
/// </summary>
public sealed class ScriptRunner : IScriptRunner
{
    private readonly RoslynScriptCompiler _compiler;
    private readonly IQuantDataContext _dataContext;
    private readonly ILogger<ScriptRunner> _logger;

    public ScriptRunner(
        RoslynScriptCompiler compiler,
        IQuantDataContext dataContext,
        ILogger<ScriptRunner> logger)
    {
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ScriptRunResult> RunAsync(
        string source,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        parameters ??= new Dictionary<string, object?>();

        var wallClock = System.Diagnostics.Stopwatch.StartNew();
        var memBefore = GC.GetTotalMemory(false);

        // Step 1 — Compile (uses SHA-256 cache)
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

        // Step 2 — Get cached compiled script or recompile
        var plotQueue = new PlotQueue();
        var globals = new QuantScriptGlobals(
            _dataContext,
            new BacktestProxy(),
            plotQueue,
            parameters,
            ct);

        string? runtimeError = null;

        await Task.Run(async () =>
        {
            try
            {
                var script = CSharpScript.Create<object>(
                    source,
                    RoslynScriptCompiler.GetExecutionOptions(),
                    globalsType: typeof(QuantScriptGlobals));

                await script.RunAsync(globals, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                runtimeError = "Script was cancelled.";
            }
            catch (Exception ex)
            {
                runtimeError = ex.Message;
                _logger.LogWarning(ex, "Script execution failed with runtime error");
            }
            finally
            {
                plotQueue.Complete();
            }
        }, ct).ConfigureAwait(false);

        wallClock.Stop();
        var peakMemory = Math.Max(0, GC.GetTotalMemory(false) - memBefore);
        var plots = plotQueue.DrainRemaining();
        var consoleOutput = string.Join(Environment.NewLine, globals.ConsoleLines);

        return new ScriptRunResult(
            Success: runtimeError is null,
            Elapsed: wallClock.Elapsed,
            CompileTime: compilationResult.CompilationTime,
            PeakMemoryBytes: peakMemory,
            CompilationErrors: Array.Empty<ScriptDiagnostic>(),
            RuntimeError: runtimeError,
            ConsoleOutput: consoleOutput,
            Metrics: globals.GetMetrics(),
            Plots: plots,
            TradesSummary: Array.Empty<string>());
    }
}
