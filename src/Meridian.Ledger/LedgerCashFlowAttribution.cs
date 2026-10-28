namespace Meridian.Ledger;

/// <summary>
/// Retained attribution of a compound journal's lines to independently balanced economic activities.
/// Group membership is evidence supplied before approval/posting, never inferred from line order,
/// equal amounts, account names, or a journal's net cash movement.
/// </summary>
public static class LedgerCashFlowAttribution
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
        foreach (var key in tags.Keys.Where(IsAttributionTag).ToArray())
            tags.Remove(key);
        for (var index = 0; index < draft.Lines.Count; index++)
            tags[TagPrefix + draft.Lines[index].EntryId.ToString("D")] = lineGroups[index].Trim();

        var attributed = new JournalEntry(draft.JournalEntryId, draft.Timestamp, draft.Description,
            draft.Lines, draft.Metadata with { Tags = tags });
        _ = ReadGroups(attributed); // Reject incomplete or unbalanced evidence before returning a draft.
        return attributed;
    }

    internal static IEnumerable<LedgerEntry> CashCounterparts(JournalEntry entry, Func<LedgerEntry, bool> matchesScope)
    {
        if (!entry.Lines.Any(line => IsCashAccount(line.Account) && matchesScope(line)))
            yield break;

        var groups = ReadGroups(entry);
        foreach (var group in groups ?? [entry.Lines])
        {
            var scoped = group.Where(matchesScope).ToArray();
            if (!scoped.Any(line => IsCashAccount(line.Account)))
                continue;

            // A partial scope must not produce a signed/exportable statement that cannot reconcile.
            if (scoped.Sum(line => line.Debit - line.Credit) != 0m)
                throw new LedgerValidationException($"Journal '{entry.JournalEntryId}' has incomplete cash-flow attribution in the report scope.");

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
                yield return line;
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
        return WithGroups(reversal, original.Lines
            .Select(line => tags[TagPrefix + line.EntryId.ToString("D")]).ToArray());
    }

    private static IReadOnlyList<IReadOnlyList<LedgerEntry>>? ReadGroups(JournalEntry entry)
    {
        var tags = entry.Metadata.Tags?.Where(pair => IsAttributionTag(pair.Key)).ToArray();
        if (tags is not { Length: > 0 })
            return null;
        if (tags.Length != entry.Lines.Count)
            throw InvalidAttribution(entry);

        var knownIds = entry.Lines.Select(line => line.EntryId).ToHashSet();
        var membership = new Dictionary<Guid, string>();
        foreach (var tag in tags)
        {
            if (!tag.Key.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(tag.Key[TagPrefix.Length..], "D", out var id)
                || !knownIds.Contains(id) || string.IsNullOrWhiteSpace(tag.Value)
                || !membership.TryAdd(id, tag.Value))
            {
                throw InvalidAttribution(entry);
            }
        }

        var groups = entry.Lines.GroupBy(line => membership[line.EntryId], StringComparer.Ordinal)
            .Select(group => (IReadOnlyList<LedgerEntry>)group.ToArray()).ToArray();
        if (groups.Any(group => group.Sum(line => line.Debit - line.Credit) != 0m))
            throw InvalidAttribution(entry);
        return groups;
    }

    private static bool IsAttributionTag(string key)
        => key.StartsWith(TagNamespace, StringComparison.OrdinalIgnoreCase);

    private static LedgerValidationException InvalidAttribution(JournalEntry entry)
        => new($"Journal '{entry.JournalEntryId}' has invalid cash-flow attribution; supply one supported group per line and balance every group.");
}
