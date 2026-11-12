using System.Collections.Frozen;

namespace Meridian.Ledger;

/// <summary>
/// Explicit posting-account registry. Retains existing account identities so historical journals,
/// balances and durable fill retries remain compatible. The hierarchical <see cref="ChartOfAccounts"/>
/// remains a reporting rollup, rather than a source of inferred posting accounts.
/// </summary>
public sealed class StandardChartOfAccounts : IChartOfAccounts
{
    public static StandardChartOfAccounts Instance { get; } = new();

    private static readonly FrozenDictionary<LedgerAccountRole, AccountDefinition> Definitions =
        new Dictionary<LedgerAccountRole, AccountDefinition>
        {
            [LedgerAccountRole.Cash] = new(LedgerAccounts.Cash),
            [LedgerAccountRole.Capital] = new(LedgerAccounts.CapitalAccount),
            [LedgerAccountRole.Securities] = new(LedgerAccounts.Securities("_"), SymbolScope.Required),
            [LedgerAccountRole.ShortSecuritiesPayable] = new(LedgerAccounts.ShortSecuritiesPayable("_"), SymbolScope.Required),
            [LedgerAccountRole.RealizedGain] = new(LedgerAccounts.RealizedGain),
            [LedgerAccountRole.RealizedLoss] = new(LedgerAccounts.RealizedLoss),
            [LedgerAccountRole.CommissionExpense] = new(LedgerAccounts.CommissionExpense),
            [LedgerAccountRole.MarginInterestExpense] = new(LedgerAccounts.MarginInterestExpense),
            [LedgerAccountRole.CashInterestIncome] = new(LedgerAccounts.CashInterestIncome),
            [LedgerAccountRole.ShortRebateIncome] = new(LedgerAccounts.ShortRebateIncome),
            [LedgerAccountRole.DividendIncome] = new(LedgerAccounts.DividendIncome),
            [LedgerAccountRole.DividendExpense] = new(LedgerAccounts.DividendExpense),
            [LedgerAccountRole.CouponIncome] = new(LedgerAccounts.CouponIncome),
            [LedgerAccountRole.CouponExpense] = new(LedgerAccounts.CouponExpense),
            [LedgerAccountRole.CorporateActionIncome] = new(LedgerAccounts.CorporateActionIncome),
            [LedgerAccountRole.CorporateActionExpense] = new(LedgerAccounts.CorporateActionExpense),
            [LedgerAccountRole.FuturesMtmSettlement] = new(LedgerAccounts.FuturesMtmSettlement("_"), SymbolScope.Required),
            [LedgerAccountRole.LoanPrincipal] = Loan("LoanPrincipal", LedgerAccountType.Asset),
            [LedgerAccountRole.LoanAccruedInterestReceivable] = Loan("AccruedInterestReceivable", LedgerAccountType.Asset),
            [LedgerAccountRole.LoanInterestIncome] = Loan("InterestIncome", LedgerAccountType.Revenue),
            [LedgerAccountRole.LoanPikInterestReceivable] = Loan("PikInterestReceivable", LedgerAccountType.Asset),
            [LedgerAccountRole.LoanPikInterestIncome] = Loan("PikInterestIncome", LedgerAccountType.Revenue),
            [LedgerAccountRole.LoanCommitmentFeeReceivable] = Loan("CommitmentFeeReceivable", LedgerAccountType.Asset),
            [LedgerAccountRole.LoanCommitmentFeeIncome] = Loan("CommitmentFeeIncome", LedgerAccountType.Revenue),
            [LedgerAccountRole.LoanFeeReceivable] = Loan("FeeReceivable", LedgerAccountType.Asset),
            [LedgerAccountRole.LoanFeeIncome] = Loan("FeeIncome", LedgerAccountType.Revenue),
            [LedgerAccountRole.LoanPenaltyReceivable] = Loan("PenaltyReceivable", LedgerAccountType.Asset),
            [LedgerAccountRole.LoanPenaltyIncome] = Loan("PenaltyIncome", LedgerAccountType.Revenue),
            [LedgerAccountRole.LoanAndAccrualsClearing] = Loan("LoanAndAccrualsClearing", LedgerAccountType.Asset),
            [LedgerAccountRole.LoanPrepaidInterestLiability] = Loan("PrepaidInterestLiability", LedgerAccountType.Liability),
            [LedgerAccountRole.LoanUnappliedCashLiability] = Loan("UnappliedCashLiability", LedgerAccountType.Liability),
            [LedgerAccountRole.LoanWriteOffExpense] = Loan("WriteOffExpense", LedgerAccountType.Expense),
            [LedgerAccountRole.LoanDiscountAmortizationExpense] = Loan("DiscountAmortizationExpense", LedgerAccountType.Expense),
            [LedgerAccountRole.LoanDiscount] = Loan("LoanDiscount", LedgerAccountType.Asset),
            // Preserve the historical loan-premium liability identity; policy changes require a governed rebook.
            [LedgerAccountRole.LoanPremium] = Loan("LoanPremium", LedgerAccountType.Liability),
            [LedgerAccountRole.LoanPremiumAmortizationIncome] = Loan("PremiumAmortizationIncome", LedgerAccountType.Revenue),
            [LedgerAccountRole.LoanRestructuringLoss] = Loan("RestructuringLoss", LedgerAccountType.Expense)
        }.ToFrozenDictionary();

    /// <inheritdoc />
    public LedgerAccount Resolve(
        LedgerAccountRole role,
        string? financialAccountId = null,
        string? symbol = null)
    {
        if (!Definitions.TryGetValue(role, out var definition))
            throw new ArgumentOutOfRangeException(nameof(role), role, "Posting account role is not registered.");

        var accountId = string.IsNullOrWhiteSpace(financialAccountId) ? null : financialAccountId.Trim();
        var normalizedSymbol = string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();
        if (definition.SymbolScope == SymbolScope.Required && normalizedSymbol is null)
            throw new ArgumentException($"Posting account role '{role}' requires a symbol.", nameof(symbol));
        if (definition.SymbolScope == SymbolScope.None && normalizedSymbol is not null)
            throw new ArgumentException($"Posting account role '{role}' does not accept a symbol.", nameof(symbol));
        if (definition.RequiresFinancialAccount && accountId is null)
            throw new ArgumentException($"Posting account role '{role}' requires a financial account.", nameof(financialAccountId));

        return definition.Account with { Symbol = normalizedSymbol, FinancialAccountId = accountId };
    }

    private static AccountDefinition Loan(string name, LedgerAccountType type) =>
        new(new LedgerAccount(name, type), SymbolScope.Optional, RequiresFinancialAccount: true);

    private sealed record AccountDefinition(
        LedgerAccount Account,
        SymbolScope SymbolScope = SymbolScope.None,
        bool RequiresFinancialAccount = false);

    private enum SymbolScope { None, Optional, Required }
}
