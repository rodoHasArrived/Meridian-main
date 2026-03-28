using Meridian.QuantScript.Plotting;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// The complete result of a single script execution.
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
    IReadOnlyList<string> TradesSummary);
