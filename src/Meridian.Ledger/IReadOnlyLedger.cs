namespace Meridian.Ledger;

/// <summary>
/// Read-only view of a double-entry accounting ledger.
/// Exposes query operations only; does not allow posting new entries.
/// Strategies and result consumers should receive this interface to prevent
/// accidental or malicious mutation of the ledger used for auditing.
/// </summary>
public interface IReadOnlyLedger
{
    /// <summary>All journal entries in chronological posting order.</summary>
    IReadOnlyList<JournalEntry> Journal { get; }

    /// <summary>All accounts that have been posted to.</summary>
    IReadOnlyCollection<LedgerAccount> Accounts { get; }

    /// <summary>Total number of journal entries posted to this ledger.</summary>
    int JournalEntryCount { get; }

    /// <summary>Total number of individual ledger entry lines (debit/credit rows) posted.</summary>
    int TotalLedgerEntryCount { get; }

    /// <summary>Returns all individual ledger lines posted to <paramref name="account"/>.</summary>
    IReadOnlyList<LedgerEntry> GetEntries(LedgerAccount account);

    /// <summary>Returns all ledger lines posted to <paramref name="account"/> within the supplied time range.</summary>
    IReadOnlyList<LedgerEntry> GetEntries(LedgerAccount account, DateTimeOffset? from, DateTimeOffset? to);

    /// <summary>
    /// Returns the net balance for <paramref name="account"/> using normal-balance rules.
    /// Assets and expenses carry debit-normal balances (debits − credits).
    /// Liabilities, equity, and revenues carry credit-normal balances (credits − debits).
    /// </summary>
    decimal GetBalance(LedgerAccount account);

    /// <summary>Returns the balance for <paramref name="account"/> as of <paramref name="timestamp"/>.</summary>
    decimal GetBalanceAsOf(LedgerAccount account, DateTimeOffset timestamp);

    /// <summary>Returns whether the ledger contains postings for <paramref name="account"/>.</summary>
    bool HasAccount(LedgerAccount account);

    /// <summary>Returns journal entries matching the supplied range and optional description filter.</summary>
    IReadOnlyList<JournalEntry> GetJournalEntries(DateTimeOffset? from = null, DateTimeOffset? to = null, string? descriptionContains = null);

    /// <summary>Returns journal entries matching the supplied structured query.</summary>
    IReadOnlyList<JournalEntry> GetJournalEntries(LedgerQuery query);

    /// <summary>Returns a summary for a single account.</summary>
    LedgerAccountSummary GetAccountSummary(LedgerAccount account);

    /// <summary>Returns summaries for all posted accounts, optionally filtered by type and financial account.</summary>
    IReadOnlyList<LedgerAccountSummary> SummarizeAccounts(LedgerAccountType? accountType = null, string? financialAccountId = null);

    /// <summary>
    /// Returns a trial balance mapping every account that has been posted to its net balance.
    /// </summary>
    IReadOnlyDictionary<LedgerAccount, decimal> TrialBalance(string? financialAccountId = null);

    /// <summary>Returns the trial balance as of <paramref name="timestamp"/>.</summary>
    IReadOnlyDictionary<LedgerAccount, decimal> TrialBalanceAsOf(DateTimeOffset timestamp, string? financialAccountId = null);

    /// <summary>Returns running-balance checkpoints for an account across the selected range.</summary>
    IReadOnlyList<LedgerBalancePoint> GetRunningBalance(LedgerAccount account, DateTimeOffset? from = null, DateTimeOffset? to = null);

    /// <summary>Returns a point-in-time ledger snapshot as of <paramref name="timestamp"/>.</summary>
    LedgerSnapshot SnapshotAsOf(DateTimeOffset timestamp, string? financialAccountId = null);
}
