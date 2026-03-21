namespace Meridian.Ledger;

/// <summary>Classifies a ledger account under standard double-entry accounting categories.</summary>
public enum LedgerAccountType
{
    /// <summary>Resources owned (cash, long securities positions).</summary>
    Asset,

    /// <summary>Obligations owed (margin debt to broker).</summary>
    Liability,

    /// <summary>Net worth / owner's initial capital.</summary>
    Equity,

    /// <summary>Inflows that increase equity (realized gains, dividends, short rebates).</summary>
    Revenue,

    /// <summary>Outflows that decrease equity (commissions, margin interest, realized losses).</summary>
    Expense,
}
