using Meridian.Backtesting.Sdk;
using Meridian.QuantScript.Plotting;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Typed trade/fill row surfaced from script execution output.
/// </summary>
public sealed record ScriptTradeResult(
    DateTimeOffset Timestamp,
    string Symbol,
    string Side,
    decimal Quantity,
    decimal Price,
    decimal Commission,
    Guid FillId = default,
    Guid OrderId = default,
    int BacktestRunIndex = 0);

/// <summary>
/// The complete result of a single script execution run.
/// </summary>
public sealed record ScriptRunResult(
    bool Success,
    TimeSpan Elapsed,
    TimeSpan CompileTime,
    long PeakMemoryBytes,
    IReadOnlyList<ScriptDiagnostic> CompilationErrors,
    IReadOnlyList<ScriptDiagnostic> RuntimeDiagnostics,
    string? RuntimeError,
    string ConsoleOutput,
    IReadOnlyList<KeyValuePair<string, string>> Metrics,
    IReadOnlyList<PlotRequest> Plots,
    IReadOnlyList<ScriptTradeResult> Trades,
    IReadOnlyList<BacktestResult> CapturedBacktests,
    IReadOnlyList<ParameterDescriptor> RuntimeParameters,
    ScriptExecutionCheckpoint? Checkpoint = null,
    IReadOnlyList<ScriptDiagnostic>? CompilationWarnings = null);
