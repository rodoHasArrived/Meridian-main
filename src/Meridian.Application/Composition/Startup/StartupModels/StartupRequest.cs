using Meridian.Application.Commands;

namespace Meridian.Application.Composition.Startup.StartupModels;

/// <summary>
/// Immutable value-object capturing everything the startup layer receives at process entry.
/// Passed to <see cref="StartupOrchestrator"/> as the starting input before any resolution has occurred.
/// </summary>
/// <param name="CliArgs">Parsed CLI arguments.</param>
/// <param name="ConfigPath">Resolved path to the configuration file.</param>
/// <param name="DashboardServerFactory">Factory for creating the host-specific dashboard server.</param>
/// <param name="CancellationToken">Process-level cancellation token.</param>
public sealed record StartupRequest(
    CliArguments CliArgs,
    string ConfigPath,
    DashboardServerFactory DashboardServerFactory,
    CancellationToken CancellationToken = default);
