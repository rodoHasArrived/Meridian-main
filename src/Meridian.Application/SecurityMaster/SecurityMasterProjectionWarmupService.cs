using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Hosted service that warms the in-memory projection cache on startup when
/// <see cref="SecurityMasterOptions.PreloadProjectionCache"/> is enabled.
/// This eliminates cold-start latency for the first queries after deployment.
/// When <see cref="SecurityMasterOptions.ProjectionCacheRefreshMinutes"/> is positive, the service
/// also re-warms the cache on that interval so multi-node deployments have BOUNDED cross-node
/// staleness: a publish on one node reaches another node's per-process cache within one refresh
/// interval (the cache's atomic <c>ReplaceAll</c> swap keeps concurrent readers on a complete set
/// throughout).
/// </summary>
public sealed class SecurityMasterProjectionWarmupService : IHostedService, IDisposable
{
    private readonly SecurityMasterProjectionService _projectionService;
    private readonly SecurityMasterOptions _options;
    private readonly ILogger<SecurityMasterProjectionWarmupService> _logger;
    private readonly SecurityMasterCanonicalSymbolSeedService? _seedService;
    private readonly CancellationTokenSource _refreshCts = new();
    private Task? _refreshLoop;

    public SecurityMasterProjectionWarmupService(
        SecurityMasterProjectionService projectionService,
        SecurityMasterOptions options,
        ILogger<SecurityMasterProjectionWarmupService> logger,
        SecurityMasterCanonicalSymbolSeedService? seedService = null)
    {
        _projectionService = projectionService;
        _options = options;
        _logger = logger;
        _seedService = seedService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.PreloadProjectionCache)
        {
            _logger.LogDebug("Security Master projection cache pre-warm is disabled (PreloadProjectionCache=false).");
            return;
        }

        _logger.LogInformation("Warming Security Master projection cache on startup...");

        try
        {
            await _projectionService.WarmAsync(cancellationToken).ConfigureAwait(false);

            // Seed the canonical symbol registry from the freshly-populated projection cache.
            if (_seedService is not null)
            {
                await _seedService.SeedAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Security Master projection cache warm-up was cancelled during startup.");
        }
        catch (Exception ex)
        {
            // Log and continue — a warm cache is a performance optimisation, not a hard requirement.
            _logger.LogError(ex, "Security Master projection cache warm-up failed; queries will hit the database directly.");
        }
        finally
        {
            // The periodic refresh starts even when the initial warm failed: the loop is exactly
            // what recovers a cold cache once the store becomes reachable.
            if (_options.ProjectionCacheRefreshMinutes > 0)
            {
                _refreshLoop = Task.Run(() => RunPeriodicRefreshAsync(_refreshCts.Token), CancellationToken.None);
            }
        }
    }

    private async Task RunPeriodicRefreshAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(_options.ProjectionCacheRefreshMinutes);
        _logger.LogInformation(
            "Security Master projection cache periodic re-warm enabled every {Minutes} minute(s); cross-node cache staleness is bounded by this interval.",
            _options.ProjectionCacheRefreshMinutes);
        using var timer = new PeriodicTimer(interval);
        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    return;
                }

                await _projectionService.WarmAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A failed refresh keeps serving the previous complete set; the next tick retries.
                _logger.LogWarning(ex, "Security Master projection cache periodic re-warm failed; retrying on the next interval.");
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _refreshCts.Cancel();
        if (_refreshLoop is { } loop)
        {
            try
            {
                await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Host shutdown timed out waiting for the refresh loop; the loop observes the
                // cancelled token and exits on its own.
            }
        }
    }

    public void Dispose() => _refreshCts.Dispose();
}
