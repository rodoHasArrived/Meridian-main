using Meridian.Contracts.SecurityMaster;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Hosted service that warms the in-memory projection cache on startup when
/// <see cref="SecurityMasterOptions.PreloadProjectionCache"/> is enabled.
/// This eliminates cold-start latency for the first queries after deployment.
/// When <see cref="SecurityMasterOptions.ProjectionCacheRefreshMinutes"/> is positive, the service
/// also re-warms the cache on that interval (the cache's atomic <c>ReplaceAll</c> swap keeps
/// concurrent readers on a complete set throughout). Cross-node coherence is primarily delivered by
/// <see cref="SecurityMasterProjectionChangeListener"/> (PostgreSQL <c>LISTEN/NOTIFY</c>); the
/// periodic re-warm is the backstop that bounds staleness if a notification is lost or the
/// listener is disabled.
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

    /// <summary>First delay after startup or periodic refresh failure; doubles up to a minute.</summary>
    internal TimeSpan RefreshRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var refreshFailed = false;
        try
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            refreshFailed = true;
            _logger.LogWarning("Security Master projection/identifier initialization was cancelled during startup.");
        }
        catch (Exception ex)
        {
            refreshFailed = true;
            // Queries can read the store while initialization recovers. Identifier seeding is
            // required even in lazy-cache, single-node deployments without a notification listener.
            _logger.LogError(ex, "Security Master projection/identifier initialization failed; retrying in the background.");
        }

        if (refreshFailed || _options.ProjectionCacheRefreshMinutes > 0)
        {
            var refreshToken = _refreshCts.Token;
            _refreshLoop = Task.Run(() => RunRefreshLoopAsync(refreshFailed, refreshToken), CancellationToken.None);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (_options.PreloadProjectionCache)
        {
            await _projectionService.WarmAsync(ct).ConfigureAwait(false);
        }
        else
        {
            _logger.LogDebug("Security Master projection cache pre-warm is disabled (PreloadProjectionCache=false).");
        }

        // In lazy mode the seeder reads durable membership without populating the cache. Skipping
        // this step would leave the canonical registry and security-ID lookup empty until a write
        // or notification, neither of which is guaranteed in a single-node deployment.
        if (_seedService is not null)
        {
            await _seedService.SeedAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task RunRefreshLoopAsync(bool retryPending, CancellationToken ct)
    {
        var retryDelay = RefreshRetryDelay;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!retryPending && _options.ProjectionCacheRefreshMinutes <= 0)
                {
                    return;
                }

                await Task.Delay(retryPending ? retryDelay : TimeSpan.FromMinutes(_options.ProjectionCacheRefreshMinutes), ct)
                    .ConfigureAwait(false);
                await RefreshAsync(ct).ConfigureAwait(false);
                retryPending = false;
                retryDelay = RefreshRetryDelay;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                retryPending = true;
                _logger.LogWarning(ex, "Security Master projection/identifier refresh failed; retrying with backoff.");
                retryDelay = TimeSpan.FromMilliseconds(Math.Min(retryDelay.TotalMilliseconds * 2, 60_000));
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

    public void Dispose()
    {
        _refreshCts.Cancel();
        _refreshCts.Dispose();
    }
}
