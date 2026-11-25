using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>Versioned statement definition bound to the retained source before certification.</summary>
public sealed record LedgerStatementControlDefinition(string Version, AccountingBasisKindDto AccountingBasis)
{
    public const string CurrentVersion = "ledger-statement-control-v1";

    public IReadOnlyList<Guid> CashFxRevaluationJournalIds { get; init; } = [];
    public long? CloseSnapshotVersion { get; init; }
    public string? CloseSnapshotHashSha256 { get; init; }
    public Guid? FiscalYearCloseReceiptId { get; init; }
    public string? FiscalYearClosePlanHashSha256 { get; init; }
    public Guid? BookFxSettlementSetId { get; init; }
    public string? BookFxSettlementPlanHashSha256 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LedgerStatementComparisonDefinition? Comparison { get; init; }
}

public sealed record LedgerStatementComparisonDefinition(
    Guid PeriodId, DateOnly StartDate, DateOnly EndDate, long CloseSnapshotVersion, string CloseSnapshotHashSha256,
    IReadOnlyList<Guid> CashFxRevaluationJournalIds);

public sealed record LedgerStatementControlRow(
    string Statement,
    string RowKey,
    string Label,
    string Measure,
    decimal Amount,
    Guid? InstrumentId = null,
    string? Symbol = null);

public sealed record LedgerStatementControlLineMapping(
    Guid JournalEntryId,
    Guid LedgerEntryId,
    string AccountRowKey,
    string? ProfitAndLossRowKey,
    bool IsOpening,
    bool IsClosingTransfer,
    decimal Debit,
    decimal Credit,
    string? ChartAccountNodeId,
    string? AccountBindingRevisionId)
{
    public LedgerAccountType AccountType { get; init; }
    public bool IsCash { get; init; }
    public string? CapitalAccountRowKey { get; init; }
}

/// <summary>Exact period statements, controls, and a complete source-to-account mapping.</summary>
public sealed record LedgerStatementControlPack(
    LedgerStatementControlDefinition Definition,
    string Currency,
    IReadOnlyList<LedgerStatementControlRow> Rows,
    IReadOnlyList<LedgerStatementControlLineMapping> LineMappings)
{
    public const string ArtifactName = "statement-controls.json";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LedgerStatementComparison? Comparison { get; init; }

    /// <summary>One invariant table shared by the workbook and Parquet exporters.</summary>
    public LedgerReportTable ToTable()
    {
        string[] headers = ["Statement", "Row Key", "Label", "Measure", "Amount", "Currency", "Instrument ID", "Symbol"];
        if (Comparison is not { } comparison || Definition.Comparison is not { } scope)
            return new("Statement Controls", headers, Rows.Select(row => Cells(row, Currency)).ToArray());

        return new("Statement Controls", [.. headers, "Accounting period ID", "Period role"],
            Rows.Select(row => (IReadOnlyList<string>)[.. Cells(row, Currency), comparison.CurrentPeriodId, "Current"])
                .Concat(comparison.Prior.Rows.Select(row => (IReadOnlyList<string>)[.. Cells(row, Currency),
                    scope.PeriodId.ToString("D"), "Comparative"])).ToArray());
    }

    private static IReadOnlyList<string> Cells(LedgerStatementControlRow row, string currency) =>
        [row.Statement, row.RowKey, row.Label, row.Measure, row.Amount.ToString("G29", CultureInfo.InvariantCulture),
            currency, row.InstrumentId?.ToString("D") ?? string.Empty, row.Symbol ?? string.Empty];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);
}

public sealed record LedgerStatementComparison(string CurrentPeriodId, LedgerStatementControlPack Prior);

/// <summary>Compiles the fixed statement mappings from the same journal population as the report.</summary>
internal static class LedgerStatementControlPackBuilder
{
    private const string BalanceVariance = "Variance";

