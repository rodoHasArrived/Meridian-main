using System.Diagnostics;
using Meridian.Application.Composition.Startup.StartupModels;
using Serilog;

namespace Meridian.Application.Composition.Startup.ModeRunners;

/// <summary>
/// Runs the browser workstation local host without starting streaming providers or collectors.
/// </summary>
public sealed class WorkstationModeRunner
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogger _log;
    private readonly DashboardServerFactory _dashboardServerFactory;

    public WorkstationModeRunner(ILogger log, DashboardServerFactory dashboardServerFactory)
    {
        _log = log;
        _dashboardServerFactory = dashboardServerFactory;
    }

    /// <summary>
    /// Starts the local UI/API server and waits until the lifecycle coordinator requests shutdown.
    /// No market data provider connections, subscriptions, or collector loops are started here.
    /// </summary>
    public async Task<int> RunAsync(StartupContext ctx, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _log.Information("Workstation mode: starting UI server ({ModeDescription})...", ctx.Deployment.ModeDescription);

        IHostDashboardServer uiServer = _dashboardServerFactory(ctx.ConfigPath, ctx.Deployment.HttpPort, ctx.Lifecycle);
        await uiServer.StartAsync(ct);
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
        finally
        {
            await StopAndDisposeAsync(uiServer);
        }

        return 0;
    }

    private async Task StopAndDisposeAsync(IHostDashboardServer uiServer)
    {
        using var shutdownCts = new CancellationTokenSource(StopTimeout);
        var stopWatch = Stopwatch.StartNew();
        try
        {
            _log.Information("Workstation mode: stopping UI server");
            await uiServer.StopAsync(shutdownCts.Token);
            _log.Information("Workstation mode: UI server stopped in {ElapsedMs} ms", stopWatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            _log.Warning(
                "Workstation mode: UI server stop timed out after {TimeoutSeconds} seconds",
                StopTimeout.TotalSeconds);
        }
        finally
        {
            await uiServer.DisposeAsync();
            _log.Information("Workstation mode: UI server disposed");
        }
    }
}
