using System.Threading.Channels;
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
/// Identifier validity boundaries always schedule a reseed, independently of periodic cache warming
/// and notifications, so clock-only activation and expiry reach the live registry.
/// </summary>
public sealed class SecurityMasterProjectionWarmupService : IHostedService, IDisposable
{
    private readonly SecurityMasterProjectionService _projectionService;
    private readonly SecurityMasterOptions _options;
    private readonly ILogger<SecurityMasterProjectionWarmupService> _logger;
    private readonly SecurityMasterCanonicalSymbolSeedService? _seedService;
    private readonly CancellationTokenSource _refreshCts = new();
    private readonly Channel<bool> _validityScheduleChanged = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
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
        if (_seedService is not null)
            _seedService.ValidityScheduleChanged += OnValidityScheduleChanged;
    }

    /// <summary>First delay after startup or periodic refresh failure; doubles up to a minute.</summary>
    internal TimeSpan RefreshRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    private void OnValidityScheduleChanged() => _validityScheduleChanged.Writer.TryWrite(true);

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

        if (refreshFailed || _options.ProjectionCacheRefreshMinutes > 0 || _seedService is not null)
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
        var lastProjectionRefresh = Clock.GetTimestamp();
        var retryStarted = lastProjectionRefresh;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var projectionDelay = _options.ProjectionCacheRefreshMinutes > 0
                    ? Remaining(TimeSpan.FromMinutes(_options.ProjectionCacheRefreshMinutes), lastProjectionRefresh)
                    : Timeout.InfiniteTimeSpan;
                var validityDelay = _seedService?.NextValidityTransitionUtc is { } nextBoundary
                    ? MaxZero(nextBoundary - Clock.GetUtcNow())
                    : Timeout.InfiniteTimeSpan;
                var delay = retryPending
                    ? Remaining(retryDelay, retryStarted)
                    : Earlier(projectionDelay, validityDelay);
                // Timer implementations have finite maximum delays. Recheck long-lived windows
                // daily without reading durable state until their actual boundary is due.
                if (delay > TimeSpan.FromDays(1))
                    delay = TimeSpan.FromDays(1);

                // A write can introduce an earlier boundary while this loop waits. Recompute the
                // schedule without postponing a pending retry or configured projection refresh.
                using (var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    var changed = _validityScheduleChanged.Reader.ReadAsync(waitCts.Token).AsTask();
                    var timer = Task.Delay(delay, Clock, waitCts.Token);
                    var completed = await Task.WhenAny(timer, changed).ConfigureAwait(false);
                    await completed.ConfigureAwait(false);
                    waitCts.Cancel();
                    if (completed == changed)
                        continue;
                }

                if (retryPending || (_options.ProjectionCacheRefreshMinutes > 0
                    && Remaining(TimeSpan.FromMinutes(_options.ProjectionCacheRefreshMinutes), lastProjectionRefresh) == TimeSpan.Zero))
                {
                    await RefreshAsync(ct).ConfigureAwait(false);
                    lastProjectionRefresh = Clock.GetTimestamp();
                }
                else if (_seedService is { } seedService
                    && seedService.NextValidityTransitionUtc is { } dueBoundary && dueBoundary <= Clock.GetUtcNow())
                {
                    // Time alone changes eligibility: no projection write or notification occurs.
                    // Reseed the durable membership without forcing lazy caches to warm.
                    await seedService.SeedAsync(ct).ConfigureAwait(false);
                }
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
                retryStarted = Clock.GetTimestamp();
                _logger.LogWarning(ex, "Security Master projection/identifier refresh failed; retrying with backoff.");
                retryDelay = TimeSpan.FromMilliseconds(Math.Min(retryDelay.TotalMilliseconds * 2, 60_000));
            }
        }

        TimeSpan Remaining(TimeSpan interval, long started)
            => MaxZero(interval - Clock.GetElapsedTime(started));
    }

    private static TimeSpan MaxZero(TimeSpan delay) => delay < TimeSpan.Zero ? TimeSpan.Zero : delay;

    private static TimeSpan Earlier(TimeSpan first, TimeSpan second)
        => first == Timeout.InfiniteTimeSpan ? second
            : second == Timeout.InfiniteTimeSpan ? first
            : first < second ? first : second;

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
        if (_seedService is not null)
            _seedService.ValidityScheduleChanged -= OnValidityScheduleChanged;
        _refreshCts.Cancel();
        _refreshCts.Dispose();
    }
}
