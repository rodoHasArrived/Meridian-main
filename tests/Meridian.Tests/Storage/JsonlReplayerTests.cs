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
    public async Task ReadEventsAsync_WhenFilesInterleave_MergesByFullUtcTimestampThenFileAndLineOrder()
    {
        var first = Path.Combine(_tempRoot, "a.jsonl");
        var second = Path.Combine(_tempRoot, "b.jsonl.gz");
        var timestamp = new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero);

        await File.WriteAllTextAsync(
            first,
            SerializeLine(BuildTradeAt("A-LATE", timestamp.AddTicks(9))) +
            SerializeLine(BuildTradeAt("A-TIE-1", timestamp.AddTicks(20))) +
            SerializeLine(BuildTradeAt("A-TIE-2", timestamp.AddTicks(20))));
        await WriteGzipAsync(
            second,
            BuildTradeAt("B-EARLY", timestamp.AddTicks(1)),
            BuildTradeAt("B-TIE", timestamp.AddTicks(20)));

        var result = await ReadAllAsync(new JsonlReplayer(_tempRoot));

        result.Select(static evt => evt.Symbol).Should().Equal(
            "B-EARLY",
            "A-LATE",
            "A-TIE-1",
            "A-TIE-2",
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
    public async Task ReadEventsAsync_WhenConsumerStops_DisposesSourceAndSpoolReaders()
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
    public async Task ReadEventsAsync_WhenCancelledDuringReplay_ThrowsAndDisposesEveryReader()
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
    public async Task ReadEventsAsync_WhenReplayCountExceedsAdmissionLimit_AllMergeBatchesComplete()
    {
        var replayers = new List<JsonlReplayer>();
        for (var replayIndex = 0; replayIndex < JsonlReplayer.MaxConcurrentReplaySorts + 1; replayIndex++)
        {
            var replayRoot = Path.Combine(_tempRoot, $"replay-{replayIndex:D2}");
            Directory.CreateDirectory(replayRoot);
            var file = Path.Combine(replayRoot, "events.jsonl");
            var start = new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero);
            var content = new StringBuilder();
            for (var eventIndex = JsonlReplayer.MaxMergeReaders; eventIndex >= 0; eventIndex--)
            {
                content.Append(SerializeLine(BuildTradeAt(
                    $"R{replayIndex}-E{eventIndex}",
                    start.AddTicks(eventIndex),
                    eventIndex + 1L)));
            }

            await File.WriteAllTextAsync(file, content.ToString());
            replayers.Add(new JsonlReplayer(
                file,
                sortRunRecordLimit: 1,
                maxMergeReaders: JsonlReplayer.MaxMergeReaders));
        }

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replayTasks = replayers.Select(async replayer =>
        {
            await startGate.Task;
            return await ReadAllAsync(replayer);
        }).ToArray();
        startGate.SetResult();

        var results = await Task.WhenAll(replayTasks).WaitAsync(TimeSpan.FromSeconds(20));

        results.Should().AllSatisfy(events =>
        {
            events.Should().HaveCount(JsonlReplayer.MaxMergeReaders + 1);
            events.Select(static evt => evt.Timestamp).Should().BeInAscendingOrder();
        });
    }

    [Fact]
    public async Task ReadEventsAsync_WhenOuterMergePrimesMoreThanAdmissionLimit_DoesNotRetainAdmissionAcrossYield()
    {
        var enumerators = new List<IAsyncEnumerator<MarketEvent>>();
        try
        {
            for (var index = 0; index < JsonlReplayer.MaxConcurrentReplaySorts + 1; index++)
            {
                var replayRoot = Path.Combine(_tempRoot, $"prime-{index:D2}");
                Directory.CreateDirectory(replayRoot);
                var file = Path.Combine(replayRoot, "events.jsonl");
                await File.WriteAllTextAsync(file, SerializeLine(BuildTrade($"S{index}", index + 1L)));
                enumerators.Add(new JsonlReplayer(file).ReadEventsAsync().GetAsyncEnumerator());
            }

            foreach (var enumerator in enumerators)
                (await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        }
        finally
        {
            foreach (var enumerator in enumerators)
                await enumerator.DisposeAsync();
        }
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
    public async Task ReadEventsAsync_WhenEqualTimestampsCrossMultipleMergePasses_PreservesPhysicalLineOrder()
    {
        var file = Path.Combine(_tempRoot, "multi-pass-ties.jsonl");
        var start = new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero);
        var input = new[]
        {
            BuildTradeAt("TIE-1", start.AddTicks(5), 1),
            BuildTradeAt("LATE", start.AddTicks(9), 2),
            BuildTradeAt("TIE-2", start.AddTicks(5), 3),
            BuildTradeAt("EARLY-1", start.AddTicks(1), 4),
            BuildTradeAt("TIE-3", start.AddTicks(5), 5),
            BuildTradeAt("MIDDLE", start.AddTicks(3), 6),
            BuildTradeAt("TIE-4", start.AddTicks(5), 7),
            BuildTradeAt("EARLY-2", start.AddTicks(2), 8),
            BuildTradeAt("TIE-5", start.AddTicks(5), 9)
        };
        await File.WriteAllTextAsync(file, string.Concat(input.Select(SerializeLine)));

        var expected = new[]
        {
            "EARLY-1", "EARLY-2", "MIDDLE", "TIE-1", "TIE-2", "TIE-3", "TIE-4", "TIE-5", "LATE"
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var replayed = await ReadAllAsync(new JsonlReplayer(file, sortRunRecordLimit: 2, maxMergeReaders: 2));
            replayed.Select(static evt => evt.Symbol).Should().Equal(expected);
        }
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
