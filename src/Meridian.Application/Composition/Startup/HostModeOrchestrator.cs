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
        var ownsLifecycle = lifecycle is null;
        var effectiveLifecycle = lifecycle ?? ApplicationLifecycleCoordinator.Create(_log, ct);
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
            if (ownsLifecycle)
            {
                effectiveLifecycle.Dispose();
            }
        }
    }
}
