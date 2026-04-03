using Meridian.QuantScript.Plotting;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// The complete result of a single script execution run.
/// </summary>
public sealed record ScriptRunResult
{
    public bool Success { get; init; }

    public TimeSpan Elapsed { get; init; }

    public TimeSpan CompileTime { get; init; }

    public long PeakMemoryBytes { get; init; }

    public IReadOnlyList<ScriptDiagnostic> CompilationErrors { get; init; } = Array.Empty<ScriptDiagnostic>();

    public string? RuntimeError { get; init; }

    public IReadOnlyList<ConsoleOutputEntry> ConsoleEntries { get; init; } = Array.Empty<ConsoleOutputEntry>();

    public IReadOnlyList<KeyValuePair<string, string>> Metrics { get; init; } = Array.Empty<KeyValuePair<string, string>>();

    public IReadOnlyList<PlotRequest> Plots { get; init; } = Array.Empty<PlotRequest>();

    public IReadOnlyList<string> TradesSummary { get; init; } = Array.Empty<string>();

    public ScriptExecutionCheckpoint? Checkpoint { get; init; }

    public string ConsoleOutput =>
        string.Join(Environment.NewLine, ConsoleEntries.Where(entry => !entry.IsMetric).Select(entry => entry.Text));
}
