using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public sealed class SecurityMasterProjectionCache
{
    // Writes are serialized on this gate; reads are not. ReplaceAll installs a whole new map by
    // reference instead of clearing in place, so a reader concurrent with a warm or rebuild sees
    // either the entire previous master or the entire new one — never the empty or half-filled
    // window a Clear()-then-refill exposes. Reads go through Volatile.Read so a reader cannot
    // observe the new map before the writes that populated it.
    private readonly Lock _writeGate = new();

    private ConcurrentDictionary<Guid, SecurityProjectionRecord> _byId = new();

    private ConcurrentDictionary<Guid, SecurityProjectionRecord> Current
        => Volatile.Read(ref _byId);

    public int Count => Current.Count;

    public SecurityProjectionRecord? Get(Guid securityId)
        => Current.TryGetValue(securityId, out var record) ? record : null;

    /// <summary>
    /// Adds or updates one record in the live master.
    /// </summary>
    /// <remarks>
    /// Taken under the write gate, and resolving <see cref="Current"/> only once inside it, so an
    /// upsert issued while a <see cref="ReplaceAll"/> is assembling its replacement waits and then
    /// lands in the map that replacement installs. Writing outside the gate would put the record
    /// into the outgoing map, where the swap would discard it — losing a security that create,
    /// amend, or a published rebuild had just persisted.
    /// </remarks>
    public void Upsert(SecurityProjectionRecord record)
    {
        lock (_writeGate)
        {
            Current[record.SecurityId] = record;
        }
    }

    /// <summary>
    /// Replaces the cached master with <paramref name="records"/> as a single reference swap.
    /// </summary>
    /// <remarks>
    /// <paramref name="records"/> is materialized before the gate is taken, so a lazily-evaluated
    /// source cannot hold writers off while it does I/O; only the in-memory fill and the swap run
    /// under the gate.
    /// <para>
    /// An upsert that lands before this call takes the gate is still overwritten when the record it
    /// touched also appears in <paramref name="records"/> — the caller materialized that set before
    /// calling, so it may already be stale. That staleness is the caller's to resolve; it is not
    /// something the cache can see.
    /// </para>
    /// </remarks>
    public void ReplaceAll(IEnumerable<SecurityProjectionRecord> records)
    {
        var materialized = records as IReadOnlyCollection<SecurityProjectionRecord> ?? records.ToArray();

        lock (_writeGate)
        {
            var replacement = new ConcurrentDictionary<Guid, SecurityProjectionRecord>();
            foreach (var record in materialized)
            {
                replacement[record.SecurityId] = record;
            }

            Volatile.Write(ref _byId, replacement);
        }
    }

    public IReadOnlyCollection<SecurityProjectionRecord> Snapshot()
        => Current.Values.ToArray();
}
