using Meridian.Application.Composition;
using Meridian.Application.Composition.Startup.StartupModels;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Serilog;

namespace Meridian.Application.Composition.Startup.ModeRunners;

/// <summary>
/// Runs the desktop host mode: starts the embedded HTTP UI server, then executes either the
/// backfill or streaming collector depending on the request, and finally shuts the server down.
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

        IHostDashboardServer? uiServer = _dashboardServerFactory(ctx.ConfigPath, ctx.Deployment.HttpPort);
        await uiServer.StartAsync(ct);
        _log.Information("Desktop mode UI server started at http://localhost:{Port}", ctx.Deployment.HttpPort);

        try
        {
            var backfillRequested = ctx.CliArgs.Backfill || (ctx.Config.Backfill?.Enabled == true);
            if (backfillRequested)
            {
                return await RunDesktopBackfillAsync(ctx, ct);
            }

            return await RunDesktopCollectorAsync(ctx, ct);
        }
        finally
        {
            await uiServer.StopAsync(ct);
            await uiServer.DisposeAsync();
        }
    }

    private async Task<int> RunDesktopBackfillAsync(StartupContext ctx, CancellationToken ct)
    {
        var statusPath = Path.Combine(ctx.Config.DataRoot, "_status", "status.json");
        await using var statusWriter = new StatusWriter(
            statusPath,
            () => ctx.ConfigurationService.LoadAndPrepareConfig(ctx.ConfigPath));

        await using var hostStartup = HostStartupFactory.Create(ctx.Deployment, ctx.ConfigPath);
        var pipeline = hostStartup.Pipeline;
        await pipeline.RecoverAsync();
        _log.Information("WAL enabled for pipeline durability");

        var backfillRunner = new BackfillModeRunner(_log);
        return await backfillRunner.RunAsync(ctx, pipeline, statusWriter, ct);
    }

    private Task<int> RunDesktopCollectorAsync(StartupContext ctx, CancellationToken ct)
    {
        var collectorRunner = new CollectorModeRunner(_log);
        return collectorRunner.RunAsync(ctx, ct);
    }
}
