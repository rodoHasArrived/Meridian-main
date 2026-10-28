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
    [Fact]
    public void AmbiguousCompoundJournal_CannotProduceSignedReportPack()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(DateTimeOffset.Parse("2026-02-15T12:00:00Z"), "transfer and accrual",
            [(LedgerAccounts.Cash, 100m, 0m),
             (new LedgerAccount("Cash reserve", LedgerAccountType.Asset), 0m, 100m),
             (LedgerAccounts.ManagementFeeExpenseFor("fund-alpha"), 7m, 0m),
             (LedgerAccounts.ManagementFeePayableFor("fund-alpha"), 0m, 7m)]);

        var build = () => LedgerReportPackBuilder.Build(ledger, LedgerReportPackTestData.BuildRequest());

        build.Should().Throw<LedgerValidationException>().WithMessage("*cash-flow attribution*");
    }

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
        LedgerCashFlowTestData.Post(ledger, postedAt,
            lines.Select(line => (line.Account, line.Debit, line.Credit, (LedgerLineDimensionSet?)null)).ToArray(),
            lines.Select(_ => "reviewed net settlement").ToArray());
        // Noncash accruals must not inflate either operating or financing cash flow.
        ledger.PostLines(postedAt.AddDays(1), "unpaid fee accrual",
            [(LedgerAccounts.ManagementFeeExpenseFor("fund-alpha"), 7m, 0m),
             (LedgerAccounts.ManagementFeePayableFor("fund-alpha"), 0m, 7m)]);

        AssertExports(ledger, expense, investment, capital);
    }

    [Fact]
    public void CompoundJournal_ScopedExportExcludesOtherFundsCounterparts()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var fundA = new LedgerLineDimensionSet(FundId: "fund-alpha");
        var fundB = new LedgerLineDimensionSet(FundId: "fund-beta");
        LedgerCashFlowTestData.Post(ledger, new DateTimeOffset(2026, 2, 15, 12, 0, 0, TimeSpan.Zero),
            [(LedgerAccounts.Cash, 90m, 0m, (LedgerLineDimensionSet?)fundA),
             (LedgerAccounts.ManagementFeeExpenseFor("fund-alpha"), 10m, 0m, fundA),
             (LedgerAccounts.InvestorCapitalFor("lp-a"), 0m, 100m, fundA),
             (LedgerAccounts.Cash, 160m, 0m, fundB),
             (LedgerAccounts.ManagementFeeExpenseFor("fund-beta"), 40m, 0m, fundB),
             (LedgerAccounts.InvestorCapitalFor("lp-b"), 0m, 200m, fundB)],
            "fund-a", "fund-a", "fund-a", "fund-b", "fund-b", "fund-b");

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

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(10, 30, 100)]
    [InlineData(20, 80, 100)]
    [InlineData(-10, -30, -100)]
    public void MixedCompoundJournal_ExportsOnlyAttributedCashActivity(int expense, int investment, int capital)
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(DateTimeOffset.Parse("2025-12-31T00:00:00Z"), "opening contribution",
            [(LedgerAccounts.Cash, 500m, 0m), (LedgerAccounts.RetainedEarnings, 0m, 500m)]);
        var lines = new List<(LedgerAccount Account, decimal Debit, decimal Credit, LedgerLineDimensionSet? Dimensions)>();
        var groups = new List<string>();
        void AddActivity(LedgerAccount debitAccount, LedgerAccount creditAccount, int amount, string group)
        {
            if (amount == 0)
                return;
            lines.Add((debitAccount, Math.Max(amount, 0), Math.Max(-amount, 0), null));
            lines.Add((creditAccount, Math.Max(-amount, 0), Math.Max(amount, 0), null));
            groups.AddRange([group, group]);
        }
        AddActivity(LedgerAccounts.Cash, new LedgerAccount("Cash reserve", LedgerAccountType.Asset), 50, "transfer");
        AddActivity(LedgerAccounts.ManagementFeeExpenseFor("fund-alpha"), LedgerAccounts.ManagementFeePayableFor("fund-alpha"), 7, "accrual");
        AddActivity(new LedgerAccount("Noncash asset", LedgerAccountType.Asset), LedgerAccounts.ManagementFeePayableFor("fund-alpha"), 11, "reclassification");
        AddActivity(LedgerAccounts.Cash, LedgerAccounts.InvestorCapitalFor("lp-1"), capital, "contribution");
        AddActivity(LedgerAccounts.ManagementFeeExpenseFor("fund-alpha"), LedgerAccounts.Cash, expense, "expense payment");
        AddActivity(LedgerAccounts.Securities("AAPL"), LedgerAccounts.Cash, investment, "investment payment");
        LedgerCashFlowTestData.Post(ledger, DateTimeOffset.Parse("2026-02-15T12:00:00Z"), lines, groups.ToArray());

        AssertExports(ledger, expense, investment, capital, beginning: 500m);
    }

    private static void AssertExports(Meridian.Ledger.Ledger ledger, int expense, int investment, int capital,
        decimal beginning = 0m)
    {
        var cashChange = capital - expense - investment;
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
        cashFlow.BeginningCash.Should().Be(beginning);
        cashFlow.EndingCash.Should().Be(beginning + cashChange);
        cashFlow.ReconciliationVariance.Should().Be(0m);
        cashFlow.Lines.Sum(line => line.Amount).Should().Be(cashChange);
        if (expense != 0)
            cashFlow.Lines.Should().Contain(line => line.Category == LedgerCashFlowCategory.Operating && line.Amount == -expense);
        if (investment != 0)
            cashFlow.Lines.Should().Contain(line => line.Category == LedgerCashFlowCategory.Investing && line.Amount == -investment);
        if (capital != 0)
            cashFlow.Lines.Should().Contain(line => line.Category == LedgerCashFlowCategory.Financing && line.Amount == capital);
        cashFlow.Lines.Should().NotContain(line => line.Description == "Noncash asset"
            || line.CounterpartyType == LedgerAccountType.Liability);

        var retainedCsv = pack.Artifacts.Single(artifact => artifact.Name == "cash-flow-statement.csv").Content;
        retainedCsv.Should().Contain($"Total,Beginning cash,,{beginning}");
        retainedCsv.Should().Contain($"Total,Ending cash,,{beginning + cashChange}");
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
        json.RootElement.GetProperty("endingCash").GetDecimal().Should().Be(beginning + cashChange);

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

    private static string Money(decimal value) => value.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
