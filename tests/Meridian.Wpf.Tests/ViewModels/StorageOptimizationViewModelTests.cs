using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class StorageOptimizationViewModelTests
{
    [Fact]
    public async Task LoadAsync_WhenAnalyticsAndDriveSucceed_ProjectsOverviewRowsAndDrivePosture()
    {
        var viewModel = CreateViewModel(
            getAnalyticsAsync: (_, _) => Task.FromResult(new StorageAnalytics
            {
                TotalSizeBytes = 1_048_576,
                TotalFileCount = 42,
                DailyGrowthBytes = 524_288,
                ProjectedDaysUntilFull = 15,
                TradeSizeBytes = 512,
                TradeFileCount = 4,
                DepthSizeBytes = 256,
                DepthFileCount = 2,
                HistoricalSizeBytes = 128,
                HistoricalFileCount = 1,
                SymbolBreakdown = new[]
                {
                    new SymbolAnalyticsInfo { Symbol = "SPY", SizeBytes = 700, FileCount = 7, PercentOfTotal = 70 },
                    new SymbolAnalyticsInfo { Symbol = "AAPL", SizeBytes = 300, FileCount = 3, PercentOfTotal = 30 },
                },
            }),
            getDriveInfoAsync: _ => Task.FromResult<DriveStorageInfo?>(new DriveStorageInfo
            {
                DriveName = "D:",
                DriveType = "Fixed",
                FreeBytes = 2_097_152,
                UsedPercent = 91.5,
            }));

        await viewModel.LoadAsync();

        viewModel.IsAnalyticsLoading.Should().BeFalse();
        viewModel.CanRefreshAnalytics.Should().BeTrue();
        viewModel.OverviewStatusText.Should().Be("Analytics loaded: 42 files across 2 symbols.");
        viewModel.OverviewStatusTone.Should().Be("Success");
        viewModel.TotalSizeText.Should().Be("1.0 MB");
        viewModel.TotalFilesText.Should().Be("42");
        viewModel.DailyGrowthText.Should().Be("512.0 KB/day");
        viewModel.DaysUntilFullText.Should().Be("15");
        viewModel.TradeFilesText.Should().Be("4 files");
        viewModel.SymbolBreakdownRows.Should().HaveCount(2);
        viewModel.SymbolBreakdownRows[0].Symbol.Should().Be("SPY");
        viewModel.HasSymbolBreakdown.Should().BeTrue();
        viewModel.IsSymbolBreakdownEmpty.Should().BeFalse();
        viewModel.DriveNameText.Should().Be("D: (Fixed)");
        viewModel.FreeSpaceText.Should().Be("2.0 MB");
        viewModel.UsedPercentText.Should().Be("91.5%");
        viewModel.DriveUsageTone.Should().Be("Error");
    }

    [Fact]
    public async Task RefreshAnalyticsCommand_WhenRunning_DisablesRefreshAndRequestsForceRefresh()
    {
        var forceRefreshRequested = false;
        var analyticsGate = new TaskCompletionSource<StorageAnalytics>();
        var viewModel = CreateViewModel(
            getAnalyticsAsync: (forceRefresh, _) =>
            {
                forceRefreshRequested = forceRefresh;
                return analyticsGate.Task;
            },
            getDriveInfoAsync: _ => Task.FromResult<DriveStorageInfo?>(null));

        var refreshTask = viewModel.RefreshAnalyticsCommand.ExecuteAsync(null);

        forceRefreshRequested.Should().BeTrue();
        viewModel.IsAnalyticsLoading.Should().BeTrue();
        viewModel.CanRefreshAnalytics.Should().BeFalse();
        viewModel.RefreshAnalyticsCommand.CanExecute(null).Should().BeFalse();
        viewModel.OverviewStatusText.Should().Be("Refreshing storage analytics...");

        analyticsGate.SetResult(new StorageAnalytics());
        await refreshTask;

        viewModel.IsAnalyticsLoading.Should().BeFalse();
        viewModel.CanRefreshAnalytics.Should().BeTrue();
        viewModel.RefreshAnalyticsCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_WhenAnalyticsFails_ProjectsRecoveryState()
    {
        var viewModel = CreateViewModel(
            getAnalyticsAsync: (_, _) => Task.FromException<StorageAnalytics>(new IOException("denied")),
            getDriveInfoAsync: _ => Task.FromResult<DriveStorageInfo?>(null));

        await viewModel.LoadAsync();

        viewModel.OverviewStatusText.Should().Be("Storage analytics failed. Verify DataRoot access and refresh.");
        viewModel.OverviewStatusTone.Should().Be("Error");
        viewModel.SymbolBreakdownStatusText.Should().Be("Failed to load analytics: denied");
        viewModel.SymbolBreakdownTone.Should().Be("Error");
        viewModel.HasSymbolBreakdown.Should().BeFalse();
        viewModel.IsSymbolBreakdownEmpty.Should().BeTrue();
        viewModel.DriveNameText.Should().Be("Unknown");
        viewModel.DriveUsageTone.Should().Be("Warning");
    }

    [Fact]
    public async Task RunAnalysisCommand_WhenReportSucceeds_UsesDataRootAndDefaultOptions()
    {
        var capturedPath = string.Empty;
        StorageAnalysisOptions? capturedOptions = null;
        var viewModel = CreateViewModel(
            resolveDataRootAsync: _ => Task.FromResult(@"D:\MeridianData"),
            analyzeStorageAsync: (path, options, _) =>
            {
                capturedPath = path;
                capturedOptions = options;
                return Task.FromResult(new StorageOptimizationReport
                {
                    AnalyzedAt = new DateTime(2026, 5, 25, 14, 30, 0, DateTimeKind.Utc),
                    TotalBytes = 2_097_152,
                    ProjectedUsageAfterOptimization = 1_048_576,
                    EstimatedOptimizationTime = TimeSpan.FromMinutes(7),
                    Recommendations =
                    {
                        new OptimizationRecommendation
                        {
                            Title = "Compress archives",
                            PotentialSavingsBytes = 1_048_576,
                        },
                    },
                });
            });

        await viewModel.RunAnalysisCommand.ExecuteAsync(null);

        capturedPath.Should().Be(@"D:\MeridianData");
        capturedOptions.Should().NotBeNull();
        capturedOptions!.CalculateHashes.Should().BeFalse();
        capturedOptions.FindDuplicates.Should().BeTrue();
        capturedOptions.AnalyzeCompression.Should().BeTrue();
        capturedOptions.FindSmallFiles.Should().BeTrue();
        capturedOptions.AnalyzeTiering.Should().BeTrue();
        capturedOptions.ColdTierAgeDays.Should().Be(90);
        viewModel.IsOptimizationActionRunning.Should().BeFalse();
        viewModel.CanRunOptimizationActions.Should().BeTrue();
        viewModel.AnalysisResultTone.Should().Be("Success");
        viewModel.AnalysisResultText.Should().Contain("Storage Optimization Report");
        viewModel.AnalysisResultText.Should().Contain("Compress archives");
    }

    [Fact]
    public async Task RunAnalysisCommand_WhenReportHasErrors_ProjectsWarning()
    {
        var viewModel = CreateViewModel(
            analyzeStorageAsync: (_, _, _) => Task.FromResult(new StorageOptimizationReport
            {
                Errors = { "catalog unavailable" },
            }));

        await viewModel.RunAnalysisCommand.ExecuteAsync(null);

        viewModel.AnalysisResultTone.Should().Be("Warning");
        viewModel.AnalysisResultText.Should().Contain("Analysis completed with errors");
        viewModel.AnalysisResultText.Should().Contain("catalog unavailable");
    }

    [Fact]
    public async Task ViewTierStatisticsCommand_WhenStatsAvailable_FormatsTierSummary()
    {
        var viewModel = CreateViewModel(
            getTierStatisticsAsync: _ => Task.FromResult<TierStatisticsApiResult?>(new TierStatisticsApiResult
            {
                GeneratedAt = new DateTime(2026, 5, 25, 18, 0, 0, DateTimeKind.Utc),
                TotalBytes = 3_145_728,
                TotalFiles = 6,
                Hot = new TierStats { FileCount = 2, TotalBytes = 1_048_576, PercentageOfTotal = 33.3 },
                Warm = new TierStats { FileCount = 3, TotalBytes = 1_572_864, PercentageOfTotal = 50.0 },
                Cold = new TierStats { FileCount = 1, TotalBytes = 524_288, PercentageOfTotal = 16.7 },
            }));

        await viewModel.ViewTierStatisticsCommand.ExecuteAsync(null);

        viewModel.AnalysisResultTone.Should().Be("Info");
        viewModel.AnalysisResultText.Should().Contain("Tier Statistics");
        viewModel.AnalysisResultText.Should().Contain("Total: 3.0 MB across 6 files");
        viewModel.AnalysisResultText.Should().Contain("Hot Tier:");
        viewModel.AnalysisResultText.Should().Contain("Warm Tier:");
        viewModel.AnalysisResultText.Should().Contain("Cold Tier:");
    }

    [Fact]
    public async Task ViewTierStatisticsCommand_WhenStatsUnavailable_ProjectsWarning()
    {
        var viewModel = CreateViewModel(
            getTierStatisticsAsync: _ => Task.FromResult<TierStatisticsApiResult?>(null));

        await viewModel.ViewTierStatisticsCommand.ExecuteAsync(null);

        viewModel.AnalysisResultTone.Should().Be("Warning");
        viewModel.AnalysisResultText.Should().Be("Tier statistics unavailable. Backend may not be running.");
    }

    [Fact]
    public void StorageOptimizationPageSource_BindsViewModelStateAndKeepsCodeBehindThin()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\StorageOptimizationPage.xaml"));
        var codeBehind = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\StorageOptimizationPage.xaml.cs"));

        xaml.Should().Contain("{Binding RefreshAnalyticsCommand}");
        xaml.Should().Contain("{Binding OverviewStatusText}");
        xaml.Should().Contain("{Binding IsAnalyticsLoading, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding TotalSizeText}");
        xaml.Should().Contain("{Binding SymbolBreakdownRows}");
        xaml.Should().Contain("{Binding IsSymbolBreakdownEmpty, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("{Binding DriveUsedPercent, Mode=OneWay}");
        xaml.Should().Contain("{Binding RunAnalysisCommand}");
        xaml.Should().Contain("{Binding ViewTierStatisticsCommand}");
        xaml.Should().Contain("{Binding AnalysisResultText}");
        xaml.Should().Contain("StorageOptimizationActionProgress");
        xaml.Should().NotContain("Click=\"RefreshAnalytics_Click\"");
        xaml.Should().NotContain("Click=\"RunAnalysis_Click\"");
        xaml.Should().NotContain("Click=\"ViewTierStats_Click\"");

        codeBehind.Should().Contain("DataContext = _viewModel");
        codeBehind.Should().Contain("await _viewModel.LoadAsync()");
        codeBehind.Should().NotContain("StorageAnalyticsService.Instance");
        codeBehind.Should().NotContain("StorageOptimizationAdvisorService.Instance");
        codeBehind.Should().NotContain("AnalysisResultText.Text");
        codeBehind.Should().NotContain("FindResource");
    }

    private static StorageOptimizationViewModel CreateViewModel(
        Func<bool, CancellationToken, Task<StorageAnalytics>>? getAnalyticsAsync = null,
        Func<CancellationToken, Task<DriveStorageInfo?>>? getDriveInfoAsync = null,
        Func<CancellationToken, Task<string>>? resolveDataRootAsync = null,
        Func<string, StorageAnalysisOptions, CancellationToken, Task<StorageOptimizationReport>>? analyzeStorageAsync = null,
        Func<CancellationToken, Task<TierStatisticsApiResult?>>? getTierStatisticsAsync = null)
        => new(
            getAnalyticsAsync ?? ((_, _) => Task.FromResult(new StorageAnalytics())),
            getDriveInfoAsync ?? (_ => Task.FromResult<DriveStorageInfo?>(null)),
            resolveDataRootAsync ?? (_ => Task.FromResult(@"D:\MeridianData")),
            analyzeStorageAsync ?? ((_, _, _) => Task.FromResult(new StorageOptimizationReport())),
            getTierStatisticsAsync ?? (_ => Task.FromResult<TierStatisticsApiResult?>(null)));

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

        throw new DirectoryNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
