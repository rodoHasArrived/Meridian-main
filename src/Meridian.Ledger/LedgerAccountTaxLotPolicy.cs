namespace Meridian.Ledger;

/// <summary>
/// Account-level tax-lot relief policy used to align ledger accounting with front-office P&amp;L.
/// </summary>
public sealed record LedgerAccountTaxLotPolicy(
    LedgerAccount Account,
    LedgerTaxLotReliefMethod ReliefMethod,
    string PolicyId,
    DateOnly EffectiveDate,
    string? Rationale = null);

