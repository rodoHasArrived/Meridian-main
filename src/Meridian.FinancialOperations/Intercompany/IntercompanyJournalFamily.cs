using Meridian.Storage.Ledger;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.Intercompany;

/// <summary>Indexes actual same-book correction lineage; shared transaction labels never merge unrelated settlements.</summary>
public sealed class IntercompanyJournalFamily
{
    private readonly IReadOnlyDictionary<Guid, LedgerJournalEntryRecord> _records;
    private readonly IReadOnlyDictionary<Guid, LedgerJournalEntryRecord[]> _children;

    public IntercompanyJournalFamily(IReadOnlyList<LedgerJournalEntryRecord> records, DateOnly asOf)
    {
        var included = records.Where(record => EffectiveDate(record) <= asOf).ToArray();
        _records = included.ToDictionary(record => record.Entry.JournalEntryId);
        _children = included.Where(record => Parent(record) is not null).GroupBy(record => Parent(record)!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(record => record.GlobalSequence).ToArray());
    }

    public bool Contains(Guid id) => _records.ContainsKey(id);

    public IReadOnlyList<LedgerJournalEntryRecord> Resolve(Guid anchor)
    {
        if (!_records.TryGetValue(anchor, out var root)) return [];
        var ancestors = new HashSet<Guid> { anchor };
        while (Parent(root) is { } parent)
        {
            if (!ancestors.Add(parent)) throw new InvalidOperationException("The retained correction lineage contains a cycle.");
            if (!_records.TryGetValue(parent, out root))
                throw new InvalidOperationException("The retained correction parent is unavailable in the same book and as-of population.");
        }
        var result = new List<LedgerJournalEntryRecord>();
        var queue = new Queue<LedgerJournalEntryRecord>();
        queue.Enqueue(root);
        var visited = new HashSet<Guid>();
        while (queue.TryDequeue(out var record))
        {
            if (!visited.Add(record.Entry.JournalEntryId)) throw new InvalidOperationException("The retained correction lineage is ambiguous.");
            result.Add(record);
            if (_children.TryGetValue(record.Entry.JournalEntryId, out var children))
                foreach (var child in children) queue.Enqueue(child);
        }
        return result;
    }

    public static DateOnly EffectiveDate(LedgerJournalEntryRecord record)
        => record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime);

    private static Guid? Parent(LedgerJournalEntryRecord record)
    {
        if (record.SourceJournalEntryId is { } authoritative) return authoritative;
        var tags = record.Entry.Metadata.Tags;
        foreach (var key in new[] { "reversalOfJournalEntryId", LedgerJournalReversal.ReversalOfTag, "rebookedFromJournalEntryId", "sourceJournalEntryId" })
            if (Guid.TryParse(tags?.GetValueOrDefault(key), out var parent)) return parent;
        return null;
    }
}
