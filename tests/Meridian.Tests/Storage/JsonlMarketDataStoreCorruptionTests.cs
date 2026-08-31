using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Store;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Storage.Store;
using Meridian.Tests.Infrastructure;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Read-side corruption disclosure for <see cref="JsonlMarketDataStore"/>: malformed JSONL lines
/// and torn compressed tails are read-time discoveries with no ingress pipeline to publish
/// through, so the store counts them (per-instance counters plus process-wide Prometheus series,
/// mirroring WAL recovery) instead of silently swallowing them. Intact events must still be
/// returned — disclosure never degrades the readable portion of the tape.
/// </summary>
public sealed class JsonlMarketDataStoreCorruptionTests : TempDirectoryTestBase
{
    [Fact]
    public async Task QueryAsync_MalformedLine_SkipsItReturnsIntactEventsAndCountsCorruption()
    {
        var lines = new[]
        {
            SerializeEvent(CreateTrade("AAPL", sequence: 1)),
            "{ this is not a valid market event",
            SerializeEvent(CreateTrade("AAPL", sequence: 2))
        };
        await File.WriteAllLinesAsync(Path.Combine(TestDataRoot, "AAPL_trade_data.jsonl"), lines);
        var store = new JsonlMarketDataStore(TestDataRoot);

        var events = await QueryAllAsync(store);

        events.Should().HaveCount(2, "both intact lines must still be returned");
        events.Select(e => e.Sequence).Should().BeEquivalentTo([1L, 2L]);
        store.MalformedLinesSkipped.Should().Be(1,
            "a skipped line means query results are incomplete and must be disclosed, not swallowed");
        store.TruncatedTailsDetected.Should().Be(0);
    }

    [Fact]
    public async Task QueryAsync_CleanFile_ReportsZeroCorruption()
    {
        await File.WriteAllLinesAsync(
            Path.Combine(TestDataRoot, "AAPL_trade_data.jsonl"),
            [SerializeEvent(CreateTrade("AAPL", sequence: 1))]);
        var store = new JsonlMarketDataStore(TestDataRoot);

        var events = await QueryAllAsync(store);

        events.Should().ContainSingle();
        store.MalformedLinesSkipped.Should().Be(0);
        store.TruncatedTailsDetected.Should().Be(0);
    }

    [Fact]
    public async Task QueryAsync_TruncatedCompressedTail_YieldsCompleteBlocksAndCountsTear()
    {
        // A crash mid-append can leave a torn trailing gzip member behind a complete one.
        var completeMember = GzipMember(SerializeEvent(CreateTrade("AAPL", sequence: 1)) + "\n");
        var fullSecondMember = GzipMember(SerializeEvent(CreateTrade("AAPL", sequence: 2)) + "\n");
        var tornTail = fullSecondMember[..(fullSecondMember.Length / 2)];
        await File.WriteAllBytesAsync(
            Path.Combine(TestDataRoot, "AAPL_trade_data.jsonl.gz"),
            [.. completeMember, .. tornTail]);
        var store = new JsonlMarketDataStore(TestDataRoot);

        var events = await QueryAllAsync(store);

        events.Should().ContainSingle("every complete block before the tear must still be returned")
            .Which.Sequence.Should().Be(1);
        store.TruncatedTailsDetected.Should().Be(1,
            "a torn tail means events past the tear were lost and must be disclosed, not treated as clean EOF");
    }

    private static string SerializeEvent(MarketEvent evt)
        => JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions);

    private static byte[] GzipMember(string content)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            gzip.Write(bytes, 0, bytes.Length);
        }

        return buffer.ToArray();
    }

    private static async Task<IReadOnlyList<MarketEvent>> QueryAllAsync(JsonlMarketDataStore store)
    {
        var results = new List<MarketEvent>();
        await foreach (var evt in store.QueryAsync(new MarketDataQuery()))
        {
            results.Add(evt);
        }

        return results;
    }

    private static MarketEvent CreateTrade(string symbol, int sequence)
    {
        var timestamp = new DateTimeOffset(2026, 8, 24, 12, 0, sequence, TimeSpan.Zero);
        var trade = new Trade(
            timestamp,
            symbol,
            100m + sequence,
            100,
            AggressorSide.Buy,
            sequence);

        return MarketEvent.Trade(timestamp, symbol, trade, "TEST", sequence);
    }
}
