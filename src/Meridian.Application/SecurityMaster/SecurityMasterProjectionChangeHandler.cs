using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>What <see cref="SecurityMasterProjectionChangeHandler"/> did with one notification.</summary>
public enum SecurityProjectionChangeOutcome
{
    /// <summary>The payload could not be parsed; it was logged and ignored.</summary>
    Malformed,

    /// <summary>The notification came from this node, whose own write path already updated the cache.</summary>
    IgnoredOwnNode,

    /// <summary>The security was re-read from the durable store and installed.</summary>
    Refreshed,

    /// <summary>The durable re-read was older than the cached record, which was kept.</summary>
    KeptNewer,

    /// <summary>The security is gone from the durable store and was evicted (or was never cached).</summary>
    Evicted,

    /// <summary>The whole cache was re-synchronised from the durable store.</summary>
    Resynchronized,

    /// <summary>The durable re-read failed; the listener schedules a full resynchronization retry.</summary>
    Failed
}

/// <summary>
/// Applies cross-node Security Master projection-change notifications to this node's
/// <see cref="SecurityMasterProjectionCache"/>. Separated from the listener's connection handling so
/// the decision logic is testable without PostgreSQL.
/// </summary>
public sealed class SecurityMasterProjectionChangeHandler
{
    private const int MaxLoggedPayloadLength = 128;

    private readonly SecurityMasterProjectionService _projectionService;
    private readonly SecurityMasterProjectionCache _cache;
    private readonly SecurityMasterNodeIdentity _nodeIdentity;
    private readonly SecurityMasterOptions _options;
    private readonly ILogger<SecurityMasterProjectionChangeHandler> _logger;
    private readonly SecurityMasterCanonicalSymbolSeedService? _seedService;

    // Securities whose notification reread failed. A lazily-populated cache does not hold a security
    // created on another node until its notification is applied, so the resync scheduled after the
    // failure retries these directly; walking the cache snapshot alone would never revisit them.
    private readonly ConcurrentDictionary<Guid, byte> _failedSecurityIds = new();

    public SecurityMasterProjectionChangeHandler(
        SecurityMasterProjectionService projectionService,
        SecurityMasterProjectionCache cache,
        SecurityMasterNodeIdentity nodeIdentity,
        SecurityMasterOptions options,
        ILogger<SecurityMasterProjectionChangeHandler> logger,
        SecurityMasterCanonicalSymbolSeedService? seedService = null)
    {
        _projectionService = projectionService;
        _cache = cache;
        _nodeIdentity = nodeIdentity;
        _options = options;
        _logger = logger;
        _seedService = seedService;
    }

    public Task<SecurityProjectionChangeOutcome> HandleAsync(string? payload, CancellationToken ct = default)
        => HandleAsync(payload, reseedCanonicalSymbols: true, ct);

