namespace Meridian.Ledger;

/// <summary>Reads retained execution run ownership; an ordinary strategy label is insufficient.</summary>
public static class LedgerRunAttribution
{
    public static bool TryGetTrustedRunId(JournalEntryMetadata metadata, out string? runId)
    {
        runId = null;
        if (metadata.Tags is not { } tags
            || !tags.TryGetValue(LedgerQuery.AccountingRunIdTag, out var admittedRun)
            || string.IsNullOrWhiteSpace(admittedRun)
            || !string.Equals(admittedRun, admittedRun.Trim(), StringComparison.Ordinal)
            || !tags.TryGetValue(LedgerQuery.StrategyRunIdTag, out var label)
            || !string.Equals(admittedRun, label, StringComparison.Ordinal))
            return false;
        runId = admittedRun;
        return true;
    }
}
