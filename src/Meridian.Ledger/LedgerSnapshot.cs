namespace Meridian.Ledger;

/// <summary>
/// Point-in-time view of ledger balances and posting counts.
/// </summary>
public sealed record LedgerSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<LedgerAccount, decimal> Balances,
    int JournalEntryCount,
    int LedgerEntryCount);
