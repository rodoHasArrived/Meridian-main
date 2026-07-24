namespace Meridian.Contracts.Ledger;

/// <summary>
/// Shared numeric tolerances for ledger and accounting equality comparisons. Consolidates the
/// per-file 1e-6 epsilon constants that were previously duplicated across the ledger, financial
/// operations, reporting, and UI projects, giving the reconciliation threshold a single source of
/// truth.
/// </summary>
public static class LedgerToleranceConstants
{
    /// <summary>
    /// Absolute epsilon for treating a ledger balance as reconciled — for example total debits
    /// versus total credits. Guards against false imbalances introduced by independent decimal
    /// rounding paths.
    /// </summary>
    public const decimal Balance = 0.000001m;

    /// <summary>
    /// Absolute epsilon for treating an allocation or apportionment total as fully distributed
    /// (for example partnership waterfall and investor allocations). Shares <see cref="Balance"/>'s
    /// value but is named distinctly so the two thresholds can diverge later without reintroducing
    /// duplication.
    /// </summary>
    public const decimal Allocation = Balance;
}
