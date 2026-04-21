using Meridian.Application.Services;

namespace Meridian.Application.Config;

/// <summary>
/// Unified deployment context that captures all deployment-related settings at startup.
/// This reduces conditional branching across entry points by centralizing deployment mode logic.
/// </summary>
/// <remarks>
/// <para><b>Supported Deployment Modes:</b></para>
/// <list type="bullet">
/// <item><description><b>Headless</b> - Console application, no UI, suitable for servers/containers</description></item>
/// <item><description><b>Desktop</b> - Native desktop app with embedded HTTP server for local UI</description></item>
/// </list>
/// <para>The context is immutable once created and provides all deployment-related decisions
/// through computed properties, eliminating scattered conditional logic.</para>
/// </remarks>
public sealed record DeploymentContext
{
    /// <summary>
    /// The resolved deployment mode.
    /// </summary>
    public required DeploymentMode Mode { get; init; }

    /// <summary>
    /// The HTTP port for web/desktop modes. Defaults to 8080.
    /// </summary>
    public int HttpPort { get; init; } = 8080;

    /// <summary>
    /// Whether configuration hot reload is enabled.
    /// </summary>
    public bool HotReloadEnabled { get; init; }

    /// <summary>
    /// Path to configuration file being used.
    /// </summary>
    public required string ConfigPath { get; init; }

    /// <summary>
    /// The environment name (e.g., Development, Production).
    /// </summary>
    public string? EnvironmentName { get; init; }

    /// <summary>
    /// Error emitted while resolving the requested deployment mode, if any.
    /// </summary>
    public string? ModeResolutionError { get; init; }

    /// <summary>
    /// Whether running in Docker container.
    /// </summary>
    public bool IsDocker { get; init; }

    /// <summary>
    /// Whether this is a one-shot command (validate, backfill, etc.) vs persistent operation.
    /// </summary>
    public bool IsOneShotCommand { get; init; }

    /// <summary>
    /// The specific command being run, if any (e.g., "backfill", "wizard", "validate-config").
    /// </summary>
    public string? Command { get; init; }


    /// <summary>
    /// Whether the deployment requires an HTTP server.
    /// </summary>
    public bool RequiresHttpServer => Mode == DeploymentMode.Desktop;

    /// <summary>
    /// Whether the deployment runs the data collector.
    /// </summary>
    public bool RunsCollector => Mode is DeploymentMode.Headless or DeploymentMode.Desktop && !IsOneShotCommand;

    /// <summary>
    /// Whether the deployment should display startup summary.
    /// </summary>
    public bool ShowStartupSummary => !IsOneShotCommand;

    /// <summary>
    /// Whether graceful shutdown handling is required.
    /// </summary>
    public bool RequiresGracefulShutdown => !IsOneShotCommand;

    /// <summary>
    /// Description of the current deployment mode for logging/display.
    /// </summary>
    public string ModeDescription => Mode switch
    {
        DeploymentMode.Headless => "Headless (console only)",
        DeploymentMode.Desktop => $"Desktop mode with UI server on port {HttpPort}",
        _ => "Unknown mode"
    };



    /// <summary>
    /// Creates a deployment context from command line arguments.
    /// </summary>
    public static DeploymentContext FromArgs(string[] args, string configPath)
    {
        var (mode, error) = CliModeResolver.ResolveWithError(args);
        var deploymentMode = MapRunMode(mode);

        return new DeploymentContext
        {
            Mode = deploymentMode,
            HttpPort = ParseHttpPort(args),
            HotReloadEnabled = HasFlag(args, "--watch-config"),
            ConfigPath = configPath,
            EnvironmentName = GetEnvironmentName(),
            ModeResolutionError = error,
            IsDocker = IsRunningInDocker(),
            IsOneShotCommand = DetermineIsOneShot(args),
            Command = DetermineCommand(args)
        };
    }

    /// <summary>
    /// Creates a context for one-shot commands (wizard, validate, etc.).
    /// </summary>
    public static DeploymentContext ForCommand(string command, string configPath)
    {
        return new DeploymentContext
        {
            Mode = DeploymentMode.Headless,
            ConfigPath = configPath,
            EnvironmentName = GetEnvironmentName(),
            IsDocker = IsRunningInDocker(),
            IsOneShotCommand = true,
            Command = command
        };
    }

