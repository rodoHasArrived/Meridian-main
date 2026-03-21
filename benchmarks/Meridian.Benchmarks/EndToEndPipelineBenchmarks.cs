using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Meridian.Application.Serialization;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;

namespace Meridian.Benchmarks;

/// <summary>
/// End-to-end pipeline benchmarks simulating the real hot path:
/// Channel publish → batch drain → serialize → write.
/// Identifies which stage dominates total latency.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class EndToEndPipelineBenchmarks
{
    private MarketEvent[] _events = null!;

    [Params(1000, 10000)]
    public int EventCount;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _events = new MarketEvent[EventCount];

        for (var i = 0; i < EventCount; i++)
        {
            var symbol = $"SYM{i % 50}";
            var basePrice = 100m + (decimal)(random.NextDouble() * 400);

            _events[i] = MarketEvent.Trade(
                DateTimeOffset.UtcNow.AddMilliseconds(i),
                symbol,
                new Trade(
                    Timestamp: DateTimeOffset.UtcNow.AddMilliseconds(i),
                    Symbol: symbol,
                    Price: basePrice,
                    Size: random.Next(100, 10000),
                    Aggressor: i % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                    SequenceNumber: i,
                    StreamId: "BENCH",
                    Venue: "TEST"
                ));
        }
    }

    /// <summary>
    /// Stage 1 only: Channel write + read throughput.
    /// Baseline to isolate channel overhead.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task Stage1_ChannelOnly()
    {
        var channel = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(100_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        var writer = Task.Run(() =>
        {
            foreach (var evt in _events)
                channel.Writer.TryWrite(evt);
            channel.Writer.Complete();
        });

        var count = 0;
        await foreach (var _ in channel.Reader.ReadAllAsync())
            count++;

        await writer;
    }

    /// <summary>
    /// Stage 1+2: Channel + batch drain (simulating EventPipeline consumer loop).
    /// </summary>
    [Benchmark]
    public async Task Stage1_2_Channel_BatchDrain()
    {
        var channel = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(100_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        var writer = Task.Run(() =>
        {
            foreach (var evt in _events)
                channel.Writer.TryWrite(evt);
            channel.Writer.Complete();
        });

        const int batchSize = 100;
        var batchBuffer = new List<MarketEvent>(batchSize);

        while (await channel.Reader.WaitToReadAsync())
        {
            batchBuffer.Clear();
            while (batchBuffer.Count < batchSize && channel.Reader.TryRead(out var evt))
                batchBuffer.Add(evt);
        }

        await writer;
    }

    /// <summary>
    /// Stage 1+2+3: Channel + batch drain + serialization (the CPU-bound bottleneck).
    /// Serializes events to <c>string</c> — the current production path.
    /// Compare with <see cref="Stage1_2_3_Utf8Bytes_Serialize"/> to see string vs UTF-8 byte cost.
    /// </summary>
    [Benchmark]
    public async Task Stage1_2_3_Channel_BatchDrain_Serialize()
    {
        var channel = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(100_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        var writer = Task.Run(() =>
        {
            foreach (var evt in _events)
                channel.Writer.TryWrite(evt);
            channel.Writer.Complete();
        });

        const int batchSize = 100;
        var batchBuffer = new List<MarketEvent>(batchSize);
        long totalBytes = 0;

        while (await channel.Reader.WaitToReadAsync())
        {
            batchBuffer.Clear();
            while (batchBuffer.Count < batchSize && channel.Reader.TryRead(out var evt))
                batchBuffer.Add(evt);

            // Serialize batch (mimics JsonlStorageSink.FlushBufferAsync)
            for (var i = 0; i < batchBuffer.Count; i++)
            {
                var json = HighPerformanceJson.Serialize(batchBuffer[i]);
                totalBytes += json.Length;
            }
        }

        await writer;
    }

    /// <summary>
    /// Stage 1+2+3 (UTF-8 variant): Channel + batch drain + UTF-8 byte serialization.
    /// Serializes directly to <c>byte[]</c> instead of <c>string</c> to avoid a UTF-16 →
    /// UTF-8 re-encode when writing to a <see cref="Stream"/>. Expected to allocate fewer
    /// intermediate strings than the string-based variant.
    /// </summary>
    [Benchmark]
    public async Task Stage1_2_3_Utf8Bytes_Serialize()
    {
        var channel = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(100_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        var writer = Task.Run(() =>
        {
            foreach (var evt in _events)
                channel.Writer.TryWrite(evt);
            channel.Writer.Complete();
        });

        const int batchSize = 100;
        var batchBuffer = new List<MarketEvent>(batchSize);
        long totalBytes = 0;

        while (await channel.Reader.WaitToReadAsync())
        {
            batchBuffer.Clear();
            while (batchBuffer.Count < batchSize && channel.Reader.TryRead(out var evt))
                batchBuffer.Add(evt);

            for (var i = 0; i < batchBuffer.Count; i++)
            {
                var utf8 = HighPerformanceJson.SerializeToUtf8Bytes(batchBuffer[i]);
                totalBytes += utf8.Length;
            }
        }

        await writer;
    }

    /// <summary>
    /// Full pipeline: Channel + batch drain + serialize + write to MemoryStream.
    /// Closest approximation to real throughput without disk I/O.
    /// </summary>
    [Benchmark]
    public async Task Stage1_2_3_4_FullPipeline_MemoryStream()
    {
        var channel = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(100_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        var writer = Task.Run(() =>
        {
            foreach (var evt in _events)
                channel.Writer.TryWrite(evt);
            channel.Writer.Complete();
        });

        const int batchSize = 100;
        var batchBuffer = new List<MarketEvent>(batchSize);
        using var stream = new MemoryStream(EventCount * 512);
        using var sw = new StreamWriter(stream, Encoding.UTF8, bufferSize: 65536, leaveOpen: true);

        while (await channel.Reader.WaitToReadAsync())
        {
            batchBuffer.Clear();
            while (batchBuffer.Count < batchSize && channel.Reader.TryRead(out var evt))
                batchBuffer.Add(evt);

            for (var i = 0; i < batchBuffer.Count; i++)
            {
                var json = HighPerformanceJson.Serialize(batchBuffer[i]);
                await sw.WriteLineAsync(json);
            }

            await sw.FlushAsync();
        }

        await writer;
    }
}

/// <summary>
/// Benchmarks for deduplication key generation — the SHA256 hashing overhead
/// that the PersistentDedupLedger performs on every cache-miss event.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class DedupKeyBenchmarks
{
    private MarketEvent _tradeEvent = null!;
    private MarketEvent _quoteEvent = null!;
    private string _precomputedKey = null!;
    private readonly Dictionary<string, long> _cache = new(StringComparer.Ordinal);

    [GlobalSetup]
    public void Setup()
    {
        _tradeEvent = MarketEvent.Trade(
            DateTimeOffset.UtcNow,
            "SPY",
            new Trade(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: "SPY",
                Price: 450.25m,
                Size: 100,
                Aggressor: AggressorSide.Buy,
                SequenceNumber: 12345,
                StreamId: "ALPACA",
                Venue: "NYSE"
            ));

        _quoteEvent = MarketEvent.BboQuote(
            DateTimeOffset.UtcNow,
            "SPY",
            BboQuotePayload.FromUpdate(
                timestamp: DateTimeOffset.UtcNow,
                symbol: "SPY",
                bidPrice: 450.24m,
                bidSize: 200,
                askPrice: 450.26m,
                askSize: 150,
                sequenceNumber: 12345,
                streamId: "ALPACA",
                venue: "NYSE"
            ));

        _precomputedKey = ComputeDedupKey(_tradeEvent);

        // Pre-populate cache
        for (var i = 0; i < 100_000; i++)
            _cache[$"key_{i}"] = DateTime.UtcNow.Ticks;
    }

    [Benchmark(Baseline = true)]
    public string ComputeTradeKey()
    {
        return ComputeDedupKey(_tradeEvent);
    }

    [Benchmark]
    public string ComputeQuoteKey()
    {
        return ComputeDedupKey(_quoteEvent);
    }

    [Benchmark]
    public bool CacheLookup_Hit()
    {
        _cache[_precomputedKey] = DateTime.UtcNow.Ticks;
        return _cache.ContainsKey(_precomputedKey);
    }

    [Benchmark]
    public bool CacheLookup_Miss()
    {
        return _cache.ContainsKey("nonexistent_key_that_will_never_match");
    }

    /// <summary>
    /// Simulates the dedup key computation from PersistentDedupLedger.
    /// Format: {Source}:{Symbol}:{Type}:{SHA256(identity fields)}
    /// </summary>
    private static string ComputeDedupKey(MarketEvent evt)
    {
        var identity = evt.Payload switch
        {
            Trade trade => $"{trade.Timestamp:O}|{trade.Price}|{trade.Size}|{trade.Aggressor}|{trade.Venue}",
            BboQuotePayload bbo => $"{bbo.Timestamp:O}|{bbo.BidPrice}|{bbo.AskPrice}|{bbo.BidSize}|{bbo.AskSize}",
            _ => $"seq:{evt.Sequence}"
        };

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        var hashHex = Convert.ToHexString(hash);

        return $"{evt.Source}:{evt.EffectiveSymbol}:{evt.Type}:{hashHex}";
    }
}

/// <summary>
/// Benchmarks for MarketEvent record creation and mutation patterns.
/// Measures the cost of record `with` expressions used throughout the pipeline.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class MarketEventCreationBenchmarks
{
    private MarketEvent _baseEvent = null!;

    [GlobalSetup]
    public void Setup()
    {
        _baseEvent = MarketEvent.Trade(
            DateTimeOffset.UtcNow,
            "SPY",
            new Trade(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: "SPY",
                Price: 450.25m,
                Size: 100,
                Aggressor: AggressorSide.Buy,
                SequenceNumber: 12345,
                StreamId: "ALPACA",
                Venue: "NYSE"
            ));
    }

    /// <summary>
    /// Static factory method — creates a new MarketEvent from scratch.
    /// </summary>
    [Benchmark(Baseline = true)]
    public MarketEvent CreateViaFactory()
    {
        return MarketEvent.Trade(
            DateTimeOffset.UtcNow,
            "SPY",
            new Trade(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: "SPY",
                Price: 450.25m,
                Size: 100,
                Aggressor: AggressorSide.Buy,
                SequenceNumber: 12345,
                StreamId: "ALPACA",
                Venue: "NYSE"
            ));
    }

    /// <summary>
    /// Record `with` — the pattern used by EventCanonicalizer and StampReceiveTime.
    /// </summary>
    [Benchmark]
    public MarketEvent MutateWithExpression()
    {
        return _baseEvent with
        {
            CanonicalSymbol = "SPY",
            CanonicalVenue = "XNYS",
            CanonicalizationVersion = 1,
            Tier = MarketEventTier.Enriched
        };
    }

    /// <summary>
    /// StampReceiveTime — called on every incoming event to record receive timestamps.
    /// </summary>
    [Benchmark]
    public MarketEvent StampReceiveTime()
    {
        return _baseEvent.StampReceiveTime(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Chained mutations — canonicalize then stamp (realistic two-stage enrichment).
    /// </summary>
    [Benchmark]
    public MarketEvent ChainedMutations()
    {
        var stamped = _baseEvent.StampReceiveTime(DateTimeOffset.UtcNow);
        return stamped with
        {
            CanonicalSymbol = "SPY",
            CanonicalVenue = "XNYS",
            CanonicalizationVersion = 1,
            Tier = MarketEventTier.Processed
        };
    }
}
