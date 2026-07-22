using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>Period roll-forward of one partner/equity capital account.</summary>
/// <remarks>
/// <see cref="AllocatedResult"/> is the net income/expense allocated to this capital account for the
/// period. It is decomposed additively into <see cref="IncomeGainAllocations"/> (revenue and gains,
/// which increase capital), <see cref="ExpenseAllocations"/> (operating expenses other than fund
/// fees, which reduce capital), and <see cref="FeeAllocations"/> (management, performance, and
/// incentive fees, which reduce capital), so a partners' capital statement can present the drivers
/// a fund accountant reports rather than a single lumped figure. The decomposition holds
/// <c>AllocatedResult == IncomeGainAllocations - ExpenseAllocations - FeeAllocations</c>.
/// </remarks>
public sealed record LedgerPartnersCapitalRollForward(
    LedgerAccount CapitalAccount,
    string AccountName,
    string? InvestorId,
    decimal BeginningCapital,
    decimal Contributions,
    decimal Distributions,
    decimal AllocatedResult,
    decimal IncomeGainAllocations,
    decimal ExpenseAllocations,
    decimal FeeAllocations,
    decimal OtherMovements,
    decimal EndingCapital)
{
    /// <summary>Ending capital implied by the roll-forward movements.</summary>
    public decimal ComputedEndingCapital =>
        BeginningCapital + Contributions - Distributions + AllocatedResult + OtherMovements;

    /// <summary>Difference between the rolled-forward and observed ending capital. Zero when reconciled.</summary>
    public decimal ReconciliationVariance => ComputedEndingCapital - EndingCapital;

    /// <summary>
    /// Difference between <see cref="AllocatedResult"/> and its income/expense/fee decomposition.
    /// Zero when the breakout ties to the net allocated result.
    /// </summary>
    public decimal AllocationComponentsVariance =>
        AllocatedResult - (IncomeGainAllocations - ExpenseAllocations - FeeAllocations);
}

/// <summary>
/// Statement of changes in partners' capital: a per-account roll-forward of beginning capital,
/// contributions, distributions, allocated result (net income and fees closed to capital), and
/// other movements to ending capital. Ending capital ties to balance-sheet equity.
/// </summary>
public sealed record LedgerPartnersCapitalStatement(
    DateTimeOffset PeriodStart,
    DateTimeOffset AsOf,
    decimal BeginningCapital,
    decimal Contributions,
    decimal Distributions,
    decimal AllocatedResult,
    decimal IncomeGainAllocations,
    decimal ExpenseAllocations,
    decimal FeeAllocations,
    decimal OtherMovements,
    decimal EndingCapital,
    IReadOnlyList<LedgerPartnersCapitalRollForward> Accounts)
{
    /// <summary>Ending capital implied by the aggregate roll-forward.</summary>
    public decimal ComputedEndingCapital =>
        BeginningCapital + Contributions - Distributions + AllocatedResult + OtherMovements;

    /// <summary>Difference between the rolled-forward and observed ending capital. Zero when reconciled.</summary>
    public decimal ReconciliationVariance => ComputedEndingCapital - EndingCapital;

    /// <summary>
    /// Difference between the aggregate <see cref="AllocatedResult"/> and its income/expense/fee
    /// decomposition. Zero when the breakout ties to the net allocated result.
    /// </summary>
    public decimal AllocationComponentsVariance =>
        AllocatedResult - (IncomeGainAllocations - ExpenseAllocations - FeeAllocations);

    /// <summary>True when the aggregate roll-forward ties to ending capital.</summary>
    public bool IsReconciled => Math.Abs(ReconciliationVariance) <= LedgerToleranceConstants.Balance;
}
