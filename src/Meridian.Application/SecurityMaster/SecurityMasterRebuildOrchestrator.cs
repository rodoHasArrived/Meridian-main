using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;
using Meridian.Contracts.SecurityMaster;

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

        if (checkpoint is null || _cache.Count == 0)
        {
            var rebuiltRecords = await _projectionService.BuildWarmSetAsync(ct).ConfigureAwait(false);
            await _store.PersistProjectionBatchAsync(ProjectionName, latestSequence, rebuiltRecords, ct).ConfigureAwait(false);
            _cache.ReplaceAll(rebuiltRecords);
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
            foreach (var rebuilt in rebuiltRecords)
            {
                _cache.Upsert(rebuilt);
            }
        }

        _logger.LogInformation(
            "Security master rebuild replayed events through sequence {Sequence} using batch size {BatchSize}",
            cursor,
            _options.ProjectionReplayBatchSize);
    }
}
