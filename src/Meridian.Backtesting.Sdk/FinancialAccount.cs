namespace Meridian.Backtesting.Sdk;

/// <summary>Supported account categories used by the simulator and ledger.</summary>
public enum FinancialAccountKind
{
    Brokerage,
    Bank
}

/// <summary>
/// Account-specific financing and trading rules.
/// Brokerage accounts can opt into margin and shorting, while bank accounts typically use
/// only the cash-interest portion of the configuration.
/// </summary>
public sealed record FinancialAccountRules(
    bool AllowMargin = true,
    bool AllowShortSelling = true,
    double AnnualMarginRate = 0.05,
    double AnnualShortRebateRate = 0.02,
    double AnnualCashInterestRate = 0.0);

/// <summary>
/// Configures a user-owned cash or brokerage account participating in a simulation.
/// </summary>
public sealed record FinancialAccount(
    string AccountId,
    string DisplayName,
    FinancialAccountKind Kind,
    string? Institution = null,
    decimal InitialCash = 0m,
    FinancialAccountRules? Rules = null)
{
    public FinancialAccount Normalize()
    {
        if (string.IsNullOrWhiteSpace(AccountId))
            throw new ArgumentException("Account ID must not be null or whitespace.", nameof(AccountId));

        if (string.IsNullOrWhiteSpace(DisplayName))
            throw new ArgumentException("Display name must not be null or whitespace.", nameof(DisplayName));

        return this with
        {
            AccountId = AccountId.Trim(),
            DisplayName = DisplayName.Trim(),
            Institution = string.IsNullOrWhiteSpace(Institution) ? null : Institution.Trim(),
            Rules = Rules ?? new FinancialAccountRules(),
        };
    }

    public static FinancialAccount CreateDefaultBrokerage(
        decimal initialCash,
        double annualMarginRate,
        double annualShortRebateRate,
        string accountId = BacktestDefaults.DefaultBrokerageAccountId,
        string displayName = "Primary Brokerage",
        string institution = "Simulated Broker")
        => new(
            accountId,
            displayName,
            FinancialAccountKind.Brokerage,
            institution,
            initialCash,
            new FinancialAccountRules(
                AllowMargin: true,
                AllowShortSelling: true,
                AnnualMarginRate: annualMarginRate,
                AnnualShortRebateRate: annualShortRebateRate));
}

/// <summary>Shared defaults for account-aware backtests.</summary>
public static class BacktestDefaults
{
    public const string DefaultBrokerageAccountId = "primary-brokerage";
}
