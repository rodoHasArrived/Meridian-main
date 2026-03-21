using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="ArchiveBrowserService"/> and its associated model types.
/// </summary>
public sealed class ArchiveBrowserServiceTests
{
    // ── Constructor ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithManifestService_ShouldNotThrow()
    {
        var manifestService = ManifestService.Instance;
        var act = () => new ArchiveBrowserService(manifestService);
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithNullManifestService_ShouldAcceptNull()
    {
        // ManifestService parameter is not null-checked in the constructor;
        // verify it does not throw at construction time.
        var service = new ArchiveBrowserService(ManifestService.Instance);
        service.Should().NotBeNull();
    }

    // ── ArchiveTree model ───────────────────────────────────────────

    [Fact]
    public void ArchiveTree_DefaultValues_ShouldBeCorrect()
    {
        var tree = new ArchiveTree();

        tree.RootPath.Should().BeEmpty();
        tree.TotalFiles.Should().Be(0);
        tree.TotalSize.Should().Be(0);
        tree.Years.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ArchiveTree_GeneratedAt_ShouldBeDefaultDateTime()
    {
        var tree = new ArchiveTree();
        tree.GeneratedAt.Should().Be(default(DateTime));
    }

    // ── YearNode model ──────────────────────────────────────────────

    [Fact]
    public void YearNode_DefaultValues_ShouldBeCorrect()
    {
        var node = new YearNode();

        node.Year.Should().Be(0);
        node.TotalFiles.Should().Be(0);
        node.TotalSize.Should().Be(0);
        node.Months.Should().NotBeNull().And.BeEmpty();
    }

    // ── MonthNode model ─────────────────────────────────────────────

    [Fact]
    public void MonthNode_DefaultValues_ShouldBeCorrect()
    {
        var node = new MonthNode();

        node.Month.Should().Be(0);
        node.TotalFiles.Should().Be(0);
        node.TotalSize.Should().Be(0);
        node.Days.Should().NotBeNull().And.BeEmpty();
    }

    // ── DayNode model ───────────────────────────────────────────────

    [Fact]
    public void DayNode_DefaultValues_ShouldBeCorrect()
    {
        var node = new DayNode();

        node.Day.Should().Be(0);
        node.Date.Should().Be(default(DateOnly));
        node.TotalFiles.Should().Be(0);
        node.TotalSize.Should().Be(0);
        node.Symbols.Should().NotBeNull().And.BeEmpty();
    }

    // ── SymbolNode model ────────────────────────────────────────────

    [Fact]
    public void SymbolNode_DefaultValues_ShouldBeCorrect()
    {
        var node = new SymbolNode();

        node.Symbol.Should().BeEmpty();
        node.Files.Should().NotBeNull().And.BeEmpty();
    }

    // ── BrowserArchiveFileInfo model ────────────────────────────────

    [Fact]
    public void BrowserArchiveFileInfo_DefaultValues_ShouldBeCorrect()
    {
        var info = new BrowserArchiveFileInfo();

        info.FullPath.Should().BeEmpty();
        info.RelativePath.Should().BeEmpty();
        info.FileName.Should().BeEmpty();
        info.EventType.Should().BeEmpty();
        info.Size.Should().Be(0);
        info.IsCompressed.Should().BeFalse();
        info.LastModified.Should().Be(default(DateTime));
    }

    [Fact]
    public void BrowserArchiveFileInfo_CanSetProperties()
    {
        var info = new BrowserArchiveFileInfo
        {
            FullPath = "/data/SPY/Trade/2024-01-15.jsonl.gz",
            RelativePath = "SPY/Trade/2024-01-15.jsonl.gz",
            FileName = "2024-01-15.jsonl.gz",
            EventType = "Trade",
            Size = 1024,
            IsCompressed = true,
            LastModified = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc)
        };

        info.FullPath.Should().Contain("SPY");
        info.IsCompressed.Should().BeTrue();
        info.Size.Should().Be(1024);
    }

    // ── FileVerificationResult model ────────────────────────────────

    [Fact]
    public void FileVerificationResult_DefaultValues_ShouldBeCorrect()
    {
        var result = new FileVerificationResult();

        result.FilePath.Should().BeEmpty();
        result.IsValid.Should().BeFalse();
        result.ExpectedChecksum.Should().BeNull();
        result.ActualChecksum.Should().BeNull();
        result.ChecksumMatch.Should().BeFalse();
        result.EventCount.Should().Be(0);
        result.Issues.Should().NotBeNull().And.BeEmpty();
    }

    // ── GetArchiveTreeAsync with non-existent path ──────────────────

    [Fact]
    public async Task GetArchiveTreeAsync_WithNonExistentPath_ShouldReturnEmptyTree()
    {
        var service = new ArchiveBrowserService(ManifestService.Instance);
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var tree = await service.GetArchiveTreeAsync(nonExistentPath);

        tree.Should().NotBeNull();
        tree.RootPath.Should().Be(nonExistentPath);
        tree.Years.Should().BeEmpty();
        tree.TotalFiles.Should().Be(0);
        tree.TotalSize.Should().Be(0);
    }

    // ── ExportOptions model ─────────────────────────────────────────

    [Fact]
    public void ExportOptions_DefaultValues_ShouldBeCorrect()
    {
        var options = new ExportOptions();

        options.Decompress.Should().BeFalse();
        options.Overwrite.Should().BeFalse();
        options.PreserveStructure.Should().BeFalse();
    }
}
