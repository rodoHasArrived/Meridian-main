using Meridian.Application.Backfill;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Startup.StartupModels;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Application.ResultTypes;
using Meridian.Infrastructure.Adapters.Core;
using Serilog;

namespace Meridian.Application.Composition.Startup.ModeRunners;

/// <summary>
/// Runs the historical data backfill operation.
/// Fully self-contained: creates its own host, pipeline, and status writer so it can be
/// invoked directly without caller-side setup.
/// </summary>
public sealed class BackfillModeRunner
{
    private readonly ILogger _log;

    public BackfillModeRunner(ILogger log) => _log = log;

    /// <summary>
    /// Executes the backfill from a resolved startup context.
    /// Creates the pipeline host, recovers WAL, runs the backfill providers, and writes status.
    /// </summary>
    /// <param name="ctx">Resolved startup context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Exit code: 0 on success, non-zero on failure.</returns>
    public async Task<int> RunAsync(StartupContext ctx, CancellationToken ct = default)
    {
        var statusPath = Path.Combine(ctx.Config.DataRoot, "_status", "status.json");
        await using var statusWriter = new StatusWriter(
            statusPath,
            () => ctx.ConfigurationService.LoadAndPrepareConfig(ctx.ConfigPath));

        await using var hostStartup = HostStartupFactory.Create(ctx.Deployment, ctx.ConfigPath);
        var pipeline = hostStartup.Pipeline;
        await pipeline.RecoverAsync();
        _log.Information("WAL enabled for pipeline durability");

        return await RunBackfillAsync(ctx, pipeline, statusWriter, ct);
    }

    /// <summary>
    /// Executes the backfill using a pre-existing <paramref name="pipeline"/> and <paramref name="statusWriter"/>
    /// created by the caller (e.g. <see cref="CollectorModeRunner"/> or <see cref="DesktopModeRunner"/>).
    /// </summary>
    internal async Task<int> RunBackfillAsync(
        StartupContext ctx,
        EventPipeline pipeline,
        StatusWriter statusWriter,
        CancellationToken ct = default)
    {
        var backfillRequest = SharedStartupHelpers.BuildBackfillRequest(ctx.Config, ctx.CliArgs);

        await using var backfillHost = HostStartupFactory.CreateForBackfill(ctx.ConfigPath);
        var backfillProviders = backfillHost.CreateBackfillProviders();

        IHistoricalDataProvider[] providersArray;
        var requestedProvider = backfillRequest.Provider?.Trim();
        var useCompositeProvider = (ctx.Config.Backfill?.EnableFallback ?? true)
            && (string.IsNullOrWhiteSpace(requestedProvider)
                || string.Equals(requestedProvider, "composite", StringComparison.OrdinalIgnoreCase)
                || string.Equals(requestedProvider, "auto", StringComparison.OrdinalIgnoreCase));

        if (useCompositeProvider)
        {
            var composite = backfillHost.CreateCompositeBackfillProvider(backfillProviders);
            providersArray = [composite];
        }
        else
        {
            providersArray = backfillProviders.ToArray();
        }

        var statusStore = BackfillStatusStore.FromConfig(ctx.Config);
        var backfill = new HistoricalBackfillService(providersArray, _log, checkpointStore: statusStore);
        var result = await backfill.RunAsync(backfillRequest, pipeline, ct);
        await statusStore.WriteAsync(result);
        await pipeline.FlushAsync();
        await statusWriter.WriteOnceAsync();

        return result.Success ? 0 : ErrorCode.ProviderError.ToExitCode();
    }
}
