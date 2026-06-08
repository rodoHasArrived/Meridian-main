using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Tests.Reporting;

public sealed class ReportWriterGridEngineTests
{
    [Fact]
    public void RenderGrids_BuildsPivotTopNContributionAndFormulaOutputs()
    {
        var rows = new[]
        {
            Row(("security", "AAPL"), ("sector", "Technology"), ("strategy", "Core"), ("marketValue", "100"), ("pnl", "15")),
            Row(("security", "MSFT"), ("sector", "Technology"), ("strategy", "Core"), ("marketValue", "200"), ("pnl", "5")),
            Row(("security", "TLT"), ("sector", "Rates"), ("strategy", "Hedge"), ("marketValue", "50"), ("pnl", "-3")),
            Row(("security", "GLD"), ("sector", "Commodities"), ("strategy", "Hedge"), ("marketValue", "50"), ("pnl", "2"))
        };
        var grids = new[]
        {
            new ReportWriterGridDefinitionDto(
                "sector-pivot",
                "Sector Pivot",
                ReportWriterGridKindDto.Pivot,
                RowFields: ["sector"],
                Metrics:
                [
                    new ReportWriterMetricDefinitionDto("marketValue", "marketValue"),
                    new ReportWriterMetricDefinitionDto("pnl", "pnl")
                ],
                Formulas:
                [
                    new ReportWriterFormulaDefinitionDto("returnPct", "{pnl} / {marketValue} * 100")
                ],
                SortBy: "marketValue"),
            new ReportWriterGridDefinitionDto(
                "top-winners",
                "Top Winners",
                ReportWriterGridKindDto.TopN,
                RowFields: ["security"],
                Metrics: [new ReportWriterMetricDefinitionDto("pnl", "pnl")],
                TopN: 2,
                SortBy: "pnl"),
            new ReportWriterGridDefinitionDto(
                "strategy-contribution",
                "Strategy Contribution",
                ReportWriterGridKindDto.Contribution,
                RowFields: ["strategy"],
                Metrics: [new ReportWriterMetricDefinitionDto("marketValue", "marketValue")])
        };

        var rendered = ReportWriterGridEngine.RenderGrids(grids, rows);

        rendered.Should().HaveCount(3);
        var pivot = rendered.Single(grid => grid.GridId == "sector-pivot");
        pivot.Columns.Select(column => column.Key).Should().ContainInOrder("sector", "marketValue", "pnl", "returnPct");
        pivot.Rows.Should().Contain(row =>
            row.Values["sector"] == "Technology" &&
            row.Values["marketValue"] == "300" &&
            row.Values["pnl"] == "20" &&
            row.Values["returnPct"] == "6.666667");

        var top = rendered.Single(grid => grid.GridId == "top-winners");
        top.Rows.Select(row => row.Values["security"]).Should().Equal("AAPL", "MSFT");

        var contribution = rendered.Single(grid => grid.GridId == "strategy-contribution");
        contribution.Columns.Should().Contain(column => column.Key == "contributionPercent" && column.Role == "formula");
        contribution.Rows.Should().Contain(row =>
            row.Values["strategy"] == "Core" &&
            row.Values["marketValue"] == "300" &&
            row.Values["contributionPercent"] == "75");
    }

    [Fact]
    public void RenderGrids_ReportsFormulaAndNumericWarningsWithoutThrowing()
    {
        var rows = new[]
        {
            Row(("bucket", "Cash"), ("marketValue", "not-a-number"), ("pnl", "1")),
            Row(("bucket", "Equity"), ("marketValue", "0"), ("pnl", "2"))
        };
        var grids = new[]
        {
            new ReportWriterGridDefinitionDto(
                "bad-formula",
                "Bad Formula",
                ReportWriterGridKindDto.Pivot,
                RowFields: ["bucket"],
                Metrics:
                [
                    new ReportWriterMetricDefinitionDto("marketValue", "marketValue"),
                    new ReportWriterMetricDefinitionDto("pnl", "pnl")
                ],
                Formulas: [new ReportWriterFormulaDefinitionDto("returnPct", "{pnl} / {marketValue}")])
        };

        var rendered = ReportWriterGridEngine.RenderGrids(grids, rows).Single();

        rendered.Rows.Should().HaveCount(2);
        rendered.Warnings.Should().Contain("Field 'marketValue' contained non-numeric values; affected cells were skipped.");
        rendered.Warnings.Should().Contain(warning => warning.StartsWith("Formula 'returnPct' could not be evaluated:", StringComparison.Ordinal));
        rendered.Rows.Should().Contain(row => row.Values["bucket"] == "Equity" && row.Values["returnPct"] == string.Empty);
    }

    private static IReadOnlyDictionary<string, string> Row(params (string Key, string Value)[] values) =>
        values.ToDictionary(static value => value.Key, static value => value.Value, StringComparer.OrdinalIgnoreCase);
}
