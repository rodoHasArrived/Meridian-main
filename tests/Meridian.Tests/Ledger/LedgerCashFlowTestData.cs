using Meridian.Ledger;

namespace Meridian.Tests.Ledger;

internal static class LedgerCashFlowTestData
{
    internal static JournalEntry Post(
        Meridian.Ledger.Ledger ledger,
        DateTimeOffset timestamp,
        IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit, LedgerLineDimensionSet? Dimensions)> lines,
        params string[] groups)
    {
        var id = Guid.NewGuid();
        var draft = new JournalEntry(id, timestamp, "cash-flow fixture", lines.Select(line =>
            new LedgerEntry(Guid.NewGuid(), id, timestamp, line.Account, line.Debit, line.Credit,
                "cash-flow fixture", line.Dimensions)).ToArray());
        var entry = LedgerCashFlowAttribution.WithGroups(draft, groups);
        ledger.Post(entry);
        return entry;
    }
}
