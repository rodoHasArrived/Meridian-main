using System.Globalization;
using ClosedXML.Excel;
using FluentAssertions;
using Meridian.Documents;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class LedgerCashFlowExportTests
{
    private static readonly DateTimeOffset PostedAt = new(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void CompoundContribution_ExportsSignedCashFlowsAndReversals(bool reverse, bool reverseLineOrder)
    {
        var ledger = new Meridian.Ledger.Ledger();
        (LedgerAccount Account, decimal Debit, decimal Credit)[] lines =
        [
            (LedgerAccounts.Cash, 90m, 0m),
            (LedgerAccounts.CommissionExpense, 10m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 100m),
        ];
        ledger.PostLines(reverse ? PostedAt.AddMonths(-1) : PostedAt, "contribution net of expense",
            reverseLineOrder ? lines.Reverse().ToArray() : lines);
        if (reverse)
            ledger.Post(LedgerJournalReversal.Reverse(ledger.Journal.Single(), Guid.NewGuid(), PostedAt));

        var (pack, export) = BuildPack(ledger);
        var sign = reverse ? -1m : 1m;

        AssertCashFlow(pack, -10m * sign, 0m, 100m * sign, reverse ? 90m : 0m, reverse ? 0m : 90m);
        var artifacts = LedgerScheduledReportExportPackageBuilder.Build(pack, export, new FinancialReportDocumentRenderer());
        var csv = artifacts.Single(artifact => artifact.Name == "scheduled-export-financials.csv").Content;
        csv.Split('\n', StringSplitOptions.TrimEntries).Should().Contain(
        [
            $"Operating,Net operating cash flow,{Money(-10m * sign)}",
            $"Financing,Net financing cash flow,{Money(100m * sign)}",
            $",Net change in cash,{Money(90m * sign)}",
        ]);

        var xlsx = artifacts.Single(artifact => artifact.Name == "scheduled-export-financials.xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(xlsx.GetBytes()));
        var sheet = workbook.Worksheet("Statement of Cash Flows");
        var rows = sheet.RowsUsed().Skip(1).ToDictionary(row => row.Cell(2).GetString(), row => row.Cell(3).GetString());
        rows["Commission Expense"].Should().Be(Money(-10m * sign));
        rows["Capital Account"].Should().Be(Money(100m * sign));
        rows["Net operating cash flow"].Should().Be(Money(-10m * sign));
        rows["Net financing cash flow"].Should().Be(Money(100m * sign));
        rows["Net change in cash"].Should().Be(Money(90m * sign));
    }

    [Fact]
    public void CompoundJournal_PreservesExactPostedAmountsAcrossAllCategories()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(PostedAt, "contribution funding securities and fees",
        [
            (LedgerAccounts.Cash, 69.997m, 0m),
            (LedgerAccounts.CommissionExpense, 10.001m, 0m),
            (LedgerAccounts.Securities("AAPL"), 20.002m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 100m),
        ]);

        var (pack, _) = BuildPack(ledger);

        AssertCashFlow(pack, -10.001m, -20.002m, 100m, 0m, 69.997m);
    }

    [Fact]
    public void OffsettingCashLegs_RetainGrossOperatingAndFinancingFlows()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(PostedAt, "contribution and expense paid in one journal",
        [
            (LedgerAccounts.Cash, 100m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 100m),
            (LedgerAccounts.CommissionExpense, 100m, 0m),
            (LedgerAccounts.Cash, 0m, 100m),
        ]);

        var (pack, _) = BuildPack(ledger);

        AssertCashFlow(pack, -100m, 0m, 100m, 0m, 0m);
    }

    [Fact]
    public void CashTransfersAndNonCashJournals_AddNoClassifiedFlow()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(PostedAt.AddMonths(-1), "opening balance",
            [(LedgerAccounts.Cash, 100m, 0m), (LedgerAccounts.CapitalAccount, 0m, 100m)]);
        ledger.PostLines(PostedAt, "cash transfer",
            [(LedgerAccounts.CashAccount("reserve"), 40m, 0m), (LedgerAccounts.Cash, 0m, 40m)]);
        ledger.PostLines(PostedAt, "non-cash expense funded by capital",
            [(LedgerAccounts.CommissionExpense, 10m, 0m), (LedgerAccounts.CapitalAccount, 0m, 10m)]);

        var (pack, _) = BuildPack(ledger);

        AssertCashFlow(pack, 0m, 0m, 0m, 100m, 100m);
        pack.Statements.CashFlow!.Lines.Should().BeEmpty();
    }

    [Fact]
    public void CompoundJournal_ScopesCashAndCounterpartsToRequestedDimensions()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var selected = new LedgerLineDimensionSet(FundId: "fund-a");
        var other = new LedgerLineDimensionSet(FundId: "fund-b");
        ledger.PostLines(PostedAt, "batched contributions net of expenses",
        [
            (LedgerAccounts.Cash, 40m, 0m, (LedgerLineDimensionSet?)selected),
            (LedgerAccounts.CashAccount("reserve"), 50m, 0m, selected),
            (LedgerAccounts.CommissionExpense, 10m, 0m, selected),
            (LedgerAccounts.CapitalAccount, 0m, 100m, selected),
            (LedgerAccounts.Cash, 900m, 0m, other),
            (LedgerAccounts.CommissionExpense, 100m, 0m, other),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m, other),
        ]);

        var (pack, _) = BuildPack(ledger, lineDimensions: selected);

        AssertCashFlow(pack, -10m, 0m, 100m, 0m, 90m);
        pack.Statements.CashFlow!.Lines.Should().HaveCount(2);
    }

    [Fact]
    public void CompoundJournal_ScopesOutUnselectedAndUnscopedFinancialAccounts()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(PostedAt, "batched account contributions",
        [
            (LedgerAccounts.CashAccount("selected"), 90m, 0m),
            (LedgerAccounts.CommissionExpenseFor("selected"), 10m, 0m),
            (LedgerAccounts.CapitalAccountFor("selected"), 0m, 100m),
            (LedgerAccounts.CashAccount("other"), 900m, 0m),
            (LedgerAccounts.CommissionExpenseFor("other"), 100m, 0m),
            (LedgerAccounts.CapitalAccountFor("other"), 0m, 1_000m),
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m),
        ]);

        var (pack, _) = BuildPack(ledger, financialAccountId: "selected");

        AssertCashFlow(pack, -10m, 0m, 100m, 0m, 90m);
    }

    [Fact]
    public void IncompleteFinancialAccountScope_RetainsVarianceInsteadOfReweightingExpense()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(PostedAt, "contribution with unscoped capital counterpart",
        [
            (LedgerAccounts.CashAccount("selected"), 90m, 0m),
            (LedgerAccounts.CommissionExpenseFor("selected"), 10m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 100m),
        ]);

        var (pack, _) = BuildPack(ledger, financialAccountId: "selected");

        AssertCashFlow(pack, -10m, 0m, 0m, 0m, 90m, reconciled: false);
        pack.Statements.CashFlow!.ReconciliationVariance.Should().Be(-100m);
    }

    private static (LedgerFinancialReportPack Pack, LedgerReportScheduledExport Export) BuildPack(
        Meridian.Ledger.Ledger ledger,
        string? financialAccountId = null,
        LedgerLineDimensionSet? lineDimensions = null)
    {
        var schedule = new LedgerReportSchedule(
            "cash-flow-report", "fund-a", "Cash-flow report", LedgerReportScheduleFrequency.Monthly,
            new DateOnly(2026, 5, 1), 5, "USD",
            [LedgerReportExportFormat.Csv, LedgerReportExportFormat.Xlsx],
            ["controller@example.com"], "controller", PostedAt.AddMonths(-1), lineDimensions: lineDimensions);
        var export = LedgerReportSchedulePlanner.Project(schedule, occurrenceCount: 1).Single();
        var request = export.ToReportPackRequest("controller", PostedAt.AddMonths(1));
        return (LedgerReportPackBuilder.Build(ledger, request, financialAccountId: financialAccountId), export);
    }

    private static void AssertCashFlow(
        LedgerFinancialReportPack pack,
        decimal operating,
        decimal investing,
        decimal financing,
        decimal beginningCash,
        decimal endingCash,
        bool reconciled = true)
    {
        var cashFlow = pack.Statements.CashFlow!;
        cashFlow.OperatingCashFlow.Should().Be(operating);
        cashFlow.InvestingCashFlow.Should().Be(investing);
        cashFlow.FinancingCashFlow.Should().Be(financing);
        cashFlow.NetCashFlow.Should().Be(operating + investing + financing);
        cashFlow.BeginningCash.Should().Be(beginningCash);
        cashFlow.EndingCash.Should().Be(endingCash);
        cashFlow.IsReconciled.Should().Be(reconciled);
        var csv = pack.Artifacts.Single(artifact => artifact.Name == "cash-flow-statement.csv").Content;
        csv.Split('\n', StringSplitOptions.TrimEntries).Should().Contain(
        [
            $"Total,Operating cash flow,,{Exact(operating)}",
            $"Total,Investing cash flow,,{Exact(investing)}",
            $"Total,Financing cash flow,,{Exact(financing)}",
            $"Total,Net change in cash,,{Exact(operating + investing + financing)}",
            $"Total,Beginning cash,,{Exact(beginningCash)}",
            $"Total,Ending cash,,{Exact(endingCash)}",
        ]);
    }

    private static string Exact(decimal amount) => amount.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string Money(decimal amount) => amount.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
