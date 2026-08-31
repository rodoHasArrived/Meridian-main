using System.IO.Compression;
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

    private async Task WriteGzipAsync(string file, MarketEvent evt)
    {
        await using var fs = File.Create(file);
        await using var gzip = new GZipStream(fs, CompressionMode.Compress);
        await using var writer = new StreamWriter(gzip);
        await writer.WriteAsync(SerializeLine(evt));
    }

    private static MarketEvent BuildTrade(string symbol, long sequence)
    {
        var timestamp = new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero).AddSeconds(sequence);
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
