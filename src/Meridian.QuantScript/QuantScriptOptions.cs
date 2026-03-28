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
}
