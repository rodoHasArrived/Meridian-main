using System.Reflection;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Sinks;
using Meridian.Storage.Services;
using Meridian.Tests.Infrastructure;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Tests for <see cref="ParquetStorageSink"/> covering the atomic temp-write-then-rename
/// pattern, hourly part files and their compaction into the day file, final flush on
/// disposal, and dispose guard. Flushes write to the current hour's part file; the day file
/// appears once the hour closes (<see cref="CloseHourAsync"/>) or the sink is disposed.
/// </summary>
public sealed class ParquetStorageSinkTests : TempDirectoryAsyncTestBase
{
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero));
    private ParquetStorageSink? _sink;

    [Theory]
    [InlineData(FileNamingConvention.Flat, "SPY")]
    [InlineData(FileNamingConvention.Flat, "BRK/B")]
    [InlineData(FileNamingConvention.BySymbol, "SPY")]
    [InlineData(FileNamingConvention.ByDate, "SPY")]
    [InlineData(FileNamingConvention.ByType, "SPY")]
    [InlineData(FileNamingConvention.BySource, "SPY")]
    [InlineData(FileNamingConvention.ByAssetClass, "SPY")]
    [InlineData(FileNamingConvention.Hierarchical, "SPY")]
    [InlineData(FileNamingConvention.Canonical, "SPY")]
    public async Task FlushAsync_HistoricalV2Files_RemainDiscoverableByCatalogDateFilter(
        FileNamingConvention convention, string symbol)
    {
        var options = new StorageOptions { RootPath = TestDataRoot, NamingConvention = convention };
        _sink = new ParquetStorageSink(options, new ParquetStorageOptions
        {
            CompressionMethod = CompressionMethod.None,
            FlushInterval = TimeSpan.FromHours(1)
        });
        var date = new DateOnly(2026, 11, 20);
        foreach (var session in new[] { date, date.AddDays(1) })
        {
            var bar = new HistoricalBar(symbol, session, 100, 101, 99, 100, 1_000,
                "fixture", PriceBasis: HistoricalPriceBasis.SplitAdjusted);
            await _sink.AppendAsync(MarketEvent.HistoricalBar(bar.ToTimestampUtc(), bar.Symbol, bar, bar.Source));
        }
        await _sink.FlushAsync();
        var catalog = new StorageCatalogService(TestDataRoot, options);
        await catalog.InitializeAsync();
        var rebuilt = await catalog.RebuildCatalogAsync();

        rebuilt.Success.Should().BeTrue(string.Join("; ", rebuilt.Errors));
        var retained = catalog.SearchFiles(new CatalogSearchCriteria
        {
            Symbols = [symbol],
            EventTypes = ["HistoricalBar"],
            StartDate = date.ToDateTime(TimeOnly.MinValue),
            EndDate = date.ToDateTime(TimeOnly.MinValue)
        }).Should().ContainSingle().Which;
        retained.Date.Should().Be(date.ToDateTime(TimeOnly.MinValue));
        retained.EventType.Should().Be("historicalbar");
        retained.FileName.Should().Contain("_historicalbar_v2_");
    }

    [Theory]
    [InlineData(HistoricalPriceBasis.Unknown)]
    [InlineData(HistoricalPriceBasis.Raw)]
    [InlineData(HistoricalPriceBasis.SplitAdjusted)]
    [InlineData(HistoricalPriceBasis.DividendAdjusted)]
    [InlineData(HistoricalPriceBasis.FullyAdjusted)]
    public async Task FlushAsync_HistoricalBars_RetainsExplicitPriceBasis(HistoricalPriceBasis basis)
    {
        _sink = CreateSink();
        var bar = new HistoricalBar("SPY", new DateOnly(2026, 11, 20), 100, 101, 99, 100, 1_000,
            "fixture", PriceBasis: basis);
        await _sink.AppendAsync(MarketEvent.HistoricalBar(bar.ToTimestampUtc(), bar.Symbol, bar, bar.Source));

        await _sink.FlushAsync();

        var file = Directory.GetFiles(TestDataRoot, "*historicalbar_v2_*.parquet", SearchOption.AllDirectories).Single();
        (await ReadStringColumnAsync(file, "PriceBasis")).Should().Equal(basis.ToString());
        await using var stream = File.OpenRead(file);
        using var reader = await ParquetReader.CreateAsync(stream);
        using var rowGroup = reader.OpenRowGroupReader(0);
        var field = reader.Schema.GetDataFields().Single(item => item.Name == "IsAdjusted");
        var column = await rowGroup.ReadColumnAsync(field);
        column.Data.Should().BeAssignableTo<bool?[]>().Subject.Should().Equal(bar.IsAdjusted);
    }

    [Fact]
    public async Task FlushAsync_NewHistoricalBarSchema_PreservesExistingLegacyFile()
    {
        _sink = CreateSink();
        var bar = new HistoricalBar("SPY", new DateOnly(2026, 11, 20), 100, 101, 99, 100, 1_000,
            "fixture", PriceBasis: HistoricalPriceBasis.SplitAdjusted);
        var item = MarketEvent.HistoricalBar(bar.ToTimestampUtc(), bar.Symbol, bar, bar.Source);
        await _sink.AppendAsync(item);
        await _sink.FlushAsync();
        var currentFile = Directory.GetFiles(TestDataRoot, "*historicalbar_v2_*.parquet", SearchOption.AllDirectories).Single();
        var legacyFile = currentFile.Replace("_historicalbar_v2_", "_historicalbar_", StringComparison.Ordinal);
        await WriteLegacyBarFileAsync(legacyFile, bar);
        var retainedBytes = await File.ReadAllBytesAsync(legacyFile);

        await _sink.AppendAsync(item);
        await _sink.FlushAsync();

        (await File.ReadAllBytesAsync(legacyFile)).Should().Equal(retainedBytes,
            "the additive schema must not rewrite or quarantine an existing unclassified history");
        (await CountRowsAsync(legacyFile)).Should().Be(1);
        (await CountRowsAsync(currentFile)).Should().Be(2);
        Directory.GetFiles(TestDataRoot, "*.corrupt-*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task FlushAsync_ReleasesEmptyBuffersAndLateWritesPreserveEarlierRows()
    {
        _sink = CreateSink();
        for (var day = 1; day <= 10; day++)
            await _sink.AppendAsync(CreateTradeEvent("SPY") with
            {
                Timestamp = new DateTimeOffset(2024, 1, day, 15, 0, 0, TimeSpan.Zero)
            });
        var buffers = (System.Collections.IDictionary)typeof(ParquetStorageSink)
            .GetField("_buffers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_sink)!;
        buffers.Count.Should().Be(10);

        await _sink.FlushAsync();
        buffers.Count.Should().Be(0);
        await _sink.AppendAsync(CreateTradeEvent("SPY") with
        {
            Timestamp = new DateTimeOffset(2024, 1, 1, 16, 0, 0, TimeSpan.Zero)
        });
        await _sink.FlushAsync();

        buffers.Count.Should().Be(0);
        await CloseHourAsync();
        var firstDay = Directory.GetFiles(TestDataRoot, "*20240101.parquet", SearchOption.AllDirectories).Single();
        (await CountRowsAsync(firstDay)).Should().Be(2);
    }

    [Fact]
    public async Task FlushAsync_OffsetCrossingMidnight_UsesUtcDateForFilenameAndDirectory()
    {
        _sink = CreateSink();
        await _sink.AppendAsync(CreateTradeEvent("SPY") with
        {
            Timestamp = new DateTimeOffset(2024, 1, 2, 0, 30, 0, TimeSpan.FromHours(2))
        });

        await _sink.FlushAsync();
        await CloseHourAsync();

        var file = Directory.GetFiles(TestDataRoot, "*.parquet", SearchOption.AllDirectories).Single();
        Path.GetFileName(file).Should().Contain("20240101");
        Path.GetFileName(file).Should().NotContain("20240102");
    }

    [Theory]
    [InlineData("BRK/B")]
    [InlineData("../ESCAPE")]
    public async Task FlushAsync_SymbolWithPathCharacters_WritesOneEncodedFileInsideTheRoot(string symbol)
    {
        _sink = CreateSink();
        await _sink.AppendAsync(CreateTradeEvent(symbol));

        await _sink.FlushAsync();
        await CloseHourAsync();

        var root = Path.GetFullPath(TestDataRoot);
        var parent = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar))!;
        Directory.GetFiles(parent, "*ESCAPE*.parquet", SearchOption.TopDirectoryOnly)
            .Should().BeEmpty("a symbol must never place a file outside the storage root");

        var file = Directory.GetFiles(root, "*.parquet", SearchOption.AllDirectories).Single();
        Path.GetFullPath(file).Should().StartWith(root);
        Path.GetFileName(file).Should().StartWith("~", "unsafe symbols are hex-encoded into one path segment");
        Directory.GetParent(file)!.Name.Should().NotBe("BRK", "a '/' in the symbol must not create a subfolder");
    }

    public override async Task DisposeAsync()
    {
        if (_sink is not null)
            await _sink.DisposeAsync();

        await base.DisposeAsync();
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
        await CloseHourAsync();

        // Assert – at least one .parquet file was created under testRoot
        var parquetFiles = Directory.GetFiles(TestDataRoot, "*.parquet", SearchOption.AllDirectories);
        parquetFiles.Should().NotBeEmpty("a Parquet file must be written after FlushAsync");

        // No stray .tmp files should remain (atomic write pattern)
        var tmpFiles = Directory.GetFiles(TestDataRoot, "*.tmp", SearchOption.AllDirectories);
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
        var parquetFiles = Directory.GetFiles(TestDataRoot, "*.parquet", SearchOption.AllDirectories);
        parquetFiles.Should().NotBeEmpty("DisposeAsync must flush buffered events to disk");
    }

    [Fact]
    public async Task FlushAsync_WithL2SnapshotEvents_CreatesParquetFileWithSerializedBookLevels()
    {
        _sink = CreateSink(bufferSize: 10000);
        await _sink.AppendAsync(CreateL2SnapshotEvent("QQQ"));

        await _sink.FlushAsync();
        await CloseHourAsync();

        var parquetFile = Directory.GetFiles(TestDataRoot, "*l2snapshot*.parquet", SearchOption.AllDirectories)
            .Should().ContainSingle("an L2 snapshot flush should create a dedicated Parquet file")
            .Subject;

        var bidsJson = await ReadStringColumnAsync(parquetFile, "BidsJson");
        var asksJson = await ReadStringColumnAsync(parquetFile, "AsksJson");

        bidsJson.Should().ContainSingle().Which.Should().Contain("\"price\":500.10");
        bidsJson.Should().ContainSingle().Which.Should().Contain("\"marketMaker\":\"MM1\"");
        asksJson.Should().ContainSingle().Which.Should().Contain("\"price\":500.15");
        asksJson.Should().ContainSingle().Which.Should().Contain("\"marketMaker\":\"MM2\"");

        Directory.GetFiles(TestDataRoot, "*.tmp", SearchOption.AllDirectories)
            .Should().BeEmpty("successful L2 flushes should leave no temp files behind");
    }

    [Fact]
    public async Task DisposeAsync_WithBufferedL2Snapshots_FlushesFinalSnapshots()
    {
        _sink = CreateSink(bufferSize: 10000);
        await _sink.AppendAsync(CreateL2SnapshotEvent("IWM"));

        await _sink.DisposeAsync();
        _sink = null;

        Directory.GetFiles(TestDataRoot, "*l2snapshot*.parquet", SearchOption.AllDirectories)
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

        Directory.GetFiles(TestDataRoot, "*.parquet*", SearchOption.AllDirectories)
            .Should().BeEmpty("a failed L2 flush must not commit a partial file");

        await _sink.FlushAsync();

        writeAttempts.Should().Be(2, "the retry should perform a second atomic-write attempt");
        Directory.GetFiles(TestDataRoot, "*l2snapshot*" + ParquetStorageSink.PartFileExtension, SearchOption.AllDirectories)
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

        Directory.GetFiles(TestDataRoot, "*.parquet*", SearchOption.AllDirectories)
            .Should().BeEmpty("a cancelled L2 flush must not commit a file before retry");

        await _sink.FlushAsync();

        writeAttempts.Should().Be(2, "retry should still be possible after cancellation");
        Directory.GetFiles(TestDataRoot, "*l2snapshot*" + ParquetStorageSink.PartFileExtension, SearchOption.AllDirectories)
            .Should().ContainSingle("cancelled L2 snapshots should remain buffered for a later retry");
    }

    [Fact]
    public async Task FlushAsync_TwiceOnSameDay_PreservesEventsFromEarlierFlush()
    {
        // Two flushes for the same symbol/type/day target the same deterministic file;
        // the second must append a row group, not replace the file.
        _sink = CreateSink(bufferSize: 10000);
        await _sink.AppendAsync(CreateTradeEvent("SPY"));
        await _sink.FlushAsync();

        await _sink.AppendAsync(CreateTradeEvent("SPY"));
        await _sink.FlushAsync();
        await CloseHourAsync();

        var parquetFile = Directory.GetFiles(TestDataRoot, "*trade*.parquet", SearchOption.AllDirectories)
            .Should().ContainSingle("same-day flushes must share one deterministic day file")
            .Subject;

        (await CountRowsAsync(parquetFile)).Should().Be(2,
            "the second same-day flush must not erase events written by the first");
        (await CountRowGroupsAsync(parquetFile)).Should().Be(1,
            "an hour's flushes are merged into one row group instead of one per flush");
    }

    [Fact]
    public async Task FlushAsync_WhenExistingDayFileIsUnreadable_QuarantinesItInsteadOfOverwriting()
    {
        _sink = CreateSink(bufferSize: 10000);
        await _sink.AppendAsync(CreateTradeEvent("SPY"));
        await _sink.FlushAsync();
        await CloseHourAsync();

        var parquetFile = Directory.GetFiles(TestDataRoot, "*trade*.parquet", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(parquetFile, "not a parquet file");

        await _sink.AppendAsync(CreateTradeEvent("SPY"));
        await _sink.FlushAsync();
        await CloseHourAsync();

        Directory.GetFiles(TestDataRoot, "*.corrupt-*", SearchOption.AllDirectories)
            .Should().ContainSingle("the unreadable day file must be preserved for recovery, never destroyed");
        (await CountRowsAsync(parquetFile)).Should().Be(1,
            "a fresh day file should hold the newly flushed event");
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
        var destination = Path.Combine(TestDataRoot, "atomic", "failure.parquet");
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
        var destination = Path.Combine(TestDataRoot, "atomic", "cancelled.parquet");
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

    [Fact]
    public async Task FlushAsync_WithinAnHour_WritesOnlyAPartFile_NotTheDayFile()
    {
        _sink = CreateSink();
        await _sink.AppendAsync(CreateTradeEvent("SPY"));
        await _sink.FlushAsync();
        await _sink.AppendAsync(CreateTradeEvent("SPY"));
        await _sink.FlushAsync();

        Directory.GetFiles(TestDataRoot, "*.parquet", SearchOption.AllDirectories)
            .Should().BeEmpty("day files are only touched when an hour's part is merged");
        var part = Directory.GetFiles(TestDataRoot, "*" + ParquetStorageSink.PartFileExtension, SearchOption.AllDirectories)
            .Should().ContainSingle("both flushes in the hour append to one part file")
            .Subject;
        (await CountRowsAsync(part)).Should().Be(2);
    }

    [Fact]
    public async Task HourClose_MergesPartIntoDayFile_DeletesPart_AndRecordsIt()
    {
        _sink = CreateSink();
        await _sink.AppendAsync(CreateTradeEvent("SPY"));
        await _sink.FlushAsync();
        var partName = Path.GetFileName(Directory.GetFiles(TestDataRoot, "*" + ParquetStorageSink.PartFileExtension, SearchOption.AllDirectories).Single());

        await CloseHourAsync();

        Directory.GetFiles(TestDataRoot, "*" + ParquetStorageSink.PartFileExtension, SearchOption.AllDirectories)
            .Should().BeEmpty("a merged part is deleted");
        var dayFile = Directory.GetFiles(TestDataRoot, "*trade*.parquet", SearchOption.AllDirectories).Single();
        (await ReadCustomMetadataAsync(dayFile))[ParquetStorageSink.CompactedPartsMetadataKey]
            .Should().Contain(partName);
    }

    [Fact]
    public async Task SeparateHours_AppendOneRowGroupEach_AndKeepEarlierRows()
    {
        _sink = CreateSink();
        await _sink.AppendAsync(CreateTradeEvent("SPY"));
        await _sink.FlushAsync();
        await CloseHourAsync();

        await _sink.AppendAsync(CreateTradeEvent("SPY"));
        await _sink.AppendAsync(CreateTradeEvent("SPY"));
        await _sink.FlushAsync();
        await CloseHourAsync();

        var dayFile = Directory.GetFiles(TestDataRoot, "*trade*.parquet", SearchOption.AllDirectories).Single();
        (await CountRowsAsync(dayFile)).Should().Be(3);
        (await CountRowGroupsAsync(dayFile)).Should().Be(2, "one row group per merged hour");
        (await ReadCustomMetadataAsync(dayFile))[ParquetStorageSink.CompactedPartsMetadataKey]
            .Split('|').Should().HaveCount(2, "the footer keeps every merged part name across appends");
    }

    [Fact]
    public async Task PartAlreadyMergedBeforeACrash_IsDeletedWithoutMergingTwice()
    {
        _sink = CreateSink();
        await _sink.AppendAsync(CreateTradeEvent("SPY"));
        await _sink.FlushAsync();
        var part = Directory.GetFiles(TestDataRoot, "*" + ParquetStorageSink.PartFileExtension, SearchOption.AllDirectories).Single();
        var partBytes = await File.ReadAllBytesAsync(part);
        await CloseHourAsync();
        var dayFile = Directory.GetFiles(TestDataRoot, "*trade*.parquet", SearchOption.AllDirectories).Single();

        // Simulate a crash after the day file was replaced but before the part was deleted, then
        // a restart that adopts the leftover part.
        await File.WriteAllBytesAsync(part, partBytes);
        File.SetLastWriteTimeUtc(part, DateTime.UtcNow.AddHours(-3));
        await _sink.DisposeAsync();
        _sink = new ParquetStorageSink(
            new StorageOptions { RootPath = TestDataRoot },
            new ParquetStorageOptions { CompressionMethod = CompressionMethod.None, FlushInterval = TimeSpan.FromHours(1) },
            InvokeWriteAtomicallyAsync,
            policy: null,
            timeProvider: _clock);
        await _sink.FlushAsync();

        File.Exists(part).Should().BeFalse();
        (await CountRowsAsync(dayFile)).Should().Be(1, "the part's rows were already in the day file");
    }

    [Fact]
    public async Task OrphanPartFromAStoppedInstance_IsAdoptedOnceOldEnough()
    {
        var crashed = CreateSink();
        await crashed.AppendAsync(CreateTradeEvent("SPY"));
        await crashed.FlushAsync();
        var part = Directory.GetFiles(TestDataRoot, "*" + ParquetStorageSink.PartFileExtension, SearchOption.AllDirectories).Single();
        // A process that died never disposes its sink; take the part over with a fresh instance.
        _sink = CreateSink();

        await _sink.FlushAsync();
        File.Exists(part).Should().BeTrue("a part younger than the orphan age may still be live in another process");

        File.SetLastWriteTimeUtc(part, DateTime.UtcNow.AddHours(-3));
        await CloseHourAsync();

        File.Exists(part).Should().BeFalse();
        var dayFile = Directory.GetFiles(TestDataRoot, "*trade*.parquet", SearchOption.AllDirectories).Single();
        (await CountRowsAsync(dayFile)).Should().Be(1);
        GC.KeepAlive(crashed);
    }

    [Fact]
    public void TryGetDayPath_RecoversTheDayFileFromAPartName()
    {
        var day = Path.Combine("root", "SPY", "SPY_trade_20260105.parquet");

        ParquetStorageSink.TryGetDayPath(day + ".h10.abcd1234-7" + ParquetStorageSink.PartFileExtension).Should().Be(day);
        ParquetStorageSink.TryGetDayPath(day).Should().BeNull();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static async Task WriteLegacyBarFileAsync(string path, HistoricalBar bar)
    {
        var schema = new ParquetSchema(
            new DataField<DateTime>("Timestamp"),
            new DataField<string>("Symbol"),
            new DataField<decimal>("Open"),
            new DataField<decimal>("High"),
            new DataField<decimal>("Low"),
            new DataField<decimal>("Close"),
            new DataField<decimal>("Volume"),
            new DataField<long>("SequenceNumber"),
            new DataField<string>("Source"));
        var columns = new[]
        {
            new DataColumn(schema.DataFields[0], new[] { bar.ToTimestampUtc().UtcDateTime }),
            new DataColumn(schema.DataFields[1], new[] { bar.Symbol }),
            new DataColumn(schema.DataFields[2], new[] { bar.Open }),
            new DataColumn(schema.DataFields[3], new[] { bar.High }),
            new DataColumn(schema.DataFields[4], new[] { bar.Low }),
            new DataColumn(schema.DataFields[5], new[] { bar.Close }),
            new DataColumn(schema.DataFields[6], new[] { (decimal)bar.Volume }),
            new DataColumn(schema.DataFields[7], new[] { bar.SequenceNumber }),
            new DataColumn(schema.DataFields[8], new[] { bar.Source })
        };
        await using var stream = File.Create(path);
        using var writer = await ParquetWriter.CreateAsync(schema, stream);
        using var rowGroup = writer.CreateRowGroup();
        foreach (var column in columns)
            await rowGroup.WriteColumnAsync(column);
    }

    private ParquetStorageSink CreateSink(
        int bufferSize = 10000,
        Func<string, Func<Stream, Task>, CancellationToken, Task>? writeAtomicallyAsync = null) =>
        new(
            new StorageOptions { RootPath = TestDataRoot },
            new ParquetStorageOptions
            {
                BufferSize = bufferSize,
                CompressionMethod = CompressionMethod.None,
                FlushInterval = TimeSpan.FromHours(1) // disable periodic flush in tests
            },
            writeAtomicallyAsync ?? InvokeWriteAtomicallyAsync,
            policy: null,
            timeProvider: _clock);

    /// <summary>Advances past the current hour and flushes, which merges closed parts into day files.</summary>
    private async Task CloseHourAsync()
    {
        _clock.Advance(TimeSpan.FromHours(1));
        await _sink!.FlushAsync();
    }

    private static async Task<int> CountRowGroupsAsync(string parquetPath)
    {
        await using var stream = File.OpenRead(parquetPath);
        using var reader = await ParquetReader.CreateAsync(stream);
        return reader.RowGroupCount;
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadCustomMetadataAsync(string parquetPath)
    {
        await using var stream = File.OpenRead(parquetPath);
        using var reader = await ParquetReader.CreateAsync(stream);
        return new Dictionary<string, string>(reader.CustomMetadata);
    }

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

    private static async Task<long> CountRowsAsync(string parquetPath)
    {
        await using var stream = File.OpenRead(parquetPath);
        using var reader = await ParquetReader.CreateAsync(stream);
        var rows = 0L;
        for (var i = 0; i < reader.RowGroupCount; i++)
        {
            using var rowGroup = reader.OpenRowGroupReader(i);
            rows += rowGroup.RowCount;
        }

        return rows;
    }

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

    /// <summary>Clock the tests move across hour boundaries; Storage.Tests has no fake time package.</summary>
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
