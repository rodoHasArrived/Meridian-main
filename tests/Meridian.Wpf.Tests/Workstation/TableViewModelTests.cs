using System.Collections.Specialized;
using Meridian.Wpf.Workstation.ViewModels.Base;

namespace Meridian.Wpf.Tests.Workstation;

public sealed class TableViewModelTests
{
    [Fact]
    public void ReplaceRows_ShouldPublishSingleResetForBatch()
    {
        var table = new TestTableViewModel();
        var events = new List<NotifyCollectionChangedEventArgs>();
        table.Rows.CollectionChanged += (_, args) => events.Add(args);

        table.ReplaceRows(Enumerable.Range(1, 250).Select(index => new RowFixture(index, $"Row {index}")));

        table.HasRows.Should().BeTrue();
        table.Rows.Should().HaveCount(250);
        events.Should().ContainSingle()
            .Which.Action.Should().Be(NotifyCollectionChangedAction.Reset);
    }

    [Fact]
    public void FilterableReplaceRows_ShouldBatchSourceAndFilteredUpdates()
    {
        var table = new FilterableTableViewModel<RowFixture>
        {
            MatchFilter = static (row, query) => row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        };
        var events = new List<NotifyCollectionChangedEventArgs>();
        table.Rows.CollectionChanged += (_, args) => events.Add(args);

        table.ReplaceRows([
            new(1, "Cash"),
            new(2, "Accrual"),
            new(3, "Equity")
        ]);
        table.FilterQuery = "a";

        table.Rows.Select(row => row.Name).Should().ContainInOrder("Cash", "Accrual");
        events.Should().HaveCount(2);
        events.Should().OnlyContain(args => args.Action == NotifyCollectionChangedAction.Reset);
    }

    private sealed class TestTableViewModel : TableViewModel<RowFixture>
    {
    }

    private sealed record RowFixture(int Id, string Name);
}
