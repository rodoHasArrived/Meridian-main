namespace Meridian.QuantScript;

/// <summary>
/// Configuration options for the QuantScript scripting environment.
/// </summary>
public sealed class QuantScriptOptions
{
    /// <summary>Directory where .csx script files are stored.</summary>
    public string ScriptsDirectory { get; set; } = "scripts";

    /// <summary>When true, scripts may access the file system and network directly.</summary>
    public bool EnableUnsafeScripts { get; set; } = false;

    /// <summary>Maximum wall-clock seconds a script may run before being cancelled.</summary>
    public int MaxExecutionSeconds { get; set; } = 120;
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
}
