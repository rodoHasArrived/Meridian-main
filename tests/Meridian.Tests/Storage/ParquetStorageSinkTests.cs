using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Sinks;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Tests for <see cref="ParquetStorageSink"/> covering the atomic temp-write-then-rename
/// pattern, flush success, final flush on disposal, and dispose guard.
/// </summary>
public sealed class ParquetStorageSinkTests : IAsyncDisposable
{
    private readonly string _testRoot;
    private ParquetStorageSink? _sink;

    public ParquetStorageSinkTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"meridian_parquet_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    public async ValueTask DisposeAsync()
    {
        if (_sink is not null)
            await _sink.DisposeAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_testRoot))
                    Directory.Delete(_testRoot, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4) { await Task.Delay(20); }
            catch (UnauthorizedAccessException) when (attempt < 4) { await Task.Delay(20); }
        }
    }

    [Fact]
    public async Task FlushAsync_WithTradeEvents_CreatesParquetFileAndNoTempFiles()
    {
        // Arrange – use a small buffer so FlushAsync is the trigger, not buffer overflow
        _sink = CreateSink(bufferSize: 10000);
        var evt = CreateTradeEvent("SPY");
        await _sink.AppendAsync(evt);

        // Act
        await _sink.FlushAsync();

        // Assert – at least one .parquet file was created under testRoot
        var parquetFiles = Directory.GetFiles(_testRoot, "*.parquet", SearchOption.AllDirectories);
        parquetFiles.Should().NotBeEmpty("a Parquet file must be written after FlushAsync");

        // No stray .tmp files should remain (atomic write pattern)
        var tmpFiles = Directory.GetFiles(_testRoot, "*.tmp", SearchOption.AllDirectories);
        tmpFiles.Should().BeEmpty("temp files must be cleaned up after a successful atomic write");
    }

    [Fact]
    public async Task DisposeAsync_WithBufferedEvents_FlushesFinalEvents()
    {
        // Arrange – buffer a trade event but do NOT call FlushAsync explicitly
        _sink = CreateSink(bufferSize: 10000);
        await _sink.AppendAsync(CreateTradeEvent("AAPL"));

        // Act – disposal triggers the final flush
        await _sink.DisposeAsync();
        _sink = null; // already disposed; prevent double-dispose in DisposeAsync()

        // Assert – file must exist after disposal flush
        var parquetFiles = Directory.GetFiles(_testRoot, "*.parquet", SearchOption.AllDirectories);
        parquetFiles.Should().NotBeEmpty("DisposeAsync must flush buffered events to disk");
    }

    [Fact]
    public async Task AppendAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        _sink = CreateSink();
        await _sink.DisposeAsync();
        var disposedSink = _sink;
        _sink = null; // prevent double-dispose

        // Act / Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => disposedSink.AppendAsync(CreateTradeEvent("MSFT")).AsTask());
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private ParquetStorageSink CreateSink(int bufferSize = 10000) =>
        new ParquetStorageSink(
            new StorageOptions { RootPath = _testRoot },
            new ParquetStorageOptions
            {
                BufferSize = bufferSize,
                FlushInterval = TimeSpan.FromHours(1) // disable periodic flush in tests
            });

    private static MarketEvent CreateTradeEvent(string symbol) =>
        MarketEvent.Trade(
            DateTimeOffset.UtcNow,
            symbol,
            new Trade(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: symbol,
                Price: 100m,
                Size: 10L,
                Aggressor: AggressorSide.Buy,
                SequenceNumber: 1L),
            seq: 1,
            source: "test");
}
