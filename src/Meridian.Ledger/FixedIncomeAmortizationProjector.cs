namespace Meridian.Ledger;

/// <summary>
/// Builds deterministic fixed-income journal lines for coupon accrual, discount accretion, and premium amortization.
/// </summary>
public static class FixedIncomeAmortizationProjector
{
    /// <summary>
    /// Projects balanced journal lines for one fixed-income accrual/amortization event.
    /// </summary>
    public static FixedIncomeAmortizationProjection Project(FixedIncomeAmortizationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.CarryingValueAccount);

        var symbol = NormalizeSymbol(input.Symbol);
        ValidateAmount(input.CouponAccrual, nameof(input.CouponAccrual));
        ValidateAmount(input.DiscountAccretion, nameof(input.DiscountAccretion));
        ValidateAmount(input.PremiumAmortization, nameof(input.PremiumAmortization));

        if (input.CarryingValueAccount.AccountType != LedgerAccountType.Asset)
        {
            throw new ArgumentException(
                "Fixed-income carrying value account must be an asset account for held securities.",
                nameof(input));
        }

        if (input.CouponAccrual == 0m && input.DiscountAccretion == 0m && input.PremiumAmortization == 0m)
            throw new ArgumentException("At least one fixed-income accrual or amortization amount must be non-zero.", nameof(input));

        var financialAccountId = NormalizeOptional(input.FinancialAccountId);
        var couponIncome = string.IsNullOrWhiteSpace(financialAccountId)
            ? LedgerAccounts.CouponIncome
            : LedgerAccounts.CouponIncomeFor(financialAccountId);
        var accruedInterest = LedgerAccounts.AccruedInterestReceivable(symbol, financialAccountId);
        var lines = new List<(LedgerAccount account, decimal debit, decimal credit)>();

        if (input.CouponAccrual > 0m)
        {
            lines.Add((accruedInterest, input.CouponAccrual, 0m));
            lines.Add((couponIncome, 0m, input.CouponAccrual));
        }

        if (input.DiscountAccretion > 0m)
        {
            lines.Add((input.CarryingValueAccount, input.DiscountAccretion, 0m));
            lines.Add((couponIncome, 0m, input.DiscountAccretion));
        }

        if (input.PremiumAmortization > 0m)
        {
            lines.Add((couponIncome, input.PremiumAmortization, 0m));
            lines.Add((input.CarryingValueAccount, 0m, input.PremiumAmortization));
        }

        var description = string.IsNullOrWhiteSpace(input.Description)
            ? $"Fixed income accrual/amortization for {symbol}"
            : input.Description.Trim();

        return new FixedIncomeAmortizationProjection(symbol, description, lines);
    }

    private static void ValidateAmount(decimal amount, string parameterName)
    {
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(parameterName, "Fixed-income accrual and amortization amounts cannot be negative.");
    }

    private static string NormalizeSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return symbol.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

