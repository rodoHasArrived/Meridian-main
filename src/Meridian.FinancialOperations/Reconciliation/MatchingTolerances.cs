using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed record MatchingTolerances(decimal Quantity, decimal Price, decimal MarketValue, decimal CashAmount, TimeSpan TimingWindow)
{
    public StatementToleranceProfile ToStatementToleranceProfile() => new(
        StatementToleranceProfile.DefaultProfileId,
        StatementToleranceProfile.DefaultProfileVersion,
        [new CashToleranceRule("cash-amount-window-v1", CashAmount, null, TimingWindow)],
        [new PositionToleranceRule("position-quantity-price-value-v1", Quantity, MarketValue, Price)],
        [new TransactionToleranceRule("transaction-amount-price-settlement-v1", CashAmount, TimingWindow, Price)]);
}
