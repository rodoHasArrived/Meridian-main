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
}
