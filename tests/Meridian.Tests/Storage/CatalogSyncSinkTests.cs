using FluentAssertions;
using System.IO.Compression;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Policies;
using Meridian.Storage.Services;
using Meridian.Storage.Sinks;
using Meridian.Storage.Interfaces;
using Meridian.Contracts.Catalog;
using Moq;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class CatalogSyncSinkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"catalog-sync-{Guid.NewGuid():N}");

    [Fact]
    public async Task FlushAsync_FailedCatalogSave_IsRetriedWithoutAnotherAppend()
    {
        var options = new StorageOptions { RootPath = _root };
        var policy = new JsonlStoragePolicy(options);
        var catalog = new Mock<IStorageCatalogService>();
        catalog.Setup(c => c.GetFilesForSymbol(It.IsAny<string>())).Returns(Array.Empty<IndexedFileEntry>());
        catalog.Setup(c => c.UpdateFileEntryAsync(It.IsAny<IndexedFileEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var attempts = 0;
        catalog.Setup(c => c.SaveCatalogAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ++attempts == 1 ? Task.FromException(new IOException("synthetic save failure")) : Task.CompletedTask);
        await using var sink = new CatalogSyncSink(
            new JsonlStorageSink(options, policy, JsonlBatchOptions.NoBatching), catalog.Object, policy, _root);
        await sink.AppendAsync(CreateEvent(new DateTimeOffset(2024, 1, 2, 15, 0, 0, TimeSpan.Zero), 1));

        await sink.FlushAsync();
        attempts.Should().Be(1);
        await sink.FlushAsync();

        attempts.Should().Be(2);
        catalog.Verify(c => c.UpdateFileEntryAsync(It.IsAny<IndexedFileEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FlushAsync_MultipleWindowsAndRestart_KeepWholeFileTotalsAndRanges()
    {
        var options = new StorageOptions { RootPath = _root };
        var policy = new JsonlStoragePolicy(options);
        var catalog = new StorageCatalogService(_root, options);
        await catalog.InitializeAsync();
        var first = new DateTimeOffset(2024, 1, 2, 15, 0, 0, TimeSpan.Zero);
        await using (var sink = new CatalogSyncSink(
            new JsonlStorageSink(options, policy, JsonlBatchOptions.NoBatching), catalog, policy, _root))
        {
            await sink.AppendAsync(CreateEvent(first, 1));
            await sink.AppendAsync(CreateEvent(first.AddMinutes(1), 2));
            await sink.FlushAsync();
            await sink.AppendAsync(CreateEvent(first.AddMinutes(2), 3));
            await sink.FlushAsync();

            catalog.GetFilesForSymbol("SPY").Should().ContainSingle().Which.EventCount.Should().Be(3);
        }

        var reloaded = new StorageCatalogService(_root, options);
        await reloaded.InitializeAsync();
        reloaded.GetFilesForSymbol("SPY").Should().ContainSingle().Which.EventCount.Should().Be(3);
        await using (var sink = new CatalogSyncSink(
            new JsonlStorageSink(options, policy, JsonlBatchOptions.NoBatching), reloaded, policy, _root))
        {
            await sink.AppendAsync(CreateEvent(first.AddMinutes(3), 4));
            await sink.FlushAsync();
        }

        var entry = reloaded.GetFilesForSymbol("SPY").Should().ContainSingle().Subject;
        entry.EventCount.Should().Be(4);
        entry.FirstTimestamp.Should().Be(first.UtcDateTime);
        entry.LastTimestamp.Should().Be(first.AddMinutes(3).UtcDateTime);
        entry.FirstSequence.Should().Be(1);
        entry.LastSequence.Should().Be(4);
        File.ReadLines(Path.Combine(_root, entry.RelativePath)).Should().HaveCount(4);
    }

    private static MarketEvent CreateEvent(DateTimeOffset timestamp, long sequence)
        => MarketEvent.Trade(timestamp, "SPY",
            new Trade(timestamp, "SPY", 100m, 10, AggressorSide.Buy, sequence), "test", sequence);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FlushAsync_LateAppendToPublishedGzip_PreservesTotalsAcrossWindowsAndRestart(bool batching)
    {
        var options = new StorageOptions { RootPath = _root };
        var policy = new JsonlStoragePolicy(options);
        var batch = batching ? new JsonlBatchOptions { BatchSize = 1000, Enabled = true } : JsonlBatchOptions.NoBatching;
        var first = new DateTimeOffset(2024, 1, 2, 15, 0, 0, TimeSpan.Zero);
        var catalog = new StorageCatalogService(_root, options);
        await catalog.InitializeAsync();
        await using (var sink = new CatalogSyncSink(new JsonlStorageSink(options, policy, batch), catalog, policy, _root))
        {
            await sink.AppendAsync(CreateEvent(first, 1));
            await sink.AppendAsync(CreateEvent(first.AddMinutes(1), 2));
            await sink.FlushAsync();
        }
        var plain = Path.Combine(_root, catalog.GetFilesForSymbol("SPY").Single().RelativePath);
        var compression = await new FileMaintenanceService(options).CompressAsync(
            new FileCompressionOptions(BackupBeforeCompression: false));
        compression.Success.Should().BeTrue();
        File.Exists(plain).Should().BeFalse();

        await using (var sink = new CatalogSyncSink(new JsonlStorageSink(options, policy, batch), catalog, policy, _root))
        {
            await sink.AppendAsync(CreateEvent(first.AddMinutes(2), 3));
            await sink.FlushAsync();
            await sink.AppendAsync(CreateEvent(first.AddMinutes(3), 4));
            await sink.FlushAsync();
        }
        var reloaded = new StorageCatalogService(_root, options);
        await reloaded.InitializeAsync();
        var entry = reloaded.GetFilesForSymbol("SPY").Should().ContainSingle().Subject;
        entry.RelativePath.Should().EndWith(".jsonl.gz");
        entry.EventCount.Should().Be(4);
        entry.FirstSequence.Should().Be(1);
        entry.LastSequence.Should().Be(4);
        entry.FirstTimestamp.Should().Be(first.UtcDateTime);
        entry.LastTimestamp.Should().Be(first.AddMinutes(3).UtcDateTime);
        entry.IsCompressed.Should().BeTrue();
        using var archive = File.OpenRead(Path.Combine(_root, entry.RelativePath));
        using var gzip = new GZipStream(archive, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(4);
        File.Exists(plain).Should().BeFalse("late appends extend the archive instead of recreating the plain file");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
