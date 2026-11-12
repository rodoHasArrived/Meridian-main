using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Contracts.Monitoring;
using Meridian.Core.Config;
using Meridian.Application.Monitoring;

namespace Meridian.Tests.Application.Monitoring;

public sealed class StatusWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-status-writer-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Start_TransientIoFailureRetriesAtNextInterval()
    {
        Directory.CreateDirectory(_root);
        var blockedDirectory = Path.Combine(_root, "blocked");
        File.WriteAllText(blockedDirectory, "a file temporarily occupies the output directory");
        var path = Path.Combine(blockedDirectory, "status.json");
        var clock = new ControlledDelayClock();
        await using var writer = new StatusWriter(path, () => new AppConfig(), timeProvider: clock);

        writer.Start(TimeSpan.FromMinutes(1));
        var retry = await clock.NextTimerAsync();
        File.Exists(path).Should().BeFalse();

        File.Delete(blockedDirectory);
        retry.Fire();
        _ = await clock.NextTimerAsync(); // The next interval is scheduled only after the retried write finishes.

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        document.RootElement.GetProperty("metrics").GetProperty("published").GetInt64().Should().Be(0);
    }

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

    private sealed class ControlledDelayClock : TimeProvider
    {
        private readonly Channel<ControlledTimer> _timers = Channel.CreateUnbounded<ControlledTimer>();
        public Task<ControlledTimer> NextTimerAsync() => _timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ControlledTimer(callback, state);
            _timers.Writer.TryWrite(timer);
            return timer;
        }
    }

    private sealed class ControlledTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;
        public void Fire() { if (!_disposed) callback(state); }
        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
