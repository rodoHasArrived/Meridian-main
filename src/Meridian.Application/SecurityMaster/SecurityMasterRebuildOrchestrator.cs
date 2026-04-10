using System.Collections.Generic;
using System.Linq;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityMasterRebuildOrchestrator
{
    private const string ProjectionName = "security_master_cache";

    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ISecurityMasterStore _store;
    private readonly SecurityMasterProjectionCache _cache;
    private readonly SecurityMasterAggregateRebuilder _rebuilder;
    private readonly SecurityMasterProjectionService _projectionService;
    private readonly SecurityMasterOptions _options;
    private readonly ILogger<SecurityMasterRebuildOrchestrator> _logger;

    public SecurityMasterRebuildOrchestrator(
        ISecurityMasterEventStore eventStore,
        ISecurityMasterStore store,
        SecurityMasterProjectionCache cache,
        SecurityMasterAggregateRebuilder rebuilder,
        SecurityMasterProjectionService projectionService,
        SecurityMasterOptions options,
        ILogger<SecurityMasterRebuildOrchestrator> logger)
    {
        _eventStore = eventStore;
        _store = store;
        _cache = cache;
        _rebuilder = rebuilder;
        _projectionService = projectionService;
        _options = options;
        _logger = logger;
    }

    public async Task RebuildAsync(CancellationToken ct = default)
    {
        var checkpoint = await _store.GetCheckpointAsync(ProjectionName, ct).ConfigureAwait(false);
        var latestSequence = await _eventStore.GetLatestSequenceAsync(ct).ConfigureAwait(false);

        if (!_options.PreloadProjectionCache)
        {
            _logger.LogInformation("Security master rebuild skipped because preload is disabled.");
            return;
        }

        var persistedBatch = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        if (persistedBatch.Count > 0)
        {
            ReplaceCacheWithMetrics(persistedBatch);
            _logger.LogInformation(
                "Hydrated security master cache from persisted projections ({Count} records, checkpoint {Checkpoint})",
                persistedBatch.Count,
                checkpoint);
        }

        if (checkpoint is null || persistedBatch.Count == 0)
        {
            var rebuiltRecords = await _projectionService.BuildWarmSetAsync(ct).ConfigureAwait(false);
            await PersistBatchThenCheckpointAsync(rebuiltRecords, latestSequence, replaceAll: true, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Security master rebuild performed full warm from snapshots/events and checkpointed sequence {Sequence}",
                latestSequence);
            return;
        }

        if (checkpoint.Value >= latestSequence)
        {
            _logger.LogInformation(
                "Security master rebuild is already up to date at sequence {Sequence}",
                checkpoint.Value);
            return;
        }

        var cursor = checkpoint.Value;
        while (cursor < latestSequence)
        {
            var events = await _eventStore.LoadSinceSequenceAsync(cursor, _options.ProjectionReplayBatchSize, ct).ConfigureAwait(false);
            if (events.Count == 0)
            {
                break;
            }

            var rebuiltRecords = new List<SecurityProjectionRecord>(events.Count);
            foreach (var @event in events)
            {
                var projectionSeed = await _store.GetProjectionAsync(@event.SecurityId, ct).ConfigureAwait(false);
                var rebuilt = await _rebuilder.RebuildAsync(@event.SecurityId, projectionSeed, ct).ConfigureAwait(false);
                if (rebuilt is not null)
                {
                    rebuiltRecords.Add(rebuilt);
                }

                cursor = Math.Max(cursor, @event.GlobalSequence ?? cursor);
            }

            await PersistBatchThenCheckpointAsync(rebuiltRecords, cursor, replaceAll: false, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Security master rebuild replayed events through sequence {Sequence} using batch size {BatchSize}",
            cursor,
            _options.ProjectionReplayBatchSize);
    }

    private async Task PersistBatchThenCheckpointAsync(
        IReadOnlyList<SecurityProjectionRecord> records,
        long sequence,
        bool replaceAll,
        CancellationToken ct)
    {
        await _store.PersistProjectionBatchAsync(ProjectionName, sequence, records, ct).ConfigureAwait(false);

        if (replaceAll)
        {
            ReplaceCacheWithMetrics(records);
        }
        else
        {
            UpsertCacheWithMetrics(records);
        }

        await _store.SaveCheckpointAsync(ProjectionName, sequence, ct).ConfigureAwait(false);
    }

    private void ReplaceCacheWithMetrics(IReadOnlyCollection<SecurityProjectionRecord> records)
    {
        _cache.ReplaceAll(records);

        var expected = records.Select(r => r.SecurityId).Distinct().Count();
        var actual = _cache.Count;
        if (actual != expected)
        {
            _logger.LogWarning(
                "Projection cache hydrate mismatch after replace-all: expected {Expected} unique records, found {Actual}",
                expected,
                actual);
        }
    }

    private void UpsertCacheWithMetrics(IEnumerable<SecurityProjectionRecord> records)
    {
        var distinctIds = new HashSet<Guid>();
        foreach (var record in records)
        {
            distinctIds.Add(record.SecurityId);
            _cache.Upsert(record);
        }

        var missing = distinctIds.Count(id => _cache.Get(id) is null);
        if (missing > 0)
        {
            _logger.LogWarning(
                "Projection cache hydrate divergence: persisted {Persisted} records but {Missing} cache entries were missing after upsert",
                distinctIds.Count,
                missing);
        }
    }
}
