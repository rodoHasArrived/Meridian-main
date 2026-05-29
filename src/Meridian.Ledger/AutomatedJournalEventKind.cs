namespace Meridian.Ledger;

/// <summary>
/// Supported economic events for deterministic journal draft automation.
/// </summary>
public enum AutomatedJournalEventKind
{
    /// <summary>Dividend declared but not yet received.</summary>
    DividendDeclared,

    /// <summary>Dividend cash received against an existing receivable.</summary>
    DividendReceived,

    /// <summary>Cash interest credited to the account.</summary>
    CashInterestCredited,

    /// <summary>Positive corporate-action cash distribution.</summary>
    CorporateActionIncome,

    /// <summary>Corporate-action fee or negative adjustment.</summary>
    CorporateActionExpense,
}

