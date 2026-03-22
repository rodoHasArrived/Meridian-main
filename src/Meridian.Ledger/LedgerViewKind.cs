namespace Meridian.Ledger;

/// <summary>
/// High-level purpose or valuation basis for a ledger within a project.
/// This allows one project to hold parallel ledgers for actuals, historical replay,
/// parameterized P&amp;L scenarios, security-master contractual projections, and future views.
/// </summary>
public enum LedgerViewKind
{
    Actual,
    Historical,
    ParameterizedPnL,
    SecurityMaster,
    Other
}
