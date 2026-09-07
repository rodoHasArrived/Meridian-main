using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster.Rebuild;

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
    private readonly ISecurityMasterConflictService? _conflictService;

    public SecurityMasterRebuildOrchestrator(
        ISecurityMasterEventStore eventStore,
        ISecurityMasterStore store,
        SecurityMasterProjectionCache cache,
        SecurityMasterAggregateRebuilder rebuilder,
        SecurityMasterProjectionService projectionService,
        SecurityMasterOptions options,
        ILogger<SecurityMasterRebuildOrchestrator> logger,
        ISecurityMasterConflictService? conflictService = null)
    {
        _eventStore = eventStore;
        _store = store;
        _cache = cache;
        _rebuilder = rebuilder;
        _projectionService = projectionService;
        _options = options;
        _logger = logger;
        _conflictService = conflictService;
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

        if (checkpoint is null || _cache.Count == 0)
        {
            var rebuiltRecords = await _projectionService.BuildWarmSetAsync(ct).ConfigureAwait(false);
            await _store.PersistProjectionBatchAsync(ProjectionName, latestSequence, rebuiltRecords, ct).ConfigureAwait(false);
            var deferredWarmSecurityIds = await TryRecordConflictsAsync(rebuiltRecords, ct).ConfigureAwait(false);
            _cache.ReplaceAll(rebuiltRecords);
            await RetryDeferredConflictDetectionAsync(deferredWarmSecurityIds, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Security master rebuild performed full warm and checkpointed sequence {Sequence}",
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
        var deferredConflictSecurityIds = new HashSet<Guid>();
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

            await _store.PersistProjectionBatchAsync(ProjectionName, cursor, rebuiltRecords, ct).ConfigureAwait(false);
            deferredConflictSecurityIds.UnionWith(await TryRecordConflictsAsync(rebuiltRecords, ct).ConfigureAwait(false));
            foreach (var rebuilt in rebuiltRecords)
            {
                _cache.Upsert(rebuilt);
            }
        }

        await RetryDeferredConflictDetectionAsync(deferredConflictSecurityIds, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Security master rebuild replayed events through sequence {Sequence} using batch size {BatchSize}",
            cursor,
            _options.ProjectionReplayBatchSize);
    }

    /// <summary>
    /// Rebuilds ONLY the securities whose stored projection carries <paramref name="assetClass"/>,
    /// bounding the rebuild cost to that class's population instead of a full shared replay. The
    /// shared replay checkpoint is deliberately untouched — the scoped rebuild is a repair/refresh
    /// of one class's projections (event-stream fold, store upsert, cache upsert), not a
    /// stream-cursor advance, so it can never mark unrelated classes' events as replayed.
    /// Returns the number of projections rebuilt.
    /// </summary>
    public async Task<int> RebuildAssetClassAsync(string assetClass, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetClass);

        var seeds = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        var rebuiltRecords = new List<SecurityProjectionRecord>();
        foreach (var seed in seeds)
        {
            if (!string.Equals(seed.AssetClass, assetClass, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rebuilt = await _rebuilder.RebuildAsync(seed.SecurityId, seed, ct).ConfigureAwait(false);
            if (rebuilt is not null)
            {
                rebuiltRecords.Add(rebuilt);
            }
        }

        foreach (var rebuilt in rebuiltRecords)
        {
            await _store.UpsertProjectionAsync(rebuilt, ct).ConfigureAwait(false);
            _cache.Upsert(rebuilt);
        }

        var deferredSecurityIds = await TryRecordConflictsAsync(rebuiltRecords, ct).ConfigureAwait(false);
        await RetryDeferredConflictDetectionAsync(deferredSecurityIds, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Security master scoped rebuild refreshed {Count} projection(s) for asset class {AssetClass}",
            rebuiltRecords.Count,
            assetClass);
        return rebuiltRecords.Count;
    }

    /// <summary>
    /// Best-effort conflict detection for a persisted batch. The replay checkpoint has already
    /// advanced when this runs, so a swallowed failure would leave the batch's ambiguities
    /// undetected by every later rebuild. The failed batch is therefore returned to the caller
    /// for an end-of-run retry instead of being dropped.
    /// </summary>
    private async Task<IReadOnlyCollection<Guid>> TryRecordConflictsAsync(
        IReadOnlyList<SecurityProjectionRecord> rebuiltRecords,
        CancellationToken ct)
    {
        if (_conflictService is null || rebuiltRecords.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        try
        {
            await _conflictService.RecordConflictsForProjectionsAsync(rebuiltRecords, ct).ConfigureAwait(false);
            return Array.Empty<Guid>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Conflict detection failed during projection rebuild for {SecurityCount} securities; the batch is held for one retry after replay",
                rebuiltRecords.Count);

            // Only the ids are held: the retry reloads each projection as persisted anyway, and
            // retaining the full records of every failed batch for the length of a large replay
            // (repeated events included) would defeat ProjectionReplayBatchSize memory bounding
            // during a conflict-service outage.
            return rebuiltRecords.Select(static record => record.SecurityId).ToArray();
        }
    }

    /// <summary>
    /// One end-of-run retry for batches whose conflict detection failed mid-replay. The rebuild
    /// itself stays resilient to a conflict-service outage, but a persistent failure is surfaced
    /// at error level naming the affected population, because the advanced checkpoint means no
    /// later replay will re-detect these records without a full conflict refresh.
    /// </summary>
    private async Task RetryDeferredConflictDetectionAsync(
        IReadOnlyCollection<Guid> deferredSecurityIds,
        CancellationToken ct)
    {
        if (_conflictService is null || deferredSecurityIds.Count == 0)
        {
            return;
        }

        // A deferred security can have been rebuilt again by a later batch whose scan
        // succeeded, so only its id was held. Each deferred security is re-read from the
        // store and the retry scans the projection as persisted now; one that no longer
        // exists has nothing left to scan. The reloads share the retry's best-effort
        // boundary — the batch and checkpoint are already committed — but each failure is
        // contained per security: one transient read aborting the loop would abandon the
        // scan for records already reloaded and ids not yet read, and no later replay will
        // re-detect any of them.
        var records = new List<SecurityProjectionRecord>();
        foreach (var securityId in deferredSecurityIds.Distinct())
        {
            try
            {
                var current = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false);
                if (current is not null)
                {
                    records.Add(current);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Deferred conflict-detection reload failed for security {SecurityId}; it carries no ambiguity scan and needs a full conflict refresh",
                    securityId);
            }
        }

        if (records.Count == 0)
        {
            return;
        }

        try
        {
            await _conflictService.RecordConflictsForProjectionsAsync(records, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Deferred conflict detection succeeded on retry for {SecurityCount} securities",
                records.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Conflict detection failed twice during projection rebuild; {SecurityCount} rebuilt securities carry no ambiguity scan and need a full conflict refresh",
                records.Count);
        }
    }
}
