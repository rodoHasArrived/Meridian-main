using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;

namespace Meridian.Benchmarks;

/// <summary>
/// Benchmarks for event pipeline throughput and latency.
/// Tests Channel{T} performance with various configurations.
///
/// Reference: docs/open-source-references.md #11 (System.Threading.Channels)
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class EventPipelineBenchmarks
{
    private MarketEvent[] _events = null!;

    [Params(1000, 10000, 100000)]
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
    public async Task BoundedChannel_Capacity50000()
    {
        var channel = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(50000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        var writer = Task.Run(async () =>
        {
            foreach (var evt in _events)
                await channel.Writer.WriteAsync(evt);
            channel.Writer.Complete();
        });

        var count = 0;
        await foreach (var evt in channel.Reader.ReadAllAsync())
            count++;

        await writer;
    }

    [Benchmark]
    public async Task BoundedChannel_Capacity10000()
    {
        var channel = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        var writer = Task.Run(async () =>
        {
            foreach (var evt in _events)
                await channel.Writer.WriteAsync(evt);
            channel.Writer.Complete();
        });

        var count = 0;
        await foreach (var evt in channel.Reader.ReadAllAsync())
            count++;

        await writer;
    }

    [Benchmark]
    public async Task BoundedChannel_DropOldest()
    {
        var channel = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(10000)
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
        await foreach (var evt in channel.Reader.ReadAllAsync())
            count++;

        await writer;
    }

    [Benchmark]
    public async Task UnboundedChannel()
    {
        var channel = Channel.CreateUnbounded<MarketEvent>(new UnboundedChannelOptions
        {
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
        await foreach (var evt in channel.Reader.ReadAllAsync())
            count++;

        await writer;
    }
}

/// <summary>
/// Benchmarks for event publishing latency.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class PublishLatencyBenchmarks
{
    private Channel<MarketEvent> _channel = null!;
    private MarketEvent _event = null!;

    [GlobalSetup]
    public void Setup()
    {
        _channel = Channel.CreateBounded<MarketEvent>(new BoundedChannelOptions(50000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });

        _event = MarketEvent.Trade(
            DateTimeOffset.UtcNow,
            "SPY",
            new Trade(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: "SPY",
                Price: 450.25m,
                Size: 100,
                Aggressor: AggressorSide.Buy,
                SequenceNumber: 12345,
                StreamId: "BENCH",
                Venue: "TEST"
            ));

        // Start a consumer
        _ = Task.Run(async () =>
        {
            await foreach (var _ in _channel.Reader.ReadAllAsync())
            {
                // Consume events
            }
        });
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _channel.Writer.Complete();
    }

    [Benchmark(Baseline = true)]
    public bool TryPublish_Sync()
    {
        return _channel.Writer.TryWrite(_event);
    }

    [Benchmark]
    public async ValueTask PublishAsync()
    {
        await _channel.Writer.WriteAsync(_event);
    }
}
