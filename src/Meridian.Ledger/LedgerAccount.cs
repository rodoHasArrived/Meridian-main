namespace Meridian.Ledger;

/// <summary>
/// Named ledger account in the chart of accounts.
/// For per-security accounts (see <see cref="LedgerSecurityKey"/>), <see cref="SecurityId"/> is the
/// account key and <see cref="Symbol"/> carries the ticker as a display attribute. Legacy
/// ticker-keyed accounts (journals written before the 2026-11-25 SecurityId keying decision, or
/// holdings the Security Master cannot resolve) have no <see cref="SecurityId"/> and are keyed by
/// <see cref="Symbol"/>.
/// For multi-bank / multi-broker ledgers, <see cref="FinancialAccountId"/> scopes the account to a
/// specific user-owned account.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="SecurityId"/> is present it is the instrument identity and <see cref="Symbol"/>
/// does not participate in equality, so a ticker rename keeps postings in one balance bucket. A
/// SecurityId-keyed account never equals a legacy ticker-keyed account; moving legacy balances is a
/// governed reclass (see <see cref="LedgerSecurityKeyMigration"/>).
/// </para>
/// <para>
/// <see cref="FinancialAccountId"/> participates in equality case-insensitively: read-side account
/// scoping (see <c>Ledger.MatchesFinancialAccount</c>) matches account ids with
/// <see cref="StringComparison.OrdinalIgnoreCase"/>, so posting-side account identity must agree.
/// Otherwise postings whose account ids differ only by casing accumulate in separate balance
/// buckets that every read-side filter then reports as a single account.
/// </para>
/// </remarks>
public sealed record LedgerAccount(
    string Name,
    LedgerAccountType AccountType,
    string? Symbol = null,
    string? FinancialAccountId = null,
    Guid? SecurityId = null)
{
    /// <summary><see langword="true"/> when the account is keyed by a Security Master identity.</summary>
    public bool IsSecurityKeyed => SecurityId.HasValue;

    /// <inheritdoc/>
    public bool Equals(LedgerAccount? other)
        => other is not null
            && Name == other.Name
            && AccountType == other.AccountType
            && SecurityId == other.SecurityId
            && (SecurityId.HasValue || Symbol == other.Symbol)
            && string.Equals(FinancialAccountId, other.FinancialAccountId, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(
            Name,
            AccountType,
            SecurityId.HasValue ? SecurityId.Value.GetHashCode() : Symbol?.GetHashCode() ?? 0,
            FinancialAccountId is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(FinancialAccountId));

    /// <inheritdoc/>
    public override string ToString()
    {
        var symbolSuffix = Symbol is null ? string.Empty : $" ({Symbol})";
        var securitySuffix = SecurityId is { } id ? $" <{id:D}>" : string.Empty;
        var accountSuffix = FinancialAccountId is null ? string.Empty : $" [{FinancialAccountId}]";
        return $"{Name}{symbolSuffix}{securitySuffix}{accountSuffix}";
    }
}
