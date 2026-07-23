using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Serialization;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Sinks;
using Meridian.Tests.Infrastructure;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Behavioural proof for JsonlWriteMode.AppendStream (audit finding P10): appends must write
/// only new bytes (no whole-file copy-on-write churn), fsync exactly at the sink flush
/// barrier, survive reopen, and leave readers tolerant of a torn compressed tail.
/// </summary>
public sealed class JsonlAppendStreamTests : TempDirectoryTestBase
{
    [Fact]
    public async Task AppendStream_LaterFlushesNeverRewriteEarlierBytes()
    {
        var options = new StorageOptions { RootPath = TestDataRoot };
        var policy = new TestStoragePolicy(TestDataRoot);
        var batchOptions = new JsonlBatchOptions { BatchSize = 1000, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        await using var sink = new JsonlStorageSink(options, policy, batchOptions);

        await sink.AppendAsync(CreateTestEvent("AAPL", 1));
        await sink.FlushAsync();

        var dayFile = Directory.GetFiles(TestDataRoot, "*.jsonl", SearchOption.AllDirectories).Should().ContainSingle().Subject;
        var prefixLength = new FileInfo(dayFile).Length;
        var prefixHash = HashPrefix(dayFile, prefixLength);

        await sink.AppendAsync(CreateTestEvent("AAPL", 2));
        await sink.FlushAsync();
        await sink.AppendAsync(CreateTestEvent("AAPL", 3));
        await sink.FlushAsync();

        new FileInfo(dayFile).Length.Should().BeGreaterThan(prefixLength, "later batches append new bytes");
        HashPrefix(dayFile, prefixLength).Should().Equal(prefixHash,
            "appending must never rewrite previously written bytes");
        Directory.GetFiles(TestDataRoot, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("append-stream mode must not create copy-on-write temp files");
    }

    [Fact]
    public async Task FlushAsync_FsyncsAtTheBarrierNotPerBatch()
    {
        var options = new StorageOptions { RootPath = TestDataRoot };
        var policy = new TestStoragePolicy(TestDataRoot);
        var batchOptions = new JsonlBatchOptions { BatchSize = 2, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        await using var sink = new JsonlStorageSink(options, policy, batchOptions);

        for (var i = 0; i < 6; i++)
        {
            await sink.AppendAsync(CreateTestEvent("SPY", i));
        }

        sink.BatchesWritten.Should().Be(3, "size-triggered batches were written");
        sink.GetStatistics().FsyncCount.Should().Be(0,
            "batch writes reach the OS but must not fsync individually");

        await sink.FlushAsync();

        var stats = sink.GetStatistics();
        stats.FsyncCount.Should().Be(1, "the sink flush barrier performs exactly one fsync per dirty file");
        stats.OpenWriterHandles.Should().Be(1);
    }

    [Fact]
    public async Task Sink_ReopensSameDayFileAcrossInstances_WithoutLosingEarlierEvents()
    {
        var options = new StorageOptions { RootPath = TestDataRoot };
        var batchOptions = new JsonlBatchOptions { BatchSize = 100, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };

        var first = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions);
        await first.AppendAsync(CreateTestEvent("MSFT", 1));
        await first.DisposeAsync();

        var second = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions);
        await second.AppendAsync(CreateTestEvent("MSFT", 2));
        await second.DisposeAsync();

        var dayFile = Directory.GetFiles(TestDataRoot, "*.jsonl", SearchOption.AllDirectories).Should().ContainSingle().Subject;
        var lines = await File.ReadAllLinesAsync(dayFile);
        lines.Where(l => !string.IsNullOrWhiteSpace(l)).Should().HaveCount(2,
            "a reopened day file must append after the existing tail, not replace it");
    }

    [Fact]
    public async Task AppendStream_ReopenTruncatesTornTailBeforeReplayingEvents()
    {
        var options = new StorageOptions { RootPath = TestDataRoot };
        var batchOptions = new JsonlBatchOptions { BatchSize = 100, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };

        await using (var first = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions))
        {
            await first.AppendAsync(CreateTestEvent("MSFT", 1));
        }

        var dayFile = Directory.GetFiles(TestDataRoot, "*.jsonl", SearchOption.AllDirectories).Should().ContainSingle().Subject;
        await File.AppendAllTextAsync(dayFile, "{\"incomplete\":");

        await using (var replay = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions))
        {
            await replay.AppendAsync(CreateTestEvent("MSFT", 2));
        }

