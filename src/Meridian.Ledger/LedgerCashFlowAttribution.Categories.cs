namespace Meridian.Ledger;

public static partial class LedgerCashFlowAttribution
{
    private const string CategoryNamespace = "cash-flow.category.";
    private const string CategoryPrefix = CategoryNamespace + "v1.";
    private const string CategorizedGroupPrefix = TagNamespace + "v2.";

    /// <summary>
    /// Retains an authoritative producer's classification for one economic activity. Its cash
    /// movement is classified as a whole; basis relief and realized gain/loss are not cash flows.
    /// This evidence version supports Investing activities only.
    /// All existing journal amounts and identities remain unchanged. Apply before approval/posting.
    /// </summary>
    public static JournalEntry WithClassifiedActivity(JournalEntry draft, LedgerCashFlowCategory category, string activity)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (category != LedgerCashFlowCategory.Investing)
            throw new ArgumentOutOfRangeException(nameof(category));
        var grouped = WithGroups(draft, Enumerable.Repeat(activity, draft.Lines.Count).ToArray());
        return WithCategories(grouped, Enumerable.Repeat<string?>(category.ToString(), draft.Lines.Count).ToArray());
    }

    private static JournalEntry WithCategories(JournalEntry draft, IReadOnlyList<string?> categories)
    {
        var tags = draft.Metadata.Tags!.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var key in tags.Keys.Where(IsCategoryTag).ToArray())
            tags.Remove(key);
        for (var index = 0; index < draft.Lines.Count; index++)
            if (categories[index] is { } category)
            {
                var id = draft.Lines[index].EntryId.ToString("D");
                var group = tags[TagPrefix + id];
                tags.Remove(TagPrefix + id);
                tags[CategorizedGroupPrefix + id] = group;
                tags[CategoryPrefix + draft.Lines[index].EntryId.ToString("D")] = category;
            }
        var result = new JournalEntry(draft.JournalEntryId, draft.Timestamp, draft.Description,
            draft.Lines, draft.Metadata with { Tags = tags });
        _ = ReadGroups(result);
        return result;
    }

    private static Dictionary<Guid, LedgerCashFlowCategory> ReadCategories(JournalEntry entry, HashSet<Guid> knownIds)
    {
        var categories = new Dictionary<Guid, LedgerCashFlowCategory>();
        foreach (var tag in entry.Metadata.Tags!.Where(pair => IsCategoryTag(pair.Key)))
        {
            if (!tag.Key.StartsWith(CategoryPrefix, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(tag.Key[CategoryPrefix.Length..], "D", out var id)
                || !knownIds.Contains(id)
                || !Enum.TryParse<LedgerCashFlowCategory>(tag.Value, out var category)
                || category != LedgerCashFlowCategory.Investing || tag.Value != category.ToString()
                || !categories.TryAdd(id, category))
                throw InvalidAttribution(entry);
        }
        return categories;
    }

    private static LedgerCashFlowCategory? GroupCategory(JournalEntry entry, IEnumerable<LedgerEntry> group,
        IReadOnlyDictionary<Guid, LedgerCashFlowCategory> categories)
    {
        var lines = group.ToArray();
        var classified = lines.Where(line => categories.ContainsKey(line.EntryId)).ToArray();
        if (classified.Length == 0)
            return null;
        if (classified.Length != lines.Length || classified.Select(line => categories[line.EntryId]).Distinct().Count() != 1)
            throw InvalidAttribution(entry);
        return categories[classified[0].EntryId];
    }

    private static bool IsCategoryTag(string key) => key.StartsWith(CategoryNamespace, StringComparison.OrdinalIgnoreCase);
}
