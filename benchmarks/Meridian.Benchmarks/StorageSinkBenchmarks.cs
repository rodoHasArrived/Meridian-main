using System.Buffers;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Meridian.Application.Serialization;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage.Services;

namespace Meridian.Benchmarks;

/// <summary>
/// Benchmarks for the EventBuffer swap-buffer drain strategy and storage serialization paths.
/// Identifies bottlenecks in the buffering layer between the event pipeline and disk I/O.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class EventBufferBenchmarks
{
    private MarketEvent[] _events = null!;
    private MarketEventBuffer _buffer = null!;

    [Params(100, 1000, 5000)]
    public int BatchSize;

    [GlobalSetup]
    public void Setup()
    {
        _events = GenerateTradeEvents(BatchSize);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _buffer = new MarketEventBuffer(BatchSize);
        foreach (var evt in _events)
            _buffer.Add(evt);
    }

    [Benchmark(Baseline = true)]
    public IReadOnlyList<MarketEvent> DrainAll()
    {
        return _buffer.DrainAll();
    }

    [Benchmark]
    public IReadOnlyList<MarketEvent> DrainBySymbol()
    {
        return _buffer.DrainBySymbol("SYM00");
    }

    [Benchmark]
    public IReadOnlyList<MarketEvent> Drain_Partial_Half()
    {
        return _buffer.Drain(BatchSize / 2);
    }

    private static MarketEvent[] GenerateTradeEvents(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i =>
            {
                var symbol = $"SYM{i % 50:D2}";
                return MarketEvent.Trade(
                    DateTimeOffset.UtcNow,
                    symbol,
                    new Trade(
                        Timestamp: DateTimeOffset.UtcNow,
                        Symbol: symbol,
                        Price: 100m + i * 0.01m,
                        Size: 100 + i,
                        Aggressor: i % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                        SequenceNumber: i,
                        StreamId: "BENCH",
                        Venue: "TEST"
                    ));
            })
            .ToArray();
    }
}

/// <summary>
/// Benchmarks for the Add path under contention — measures buffer ingestion throughput
/// with single-threaded and multi-threaded producers.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class EventBufferIngestionBenchmarks
{
    private MarketEvent[] _events = null!;

    [Params(1000, 10000)]
    public int EventCount;

    [GlobalSetup]
    public void Setup()
    {
        _events = Enumerable.Range(0, EventCount)
            .Select(i =>
            {
                var symbol = $"SYM{i % 100}";
                return MarketEvent.Trade(
                    DateTimeOffset.UtcNow,
                    symbol,
                    new Trade(
                        Timestamp: DateTimeOffset.UtcNow,
                        Symbol: symbol,
                        Price: 100m + i * 0.01m,
                        Size: 100 + i,
                        Aggressor: i % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                        SequenceNumber: i,
                        StreamId: "BENCH",
                        Venue: "TEST"
                    ));
            })
            .ToArray();
    }

    [Benchmark(Baseline = true)]
    public void SingleProducer_AddAndDrain()
    {
        var buffer = new MarketEventBuffer(EventCount);
        foreach (var evt in _events)
            buffer.Add(evt);
        _ = buffer.DrainAll();
    }

    [Benchmark]
    public void MultiProducer_4Threads_AddAndDrain()
    {
        var buffer = new MarketEventBuffer(EventCount);
        var chunk = EventCount / 4;

        Parallel.For(0, 4, threadIdx =>
        {
            var start = threadIdx * chunk;
            var end = Math.Min(start + chunk, EventCount);
            for (var i = start; i < end; i++)
                buffer.Add(_events[i]);
        });

        _ = buffer.DrainAll();
    }
}

/// <summary>
/// Benchmarks for batch serialization — the actual CPU-bound work inside JsonlStorageSink.FlushBufferAsync.
/// Compares sequential vs parallel serialization at different batch sizes.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class BatchSerializationBenchmarks
{
    private MarketEvent[] _events = null!;

    // Static options avoid per-iteration allocation of JsonSerializerOptions, which would
    // inflate the "Allocated" column and make reflection vs source-gen comparisons unfair.
    private static readonly JsonSerializerOptions ReflectionOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Params(100, 1000, 5000)]
    public int BatchSize;

    [GlobalSetup]
    public void Setup()
    {
        _events = Enumerable.Range(0, BatchSize)
            .Select(i =>
            {
                var symbol = $"SYM{i % 100}";
                return MarketEvent.Trade(
                    DateTimeOffset.UtcNow,
                    symbol,
                    new Trade(
                        Timestamp: DateTimeOffset.UtcNow,
                        Symbol: symbol,
                        Price: 100m + i * 0.01m,
                        Size: 100 + i,
                        Aggressor: i % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                        SequenceNumber: i,
                        StreamId: "BENCH",
                        Venue: "TEST"
                    ));
            })
            .ToArray();
    }

    [Benchmark(Baseline = true)]
    public string[] Sequential_SourceGenerated()
    {
        var lines = new string[_events.Length];
        for (var i = 0; i < _events.Length; i++)
            lines[i] = HighPerformanceJson.Serialize(_events[i]);
        return lines;
    }

    [Benchmark]
    public string[] Parallel_SourceGenerated()
    {
        var lines = new string[_events.Length];
        Parallel.For(0, _events.Length, i =>
        {
            lines[i] = HighPerformanceJson.Serialize(_events[i]);
        });
        return lines;
    }

    [Benchmark]
    public byte[][] Sequential_Utf8Bytes()
    {
        var results = new byte[_events.Length][];
        for (var i = 0; i < _events.Length; i++)
            results[i] = HighPerformanceJson.SerializeToUtf8Bytes(_events[i]);
        return results;
    }

    [Benchmark]
    public byte[][] Parallel_Utf8Bytes()
    {
        var results = new byte[_events.Length][];
        Parallel.For(0, _events.Length, i =>
        {
            results[i] = HighPerformanceJson.SerializeToUtf8Bytes(_events[i]);
        });
        return results;
    }

    /// <summary>
    /// Serializes each event to a pooled <see cref="ArrayBufferWriter{T}"/> via
    /// <see cref="HighPerformanceJson.WriteTo"/> and resets it between events.
    /// Avoids per-event string allocation; the buffer can be written directly to a
    /// <see cref="Stream"/> without a UTF-16 → UTF-8 conversion step.
    /// </summary>
    [Benchmark]
    public long Sequential_WriteTo_PooledBuffer()
    {
        var bufferWriter = new ArrayBufferWriter<byte>(512);
        using var jsonWriter = new Utf8JsonWriter(bufferWriter, new JsonWriterOptions
        {
            SkipValidation = true
        });

        long totalBytes = 0;
        for (var i = 0; i < _events.Length; i++)
        {
            HighPerformanceJson.WriteTo(jsonWriter, _events[i]);
            jsonWriter.Flush();
            totalBytes += bufferWriter.WrittenCount;
            // Reset writer and buffer for the next event (simulates writing to stream then clearing)
            jsonWriter.Reset(bufferWriter);
            bufferWriter.Clear();
        }

        return totalBytes;
    }

    [Benchmark]
    public string[] Sequential_Reflection()
    {
        var lines = new string[_events.Length];
        for (var i = 0; i < _events.Length; i++)
            lines[i] = JsonSerializer.Serialize(_events[i], ReflectionOptions);
        return lines;
    }
}
