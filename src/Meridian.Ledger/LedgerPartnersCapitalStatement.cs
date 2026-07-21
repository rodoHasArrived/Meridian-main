using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>Period roll-forward of one partner/equity capital account.</summary>
public sealed record LedgerPartnersCapitalRollForward(
    LedgerAccount CapitalAccount,
    string AccountName,
    string? InvestorId,
    decimal BeginningCapital,
    decimal Contributions,
    decimal Distributions,
    decimal AllocatedResult,
    decimal OtherMovements,
    decimal EndingCapital)
{
    /// <summary>Ending capital implied by the roll-forward movements.</summary>
    public decimal ComputedEndingCapital =>
        BeginningCapital + Contributions - Distributions + AllocatedResult + OtherMovements;

    /// <summary>Difference between the rolled-forward and observed ending capital. Zero when reconciled.</summary>
    public decimal ReconciliationVariance => ComputedEndingCapital - EndingCapital;
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
    decimal OtherMovements,
    decimal EndingCapital,
    IReadOnlyList<LedgerPartnersCapitalRollForward> Accounts)
{
    /// <summary>Ending capital implied by the aggregate roll-forward.</summary>
    public decimal ComputedEndingCapital =>
        BeginningCapital + Contributions - Distributions + AllocatedResult + OtherMovements;

    /// <summary>Difference between the rolled-forward and observed ending capital. Zero when reconciled.</summary>
    public decimal ReconciliationVariance => ComputedEndingCapital - EndingCapital;

    /// <summary>True when the aggregate roll-forward ties to ending capital.</summary>
    public bool IsReconciled => Math.Abs(ReconciliationVariance) <= LedgerToleranceConstants.Balance;
}
