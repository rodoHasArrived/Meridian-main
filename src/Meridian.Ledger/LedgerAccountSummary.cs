namespace Meridian.Ledger;

/// <summary>
/// Aggregate summary for a posted ledger account.
/// </summary>
public sealed record LedgerAccountSummary(
    LedgerAccount Account,
    decimal Balance,
    decimal TotalDebits,
    decimal TotalCredits,
    int EntryCount,
    DateTimeOffset? FirstPostedAt,
    DateTimeOffset? LastPostedAt)
{
    /// <summary>
    /// Creates an empty summary for an account with no postings.
    /// </summary>
    public static LedgerAccountSummary Empty(LedgerAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new LedgerAccountSummary(account, 0m, 0m, 0m, 0, null, null);
    }
}
