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
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Services;
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

    [Fact]
    public async Task Compression_RefusesFileWhileSinkHoldsItsAppendStream()
    {
        var options = new StorageOptions { RootPath = TestDataRoot };
        var batchOptions = new JsonlBatchOptions { BatchSize = 1000, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        await using var sink = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions);
        await sink.AppendAsync(CreateTestEvent("AAPL", 1, ClosedDay));
        await sink.FlushAsync();
        var dayFile = Directory.GetFiles(TestDataRoot, "*.jsonl").Should().ContainSingle().Subject;
        var before = await File.ReadAllBytesAsync(dayFile);

        var result = await new FileMaintenanceService(options)
            .CompressAsync(new FileCompressionOptions(BackupBeforeCompression: false));

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Contains("open for writing"));
        File.Exists(dayFile + ".gz").Should().BeFalse();
        (await File.ReadAllBytesAsync(dayFile)).Should().Equal(before);
    }

    [Fact]
    public async Task SinkReopenDuringCompression_WaitsThenExtendsThePublishedArchive()
    {
        var options = new StorageOptions { RootPath = TestDataRoot };
        var batchOptions = new JsonlBatchOptions { BatchSize = 1000, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        await using (var first = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions))
        {
            await first.AppendAsync(CreateTestEvent("AAPL", 1, ClosedDay));
            await first.FlushAsync();
        }
        var dayFile = Directory.GetFiles(TestDataRoot, "*.jsonl").Should().ContainSingle().Subject;
        var original = await File.ReadAllBytesAsync(dayFile);
        await using var second = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions);
        Task? lateWrite = null;
        var service = new FileMaintenanceService(options)
        {
            CompressionCheckpoint = stage =>
            {
                if (stage != CompressionPublicationStage.StagedVerified)
                    return;
                lateWrite = Task.Run(async () =>
                {
                    await second.AppendAsync(CreateTestEvent("AAPL", 2, ClosedDay));
                    await second.FlushAsync();
                });
                // The reopen must wait on the fence rather than append to the doomed source.
                lateWrite.Wait(TimeSpan.FromMilliseconds(200)).Should().BeFalse();
            }
        };

        var result = await service.CompressAsync(new FileCompressionOptions(BackupBeforeCompression: false));
        await lateWrite!.WaitAsync(TimeSpan.FromSeconds(10));

        result.Success.Should().BeTrue();
        File.Exists(dayFile).Should().BeFalse("a late write must not recreate the plain source beside its archive");
        var archived = await DecompressAsync(dayFile + ".gz");
        archived.Take(original.Length).Should().Equal(original, "the verified source is the archive's first member");
        ReadJsonLines(archived).Should().HaveCount(2, "the late event is appended once as a further gzip member");
    }

    [Fact]
    public async Task WriteAfterCompression_ExtendsArchiveAndReplaysEachEventOnce()
    {
        var options = new StorageOptions { RootPath = TestDataRoot };
        var batchOptions = new JsonlBatchOptions { BatchSize = 1000, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        await using (var first = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions))
        {
            await first.AppendAsync(CreateTestEvent("AAPL", 1, ClosedDay));
            await first.FlushAsync();
        }
        var dayFile = Directory.GetFiles(TestDataRoot, "*.jsonl").Should().ContainSingle().Subject;
        await AtomicFileWriter.WriteAsync(dayFile + ".sha256",
            $"{Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(dayFile))).ToLowerInvariant()}  {Path.GetFileName(dayFile)}");
        var maintenance = new FileMaintenanceService(options);
        (await maintenance.CompressAsync(new FileCompressionOptions(BackupBeforeCompression: false))).Success.Should().BeTrue();

        await using (var second = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions))
        {
            await second.AppendAsync(CreateTestEvent("AAPL", 2, ClosedDay));
            await second.FlushAsync();
        }

        File.Exists(dayFile).Should().BeFalse();
        var replayed = new List<MarketEvent>();
        await foreach (var evt in new Meridian.Storage.Replay.JsonlReplayer(TestDataRoot).ReadEventsAsync())
            replayed.Add(evt);
        replayed.Select(evt => evt.Sequence).Should().Equal(1, 2);
        (await maintenance.RunHealthCheckAsync(new HealthCheckOptions(ValidateChecksums: true)))
            .Issues.Should().NotContain(issue => issue.Type == IssueType.ChecksumMismatch,
                "the archive's checksum is refreshed with its new member");
    }

    [Fact]
    public async Task ConcurrentSinksAppendingToACompressedArchive_KeepEveryBatchExactlyOnce()
    {
        var options = new StorageOptions { RootPath = TestDataRoot };
        var batchOptions = new JsonlBatchOptions { BatchSize = 1000, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        await using (var seed = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions))
        {
            await seed.AppendAsync(CreateTestEvent("AAPL", 1, ClosedDay));
            await seed.FlushAsync();
        }
        var dayFile = Directory.GetFiles(TestDataRoot, "*.jsonl").Should().ContainSingle().Subject;
        await AtomicFileWriter.WriteAsync(dayFile + ".sha256",
            $"{Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(dayFile))).ToLowerInvariant()}  {Path.GetFileName(dayFile)}");
        var maintenance = new FileMaintenanceService(options);
        (await maintenance.CompressAsync(new FileCompressionOptions(BackupBeforeCompression: false))).Success.Should().BeTrue();

        // Two independent sinks, as with the ingestion singleton and a backfill coordinator's sink.
        await using var ingestion = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions);
        await using var backfill = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions);
        var expected = new List<long> { 1 };
        for (var round = 0; round < 8; round++)
        {
            var a = 100 + round * 2;
            var b = a + 1;
            expected.AddRange([a, b]);
            await ingestion.AppendAsync(CreateTestEvent("AAPL", a, ClosedDay));
            await backfill.AppendAsync(CreateTestEvent("AAPL", b, ClosedDay));
            using var start = new Barrier(2);
            await Task.WhenAll(
                Task.Run(async () => { start.SignalAndWait(); await ingestion.FlushAsync(); }),
                Task.Run(async () => { start.SignalAndWait(); await backfill.FlushAsync(); }));
        }

        File.Exists(dayFile).Should().BeFalse();
        var replayed = new List<long>();
        await foreach (var evt in new Meridian.Storage.Replay.JsonlReplayer(TestDataRoot).ReadEventsAsync())
            replayed.Add(evt.Sequence);
        replayed.Should().BeEquivalentTo(expected, "each sink's batch is appended once and no replace drops the other's member");
        (await maintenance.RunHealthCheckAsync(new HealthCheckOptions(ValidateChecksums: true)))
            .Issues.Should().NotContain(issue => issue.Type == IssueType.ChecksumMismatch);
    }

    [Theory]
    [InlineData("Published")]
    [InlineData("PublicationSynced")]
    public async Task WriteAfterInterruptedCompressionCleanup_ExtendsTheArchiveAndReplaysEachEventOnce(string stageName)
    {
        var options = new StorageOptions { RootPath = TestDataRoot };
        var batchOptions = new JsonlBatchOptions { BatchSize = 1000, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        await using (var first = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions))
        {
            await first.AppendAsync(CreateTestEvent("AAPL", 1, ClosedDay));
            await first.FlushAsync();
        }
        var dayFile = Directory.GetFiles(TestDataRoot, "*.jsonl").Should().ContainSingle().Subject;
        await AtomicFileWriter.WriteAsync(dayFile + ".sha256",
            $"{Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(dayFile))).ToLowerInvariant()}  {Path.GetFileName(dayFile)}");
        var stage = Enum.Parse<CompressionPublicationStage>(stageName);
        var interrupted = new FileMaintenanceService(options)
        {
            CompressionCheckpoint = checkpoint =>
            {
                if (checkpoint == stage)
                    throw new IOException($"Injected interruption at {checkpoint}");
            }
        };
        (await interrupted.CompressAsync(new FileCompressionOptions(BackupBeforeCompression: false))).Success.Should().BeFalse();
        File.Exists(dayFile).Should().BeTrue("the crash left both equal representations");
        File.Exists(dayFile + ".gz").Should().BeTrue();

        await using (var second = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions))
        {
            await second.AppendAsync(CreateTestEvent("AAPL", 2, ClosedDay));
            await second.FlushAsync();
        }

        File.Exists(dayFile).Should().BeFalse("the interrupted cleanup completes before the archive is extended");
        File.Exists(dayFile + ".sha256").Should().BeFalse();
        File.Exists(dayFile + ".gz.sha256").Should().BeTrue(
            "a committed append publishes the archive's checksum even when the interrupted compression never did");
        var replayed = new List<MarketEvent>();
        await foreach (var evt in new Meridian.Storage.Replay.JsonlReplayer(TestDataRoot).ReadEventsAsync())
            replayed.Add(evt);
        replayed.Select(evt => evt.Sequence).Should().Equal(1, 2);
        var maintenance = new FileMaintenanceService(options);
        var resumed = await maintenance.CompressAsync(new FileCompressionOptions(BackupBeforeCompression: false));
        resumed.Errors.Should().BeEmpty("no retained destination is left for compression to refuse");
        (await maintenance.RunHealthCheckAsync(new HealthCheckOptions(ValidateChecksums: true)))
            .Issues.Should().NotContain(issue => issue.Type == IssueType.ChecksumMismatch || issue.Type == IssueType.MissingChecksum);
    }

    [Fact]
    public async Task RetentionSweep_RemovesAnExpiredArchiveWithItsChecksumSoTheDayCanBeRecompressed()
    {
        var archive = Path.Combine(TestDataRoot, "SPY_trade_20260102.jsonl.gz");
        await WriteArchiveWithChecksumAsync(archive, DateTime.UtcNow.AddDays(-30));
        var options = new StorageOptions { RootPath = TestDataRoot, RetentionDays = 7 };
        await using (var sink = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot)))
            sink.RunRetentionCleanup();

        File.Exists(archive).Should().BeFalse();
        File.Exists(archive + ".sha256").Should().BeFalse("a checksum must not outlive the archive it covers");

        var source = archive[..^3];
        await File.WriteAllTextAsync(source, "{\"late\":1}\n");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddHours(-1));
        var recompressed = await new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot })
            .CompressAsync(new FileCompressionOptions(BackupBeforeCompression: false));
        recompressed.Errors.Should().BeEmpty();
        recompressed.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RetentionByteBudget_CountsChecksumSidecars()
    {
        var older = Path.Combine(TestDataRoot, "SPY_trade_20260102.jsonl.gz");
        var newer = Path.Combine(TestDataRoot, "SPY_trade_20260105.jsonl.gz");
        await WriteArchiveWithChecksumAsync(older, DateTime.UtcNow.AddDays(-3));
        await WriteArchiveWithChecksumAsync(newer, DateTime.UtcNow.AddDays(-1));
        var dataBytes = new FileInfo(older).Length + new FileInfo(newer).Length;
        var sidecarBytes = new FileInfo(older + ".sha256").Length;
        // The data alone fits the budget; data plus both sidecars does not.
        var options = new StorageOptions { RootPath = TestDataRoot, MaxTotalBytes = dataBytes + sidecarBytes };
        await using (var sink = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot)))
            sink.RunRetentionCleanup();

        File.Exists(older).Should().BeFalse();
        File.Exists(older + ".sha256").Should().BeFalse();
        File.Exists(newer).Should().BeTrue();
        File.Exists(newer + ".sha256").Should().BeTrue();
    }

    private static async Task WriteArchiveWithChecksumAsync(string archive, DateTime lastWriteUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        await using (var file = File.Create(archive))
        await using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            await gzip.WriteAsync(System.Text.Encoding.UTF8.GetBytes("{\"seq\":1}\n"));
        await File.WriteAllTextAsync(archive + ".sha256",
            $"{Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive))).ToLowerInvariant()}  {Path.GetFileName(archive)}");
        File.SetLastWriteTimeUtc(archive, lastWriteUtc);
        File.SetLastWriteTimeUtc(archive + ".sha256", lastWriteUtc);
    }

    [Fact]
    public async Task ArchiveAppend_WaitsForAnotherProcessHoldingTheArchiveLock()
    {
        var options = new StorageOptions { RootPath = TestDataRoot };
        var batchOptions = new JsonlBatchOptions { BatchSize = 1000, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        await using (var first = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions))
        {
            await first.AppendAsync(CreateTestEvent("AAPL", 1, ClosedDay));
            await first.FlushAsync();
        }
        var dayFile = Directory.GetFiles(TestDataRoot, "*.jsonl").Should().ContainSingle().Subject;
        await AtomicFileWriter.WriteAsync(dayFile + ".sha256",
            $"{Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(dayFile))).ToLowerInvariant()}  {Path.GetFileName(dayFile)}");
        (await new FileMaintenanceService(options).CompressAsync(new FileCompressionOptions(BackupBeforeCompression: false))).Success.Should().BeTrue();
        var archive = dayFile + ".gz";
        // Another process holds the archive's lock exactly as an appender would: an exclusive open handle.
        var otherProcess = new FileStream(ArchiveAppendLock.PathFor(archive), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        await using var second = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions);
        await second.AppendAsync(CreateTestEvent("AAPL", 2, ClosedDay));
        var flush = second.FlushAsync();
        (await Task.WhenAny(flush, Task.Delay(TimeSpan.FromMilliseconds(400)))).Should().NotBeSameAs(flush,
            "the append must wait for the other process rather than copy the archive underneath it");
        otherProcess.Dispose();
        await flush.WaitAsync(TimeSpan.FromSeconds(10));

        var replayed = new List<MarketEvent>();
        await foreach (var evt in new Meridian.Storage.Replay.JsonlReplayer(TestDataRoot).ReadEventsAsync())
            replayed.Add(evt);
        replayed.Select(evt => evt.Sequence).Should().Equal(1, 2);
        // The lock is released with the append; the file itself stays and carries no state.
        using var reacquired = new FileStream(ArchiveAppendLock.PathFor(archive), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task ChecksumRepublishFailureAfterCommittedAppend_ReportsSuccessAndWritesOnce()
    {
        var options = new StorageOptions { RootPath = TestDataRoot };
        var batchOptions = new JsonlBatchOptions { BatchSize = 1000, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        await using (var first = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions))
        {
            await first.AppendAsync(CreateTestEvent("AAPL", 1, ClosedDay));
            await first.FlushAsync();
        }
        var dayFile = Directory.GetFiles(TestDataRoot, "*.jsonl").Should().ContainSingle().Subject;
        await AtomicFileWriter.WriteAsync(dayFile + ".sha256",
            $"{Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(dayFile))).ToLowerInvariant()}  {Path.GetFileName(dayFile)}");
        var maintenance = new FileMaintenanceService(options);
        (await maintenance.CompressAsync(new FileCompressionOptions(BackupBeforeCompression: false))).Success.Should().BeTrue();
        File.Exists(dayFile + ".gz.sha256").Should().BeTrue();

        await using (var second = new JsonlStorageSink(options, new TestStoragePolicy(TestDataRoot), batchOptions)
        {
            ArchiveChecksumFault = () => throw new IOException("injected checksum failure after commit")
        })
        {
            await second.AppendAsync(CreateTestEvent("AAPL", 2, ClosedDay));
            Func<Task> flush = () => second.FlushAsync();
            await flush.Should().NotThrowAsync("the gzip member was already committed");
            await second.FlushAsync();
        }

        var replayed = new List<MarketEvent>();
        await foreach (var evt in new Meridian.Storage.Replay.JsonlReplayer(TestDataRoot).ReadEventsAsync())
            replayed.Add(evt);
        replayed.Select(evt => evt.Sequence).Should().Equal(new long[] { 1, 2 }, "a committed append is never retried");
        File.Exists(dayFile + ".gz.sha256").Should().BeFalse("the stale checksum stays retired rather than misreporting corruption");
        var issues = (await maintenance.RunHealthCheckAsync(new HealthCheckOptions(ValidateChecksums: true))).Issues;
        issues.Should().NotContain(issue => issue.Type == IssueType.ChecksumMismatch);
        issues.Should().ContainSingle(issue => issue.Type == IssueType.MissingChecksum && issue.Path == dayFile + ".gz",
            "an archive that lost its checksum coverage must not be reported healthy indefinitely");
    }

    private static async Task<byte[]> DecompressAsync(string path)
    {
        await using var archive = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        await archive.CopyToAsync(decompressed);
        return decompressed.ToArray();
    }

    private static IReadOnlyList<string> ReadJsonLines(byte[] bytes) =>
        System.Text.Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task WriteFence_ExclusiveHolderRefusedWhileWriterHoldsLease()
    {
        var path = Path.Combine(TestDataRoot, "fence.jsonl");
        using (await JsonlWriteFence.EnterWriteAsync(path, CancellationToken.None))
            JsonlWriteFence.TryEnterExclusive(path, out _).Should().BeFalse();

        JsonlWriteFence.TryEnterExclusive(path, out var exclusive).Should().BeTrue();
        var waiting = JsonlWriteFence.EnterWriteAsync(path, CancellationToken.None).AsTask();
        await Task.Delay(50);
        waiting.IsCompleted.Should().BeFalse();
        exclusive.Dispose();
        (await waiting.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    // Scheduled compression only selects closed partition periods, so tests that compress a
    // sink's output write events on a past day (TestStoragePolicy names files by event date).
    private static readonly DateTimeOffset ClosedDay = new(2026, 1, 2, 15, 0, 0, TimeSpan.Zero);

    private static MarketEvent CreateTestEvent(string symbol, int sequence, DateTimeOffset? at = null)
    {
        var timestamp = at ?? DateTimeOffset.UtcNow;
        var trade = new Trade(
            timestamp,
            symbol,
            100m + sequence,
            100,
            AggressorSide.Buy,
            sequence);

        return MarketEvent.Trade(timestamp, symbol, trade, "TEST", sequence);
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
