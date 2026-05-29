namespace Meridian.Ledger;

/// <summary>
/// Inputs for a fixed-income accrual and premium/discount amortization journal projection.
/// Amounts are expressed in the ledger book currency for the target account.
/// </summary>
public sealed record FixedIncomeAmortizationInput(
    string Symbol,
    LedgerAccount CarryingValueAccount,
    decimal CouponAccrual,
    decimal DiscountAccretion,
    decimal PremiumAmortization,
    string? FinancialAccountId = null,
    string? Description = null);

