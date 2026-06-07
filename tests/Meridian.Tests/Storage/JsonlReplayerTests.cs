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

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static MarketEvent BuildTrade(string symbol, long sequence)
    {
        var timestamp = new DateTimeOffset(2026, 1, 2, 14, 30, 0, TimeSpan.Zero).AddSeconds(sequence);
        var trade = new Trade(timestamp, symbol, 100m + sequence, 10, AggressorSide.Buy, sequence, "TEST", "XNYS");
        return MarketEvent.Trade(timestamp, symbol, trade, sequence, "TEST");
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
