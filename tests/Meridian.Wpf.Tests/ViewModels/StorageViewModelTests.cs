using System;
using System.IO;
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

    [Theory]
    [InlineData(null, "data")]
    [InlineData("", "data")]
    [InlineData("   ", "data")]
    [InlineData("./data", "data")]
    [InlineData(".\\archive", "archive")]
    [InlineData("/mnt/archive", "mnt/archive")]
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
        xaml.Should().Contain("{Binding PreviewScopeText}");
        xaml.Should().Contain("{Binding PreviewActionText}");
        xaml.Should().Contain("StorageNamingConventionCombo");
        xaml.Should().Contain("StorageCompressionCombo");
        xaml.Should().Contain("StorageArchivePostureCard");
        xaml.Should().Contain("StoragePostureTitle");
        xaml.Should().Contain("StoragePostureDetail");
        xaml.Should().Contain("StorageGrowthText");
        xaml.Should().Contain("StorageCapacityHorizonText");
        xaml.Should().Contain("StorageLastScanText");
        xaml.Should().Contain("{Binding StoragePostureTitle}");
        xaml.Should().Contain("{Binding StorageCapacityHorizonText}");
    }

    private static StorageViewModel CreateViewModel() =>
        new(StorageAnalyticsService.Instance, SettingsConfigurationService.Instance);

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