    /// <summary>
    /// Applies one notification. With <paramref name="reseedCanonicalSymbols"/> false the caller
    /// owns reseeding: the listener drains a batch of notifications and reseeds the canonical
    /// registry once afterwards, rather than rescanning the cache and rewriting the registry per
    /// notification.
    /// </summary>
    public async Task<SecurityProjectionChangeOutcome> HandleAsync(
        string? payload,
        bool reseedCanonicalSymbols,
        CancellationToken ct = default)
    {
        if (!SecurityProjectionChangeNotification.TryParse(payload, out var notification) || notification is null)
        {
            _logger.LogWarning(
                "Ignoring malformed Security Master projection-change notification payload {Payload}",
                Truncate(payload));
            return SecurityProjectionChangeOutcome.Malformed;
        }

        if (notification.OriginNodeId == _nodeIdentity.NodeId)
        {
            return SecurityProjectionChangeOutcome.IgnoredOwnNode;
        }

        try
        {
            if (notification.Kind == SecurityProjectionChangeKind.Resync)
            {
                await ResynchronizeAsync(ct).ConfigureAwait(false);
                return SecurityProjectionChangeOutcome.Resynchronized;
            }

            // Every notification rereads, even one older than the cached version: aliases have
            // their own revision history, so a cached record at a newer version can still carry
            // aliases read before the change this notification announces. The reread's version and
            // alias-stamp guards keep whichever terms and aliases are newer.
            // Shares the cache's refresh gate with local alias writes, so a remote reread cannot
            // interleave with a local one and land a superseded equal-version record last.
            SecurityProjectionRefreshResult result;
            await _cache.RefreshGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                result = await _projectionService.RefreshSecurityAsync(notification.SecurityId, ct).ConfigureAwait(false);
            }
            finally
            {
                _cache.RefreshGate.Release();
            }

            _failedSecurityIds.TryRemove(notification.SecurityId, out _);
            _logger.LogDebug(
                "Applied Security Master projection change for {SecurityId} version {Version} from node {OriginNodeId}: {Result}",
                notification.SecurityId,
                notification.Version,
                notification.OriginNodeId,
                result);
            if (reseedCanonicalSymbols && result != SecurityProjectionRefreshResult.KeptNewer)
            {
                // Local writes reseed the canonical registry after touching the cache; a remote
                // change to a primary ticker or provider alias must reach the hot-path lookup too.
                await ReseedCanonicalSymbolsAsync(ct).ConfigureAwait(false);
            }

            return result switch
            {
                SecurityProjectionRefreshResult.Refreshed => SecurityProjectionChangeOutcome.Refreshed,
                SecurityProjectionRefreshResult.KeptNewer => SecurityProjectionChangeOutcome.KeptNewer,
                _ => SecurityProjectionChangeOutcome.Evicted
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (notification.Kind != SecurityProjectionChangeKind.Resync)
            {
                _failedSecurityIds.TryAdd(notification.SecurityId, 0);
            }

            _logger.LogWarning(
                ex,
                "Failed to apply Security Master projection change {Kind} for {SecurityId} from node {OriginNodeId}; the listener will schedule a resynchronization retry",
                notification.Kind,
                notification.SecurityId,
                notification.OriginNodeId);
            return SecurityProjectionChangeOutcome.Failed;
        }
    }

    /// <summary>Reseeds the canonical symbol registry from the current cache, when one is wired.</summary>
    public Task ReseedCanonicalSymbolsAsync(CancellationToken ct = default)
        => _seedService is null ? Task.CompletedTask : _seedService.SeedAsync(ct);

    /// <summary>
    /// Re-synchronises the whole cache after notifications may have been missed (a listener
    /// reconnect) or after a bulk publish on another node. A pre-warmed cache is re-warmed with the
    /// atomic <c>ReplaceAll</c> swap; a lazily-populated cache (pre-warm disabled) refreshes the
    /// securities it already holds plus any whose notification reread failed, so a resync keeps its
    /// memory profile without losing a remote creation whose first reread failed.
    /// </summary>
    public async Task ResynchronizeAsync(CancellationToken ct = default)
    {
        // Taken before the reads below: a failure recorded while they run stays pending for the
        // next resync instead of being cleared by a read that may have preceded its write.
        var failedSecurityIds = _failedSecurityIds.Keys.ToArray();
        if (_options.PreloadProjectionCache)
        {
            // WarmAsync holds the cache's refresh gate across its durable read and swap.
            await _projectionService.WarmAsync(ct).ConfigureAwait(false);
            foreach (var securityId in failedSecurityIds)
            {
                _failedSecurityIds.TryRemove(securityId, out _);
            }
        }
        else
        {
            // Cached securities plus those whose notification failed before they were ever cached.
            var securityIds = _cache.Snapshot().Select(static record => record.SecurityId)
                .Concat(failedSecurityIds)
                .Distinct()
                .ToArray();
            foreach (var securityId in securityIds)
            {
                ct.ThrowIfCancellationRequested();
                await _cache.RefreshGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await _projectionService.RefreshSecurityAsync(securityId, ct).ConfigureAwait(false);
                }
                finally
                {
                    _cache.RefreshGate.Release();
                }

                _failedSecurityIds.TryRemove(securityId, out _);
            }
        }

        await ReseedCanonicalSymbolsAsync(ct).ConfigureAwait(false);
    }

    private static string Truncate(string? payload)
        => payload is null
            ? "<null>"
            : payload.Length <= MaxLoggedPayloadLength ? payload : payload[..MaxLoggedPayloadLength] + "...";
}
