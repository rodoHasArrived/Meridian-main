using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Tests.Reporting;

public sealed class ReportSnapshotDiffEngineTests
{
    [Fact]
    public void Diff_ClassifiesAddedRemovedChangedAndUnchangedRows()
    {
        var prior = Render(
            "holdings",
            "Holdings",
            [Col("sector"), Col("marketValue")],
            Row("Technology", ("sector", "Technology"), ("marketValue", "100")),
            Row("Rates", ("sector", "Rates"), ("marketValue", "40")),
            Row("Cash", ("sector", "Cash"), ("marketValue", "25")));
        var current = Render(
            "holdings",
            "Holdings",
            [Col("sector"), Col("marketValue")],
            Row("Technology", ("sector", "Technology"), ("marketValue", "150")),
            Row("Rates", ("sector", "Rates"), ("marketValue", "40")),
            Row("Credit", ("sector", "Credit"), ("marketValue", "60")));

        var diff = ReportSnapshotDiffEngine.Diff(prior, current);

        diff.ChangedRowCount.Should().Be(1);
        diff.UnchangedRowCount.Should().Be(1);
        diff.AddedRowCount.Should().Be(1);
        diff.RemovedRowCount.Should().Be(1);
        diff.Warnings.Should().BeEmpty();

        var technology = diff.Rows.Single(row => row.RowKey == "Technology");
        technology.State.Should().Be(ReportWriterDiffRowStateDto.Changed);
        var technologyDelta = technology.Cells.Single(cell => cell.Column == "marketValue");
        technologyDelta.PriorValue.Should().Be("100");
        technologyDelta.CurrentValue.Should().Be("150");
        technologyDelta.Delta.Should().Be("50");
        technologyDelta.Direction.Should().Be(ReportWriterDiffDirectionDto.Up);

        diff.Rows.Single(row => row.RowKey == "Rates").State.Should().Be(ReportWriterDiffRowStateDto.Unchanged);

        var credit = diff.Rows.Single(row => row.RowKey == "Credit");
        credit.State.Should().Be(ReportWriterDiffRowStateDto.Added);
        credit.Cells.Single(cell => cell.Column == "marketValue").PriorValue.Should().BeNull();

        var cash = diff.Rows.Single(row => row.RowKey == "Cash");
        cash.State.Should().Be(ReportWriterDiffRowStateDto.Removed);
        cash.Cells.Single(cell => cell.Column == "marketValue").CurrentValue.Should().BeNull();
    }

    [Fact]
    public void Diff_OrdersCurrentRowsBeforeRemovedRows()
    {
        var prior = Render(
            "g",
            "G",
            [Col("sector"), Col("marketValue")],
            Row("Cash", ("sector", "Cash"), ("marketValue", "25")),
            Row("Technology", ("sector", "Technology"), ("marketValue", "100")));
        var current = Render(
            "g",
            "G",
            [Col("sector"), Col("marketValue")],
            Row("Technology", ("sector", "Technology"), ("marketValue", "100")));

        var diff = ReportSnapshotDiffEngine.Diff(prior, current);

        diff.Rows.Select(row => row.RowKey).Should().Equal("Technology", "Cash");
        diff.Rows[^1].State.Should().Be(ReportWriterDiffRowStateDto.Removed);
    }

    [Fact]
    public void Diff_AddsWarningForDifferingColumnSets()
    {
        var prior = Render(
            "g",
            "G",
            [Col("sector"), Col("marketValue")],
            Row("Technology", ("sector", "Technology"), ("marketValue", "100")));
        var current = Render(
            "g",
            "G",
            [Col("sector"), Col("pnl")],
            Row("Technology", ("sector", "Technology"), ("pnl", "10")));

        var diff = ReportSnapshotDiffEngine.Diff(prior, current);

        diff.Warnings.Should().Contain(warning =>
            warning.Contains("different column sets", StringComparison.OrdinalIgnoreCase));
    }

    private static ReportWriterGridColumnDto Col(string key) => new(key, key, "dimension");

    private static ReportWriterGridRowDto Row(string rowKey, params (string Key, string Value)[] values) =>
        new(rowKey, values.ToDictionary(static value => value.Key, static value => value.Value, StringComparer.OrdinalIgnoreCase));

    private static ReportWriterGridRenderDto Render(
        string gridId,
        string title,
        IReadOnlyList<ReportWriterGridColumnDto> columns,
        params ReportWriterGridRowDto[] rows) =>
        new(gridId, title, ReportWriterGridKindDto.Pivot, columns, rows, Array.Empty<string>());
}
