using Meridian.Ledger;

namespace Meridian.Execution.Events;

/// <summary>Adapts only the new canonical trade attribution to an immutable pre-attribution journal.</summary>
internal static class TradeFillCashFlowReplayCompatibility
{
    internal const string StrategyRunIdTag = "strategy.runId";

    internal static JournalEntry SelectCandidate(JournalEntry expected, JournalEntry retained)
        => SelectCashFlowCandidate(SelectStrategyAttributionCandidate(expected, retained), retained);

    /// <summary>
    /// Fill journals retained before strategy attribution carry no strategy id or run tag. When the
    /// same fill is redelivered after the upgrade, compare it in the retained shape; every other
    /// field is still compared by the governed target. The retained record is never edited.
    /// </summary>
    private static JournalEntry SelectStrategyAttributionCandidate(JournalEntry expected, JournalEntry retained)
    {
        if (expected.JournalEntryId != retained.JournalEntryId
            || !string.IsNullOrWhiteSpace(retained.Metadata.StrategyId)
            || retained.Metadata.Tags?.ContainsKey(StrategyRunIdTag) is true
            || (string.IsNullOrWhiteSpace(expected.Metadata.StrategyId)
                && expected.Metadata.Tags?.ContainsKey(StrategyRunIdTag) is not true))
            return expected;

        var legacyTags = expected.Metadata.Tags?
            .Where(static pair => !string.Equals(pair.Key, StrategyRunIdTag, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        return new JournalEntry(expected.JournalEntryId, expected.Timestamp, expected.Description,
            expected.Lines, expected.Metadata with { StrategyId = null, Tags = legacyTags });
    }

    private static JournalEntry SelectCashFlowCandidate(JournalEntry expected, JournalEntry retained)
    {
        if (expected.JournalEntryId != retained.JournalEntryId
            || expected.Metadata.ActivityType != "trade-fill"
            || retained.Metadata.Tags?.Keys.Any(IsCashFlowTag) is true
            || expected.Metadata.Tags is not { } tags)
            return expected;

        var supported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in expected.Lines)
        {
            supported["cash-flow.group.v2." + line.EntryId.ToString("D")] = "Security trade";
            supported["cash-flow.category.v1." + line.EntryId.ToString("D")] = "Investing";
        }
        var evidence = tags.Where(pair => IsCashFlowTag(pair.Key)).ToArray();
        if (evidence.Length != supported.Count || evidence.Any(pair =>
                !supported.TryGetValue(pair.Key, out var value) || pair.Value != value))
            return expected;

        // Everything else is still compared by the ordinary governed target and independently by
        // the consumer. Never edit the retained record, remove old tags, or infer missing history.
        var legacyTags = tags.Where(pair => !supported.ContainsKey(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        return new JournalEntry(expected.JournalEntryId, expected.Timestamp, expected.Description,
            expected.Lines, expected.Metadata with { Tags = legacyTags });
    }

    private static bool IsCashFlowTag(string key) => key.StartsWith("cash-flow.", StringComparison.OrdinalIgnoreCase);
}
