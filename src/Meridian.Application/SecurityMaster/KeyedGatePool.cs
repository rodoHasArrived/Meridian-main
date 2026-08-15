using System.Collections.Concurrent;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Keyed async mutual exclusion whose entries live only while in use: every acquisition
/// reference-counts its key's semaphore and the LAST release removes and disposes the entry. The
/// static per-security gate dictionaries this replaces retained one <see cref="SemaphoreSlim"/>
/// per security ever touched for the life of the process, so ingestion and corporate-action
/// workloads grew memory with the security universe.
/// </summary>
internal sealed class KeyedGatePool<TKey> where TKey : notnull
{
    internal sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int RefCount = 1;
    }

    private readonly ConcurrentDictionary<TKey, Entry> _entries = new();

    /// <summary>Entries currently held or awaited; exposed so tests can assert reclamation.</summary>
    internal int ActiveEntryCount => _entries.Count;

    /// <summary>
    /// Acquires the key's gate, waiting until any current holder releases it. Dispose the returned
    /// releaser to release the gate and drop the reference; the last reference retires the entry.
    /// </summary>
    public async Task<Releaser> AcquireAsync(TKey key, CancellationToken ct)
    {
        var entry = ReserveEntry(key);
        try
        {
            await entry.Semaphore.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private Entry ReserveEntry(TKey key)
    {
        while (true)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                // A racing LAST release may have retired this entry (RefCount 0, about to be
                // removed and disposed): the CAS only revives live counts, and a retired entry is
                // retried until its removal makes room for a fresh one. Interlocked operations on
                // the count serialize the race — either the reservation lands before the count
                // reaches zero, or it observes zero and retries.
                var count = Volatile.Read(ref existing.RefCount);
                if (count > 0 && Interlocked.CompareExchange(ref existing.RefCount, count + 1, count) == count)
                {
                    return existing;
                }

                continue;
            }

            var created = new Entry();
            if (_entries.TryAdd(key, created))
            {
                return created;
            }
        }
    }

    private void ReleaseReference(TKey key, Entry entry)
    {
        if (Interlocked.Decrement(ref entry.RefCount) == 0)
        {
            // No holders and no waiters remain (each holds a reference), so the semaphore can be
            // removed and disposed; a concurrent reservation that still sees the retired entry
            // observes RefCount 0 and creates a fresh one after the removal.
            _entries.TryRemove(new KeyValuePair<TKey, Entry>(key, entry));
            entry.Semaphore.Dispose();
        }
    }

    public readonly struct Releaser : IDisposable
    {
        private readonly KeyedGatePool<TKey> _pool;
        private readonly TKey _key;
        private readonly Entry _entry;

        internal Releaser(KeyedGatePool<TKey> pool, TKey key, Entry entry)
        {
            _pool = pool;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            _entry.Semaphore.Release();
            _pool.ReleaseReference(_key, _entry);
        }
    }
}
