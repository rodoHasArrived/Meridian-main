using Meridian.Ui.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class SymbolStorageViewModelTests
{
    [Fact]
    public async Task LoadAsync_WhenAnalyticsLoadSucceeds_ProjectsMetricsSymbolsAndProjection()
    {
        var viewModel = CreateSubject((forceRefresh, _) =>
        {
            forceRefresh.Should().BeFalse();
            return Task.FromResult(new StorageAnalytics
            {
                TotalSizeBytes = 3_145_728,
                TotalFileCount = 3,
                DailyGrowthBytes = 1_048_576,
                ProjectedDaysUntilFull = 30,
                TradeSizeBytes = 1_048_576,
                TradeFileCount = 1,
                DepthSizeBytes = 2_097_152,
                DepthFileCount = 2,
                HistoricalSizeBytes = 0,
                HistoricalFileCount = 0,
                SymbolBreakdown =
                [
                    new SymbolAnalyticsInfo
                    {
                        Symbol = "SPY",
                        SizeBytes = 2_097_152,
                        FileCount = 2,
                        PercentOfTotal = 66.7,
                        OldestData = new DateTime(2026, 5, 1),
                        NewestData = new DateTime(2026, 5, 20),
                    },
                ],
            });
        });

        await viewModel.LoadAsync();

        viewModel.IsLoading.Should().BeFalse();
        viewModel.CanRefresh.Should().BeTrue();
        viewModel.TotalSizeText.Should().Be("3.0 MB");
        viewModel.TotalFilesText.Should().Be("3");
        viewModel.SymbolCountText.Should().Be("1");
        viewModel.DailyGrowthText.Should().Be("1.0 MB/d");
        viewModel.TradeSizeText.Should().Be("1.0 MB");
        viewModel.TradeFilesText.Should().Be("1 file");
        viewModel.DepthSizeText.Should().Be("2.0 MB");
        viewModel.DepthFilesText.Should().Be("2 files");
        viewModel.HistoricalSizeText.Should().Be("0.0 B");
        viewModel.HistoricalFilesText.Should().Be("0 files");
        viewModel.Symbols.Should().ContainSingle();
        viewModel.Symbols[0].Symbol.Should().Be("SPY");
        viewModel.Symbols[0].SizeText.Should().Be("2.0 MB");
        viewModel.Symbols[0].FileCountText.Should().Be("2 files");
        viewModel.Symbols[0].DateRangeText.Should().Be("May 01 - May 20");
        viewModel.HasSymbols.Should().BeTrue();
        viewModel.IsSymbolListEmpty.Should().BeFalse();
        viewModel.HasProjection.Should().BeTrue();
        viewModel.ProjectionText.Should().Contain("30 days");
        viewModel.StatusText.Should().Be("Analytics loaded: 3 files across 1 symbol.");
    }

    [Fact]
    public async Task LoadAsync_WhenNoSymbolsExist_ProjectsEmptyState()
    {
        var viewModel = CreateSubject((_, _) => Task.FromResult(new StorageAnalytics()));

        await viewModel.LoadAsync();

        viewModel.HasSymbols.Should().BeFalse();
        viewModel.IsSymbolListEmpty.Should().BeTrue();
        viewModel.HasProjection.Should().BeFalse();
        viewModel.StatusText.Should().Be(
            "Analytics loaded: no retained symbol files were found under the configured DataRoot.");
    }

    [Fact]
    public async Task RefreshCommand_WhenRunning_DisablesRefreshAndUsesForceRefresh()
    {
        var forceRefreshRequested = false;
        var analyticsGate = new TaskCompletionSource<StorageAnalytics>();
        var viewModel = CreateSubject((forceRefresh, _) =>
        {
            forceRefreshRequested = forceRefresh;
            return analyticsGate.Task;
        });

        var refreshTask = viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.IsLoading.Should().BeTrue();
        viewModel.CanRefresh.Should().BeFalse();
        viewModel.RefreshCommand.CanExecute(null).Should().BeFalse();
        viewModel.StatusText.Should().Be("Refreshing symbol storage analytics...");

        analyticsGate.SetResult(new StorageAnalytics { TotalFileCount = 1 });
        await refreshTask;

        forceRefreshRequested.Should().BeTrue();
        viewModel.IsLoading.Should().BeFalse();
        viewModel.CanRefresh.Should().BeTrue();
        viewModel.RefreshCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_WhenAnalyticsFail_ProjectsErrorRecovery()
    {
        var viewModel = CreateSubject((_, _) => Task.FromException<StorageAnalytics>(new IOException("denied")));

        await viewModel.LoadAsync();

        viewModel.IsLoading.Should().BeFalse();
        viewModel.CanRefresh.Should().BeTrue();
        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorText.Should().Contain("denied");
        viewModel.IsSymbolListEmpty.Should().BeFalse();
        viewModel.HasProjection.Should().BeFalse();
        viewModel.StatusText.Should().Contain("Verify the configured DataRoot");
    }

    [Theory]
    [InlineData(0, 0, "")]
    [InlineData(1_048_576, 400, "more than a year")]
    [InlineData(1_048_576, 12, "12 days")]
    public void BuildProjectionText_ProjectsOnlyActionableCapacityGuidance(
        long dailyGrowthBytes,
        int projectedDaysUntilFull,
        string expectedText)
    {
        var projection = SymbolStorageViewModel.BuildProjectionText(new StorageAnalytics
        {
            DailyGrowthBytes = dailyGrowthBytes,
            ProjectedDaysUntilFull = projectedDaysUntilFull,
        });

        if (expectedText.Length == 0)
        {
            projection.Should().BeEmpty();
        }
        else
        {
            projection.Should().Contain(expectedText);
        }
    }

    [Fact]
    public void SymbolStoragePageSource_BindsAnalyticsStateThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SymbolStoragePage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SymbolStoragePage.xaml.cs"));

        xaml.Should().Contain("Command=\"{Binding RefreshCommand}\"");
        xaml.Should().Contain("IsEnabled=\"{Binding CanRefresh}\"");
        xaml.Should().Contain("{Binding TotalSizeText}");
        xaml.Should().Contain("{Binding TradeFilesText}");
        xaml.Should().Contain("ItemsSource=\"{Binding Symbols}\"");
        xaml.Should().Contain("{Binding HasSymbols, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("SymbolStorageStatusPanel");
        xaml.Should().Contain("SymbolStorageErrorPanel");
        xaml.Should().Contain("{Binding IsLoading, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding HasProjection, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().NotContain("Click=\"Refresh_Click\"");

        codeBehind.Should().Contain("new SymbolStorageViewModel()");
        codeBehind.Should().Contain("_viewModel.LoadAsync");
        codeBehind.Should().NotContain("StorageAnalyticsService.Instance");
        codeBehind.Should().NotContain("SymbolList.ItemsSource");
        codeBehind.Should().NotContain("TotalSizeText.Text");
        codeBehind.Should().NotContain("LoadingPanel.Visibility");
    }

    private static SymbolStorageViewModel CreateSubject(
        Func<bool, CancellationToken, Task<StorageAnalytics>> getAnalyticsAsync) =>
        new(getAnalyticsAsync);
}
