using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityMasterProjectionService
{
    private readonly ISecurityMasterStore _store;
    private readonly SecurityMasterProjectionCache _cache;
    private readonly SecurityMasterAggregateRebuilder _rebuilder;
    private readonly ILogger<SecurityMasterProjectionService> _logger;

    public SecurityMasterProjectionService(
        ISecurityMasterStore store,
        SecurityMasterProjectionCache cache,
        SecurityMasterAggregateRebuilder rebuilder,
        ILogger<SecurityMasterProjectionService> logger)
    {
        _store = store;
        _cache = cache;
        _rebuilder = rebuilder;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SecurityProjectionRecord>> BuildWarmSetAsync(CancellationToken ct = default)
    {
        var seedRecords = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        var rebuiltRecords = new List<SecurityProjectionRecord>(seedRecords.Count);

        foreach (var seed in seedRecords)
        {
            var rebuiltEconomic = await _rebuilder.RebuildEconomicDefinitionAsync(seed.SecurityId, seed, ct).ConfigureAwait(false);
            var rebuilt = rebuiltEconomic is null
                ? null
                : SecurityEconomicDefinitionAdapter.ToProjection(rebuiltEconomic, seed.Aliases);
            if (rebuilt is not null)
            {
                rebuiltRecords.Add(rebuilt);
            }
        }

        return rebuiltRecords;
    }

    /// <summary>
    /// Re-reads one security from the durable store, rebuilds it exactly as a warm would, and
    /// applies it to the cache through the version-guarded <see cref="SecurityMasterProjectionCache.Upsert"/>
    /// (so an older read never downgrades a newer cached record). A security the durable store no
    /// longer holds is evicted; deactivated securities stay cached with their inactive status, as
    /// a full warm leaves them.
    /// </summary>
    public async Task<SecurityProjectionRefreshResult> RefreshSecurityAsync(Guid securityId, CancellationToken ct = default)
    {
        var seed = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        var rebuiltEconomic = await _rebuilder.RebuildEconomicDefinitionAsync(securityId, seed, ct).ConfigureAwait(false);
        if (seed is null || rebuiltEconomic is null)
        {
            return _cache.Remove(securityId)
                ? SecurityProjectionRefreshResult.Evicted
                : SecurityProjectionRefreshResult.NotFound;
        }

        var rebuilt = SecurityEconomicDefinitionAdapter.ToProjection(rebuiltEconomic, seed.Aliases);
        _cache.Upsert(rebuilt);
        return _cache.Get(securityId)?.Version == rebuilt.Version
            ? SecurityProjectionRefreshResult.Refreshed
            : SecurityProjectionRefreshResult.KeptNewer;
    }

    public async Task WarmAsync(CancellationToken ct = default)
    {
        var rebuiltRecords = await BuildWarmSetAsync(ct).ConfigureAwait(false);
        _cache.ReplaceAll(rebuiltRecords);
        _logger.LogInformation(
            "Warmed security master projection cache with {Count} rebuilt records from snapshots/events",
            rebuiltRecords.Count);
    }
}

/// <summary>Outcome of <see cref="SecurityMasterProjectionService.RefreshSecurityAsync"/>.</summary>
public enum SecurityProjectionRefreshResult
{
    /// <summary>The durable record was installed in the cache.</summary>
    Refreshed,

    /// <summary>The cache already held a newer version; the durable read did not downgrade it.</summary>
    KeptNewer,

    /// <summary>The durable store no longer holds the security; the cached entry was evicted.</summary>
    Evicted,

    /// <summary>Neither the durable store nor the cache holds the security.</summary>
    NotFound
}
