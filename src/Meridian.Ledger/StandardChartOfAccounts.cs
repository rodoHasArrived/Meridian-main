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
    public const string Revision = "meridian-standard-chart-v1";

    private static readonly FrozenDictionary<LedgerAccountRole, AccountDefinition> Definitions =
        new Dictionary<LedgerAccountRole, AccountDefinition>
        {
            [LedgerAccountRole.Cash] = new("1000", LedgerAccounts.Cash),
            [LedgerAccountRole.Capital] = new("3000", LedgerAccounts.CapitalAccount),
            [LedgerAccountRole.Securities] = new("1100", LedgerAccounts.Securities("_"), SymbolScope.Required),
            [LedgerAccountRole.ShortSecuritiesPayable] = new("2100", LedgerAccounts.ShortSecuritiesPayable("_"), SymbolScope.Required),
            [LedgerAccountRole.RealizedGain] = new("4000", LedgerAccounts.RealizedGain),
            [LedgerAccountRole.RealizedLoss] = new("5000", LedgerAccounts.RealizedLoss),
            [LedgerAccountRole.CommissionExpense] = new("5010", LedgerAccounts.CommissionExpense),
            [LedgerAccountRole.MarginInterestExpense] = new("5020", LedgerAccounts.MarginInterestExpense),
            [LedgerAccountRole.CashInterestIncome] = new("4010", LedgerAccounts.CashInterestIncome),
            [LedgerAccountRole.ShortRebateIncome] = new("4020", LedgerAccounts.ShortRebateIncome),
            [LedgerAccountRole.DividendIncome] = new("4030", LedgerAccounts.DividendIncome),
            [LedgerAccountRole.DividendExpense] = new("5030", LedgerAccounts.DividendExpense),
            [LedgerAccountRole.DividendReceivable] = new("1110", LedgerAccounts.DividendReceivable("_"), SymbolScope.Required),
            [LedgerAccountRole.AccruedInterestReceivable] = new("1120", LedgerAccounts.AccruedInterestReceivable("_"), SymbolScope.Required),
            [LedgerAccountRole.CouponIncome] = new("4040", LedgerAccounts.CouponIncome),
            [LedgerAccountRole.CouponExpense] = new("5040", LedgerAccounts.CouponExpense),
            [LedgerAccountRole.CorporateActionIncome] = new("4050", LedgerAccounts.CorporateActionIncome),
            [LedgerAccountRole.CorporateActionExpense] = new("5050", LedgerAccounts.CorporateActionExpense),
            [LedgerAccountRole.FuturesMtmSettlement] = new("4060", LedgerAccounts.FuturesMtmSettlement("_"), SymbolScope.Required),
            [LedgerAccountRole.LoanPrincipal] = Loan("1200", "LoanPrincipal", LedgerAccountType.Asset),
            [LedgerAccountRole.LoanAccruedInterestReceivable] = Loan("1210", "AccruedInterestReceivable", LedgerAccountType.Asset),
            [LedgerAccountRole.LoanInterestIncome] = Loan("4100", "InterestIncome", LedgerAccountType.Revenue),
            [LedgerAccountRole.LoanPikInterestReceivable] = Loan("1220", "PikInterestReceivable", LedgerAccountType.Asset),
            [LedgerAccountRole.LoanPikInterestIncome] = Loan("4110", "PikInterestIncome", LedgerAccountType.Revenue),
            [LedgerAccountRole.LoanCommitmentFeeReceivable] = Loan("1230", "CommitmentFeeReceivable", LedgerAccountType.Asset),
            [LedgerAccountRole.LoanCommitmentFeeIncome] = Loan("4120", "CommitmentFeeIncome", LedgerAccountType.Revenue),
            [LedgerAccountRole.LoanFeeReceivable] = Loan("1240", "FeeReceivable", LedgerAccountType.Asset),
            [LedgerAccountRole.LoanFeeIncome] = Loan("4130", "FeeIncome", LedgerAccountType.Revenue),
            [LedgerAccountRole.LoanPenaltyReceivable] = Loan("1250", "PenaltyReceivable", LedgerAccountType.Asset),
            [LedgerAccountRole.LoanPenaltyIncome] = Loan("4140", "PenaltyIncome", LedgerAccountType.Revenue),
            [LedgerAccountRole.LoanAndAccrualsClearing] = Loan("1260", "LoanAndAccrualsClearing", LedgerAccountType.Asset),
            [LedgerAccountRole.LoanPrepaidInterestLiability] = Loan("2200", "PrepaidInterestLiability", LedgerAccountType.Liability),
            [LedgerAccountRole.LoanUnappliedCashLiability] = Loan("2210", "UnappliedCashLiability", LedgerAccountType.Liability),
            [LedgerAccountRole.LoanWriteOffExpense] = Loan("5100", "WriteOffExpense", LedgerAccountType.Expense),
            [LedgerAccountRole.LoanDiscountAmortizationExpense] = Loan("5110", "DiscountAmortizationExpense", LedgerAccountType.Expense),
            [LedgerAccountRole.LoanDiscount] = Loan("1270", "LoanDiscount", LedgerAccountType.Asset),
            // Preserve the historical loan-premium liability identity; policy changes require a governed rebook.
            [LedgerAccountRole.LoanPremium] = Loan("2220", "LoanPremium", LedgerAccountType.Liability),
            [LedgerAccountRole.LoanPremiumAmortizationIncome] = Loan("4150", "PremiumAmortizationIncome", LedgerAccountType.Revenue),
            [LedgerAccountRole.LoanRestructuringLoss] = Loan("5120", "RestructuringLoss", LedgerAccountType.Expense),
            [LedgerAccountRole.MarginLoanPayable] = new("2110", LedgerAccounts.MarginLoanPayable),
            [LedgerAccountRole.ShortSaleCollateralReceivable] = new("1130", LedgerAccounts.ShortSaleCollateralReceivable)
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, LedgerAccountRole> RolesByCode =
        Definitions.ToFrozenDictionary(pair => pair.Value.Code, pair => pair.Key, StringComparer.Ordinal);

    private static readonly FrozenDictionary<(string, LedgerAccountType), LedgerAccountRole> RolesByAccount =
        Definitions.ToFrozenDictionary(pair => (pair.Value.Account.Name, pair.Value.Account.AccountType), pair => pair.Key);

    /// <summary>Resolves an explicitly registered posting code with the same scope rules as its role.</summary>
    public LedgerAccount ResolveCode(string code, string? financialAccountId = null, string? symbol = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (!RolesByCode.TryGetValue(code.Trim(), out var role))
            throw new ArgumentException($"Posting account code '{code}' is not registered.", nameof(code));

        return Resolve(role, financialAccountId, symbol);
    }

    /// <inheritdoc />
    public LedgerAccountChartReference? GetPostingReference(LedgerAccount account)
        => TryGetRegistered(account, out var role) ? Definitions[role].Reference : null;

    /// <inheritdoc />
    public LedgerAccount GetDefinition(LedgerAccountRole role)
    {
        if (!Definitions.TryGetValue(role, out var definition))
            throw new ArgumentOutOfRangeException(nameof(role), role, "Posting account role is not registered.");

        return definition.Account with { Symbol = null, FinancialAccountId = null };
    }

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

    /// <inheritdoc />
    public bool TryGetRegistered(LedgerAccount account, out LedgerAccountRole role)
    {
        ArgumentNullException.ThrowIfNull(account);
        role = default;
        if (!RolesByAccount.TryGetValue((account.Name, account.AccountType), out var candidate) ||
            !IsCanonicalOptional(account.Symbol, upper: true) ||
            !IsCanonicalOptional(account.FinancialAccountId, upper: false))
            return false;

        var definition = Definitions[candidate];
        var symbolAccepted = definition.SymbolScope switch
        {
            SymbolScope.Required => account.Symbol is not null,
            SymbolScope.None => account.Symbol is null,
            _ => true
        };
        if (!symbolAccepted || (definition.RequiresFinancialAccount && account.FinancialAccountId is null))
            return false;

        role = candidate;
        return true;
    }

    // Resolve() trims scope values and upper-cases symbols, so only that canonical form is registered.
    private static bool IsCanonicalOptional(string? value, bool upper)
        => value is null ||
           (!string.IsNullOrWhiteSpace(value) && value == value.Trim() &&
            (!upper || value == value.ToUpperInvariant()));

    private static AccountDefinition Loan(string code, string name, LedgerAccountType type) =>
        new(code, new LedgerAccount(name, type), SymbolScope.Optional, RequiresFinancialAccount: true);

    private sealed record AccountDefinition(
        string Code,
        LedgerAccount Account,
        SymbolScope SymbolScope = SymbolScope.None,
        bool RequiresFinancialAccount = false)
    {
        public LedgerAccountChartReference Reference { get; } = new($"standard:{Code}", Revision, Code);
    }

    private enum SymbolScope { None, Optional, Required }
}
