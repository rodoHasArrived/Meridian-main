namespace Meridian.QuantScript;

/// <summary>
/// Configuration options for the QuantScript scripting environment.
/// Bind via <c>"QuantScript"</c> section in appsettings.json.
/// </summary>
public sealed class QuantScriptOptions
{
    public const string SectionName = "QuantScript";

    /// <summary>Directory to scan for notebook and legacy script files.</summary>
    public string ScriptsDirectory { get; init; } = "scripts";

    /// <summary>Primary notebook file extension.</summary>
    public string NotebookExtension { get; init; } = ".mqnb";

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

    /// <summary>Soft warning threshold for the number of notebook cells in a document.</summary>
    public int NotebookCellWarningThreshold { get; init; } = 25;

    /// <summary>Default data root passed to BacktestProxy when not overridden in script.</summary>
    public string DefaultDataRoot { get; init; } = "./data";
}
