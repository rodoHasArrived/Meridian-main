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
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogger _log;
    private readonly DashboardServerFactory _dashboardServerFactory;
    private readonly TimeSpan _stopTimeout;

    public DesktopModeRunner(ILogger log, DashboardServerFactory dashboardServerFactory)
        : this(log, dashboardServerFactory, DefaultStopTimeout)
    {
    }

    internal DesktopModeRunner(
        ILogger log,
        DashboardServerFactory dashboardServerFactory,
        TimeSpan stopTimeout)
    {
        _log = log;
        _dashboardServerFactory = dashboardServerFactory;
        _stopTimeout = stopTimeout > TimeSpan.Zero
            ? stopTimeout
            : throw new ArgumentOutOfRangeException(nameof(stopTimeout));
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

        IHostDashboardServer? uiServer = null;
        var started = false;
        try
        {
            uiServer = _dashboardServerFactory(ctx.ConfigPath, ctx.Deployment.HttpPort, ctx.Lifecycle);
            await uiServer.StartAsync(ct).ConfigureAwait(false);
            started = true;
            _log.Information("Desktop mode UI server started at http://localhost:{Port}", ctx.Deployment.HttpPort);

            if (ct.IsCancellationRequested || ctx.Lifecycle.IsShutdownRequested)
            {
                _log.Information(
                    "Desktop mode shutdown requested before collector startup ({Reason})",
                    ctx.Lifecycle.ShutdownReason ?? "cancellation-requested");
                return 0;
            }

            var backfillRequested = ctx.CliArgs.Backfill || (ctx.Config.Backfill?.Enabled == true);
            if (backfillRequested)
            {
                return await new BackfillModeRunner(_log).RunAsync(ctx, ct);
            }

            return await new CollectorModeRunner(_log).RunAsync(ctx, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || ctx.Lifecycle.ShutdownToken.IsCancellationRequested)
        {
            _log.Information(
                "Desktop mode shutdown requested during runtime execution ({Reason})",
                ctx.Lifecycle.ShutdownReason ?? "cancellation-requested");
            return 0;
        }
        finally
        {
            if (started &&
                ctx.Lifecycle.IsShutdownRequested &&
                ctx.Lifecycle is IRuntimeLifecycleControlPlane runtimeLifecycle &&
                !runtimeLifecycle.TerminationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, runtimeLifecycle.TerminationToken);
                }
                catch (OperationCanceledException) when (runtimeLifecycle.TerminationToken.IsCancellationRequested)
                {
                    // The in-process shutdown sequence persisted its receipt and released the host.
                }
            }

            if (uiServer is not null)
                await StopAndDisposeAsync(uiServer).ConfigureAwait(false);
        }
    }

    private async Task StopAndDisposeAsync(IHostDashboardServer uiServer)
    {
        using (var shutdownCts = new CancellationTokenSource(_stopTimeout))
        {
            try
            {
                _log.Information("Desktop mode: stopping UI server");
                var stopTask = uiServer.StopAsync(shutdownCts.Token);
                await stopTask.WaitAsync(shutdownCts.Token).ConfigureAwait(false);
                _log.Information("Desktop mode: UI server stopped");
            }
            catch (OperationCanceledException) when (shutdownCts.IsCancellationRequested)
            {
                _log.Warning(
                    "Desktop mode: UI server stop timed out after {TimeoutSeconds} seconds",
                    _stopTimeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Desktop mode: UI server stop raised an error");
            }
        }

        using var disposeCts = new CancellationTokenSource(_stopTimeout);
        try
        {
            await uiServer.DisposeAsync().AsTask().WaitAsync(disposeCts.Token).ConfigureAwait(false);
            _log.Information("Desktop mode: UI server disposed");
        }
        catch (OperationCanceledException) when (disposeCts.IsCancellationRequested)
        {
            _log.Warning(
                "Desktop mode: UI server disposal timed out after {TimeoutSeconds} seconds",
                _stopTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Desktop mode: UI server disposal raised an error");
        }
    }
}
