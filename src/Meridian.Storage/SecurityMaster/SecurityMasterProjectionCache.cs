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
/// <see cref="Upsert"/> accepted while the replacement is being built is captured and replayed by
/// version into the map the replacement installs, so a security persisted by create, amend, or a
/// published rebuild cannot be discarded by a concurrent warm; and an upsert older than the record
/// a rebuild installed does not downgrade it.</para>
///
/// <para>Multi-node deployments: each node upserts its own writes directly, and every committed
/// projection write also emits a PostgreSQL <c>NOTIFY</c> from inside its transaction
/// (<see cref="PostgresSecurityMasterStore"/>, channel
/// <see cref="SecurityProjectionChangeNotification.ChannelFor"/>), so it is delivered only on
/// commit and never for a rolled-back write. Every other node's
/// <c>SecurityMasterProjectionChangeListener</c> re-reads the announced security from the durable
/// store and applies it through the version-guarded <see cref="Upsert"/> (or <see cref="Remove"/>
/// when it is gone), ignoring its own node's notifications. Cross-node staleness is therefore
/// bounded by notification delivery, not by a timer. PostgreSQL does not replay notifications to a
/// disconnected listener, so after the listener reconnects it re-synchronises the whole cache; the
/// periodic re-warm (<c>SecurityMasterOptions.ProjectionCacheRefreshMinutes</c>, run by
/// <c>SecurityMasterProjectionWarmupService</c>) remains a backstop, and
/// <c>SecurityMasterOptions.ProjectionCacheNotificationsEnabled = false</c> falls back to it alone.
/// Reads that must be authoritative (governed workflows) still go to the durable store, not this
/// cache.</para>
/// </summary>
public sealed class SecurityMasterProjectionCache
{
    // Writes are serialized on this gate; reads are not. ReplaceAll installs a whole new map by
    // reference instead of clearing in place, so a reader concurrent with a warm or rebuild sees
    // either the entire previous master or the entire new one — never the empty or half-filled
    // window a Clear()-then-refill exposes. Upserts accepted while a replacement is being built are
    // captured and replayed before the swap. Reads go through Volatile.Read so a reader cannot
    // observe the new map before the writes that populated it.
    private readonly Lock _writeGate = new();
    private readonly SemaphoreSlim _replacementGate = new(1, 1);

    private ConcurrentDictionary<Guid, SecurityProjectionRecord> _byId = new();

    /// <summary>
    /// Orders "reread the durable projection, then upsert it" refreshes against each other. Alias
    /// writes keep the projection version and <see cref="Upsert"/> accepts an equal version, so two
    /// refreshes of the same security — a local alias write and a remote notification, or two
    /// local writes — must not interleave, or an earlier reread can land last. Every such refresh
    /// path holds this gate across its reread and upsert.
    /// </summary>
    public SemaphoreSlim RefreshGate { get; } = new(1, 1);
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
            if (current.TryGetValue(record.SecurityId, out var installed))
            {
                if (record.Version < installed.Version)
                {
                    return;
                }

                // Aliases do not move the version, so an equal or newer record can still carry an
                // alias list read before the installed one; keep the newer-stamped aliases.
                record = SecurityAliasSnapshot.KeepNewerAliases(record, installed);
            }

            current[record.SecurityId] = record;
            if (_upsertsDuringReplacement is not null)
            {
                _upsertsDuringReplacement[record.SecurityId] = record;
            }
        }
    }

    /// <summary>Evicts one security (e.g. after a purge); a no-op when it was not cached.</summary>
    /// <remarks>
    /// Write-gated so it also drops any upsert captured for the key while a replacement is being
    /// built — without that, the replay would resurrect the record the caller just evicted. A
    /// replacement whose source read preceded the underlying delete can still reinstall the record
    /// until the next re-warm: the replacement set is authoritative about membership, and reads
    /// that must be authoritative go to the durable store.
    /// </remarks>
    public bool Remove(Guid securityId)
    {
        lock (_writeGate)
        {
            _upsertsDuringReplacement?.Remove(securityId);
            return Current.TryRemove(securityId, out _);
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

        _replacementGate.Wait();
        try
        {
            var capturedUpserts = BeginCapture();
            try
            {
                Install(records as SecurityProjectionRecord[] ?? records.ToArray(), capturedUpserts);
            }
            finally
            {
                EndCapture(capturedUpserts);
            }
        }
        finally
        {
            _replacementGate.Release();
        }
    }

    /// <summary>
    /// Builds the replacement with <paramref name="buildReplacement"/> and installs it as
    /// <see cref="ReplaceAll"/> does, but opens the upsert capture BEFORE the build reads its source.
    /// A local create or amend whose cache upsert lands while a warm is still reading or rebuilding
    /// is therefore replayed into the replacement even when the warm's read preceded that write's
    /// commit, instead of being dropped or downgraded by the swap.
    /// </summary>
    public async Task<IReadOnlyList<SecurityProjectionRecord>> ReplaceAllAsync(
        Func<CancellationToken, Task<IReadOnlyList<SecurityProjectionRecord>>> buildReplacement,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(buildReplacement);

        await _replacementGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var capturedUpserts = BeginCapture();
            try
            {
                var records = await buildReplacement(ct).ConfigureAwait(false);
                Install(records, capturedUpserts);
                return records;
            }
            finally
            {
                EndCapture(capturedUpserts);
            }
        }
        finally
        {
            _replacementGate.Release();
        }
    }

    private Dictionary<Guid, SecurityProjectionRecord> BeginCapture()
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

        return capturedUpserts;
    }

    private void Install(
        IReadOnlyCollection<SecurityProjectionRecord> records,
        Dictionary<Guid, SecurityProjectionRecord> capturedUpserts)
    {
        var replacement = new ConcurrentDictionary<Guid, SecurityProjectionRecord>();
        foreach (var record in records)
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
                    (_, candidate) => upsert.Version >= candidate.Version
                        ? SecurityAliasSnapshot.KeepNewerAliases(upsert, candidate)
                        : SecurityAliasSnapshot.KeepNewerAliases(candidate, upsert));
            }

            // The replacement is authoritative about membership and terms as of its snapshot,
            // but an alias correction refreshed into the outgoing map after that snapshot was
            // read must not be rolled back by the swap.
            var outgoing = Current;
            foreach (var (securityId, candidate) in replacement)
            {
                if (outgoing.TryGetValue(securityId, out var installed))
                {
                    replacement[securityId] = SecurityAliasSnapshot.KeepNewerAliases(candidate, installed);
                }
            }

            Volatile.Write(ref _byId, replacement);
            _upsertsDuringReplacement = null;
        }
    }

    private void EndCapture(Dictionary<Guid, SecurityProjectionRecord> capturedUpserts)
    {
        lock (_writeGate)
        {
            if (ReferenceEquals(_upsertsDuringReplacement, capturedUpserts))
            {
                _upsertsDuringReplacement = null;
            }
        }
    }

    public IReadOnlyCollection<SecurityProjectionRecord> Snapshot()
        => Current.Values.ToArray();
}
