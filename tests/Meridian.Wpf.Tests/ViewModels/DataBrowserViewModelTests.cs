using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class DataBrowserViewModelTests
{
    [Fact]
    public void SearchMiss_ShowsRecoveryStateAndResetRestoresRows()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = new DataBrowserViewModel();

            viewModel.RefreshResults();
            viewModel.PagedRecords.Should().NotBeEmpty();
            viewModel.HasRows.Should().BeTrue();
            viewModel.HasActiveFilters.Should().BeFalse();
            viewModel.ResetFiltersCommand.CanExecute(null).Should().BeFalse();

            viewModel.SymbolFilter = "NOT_A_SYMBOL";

            viewModel.PagedRecords.Should().BeEmpty();
            viewModel.FilteredCountText.Should().Be("0 records");
            viewModel.HasRows.Should().BeFalse();
            viewModel.HasActiveFilters.Should().BeTrue();
            viewModel.HasFilterRecoveryAction.Should().BeTrue();
            viewModel.EmptyStateTitle.Should().Be("No records match the current filters");
            viewModel.EmptyStateDetail.Should().Contain("Reset filters");
            viewModel.ResetFiltersCommand.CanExecute(null).Should().BeTrue();

            viewModel.ResetFiltersCommand.Execute(null);

            viewModel.SymbolFilter.Should().BeEmpty();
            viewModel.SelectedDataType.Should().Be("All");
            viewModel.SelectedVenue.Should().Be("All");
            viewModel.PagedRecords.Should().NotBeEmpty();
            viewModel.HasRows.Should().BeTrue();
            viewModel.HasActiveFilters.Should().BeFalse();
            viewModel.HasFilterRecoveryAction.Should().BeFalse();
            viewModel.ResetFiltersCommand.CanExecute(null).Should().BeFalse();
        });
    }

    [Fact]
    public void FilterInputs_RefreshResultsAndUpdateActiveFilterCount()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = new DataBrowserViewModel();

            viewModel.RefreshResults();
            viewModel.PagedRecords.Should().NotBeEmpty();

            viewModel.SelectedDataType = "Trades";

            viewModel.ActiveFilterCount.Should().Be(1);
            viewModel.PagedRecords.Should().NotBeEmpty();
            viewModel.PagedRecords.Should().OnlyContain(record => record.DataType == "Trades");

            var retainedSymbol = viewModel.PagedRecords[0].Symbol;
            viewModel.SymbolFilter = retainedSymbol;

            viewModel.ActiveFilterCount.Should().Be(2);
            viewModel.PagedRecords.Should().OnlyContain(record =>
                record.DataType == "Trades" &&
                record.Symbol.Contains(retainedSymbol, StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void TimePeriodSelection_AppliesDateRangeAndResetRestoresAllTime()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = new DataBrowserViewModel();
            var expectedFrom = DateTime.Today.AddDays(-1);

            viewModel.RefreshResults();
            viewModel.SelectedTimePeriodKey.Should().Be(DataBrowserTimePeriodOption.AllTimeKey);
            viewModel.TimePeriodScopeText.Should().Be("All retained records");
            viewModel.ActiveFilterCount.Should().Be(0);
            viewModel.PagedRecords.Should().NotBeEmpty();

            viewModel.SelectedTimePeriodKey = "1D";

            viewModel.FromDate.Should().Be(expectedFrom);
            viewModel.ToDate.Should().BeNull();
            viewModel.SelectedTimePeriodKey.Should().Be("1D");
            viewModel.TimePeriodScopeText.Should().Be("1 Day");
            viewModel.ActiveFilterCount.Should().Be(1);
            viewModel.PagedRecords.Should().OnlyContain(record => record.Timestamp >= expectedFrom);
            viewModel.ResetFiltersCommand.CanExecute(null).Should().BeTrue();

            viewModel.ResetFiltersCommand.Execute(null);

            viewModel.SelectedTimePeriodKey.Should().Be(DataBrowserTimePeriodOption.AllTimeKey);
            viewModel.FromDate.Should().BeNull();
            viewModel.ToDate.Should().BeNull();
            viewModel.TimePeriodScopeText.Should().Be("All retained records");
            viewModel.ActiveFilterCount.Should().Be(0);
        });
    }

    [Fact]
    public void ManualDateRange_MarksTimePeriodAsCustom()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = new DataBrowserViewModel();
            var fromDate = DateTime.Today.AddDays(-10);
            var toDate = DateTime.Today.AddDays(-2);

            viewModel.FromDate = fromDate;
            viewModel.ToDate = toDate;

            viewModel.SelectedTimePeriodKey.Should().Be(DataBrowserTimePeriodOption.CustomKey);
            viewModel.TimePeriodScopeText.Should().Be($"{fromDate:MMM d} to {toDate:MMM d}");
            viewModel.ActiveFilterCount.Should().Be(1);
        });
    }

    [Fact]
    public void SelectedRecord_ProjectsDetailStateAndCopiesJsonPayload()
    {
        WpfTestThread.Run(() =>
        {
            string? copiedJson = null;
            var viewModel = new DataBrowserViewModel(json => copiedJson = json);

            viewModel.RefreshResults();
            viewModel.CopySelectedRecordJsonCommand.CanExecute(null).Should().BeFalse();

            var selected = viewModel.PagedRecords[0];
            viewModel.SelectedRecord = selected;

            viewModel.HasSelectedRecord.Should().BeTrue();
            viewModel.CanCopySelectedRecord.Should().BeTrue();
            viewModel.CopySelectedRecordJsonCommand.CanExecute(null).Should().BeTrue();
            viewModel.SelectedRecordSymbol.Should().Be(selected.Symbol);
            viewModel.SelectedRecordVenue.Should().Be(selected.Venue);
            viewModel.SelectedRecordDataType.Should().Be(selected.DataType);
            viewModel.SelectedRecordTimestampText.Should().Be(selected.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            viewModel.SelectedRecordPriceText.Should().Be(selected.Price.ToString("N2"));
            viewModel.SelectedRecordSizeText.Should().Be(selected.Size.ToString("N0"));
            viewModel.SelectedRecordAutomationName.Should().Contain(selected.Symbol);

            var json = viewModel.BuildSelectedRecordJson();
            json.Should().Contain($"\"Symbol\": \"{selected.Symbol}\"");
            json.Should().Contain($"\"Timestamp\": \"{selected.Timestamp:O}\"");

            viewModel.CopySelectedRecordJsonCommand.Execute(null);

            copiedJson.Should().Be(json);
        });
    }

    [Fact]
    public void RefreshResults_WhenSelectedRecordLeavesPage_ClearsDetailPanelState()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = new DataBrowserViewModel(_ => { });

            viewModel.RefreshResults();
            viewModel.SelectedRecord = viewModel.PagedRecords[0];
            viewModel.HasSelectedRecord.Should().BeTrue();

            viewModel.SymbolFilter = "NOT_A_SYMBOL";

            viewModel.PagedRecords.Should().BeEmpty();
            viewModel.SelectedRecord.Should().BeNull();
            viewModel.HasSelectedRecord.Should().BeFalse();
            viewModel.CanCopySelectedRecord.Should().BeFalse();
            viewModel.CopySelectedRecordJsonCommand.CanExecute(null).Should().BeFalse();
            viewModel.SelectedRecordSymbol.Should().Be("--");
        });
    }

    [Fact]
    public void DataBrowserPageSource_BindsRecoveryStateAndResetCommand()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\DataBrowserPage.xaml"));
        var codeBehind = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\DataBrowserPage.xaml.cs"));

        xaml.Should().Contain("DataBrowserSearchBox");
        xaml.Should().Contain("DataBrowserDataTypeCombo");
        xaml.Should().Contain("DataBrowserVenueCombo");
        xaml.Should().Contain("DataBrowserTimePeriodCombo");
        xaml.Should().Contain("ItemsSource=\"{Binding TimePeriods}\"");
        xaml.Should().Contain("SelectedValue=\"{Binding SelectedTimePeriodKey");
        xaml.Should().Contain("{Binding FilteredCountText, Mode=OneWay}");
        xaml.Should().Contain("{Binding TimePeriodScopeText, Mode=OneWay}");
        xaml.Should().Contain("DataBrowserEmptyStatePanel");
        xaml.Should().Contain("DataBrowserResetFiltersButton");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedRecord, Mode=TwoWay}\"");
        xaml.Should().Contain("DataBrowserDetailPanel");
        xaml.Should().Contain("{Binding HasSelectedRecord, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding SelectedRecordSymbol}");
        xaml.Should().Contain("{Binding SelectedRecordTimestampText}");
        xaml.Should().Contain("{Binding CopySelectedRecordJsonCommand}");
        xaml.Should().Contain("DataBrowserCopySelectedRecordJsonButton");
        xaml.Should().Contain("{Binding HasRows, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding EmptyStateTitle}");
        xaml.Should().Contain("{Binding EmptyStateDetail}");
        xaml.Should().Contain("{Binding ResetFiltersCommand}");
        xaml.Should().Contain("{Binding HasFilterRecoveryAction");
        xaml.Should().NotContain("Click=\"ResetFilters_Click\"");
        xaml.Should().NotContain("TimePeriodCombo_SelectionChanged");
        xaml.Should().NotContain("SelectionChanged=\"ResultsGrid_SelectionChanged\"");
        xaml.Should().NotContain("Click=\"CopyJson_Click\"");
        codeBehind.Should().Contain("SelectedTimePeriodKey");
        codeBehind.Should().NotContain("ResetFilters_Click");
        codeBehind.Should().NotContain("DetailPanel.Visibility");
        codeBehind.Should().NotContain("DetailSymbol.Text");
        codeBehind.Should().NotContain("JsonSerializer.Serialize");
    }

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
