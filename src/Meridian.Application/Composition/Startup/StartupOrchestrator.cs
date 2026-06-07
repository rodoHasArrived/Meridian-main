using Meridian.Application.Composition.Startup.ModeRunners;
using Meridian.Application.Composition.Startup.StartupModels;
using Meridian.Core.Config;
using Meridian.Application.Services;
using Meridian.Core.Diagnostics;
using Serilog;
using DeploymentMode = Meridian.Platform.Runtime.DeploymentMode;

namespace Meridian.Application.Composition.Startup;

/// <summary>
/// Sequences the named startup phases and delegates execution to the appropriate mode runner.
/// </summary>
/// <remarks>
/// <para><b>Phase sequence:</b></para>
/// <list type="number">
///   <item><description>Command dispatch — one-shot CLI commands exit here.</description></item>
///   <item><description>Validation — configuration, file permissions, schema compatibility.</description></item>
///   <item><description>Runtime selection — desktop, backfill, or streaming collector.</description></item>
/// </list>
/// </remarks>
public sealed class StartupOrchestrator
{
    private const string StartupOperationName = "runtime.startup.sequence";
    private const string ComponentName = nameof(StartupOrchestrator);
    private static readonly TimeSpan StartupPhaseWarningThreshold = TimeSpan.FromSeconds(5);

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
        var correlationId = Guid.NewGuid().ToString("N");
        var startupStopwatch = System.Diagnostics.Stopwatch.StartNew();

        _log.Information(
            "Startup sequence started for {OperationName} with {CorrelationId}; component={ComponentName}; mode={Mode}; command={Command}; configPath={ConfigPath}",
            StartupOperationName,
            correlationId,
            ComponentName,
            ctx.Deployment.Mode,
            RuntimeDiagnosticRedactor.SanitizeText(ctx.Deployment.Command),
            RuntimeDiagnosticRedactor.SanitizeText(ctx.ConfigPath));

        try
        {
            // Phase 1 — Command dispatch
            var commandRunner = new CommandModeRunner(_log);
            var commandStopwatch = StartPhase(correlationId, "command-dispatch");
            var commandResult = await commandRunner.TryRunAsync(ctx, ctx.CancellationToken);
            CompletePhase(correlationId, "command-dispatch", commandStopwatch, commandResult.HasValue ? "one-shot-command" : "continue");
            if (commandResult.HasValue)
            {
                return CompleteStartup(correlationId, startupStopwatch, commandResult.Value, "command-dispatch");
            }

            // Phase 2 — Validation
            var validationStopwatch = StartPhase(correlationId, "validation");
            var validationResult = await RunValidationAsync(ctx);
            CompletePhase(correlationId, "validation", validationStopwatch, validationResult.Success ? "passed" : "failed");
            if (!validationResult.Success)
            {
                return CompleteStartup(
                    correlationId,
                    startupStopwatch,
                    validationResult.ExitCode.GetValueOrDefault(1),
                    "validation");
            }

            // Phase 3 — Runtime selection
            var selectionStopwatch = StartPhase(correlationId, "runtime-selection");
            var plan = ResolvePlan(ctx);
            CompletePhase(correlationId, "runtime-selection", selectionStopwatch, plan.Mode.ToString());

            var runtimeStopwatch = StartPhase(correlationId, "mode-execution");
            var exitCode = await ExecutePlanAsync(plan);
            CompletePhase(correlationId, "mode-execution", runtimeStopwatch, $"exit-code:{exitCode}");
            return CompleteStartup(correlationId, startupStopwatch, exitCode, plan.Mode.ToString());
        }
        catch (Exception ex)
        {
            startupStopwatch.Stop();
            _log.Error(
                ex,
                "Startup sequence failed for {OperationName} with {CorrelationId}; component={ComponentName}; elapsedMs={ElapsedMs}; recoveryAction={RecoveryAction}",
                StartupOperationName,
                correlationId,
                ComponentName,
                startupStopwatch.ElapsedMilliseconds,
                "Review the preceding startup phase log, fix the reported dependency/configuration failure, then restart Meridian.");
            throw;
        }
    }

    private System.Diagnostics.Stopwatch StartPhase(string correlationId, string phaseName)
    {
        _log.Debug(
            "Startup phase started for {OperationName} with {CorrelationId}; component={ComponentName}; startupPhase={StartupPhase}",
            StartupOperationName,
            correlationId,
            ComponentName,
            phaseName);
        return System.Diagnostics.Stopwatch.StartNew();
    }

    private void CompletePhase(
        string correlationId,
        string phaseName,
        System.Diagnostics.Stopwatch stopwatch,
        string outcome)
    {
        stopwatch.Stop();

        if (stopwatch.Elapsed > StartupPhaseWarningThreshold)
        {
            _log.Warning(
                "Startup phase slow for {OperationName} with {CorrelationId}; component={ComponentName}; startupPhase={StartupPhase}; elapsedMs={ElapsedMs}; outcome={Outcome}; recoveryAction={RecoveryAction}",
                StartupOperationName,
                correlationId,
                ComponentName,
                phaseName,
                stopwatch.ElapsedMilliseconds,
                outcome,
                "Inspect provider, storage, and configuration readiness before enabling heavier diagnostics.");
            return;
        }

        _log.Information(
            "Startup phase completed for {OperationName} with {CorrelationId}; component={ComponentName}; startupPhase={StartupPhase}; elapsedMs={ElapsedMs}; outcome={Outcome}",
            StartupOperationName,
            correlationId,
            ComponentName,
            phaseName,
            stopwatch.ElapsedMilliseconds,
            outcome);
    }

    private int CompleteStartup(
        string correlationId,
        System.Diagnostics.Stopwatch stopwatch,
        int exitCode,
        string exitPath)
    {
        stopwatch.Stop();
        _log.Information(
            "Startup sequence completed for {OperationName} with {CorrelationId}; component={ComponentName}; elapsedMs={ElapsedMs}; exitCode={ExitCode}; exitPath={ExitPath}",
            StartupOperationName,
            correlationId,
            ComponentName,
            stopwatch.ElapsedMilliseconds,
            exitCode,
            exitPath);
        return exitCode;
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
            DeploymentMode.Workstation => HostMode.Workstation,
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
            HostMode.Workstation => new WorkstationModeRunner(_log, _dashboardServerFactory)
                                    .RunAsync(plan.Context, plan.Context.CancellationToken),

            HostMode.Desktop => new DesktopModeRunner(_log, _dashboardServerFactory)
                                    .RunAsync(plan.Context, plan.Context.CancellationToken),

            HostMode.Backfill => new BackfillModeRunner(_log)
                                    .RunAsync(plan.Context, plan.Context.CancellationToken),

            _ /* Collector */ => new CollectorModeRunner(_log)
                                    .RunAsync(plan.Context, plan.Context.CancellationToken),
        };
    }
}