    internal static LedgerStatementControlPack Build(
        IReadOnlyLedger ledger,
        LedgerReportPackRequest request,
        LedgerFinancialStatements statements,
        string? financialAccountId,
        LedgerStatementControlPack? prior)
    {
        var definition = request.StatementControl!;
        if (definition.Version != LedgerStatementControlDefinition.CurrentVersion || !Enum.IsDefined(definition.AccountingBasis))
            throw new LedgerValidationException("The retained financial statement control definition is unsupported.");
        if (definition.Comparison is { } comparison
            ? prior is null || prior.Comparison is not null || prior.Currency != request.BaseCurrency
                || prior.Definition.AccountingBasis != definition.AccountingBasis
                || prior.Definition.CloseSnapshotVersion != comparison.CloseSnapshotVersion
                || prior.Definition.CloseSnapshotHashSha256 != comparison.CloseSnapshotHashSha256
            : prior is not null)
            throw new LedgerValidationException("The comparative statement controls do not match their retained close definition.");

        var postings = ledger.GetJournalEntries(to: request.AsOf)
            .OrderBy(static entry => entry.Timestamp).ThenBy(static entry => entry.JournalEntryId)
            .SelectMany(entry => ProjectEntry(entry, request, financialAccountId))
            .ToArray();
        if (postings.Select(static line => line.Line.EntryId).Distinct().Count() != postings.Length)
            throw new LedgerValidationException("Duplicate ledger lines cannot enter a certified statement control pack.");

        var rows = new List<LedgerStatementControlRow>();
        AddAccountRows(rows, postings);
        AddProfitAndLossRows(rows, postings);
        if (definition.FiscalYearCloseReceiptId is not null)
            AddFiscalCloseRows(rows, postings);

        var income = statements.PeriodIncome
            ?? throw new LedgerValidationException("Certified statement controls require period income.");
        Add(rows, "Income Statement", "total-revenue", "Total revenue", income.TotalRevenue);
        Add(rows, "Income Statement", "total-expenses", "Total expenses", income.TotalExpenses);
        Add(rows, "Income Statement", "net-income", "Net income", income.NetIncome);

        Add(rows, "Balance Sheet", "assets", "Assets", statements.TotalAssets);
        Add(rows, "Balance Sheet", "liabilities", "Liabilities", statements.TotalLiabilities);
        Add(rows, "Balance Sheet", "undistributed-income", "Undistributed net income", statements.NetIncome);
        Add(rows, "Balance Sheet", "equity", "Ending equity", statements.EndingEquity);

        var beginning = postings.Where(static posting => posting.IsOpening).ToArray();
        var activity = postings.Where(static posting => !posting.IsOpening).ToArray();
        AddControl(rows, "opening-trial-balance", "Opening debits less credits", SignedSum(beginning));
        AddControl(rows, "period-activity", "Period debits less credits", SignedSum(activity));
        AddControl(rows, "closing-trial-balance", "Closing debits less credits", SignedSum(postings));
        AddControl(rows, "accounting-equation", "Assets less liabilities and equity", statements.AccountingEquationVariance);
        AddControl(rows, "profit-and-loss", "Profit and loss allocation less period net income",
            postings.Where(static posting => posting.ProfitAndLossAccount is not null).Sum(SignedProfitAndLoss) - income.NetIncome);

        AddCashRows(rows, statements.CashFlow!);
        AddCapitalRows(rows, statements.PartnersCapital!, statements.EndingEquity);

        var failed = rows.Where(static row => row.Statement == "Agreement Controls" && row.Amount != 0m)
            .Select(static row => row.Label).ToArray();
        if (failed.Length > 0)
            throw new LedgerValidationException($"The statement control pack does not reconcile. {string.Join(", ", failed)}.");
        if (rows.Select(static row => (row.Statement, row.RowKey, row.Measure)).Distinct().Count() != rows.Count)
            throw new LedgerValidationException("Duplicate statement row identities block certification.");

        var mappings = postings.Select(static posting => new LedgerStatementControlLineMapping(
            posting.JournalEntryId, posting.Line.EntryId, posting.AccountKey, posting.ProfitAndLossAccount?.Key,
            posting.IsOpening, posting.IsClosingTransfer, posting.Line.Debit, posting.Line.Credit,
            posting.Line.ChartAccount?.NodeId ?? posting.Line.Dimensions?.ChartAccountNodeId,
            posting.Line.Dimensions?.AccountBindingRevisionId)
        {
            AccountType = posting.Line.Account.AccountType,
            IsCash = LedgerCashFlowAttribution.IsCashAccount(posting.Line.Account),
            CapitalAccountRowKey = posting.Line.Account.AccountType == LedgerAccountType.Equity
                ? CapitalRowKey(posting.Line.Account, posting.Line.Account.FinancialAccountId)
                : null
        }).ToArray();
        return new(definition, request.BaseCurrency, ReadOnlyCollectionHelpers.FreezeList(rows),
            ReadOnlyCollectionHelpers.FreezeList(mappings))
        {
            Comparison = prior is null ? null : new(request.PeriodId, prior)
        };
    }

