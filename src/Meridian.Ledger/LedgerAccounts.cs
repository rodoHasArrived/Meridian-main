namespace Meridian.Ledger;

/// <summary>
/// Well-known chart-of-accounts entries used in double-entry bookkeeping.
/// </summary>
public static class LedgerAccounts
{
    /// <summary>Simulated cash held in the trading account.</summary>
    public static readonly LedgerAccount Cash =
        new("Cash", LedgerAccountType.Asset);

    /// <summary>Initial capital deposited into the account at the start of the run.</summary>
    public static readonly LedgerAccount CapitalAccount =
        new("Capital Account", LedgerAccountType.Equity);

    /// <summary>Realized gain on trades where proceeds exceed cost basis.</summary>
    public static readonly LedgerAccount RealizedGain =
        new("Realized Gain", LedgerAccountType.Revenue);

    /// <summary>Realized loss on trades where cost basis exceeds proceeds.</summary>
    public static readonly LedgerAccount RealizedLoss =
        new("Realized Loss", LedgerAccountType.Expense);

    /// <summary>Brokerage commission expense charged on each order fill.</summary>
    public static readonly LedgerAccount CommissionExpense =
        new("Commission Expense", LedgerAccountType.Expense);

    /// <summary>Margin interest expense charged daily on debit (borrowed) balances.</summary>
    public static readonly LedgerAccount MarginInterestExpense =
        new("Margin Interest Expense", LedgerAccountType.Expense);

    /// <summary>Interest income credited to positive idle cash balances.</summary>
    public static readonly LedgerAccount CashInterestIncome =
        new("Cash Interest Income", LedgerAccountType.Revenue);

    /// <summary>Short-sale rebate income credited daily by the broker on short positions.</summary>
    public static readonly LedgerAccount ShortRebateIncome =
        new("Short Rebate Income", LedgerAccountType.Revenue);

    /// <summary>Dividend income received on long positions.</summary>
    public static readonly LedgerAccount DividendIncome =
        new("Dividend Income", LedgerAccountType.Revenue);

    public static LedgerAccount CashAccount(string financialAccountId) =>
        CreateScoped("Cash", LedgerAccountType.Asset, financialAccountId);

    public static LedgerAccount CapitalAccountFor(string financialAccountId) =>
        CreateScoped("Capital Account", LedgerAccountType.Equity, financialAccountId);

    public static LedgerAccount RealizedGainFor(string financialAccountId) =>
        CreateScoped("Realized Gain", LedgerAccountType.Revenue, financialAccountId);

    public static LedgerAccount RealizedLossFor(string financialAccountId) =>
        CreateScoped("Realized Loss", LedgerAccountType.Expense, financialAccountId);

    public static LedgerAccount CommissionExpenseFor(string financialAccountId) =>
        CreateScoped("Commission Expense", LedgerAccountType.Expense, financialAccountId);

    public static LedgerAccount MarginInterestExpenseFor(string financialAccountId) =>
        CreateScoped("Margin Interest Expense", LedgerAccountType.Expense, financialAccountId);

    public static LedgerAccount CashInterestIncomeFor(string financialAccountId) =>
        CreateScoped("Cash Interest Income", LedgerAccountType.Revenue, financialAccountId);

    public static LedgerAccount ShortRebateIncomeFor(string financialAccountId) =>
        CreateScoped("Short Rebate Income", LedgerAccountType.Revenue, financialAccountId);

    public static LedgerAccount DividendIncomeFor(string financialAccountId) =>
        CreateScoped("Dividend Income", LedgerAccountType.Revenue, financialAccountId);

    public static LedgerAccount DividendExpenseFor(string financialAccountId) =>
        CreateScoped("Dividend Expense", LedgerAccountType.Expense, financialAccountId);

    public static LedgerAccount CouponIncomeFor(string financialAccountId) =>
        CreateScoped("Coupon Income", LedgerAccountType.Revenue, financialAccountId);

    public static LedgerAccount CouponExpenseFor(string financialAccountId) =>
        CreateScoped("Coupon Expense", LedgerAccountType.Expense, financialAccountId);

    public static LedgerAccount CorporateActionIncomeFor(string financialAccountId) =>
        CreateScoped("Corporate Action Income", LedgerAccountType.Revenue, financialAccountId);

    public static LedgerAccount CorporateActionExpenseFor(string financialAccountId) =>
        CreateScoped("Corporate Action Expense", LedgerAccountType.Expense, financialAccountId);

