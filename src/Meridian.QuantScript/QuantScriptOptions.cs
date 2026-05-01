namespace Meridian.QuantScript;

/// <summary>
/// Configuration options for the QuantScript scripting environment.
/// Bind via <c>"QuantScript"</c> section in appsettings.json.
/// </summary>
public sealed class QuantScriptOptions
{
    public const string SectionName = "QuantScript";

    /// <summary>Directory to scan for .csx script files.</summary>
    public string ScriptsDirectory { get; init; } = "scripts";

    /// <summary>Maximum wall-clock seconds a script may run before cancellation.</summary>
    public int RunTimeoutSeconds { get; init; } = 300;

    /// <summary>Maximum seconds allowed for Roslyn compilation.</summary>
    public int CompilationTimeoutSeconds { get; init; } = 15;

    /// <summary>
    /// When false (default), scripts are denied File/Network/Process access via
    /// Roslyn's MetadataReferenceResolver restriction list.
    /// </summary>
    public bool EnableUnsafeScripts { get; init; } = false;

    /// <summary>Soft limit on plot requests per run. Excess plots are silently dropped.</summary>
    public int MaxPlotsPerRun { get; init; } = 100;

    /// <summary>Default data root passed to BacktestProxy when not overridden in script.</summary>
    public string DefaultDataRoot { get; init; } = "./data";

    /// <summary>
    /// Maximum allowed increase in managed memory (bytes) during a single script run.
    /// Set to 0 or less to disable the guard.
    /// </summary>
    public long MaxMemoryDeltaBytes { get; init; } = 0;

    /// <summary>
    /// Optional guard for maximum elapsed run time in milliseconds, evaluated after each run.
    /// Set to 0 or less to disable the guard.
    /// </summary>
    public int MaxRunElapsedMilliseconds { get; init; } = 0;

    /// <summary>
    /// Optional guard for maximum emitted artifacts (metrics + plots + captured backtests).
    /// Set to 0 or less to disable the guard.
    /// </summary>
    public int MaxOutputItemsPerRun { get; init; } = 0;

    /// <summary>
    /// File extension used to identify notebook documents in the scripts directory.
    /// Defaults to <c>.ipynb</c>.
    /// </summary>
    public string NotebookExtension { get; init; } = ".ipynb";
}
