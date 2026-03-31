using Meridian.Application.Composition.Startup.ModeRunners;
using Meridian.Application.Composition.Startup.StartupModels;
using Meridian.Application.Config;
using Meridian.Application.Monitoring;
using Serilog;

namespace Meridian.Application.Composition.Startup;

/// <summary>
/// Sequences the named startup phases and delegates execution to the appropriate mode runner.
/// </summary>
/// <remarks>
/// <para><b>Phase sequence:</b></para>
/// <list type="number">
///   <item><description>Command dispatch — one-shot CLI commands exit here.</description></item>
///   <item><description>Web mode check — HTTP-only dashboard exits here (skips validation).</description></item>
///   <item><description>Validation — configuration, file permissions, schema compatibility.</description></item>
///   <item><description>Runtime selection — desktop, backfill, or streaming collector.</description></item>
/// </list>
/// </remarks>
public sealed class StartupOrchestrator
{
    private readonly ILogger _log;
    private readonly DashboardServerFactory _dashboardServerFactory;

    public StartupOrchestrator(ILogger log, DashboardServerFactory dashboardServerFactory)
    {
        _log = log;
        _dashboardServerFactory = dashboardServerFactory;
    }

    /// <summary>
    /// Runs the full startup sequence using the supplied <paramref name="ctx"/>.
    /// </summary>
    /// <returns>Process exit code.</returns>
    public async Task<int> RunAsync(StartupContext ctx)
    {
        // Phase 1 — Command dispatch
        var commandRunner = new CommandModeRunner(_log);
        var commandResult = await commandRunner.TryRunAsync(ctx, ctx.CancellationToken);
        if (commandResult.HasValue)
            return commandResult.Value;

        // Phase 2 — Web mode (skips validation; the web server handles its own health checks)
        if (ctx.Deployment.Mode == DeploymentMode.Web)
        {
            var webRunner = new WebModeRunner(_log, _dashboardServerFactory);
            return await webRunner.RunAsync(ctx, ctx.CancellationToken);
        }

        // Phase 3 — Validation
        var validationResult = await RunValidationAsync(ctx);
        if (!validationResult.Success)
            return validationResult.ExitCode!.Value;

        // Phase 4 — Runtime selection
        var plan = ResolvePlan(ctx);
        return await ExecutePlanAsync(plan);
    }

    // ── Validation phase ──────────────────────────────────────────────────────

    private async Task<StartupValidationResult> RunValidationAsync(StartupContext ctx)
    {
        var configResult = StartupValidationRunner.ValidateConfiguration(
            ctx.Config, ctx.ConfigurationService, _log);
        if (configResult.HasValue)
            return StartupValidationResult.Fail(configResult.Value);

        var permResult = StartupValidationRunner.EnsureDataDirectoryPermissions(ctx.Config, _log);
        if (permResult.HasValue)
            return StartupValidationResult.Fail(permResult.Value);

        var schemaResult = await StartupValidationRunner.ValidateSchemasAsync(
            ctx.CliArgs, ctx.Config, _log, ctx.CancellationToken);
        if (schemaResult.HasValue)
            return StartupValidationResult.Fail(schemaResult.Value);

        return StartupValidationResult.Ok();
    }

    // ── Plan resolution ───────────────────────────────────────────────────────

    private static StartupPlan ResolvePlan(StartupContext ctx)
    {
        var mode = ctx.Deployment.Mode switch
        {
            DeploymentMode.Desktop => HostMode.Desktop,
            _ when ctx.CliArgs.Backfill || (ctx.Config.Backfill?.Enabled == true) => HostMode.Backfill,
            _ => HostMode.Collector
        };

        return new StartupPlan { Mode = mode, Context = ctx };
    }

    // ── Plan execution ────────────────────────────────────────────────────────

    private Task<int> ExecutePlanAsync(StartupPlan plan)
    {
        return plan.Mode switch
        {
            HostMode.Desktop => new DesktopModeRunner(_log, _dashboardServerFactory)
                                    .RunAsync(plan.Context, plan.Context.CancellationToken),

            HostMode.Backfill => RunHeadlessBackfillAsync(plan.Context),

            _ /* Collector */ => new CollectorModeRunner(_log)
                                    .RunAsync(plan.Context, plan.Context.CancellationToken),
        };
    }

    private async Task<int> RunHeadlessBackfillAsync(StartupContext ctx)
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
        return await backfillRunner.RunAsync(ctx, pipeline, statusWriter, ctx.CancellationToken);
    }
}
