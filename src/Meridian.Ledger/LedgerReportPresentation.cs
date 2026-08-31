using System.Globalization;

namespace Meridian.Ledger;

/// <summary>A titled, tabular section of a financial report, shared by every renderer.</summary>
public sealed record LedgerReportTable(
    string Title,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// Builds the canonical set of presentation tables from a report pack so every renderer (the
/// built-in fallback and the client-grade QuestPDF/ClosedXML renderer) shows identical figures.
/// </summary>
public static class LedgerReportPresentation
{
    /// <summary>Title of the statement-of-changes-in-partners'-capital table, shared so a renderer can
    /// substitute a bespoke layout for the generic table without matching a loose string.</summary>
    public const string PartnersCapitalTableTitle = "Statement of Changes in Partners' Capital";

    public static IReadOnlyList<LedgerReportTable> BuildTables(LedgerFinancialReportPack reportPack)
    {
        ArgumentNullException.ThrowIfNull(reportPack);
        var statements = reportPack.Statements;
        var tables = new List<LedgerReportTable>
        {
            BuildOverview(reportPack),
            BuildBalanceSheet(statements),
            BuildIncomeStatement(statements),
        };

        if (statements.CashFlow is { } cashFlow)
            tables.Add(BuildCashFlow(cashFlow));
        if (statements.PartnersCapital is { } partnersCapital)
            tables.Add(BuildPartnersCapital(partnersCapital));

        return tables;
    }

    private static LedgerReportTable BuildOverview(LedgerFinancialReportPack reportPack)
    {
        var request = reportPack.Request;
        var statements = reportPack.Statements;
        var rows = new List<IReadOnlyList<string>>
        {
            Row("Report", request.ReportId),
            Row("Fund", request.FundId),
            Row("Period", request.PeriodId),
            Row("Period start", request.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Row("Period end", request.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Row("As of", request.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Row("Base currency", request.BaseCurrency),
            Row("Total assets", Money(statements.TotalAssets)),
            Row("Total liabilities", Money(statements.TotalLiabilities)),
            Row("Ending equity", Money(statements.EndingEquity)),
            Row("Net income", Money(statements.NetIncome)),
            Row("Accounting equation variance", Money(statements.AccountingEquationVariance)),
            Row("Report pack signature", reportPack.Signature.PayloadChecksumSha256),
        };

        return new LedgerReportTable("Overview", ["Field", "Value"], rows);
    }

    private static LedgerReportTable BuildBalanceSheet(LedgerFinancialStatements statements)
    {
        var rows = statements.BalanceSheetRows
            .OrderBy(static row => row.Account.AccountType)
            .ThenBy(static row => row.Path, StringComparer.Ordinal)
            .Select(row => (IReadOnlyList<string>)
                [row.Account.AccountType.ToString(), row.Account.Name, Money(row.AggregateBalance)])
            .ToList();
        rows.Add(["", "Total assets", Money(statements.TotalAssets)]);
        rows.Add(["", "Total liabilities", Money(statements.TotalLiabilities)]);
        rows.Add(["", "Ending equity", Money(statements.EndingEquity)]);

        return new LedgerReportTable("Balance Sheet", ["Type", "Account", "Balance"], rows);
    }

    private static LedgerReportTable BuildIncomeStatement(LedgerFinancialStatements statements)
    {
        var rows = statements.IncomeStatementRows
            .OrderBy(static row => row.Account.AccountType)
            .ThenBy(static row => row.Path, StringComparer.Ordinal)
            .Select(row => (IReadOnlyList<string>)
                [row.Account.AccountType.ToString(), row.Account.Name, Money(row.AggregateBalance)])
            .ToList();
        rows.Add(["", "Total revenue", Money(statements.TotalRevenue)]);
        rows.Add(["", "Total expenses", Money(statements.TotalExpenses)]);
        rows.Add(["", "Net income", Money(statements.NetIncome)]);

        return new LedgerReportTable("Income Statement", ["Type", "Account", "Amount"], rows);
    }

    private static LedgerReportTable BuildCashFlow(LedgerCashFlowStatement cashFlow)
    {
        var rows = cashFlow.Lines
            .OrderBy(static line => line.Category)
            .ThenBy(static line => line.Description, StringComparer.Ordinal)
            .Select(line => (IReadOnlyList<string>)
                [line.Category.ToString(), line.Description, Money(line.Amount)])
            .ToList();
        rows.Add(["Operating", "Net operating cash flow", Money(cashFlow.OperatingCashFlow)]);
        rows.Add(["Investing", "Net investing cash flow", Money(cashFlow.InvestingCashFlow)]);
        rows.Add(["Financing", "Net financing cash flow", Money(cashFlow.FinancingCashFlow)]);
        rows.Add(["", "Net change in cash", Money(cashFlow.NetCashFlow)]);
        rows.Add(["", "Beginning cash", Money(cashFlow.BeginningCash)]);
        rows.Add(["", "Ending cash", Money(cashFlow.EndingCash)]);

        return new LedgerReportTable("Statement of Cash Flows", ["Activity", "Description", "Amount"], rows);
    }

    private static LedgerReportTable BuildPartnersCapital(LedgerPartnersCapitalStatement partnersCapital)
    {
        var rows = partnersCapital.Accounts
            .OrderBy(static account => account.AccountName, StringComparer.Ordinal)
            .ThenBy(static account => account.InvestorId ?? string.Empty, StringComparer.Ordinal)
            .Select(account => (IReadOnlyList<string>)
            [
                account.InvestorId ?? account.AccountName,
                Money(account.BeginningCapital),
                Money(account.Contributions),
                Money(account.Distributions),
                Money(account.IncomeGainAllocations),
                Money(account.ExpenseAllocations),
                Money(account.FeeAllocations),
                Money(account.EndingCapital),
            ])
            .ToList();
        rows.Add(
        [
            "Total",
            Money(partnersCapital.BeginningCapital),
            Money(partnersCapital.Contributions),
            Money(partnersCapital.Distributions),
            Money(partnersCapital.IncomeGainAllocations),
            Money(partnersCapital.ExpenseAllocations),
            Money(partnersCapital.FeeAllocations),
            Money(partnersCapital.EndingCapital),
        ]);

        return new LedgerReportTable(
            PartnersCapitalTableTitle,
            ["Partner", "Beginning", "Contributions", "Distributions", "Income & Gains", "Expenses", "Fees", "Ending"],
            rows);
    }

    private static IReadOnlyList<string> Row(params string[] cells) => cells;

    private static string Money(decimal value) => value.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
