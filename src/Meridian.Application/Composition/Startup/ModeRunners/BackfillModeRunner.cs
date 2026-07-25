using Meridian.Application.Backfill;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Startup.StartupModels;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Platform.Results;
using Meridian.Infrastructure.Adapters.Core;
using Serilog;
using Meridian.Storage.Backfill;

namespace Meridian.Application.Composition.Startup.ModeRunners;

/// <summary>
/// Runs the historical data backfill operation.
/// Fully self-contained: creates its own host, pipeline, and status writer so it can be
/// invoked directly without caller-side setup.
/// </summary>
public sealed class BackfillModeRunner
{
    private readonly ILogger _log;
    private readonly BackfillHostFactory _hostFactory;

    public BackfillModeRunner(ILogger log)
        : this(
            log,
            static (deployment, configPath, ct) =>
                HostStartupFactory.CreateForBackfillAsync(deployment, configPath, ct))
    {
    }

    internal BackfillModeRunner(
        ILogger log,
        BackfillHostFactory hostFactory)
    {
        _log = log;
        _hostFactory = hostFactory;
    }

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

        await using var hostStartup = await _hostFactory(ctx.Deployment, ctx.ConfigPath, ct)
            .ConfigureAwait(false);
        var pipeline = hostStartup.Pipeline;
        await pipeline.RecoverAsync(ct);
        _log.Information("WAL enabled for pipeline durability");

        return await RunBackfillAsync(ctx, hostStartup, pipeline, statusWriter, ct);
    }

    /// <summary>
    /// Executes the backfill using the caller-owned <paramref name="backfillHost"/>,
    /// <paramref name="pipeline"/>, and <paramref name="statusWriter"/>.
    /// </summary>
    /// <param name="ctx">Prepared startup context.</param>
    /// <param name="backfillHost">Started backfill host that owns providers and hosted services.</param>
    /// <param name="pipeline">Pipeline owned by <paramref name="backfillHost"/>.</param>
    /// <param name="statusWriter">Status writer for the current process.</param>
    /// <param name="ct">Token that cancels the backfill.</param>
    /// <returns>The process exit code.</returns>
    internal async Task<int> RunBackfillAsync(
        StartupContext ctx,
        HostStartup backfillHost,
        EventPipeline pipeline,
        StatusWriter statusWriter,
        CancellationToken ct = default)
    {
        var backfillRequest = SharedStartupHelpers.BuildBackfillRequest(ctx.Config, ctx.CliArgs);

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

        var backfill = new HistoricalBackfillService(providersArray, _log);
        var result = await backfill.RunAsync(backfillRequest, pipeline, ct);
        var statusStore = BackfillStatusStore.FromConfig(ctx.Config);
        await statusStore.WriteAsync(result);
        await pipeline.FlushAsync();
        await statusWriter.WriteOnceAsync();

        return result.Success ? 0 : ErrorCode.ProviderError.ToExitCode();
    }

    internal delegate Task<HostStartup> BackfillHostFactory(
        Meridian.Platform.Runtime.DeploymentContext deployment,
        string configPath,
        CancellationToken cancellationToken);

}
