using Meridian.Application.Composition.Startup.StartupModels;
using Meridian.Application.Commands;
using Meridian.Core.Config;
using Meridian.Application.Services;
using Serilog;
using DeploymentContext = Meridian.Platform.Runtime.DeploymentContext;

namespace Meridian.Application.Composition.Startup;

/// <summary>
/// Shared startup orchestrator that dispatches commands and runs the appropriate host mode using <see cref="HostStartupFactory"/>.
/// </summary>
/// <remarks>
/// Delegates to <see cref="StartupOrchestrator"/> which sequences phases and selects a mode runner.
/// This class is retained for backward compatibility with existing call sites and tests.
/// </remarks>
public sealed class HostModeOrchestrator
{
    private readonly ILogger _log;
    private readonly DashboardServerFactory _dashboardServerFactory;

    public HostModeOrchestrator(ILogger log, DashboardServerFactory dashboardServerFactory)
    {
        _log = log;
        _dashboardServerFactory = dashboardServerFactory;
    }

    public async Task<int> RunAsync(
        CliArguments cliArgs,
        AppConfig cfg,
        string cfgPath,
        ConfigurationService configService,
        DeploymentContext deployment,
        CancellationToken ct = default,
        IApplicationLifecycleCoordinator? lifecycle = null)
    {
        var ownedLifecycle = lifecycle is null
            ? ApplicationLifecycleCoordinator.Create(_log, ct)
            : null;
        var effectiveLifecycle = lifecycle ?? ownedLifecycle!;
        // Compatibility callers do not host RuntimeShutdownSequence, so any accepted shutdown
        // request must also release the runner's termination wait. Production bootstrap supplies
        // an explicit lifecycle and retains the full drain, receipt, and host-release sequence.
        using var compatibilityTerminationRegistration = ownedLifecycle is not null
            ? ownedLifecycle.StopWorkToken.Register(ownedLifecycle.SignalTermination)
            : default;
        try
        {
            var ctx = new StartupContext
            {
                CliArgs = cliArgs,
                Config = cfg,
                ConfigPath = cfgPath,
                Deployment = deployment,
                ConfigurationService = configService,
                DashboardServerFactory = _dashboardServerFactory,
                Lifecycle = effectiveLifecycle,
                Log = _log,
                CancellationToken = effectiveLifecycle.ShutdownToken
            };

            var orchestrator = new StartupOrchestrator(_log, _dashboardServerFactory);
            return await orchestrator.RunAsync(ctx);
        }
        finally
        {
            if (ownedLifecycle is not null)
            {
                ownedLifecycle.Dispose();
            }
        }
    }
}
