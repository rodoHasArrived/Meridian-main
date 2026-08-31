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
    private readonly BackfillProviderFactory _providerFactory;
    private readonly CompositeBackfillProviderFactory _compositeProviderFactory;

    public BackfillModeRunner(ILogger log)
        : this(
            log,
            static (deployment, configPath, ct) =>
                HostStartupFactory.CreateForBackfillAsync(deployment, configPath, ct),
            static host => host.CreateBackfillProviders(),
            static (host, providers) => host.CreateCompositeBackfillProvider(providers))
    {
    }

    internal BackfillModeRunner(
        ILogger log,
        BackfillHostFactory hostFactory,
        BackfillProviderFactory? providerFactory = null,
        CompositeBackfillProviderFactory? compositeProviderFactory = null)
    {
        _log = log;
        _hostFactory = hostFactory;
        _providerFactory = providerFactory ?? (static host => host.CreateBackfillProviders());
        _compositeProviderFactory = compositeProviderFactory
            ?? (static (host, providers) => host.CreateCompositeBackfillProvider(providers));
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

        var backfillProviders = _providerFactory(backfillHost);
        var requestedProvider = backfillRequest.Provider?.Trim();
        var useCompositeProvider = (ctx.Config.Backfill?.EnableFallback ?? true)
            && (string.IsNullOrWhiteSpace(requestedProvider)
                || string.Equals(requestedProvider, "composite", StringComparison.OrdinalIgnoreCase)
                || string.Equals(requestedProvider, "auto", StringComparison.OrdinalIgnoreCase));

        CompositeHistoricalDataProvider? composite = null;
        try
        {
            IHistoricalDataProvider[] providersArray;
            if (useCompositeProvider)
            {
                composite = _compositeProviderFactory(backfillHost, backfillProviders);
                providersArray = [composite];
            }
            else
            {
                providersArray = backfillProviders.ToArray();
            }

            var backfill = new HistoricalBackfillService(providersArray, _log);
            var result = await backfill.RunAsync(backfillRequest, pipeline, ct).ConfigureAwait(false);
            var statusStore = BackfillStatusStore.FromConfig(ctx.Config);
            await statusStore.WriteAsync(result, ct).ConfigureAwait(false);
            await pipeline.FlushAsync(ct).ConfigureAwait(false);
            await statusWriter.WriteOnceAsync(ct).ConfigureAwait(false);

            return result.Success ? 0 : ErrorCode.ProviderError.ToExitCode();
        }
        finally
        {
            if (composite is not null)
            {
                DisposeProvider(composite);
            }
            else
            {
                foreach (var provider in backfillProviders)
                    DisposeProvider(provider);
            }
        }
    }

    private void DisposeProvider(IHistoricalDataProvider provider)
    {
        try
        {
            provider.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warning(
                ex,
                "Backfill provider {ProviderName} raised an error during disposal",
                provider.Name);
        }
    }

    internal delegate Task<HostStartup> BackfillHostFactory(
        Meridian.Platform.Runtime.DeploymentContext deployment,
        string configPath,
        CancellationToken cancellationToken);

    internal delegate IReadOnlyList<IHistoricalDataProvider> BackfillProviderFactory(HostStartup host);

    internal delegate CompositeHistoricalDataProvider CompositeBackfillProviderFactory(
        HostStartup host,
        IReadOnlyList<IHistoricalDataProvider> providers);

}
