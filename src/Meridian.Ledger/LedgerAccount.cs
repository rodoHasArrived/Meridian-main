namespace Meridian.Ledger;

/// <summary>
/// Named ledger account in the chart of accounts.
/// For per-symbol securities accounts, <see cref="Symbol"/> identifies the underlying instrument.
/// For multi-bank / multi-broker ledgers, <see cref="FinancialAccountId"/> scopes the account to a
/// specific user-owned account.
/// </summary>
/// <remarks>
/// <see cref="FinancialAccountId"/> participates in equality case-insensitively: read-side account
/// scoping (see <c>Ledger.MatchesFinancialAccount</c>) matches account ids with
/// <see cref="StringComparison.OrdinalIgnoreCase"/>, so posting-side account identity must agree.
/// Otherwise postings whose account ids differ only by casing accumulate in separate balance
/// buckets that every read-side filter then reports as a single account.
/// </remarks>
public sealed record LedgerAccount(
    string Name,
    LedgerAccountType AccountType,
    string? Symbol = null,
    string? FinancialAccountId = null)
{
    /// <inheritdoc/>
    public bool Equals(LedgerAccount? other)
        => other is not null
            && Name == other.Name
            && AccountType == other.AccountType
            && Symbol == other.Symbol
            && string.Equals(FinancialAccountId, other.FinancialAccountId, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(
            Name,
            AccountType,
            Symbol,
            FinancialAccountId is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(FinancialAccountId));

    /// <inheritdoc/>
    public override string ToString()
    {
        var symbolSuffix = Symbol is null ? string.Empty : $" ({Symbol})";
        var accountSuffix = FinancialAccountId is null ? string.Empty : $" [{FinancialAccountId}]";
        return $"{Name}{symbolSuffix}{accountSuffix}";
    }
}
