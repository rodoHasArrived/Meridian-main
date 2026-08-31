using System.Diagnostics;
using Meridian.Application.Composition.Startup.StartupModels;
using Serilog;

namespace Meridian.Application.Composition.Startup.ModeRunners;

/// <summary>
/// Runs the browser workstation local host without starting streaming providers or collectors.
/// </summary>
public sealed class WorkstationModeRunner
{
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogger _log;
    private readonly DashboardServerFactory _dashboardServerFactory;
    private readonly TimeSpan _stopTimeout;

    public WorkstationModeRunner(ILogger log, DashboardServerFactory dashboardServerFactory)
        : this(log, dashboardServerFactory, DefaultStopTimeout)
    {
    }

    internal WorkstationModeRunner(
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
    /// Starts the local UI/API server and waits until the lifecycle coordinator requests shutdown.
    /// No market data provider connections, subscriptions, or collector loops are started here.
    /// </summary>
    public async Task<int> RunAsync(StartupContext ctx, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _log.Information("Workstation mode: starting UI server ({ModeDescription})...", ctx.Deployment.ModeDescription);

        IHostDashboardServer? uiServer = null;
        try
        {
            uiServer = _dashboardServerFactory(ctx.ConfigPath, ctx.Deployment.HttpPort, ctx.Lifecycle);
            await uiServer.StartAsync(ct).ConfigureAwait(false);
            _log.Information(
                "Workstation mode UI server started at http://localhost:{Port} in {ElapsedMs} ms",
                ctx.Deployment.HttpPort,
                stopwatch.ElapsedMilliseconds);

            var terminationToken = ctx.Lifecycle is IRuntimeLifecycleControlPlane runtimeLifecycle
                ? runtimeLifecycle.TerminationToken
                : ctx.Lifecycle.ShutdownToken;

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, terminationToken);
            }
            catch (OperationCanceledException) when (terminationToken.IsCancellationRequested)
            {
                _log.Information(
                    "Workstation mode shutdown requested ({Reason}) after {UptimeSeconds:F1} seconds",
                    ctx.Lifecycle.ShutdownReason ?? "unspecified",
                    stopwatch.Elapsed.TotalSeconds);
            }
        }
        finally
        {
            if (uiServer is not null)
                await StopAndDisposeAsync(uiServer).ConfigureAwait(false);
        }

        return 0;
    }

    private async Task StopAndDisposeAsync(IHostDashboardServer uiServer)
    {
        using var shutdownCts = new CancellationTokenSource(_stopTimeout);
        var stopWatch = Stopwatch.StartNew();
        try
        {
            _log.Information("Workstation mode: stopping UI server");
            var stopTask = uiServer.StopAsync(shutdownCts.Token);
            await stopTask.WaitAsync(shutdownCts.Token).ConfigureAwait(false);
            _log.Information("Workstation mode: UI server stopped in {ElapsedMs} ms", stopWatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (shutdownCts.IsCancellationRequested)
        {
            _log.Warning(
                "Workstation mode: UI server stop timed out after {TimeoutSeconds} seconds",
                _stopTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Workstation mode: UI server stop raised an error");
        }

        using var disposeCts = new CancellationTokenSource(_stopTimeout);
        try
        {
            await uiServer.DisposeAsync().AsTask().WaitAsync(disposeCts.Token).ConfigureAwait(false);
            _log.Information("Workstation mode: UI server disposed");
        }
        catch (OperationCanceledException) when (disposeCts.IsCancellationRequested)
        {
            _log.Warning(
                "Workstation mode: UI server disposal timed out after {TimeoutSeconds} seconds",
                _stopTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Workstation mode: UI server disposal raised an error");
        }
    }
}
