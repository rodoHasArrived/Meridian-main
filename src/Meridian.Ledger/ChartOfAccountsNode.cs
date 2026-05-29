namespace Meridian.Ledger;

/// <summary>
/// Registered account metadata inside a hierarchical chart of accounts.
/// </summary>
public sealed record ChartOfAccountsNode(
    LedgerAccount Account,
    string Path,
    string? ParentPath);

