namespace Meridian.Ledger;

/// <summary>
/// Account-level variance between the actual ledger and an independent shadow-NAV ledger.
/// </summary>
public sealed record ShadowNavValidationFinding(
    LedgerAccount Account,
    decimal ActualBalance,
    decimal ShadowBalance,
    decimal Variance,
    decimal AbsoluteVariance,
    bool RequiresOverride);