    private static IEnumerable<Posting> ProjectEntry(JournalEntry entry, LedgerReportPackRequest request, string? financialAccountId)
    {
        var isOpening = entry.Timestamp < request.PeriodStart;
        var isClosingTransfer = !isOpening && LedgerFinancialStatementBuilder.IsClosingTransfer(
            entry, financialAccountId, request.LineDimensions);
        foreach (var line in entry.Lines.OrderBy(static line => line.EntryId))
        {
            if ((financialAccountId is { Length: > 0 }
                    && !string.Equals(line.Account.FinancialAccountId, financialAccountId.Trim(), StringComparison.OrdinalIgnoreCase))
                || !LedgerDimensionScope.Matches(line.Dimensions, request.LineDimensions))
                continue;

            if (!Enum.IsDefined(line.Account.AccountType) || string.IsNullOrWhiteSpace(line.Account.Name)
                || (line.Currency is { } currency && currency.FunctionalCurrency != request.BaseCurrency))
                throw new LedgerValidationException($"Ledger line '{line.EntryId:D}' lacks a valid account mapping or is outside the report currency.");

            var account = line.Account;
            var accountKey = Digest(new
            {
                account.Name,
                account.AccountType,
                account.Symbol,
                FinancialAccountId = account.FinancialAccountId?.ToUpperInvariant()
            });
            var pnl = !isOpening && !isClosingTransfer && account.AccountType is LedgerAccountType.Revenue or LedgerAccountType.Expense
                ? MapProfitAndLoss(entry, line)
                : null;
            yield return new(entry.JournalEntryId, line, accountKey, isOpening, isClosingTransfer, pnl);
        }
    }

    private static void AddAccountRows(List<LedgerStatementControlRow> rows, Posting[] postings)
    {
        foreach (var group in postings.GroupBy(static posting => posting.AccountKey)
                     .OrderBy(static group => group.First().Line.Account.AccountType)
                     .ThenBy(static group => group.First().Line.Account.Name, StringComparer.Ordinal)
                     .ThenBy(static group => group.Key, StringComparer.Ordinal))
        {
            var account = group.First().Line.Account;
            var open = group.Where(static posting => posting.IsOpening).ToArray();
            var activity = group.Where(static posting => !posting.IsOpening).ToArray();
            var opening = NormalBalance(account, open);
            var ending = NormalBalance(account, group);
            var label = account.ToString();
            rows.Add(new("Trial Balance", group.Key, label, "Opening", opening, Symbol: account.Symbol));
            rows.Add(new("Trial Balance", group.Key, label, "Period debit", activity.Sum(static item => item.Line.Debit), Symbol: account.Symbol));
            rows.Add(new("Trial Balance", group.Key, label, "Period credit", activity.Sum(static item => item.Line.Credit), Symbol: account.Symbol));
            rows.Add(new("Trial Balance", group.Key, label, "Closing", ending, Symbol: account.Symbol));
            if (ending != opening + NormalBalance(account, activity))
                throw new LedgerValidationException($"Trial-balance activity does not tie to account '{account.Name}'.");

            if (account.AccountType is LedgerAccountType.Asset or LedgerAccountType.Liability or LedgerAccountType.Equity)
                rows.Add(new("Balance Sheet", group.Key, label, account.AccountType.ToString(), ending, Symbol: account.Symbol));
            else
                rows.Add(new("Income Statement", group.Key, label, account.AccountType.ToString(),
                    NormalBalance(account, activity.Where(static posting => !posting.IsClosingTransfer)), Symbol: account.Symbol));
        }
    }

