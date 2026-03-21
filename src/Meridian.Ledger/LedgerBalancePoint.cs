namespace Meridian.Ledger;

/// <summary>
/// Running-balance checkpoint for an account after a journal entry is applied.
/// Useful for reconstructing account history during backtests and portfolio analytics.
/// </summary>
public sealed record LedgerBalancePoint(
    DateTimeOffset Timestamp,
    Guid JournalEntryId,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal Balance,
    JournalEntryMetadata Metadata);
