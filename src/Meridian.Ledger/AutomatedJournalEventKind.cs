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

    /// <summary>Management fee incurred but not yet paid.</summary>
    ManagementFeeAccrued,

    /// <summary>Performance or incentive fee incurred but not yet paid.</summary>
    PerformanceFeeAccrued,

    /// <summary>Commission incurred but not yet paid or settled in cash.</summary>
    CommissionAccrued,

    /// <summary>Withholding tax obligation incurred but not yet remitted.</summary>
    WithholdingTaxAccrued,

    /// <summary>Daily fair-value mark-to-market adjustment for portfolio positions.</summary>
    FairValueMarkAdjustment,

    /// <summary>Period-close closing entries rolling net income into retained earnings.</summary>
    PeriodCloseClosingEntries,
}
