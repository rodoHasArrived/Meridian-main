using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Backfill;
using Meridian.Storage.Interfaces;
using Xunit;
using AppBackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Tests.Application.Backfill;

/// <summary>
/// A resume checkpoint asserts that a symbol's bars are on disk. These tests pin the ordering:
/// the checkpoint may only be written after the pipeline flush succeeded, and a failed flush
/// must surface as a failed run rather than a reported success.
/// </summary>
public sealed class HistoricalBackfillDurabilityTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "meridian-backfill-durability", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public async Task RunAsync_CheckpointIsWrittenOnlyAfterBarsAreFlushed()
    {
        var store = new BackfillStatusStore(_dataRoot);
        var sink = new ProbeSink();
        // The checkpoint file must not exist at any point before the first successful flush.
        sink.OnFlush = () => sink.CheckpointPresentAtFirstFlush ??= store.TryReadSymbolCheckpoints() is { Count: > 0 };
        await using var pipeline = new EventPipeline(sink, capacity: 1_000, enablePeriodicFlush: false);
        var service = new HistoricalBackfillService([new OneBarProvider()], checkpointStore: store);

        var result = await service.RunAsync(
            new AppBackfillRequest("test", ["SPY"], From: new DateOnly(2024, 1, 2), To: new DateOnly(2024, 1, 2)),
            pipeline);

        result.Success.Should().BeTrue();
        sink.Appended.Should().Be(1);
        sink.CheckpointPresentAtFirstFlush.Should().BeFalse(
            "a checkpoint written before the flush would let a resumed run skip bars a crash never stored");
        store.TryReadSymbolCheckpoints().Should().ContainKey("SPY");
    }

    [Fact]
    public async Task RunAsync_WhenFlushFails_WritesNoCheckpointAndReportsFailure()
    {
        var store = new BackfillStatusStore(_dataRoot);
        var sink = new ProbeSink { OnFlush = static () => throw new IOException("disk full") };
        await using var pipeline = new EventPipeline(sink, capacity: 1_000, enablePeriodicFlush: false);
        var service = new HistoricalBackfillService([new OneBarProvider()], checkpointStore: store);

        var result = await service.RunAsync(
            new AppBackfillRequest("test", ["SPY"], From: new DateOnly(2024, 1, 2), To: new DateOnly(2024, 1, 2)),
            pipeline);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("disk full");
        (store.TryReadSymbolCheckpoints() ?? new Dictionary<string, DateOnly>()).Should().NotContainKey("SPY");
        sink.OnFlush = null; // let pipeline disposal flush cleanly
    }

    [Fact]
    public async Task RunAsync_WhenFinalFlushFailsWithoutCheckpointStore_ReportsFailure()
    {
        var sink = new ProbeSink { OnFlush = static () => throw new IOException("disk full") };
        await using var pipeline = new EventPipeline(sink, capacity: 1_000, enablePeriodicFlush: false);
        var service = new HistoricalBackfillService([new OneBarProvider()]);

        var result = await service.RunAsync(
            new AppBackfillRequest("test", ["SPY"], From: new DateOnly(2024, 1, 2), To: new DateOnly(2024, 1, 2)),
            pipeline);

        result.Success.Should().BeFalse("queued bars that never flushed are not a successful backfill");
        result.Error.Should().Contain("flush failed");
        sink.OnFlush = null;
    }

    private sealed class ProbeSink : IStorageSink
    {
        private int _appended;

        public Action? OnFlush { get; set; }
        public bool? CheckpointPresentAtFirstFlush { get; set; }
        public int Appended => Volatile.Read(ref _appended);

        public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _appended);
            return ValueTask.CompletedTask;
        }

        public Task FlushAsync(CancellationToken ct = default)
        {
            OnFlush?.Invoke();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class OneBarProvider : IHistoricalDataProvider
    {
        public string Name => "test";
        public string DisplayName => "test";
        public string Description => string.Empty;

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HistoricalBar>>(
                [new HistoricalBar(symbol, new DateOnly(2024, 1, 2), 100m, 101m, 99m, 100.5m, 1_000, "test")]);
    }
}
