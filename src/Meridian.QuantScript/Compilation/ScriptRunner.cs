using System.Diagnostics;
using Meridian.Backtesting.Engine;
using Meridian.QuantScript.Api;
using Meridian.QuantScript.Plotting;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Compiles and executes .csx scripts in a sandboxed Roslyn environment.
/// Each run creates fresh <see cref="QuantScriptGlobals"/> with its own CancellationTokenSource.
/// </summary>
public sealed class ScriptRunner(
    IQuantScriptCompiler compiler,
    IQuantDataContext dataContext,
    PlotQueue plotQueue,
    BacktestEngine backtestEngine,
    IOptions<QuantScriptOptions> options,
    ILogger<ScriptRunner> logger) : IScriptRunner
{
    /// <inheritdoc/>
    public async Task<ScriptRunResult> RunAsync(
        string source,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(parameters);

        var sw = Stopwatch.StartNew();

        // Compile first; return early on error
        var compilation = await compiler.CompileAsync(source, ct);
        if (!compilation.Success)
        {
            logger.LogWarning("Script compilation failed with {Count} error(s)", compilation.Diagnostics.Count);
            return new ScriptRunResult(false, sw.Elapsed, compilation.Diagnostics, null);
        }

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        runCts.CancelAfter(TimeSpan.FromSeconds(options.Value.RunTimeoutSeconds));
        var runCt = runCts.Token;

        // Prepare plot queue for this run
        plotQueue.MaxPlotsPerRun = options.Value.MaxPlotsPerRun;
        ScriptContext.PlotQueue = plotQueue;

        var ctProvider = () => runCt;
        var dataProxy = new DataProxy(dataContext, ctProvider);
        var backtestProxy = new BacktestProxy(backtestEngine, options.Value);
        var globals = new QuantScriptGlobals(dataProxy, backtestProxy, runCt);

        // Get the compiled Script<object> from the compiler (may hit cache)
        var roslyn = compiler is RoslynScriptCompiler rsc
            ? rsc.GetCachedScript(source) ?? rsc.BuildScript(source)
            : rsc_BuildFallback(source);

        string? runtimeError = null;
        try
        {
            logger.LogInformation("Executing script (timeout {Timeout}s)", options.Value.RunTimeoutSeconds);
            await roslyn.RunAsync(globals, runCt);
        }
        catch (OperationCanceledException)
        {
            runtimeError = ct.IsCancellationRequested ? "Script cancelled by user." : "Script timed out.";
            logger.LogWarning("Script run cancelled: {Reason}", runtimeError);
        }
        catch (Exception ex)
        {
            runtimeError = ex.Message;
            logger.LogError(ex, "Script runtime exception");
        }
        finally
        {
            globals.CompleteConsole();
            plotQueue.Complete();
            ScriptContext.PlotQueue = null;
        }

        return new ScriptRunResult(
            runtimeError is null,
            sw.Elapsed,
            [],
            runtimeError);
    }

    private static Microsoft.CodeAnalysis.Scripting.Script<object> rsc_BuildFallback(string source)
    {
        // Fallback for non-RoslynScriptCompiler implementations (used in tests)
        var scriptCompiler = new RoslynScriptCompiler(
            Microsoft.Extensions.Options.Options.Create(new QuantScriptOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RoslynScriptCompiler>.Instance);
        return scriptCompiler.BuildScript(source);
    }
}
