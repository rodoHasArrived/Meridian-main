using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster.Rebuild;

/// <summary>
/// Published-revision side effect (<c>Order = 10</c>, runs first): refreshes the edited security's
/// cached economic projection from the durable event stream so reads taken after the publish are
/// coherent with the persisted stream.
///
/// <para>Rebuilds <b>only the edited security</b> — <see cref="SecurityMasterAggregateRebuilder.RebuildAsync"/>
/// folds the snapshot plus the tail events for that single stream (the per-edit hot path) and upserts
/// the result into the shared <see cref="SecurityMasterProjectionCache"/>. It deliberately does NOT
/// trigger a full multi-asset / UFL replay, which is reserved for ingest/maintenance.</para>
///
/// <para>Idempotent: a retried publish rebuilds and upserts the same cache key. The rebuilder and cache
/// are optional dependencies (registered only when the Security Master backend is configured); with no
/// backend there is no projection cache to refresh and the handler is a no-op.</para>
/// </summary>
public sealed class SecurityProjectionRebuildHandler : ISecurityMasterRevisionPublishedHandler
{
    private readonly SecurityMasterAggregateRebuilder? _rebuilder;
    private readonly SecurityMasterProjectionCache? _cache;
    private readonly ILogger<SecurityProjectionRebuildHandler> _logger;

    public SecurityProjectionRebuildHandler(
        ILogger<SecurityProjectionRebuildHandler> logger,
        SecurityMasterAggregateRebuilder? rebuilder = null,
        SecurityMasterProjectionCache? cache = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rebuilder = rebuilder;
        _cache = cache;
    }

    public int Order => 10;

    public async Task HandleAsync(SecurityMasterRevisionPublishedEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (_rebuilder is null || _cache is null)
        {
            // No Security Master backend is configured, so there is no projection cache to refresh.
            return;
        }

        // Seed from the cached projection to preserve aliases; the rebuild folds the snapshot plus the
        // tail events for this one security only.
        var seed = _cache.Get(evt.SecurityId);
        var rebuilt = await _rebuilder.RebuildAsync(evt.SecurityId, seed, ct).ConfigureAwait(false);
        if (rebuilt is null)
        {
            _logger.LogWarning(
                "Published security {SecurityId} (revision {RevisionId}) could not be rebuilt into a projection; cache left unchanged.",
                evt.SecurityId, evt.RevisionId);
            return;
        }

        _cache.Upsert(rebuilt);
        _logger.LogInformation(
            "Refreshed cached projection for published security {SecurityId} (revision {RevisionId}).",
            evt.SecurityId, evt.RevisionId);
    }
}
