using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Sinks;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Golden-path regression test for provider normalization at the persistence boundary.
/// Exercises the adapter-facing quote update, collector normalization, event pipeline,
/// and JSONL sink together, then compares a stable JSON subset against a committed baseline.
/// </summary>
public sealed class AlpacaQuotePipelineGoldenTests : IDisposable
{
    private readonly string _root;

    public AlpacaQuotePipelineGoldenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"mdc_alpaca_quote_golden_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task AlpacaQuote_EndToEndJsonlSubset_MatchesGoldenMaster()
    {
        var options = new StorageOptions { RootPath = _root };
        var policy = new SingleFilePolicy(_root);
        await using (var sink = new JsonlStorageSink(options, policy, JsonlBatchOptions.NoBatching))
        await using (var pipeline = new EventPipeline(
            sink,
            capacity: 128,
            batchSize: 1,
            enablePeriodicFlush: false))
        {
            var collector = new QuoteCollector(pipeline);
            var timestamp = new DateTimeOffset(2026, 3, 19, 14, 30, 0, TimeSpan.Zero);
            collector.OnQuote(new MarketQuoteUpdate(
                Timestamp: timestamp,
                Symbol: "AAPL",
                BidPrice: 185.50m,
                BidSize: 500,
                AskPrice: 185.55m,
                AskSize: 300,
                StreamId: "ALPACA",
                Venue: "ALPACA"));

            await pipeline.FlushAsync();
        }

        var actualLine = File.ReadAllLines(Directory.GetFiles(_root, "*.jsonl", SearchOption.AllDirectories).Single()).Single();
        using var actualDoc = JsonDocument.Parse(actualLine);
        using var expectedDoc = JsonDocument.Parse(await File.ReadAllTextAsync(GetGoldenPath()));

        var actualSubset = ExtractStableSubset(actualDoc.RootElement);
        var expectedSubset = ExtractStableSubset(expectedDoc.RootElement);

        actualSubset.Should().BeEquivalentTo(expectedSubset);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
            return;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(_root, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(10);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(10);
            }
        }
    }

    private static object ExtractStableSubset(JsonElement root)
    {
        var payload = root.GetProperty("payload");
        return new
        {
            symbol = root.GetProperty("symbol").GetString(),
            sequence = root.GetProperty("sequence").GetInt64(),
            source = root.GetProperty("source").GetString(),
            payload = new
            {
                kind = payload.GetProperty("kind").GetString(),
                timestamp = payload.GetProperty("timestamp").GetString(),
                symbol = payload.GetProperty("symbol").GetString(),
                bidPrice = payload.GetProperty("bidPrice").GetDecimal(),
                bidSize = payload.GetProperty("bidSize").GetInt64(),
                askPrice = payload.GetProperty("askPrice").GetDecimal(),
                askSize = payload.GetProperty("askSize").GetInt64(),
                midPrice = payload.GetProperty("midPrice").GetDecimal(),
                spread = payload.GetProperty("spread").GetDecimal(),
                sequenceNumber = payload.GetProperty("sequenceNumber").GetInt64(),
                streamId = payload.GetProperty("streamId").GetString(),
                venue = payload.GetProperty("venue").GetString()
            }
        };
    }

    private static string GetGoldenPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tests", "Meridian.Tests", "TestData", "Golden", "alpaca-quote-pipeline.json");
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new FileNotFoundException("Unable to locate alpaca quote golden file.");
    }

    private sealed class SingleFilePolicy : IStoragePolicy
    {
        private readonly string _root;

        public SingleFilePolicy(string root)
        {
            _root = root;
        }

        public string GetPath(MarketEvent evt) => Path.Combine(_root, "alpaca_quote.jsonl");
    }
}
