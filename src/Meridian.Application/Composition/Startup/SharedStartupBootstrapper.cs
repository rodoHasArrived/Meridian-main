using System.Diagnostics;
using Meridian.Application.Commands;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Platform.Results;
using Meridian.Application.Services;
using Serilog;
using DeploymentContext = Meridian.Application.Config.DeploymentContext;

namespace Meridian.Application.Composition.Startup;

/// <summary>
/// Factory delegate for creating the host-specific dashboard server implementation.
/// </summary>
public delegate IHostDashboardServer DashboardServerFactory(
    string configPath,
    int port,
    IApplicationLifecycleCoordinator lifecycle);

/// <summary>
/// Host-agnostic abstraction over the dashboard server used by shared startup orchestration.
/// </summary>
public interface IHostDashboardServer : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

/// <summary>
/// Shared bootstrap entry point for the console host.
/// Keeps <c>Program.cs</c> thin while reusing the same startup flow as the shared composition layer.
/// </summary>
public static class SharedStartupBootstrapper
{
    public static Task<int> RunAsync(string[] args, DashboardServerFactory dashboardServerFactory, CancellationToken ct = default)
    {
        var bootstrapStopwatch = Stopwatch.StartNew();
        var cliArgs = CliArguments.Parse(args);
        var cfgPath = SharedStartupHelpers.ResolveConfigPath(cliArgs);
        var configProbeStopwatch = Stopwatch.StartNew();
        var initialCfg = SharedStartupHelpers.LoadConfigMinimal(cfgPath);
        configProbeStopwatch.Stop();

        LoggingSetup.Initialize(dataRoot: initialCfg.DataRoot);
        var log = LoggingSetup.ForContext("Program");
        var deploymentContext = SharedStartupHelpers.ResolveDeployment(args, cfgPath);

        if (!string.IsNullOrWhiteSpace(deploymentContext.ModeResolutionError))
        {
            Console.Error.WriteLine(deploymentContext.ModeResolutionError);
            log.Error("Invalid deployment mode request: {ModeResolutionError}", deploymentContext.ModeResolutionError);
            LoggingSetup.CloseAndFlush();
            return Task.FromResult(1);
        }

        log.Information(
            "Meridian bootstrap prepared in {ElapsedMs} ms (ConfigProbeMs={ConfigProbeMs}, Mode={Mode}, Command={Command}, Docker={IsDocker})",
            bootstrapStopwatch.ElapsedMilliseconds,
            configProbeStopwatch.ElapsedMilliseconds,
            deploymentContext.Mode,
            deploymentContext.Command,
            deploymentContext.IsDocker);

        return RunCoreAsync(cliArgs, cfgPath, log, deploymentContext, dashboardServerFactory, ct);
    }

    private static async Task<int> RunCoreAsync(
        CliArguments cliArgs,
        string cfgPath,
        ILogger log,
        DeploymentContext deploymentContext,
        DashboardServerFactory dashboardServerFactory,
        CancellationToken ct)
    {
        var configStopwatch = Stopwatch.StartNew();
        using var lifecycle = ApplicationLifecycleCoordinator.Create(log, ct);
        await using var configService = new ConfigurationService(log);
        var cfg = configService.LoadAndPrepareConfig(cfgPath);
        configStopwatch.Stop();

        try
        {
            var orchestrator = new HostModeOrchestrator(log, dashboardServerFactory);
            log.Information("Meridian startup beginning (Mode={Mode}, Port={Port}, ConfigPath={ConfigPath}, ConfigLoadMs={ConfigLoadMs})",
                deploymentContext.Mode,
                deploymentContext.HttpPort,
                cfgPath,
                configStopwatch.ElapsedMilliseconds);
            var exitCode = await orchestrator.RunAsync(
                cliArgs,
                cfg,
                cfgPath,
                configService,
                deploymentContext,
                lifecycle.ShutdownToken,
                lifecycle);
            log.Information("Meridian host stopped with exit code {ExitCode}", exitCode);
            return exitCode;
        }
        catch (OperationCanceledException) when (lifecycle.ShutdownToken.IsCancellationRequested)
        {
            log.Information("Meridian shutdown completed after cancellation request ({Reason})", lifecycle.ShutdownReason);
            return 0;
        }
        catch (Exception ex)
        {
            var errorCode = ErrorCodeExtensions.FromException(ex);
            var friendlyError = FriendlyErrorFormatter.Format(ex);

            FriendlyErrorFormatter.DisplayError(friendlyError);
            log.Fatal(ex, "Meridian terminated unexpectedly (ErrorCode={ErrorCode}, ExitCode={ExitCode})",
                errorCode, errorCode.ToExitCode());

            return errorCode.ToExitCode();
        }
        finally
        {
            log.Information("Flushing Meridian logging pipeline");
            LoggingSetup.CloseAndFlush();
        }
    }
}