        var lines = await File.ReadAllLinesAsync(dayFile);
        lines.Where(line => !string.IsNullOrWhiteSpace(line)).Should().HaveCount(2);
        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            JsonSerializer.Deserialize<MarketEvent>(line, MarketDataJsonContext.HighPerformanceOptions)
                .Should().NotBeNull("replayed events must not be concatenated to a torn JSONL tail");
        }
    }

    [Fact]
    public async Task CompressedAppendStream_ConcatenatedGzipMembersRoundTrip()
    {
        var options = new StorageOptions { RootPath = TestDataRoot, Compress = true };
        var batchOptions = new JsonlBatchOptions { BatchSize = 100, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        var sink = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot, ".jsonl.gz"), batchOptions);

        await sink.AppendAsync(CreateTestEvent("TLT", 1));
        await sink.FlushAsync();
        await sink.AppendAsync(CreateTestEvent("TLT", 2));
        await sink.DisposeAsync();

        var dayFile = Directory.GetFiles(TestDataRoot, "*.jsonl.gz", SearchOption.AllDirectories).Should().ContainSingle().Subject;
        var lines = await ReadGzipLinesAsync(dayFile);
        lines.Should().HaveCount(2, "each flush appends a gzip member and readers decode concatenated members");
    }

    [Fact]
    public async Task CompressedAppendStream_RefusesToAppendAfterATornGzipMember()
    {
        var options = new StorageOptions { RootPath = TestDataRoot, Compress = true };
        var batchOptions = new JsonlBatchOptions { BatchSize = 100, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };

        await using (var first = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot, ".jsonl.gz"), batchOptions))
        {
            await first.AppendAsync(CreateTestEvent("TLT", 1));
        }

        var dayFile = Directory.GetFiles(TestDataRoot, "*.jsonl.gz", SearchOption.AllDirectories).Should().ContainSingle().Subject;
        await using (var corruptTail = new FileStream(dayFile, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            await corruptTail.WriteAsync(new byte[] { 0x1f, 0x8b, 0x08, 0x00 });
        }
        var lengthBeforeReplay = new FileInfo(dayFile).Length;

        await using var replay = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot, ".jsonl.gz"), batchOptions);
        await replay.AppendAsync(CreateTestEvent("TLT", 2));

        await Assert.ThrowsAsync<InvalidDataException>(() => replay.FlushAsync());
        new FileInfo(dayFile).Length.Should().Be(lengthBeforeReplay,
            "WAL replay must not be appended after an unreadable gzip member");
    }

    [Fact]
    public async Task CopyOnWriteMode_RemainsAvailableAsRollbackPath()
    {
        var options = new StorageOptions { RootPath = TestDataRoot };
        var batchOptions = new JsonlBatchOptions
        {
            BatchSize = 100,
            Enabled = true,
            FlushInterval = TimeSpan.FromMinutes(5),
            WriteMode = JsonlWriteMode.CopyOnWrite
        };
        var sink = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions);

        await sink.AppendAsync(CreateTestEvent("IWM", 1));
        await sink.FlushAsync();
        await sink.AppendAsync(CreateTestEvent("IWM", 2));
        await sink.DisposeAsync();

        var dayFile = Directory.GetFiles(TestDataRoot, "*.jsonl", SearchOption.AllDirectories).Should().ContainSingle().Subject;
        var lines = await File.ReadAllLinesAsync(dayFile);
        lines.Where(l => !string.IsNullOrWhiteSpace(l)).Should().HaveCount(2,
            "the legacy copy-on-write mode must keep working as the rollback path");
    }

    private static byte[] HashPrefix(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[length];
        stream.ReadExactly(buffer, 0, (int)length);
        return SHA256.HashData(buffer);
    }

    private static async Task<List<string>> ReadGzipLinesAsync(string path)
    {
        await using var fileStream = File.OpenRead(path);
        await using var gzip = new GZipStream(fileStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var lines = new List<string>();
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }

        return lines;
    }

    private static MarketEvent CreateTestEvent(string symbol, int sequence)
    {
        var trade = new Trade(
            DateTimeOffset.UtcNow,
            symbol,
            100m + sequence,
            100,
            AggressorSide.Buy,
            sequence);

        return MarketEvent.Trade(DateTimeOffset.UtcNow, symbol, trade, sequence, "TEST");
    }

    private sealed class TestStoragePolicy : IStoragePolicy
    {
        private readonly string _root;
        private readonly string _extension;

        public TestStoragePolicy(string root, string extension = ".jsonl")
        {
            _root = root;
            _extension = extension;
        }

        public string GetPath(MarketEvent evt)
        {
            return Path.Combine(_root, $"{evt.Symbol}_{evt.Timestamp:yyyyMMdd}{_extension}");
        }
    }
}
