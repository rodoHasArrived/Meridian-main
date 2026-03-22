using System.Collections.Concurrent;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Storage.SecurityMaster;

public sealed class SecurityMasterProjectionCache
{
    private readonly ConcurrentDictionary<Guid, SecurityProjectionRecord> _byId = new();

    public int Count => _byId.Count;

    public SecurityProjectionRecord? Get(Guid securityId)
        => _byId.TryGetValue(securityId, out var record) ? record : null;

    public void Upsert(SecurityProjectionRecord record)
        => _byId[record.SecurityId] = record;

    public void ReplaceAll(IEnumerable<SecurityProjectionRecord> records)
    {
        _byId.Clear();
        foreach (var record in records)
        {
            _byId[record.SecurityId] = record;
        }
    }

    public IReadOnlyCollection<SecurityProjectionRecord> Snapshot()
        => _byId.Values.ToArray();
}
