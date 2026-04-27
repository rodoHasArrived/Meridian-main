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
