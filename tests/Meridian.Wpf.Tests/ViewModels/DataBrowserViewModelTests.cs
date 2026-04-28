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
        xaml.Should().Contain("TimePeriodScopeText");
        xaml.Should().Contain("DataBrowserEmptyStatePanel");
        xaml.Should().Contain("DataBrowserResetFiltersButton");
        xaml.Should().Contain("{Binding HasRows, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding EmptyStateTitle}");
        xaml.Should().Contain("{Binding EmptyStateDetail}");
        xaml.Should().Contain("{Binding ResetFiltersCommand}");
        xaml.Should().Contain("{Binding HasFilterRecoveryAction");
        xaml.Should().NotContain("Click=\"ResetFilters_Click\"");
        xaml.Should().NotContain("TimePeriodCombo_SelectionChanged");
        codeBehind.Should().Contain("SelectedTimePeriodKey");
        codeBehind.Should().NotContain("ResetFilters_Click");
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
