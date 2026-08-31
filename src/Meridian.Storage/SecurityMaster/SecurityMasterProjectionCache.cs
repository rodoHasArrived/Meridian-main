using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Per-process warm-read cache over the durable projection store. The durable store remains the
/// authority — the cache is a latency optimisation, and consumers fall back to the database on a
/// miss.
///
/// <para><see cref="ReplaceAll"/> swaps in a fully-populated replacement dictionary ATOMICALLY: the
/// previous clear-then-fill implementation exposed an empty master to any reader that arrived
/// between the clear and the repopulation, so a full re-warm looked like a mass delisting. An
/// <see cref="Upsert"/> racing a <see cref="ReplaceAll"/> may land in the outgoing dictionary and
/// be superseded by the replacement — acceptable, because ReplaceAll is only ever fed a complete
/// rebuild from the durable store, which already contains any write committed before the rebuild
/// read.</para>
///
/// <para>Multi-node deployments: publishes on one node do not invalidate another node's cache.
/// Coherence is bounded by the periodic re-warm
/// (<c>SecurityMasterOptions.ProjectionCacheRefreshMinutes</c>, run by
/// <c>SecurityMasterProjectionWarmupService</c>) plus the per-security rebuild each node performs
/// on its own writes; reads that must be authoritative (governed workflows) go to the durable
/// store, not this cache.</para>
/// </summary>
public sealed class SecurityMasterProjectionCache
{
    private volatile ConcurrentDictionary<Guid, SecurityProjectionRecord> _byId = new();

    public int Count => _byId.Count;

    public SecurityProjectionRecord? Get(Guid securityId)
        => _byId.TryGetValue(securityId, out var record) ? record : null;

    public void Upsert(SecurityProjectionRecord record)
        => _byId[record.SecurityId] = record;

    /// <summary>Evicts one security (e.g. after a purge); a no-op when it was not cached.</summary>
    public bool Remove(Guid securityId)
        => _byId.TryRemove(securityId, out _);

    /// <summary>
    /// Atomically replaces the entire cached set: the replacement is fully populated before the
    /// reference swap, so concurrent readers observe either the complete old set or the complete
    /// new one — never an empty or partially-filled master.
    /// </summary>
    public void ReplaceAll(IEnumerable<SecurityProjectionRecord> records)
    {
        var replacement = new ConcurrentDictionary<Guid, SecurityProjectionRecord>();
        foreach (var record in records)
        {
            replacement[record.SecurityId] = record;
        }

        _byId = replacement;
    }

    public IReadOnlyCollection<SecurityProjectionRecord> Snapshot()
        => _byId.Values.ToArray();
}
