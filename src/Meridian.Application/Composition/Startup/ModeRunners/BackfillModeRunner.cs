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
/// Creates a backfill-profile host, selects the appropriate provider(s), executes the backfill,
/// and writes status. Designed to be independently runnable without a streaming collector.
/// </summary>
public sealed class BackfillModeRunner
{
    private readonly ILogger _log;

    public BackfillModeRunner(ILogger log) => _log = log;

    /// <summary>
    /// Executes the backfill using providers and the <paramref name="pipeline"/> supplied by the caller.
    /// Called from <see cref="CollectorModeRunner"/> or <see cref="DesktopModeRunner"/> which both
    /// create the shared pipeline and status writer before delegating here.
    /// </summary>
    /// <param name="ctx">Resolved startup context.</param>
    /// <param name="pipeline">Event pipeline used to persist backfill events.</param>
    /// <param name="statusWriter">Status writer for recording the run outcome.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Exit code: 0 on success, non-zero on failure.</returns>
    public async Task<int> RunAsync(
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

        var backfill = new HistoricalBackfillService(providersArray, _log);
        var result = await backfill.RunAsync(backfillRequest, pipeline);
        var statusStore = BackfillStatusStore.FromConfig(ctx.Config);
        await statusStore.WriteAsync(result);
        await pipeline.FlushAsync();
        await statusWriter.WriteOnceAsync();

        return result.Success ? 0 : ErrorCode.ProviderError.ToExitCode();
    }
}
