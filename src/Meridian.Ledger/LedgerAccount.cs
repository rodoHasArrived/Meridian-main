namespace Meridian.Ledger;

/// <summary>
/// Named ledger account in the chart of accounts.
/// For per-symbol securities accounts, <see cref="Symbol"/> identifies the underlying instrument.
/// For multi-bank / multi-broker ledgers, <see cref="FinancialAccountId"/> scopes the account to a
/// specific user-owned account.
/// </summary>
public sealed record LedgerAccount(
    string Name,
    LedgerAccountType AccountType,
    string? Symbol = null,
    string? FinancialAccountId = null)
{
    /// <inheritdoc/>
    public override string ToString()
    {
        var symbolSuffix = Symbol is null ? string.Empty : $" ({Symbol})";
        var accountSuffix = FinancialAccountId is null ? string.Empty : $" [{FinancialAccountId}]";
        return $"{Name}{symbolSuffix}{accountSuffix}";
    }
}
