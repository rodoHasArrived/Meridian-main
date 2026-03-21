using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="StorageAnalyticsService"/> models, static utilities,
/// and the FormatBytes helper.
/// Note: Full analytics calculation requires file I/O, so these tests
/// focus on data models and the static FormatBytes utility.
/// </summary>
public sealed class StorageAnalyticsServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = StorageAnalyticsService.Instance;
        var b = StorageAnalyticsService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── FormatBytes ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "0.0 B")]
    [InlineData(512, "512.0 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(1099511627776, "1.0 TB")]
    public void FormatBytes_ShouldFormatCorrectly(long bytes, string expected)
    {
        StorageAnalyticsService.FormatBytes(bytes).Should().Be(expected);
    }

    // ── StorageAnalytics model ───────────────────────────────────────

    [Fact]
    public void StorageAnalytics_ShouldHaveDefaults()
    {
        var analytics = new StorageAnalytics();
        analytics.LastUpdated.Should().BeNull();
        analytics.TotalSizeBytes.Should().Be(0);
        analytics.TotalFileCount.Should().Be(0);
        analytics.TradeSizeBytes.Should().Be(0);
        analytics.TradeFileCount.Should().Be(0);
        analytics.DepthSizeBytes.Should().Be(0);
        analytics.DepthFileCount.Should().Be(0);
        analytics.HistoricalSizeBytes.Should().Be(0);
        analytics.HistoricalFileCount.Should().Be(0);
        analytics.SymbolBreakdown.Should().NotBeNull().And.BeEmpty();
        analytics.DailyGrowthBytes.Should().Be(0);
        analytics.ProjectedDaysUntilFull.Should().BeNull();
    }

    [Fact]
    public void StorageAnalytics_TotalFiles_ShouldAliasTotalFileCount()
    {
        var analytics = new StorageAnalytics { TotalFileCount = 42 };
        analytics.TotalFiles.Should().Be(42);
    }

    [Fact]
    public void StorageAnalytics_ShouldAcceptRealisticValues()
    {
        var analytics = new StorageAnalytics
        {
            LastUpdated = DateTime.UtcNow,
            TotalSizeBytes = 10_737_418_240L, // ~10 GB
            TotalFileCount = 2500,
            TradeSizeBytes = 6_000_000_000L,
            TradeFileCount = 1500,
            DepthSizeBytes = 3_000_000_000L,
            DepthFileCount = 800,
            HistoricalSizeBytes = 1_737_418_240L,
            HistoricalFileCount = 200,
            DailyGrowthBytes = 500_000_000L,
            ProjectedDaysUntilFull = 90,
            SymbolBreakdown = new[]
            {
                new SymbolAnalyticsInfo
                {
                    Symbol = "SPY",
                    SizeBytes = 2_000_000_000L,
                    FileCount = 500,
                    PercentOfTotal = 18.6f,
                    OldestData = DateTime.UtcNow.AddDays(-30),
                    NewestData = DateTime.UtcNow
                }
            }
        };

        analytics.TotalSizeBytes.Should().BeGreaterThan(0);
        analytics.TotalFileCount.Should().Be(2500);
        analytics.SymbolBreakdown.Should().HaveCount(1);
        analytics.SymbolBreakdown[0].Symbol.Should().Be("SPY");
    }

    // ── SymbolAnalyticsInfo model ────────────────────────────────────

    [Fact]
    public void SymbolAnalyticsInfo_ShouldHaveDefaults()
    {
        var info = new SymbolAnalyticsInfo();
        info.Symbol.Should().BeEmpty();
        info.SizeBytes.Should().Be(0);
        info.FileCount.Should().Be(0);
        info.PercentOfTotal.Should().Be(0);
    }

    [Fact]
    public void SymbolAnalyticsInfo_ShouldStoreValues()
    {
        var info = new SymbolAnalyticsInfo
        {
            Symbol = "AAPL",
            SizeBytes = 500_000_000,
            FileCount = 120,
            OldestData = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            NewestData = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc),
            PercentOfTotal = 12.5f
        };

        info.Symbol.Should().Be("AAPL");
        info.SizeBytes.Should().Be(500_000_000);
        info.FileCount.Should().Be(120);
        info.PercentOfTotal.Should().Be(12.5);
        info.NewestData.Should().BeAfter(info.OldestData);
    }

    // ── DriveStorageInfo model ───────────────────────────────────────

    [Fact]
    public void DriveStorageInfo_ShouldHaveDefaults()
    {
        var info = new DriveStorageInfo();
        info.DriveName.Should().BeEmpty();
        info.TotalBytes.Should().Be(0);
        info.FreeBytes.Should().Be(0);
        info.UsedBytes.Should().Be(0);
        info.UsedPercent.Should().Be(0);
        info.DriveType.Should().BeEmpty();
    }

    [Fact]
    public void DriveStorageInfo_ShouldAcceptRealisticValues()
    {
        var total = 500_000_000_000L; // 500 GB
        var free = 200_000_000_000L; // 200 GB
        var used = total - free;
        var usedPercent = (double)used / total * 100;

        var info = new DriveStorageInfo
        {
            DriveName = "C:",
            TotalBytes = total,
            FreeBytes = free,
            UsedBytes = used,
            UsedPercent = usedPercent,
            DriveType = "Fixed"
        };

        info.DriveName.Should().Be("C:");
        info.TotalBytes.Should().Be(total);
        info.FreeBytes.Should().Be(free);
        info.UsedBytes.Should().Be(used);
        info.UsedPercent.Should().BeApproximately(60.0, 0.1);
    }

    // ── StorageAnalyticsEventArgs model ──────────────────────────────

    [Fact]
    public void StorageAnalyticsEventArgs_ShouldHaveDefaults()
    {
        var args = new StorageAnalyticsEventArgs();
        args.Analytics.Should().BeNull();
    }

    [Fact]
    public void StorageAnalyticsEventArgs_ShouldAcceptAnalytics()
    {
        var analytics = new StorageAnalytics { TotalFileCount = 100 };
        var args = new StorageAnalyticsEventArgs { Analytics = analytics };

        args.Analytics.Should().NotBeNull();
        args.Analytics!.TotalFileCount.Should().Be(100);
    }

    // ── SymbolHasDataAsync / GetLastUpdateTimeAsync ──────────────────

    [Fact]
    public async Task SymbolHasDataAsync_NonExistentPath_ShouldReturnFalse()
    {
        var svc = StorageAnalyticsService.Instance;
        var result = await svc.SymbolHasDataAsync("SPY", "/non/existent/path/" + Guid.NewGuid());
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetLastUpdateTimeAsync_NonExistentPath_ShouldReturnNull()
    {
        var svc = StorageAnalyticsService.Instance;
        var result = await svc.GetLastUpdateTimeAsync("SPY", "/non/existent/path/" + Guid.NewGuid());
        result.Should().BeNull();
    }

    // ── SymbolHasDataAsync with temp directory ───────────────────────

    [Fact]
    public async Task SymbolHasDataAsync_WithDataFile_ShouldReturnTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mdc-test-" + Guid.NewGuid());
        try
        {
            var symbolDir = Path.Combine(tempDir, "SPY");
            Directory.CreateDirectory(symbolDir);
            await File.WriteAllTextAsync(Path.Combine(symbolDir, "trades_2026-01-01.jsonl"), "{}");

            var svc = StorageAnalyticsService.Instance;
            var result = await svc.SymbolHasDataAsync("SPY", tempDir);
            result.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetLastUpdateTimeAsync_WithDataFile_ShouldReturnTime()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mdc-test-" + Guid.NewGuid());
        try
        {
            var symbolDir = Path.Combine(tempDir, "AAPL");
            Directory.CreateDirectory(symbolDir);
            var filePath = Path.Combine(symbolDir, "trades.jsonl");
            await File.WriteAllTextAsync(filePath, "{}");

            var svc = StorageAnalyticsService.Instance;
            var result = await svc.GetLastUpdateTimeAsync("AAPL", tempDir);
            result.Should().NotBeNull();
            result!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
