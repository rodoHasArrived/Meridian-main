namespace Meridian.Ledger;

/// <summary>
/// Retained attribution of a compound journal's lines to independently balanced economic activities.
/// Group membership is evidence supplied before approval/posting, never inferred from line order,
/// equal amounts, account names, or a journal's net cash movement.
/// </summary>
public static partial class LedgerCashFlowAttribution
{
    private const string TagNamespace = "cash-flow.group.";
    private const string TagPrefix = TagNamespace + "v1.";

    /// <summary>
    /// Creates a draft copy with one activity group per line. Each group must represent one
    /// cash-related transaction, a cash transfer, or an independent noncash transaction. The caller
    /// supplies groups in the draft's line order; retained tags bind them to immutable line IDs.
    /// Do not amend posted history: use the governed reversal/rebook path for corrections.
    /// </summary>
    public static JournalEntry WithGroups(JournalEntry draft, params string[] lineGroups)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(lineGroups);
        if (lineGroups.Length != draft.Lines.Count || lineGroups.Any(string.IsNullOrWhiteSpace))
            throw InvalidAttribution(draft);

        var tags = draft.Metadata.Tags?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in tags.Keys.Where(key => IsAttributionTag(key) || IsCategoryTag(key)).ToArray())
            tags.Remove(key);
        for (var index = 0; index < draft.Lines.Count; index++)
            tags[TagPrefix + draft.Lines[index].EntryId.ToString("D")] = lineGroups[index].Trim();

        var attributed = new JournalEntry(draft.JournalEntryId, draft.Timestamp, draft.Description,
            draft.Lines, draft.Metadata with { Tags = tags });
        _ = ReadGroups(attributed); // Reject incomplete or unbalanced evidence before returning a draft.
        return attributed;
    }

    internal static IEnumerable<CashContribution> CashCounterparts(JournalEntry entry, Func<LedgerEntry, bool> matchesScope)
    {
        if (!entry.Lines.Any(line => IsCashAccount(line.Account) && matchesScope(line)))
            yield break;

        var groups = ReadGroups(entry);
        foreach (var group in groups ?? [new ActivityGroup(entry.Description, entry.Lines, null)])
        {
            var scoped = group.Lines.Where(matchesScope).ToArray();
            if (!scoped.Any(line => IsCashAccount(line.Account)))
                continue;

            // A partial scope must not produce a signed/exportable statement that cannot reconcile.
            if (scoped.Sum(line => line.Debit - line.Credit) != 0m)
                throw new LedgerValidationException($"Journal '{entry.JournalEntryId}' has incomplete cash-flow attribution in the report scope.");

            if (group.Category is { } category)
            {
                if (scoped.Length != group.Lines.Count)
                    throw new LedgerValidationException($"Journal '{entry.JournalEntryId}' has incomplete cash-flow attribution in the report scope.");
                var actualTypes = scoped.Where(line => !IsCashAccount(line.Account))
                    .Select(line => line.Account.AccountType).Distinct().ToArray();
                // The retained investing activity's carrying leg identifies the counterparty.
                // A fully relieved asset may leave only the actual realized-result account.
                var counterpartyType = actualTypes.Contains(LedgerAccountType.Asset)
                    ? LedgerAccountType.Asset : actualTypes.Length == 1 ? actualTypes[0]
                    : throw InvalidAttribution(entry);
                yield return new CashContribution(category, group.Name, counterpartyType,
                    scoped.Where(line => IsCashAccount(line.Account)).Sum(line => line.Debit - line.Credit));
                continue;
            }

            var counterparts = scoped.Where(line => !IsCashAccount(line.Account)).ToArray();
            // Opposing noncash legs can be either real gross cash activity or an unrelated accrual /
            // reclassification. The same booked amounts describe both, even when net cash is nonzero.
            // Without retained attribution it is not possible to choose an accounting treatment.
            if (groups is null && counterparts.Any(line => line.Debit != 0m)
                && counterparts.Any(line => line.Credit != 0m))
            {
                throw new LedgerValidationException($"Journal '{entry.JournalEntryId}' requires explicit cash-flow attribution for opposing noncash legs.");
            }

            foreach (var line in counterparts)
                yield return new CashContribution(null, line.Account.Name, line.Account.AccountType, line.Credit - line.Debit);
        }
    }

    internal static bool IsCashAccount(LedgerAccount account)
        => account.AccountType == LedgerAccountType.Asset
           && account.Name.StartsWith("Cash", StringComparison.OrdinalIgnoreCase);

    internal static JournalEntry RemapReversal(JournalEntry original, JournalEntry reversal)
    {
        if (ReadGroups(original) is null)
            return reversal;

        var tags = original.Metadata.Tags!.ToDictionary(pair => pair.Key, pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        var grouped = WithGroups(reversal, original.Lines
            .Select(line => tags.TryGetValue(TagPrefix + line.EntryId.ToString("D"), out var group)
                ? group : tags[CategorizedGroupPrefix + line.EntryId.ToString("D")]).ToArray());
        var categories = original.Lines.Select(line =>
            tags.TryGetValue(CategoryPrefix + line.EntryId.ToString("D"), out var category) ? category : null).ToArray();
        return WithCategories(grouped, categories);
    }

    private static IReadOnlyList<ActivityGroup>? ReadGroups(JournalEntry entry)
    {
        var tags = entry.Metadata.Tags?.Where(pair => IsAttributionTag(pair.Key)).ToArray();
        if (tags is not { Length: > 0 })
        {
            if (entry.Metadata.Tags?.Keys.Any(IsCategoryTag) is true)
                throw InvalidAttribution(entry);
            return null;
        }
        if (tags.Length != entry.Lines.Count)
            throw InvalidAttribution(entry);

        var knownIds = entry.Lines.Select(line => line.EntryId).ToHashSet();
        var membership = new Dictionary<Guid, string>();
        var classifiedIds = new HashSet<Guid>();
        foreach (var tag in tags)
        {
            var classified = tag.Key.StartsWith(CategorizedGroupPrefix, StringComparison.OrdinalIgnoreCase);
            var prefix = classified ? CategorizedGroupPrefix : TagPrefix;
            if (!tag.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(tag.Key[prefix.Length..], "D", out var id)
                || !knownIds.Contains(id) || string.IsNullOrWhiteSpace(tag.Value)
                || !membership.TryAdd(id, tag.Value))
            {
                throw InvalidAttribution(entry);
            }
            if (classified)
                classifiedIds.Add(id);
        }

        var categories = ReadCategories(entry, knownIds);
        if (!classifiedIds.SetEquals(categories.Keys))
            throw InvalidAttribution(entry);
        var groups = entry.Lines.GroupBy(line => membership[line.EntryId], StringComparer.Ordinal)
            .Select(group => new ActivityGroup(group.Key, group.ToArray(), GroupCategory(entry, group, categories))).ToArray();
        if (groups.Any(group => group.Lines.Sum(line => line.Debit - line.Credit) != 0m))
            throw InvalidAttribution(entry);
        return groups;
    }

    private static bool IsAttributionTag(string key)
        => key.StartsWith(TagNamespace, StringComparison.OrdinalIgnoreCase);

    private sealed record ActivityGroup(string Name, IReadOnlyList<LedgerEntry> Lines, LedgerCashFlowCategory? Category);

    internal sealed record CashContribution(LedgerCashFlowCategory? Category, string Description,
        LedgerAccountType CounterpartyType, decimal Amount);

    private static LedgerValidationException InvalidAttribution(JournalEntry entry)
        => new($"Journal '{entry.JournalEntryId}' has invalid cash-flow attribution; supply one supported group per line and balance every group.");
}
