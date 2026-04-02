using Meridian.Application.Commands;
using Meridian.Application.Config;
using Meridian.Application.Services;
using Serilog;

namespace Meridian.Application.Composition.Startup.StartupModels;

/// <summary>
/// Fully-resolved startup context that the <see cref="StartupOrchestrator"/> and every mode runner operate on.
/// Created after config is loaded, logging is bootstrapped, and the deployment context is resolved.
/// All fields are required; the record is produced once and flows immutably through the startup pipeline.
/// </summary>
public sealed record StartupContext
{
    /// <summary>Parsed CLI arguments.</summary>
    public required CliArguments CliArgs { get; init; }

    /// <summary>Resolved path to the active configuration file.</summary>
    public required string ConfigPath { get; init; }

    /// <summary>Loaded and prepared application configuration.</summary>
    public required AppConfig Config { get; init; }

    /// <summary>Resolved deployment context (mode, port, hot-reload, Docker, etc.).</summary>
    public required DeploymentContext Deployment { get; init; }

    /// <summary>Configuration service used for validation and hot-reload.</summary>
    public required ConfigurationService ConfigurationService { get; init; }

    /// <summary>Factory for creating the host-specific dashboard server.</summary>
    public required DashboardServerFactory DashboardServerFactory { get; init; }

    /// <summary>Bootstrap logger scoped to the process entry point.</summary>
    public required ILogger Log { get; init; }

    /// <summary>Process-level cancellation token.</summary>
    public CancellationToken CancellationToken { get; init; }
}