    private static void AddProfitAndLossRows(List<LedgerStatementControlRow> rows, Posting[] postings)
    {
        foreach (var group in postings.Where(static posting => posting.ProfitAndLossAccount is not null)
                     .GroupBy(static posting => posting.ProfitAndLossAccount!.Key)
                     .OrderBy(static group => group.First().ProfitAndLossAccount!.Component, StringComparer.Ordinal)
                     .ThenBy(static group => group.Key, StringComparer.Ordinal))
        {
            var pnl = group.First().ProfitAndLossAccount!;
            var symbol = group.Select(static posting => posting.ProfitAndLossAccount!.Symbol)
                .Where(static symbol => symbol is not null).Order(StringComparer.Ordinal).FirstOrDefault();
            rows.Add(new("Profit and Loss", group.Key, pnl.StrategyId ?? "Unassigned strategy", pnl.Component,
                group.Sum(SignedProfitAndLoss), pnl.InstrumentId, symbol));
        }
    }

    private static void AddFiscalCloseRows(List<LedgerStatementControlRow> rows, Posting[] postings)
    {
        foreach (var group in postings.GroupBy(static posting => posting.AccountKey)
                     .OrderBy(static group => group.First().Line.Account.AccountType)
                     .ThenBy(static group => group.First().Line.Account.Name, StringComparer.Ordinal)
                     .ThenBy(static group => group.Key, StringComparer.Ordinal))
        {
            var account = group.First().Line.Account;
            var label = account.ToString();
            var before = NormalBalance(account, group.Where(static posting => !posting.IsClosingTransfer));
            var transfers = NormalBalance(account, group.Where(static posting => posting.IsClosingTransfer));
            var after = NormalBalance(account, group);
            rows.Add(new("Fiscal Close", group.Key, label, "Before closing transfers", before, Symbol: account.Symbol));
            rows.Add(new("Fiscal Close", group.Key, label, "Posted closing transfers", transfers, Symbol: account.Symbol));
            rows.Add(new("Fiscal Close", group.Key, label, "After closing transfers", after, Symbol: account.Symbol));
            if (account.AccountType is LedgerAccountType.Asset or LedgerAccountType.Liability or LedgerAccountType.Equity)
                rows.Add(new("Fiscal Close", group.Key, label, "Next-year opening", after, Symbol: account.Symbol));
            if (before + transfers != after)
                throw new LedgerValidationException($"Fiscal close balances do not tie for account '{account.Name}'.");
        }
    }

    private static void AddCashRows(List<LedgerStatementControlRow> rows, LedgerCashFlowStatement cash)
    {
        foreach (var line in cash.Lines.OrderBy(static line => line.Category).ThenBy(static line => line.Description, StringComparer.Ordinal)
                     .ThenBy(static line => line.CounterpartyType))
            rows.Add(new("Cash Bridge", Digest(new { line.Category, line.Description, line.CounterpartyType }),
                line.Description, line.Category.ToString(), line.Amount));
        Add(rows, "Cash Bridge", "opening-cash", "Opening cash", cash.BeginningCash);
        Add(rows, "Cash Bridge", "operating-cash", "Operating cash flow", cash.OperatingCashFlow);
        Add(rows, "Cash Bridge", "investing-cash", "Investing cash flow", cash.InvestingCashFlow);
        Add(rows, "Cash Bridge", "financing-cash", "Financing cash flow", cash.FinancingCashFlow);
        Add(rows, "Cash Bridge", "exchange-rate-effects", "Exchange-rate effects", cash.ExchangeRateEffects);
        Add(rows, "Cash Bridge", "ending-cash", "Ending cash", cash.EndingCash);
        AddControl(rows, "cash-bridge", "Cash bridge less closing cash", cash.ReconciliationVariance);
    }

