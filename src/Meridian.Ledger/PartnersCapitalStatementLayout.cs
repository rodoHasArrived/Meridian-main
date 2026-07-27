using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>
/// The economic role a capital account plays in a partners' capital statement. This is a
/// presentation label only — it groups and titles the roll-forward lines a fund administrator hands
/// to investors and never alters any figure. Classification is derived from the ledger-owned account
/// name produced by <see cref="LedgerAccounts"/>, so a mislabel can only change a caption, not a
/// balance.
/// </summary>
public enum PartnersCapitalPartnerRole
{
    /// <summary>Limited-partner (investor) capital, e.g. <c>Investor Capital</c> accounts.</summary>
    LimitedPartner,

    /// <summary>General-partner economics, e.g. <c>Carried Interest Allocation</c> accounts.</summary>
    GeneralPartner,

    /// <summary>Period result not yet closed to a named partner (the undistributed net income line).</summary>
    UndistributedResult,

    /// <summary>Undesignated fund capital (a generic capital account or retained earnings).</summary>
    GeneralCapital,
}

/// <summary>
/// One bespoke-layout line of the statement of changes in partners' capital: a partner-oriented
/// roll-forward carrying the income/expense/fee breakout plus the partner's ownership share of
/// ending capital. Money fields stay typed (not pre-formatted) so a renderer can emit computable
/// numeric cells rather than a text dump.
/// </summary>
public sealed record PartnersCapitalStatementLine(
    string PartnerLabel,
    PartnersCapitalPartnerRole Role,
    decimal BeginningCapital,
    decimal Contributions,
    decimal Distributions,
    decimal IncomeGainAllocations,
    decimal ExpenseAllocations,
    decimal FeeAllocations,
    decimal AllocatedResult,
    decimal OtherMovements,
    decimal EndingCapital,
    decimal OwnershipPercent);

/// <summary>
/// A bespoke, client-grade layout of the statement of changes in partners' capital. Unlike the
/// generic <see cref="LedgerReportTable"/> projection, this model keeps money typed, classifies each
/// account by partner role, computes each partner's ownership percentage of ending capital, and
/// anchors the statement to the fund's ledger-backed net asset value (the unitized NAV base) with an
/// explicit reconciliation flag. It is a pure projection over a
/// <see cref="LedgerPartnersCapitalStatement"/> — it introduces no new ledger fact.
/// </summary>
public sealed record PartnersCapitalStatementLayout(
    string FundId,
    string PeriodId,
    string BaseCurrency,
    DateTimeOffset PeriodStart,
    DateTimeOffset AsOf,
    decimal NetAssetValue,
    bool TiesToNetAssets,
    IReadOnlyList<PartnersCapitalStatementLine> Lines,
    PartnersCapitalStatementLine Total)
{
    /// <summary>Reconciliation gap between ending partners' capital and the fund's net asset value.</summary>
    public decimal NetAssetVariance => Total.EndingCapital - NetAssetValue;
}

/// <summary>
/// Builds a <see cref="PartnersCapitalStatementLayout"/> from ledger-backed statements. The layout is
/// a deterministic function of its inputs so it renders to byte-stable governed artifacts.
/// </summary>
public static class PartnersCapitalStatementLayoutBuilder
{
    /// <summary>
    /// Projects the partners' capital statement inside <paramref name="pack"/>, anchored to the same
    /// pack's ledger-backed net assets (ending equity = total assets − total liabilities), which is
    /// the unitized NAV base the statement reconciles to.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The pack carries no partners' capital statement to lay out.
    /// </exception>
    public static PartnersCapitalStatementLayout Build(LedgerFinancialReportPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var statement = pack.Statements.PartnersCapital
            ?? throw new InvalidOperationException(
                "Report pack has no partners' capital statement to lay out.");

        return Build(
            statement,
            pack.Request.FundId,
            pack.Request.PeriodId,
            pack.Request.BaseCurrency,
            pack.Statements.EndingEquity);
    }

