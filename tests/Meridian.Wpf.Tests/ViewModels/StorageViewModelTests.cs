using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class StorageViewModelTests
{
    [Fact]
    public void BuildStoragePosture_WhenArchiveIsEmpty_ProjectsBackfillGuidance()
    {
        var posture = StorageViewModel.BuildStoragePosture(new StorageAnalytics
        {
            LastUpdated = new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Local),
        });

        posture.Title.Should().Be("Archive waiting for data");
        posture.Detail.Should().Contain("Run backfill or collection");
        posture.GrowthText.Should().Be("Daily growth: no recent files");
        posture.CapacityHorizonText.Should().Be("Capacity horizon: not enough growth history");
        posture.LastScanText.Should().StartWith("Last scan:");
    }

    [Fact]
    public void BuildStoragePosture_WhenCapacityHorizonIsShort_ProjectsRetentionWarning()
    {
        var posture = StorageViewModel.BuildStoragePosture(new StorageAnalytics
        {
            TotalFileCount = 245,
            DailyGrowthBytes = 1_048_576,
            ProjectedDaysUntilFull = 3,
            LastUpdated = new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Local),
            SymbolBreakdown = new[]
            {
                new SymbolAnalyticsInfo { Symbol = "SPY" },
            },
        });

        posture.Title.Should().Be("Storage capacity needs attention");
        posture.Detail.Should().Contain("retention");
        posture.GrowthText.Should().Be("Daily growth: 1.0 MB/day");
        posture.CapacityHorizonText.Should().Be("Capacity horizon: 3 days");
    }

    [Fact]
    public void BuildStoragePosture_WhenArchiveHasFilesWithoutRecentGrowth_ProjectsStableGuidance()
    {
        var posture = StorageViewModel.BuildStoragePosture(new StorageAnalytics
        {
            TotalFileCount = 12,
            DailyGrowthBytes = 0,
            LastUpdated = new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Local),
        });

        posture.Title.Should().Be("Archive is stable");
        posture.Detail.Should().Contain("12 retained files");
        posture.GrowthText.Should().Be("Daily growth: no recent files");
    }

    [Fact]
    public void BuildStoragePosture_WhenGrowthIsTracked_ProjectsArchiveScope()
    {
        var posture = StorageViewModel.BuildStoragePosture(new StorageAnalytics
        {
            TotalFileCount = 1200,
            DailyGrowthBytes = 2_097_152,
            ProjectedDaysUntilFull = 120,
            LastUpdated = new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Local),
            SymbolBreakdown = new[]
            {
                new SymbolAnalyticsInfo { Symbol = "SPY" },
                new SymbolAnalyticsInfo { Symbol = "AAPL" },
            },
        });

        posture.Title.Should().Be("Storage growth is being tracked");
        posture.Detail.Should().Contain("files across 2 symbols");
        posture.GrowthText.Should().Be("Daily growth: 2.0 MB/day");
        posture.CapacityHorizonText.Should().Be("Capacity horizon: 120 days");
    }

    [Fact]
    public void BuildStoragePostureUnavailable_WhenScanFails_ProjectsRecoveryGuidance()
    {
        var posture = StorageViewModel.BuildStoragePostureUnavailable();

        posture.Title.Should().Be("Storage metrics unavailable");
        posture.Detail.Should().Contain("configured DataRoot");
        posture.GrowthText.Should().Be("Daily growth: --");
        posture.CapacityHorizonText.Should().Be("Capacity horizon: --");
        posture.LastScanText.Should().Be("Last scan: failed");
    }

    [Fact]
    public void BuildMetricsStatusText_WhenArchiveIsEmpty_ProjectsLoadedEmptyGuidance()
    {
        var status = StorageViewModel.BuildMetricsStatusText(new StorageAnalytics());

        status.Should().Be("Metrics loaded: no retained archive files found under the configured DataRoot.");
    }

    [Fact]
    public void BuildMetricsStatusText_WhenArchiveHasFiles_ProjectsLoadedScope()
    {
        var status = StorageViewModel.BuildMetricsStatusText(new StorageAnalytics
        {
            TotalFileCount = 42,
            SymbolBreakdown = new[]
            {
                new SymbolAnalyticsInfo { Symbol = "SPY" },
                new SymbolAnalyticsInfo { Symbol = "AAPL" },
            },
        });

        status.Should().Be("Metrics loaded: 42 files across 2 symbols.");
    }

    [Fact]
    public async Task LoadAsync_WhenMetricsLoadSucceeds_ProjectsReadinessStatus()
    {
        var forceRefreshRequested = false;
        var viewModel = CreateViewModel(
            loadAnalytics: (forceRefresh, _) =>
            {
                forceRefreshRequested = forceRefresh;
                return Task.FromResult(new StorageAnalytics
                {
                    TotalSizeBytes = 1_048_576,
                    TotalFileCount = 1,
                    TradeSizeBytes = 512,
                    DepthSizeBytes = 256,
                    HistoricalSizeBytes = 128,
                    SymbolBreakdown = new[]
                    {
                        new SymbolAnalyticsInfo { Symbol = "SPY" },
                    },
                });
            });

        await viewModel.LoadAsync();

        forceRefreshRequested.Should().BeFalse();
        viewModel.IsMetricsLoading.Should().BeFalse();
        viewModel.CanRefreshMetrics.Should().BeTrue();
        viewModel.TotalSizeText.Should().Be("1.0 MB");
        viewModel.TotalFilesText.Should().Be("1");
        viewModel.SymbolCountText.Should().Be("1");
        viewModel.MetricsStatusText.Should().Be("Metrics loaded: 1 file across 1 symbol.");
    }

    [Fact]
    public async Task RefreshMetricsCommand_WhenRunning_ProjectsBusyStateAndUsesForceRefresh()
    {
        var forceRefreshRequested = false;
        var analyticsGate = new TaskCompletionSource<StorageAnalytics>();
        var viewModel = CreateViewModel(
            loadAnalytics: (forceRefresh, _) =>
            {
                forceRefreshRequested = forceRefresh;
                return analyticsGate.Task;
            });

        var refreshTask = viewModel.RefreshMetricsCommand.ExecuteAsync(null);

        viewModel.IsMetricsLoading.Should().BeTrue();
        viewModel.CanRefreshMetrics.Should().BeFalse();
        viewModel.RefreshMetricsCommand.CanExecute(null).Should().BeFalse();
        viewModel.MetricsStatusText.Should().Be("Refreshing storage metrics...");

        analyticsGate.SetResult(new StorageAnalytics
        {
            TotalFileCount = 2,
            SymbolBreakdown = new[]
            {
                new SymbolAnalyticsInfo { Symbol = "SPY" },
            },
        });
        await refreshTask;

        forceRefreshRequested.Should().BeTrue();
        viewModel.IsMetricsLoading.Should().BeFalse();
        viewModel.CanRefreshMetrics.Should().BeTrue();
        viewModel.RefreshMetricsCommand.CanExecute(null).Should().BeTrue();
        viewModel.MetricsStatusText.Should().Be("Metrics loaded: 2 files across 1 symbol.");
    }

    [Fact]
    public async Task LoadAsync_WhenMetricsScanFails_ProjectsRecoveryStatus()
    {
        var viewModel = CreateViewModel(
            loadAnalytics: (_, _) => Task.FromException<StorageAnalytics>(new IOException("denied")));

        await viewModel.LoadAsync();

        viewModel.IsMetricsLoading.Should().BeFalse();
        viewModel.CanRefreshMetrics.Should().BeTrue();
        viewModel.TotalSizeText.Should().Be("--");
        viewModel.MetricsStatusText.Should().Be(
            "Storage metrics scan failed. Verify the configured DataRoot and permissions, then refresh.");
        viewModel.StoragePostureTitle.Should().Be("Storage metrics unavailable");
        viewModel.StorageLastScanText.Should().Be("Last scan: failed");
    }

    [Fact]
    public void RefreshPreview_WhenLayoutChanges_ProjectsScopeAndPreviewGuidance()
    {
        var viewModel = CreateViewModel();

        viewModel.RefreshPreview("./archive", "ByDate", "zstd");

        viewModel.PreviewScopeText.Should().Be("By date layout - ZSTD - archive/");
        viewModel.PreviewActionText.Should().Contain("verify the archive path");
        viewModel.FileTreePreviewText.Should().StartWith("archive/");
        viewModel.FileTreePreviewText.Should().Contain("2026-03-03");
        viewModel.FileTreePreviewText.Should().Contain(".jsonl.zst");
        viewModel.StorageEstimateText.Should().Be("Estimated daily size: ~21.0 MB for 3 symbols (trades + quotes sample)");
    }

    [Fact]
    public void Constructor_InitializesBoundPreviewDefaults()
    {
        var viewModel = CreateViewModel();

        viewModel.DataDirectoryText.Should().Be("./data");
        viewModel.NamingConvention.Should().Be("BySymbol");
        viewModel.CompressionFormat.Should().Be("gzip");
        viewModel.PreviewScopeText.Should().Be("By symbol layout - Gzip - data/");
        viewModel.FileTreePreviewText.Should().StartWith("data/");
        viewModel.PreviewActionText.Should().Contain("verify the archive path");
    }

    [Fact]
    public void BoundPreviewInputs_WhenChanged_RegeneratePreview()
    {
        var viewModel = CreateViewModel();

        viewModel.DataDirectoryText = ".\\archive";
        viewModel.NamingConvention = "Flat";
        viewModel.CompressionFormat = "lz4";

        viewModel.PreviewScopeText.Should().Be("Flat layout - LZ4 - archive/");
        viewModel.FileTreePreviewText.Should().StartWith("archive/");
        viewModel.FileTreePreviewText.Should().Contain(".jsonl.lz4");
    }

    [Fact]
    public void BrowseStorageRootCommand_WhenFolderSelected_UpdatesRootAndPreview()
    {
        var picker = new FakeStorageFolderPicker(@"D:\Meridian Archive");
        var viewModel = CreateViewModel(picker);

        viewModel.BrowseStorageRootCommand.Execute(null);

        picker.CapturedCurrentPath.Should().Be("./data");
        viewModel.DataDirectoryText.Should().Be(@"D:\Meridian Archive");
        viewModel.PreviewScopeText.Should().Be("By symbol layout - Gzip - D:/Meridian Archive/");
        viewModel.FileTreePreviewText.Should().StartWith("D:/Meridian Archive/");
    }

    [Fact]
    public void BrowseStorageRootCommand_WhenSelectionCanceled_KeepsCurrentRoot()
    {
        var picker = new FakeStorageFolderPicker(null);
        var viewModel = CreateViewModel(picker);

        viewModel.BrowseStorageRootCommand.Execute(null);

        viewModel.DataDirectoryText.Should().Be("./data");
        viewModel.PreviewScopeText.Should().Be("By symbol layout - Gzip - data/");
    }

    [Theory]
    [InlineData(null, "data")]
    [InlineData("", "data")]
    [InlineData("   ", "data")]
    [InlineData("./data", "data")]
    [InlineData(".\\archive", "archive")]
    [InlineData("/mnt/archive", "mnt/archive")]
    [InlineData(@"D:\Meridian Archive", "D:/Meridian Archive")]
    public void NormalizePreviewRoot_WhenPathIsBlankOrRelative_ReturnsStablePreviewRoot(
        string? dataDirectory,
        string expected)
    {
        StorageViewModel.NormalizePreviewRoot(dataDirectory).Should().Be(expected);
    }

    [Fact]
    public void StoragePageSource_BindsPreviewScopeAndAutomationIds()
    {
        var xaml = File.ReadAllText(GetRepositoryFilePath(@"src\Meridian.Wpf\Views\StoragePage.xaml"));

        xaml.Should().Contain("StoragePreviewScopeStrip");
        xaml.Should().Contain("StoragePreviewScopeText");
        xaml.Should().Contain("StoragePreviewActionText");
        xaml.Should().Contain("StorageFileTreePreviewText");
        xaml.Should().Contain("StorageEstimateText");
        xaml.Should().Contain("StorageDataDirectoryBox");
        xaml.Should().Contain("StorageBrowseDirectoryButton");
        xaml.Should().Contain("{Binding PreviewScopeText}");
        xaml.Should().Contain("{Binding PreviewActionText}");
        xaml.Should().Contain("{Binding DataDirectoryText, Mode=OneWay}");
        xaml.Should().Contain("{Binding BrowseStorageRootCommand}");
        xaml.Should().Contain("{Binding NamingConvention, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}");
        xaml.Should().Contain("{Binding CompressionFormat, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}");
        xaml.Should().Contain("StorageNamingConventionCombo");
        xaml.Should().Contain("StorageCompressionCombo");
        xaml.Should().Contain("StorageArchivePostureCard");
        xaml.Should().Contain("StorageMetricsReadinessStrip");
        xaml.Should().Contain("StorageMetricsStatusText");
        xaml.Should().Contain("StorageRefreshMetricsButton");
        xaml.Should().Contain("StorageMetricsProgress");
        xaml.Should().Contain("{Binding MetricsStatusText}");
        xaml.Should().Contain("{Binding RefreshMetricsCommand}");
        xaml.Should().Contain("{Binding CanRefreshMetrics}");
        xaml.Should().Contain("{Binding IsMetricsLoading, Converter={StaticResource BoolToVisibilityConverter}}");
        xaml.Should().Contain("StoragePostureTitle");
        xaml.Should().Contain("StoragePostureDetail");
        xaml.Should().Contain("StorageGrowthText");
        xaml.Should().Contain("StorageCapacityHorizonText");
        xaml.Should().Contain("StorageLastScanText");
        xaml.Should().Contain("{Binding StoragePostureTitle}");
        xaml.Should().Contain("{Binding StorageCapacityHorizonText}");
        xaml.Should().NotContain("SelectionChanged=\"StorageConfig_Changed\"");
    }

    private static StorageViewModel CreateViewModel(
        IStorageFolderPicker? picker = null,
        Func<bool, CancellationToken, Task<StorageAnalytics>>? loadAnalytics = null)
    {
        picker ??= new FakeStorageFolderPicker(null);

        return loadAnalytics is null
            ? new StorageViewModel(StorageAnalyticsService.Instance, SettingsConfigurationService.Instance, picker)
            : new StorageViewModel(loadAnalytics, SettingsConfigurationService.Instance, picker);
    }

    private sealed class FakeStorageFolderPicker(string? selectedPath) : IStorageFolderPicker
    {
        public string? CapturedCurrentPath { get; private set; }

        public string? SelectFolder(string currentPath)
        {
            CapturedCurrentPath = currentPath;
            return selectedPath;
        }
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

        throw new DirectoryNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
