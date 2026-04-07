namespace Meridian.Ledger;

/// <summary>
/// Point-in-time view of ledger balances and posting counts.
/// </summary>
public sealed record LedgerSnapshot
{
    public LedgerSnapshot(
        DateTimeOffset timestamp,
        IReadOnlyDictionary<LedgerAccount, decimal> balances,
        int journalEntryCount,
        int ledgerEntryCount)
    {
        ArgumentNullException.ThrowIfNull(balances);

        Timestamp = timestamp;
        Balances = ReadOnlyCollectionHelpers.FreezeDictionary(balances);
        JournalEntryCount = journalEntryCount;
        LedgerEntryCount = ledgerEntryCount;
    }

    public DateTimeOffset Timestamp { get; init; }

    public IReadOnlyDictionary<LedgerAccount, decimal> Balances { get; init; }

    public int JournalEntryCount { get; init; }

    public int LedgerEntryCount { get; init; }
}
