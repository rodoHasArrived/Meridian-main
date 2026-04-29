using Meridian.QuantScript.Plotting;

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
    IReadOnlyList<ScriptTradeResult> Trades,
    ScriptExecutionCheckpoint? Checkpoint = null);
/// <summary>
/// Structured trade/fill payload emitted from script execution for backtest-driven runs.
/// </summary>
public sealed record ScriptTradeResult(
    DateTimeOffset Timestamp,
    string Symbol,
    string Side,
    decimal Quantity,
    decimal Price,
    decimal Commission);
