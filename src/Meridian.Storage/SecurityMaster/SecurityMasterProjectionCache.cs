using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public sealed class SecurityMasterProjectionCache
{
    // Writes are serialized on this gate; reads are not. ReplaceAll installs a whole new map by
    // reference instead of clearing in place, so a reader concurrent with a warm or rebuild sees
    // either the entire previous master or the entire new one — never the empty or half-filled
    // window a Clear()-then-refill exposes. Upserts accepted while a replacement is being built are
    // captured and replayed before the swap. Reads go through Volatile.Read so a reader cannot
    // observe the new map before the writes that populated it.
    private readonly Lock _writeGate = new();
    private readonly Lock _replacementGate = new();

    private ConcurrentDictionary<Guid, SecurityProjectionRecord> _byId = new();
    private Dictionary<Guid, SecurityProjectionRecord>? _upsertsDuringReplacement;

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
    /// Taken under the write gate, and resolving <see cref="Current"/> only once inside it. If a
    /// <see cref="ReplaceAll"/> is materializing or building its replacement outside that gate, an
    /// accepted upsert is also recorded for replay into the map that replacement installs. Once the
    /// replacement takes the gate to publish, the upsert waits and then lands directly in the newly
    /// installed map. Together those phases prevent a replacement from discarding a security that
    /// create, amend, or a published rebuild had just persisted.
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
            var current = Current;
            if (current.TryGetValue(record.SecurityId, out var installed)
                && record.Version < installed.Version)
            {
                return;
            }

            current[record.SecurityId] = record;
            if (_upsertsDuringReplacement is not null)
            {
                _upsertsDuringReplacement[record.SecurityId] = record;
            }
        }
    }

    /// <summary>
    /// Replaces the cached master with <paramref name="records"/> as a single reference swap.
    /// </summary>
    /// <remarks>
    /// Replacements are serialized with one another. <paramref name="records"/> is copied and the
    /// candidate map is built without holding the write gate, so a lazy source cannot deadlock a
    /// writer by waiting on work that calls <see cref="Upsert"/>. Before that work starts, this
    /// method opens a replacement-scoped capture under the write gate. Every accepted upsert during
    /// the copy or candidate build is replayed into the candidate by version before the reference
    /// swap, closing the lost-update window without merging untouched entries from the outgoing map.
    /// <para>
    /// A newer candidate record wins over an older captured upsert; an equal-version upsert wins as
    /// the later accepted write. Entries absent from both the replacement and the capture remain
    /// absent, preserving the replacement set's authority over deletions.
    /// </para>
    /// <para>
    /// The capture begins when this method acquires the replacement and write gates. Records that
    /// were already stale when the caller invoked <c>ReplaceAll</c> remain the caller's snapshot-
    /// boundary concern; this method does not merge writes that preceded its own capture.
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
        ArgumentNullException.ThrowIfNull(records);

        lock (_replacementGate)
        {
            var capturedUpserts = new Dictionary<Guid, SecurityProjectionRecord>();
            lock (_writeGate)
            {
                if (_upsertsDuringReplacement is not null)
                {
                    throw new InvalidOperationException("A projection-cache replacement is already active.");
                }

                _upsertsDuringReplacement = capturedUpserts;
            }

            try
            {
                var materialized = records as SecurityProjectionRecord[] ?? records.ToArray();
                var replacement = new ConcurrentDictionary<Guid, SecurityProjectionRecord>();
                foreach (var record in materialized)
                {
                    replacement[record.SecurityId] = record;
                }

                lock (_writeGate)
                {
                    foreach (var upsert in capturedUpserts.Values)
                    {
                        replacement.AddOrUpdate(
                            upsert.SecurityId,
                            upsert,
                            (_, candidate) => upsert.Version >= candidate.Version ? upsert : candidate);
                    }

                    Volatile.Write(ref _byId, replacement);
                    _upsertsDuringReplacement = null;
                }
            }
            finally
            {
                lock (_writeGate)
                {
                    if (ReferenceEquals(_upsertsDuringReplacement, capturedUpserts))
                    {
                        _upsertsDuringReplacement = null;
                    }
                }
            }
        }
    }

    public IReadOnlyCollection<SecurityProjectionRecord> Snapshot()
        => Current.Values.ToArray();
}
