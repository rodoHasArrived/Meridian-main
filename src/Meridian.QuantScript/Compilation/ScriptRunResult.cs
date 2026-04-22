using Meridian.QuantScript.Plotting;
using Meridian.Backtesting.Sdk;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// The complete result of a single script execution run.
/// </summary>
public sealed record ScriptRunResult(
    bool Success,
    TimeSpan Elapsed,
    TimeSpan CompileTime,
    long PeakMemoryBytes,
    IReadOnlyList<ScriptDiagnostic> CompilationErrors,
    string? RuntimeError,
    string ConsoleOutput,
    IReadOnlyList<KeyValuePair<string, string>> Metrics,
    IReadOnlyList<PlotRequest> Plots,
    IReadOnlyList<string> TradesSummary,
    IReadOnlyList<BacktestResult> CapturedBacktests,
    IReadOnlyList<ParameterDescriptor> RuntimeParameters,
    ScriptExecutionCheckpoint? Checkpoint = null);
