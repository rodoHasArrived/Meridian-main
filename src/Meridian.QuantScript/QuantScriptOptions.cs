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
    /// When false (default), the compiler applies a best-effort advisory guard that rejects
    /// scripts referencing File/Network/Process/Reflection APIs (by source inspection) and
    /// disables <c>#r</c>/<c>#load</c> directives.
    /// <para>
    /// This is NOT a security sandbox: the guard is a source-level denylist that a determined
    /// author can bypass (for example via reflection or runtime type resolution). Do not run
    /// untrusted scripts on the strength of this flag alone; isolate them at the process or OS
    /// level. Set to <see langword="true"/> only for trusted authors who need those APIs.
    /// </para>
    /// </summary>
    public bool EnableUnsafeScripts { get; init; } = false;

    /// <summary>
    /// Maximum number of compiled scripts retained in the in-memory compilation cache. Each
    /// cached entry holds a Roslyn script/compilation graph, so an unbounded cache grows without
    /// limit on a long-running host. When the count exceeds this bound the oldest entries are
    /// evicted first (FIFO). Set to 0 or less to disable caching entirely.
    /// </summary>
    public int MaxCachedCompilations { get; init; } = 256;

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
