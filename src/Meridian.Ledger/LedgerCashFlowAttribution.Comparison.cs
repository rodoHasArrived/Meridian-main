using System.Collections.ObjectModel;

namespace Meridian.Ledger;

public static partial class LedgerCashFlowAttribution
{
    /// <summary>Preserves validated attribution when a new draft's unchanged ordered lines receive canonical IDs.</summary>
    public static JournalEntry RemapForRebuiltLines(JournalEntry source, JournalEntry rebuilt)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rebuilt);
        _ = GetComparisonTokens(source);
        if (source.Lines.Count != rebuilt.Lines.Count)
            throw InvalidAttribution(rebuilt);
        var idMap = new Dictionary<Guid, Guid>();
        for (var index = 0; index < source.Lines.Count; index++)
        {
            var prior = source.Lines[index];
            var next = rebuilt.Lines[index];
            if (prior.Account != next.Account || prior.Debit != next.Debit || prior.Credit != next.Credit
                || prior.Timestamp != next.Timestamp || prior.Description != next.Description
                || prior.Dimensions != next.Dimensions || prior.Currency != next.Currency)
                throw InvalidAttribution(rebuilt);
            idMap.Add(prior.EntryId, next.EntryId);
        }
        var sourceTags = source.Metadata.Tags?.Where(pair => pair.Key.StartsWith("cash-flow.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase) ?? [];
        var tags = rebuilt.Metadata.Tags?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase) ?? [];
        var targetTags = tags.Where(pair => pair.Key.StartsWith("cash-flow.", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (targetTags.Length != sourceTags.Count || targetTags.Any(pair =>
                !sourceTags.TryGetValue(pair.Key, out var value) || value != pair.Value))
            throw InvalidAttribution(rebuilt);
        if (sourceTags.Count == 0)
            return rebuilt;
        foreach (var tag in targetTags)
            tags.Remove(tag.Key);
        foreach (var tag in sourceTags)
        {
            var separator = tag.Key.LastIndexOf('.');
            var priorId = Guid.ParseExact(tag.Key[(separator + 1)..], "D");
            tags.Add(tag.Key[..(separator + 1)] + idMap[priorId].ToString("D"), tag.Value);
        }
        var remapped = new JournalEntry(rebuilt.JournalEntryId, rebuilt.Timestamp, rebuilt.Description,
            rebuilt.Lines, rebuilt.Metadata with { Tags = tags });
        _ = GetComparisonTokens(remapped);
        return remapped;
    }

    /// <summary>
    /// Validates retained attribution and represents its exact versions, group values and categories
    /// against ordered line positions for comparison with a rebuilt draft. Journal and line IDs,
    /// metadata and amounts are never modified. Unknown evidence cannot become comparison tokens.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetComparisonTokens(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var evidence = entry.Metadata.Tags?.Where(pair => pair.Key.StartsWith("cash-flow.", StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];
        if (evidence.Any(pair => !IsAttributionTag(pair.Key) && !IsCategoryTag(pair.Key)))
            throw InvalidAttribution(entry);
        _ = ReadGroups(entry);
        var positions = entry.Lines.Select((line, index) => (line.EntryId, index))
            .ToDictionary(item => item.EntryId, item => item.index);
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in evidence)
        {
            var prefix = tag.Key.StartsWith(CategorizedGroupPrefix, StringComparison.OrdinalIgnoreCase)
                ? CategorizedGroupPrefix : tag.Key.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)
                    ? TagPrefix : CategoryPrefix;
            if (!Guid.TryParseExact(tag.Key[prefix.Length..], "D", out var id) || !positions.TryGetValue(id, out var index))
                throw InvalidAttribution(entry);
            tokens.Add(prefix + "line:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), tag.Value);
        }
        return new ReadOnlyDictionary<string, string>(tokens);
    }
}
