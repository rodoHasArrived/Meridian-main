namespace Meridian.Ledger;

/// <summary>Resolves registered posting roles without inferring an account's type from its name or number.</summary>
public interface IChartOfAccounts
{
    /// <summary>Returns registered name and type metadata without authorizing a scoped posting.</summary>
    LedgerAccount GetDefinition(LedgerAccountRole role);

    LedgerAccount Resolve(
        LedgerAccountRole role,
        string? financialAccountId = null,
        string? symbol = null);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="account"/> is exactly an account this chart can
    /// resolve: a registered name and type, with symbol and financial-account scope that satisfy the
    /// role's registration. Implementations must not infer a role from a name prefix or number.
    /// </summary>
    bool TryGetRegistered(LedgerAccount account, out LedgerAccountRole role);

    /// <summary>Returns the registered posting identity, if the chart exposes stable account codes.</summary>
    LedgerAccountChartReference? GetPostingReference(LedgerAccount account) => null;
}
