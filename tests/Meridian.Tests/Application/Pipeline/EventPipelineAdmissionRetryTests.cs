using FluentAssertions;
using Meridian.Application.Composition.Startup;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Performance;
using Meridian.Domain.Events;
using Xunit;

namespace Meridian.Tests.Pipeline;

/// <summary>
/// Failures that happen before a batch reaches the WAL or sink, and the order in which shutdown
/// closes the two pipeline paths, must not cost events.
/// </summary>
public sealed class EventPipelineAdmissionRetryTests
{
    [Fact]
    public async Task TransientDedupStoreFailure_RetainsTheBatchInsteadOfDiscardingIt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var sink = new MockStorageSink();
        var dedup = new FlakyDedupStore(reserveFailures: 1);
        await using var pipeline = new EventPipeline(
            sink, capacity: 64, enablePeriodicFlush: false, dedupLedger: dedup);

        for (var i = 1; i <= 5; i++)
            await pipeline.PublishAsync(CreateTradeEvent("SPY", i), timeout.Token);

        await pipeline.FlushAsync(timeout.Token);

        dedup.ReserveAttempts.Should().BeGreaterThan(5, "the failed reservation was retried");
        sink.ReceivedEvents.Select(evt => evt.Sequence).Should().Equal(new long[] { 1, 2, 3, 4, 5 },
            "a transient dedup-store fault must not drop the batch or reorder it");
    }

    [Fact]
    public async Task ShutdownParticipant_DrainsHotPathBuffersBeforeClosingTheSlowPath()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var sink = new MockStorageSink();
        var slowPath = new EventPipeline(sink, capacity: 64, enablePeriodicFlush: false);
        var symbols = new SymbolTable();
        // No consumers: accepted trades stay in the ring buffer, as they would for events that
        // arrive just before shutdown.
        var hotPath = new DualPathEventPipeline(
            slowPath, symbols, ringBufferCapacity: 16, batchDrainSize: 4, startConsumers: false);

        try
        {
            for (var i = 1; i <= 3; i++)
                hotPath.TryPublish(CreateTradeEvent("SPY", i)).Should().BeTrue();
            hotPath.TradeBufferCount.Should().Be(3);

            var participant = new EventPipelineShutdownParticipant(slowPath, hotPath);
            await participant.ExecuteAsync(timeout.Token);

            sink.ReceivedEvents.Should().HaveCount(3,
                "events accepted by the hot path must reach storage before the slow path is closed");
            hotPath.HotTradeDropped.Should().Be(0);
        }
        finally
        {
            await hotPath.DisposeAsync();
            await slowPath.DisposeAsync();
        }
    }

    private static MarketEvent CreateTradeEvent(string symbol, int seq)
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 100.50m + seq,
            Size: 100L,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: seq);
        return MarketEvent.Trade(DateTimeOffset.UtcNow, symbol, trade, "TEST", seq);
    }

    /// <summary>Accepts every identity; fails the first N reservation calls.</summary>
    private sealed class FlakyDedupStore(int reserveFailures) : IDedupStore
    {
        private int _failuresRemaining = reserveFailures;
        private int _reserveAttempts;
        private long _token;

        public int ReserveAttempts => Volatile.Read(ref _reserveAttempts);
        public long TotalChecked => ReserveAttempts;
        public long TotalDuplicates => 0;

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask<bool> IsDuplicateAsync(MarketEvent evt, CancellationToken ct = default)
            => ValueTask.FromResult(false);

        public ValueTask<DedupReservationResult> TryReserveAsync(
            MarketEvent evt, DedupLookupScope scope, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _reserveAttempts);
            if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
                throw new IOException("Injected dedup store failure");

            var reservation = new DedupReservation($"{evt.EffectiveSymbol}:{evt.Sequence}", Interlocked.Increment(ref _token));
            return ValueTask.FromResult(new DedupReservationResult(DedupReservationStatus.Reserved, reservation));
        }

        public Task CommitDurableAsync(IReadOnlyList<DedupReservation> reservations, CancellationToken ct = default)
            => Task.CompletedTask;

        public bool Release(in DedupReservation reservation) => true;

        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task CompactAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
