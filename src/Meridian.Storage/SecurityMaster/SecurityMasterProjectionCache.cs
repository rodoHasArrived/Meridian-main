using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public sealed class SecurityMasterProjectionCache
{
    // Swapped by reference on ReplaceAll rather than cleared in place, so a reader concurrent with a
    // warm or rebuild sees either the whole previous master or the whole new one — never the empty
    // or half-filled window a Clear()-then-refill exposes. Reads go through Volatile.Read so a
    // reader cannot observe the new map before the writes that populated it.
    private ConcurrentDictionary<Guid, SecurityProjectionRecord> _byId = new();

    private ConcurrentDictionary<Guid, SecurityProjectionRecord> Current
        => Volatile.Read(ref _byId);

    public int Count => Current.Count;

    public SecurityProjectionRecord? Get(Guid securityId)
        => Current.TryGetValue(securityId, out var record) ? record : null;

    public void Upsert(SecurityProjectionRecord record)
        => Current[record.SecurityId] = record;

    /// <summary>
    /// Replaces the cached master with <paramref name="records"/> as a single reference swap.
    /// </summary>
    /// <remarks>
    /// This removes the empty-master window, not every race: an <see cref="Upsert"/> that resolves
    /// <see cref="Current"/> before the swap still writes into the outgoing map and is dropped, the
    /// same outcome a concurrent <c>Clear()</c> produced. Serializing a publish against a rebuild
    /// remains the caller's concern.
    /// </remarks>
    public void ReplaceAll(IEnumerable<SecurityProjectionRecord> records)
    {
        var replacement = new ConcurrentDictionary<Guid, SecurityProjectionRecord>();
        foreach (var record in records)
        {
            replacement[record.SecurityId] = record;
        }

        Volatile.Write(ref _byId, replacement);
    }

    public IReadOnlyCollection<SecurityProjectionRecord> Snapshot()
        => Current.Values.ToArray();
}
