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

    public async Task WarmAsync(CancellationToken ct = default)
    {
        var rebuiltRecords = await BuildWarmSetAsync(ct).ConfigureAwait(false);
        _cache.ReplaceAll(rebuiltRecords);
        _logger.LogInformation(
            "Warmed security master projection cache with {Count} rebuilt records from snapshots/events",
            rebuiltRecords.Count);
    }
}