    private static void AddCapitalRows(List<LedgerStatementControlRow> rows, LedgerPartnersCapitalStatement capital, decimal endingEquity)
    {
        foreach (var account in capital.Accounts)
        {
            var key = CapitalRowKey(account.CapitalAccount, account.InvestorId);
            var label = account.CapitalAccount.ToString();
            rows.Add(new("Partners' Capital", key, label, "Opening", account.BeginningCapital));
            rows.Add(new("Partners' Capital", key, label, "Contributions", account.Contributions));
            rows.Add(new("Partners' Capital", key, label, "Distributions", -account.Distributions));
            rows.Add(new("Partners' Capital", key, label, "Allocated income and gains", account.IncomeGainAllocations));
            rows.Add(new("Partners' Capital", key, label, "Allocated expenses", -account.ExpenseAllocations));
            rows.Add(new("Partners' Capital", key, label, "Allocated fees", -account.FeeAllocations));
            rows.Add(new("Partners' Capital", key, label, "Other movements", account.OtherMovements));
            rows.Add(new("Partners' Capital", key, label, "Closing", account.EndingCapital));
            if (account.ReconciliationVariance != 0m || account.AllocationComponentsVariance != 0m)
                throw new LedgerValidationException($"The partners' capital roll-forward does not tie for '{label}'.");
        }
        Add(rows, "Partners' Capital", "ending-capital", "Total ending capital", capital.EndingCapital);
        AddControl(rows, "net-assets-to-capital", "Ending capital less balance sheet equity", capital.EndingCapital - endingEquity);
        AddControl(rows, "capital-roll-forward", "Capital roll-forward less closing capital", capital.ReconciliationVariance);
    }

    private static ProfitAndLoss MapProfitAndLoss(JournalEntry entry, LedgerEntry line)
    {
        var name = line.Account.Name;
        var component = name == LedgerAccounts.RealizedGain.Name || name == LedgerAccounts.RealizedLoss.Name
            || name == LedgerAccounts.RealizedFxGain.Name || name == LedgerAccounts.RealizedFxLoss.Name ? "Realized"
            : name == LedgerAccounts.UnrealizedGain.Name || name == LedgerAccounts.UnrealizedLoss.Name
            || name == LedgerAccounts.UnrealizedFxGain.Name || name == LedgerAccounts.UnrealizedFxLoss.Name ? "Unrealized"
            : "Other period income or expense";
        var instrumentId = line.Dimensions?.InstrumentId ?? entry.Metadata.SecurityId;
        var symbol = (line.Account.Symbol ?? entry.Metadata.Symbol)?.Trim().ToUpperInvariant();
        var strategyId = line.Dimensions?.StrategyId ?? entry.Metadata.StrategyId;
        var key = Digest(new { component, instrumentId, symbol = instrumentId is null ? symbol : null, strategyId });
        return new(key, component, instrumentId, symbol, strategyId);
    }

    private static decimal SignedProfitAndLoss(Posting posting) => posting.Line.Credit - posting.Line.Debit;

    private static decimal SignedSum(IEnumerable<Posting> postings) => postings.Sum(static posting => posting.Line.Debit - posting.Line.Credit);

    private static decimal NormalBalance(LedgerAccount account, IEnumerable<Posting> postings)
    {
        var debitNet = SignedSum(postings);
        return account.AccountType is LedgerAccountType.Asset or LedgerAccountType.Expense ? debitNet : -debitNet;
    }

    private static void Add(List<LedgerStatementControlRow> rows, string statement, string key, string label, decimal amount) =>
        rows.Add(new(statement, key, label, "Total", amount));

    private static void AddControl(List<LedgerStatementControlRow> rows, string key, string label, decimal variance) =>
        rows.Add(new("Agreement Controls", key, label, BalanceVariance, variance));

    private static string Digest<T>(T value) => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(value));

    private static string CapitalRowKey(LedgerAccount account, string? investorId)
        => Digest(new
        {
            account.Name,
            account.AccountType,
            account.Symbol,
            FinancialAccountId = account.FinancialAccountId?.ToUpperInvariant(),
            InvestorId = investorId?.ToUpperInvariant()
        });

    private sealed record Posting(Guid JournalEntryId, LedgerEntry Line, string AccountKey, bool IsOpening,
        bool IsClosingTransfer, ProfitAndLoss? ProfitAndLossAccount);

    private sealed record ProfitAndLoss(string Key, string Component, Guid? InstrumentId, string? Symbol, string? StrategyId);
}
