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
    /// Adds one record to the live master, or updates the installed one when
    /// <paramref name="record"/> is not older than it.
    /// </summary>
    /// <remarks>
    /// Taken under the write gate, and resolving <see cref="Current"/> only once inside it, so an
    /// upsert issued while a <see cref="ReplaceAll"/> is assembling its replacement waits and then
    /// lands in the map that replacement installs. Writing outside the gate would put the record
    /// into the outgoing map, where the swap would discard it — losing a security that create,
    /// amend, or a published rebuild had just persisted.
    /// <para>
    /// Waiting on the gate is exactly what makes the version check necessary. A caller can produce
    /// its projection and then take a while to get here — <c>SecurityMasterService</c> releases its
    /// per-security amendment gate and awaits several times before it calls — so the record in hand
    /// may be older than one a rebuild installed in the meantime. An unconditional assignment would
    /// then downgrade that key. Equal versions take the incoming record, which is a no-op in
    /// content.
    /// </para>
    /// </remarks>
    public void Upsert(SecurityProjectionRecord record)
    {
        lock (_writeGate)
        {
            Current.AddOrUpdate(
                record.SecurityId,
                record,
                (_, installed) => record.Version >= installed.Version ? record : installed);
        }
    }

    /// <summary>
    /// Replaces the cached master with <paramref name="records"/> as a single reference swap.
    /// </summary>
    /// <remarks>
    /// <paramref name="records"/> is copied to an array before the gate is taken unless it already
    /// is one, so only the in-memory fill and the swap run under the gate. The check is for an
    /// array specifically, not a collection interface: a type can implement
    /// <see cref="IReadOnlyCollection{T}"/> and still enumerate lazily, and enumerating such a
    /// source under the gate would both do its work there and deadlock outright if it waits on
    /// anything that calls <see cref="Upsert"/> — the enumerator would hold the gate the writer
    /// needs. An array cannot enumerate lazily, so it is the only shape safe to skip the copy for.
    /// <para>
    /// An upsert that lands before this call takes the gate is still overwritten when the record it
    /// touched also appears in <paramref name="records"/> — the caller materialized that set before
    /// calling, so it may already be stale. That staleness is the caller's to resolve; it is not
    /// something the cache can see.
    /// </para>
    /// <para>
    /// This substitutes rather than merging by version, which is the deliberate asymmetry with
    /// <see cref="Upsert"/>: a rebuild replays the whole master from the event stream, so its set is
    /// authoritative as of its own snapshot, including about which securities are absent. Merging a
    /// newer straggler in would also resurrect every record the rebuild legitimately dropped.
    /// </para>
    /// </remarks>
    public void ReplaceAll(IEnumerable<SecurityProjectionRecord> records)
    {
        var materialized = records as SecurityProjectionRecord[] ?? records.ToArray();

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
