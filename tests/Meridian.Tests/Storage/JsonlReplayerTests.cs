using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Serialization;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Storage.Replay;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class JsonlReplayerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"meridian-jsonl-replayer-{Guid.NewGuid():N}");

    public JsonlReplayerTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task ReadEventsAsync_WhenPathIsSingleJsonlFile_ReplaysThatFile()
    {
        var file = Path.Combine(_tempRoot, "events.jsonl");
        var evt = BuildTrade("SPY", 1);
        await File.WriteAllTextAsync(file, SerializeLine(evt));

        var result = await ReadAllAsync(new JsonlReplayer(file));

        result.Should().ContainSingle();
        result[0].Symbol.Should().Be("SPY");
        result[0].Type.Should().Be(MarketEventType.Trade);
    }

    [Fact]
    public async Task ReadEventsAsync_WhenPathIsDirectory_ReplaysJsonlFilesInStableOrder()
    {
        var first = Path.Combine(_tempRoot, "a.jsonl");
        var second = Path.Combine(_tempRoot, "b.jsonl");
        await File.WriteAllTextAsync(second, SerializeLine(BuildTrade("MSFT", 2)));
        await File.WriteAllTextAsync(first, SerializeLine(BuildTrade("AAPL", 1)));

        var result = await ReadAllAsync(new JsonlReplayer(_tempRoot));

        result.Select(static evt => evt.Symbol).Should().Equal("AAPL", "MSFT");
    }

    [Fact]
    public async Task ReadEventsAsync_WhenFilesInterleave_MergesByFullUtcTimestampThenFileOrder()
    {
        var first = Path.Combine(_tempRoot, "a.jsonl");
        var second = Path.Combine(_tempRoot, "b.jsonl.gz");
        var timestamp = new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero);

        await File.WriteAllTextAsync(
            first,
            SerializeLine(BuildTradeAt("A-LATE", timestamp.AddTicks(9))) +
            SerializeLine(BuildTradeAt("A-TIE", timestamp.AddTicks(20))));
        await WriteGzipAsync(
            second,
            BuildTradeAt("B-EARLY", timestamp.AddTicks(1)),
            BuildTradeAt("B-TIE", timestamp.AddTicks(20)));

        var result = await ReadAllAsync(new JsonlReplayer(_tempRoot));

        result.Select(static evt => evt.Symbol).Should().Equal(
            "B-EARLY",
            "A-LATE",
            "A-TIE",
            "B-TIE");
    }

    [Fact]
    public async Task ReadEventsAsync_WhenRecordIsMalformed_FailsClosedWithFileAndLineEvidence()
    {
        var file = Path.Combine(_tempRoot, "malformed.jsonl");
        await File.WriteAllTextAsync(
            file,
            SerializeLine(BuildTrade("SPY", 1)) + "{not-json}" + Environment.NewLine);

        var act = async () => await ReadAllAsync(new JsonlReplayer(_tempRoot));

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*malformed.jsonl*line 2*");
    }

    [Fact]
    public async Task ReadEventsAsync_WhenRecordIsJsonNull_FailsClosedWithFileAndLineEvidence()
    {
        var file = Path.Combine(_tempRoot, "null-record.jsonl");
        await File.WriteAllTextAsync(file, "null" + Environment.NewLine);

        var act = async () => await ReadAllAsync(new JsonlReplayer(_tempRoot));

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*null-record.jsonl*line 1*");
    }

    [Fact]
    public async Task ReadEventsAsync_WhenOneFileContainsLateArrival_ReordersByTimestamp()
    {
        var file = Path.Combine(_tempRoot, "regression.jsonl");
        var timestamp = new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero);
        await File.WriteAllTextAsync(
            file,
            SerializeLine(BuildTradeAt("SPY", timestamp.AddTicks(2))) +
            SerializeLine(BuildTradeAt("SPY", timestamp.AddTicks(1))));

        var result = await ReadAllAsync(new JsonlReplayer(_tempRoot));

        result.Select(static evt => evt.Timestamp).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ReadEventsAsync_WhenConsumerStops_DisposesEveryPrimedFileReader()
    {
        var first = Path.Combine(_tempRoot, "a.jsonl");
        var second = Path.Combine(_tempRoot, "b.jsonl");
        await File.WriteAllTextAsync(first, SerializeLine(BuildTrade("AAPL", 1)));
        await File.WriteAllTextAsync(second, SerializeLine(BuildTrade("MSFT", 2)));

        await using (var enumerator = new JsonlReplayer(_tempRoot).ReadEventsAsync().GetAsyncEnumerator())
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
        }

        using var firstExclusive = new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var secondExclusive = new FileStream(second, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task ReadEventsAsync_WhenCancelledDuringMerge_ThrowsAndDisposesEveryReader()
    {
        var first = Path.Combine(_tempRoot, "a.jsonl");
        var second = Path.Combine(_tempRoot, "b.jsonl");
        await File.WriteAllTextAsync(first, SerializeLine(BuildTrade("AAPL", 1)));
        await File.WriteAllTextAsync(second, SerializeLine(BuildTrade("MSFT", 2)));
        using var cts = new CancellationTokenSource();

        await using (var enumerator = new JsonlReplayer(_tempRoot)
                         .ReadEventsAsync(cts.Token)
                         .GetAsyncEnumerator(cts.Token))
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            cts.Cancel();

            var act = async () => await enumerator.MoveNextAsync();
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        using var firstExclusive = new FileStream(first, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var secondExclusive = new FileStream(second, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task ReadEventsAsync_WhenPartitionCountExceedsFormerReaderCap_ReplaysAllPartitions()
    {
        for (var index = 0; index < 129; index++)
        {
            var file = Path.Combine(_tempRoot, $"{index:D3}.jsonl");
            await File.WriteAllTextAsync(file, SerializeLine(BuildTrade($"S{index}", index + 1)));
        }

        var replayed = await ReadAllAsync(new JsonlReplayer(_tempRoot));

        replayed.Should().HaveCount(129);
    }

    [Fact]
    public async Task ReadEventsAsync_WhenLateArrivalsExceedSortRun_ReplaysInBoundedTimestampOrder()
    {
        var file = Path.Combine(_tempRoot, "late-arrivals.jsonl");
        var start = new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var count = JsonlReplayer.SortRunRecordLimit + 7;
        var content = new StringBuilder();
        for (var index = count - 1; index >= 0; index--)
            content.Append(SerializeLine(BuildTradeAt("SPY", start.AddTicks(index), index + 1L)));
        await File.WriteAllTextAsync(file, content.ToString());

        var replayed = await ReadAllAsync(new JsonlReplayer(file));

        replayed.Should().HaveCount(count);
        replayed.Select(static evt => evt.Timestamp).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ReadEventsAsync_WhenPathIsGzipJsonlFile_DecompressesAndReplays()
    {
        var file = Path.Combine(_tempRoot, "events.jsonl.gz");
        await using (var fs = File.Create(file))
        await using (var gzip = new GZipStream(fs, CompressionMode.Compress))
        await using (var writer = new StreamWriter(gzip))
        {
            await writer.WriteAsync(SerializeLine(BuildTrade("QQQ", 1)));
        }

        var result = await ReadAllAsync(new JsonlReplayer(file));

        result.Should().ContainSingle();
        result[0].Symbol.Should().Be("QQQ");
    }

    [Fact]
    public async Task ReadEventsAsync_WhenFileHasGzipExtension_DecompressesAndReplays()
    {
        // The storage policy can emit a ".gzip" suffix; the reader must recognize it, not only ".gz".
        var file = Path.Combine(_tempRoot, "events.jsonl.gzip");
        await WriteGzipAsync(file, BuildTrade("IWM", 1));

        var result = await ReadAllAsync(new JsonlReplayer(file));

        result.Should().ContainSingle();
        result[0].Symbol.Should().Be("IWM");
    }

    [Fact]
    public async Task ReadEventsAsync_WhenGzipContentHasMismatchedCodecExtension_DetectsCodecFromContent()
    {
        // A JSONL file can be named with a codec suffix (e.g. .zst) while actually holding gzip bytes.
        // Detection prefers the leading magic bytes, so the file is decoded by its real content
        // rather than fed to the reader as undecoded bytes (which silently drops every line).
        var file = Path.Combine(_tempRoot, "events.jsonl.zst");
        await WriteGzipAsync(file, BuildTrade("DIA", 1));

        var result = await ReadAllAsync(new JsonlReplayer(file));

        result.Should().ContainSingle();
        result[0].Symbol.Should().Be("DIA");
    }

    [Fact]
    public async Task ReadEventsAsync_WhenRawJsonlHasCompressedExtension_ReadsAsRaw()
    {
        // TierMigrationService raw-copies non-gzip tiers, producing a .zst-named file that actually
        // holds uncompressed JSONL. With no zstd signature present, it must be read raw, not forced
        // through a decompressor (which would fail and drop every line).
        var file = Path.Combine(_tempRoot, "events.jsonl.zst");
        await File.WriteAllTextAsync(file, SerializeLine(BuildTrade("EEM", 1)));

        var result = await ReadAllAsync(new JsonlReplayer(file));

        result.Should().ContainSingle();
        result[0].Symbol.Should().Be("EEM");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private async Task WriteGzipAsync(string file, params MarketEvent[] events)
    {
        await using var fs = File.Create(file);
        await using var gzip = new GZipStream(fs, CompressionMode.Compress);
        await using var writer = new StreamWriter(gzip);
        foreach (var evt in events)
            await writer.WriteAsync(SerializeLine(evt));
    }

    private static MarketEvent BuildTrade(string symbol, long sequence)
    {
        var timestamp = new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero).AddSeconds(sequence);
        return BuildTradeAt(symbol, timestamp, sequence);
    }

    private static MarketEvent BuildTradeAt(string symbol, DateTimeOffset timestamp, long sequence = 1)
    {
        var trade = new Trade(timestamp, symbol, 100m + sequence, 10, AggressorSide.Buy, sequence, "TEST", "XNYS");
        return MarketEvent.Trade(timestamp, symbol, trade, "TEST", sequence);
    }

    private static string SerializeLine(MarketEvent evt) =>
        JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions) + Environment.NewLine;

    private static async Task<IReadOnlyList<MarketEvent>> ReadAllAsync(JsonlReplayer replayer)
    {
        var events = new List<MarketEvent>();
        await foreach (var evt in replayer.ReadEventsAsync())
            events.Add(evt);
        return events;
    }
}