    /// <summary>
    /// Creates a context for running in desktop mode.
    /// </summary>
    public static DeploymentContext ForDesktop(string configPath, int port = 8080, bool hotReload = false)
    {
        return new DeploymentContext
        {
            Mode = DeploymentMode.Desktop,
            HttpPort = port,
            HotReloadEnabled = hotReload,
            ConfigPath = configPath,
            EnvironmentName = GetEnvironmentName(),
            IsDocker = false,
            IsOneShotCommand = false
        };
    }



    private static DeploymentMode MapRunMode(CliModeResolver.RunMode mode) => mode switch
    {
        CliModeResolver.RunMode.Desktop => DeploymentMode.Desktop,
        _ => DeploymentMode.Headless
    };

    private static int ParseHttpPort(string[] args)
    {
        var portValue = GetArgValue(args, "--http-port");
        return int.TryParse(portValue, out var port) ? port : 8080;
    }

    private static bool HasFlag(string[] args, string flag)
        => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static string? GetArgValue(string[] args, string key)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static string? GetEnvironmentName()
    {
        var env = Environment.GetEnvironmentVariable("MDC_ENVIRONMENT");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        return Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
    }

    private static bool IsRunningInDocker()
    {
        // Check for common Docker indicators
        return File.Exists("/.dockerenv")
            || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true"
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST"));
    }

    private static bool DetermineIsOneShot(string[] args)
    {
        // Commands that run once and exit
        var oneShotFlags = new[]
        {
            "--help", "-h", "--wizard", "--auto-config", "--detect-providers",
            "--validate-credentials", "--generate-config", "--generate-config-schema", "--quick-check",
            "--test-connectivity", "--error-codes", "--check-schemas",
            "--show-config", "--symbols", "--symbols-monitored", "--symbols-archived",
            "--symbols-add", "--symbols-remove", "--symbol-status",
            "--validate-config", "--dry-run", "--selftest",
            "--backfill", "--replay",
            "--package", "--import-package", "--list-package", "--validate-package"
        };

        return args.Any(a => oneShotFlags.Any(f => a.Equals(f, StringComparison.OrdinalIgnoreCase)));
    }

    private static string? DetermineCommand(string[] args)
    {
        // Map flags to command names
        var commandMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["--help"] = "help",
            ["-h"] = "help",
            ["--wizard"] = "wizard",
            ["--auto-config"] = "auto-config",
            ["--detect-providers"] = "detect-providers",
            ["--validate-credentials"] = "validate-credentials",
            ["--generate-config"] = "generate-config",
            ["--generate-config-schema"] = "generate-config-schema",
            ["--quick-check"] = "quick-check",
            ["--test-connectivity"] = "test-connectivity",
            ["--error-codes"] = "error-codes",
            ["--check-schemas"] = "check-schemas",
            ["--show-config"] = "show-config",
            ["--symbols"] = "symbols",
            ["--symbols-monitored"] = "symbols-monitored",
            ["--symbols-archived"] = "symbols-archived",
            ["--symbols-add"] = "symbols-add",
            ["--symbols-remove"] = "symbols-remove",
            ["--symbol-status"] = "symbol-status",
            ["--validate-config"] = "validate-config",
            ["--dry-run"] = "dry-run",
            ["--selftest"] = "selftest",
            ["--backfill"] = "backfill",
            ["--replay"] = "replay",
            ["--package"] = "package",
            ["--import-package"] = "import-package",
            ["--list-package"] = "list-package",
            ["--validate-package"] = "validate-package"
        };

        foreach (var arg in args)
        {
            if (commandMap.TryGetValue(arg, out var command))
                return command;
        }

        return null;
    }

}

/// <summary>
/// Deployment modes supported by the application.
/// </summary>
public enum DeploymentMode : byte
{
    /// <summary>
    /// Headless mode - console application without UI.
    /// Suitable for servers, containers, and background services.
    /// </summary>
    Headless,

    /// <summary>
    /// Desktop mode - native application with embedded HTTP server.
    /// Runs both the data collector and UI server.
    /// </summary>
    Desktop
}