    /// <summary>
    /// Projects <paramref name="statement"/> into the bespoke layout, expressing each partner's
    /// ownership share and anchoring the whole statement to <paramref name="netAssetValue"/> (the
    /// fund's ledger-backed net asset value / NAV base).
    /// </summary>
    public static PartnersCapitalStatementLayout Build(
        LedgerPartnersCapitalStatement statement,
        string fundId,
        string periodId,
        string baseCurrency,
        decimal netAssetValue)
    {
        ArgumentNullException.ThrowIfNull(statement);

        // Ownership is a partner's share of total ending partners' capital. Dividing every line by
        // the same total means the column foots to 100% (each line = lineEnding / totalEnding, and the
        // line endings sum to the total by construction). A fully-distributed fund has no capital to
        // apportion, so every share is zero.
        var totalEnding = statement.EndingCapital;

        var lines = statement.Accounts
            .Select(account => new PartnersCapitalStatementLine(
                PartnerLabel: string.IsNullOrWhiteSpace(account.InvestorId)
                    ? account.AccountName
                    : account.InvestorId,
                Role: ClassifyRole(account.AccountName),
                BeginningCapital: account.BeginningCapital,
                Contributions: account.Contributions,
                Distributions: account.Distributions,
                IncomeGainAllocations: account.IncomeGainAllocations,
                ExpenseAllocations: account.ExpenseAllocations,
                FeeAllocations: account.FeeAllocations,
                AllocatedResult: account.AllocatedResult,
                OtherMovements: account.OtherMovements,
                EndingCapital: account.EndingCapital,
                OwnershipPercent: OwnershipShare(account.EndingCapital, totalEnding)))
            .OrderBy(static line => line.Role)
            .ThenBy(static line => line.PartnerLabel, StringComparer.Ordinal)
            .ToList();

        var total = new PartnersCapitalStatementLine(
            PartnerLabel: "Total partners' capital",
            Role: PartnersCapitalPartnerRole.GeneralCapital,
            BeginningCapital: statement.BeginningCapital,
            Contributions: statement.Contributions,
            Distributions: statement.Distributions,
            IncomeGainAllocations: statement.IncomeGainAllocations,
            ExpenseAllocations: statement.ExpenseAllocations,
            FeeAllocations: statement.FeeAllocations,
            AllocatedResult: statement.AllocatedResult,
            OtherMovements: statement.OtherMovements,
            EndingCapital: statement.EndingCapital,
            OwnershipPercent: lines.Sum(static line => line.OwnershipPercent));

        return new PartnersCapitalStatementLayout(
            FundId: fundId,
            PeriodId: periodId,
            BaseCurrency: baseCurrency,
            PeriodStart: statement.PeriodStart,
            AsOf: statement.AsOf,
            NetAssetValue: netAssetValue,
            TiesToNetAssets: Math.Abs(statement.EndingCapital - netAssetValue) <= LedgerToleranceConstants.Balance,
            Lines: lines,
            Total: total);
    }

    private static decimal OwnershipShare(decimal endingCapital, decimal totalEndingCapital)
        => totalEndingCapital == 0m ? 0m : endingCapital / totalEndingCapital * 100m;

    // Role is derived from the stable account names minted by LedgerAccounts. It is a caption only:
    // an unrecognized capital account falls back to GeneralCapital and its figures are untouched.
    private static PartnersCapitalPartnerRole ClassifyRole(string accountName)
    {
        if (accountName.Equals("Undistributed Net Income", StringComparison.Ordinal))
            return PartnersCapitalPartnerRole.UndistributedResult;
        if (accountName.Contains("Carried Interest", StringComparison.OrdinalIgnoreCase))
            return PartnersCapitalPartnerRole.GeneralPartner;
        if (accountName.Contains("Investor Capital", StringComparison.OrdinalIgnoreCase))
            return PartnersCapitalPartnerRole.LimitedPartner;
        return PartnersCapitalPartnerRole.GeneralCapital;
    }
}
