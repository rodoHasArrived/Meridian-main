using Meridian.Application.Composition.Startup.StartupModels;
using Serilog;

namespace Meridian.Application.Composition.Startup.ModeRunners;

/// <summary>
/// Runs the desktop host mode: starts the embedded HTTP UI server, then executes either the
/// backfill or streaming collector depending on the request, and finally shuts the server down.
/// Desktop mode routing (backfill vs. collector) is resolved here since the orchestrator uses
/// a single <see cref="HostMode.Desktop"/> plan entry for all desktop invocations.
/// </summary>
public sealed class DesktopModeRunner
{
    private readonly ILogger _log;
    private readonly DashboardServerFactory _dashboardServerFactory;

    public DesktopModeRunner(ILogger log, DashboardServerFactory dashboardServerFactory)
    {
        _log = log;
        _dashboardServerFactory = dashboardServerFactory;
    }

    /// <summary>
    /// Starts the desktop UI server, runs the appropriate data operation (backfill or streaming),
    /// then stops and disposes the server on completion.
    /// </summary>
    /// <param name="ctx">Resolved startup context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Exit code from the inner runner.</returns>
    public async Task<int> RunAsync(StartupContext ctx, CancellationToken ct = default)
    {
        _log.Information("Desktop mode: starting UI server ({ModeDescription})...", ctx.Deployment.ModeDescription);

        IHostDashboardServer uiServer = _dashboardServerFactory(ctx.ConfigPath, ctx.Deployment.HttpPort);
        await uiServer.StartAsync(ct);
        _log.Information("Desktop mode UI server started at http://localhost:{Port}", ctx.Deployment.HttpPort);

        try
        {
            var backfillRequested = ctx.CliArgs.Backfill || (ctx.Config.Backfill?.Enabled == true);
            if (backfillRequested)
            {
                return await new BackfillModeRunner(_log).RunAsync(ctx, ct);
            }

            return await new CollectorModeRunner(_log).RunAsync(ctx, ct);
        }
        finally
        {
            await uiServer.StopAsync(ct);
            await uiServer.DisposeAsync();
        }
    }
}
