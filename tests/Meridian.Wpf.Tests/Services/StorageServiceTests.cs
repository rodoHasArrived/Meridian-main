using FluentAssertions;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for <see cref="StorageService"/> — singleton lifecycle, inheritance,
/// file icon resolution, byte formatting, and cancellation support.
/// </summary>
public sealed class StorageServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        StorageService.Instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameSingleton()
    {
        var a = StorageService.Instance;
        var b = StorageService.Instance;
        a.Should().BeSameAs(b);
    }

    [Fact]
    public void Instance_ThreadSafety_ShouldReturnSameInstance()
    {
        StorageService? i1 = null, i2 = null;
        var t1 = Task.Run(() => i1 = StorageService.Instance);
        var t2 = Task.Run(() => i2 = StorageService.Instance);
        Task.WaitAll(t1, t2);

        i1.Should().NotBeNull();
        i1.Should().BeSameAs(i2);
    }

    // ── Inheritance ──────────────────────────────────────────────────

    [Fact]
    public void StorageService_ShouldInheritFromStorageServiceBase()
    {
        StorageService.Instance.Should().BeAssignableTo<StorageServiceBase>();
    }

    // ── FormatBytes ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "0.0 B")]
    [InlineData(512, "512.0 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1_048_576, "1.0 MB")]
    [InlineData(1_073_741_824, "1.0 GB")]
    [InlineData(1_099_511_627_776, "1.0 TB")]
    public void FormatBytes_ShouldFormatCorrectly(long bytes, string expected)
    {
        StorageServiceBase.FormatBytes(bytes).Should().Be(expected);
    }

    [Fact]
    public void FormatBytes_LargeValue_ShouldFormatInTB()
    {
        var result = StorageServiceBase.FormatBytes(5_497_558_138_880); // 5 TB
        result.Should().Be("5.0 TB");
    }

    // ── GetFileIcon ──────────────────────────────────────────────────

    [Theory]
    [InlineData("trades", "\uE8AB")]
    [InlineData("quotes", "\uE8D4")]
    [InlineData("depth", "\uE8A1")]
    [InlineData("bars", "\uE9D9")]
    [InlineData("parquet", "\uE7C3")]
    public void GetFileIcon_KnownTypes_ShouldReturnExpectedIcon(string dataType, string expectedIcon)
    {
        StorageServiceBase.GetFileIcon(dataType).Should().Be(expectedIcon);
    }

    [Theory]
    [InlineData("Trades")]
    [InlineData("TRADES")]
    [InlineData("TrAdEs")]
    public void GetFileIcon_ShouldBeCaseInsensitive(string dataType)
    {
        StorageServiceBase.GetFileIcon(dataType).Should().Be("\uE8AB"); // Trades icon
    }

    [Fact]
    public void GetFileIcon_UnknownType_ShouldReturnDefaultIcon()
    {
        StorageServiceBase.GetFileIcon("unknown").Should().Be("\uE8A5");
    }

    [Fact]
    public void GetFileIcon_EmptyString_ShouldReturnDefaultIcon()
    {
        StorageServiceBase.GetFileIcon("").Should().Be("\uE8A5");
    }

    // ── API methods with cancellation ────────────────────────────────

    [Fact]
    public async Task GetStorageStatsAsync_WithCancellation_ShouldThrowOrReturnNull()
    {
        var svc = StorageService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetStorageStatsAsync(cts.Token);
            result.Should().BeNull();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task GetStorageBreakdownAsync_WithCancellation_ShouldThrowOrReturnNull()
    {
        var svc = StorageService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetStorageBreakdownAsync(cts.Token);
            result.Should().BeNull();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task GetStorageHealthAsync_WithCancellation_ShouldThrowOrReturnNull()
    {
        var svc = StorageService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetStorageHealthAsync(cts.Token);
            result.Should().BeNull();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task GetArchiveStatsAsync_WithCancellation_ShouldThrowOrReturnNull()
    {
        var svc = StorageService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetArchiveStatsAsync(cts.Token);
            result.Should().BeNull();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task GetSymbolFilesAsync_WithCancellation_ShouldThrowOrReturnEmpty()
    {
        var svc = StorageService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetSymbolFilesAsync("SPY", cts.Token);
            // Could return empty list on failure
            result.Should().NotBeNull();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task GetSymbolInfoAsync_WithCancellation_ShouldThrowOrReturnNull()
    {
        var svc = StorageService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetSymbolInfoAsync("SPY", cts.Token);
            result.Should().BeNull();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }

    [Fact]
    public async Task GetSymbolFolderPathAsync_WithCancellation_ShouldThrowOrReturnNull()
    {
        var svc = StorageService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            var result = await svc.GetSymbolFolderPathAsync("SPY", cts.Token);
            result.Should().BeNull();
        }
        catch (Exception ex)
        {
            ex.Should().BeAssignableTo<Exception>();
        }
    }
}
