using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Monitoring;
using Meridian.Core.Config;
using Meridian.Application.Monitoring;

namespace Meridian.Tests.Application.Monitoring;

public sealed class StatusWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-status-writer-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteOnceAsync_WritesReadableStatusSnapshot()
    {
        var path = Path.Combine(_root, "status.json");
        await using var writer = new StatusWriter(
            path,
            () => new AppConfig(Symbols: [new SymbolConfig("SPY", SubscribeTrades: true)]),
            new TestEventMetrics());

        await writer.WriteOnceAsync();

        File.Exists(path).Should().BeTrue();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        document.RootElement.GetProperty("symbols")[0].GetProperty("symbol").GetString().Should().Be("SPY");
        document.RootElement.GetProperty("metrics").GetProperty("published").GetInt64().Should().Be(0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestEventMetrics : IEventMetrics
    {
        public long Published => 0;
        public long Dropped => 0;
        public long Integrity => 0;
        public long Trades => 0;
        public long DepthUpdates => 0;
        public long Quotes => 0;
        public long HistoricalBars => 0;
        public double EventsPerSecond => 0;
        public double DropRate => 0;

        public void IncPublished() { }
        public void IncDropped() { }
        public void IncIntegrity() { }
        public void IncTrades() { }
        public void IncDepthUpdates() { }
        public void IncQuotes() { }
        public void IncHistoricalBars() { }
        public void RecordLatency(long startTimestamp) { }
        public void Reset() { }

        public MetricsSnapshot GetSnapshot() => new(
            Published,
            Dropped,
            Integrity,
            Trades,
            DepthUpdates,
            Quotes,
            HistoricalBars,
            EventsPerSecond,
            TradesPerSecond: 0,
            DepthUpdatesPerSecond: 0,
            HistoricalBarsPerSecond: 0,
            DropRate,
            AverageLatencyUs: 0,
            MinLatencyUs: 0,
            MaxLatencyUs: 0,
            LatencySampleCount: 0,
            Gc0Collections: 0,
            Gc1Collections: 0,
            Gc2Collections: 0,
            Gc0Delta: 0,
            Gc1Delta: 0,
            Gc2Delta: 0,
            MemoryUsageMb: 0,
            HeapSizeMb: 0,
            DateTimeOffset.UtcNow);
    }
}
