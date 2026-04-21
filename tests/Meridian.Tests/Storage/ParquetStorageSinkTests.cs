using System.Reflection;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Sinks;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
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
    public async Task FlushAsync_WithL2SnapshotEvents_CreatesParquetFileWithSerializedBookLevels()
    {
        _sink = CreateSink(bufferSize: 10000);
        await _sink.AppendAsync(CreateL2SnapshotEvent("QQQ"));

        await _sink.FlushAsync();

        var parquetFile = Directory.GetFiles(_testRoot, "*l2snapshot*.parquet", SearchOption.AllDirectories)
            .Should().ContainSingle("an L2 snapshot flush should create a dedicated Parquet file")
            .Subject;

        var bidsJson = await ReadStringColumnAsync(parquetFile, "BidsJson");
        var asksJson = await ReadStringColumnAsync(parquetFile, "AsksJson");

        bidsJson.Should().ContainSingle().Which.Should().Contain("\"price\":500.10");
        bidsJson.Should().ContainSingle().Which.Should().Contain("\"marketMaker\":\"MM1\"");
        asksJson.Should().ContainSingle().Which.Should().Contain("\"price\":500.15");
        asksJson.Should().ContainSingle().Which.Should().Contain("\"marketMaker\":\"MM2\"");

        Directory.GetFiles(_testRoot, "*.tmp", SearchOption.AllDirectories)
            .Should().BeEmpty("successful L2 flushes should leave no temp files behind");
    }

    [Fact]
    public async Task DisposeAsync_WithBufferedL2Snapshots_FlushesFinalSnapshots()
    {
        _sink = CreateSink(bufferSize: 10000);
        await _sink.AppendAsync(CreateL2SnapshotEvent("IWM"));

        await _sink.DisposeAsync();
        _sink = null;

        Directory.GetFiles(_testRoot, "*l2snapshot*.parquet", SearchOption.AllDirectories)
            .Should().ContainSingle("DisposeAsync must flush buffered L2 snapshots as part of the final Wave 1 storage proof");
    }

    [Fact]
    public async Task FlushAsync_WhenL2WriteFails_PreservesBufferedSnapshotsForRetry()
    {
        var writeAttempts = 0;
        _sink = CreateSink(
            bufferSize: 10000,
            writeAtomicallyAsync: async (path, writeAsync, ct) =>
            {
                writeAttempts++;
                if (writeAttempts == 1)
                    throw new IOException("simulated flush failure");

                await InvokeWriteAtomicallyAsync(path, writeAsync, ct);
            });

        await _sink.AppendAsync(CreateL2SnapshotEvent("DIA"));

        var failedFlush = () => _sink.FlushAsync();
        await failedFlush.Should().ThrowAsync<IOException>();

        Directory.GetFiles(_testRoot, "*.parquet", SearchOption.AllDirectories)
            .Should().BeEmpty("a failed L2 flush must not commit a partial file");

        await _sink.FlushAsync();

        writeAttempts.Should().Be(2, "the retry should perform a second atomic-write attempt");
        Directory.GetFiles(_testRoot, "*l2snapshot*.parquet", SearchOption.AllDirectories)
            .Should().ContainSingle("the buffered L2 snapshot should still be available for retry after a failed flush");
    }

    [Fact]
    public async Task FlushAsync_WhenL2WriteIsCancelled_PreservesBufferedSnapshotsForRetry()
    {
        var writeAttempts = 0;
        _sink = CreateSink(
            bufferSize: 10000,
            writeAtomicallyAsync: async (path, writeAsync, ct) =>
            {
                writeAttempts++;
                if (writeAttempts == 1)
                    throw new OperationCanceledException("simulated cancellation");

                await InvokeWriteAtomicallyAsync(path, writeAsync, ct);
            });

        await _sink.AppendAsync(CreateL2SnapshotEvent("TLT"));

        var cancelledFlush = () => _sink.FlushAsync();
        await cancelledFlush.Should().ThrowAsync<OperationCanceledException>();

        Directory.GetFiles(_testRoot, "*.parquet", SearchOption.AllDirectories)
            .Should().BeEmpty("a cancelled L2 flush must not commit a file before retry");

        await _sink.FlushAsync();

        writeAttempts.Should().Be(2, "retry should still be possible after cancellation");
        Directory.GetFiles(_testRoot, "*l2snapshot*.parquet", SearchOption.AllDirectories)
            .Should().ContainSingle("cancelled L2 snapshots should remain buffered for a later retry");
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

    [Fact]
    public async Task WriteAtomicallyAsync_WhenWriteDelegateThrows_DeletesTempFile()
    {
        var destination = Path.Combine(_testRoot, "atomic", "failure.parquet");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        var act = () => InvokeWriteAtomicallyAsync(
            destination,
            async stream =>
            {
                var bytes = Encoding.UTF8.GetBytes("partial-write");
                await stream.WriteAsync(bytes, 0, bytes.Length);
                throw new IOException("boom");
            });

        await act.Should().ThrowAsync<IOException>();

        File.Exists(destination).Should().BeFalse("failed atomic writes must not leave a destination file behind");
        Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.tmp", SearchOption.AllDirectories)
            .Should().BeEmpty("failed atomic writes must clean their temp files");
    }

    [Fact]
    public async Task WriteAtomicallyAsync_WhenCancelledAfterTempWrite_DeletesTempFile()
    {
        var destination = Path.Combine(_testRoot, "atomic", "cancelled.parquet");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var cts = new CancellationTokenSource();

        var act = () => InvokeWriteAtomicallyAsync(
            destination,
            async stream =>
            {
                var bytes = Encoding.UTF8.GetBytes("partial-write");
                await stream.WriteAsync(bytes, 0, bytes.Length);
                await stream.FlushAsync();
                cts.Cancel();
            },
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        File.Exists(destination).Should().BeFalse("cancellation after the temp write should still prevent the final rename");
        Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.tmp", SearchOption.AllDirectories)
            .Should().BeEmpty("cancellation should not strand temp files");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private ParquetStorageSink CreateSink(
        int bufferSize = 10000,
        Func<string, Func<Stream, Task>, CancellationToken, Task>? writeAtomicallyAsync = null) =>
        writeAtomicallyAsync is null
            ? new ParquetStorageSink(
                new StorageOptions { RootPath = _testRoot },
                new ParquetStorageOptions
                {
                    BufferSize = bufferSize,
                    CompressionMethod = CompressionMethod.None,
                    FlushInterval = TimeSpan.FromHours(1) // disable periodic flush in tests
                })
            : new ParquetStorageSink(
                new StorageOptions { RootPath = _testRoot },
                new ParquetStorageOptions
                {
                    BufferSize = bufferSize,
                    CompressionMethod = CompressionMethod.None,
                    FlushInterval = TimeSpan.FromHours(1) // disable periodic flush in tests
                },
                writeAtomicallyAsync);

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

    private static MarketEvent CreateL2SnapshotEvent(string symbol) =>
        MarketEvent.L2Snapshot(
            DateTimeOffset.UtcNow,
            symbol,
            new LOBSnapshot(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: symbol,
                Bids:
                [
                    new OrderBookLevel(OrderBookSide.Bid, 0, 500.10m, 10m, "MM1"),
                    new OrderBookLevel(OrderBookSide.Bid, 1, 500.05m, 8m, "MM1")
                ],
                Asks:
                [
                    new OrderBookLevel(OrderBookSide.Ask, 0, 500.15m, 12m, "MM2"),
                    new OrderBookLevel(OrderBookSide.Ask, 1, 500.20m, 14m, "MM2")
                ],
                MidPrice: 500.125m,
                SequenceNumber: 2L,
                Venue: "NASDAQ"),
            seq: 2,
            source: "test");

    private static async Task<string[]> ReadStringColumnAsync(string parquetPath, string columnName)
    {
        await using var stream = File.OpenRead(parquetPath);
        using var reader = await ParquetReader.CreateAsync(stream);
        using var rowGroup = reader.OpenRowGroupReader(0);
        var field = reader.Schema.GetDataFields().Single(f => f.Name == columnName);
        var column = await rowGroup.ReadColumnAsync((DataField)field);
        return column.Data.Should().BeAssignableTo<string[]>().Subject;
    }

    private static Task InvokeWriteAtomicallyAsync(
        string destination,
        Func<Stream, Task> writeAsync,
        CancellationToken cancellationToken = default)
    {
        var method = typeof(ParquetStorageSink).GetMethod(
            "WriteAtomicallyAsync",
            BindingFlags.Static | BindingFlags.NonPublic);

        method.Should().NotBeNull("the Wave 1 atomic-write proof uses the sink's internal temp-file helper directly");
        return (Task)method!.Invoke(null, [destination, writeAsync, cancellationToken])!;
    }
}