    /// <summary>Dividend expense owed on short positions or other negative dividend adjustments.</summary>
    public static readonly LedgerAccount DividendExpense =
        new("Dividend Expense", LedgerAccountType.Expense);

    /// <summary>Bond or fund coupon income received on held units.</summary>
    public static readonly LedgerAccount CouponIncome =
        new("Coupon Income", LedgerAccountType.Revenue);

    /// <summary>Coupon expense owed on short positions or negative coupon adjustments.</summary>
    public static readonly LedgerAccount CouponExpense =
        new("Coupon Expense", LedgerAccountType.Expense);

    /// <summary>Income from non-dividend corporate actions, merger cash, and miscellaneous asset events.</summary>
    public static readonly LedgerAccount CorporateActionIncome =
        new("Corporate Action Income", LedgerAccountType.Revenue);

    /// <summary>Expense from fees, merger charges, and other negative asset-event cash adjustments.</summary>
    public static readonly LedgerAccount CorporateActionExpense =
        new("Corporate Action Expense", LedgerAccountType.Expense);

    /// <summary>
    /// Returns the asset account representing equity holdings in <paramref name="symbol"/>.
    /// Each symbol has its own securities account so per-symbol cost-basis is tracked separately.
    /// The symbol is normalized to upper-case so accounts are case-insensitive by identity.
    /// </summary>
    public static LedgerAccount Securities(string symbol, string? financialAccountId = null)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        return new("Securities", LedgerAccountType.Asset, normalizedSymbol, NormalizeOptionalAccountId(financialAccountId));
    }

    /// <summary>
    /// Returns the asset account representing dividends declared but not yet received for
    /// <paramref name="symbol"/> (ex-date passed; pay-date still pending).
    /// Post: Dr DividendReceivable / Cr DividendIncome on declaration.
    ///        Dr Cash / Cr DividendReceivable when payment arrives.
    /// The symbol is normalized to upper-case.
    /// </summary>
    public static LedgerAccount DividendReceivable(string symbol, string? financialAccountId = null)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        return new("Dividend Receivable", LedgerAccountType.Asset, normalizedSymbol, NormalizeOptionalAccountId(financialAccountId));
    }

    /// <summary>
    /// Returns the asset account representing bond or fund coupon interest accrued but not yet
    /// received for <paramref name="symbol"/>.
    /// The symbol is normalized to upper-case.
    /// </summary>
    public static LedgerAccount AccruedInterestReceivable(string symbol, string? financialAccountId = null)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        return new("Accrued Interest Receivable", LedgerAccountType.Asset, normalizedSymbol, NormalizeOptionalAccountId(financialAccountId));
    }

    /// <summary>
    /// Returns the revenue account for non-dividend corporate action distributions
    /// (spin-off proceeds, rights issue income, merger cash, etc.) for <paramref name="symbol"/>.
    /// The symbol is normalized to upper-case.
    /// </summary>
    public static LedgerAccount CorpActionDistribution(string symbol, string? financialAccountId = null)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        return new("Corporate Action Distribution", LedgerAccountType.Revenue, normalizedSymbol, NormalizeOptionalAccountId(financialAccountId));
    }

    /// <summary>
    /// Returns the liability account representing the obligation to return borrowed shares for
    /// a short position in <paramref name="symbol"/>.
    /// Each symbol has its own short payable account. The symbol is normalized to upper-case.
    /// </summary>
    public static LedgerAccount ShortSecuritiesPayable(string symbol, string? financialAccountId = null)
    {
        var normalizedSymbol = NormalizeSymbol(symbol);
        return new("Short Securities Payable", LedgerAccountType.Liability, normalizedSymbol, NormalizeOptionalAccountId(financialAccountId));
    }

    private static LedgerAccount CreateScoped(string name, LedgerAccountType accountType, string financialAccountId)
        => new(name, accountType, FinancialAccountId: NormalizeAccountId(financialAccountId));

    private static string NormalizeSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return symbol.Trim().ToUpperInvariant();
    }

    private static string NormalizeAccountId(string? financialAccountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(financialAccountId);
        return financialAccountId.Trim();
    }

    private static string? NormalizeOptionalAccountId(string? financialAccountId)
        => string.IsNullOrWhiteSpace(financialAccountId) ? null : financialAccountId.Trim();
}
