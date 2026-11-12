namespace Meridian.Ledger;

/// <summary>
/// Registered economic roles shared by simulation, execution, and direct-lending postings.
/// These values are lookup keys, not persisted account identities or inferred account types.
/// </summary>
public enum LedgerAccountRole
{
    Cash,
    Capital,
    Securities,
    ShortSecuritiesPayable,
    RealizedGain,
    RealizedLoss,
    CommissionExpense,
    MarginInterestExpense,
    CashInterestIncome,
    ShortRebateIncome,
    DividendIncome,
    DividendExpense,
    CouponIncome,
    CouponExpense,
    CorporateActionIncome,
    CorporateActionExpense,
    FuturesMtmSettlement,
    LoanPrincipal,
    LoanAccruedInterestReceivable,
    LoanInterestIncome,
    LoanPikInterestReceivable,
    LoanPikInterestIncome,
    LoanCommitmentFeeReceivable,
    LoanCommitmentFeeIncome,
    LoanFeeReceivable,
    LoanFeeIncome,
    LoanPenaltyReceivable,
    LoanPenaltyIncome,
    LoanAndAccrualsClearing,
    LoanPrepaidInterestLiability,
    LoanUnappliedCashLiability,
    LoanWriteOffExpense,
    LoanDiscountAmortizationExpense,
    LoanDiscount,
    LoanPremium,
    LoanPremiumAmortizationIncome,
    LoanRestructuringLoss
}
