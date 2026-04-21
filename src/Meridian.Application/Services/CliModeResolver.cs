namespace Meridian.Application.Services;

/// <summary>
/// Resolves the application run mode from CLI arguments.
/// Handles translation of legacy flags to unified --mode system.
/// </summary>
public static class CliModeResolver
{
    /// <summary>
    /// Application run modes.
    /// </summary>
    public enum RunMode : byte
    {
        /// <summary>Headless mode - no UI, command-line only.</summary>
        Headless,
        /// <summary>Desktop mode - native desktop application with embedded server.</summary>
        Desktop
    }

    /// <summary>
    /// Translates legacy CLI flags to the unified --mode system.
    /// This provides backwards compatibility while funneling all modes through a single code path.
    /// </summary>
    /// <remarks>
    /// Legacy flag mappings that still participate in mode resolution.
    /// </remarks>
    /// <param name="args">Command line arguments.</param>
    /// <returns>The effective mode string, or null for headless.</returns>
    public static string? TranslateLegacyFlags(string[] args)
    {
        // Check for explicit --mode first (takes precedence)
        var explicitMode = GetArgValue(args, "--mode");
        if (!string.IsNullOrWhiteSpace(explicitMode))
            return explicitMode;

        return null;
    }

    /// <summary>
    /// Resolves the run mode from CLI arguments.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>The resolved run mode.</returns>
    public static RunMode Resolve(string[] args)
    {
        var (mode, _) = ResolveWithError(args);
        return mode;
    }

    /// <summary>
    /// Resolves the run mode from CLI arguments, returning any error message.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Tuple of resolved mode and any error message.</returns>
    public static (RunMode Mode, string? Error) ResolveWithError(string[] args)
    {
        if (HasFlag(args, "--ui"))
        {
            return (RunMode.Headless, BuildRemovedWebMessage("--ui"));
        }

        var modeArg = TranslateLegacyFlags(args);
        if (string.IsNullOrWhiteSpace(modeArg))
            return (RunMode.Headless, null);

        var normalized = modeArg.Trim().ToLowerInvariant();
        return normalized switch
        {
            "desktop" => (RunMode.Desktop, null),
            "headless" => (RunMode.Headless, null),
            "web" => (RunMode.Headless, BuildRemovedWebMessage("--mode web")),
            _ => (RunMode.Headless, $"Unknown mode '{normalized}'. Use desktop or headless.")
        };
    }

    private static string BuildRemovedWebMessage(string flag)
        => $"The web dashboard has been removed; use desktop or headless mode instead of '{flag}'.";

    /// <summary>
    /// Checks if a specific flag is present in the arguments.
    /// </summary>
    public static bool HasFlag(string[] args, string flag)
        => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the value following a specific argument key.
    /// </summary>
    public static string? GetArgValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
