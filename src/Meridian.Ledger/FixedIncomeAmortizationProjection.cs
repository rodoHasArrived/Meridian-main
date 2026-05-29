namespace Meridian.Ledger;

/// <summary>
/// Balanced journal lines produced for a fixed-income accrual/amortization event.
/// </summary>
public sealed record FixedIncomeAmortizationProjection(
    string Symbol,
    string Description,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> Lines)
{
    /// <summary>Total debits in the projected journal lines.</summary>
    public decimal TotalDebits => Lines.Sum(static line => line.debit);

    /// <summary>Total credits in the projected journal lines.</summary>
    public decimal TotalCredits => Lines.Sum(static line => line.credit);

    /// <summary>True when total projected debits equal projected credits.</summary>
    public bool IsBalanced => TotalDebits == TotalCredits;
}

