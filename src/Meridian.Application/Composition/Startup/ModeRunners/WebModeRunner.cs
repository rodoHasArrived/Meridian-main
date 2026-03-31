using Meridian.Application.Composition.Startup.StartupModels;
using Serilog;

namespace Meridian.Application.Composition.Startup.ModeRunners;

/// <summary>
/// Runs the standalone web dashboard server.
/// Starts the HTTP server, waits for Ctrl+C or cancellation, then shuts it down gracefully.
/// No data collector is started in this mode.
/// </summary>
public sealed class WebModeRunner
{
    private readonly ILogger _log;
    private readonly DashboardServerFactory _dashboardServerFactory;

    public WebModeRunner(ILogger log, DashboardServerFactory dashboardServerFactory)
    {
        _log = log;
        _dashboardServerFactory = dashboardServerFactory;
    }

    /// <summary>
    /// Starts the web dashboard and blocks until the process is cancelled or the user presses Ctrl+C.
    /// </summary>
    /// <returns>Exit code 0 on clean shutdown.</returns>
    public async Task<int> RunAsync(StartupContext ctx, CancellationToken ct = default)
    {
        _log.Information("Starting web dashboard ({ModeDescription})...", ctx.Deployment.ModeDescription);

        await using var webServer = _dashboardServerFactory(ctx.ConfigPath, ctx.Deployment.HttpPort);
        await webServer.StartAsync(ct);

        _log.Information("Web dashboard started at http://localhost:{Port}", ctx.Deployment.HttpPort);
        Console.WriteLine($"Web dashboard running at http://localhost:{ctx.Deployment.HttpPort}");
        Console.WriteLine("Press Ctrl+C to stop...");

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            _log.Information("Shutdown requested");
            done.TrySetResult();
        };

        Console.CancelKeyPress += handler;
        try
        {
            using var registration = ct.Register(() => done.TrySetCanceled(ct));
            try
            {
                await done.Task;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _log.Information("Web dashboard shutdown requested via cancellation token");
            }
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }

        var shutdownToken = ct.IsCancellationRequested ? CancellationToken.None : ct;
        _log.Information("Stopping web dashboard...");
        await webServer.StopAsync(shutdownToken);
        _log.Information("Web dashboard stopped");
        return 0;
    }
}
