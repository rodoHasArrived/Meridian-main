using System.Diagnostics;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.QuantScript.Api;
using Meridian.QuantScript.Compilation;
using Meridian.QuantScript.Plotting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meridian.QuantScript.Runtime;

/// <summary>
/// Runs Roslyn only inside the dedicated worker process. The public runner never invokes this
/// type in its host process.
/// </summary>
internal sealed class InProcessQuantScriptExecutor(
    IQuantDataContext dataContext,
    BacktestEngine? backtestEngine,
    WorkerRunOptions wireOptions)
{
    public async Task<ScriptRunResult> ExecuteAsync(
        IReadOnlyList<WorkerScriptCell> replayCells,
        WorkerScriptCell currentCell,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(replayCells);
        ArgumentNullException.ThrowIfNull(currentCell);

        var options = CreateOptions(wireOptions);
        var compiler = new RoslynScriptCompiler(
            Options.Create(options),
            NullLogger<RoslynScriptCompiler>.Instance);
        var wallClock = Stopwatch.StartNew();
        var compileTime = TimeSpan.Zero;
        var runtimeDiagnostics = new List<ScriptDiagnostic>();
        var continuationDiagnostics = new List<ScriptDiagnostic>();
        string? runtimeError = null;
        var plotQueue = new PlotQueue();
        ScriptState<object>? state = null;
        QuantScriptGlobals? globals = null;

        var cells = replayCells.Concat([currentCell]).ToList();
        try
        {
            ScriptContext.PlotQueue = plotQueue;
            for (var index = 0; index < cells.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var cell = cells[index];
                var isCurrentCell = index == cells.Count - 1;
                var parameters = cell.Parameters.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.ToObject(),
                    StringComparer.OrdinalIgnoreCase);

                if (state is null)
                {
                    var compilation = await compiler.CompileAsync(cell.Source, ct).ConfigureAwait(false);
                    if (isCurrentCell)
                        compileTime = compilation.CompilationTime;

                    if (!compilation.Success)
                    {
                        continuationDiagnostics.AddRange(compilation.Diagnostics);
                        break;
                    }

                    var dataProxy = new DataProxy(dataContext, () => ct);
                    var backtestProxy = new BacktestProxy(backtestEngine, options, () => ct);
                    globals = new QuantScriptGlobals(dataProxy, backtestProxy, () => ct, parameters);
                    var script = compiler.GetCachedScript(cell.Source) ?? compiler.BuildScript(cell.Source);
                    state = await script.RunAsync(globals, ct).ConfigureAwait(false);
                }
                else
                {
                    if (!options.EnableUnsafeScripts &&
                        RoslynScriptCompiler.TryCreateSafeModeDiagnostic(cell.Source) is { } safeModeDiagnostic)
                    {
                        continuationDiagnostics.Add(safeModeDiagnostic);
                        break;
                    }

                    globals!.UpdateExecutionContext(parameters, () => ct);
                    try
                    {
                        state = await state.ContinueWithAsync(cell.Source, cancellationToken: ct).ConfigureAwait(false);
                    }
                    catch (CompilationErrorException ex)
                    {
                        continuationDiagnostics.AddRange(ex.Diagnostics
                            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                            .Select(MapDiagnostic));
                        break;
                    }
                }

                if (!isCurrentCell)
                    DrainReplayArtifacts(globals!, plotQueue);
            }
        }
        catch (OperationCanceledException)
        {
            runtimeError = "Script cancelled by worker shutdown.";
        }
        catch (Exception ex)
        {
            runtimeError = ex.Message;
        }
        finally
        {
            plotQueue.Complete();
            ScriptContext.PlotQueue = null;
            wallClock.Stop();
        }

        var plots = plotQueue.DrainRemaining();
        var metrics = globals?.DrainMetrics() ?? Array.Empty<KeyValuePair<string, string>>();
        var capturedBacktests = globals?.Backtest.DrainCapturedResults() ?? Array.Empty<BacktestResult>();
        var capturedFills = globals?.Backtest.DrainCapturedFills() ?? [];
        IReadOnlyList<ScriptTradeResult> trades = capturedFills.Count == 0
            ? Array.Empty<ScriptTradeResult>()
            : capturedFills
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
        if (options.MaxRunElapsedMilliseconds > 0 &&
            wallClock.ElapsedMilliseconds > options.MaxRunElapsedMilliseconds)
        {
            runtimeDiagnostics.Add(new ScriptDiagnostic(
                "RuntimeLimit",
                $"Elapsed limit exceeded: {wallClock.ElapsedMilliseconds} ms > {options.MaxRunElapsedMilliseconds} ms.",
                0,
                0));
            runtimeError ??= "Script exceeded configured elapsed-time limit.";
        }

        if (options.MaxOutputItemsPerRun > 0 && outputItemsCount > options.MaxOutputItemsPerRun)
        {
            runtimeDiagnostics.Add(new ScriptDiagnostic(
                "RuntimeLimit",
                $"Output item limit exceeded: {outputItemsCount} items > {options.MaxOutputItemsPerRun} items.",
                0,
                0));
            runtimeError ??= "Script exceeded configured output-item limit.";
        }

        var success = state is not null &&
                      runtimeError is null &&
                      continuationDiagnostics.Count == 0 &&
                      runtimeDiagnostics.Count == 0;
        return new ScriptRunResult(
            Success: success,
            Elapsed: wallClock.Elapsed,
            CompileTime: replayCells.Count == 0 ? compileTime : TimeSpan.Zero,
            PeakMemoryBytes: 0,
            CompilationErrors: continuationDiagnostics,
            RuntimeDiagnostics: runtimeDiagnostics,
            RuntimeError: runtimeError,
            ConsoleOutput: globals?.DrainConsoleOutput() ?? string.Empty,
            Metrics: metrics,
            Plots: plots,
            Trades: trades,
            CapturedBacktests: capturedBacktests,
            RuntimeParameters: globals?.SnapshotRuntimeParameters() ?? Array.Empty<ParameterDescriptor>(),
            Checkpoint: null);
    }

    private static QuantScriptOptions CreateOptions(WorkerRunOptions options)
        => new()
        {
            CompilationTimeoutSeconds = options.CompilationTimeoutSeconds,
            EnableUnsafeScripts = options.EnableUnsafeScripts,
            MaxCachedCompilations = options.MaxCachedCompilations,
            MaxPlotsPerRun = options.MaxPlotsPerRun,
            DefaultDataRoot = options.DefaultDataRoot,
            MaxRunElapsedMilliseconds = options.MaxRunElapsedMilliseconds,
            MaxOutputItemsPerRun = options.MaxOutputItemsPerRun
        };

    private static void DrainReplayArtifacts(QuantScriptGlobals globals, PlotQueue plotQueue)
    {
        _ = globals.DrainConsoleOutput();
        _ = globals.DrainMetrics();
        _ = globals.Backtest.DrainCapturedResults();
        _ = globals.Backtest.DrainCapturedFills();
        _ = plotQueue.DrainRemaining();
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
