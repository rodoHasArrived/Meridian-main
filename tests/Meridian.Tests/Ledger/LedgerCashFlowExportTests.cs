using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Meridian.Documents;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class LedgerCashFlowExportTests
{
    [Theory]
    [InlineData(10, 0, 100)]
    [InlineData(-10, 0, -100)]
    [InlineData(10, 30, 100)]
    [InlineData(100, 0, 100)]
    public void CompoundJournal_PreservesSignedActivitiesThroughScheduledExports(
        int expense, int investment, int capital)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var cashChange = capital - expense - investment;
        var lines = new List<(LedgerAccount Account, decimal Debit, decimal Credit)>
        {
            (LedgerAccounts.ManagementFeeExpenseFor("fund-alpha"), Math.Max(expense, 0), Math.Max(-expense, 0)),
            (LedgerAccounts.InvestorCapitalFor("lp-1"), Math.Max(-capital, 0), Math.Max(capital, 0)),
        };
        if (investment != 0)
            lines.Add((LedgerAccounts.Securities("AAPL"), Math.Max(investment, 0), Math.Max(-investment, 0)));
        if (cashChange != 0)
        {
            lines.Add((LedgerAccounts.Cash, Math.Max(cashChange, 0), Math.Max(-cashChange, 0)));
        }
        else
        {
            // A contribution and an equal cash expense still have separate financing/operating
            // flows even though the batched journal's cash legs offset exactly.
            lines.Add((LedgerAccounts.Cash, capital, 0m));
            lines.Add((LedgerAccounts.Cash, 0m, expense));
        }

        var postedAt = new DateTimeOffset(2026, 2, 15, 12, 0, 0, TimeSpan.Zero);
        ledger.PostLines(postedAt, "compound cash journal", lines);
        // Noncash accruals must not inflate either operating or financing cash flow.
        ledger.PostLines(postedAt.AddDays(1), "unpaid fee accrual",
            [(LedgerAccounts.ManagementFeeExpenseFor("fund-alpha"), 7m, 0m),
             (LedgerAccounts.ManagementFeePayableFor("fund-alpha"), 0m, 7m)]);

        var export = LedgerReportPackTestData.BuildScheduledExport() with
        {
            Formats = [LedgerReportExportFormat.Csv, LedgerReportExportFormat.Json, LedgerReportExportFormat.Xlsx],
        };
        var pack = LedgerReportPackBuilder.Build(ledger, LedgerReportPackTestData.BuildRequest());
        var cashFlow = pack.Statements.CashFlow!;

        cashFlow.OperatingCashFlow.Should().Be(-expense);
        cashFlow.InvestingCashFlow.Should().Be(-investment);
        cashFlow.FinancingCashFlow.Should().Be(capital);
        cashFlow.NetCashFlow.Should().Be(cashChange);
        cashFlow.BeginningCash.Should().Be(0m);
        cashFlow.EndingCash.Should().Be(cashChange);
        cashFlow.ReconciliationVariance.Should().Be(0m);
        cashFlow.Lines.Should().Contain(line =>
            line.Category == LedgerCashFlowCategory.Operating && line.Amount == -expense);
        cashFlow.Lines.Should().Contain(line =>
            line.Category == LedgerCashFlowCategory.Financing && line.Amount == capital);

        var retainedCsv = pack.Artifacts.Single(artifact => artifact.Name == "cash-flow-statement.csv").Content;
        retainedCsv.Should().Contain($"Total,Operating cash flow,,{-expense}");
        retainedCsv.Should().Contain($"Total,Investing cash flow,,{-investment}");
        retainedCsv.Should().Contain($"Total,Financing cash flow,,{capital}");

        var artifacts = LedgerScheduledReportExportPackageBuilder.Build(
            pack, export, new FinancialReportDocumentRenderer());
        var csv = artifacts.Single(artifact => artifact.Name == "scheduled-export-financials.csv").Content;
        csv.Should().Contain($"Operating,Net operating cash flow,{Money(-expense)}");
        csv.Should().Contain($"Investing,Net investing cash flow,{Money(-investment)}");
        csv.Should().Contain($"Financing,Net financing cash flow,{Money(capital)}");

        using var json = JsonDocument.Parse(artifacts.Single(artifact => artifact.Name == "scheduled-export-financials.json").GetBytes());
        json.RootElement.GetProperty("netCashFlow").GetDecimal().Should().Be(cashChange);
        json.RootElement.GetProperty("endingCash").GetDecimal().Should().Be(cashChange);

        using var workbook = new XLWorkbook(new MemoryStream(
            artifacts.Single(artifact => artifact.Name == "scheduled-export-financials.xlsx").GetBytes()));
        var rows = workbook.Worksheet("Statement of Cash Flows").RowsUsed().ToArray();
        rows.Single(row => row.Cell(2).GetString() == "Net operating cash flow")
            .Cell(3).GetString().Should().Be(Money(-expense));
        rows.Single(row => row.Cell(2).GetString() == "Net investing cash flow")
            .Cell(3).GetString().Should().Be(Money(-investment));
        rows.Single(row => row.Cell(2).GetString() == "Net financing cash flow")
            .Cell(3).GetString().Should().Be(Money(capital));
    }

    [Fact]
    public void CompoundJournal_ScopedExportExcludesOtherFundsCounterparts()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var fundA = new LedgerLineDimensionSet(FundId: "fund-alpha");
        var fundB = new LedgerLineDimensionSet(FundId: "fund-beta");
        ledger.PostLines(new DateTimeOffset(2026, 2, 15, 12, 0, 0, TimeSpan.Zero), "batched contributions and fees",
            [(LedgerAccounts.Cash, 90m, 0m, (LedgerLineDimensionSet?)fundA),
             (LedgerAccounts.ManagementFeeExpenseFor("fund-alpha"), 10m, 0m, fundA),
             (LedgerAccounts.InvestorCapitalFor("lp-a"), 0m, 100m, fundA),
             (LedgerAccounts.Cash, 160m, 0m, fundB),
             (LedgerAccounts.ManagementFeeExpenseFor("fund-beta"), 40m, 0m, fundB),
             (LedgerAccounts.InvestorCapitalFor("lp-b"), 0m, 200m, fundB)]);

        var schedule = new LedgerReportSchedule(
            "cash-flow-scope", "fund-alpha", "Scoped cash flows", LedgerReportScheduleFrequency.Monthly,
            new DateOnly(2026, 2, 1), 5, "USD", [LedgerReportExportFormat.Csv],
            ["controller@example.com"], "controller", DateTimeOffset.Parse("2026-01-15T09:00:00Z"),
            lineDimensions: fundA);
        var export = LedgerReportSchedulePlanner.Project(schedule, 1).Single();
        var request = export.ToReportPackRequest("controller", DateTimeOffset.Parse("2026-03-01T09:00:00Z"));
        var pack = LedgerReportPackBuilder.Build(ledger, request);
        var artifacts = LedgerScheduledReportExportPackageBuilder.Build(pack, export);

        pack.Statements.CashFlow!.OperatingCashFlow.Should().Be(-10m);
        pack.Statements.CashFlow.FinancingCashFlow.Should().Be(100m);
        pack.Statements.CashFlow.EndingCash.Should().Be(90m);
        pack.Statements.CashFlow.ReconciliationVariance.Should().Be(0m);
        var csv = artifacts.Single(artifact => artifact.Name == "scheduled-export-financials.csv").Content;
        csv.Should().Contain("Operating,Net operating cash flow,-10.00");
        csv.Should().Contain("Financing,Net financing cash flow,100.00");
        csv.Should().NotContain("fund-beta");
        csv.Should().NotContain("lp-b");
    }

    private static string Money(decimal value) => value.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
