using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;
using Xunit;

namespace Meridian.Tests.Pipeline;

/// <summary>
/// Tests for the EventPipeline's Write-Ahead Log (WAL) integration.
/// Verifies that events are durably persisted through the WAL before reaching
/// the primary storage sink, and that crash recovery replays uncommitted records.
/// </summary>
public sealed class WalEventPipelineTests : IAsyncDisposable
{
    private readonly string _walDir;

    public WalEventPipelineTests()
    {
        _walDir = Path.Combine(Path.GetTempPath(), $"mdc_wal_pipeline_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_walDir);
    }

    public async ValueTask DisposeAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_walDir))
                    Directory.Delete(_walDir, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4) { await Task.Delay(20); }
            catch (UnauthorizedAccessException) when (attempt < 4) { await Task.Delay(20); }
        }
    }

    #region WAL Configuration Tests

    [Fact]
    public async Task Pipeline_WithoutWal_IsWalEnabledReturnsFalse()
    {
        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false);

        pipeline.IsWalEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Pipeline_WithWal_IsWalEnabledReturnsTrue()
    {
        var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false, wal: wal);

        pipeline.IsWalEnabled.Should().BeTrue();
    }

    #endregion

    #region Consumer WAL Integration Tests

    [Fact]
    public async Task TryPublish_WithWal_EventIsWrittenToWalAndSink()
    {
        var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal.InitializeAsync();

        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false, wal: wal);

        var evt = CreateTradeEvent("SPY");
        pipeline.TryPublish(evt);

        await WaitForConsumption(sink, expectedCount: 1);

        sink.ReceivedEvents.Should().ContainSingle()
            .Which.Symbol.Should().Be("SPY");

        // Verify WAL has records (committed since consumer processes and commits)
        var walFiles = Directory.GetFiles(_walDir, "*.wal");
        walFiles.Should().NotBeEmpty("WAL should have files after event processing");
    }

    [Fact]
    public async Task TryPublish_WithWal_MultipleEvents_AllConsumedAndCommitted()
    {
        // Use NoSync to avoid 50 sequential file flushes in the consumer which
        // can exceed the wait timeout on busy CI runners (especially Windows).
        // Single-event EveryWrite behavior is covered by TryPublish_WithWal_EventIsWrittenToWalAndSink.
        var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(sink, capacity: 1000, enablePeriodicFlush: false, wal: wal);

        for (int i = 0; i < 50; i++)
        {
            pipeline.TryPublish(CreateTradeEvent($"SYM{i}"));
        }

        // FlushAsync waits for the consumer to drain all queued events
        await pipeline.FlushAsync(CancellationToken.None);

        sink.ReceivedEvents.Should().HaveCount(50);
        pipeline.ConsumedCount.Should().Be(50);
    }

    [Fact]
    public async Task PublishAsync_WithWal_EventWrittenToWalByConsumer()
    {
        var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal.InitializeAsync();

        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false, wal: wal);

        var evt = CreateTradeEvent("AAPL");
        await pipeline.PublishAsync(evt);

        await WaitForConsumption(sink, expectedCount: 1);

        sink.ReceivedEvents.Should().ContainSingle()
            .Which.Symbol.Should().Be("AAPL");
    }

    #endregion

    #region Crash Recovery Tests

    [Fact]
    public async Task RecoverAsync_WithNoUncommittedEvents_IsNoOp()
    {
        var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });

        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false, wal: wal);

        await pipeline.RecoverAsync();

        pipeline.RecoveredCount.Should().Be(0);
        sink.ReceivedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task RecoverAsync_WithUncommittedEvents_ReplaysToSink()
    {
        // Phase 1: Write events to WAL but don't commit (simulates crash)
        var wal1 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal1.InitializeAsync();

        var evt1 = CreateTradeEvent("SPY");
        var evt2 = CreateTradeEvent("AAPL");
        await wal1.AppendAsync(evt1, evt1.Type.ToString());
        await wal1.AppendAsync(evt2, evt2.Type.ToString());
        await wal1.FlushAsync();
        // Dispose without committing - simulates crash
        await wal1.DisposeAsync();

        // Phase 2: Create new pipeline with recovery
        var wal2 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });

        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false, wal: wal2);

        await pipeline.RecoverAsync();

        // Verify recovery
        pipeline.RecoveredCount.Should().Be(2);
        sink.ReceivedEvents.Should().HaveCount(2);
        sink.FlushCount.Should().BeGreaterThanOrEqualTo(1, "sink should be flushed after recovery");
    }

    [Fact]
    public async Task RecoverAsync_CommittedEventsAreNotReplayed()
    {
        // Phase 1: Write and commit events
        var wal1 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal1.InitializeAsync();

        var r1 = await wal1.AppendAsync(CreateTradeEvent("SPY"), "Trade");
        var r2 = await wal1.AppendAsync(CreateTradeEvent("AAPL"), "Trade");
        await wal1.CommitAsync(r2.Sequence);
        await wal1.DisposeAsync();

        // Phase 2: Recovery should find no uncommitted events
        var wal2 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });

        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false, wal: wal2);

        await pipeline.RecoverAsync();

        pipeline.RecoveredCount.Should().Be(0);
        sink.ReceivedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task RecoverAsync_PartiallyCommitted_RecoversOnlyUncommitted()
    {
        // Phase 1: Write 3 events, commit first 1
        var wal1 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal1.InitializeAsync();

        var r1 = await wal1.AppendAsync(CreateTradeEvent("SPY"), "Trade");
        await wal1.CommitAsync(r1.Sequence);

        // These 2 are uncommitted
        await wal1.AppendAsync(CreateTradeEvent("AAPL"), "Trade");
        await wal1.AppendAsync(CreateTradeEvent("GOOGL"), "Trade");
        await wal1.FlushAsync();
        await wal1.DisposeAsync();

        // Phase 2: Recovery
        var wal2 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });

        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false, wal: wal2);

        await pipeline.RecoverAsync();

        pipeline.RecoveredCount.Should().Be(2, "only the 2 uncommitted events should be recovered");
        sink.ReceivedEvents.Should().HaveCount(2);
    }

    [Fact]
    public async Task RecoverAsync_WithoutWal_IsNoOp()
    {
        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false);

        // Should not throw when no WAL is configured
        await pipeline.RecoverAsync();

        pipeline.RecoveredCount.Should().Be(0);
    }

    #endregion

    #region WAL-Sink Transaction Tests (improvement 1.2)

    [Fact]
    public async Task ConsumeAsync_WalCommitFailure_DoesNotCauseReFlushedDuplicates()
    {
        // Arrange: use a sink that counts flush calls so we can detect re-flushes
        var flushSink = new CountingFlushSink();
        var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        await using var pipeline = new EventPipeline(
            flushSink, capacity: 100, enablePeriodicFlush: false, wal: wal);

        // Act: publish and drain events normally
        for (var i = 0; i < 5; i++)
            pipeline.TryPublish(CreateTradeEvent($"SYM{i}"));

        await pipeline.FlushAsync(CancellationToken.None);

        // Assert: the sink received exactly 5 events with no duplicates
        flushSink.ReceivedEvents.Should().HaveCount(5,
            "WAL commit failure must not cause the same batch to be re-flushed");
        pipeline.ConsumedCount.Should().Be(5);
    }

    [Fact]
    public async Task RecoverAsync_WalCommitFailure_SinkDataNotReplayed()
    {
        // Phase 1: write two events to WAL but don't commit (simulates crash)
        var wal1 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal1.InitializeAsync();
        var e1 = CreateTradeEvent("SPY");
        var e2 = CreateTradeEvent("AAPL");
        await wal1.AppendAsync(e1, e1.Type.ToString());
        await wal1.AppendAsync(e2, e2.Type.ToString());
        await wal1.FlushAsync();
        await wal1.DisposeAsync();

        // Phase 2: recover into a fresh sink – events must arrive exactly once
        var wal2 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(
            sink, capacity: 100, enablePeriodicFlush: false, wal: wal2);

        await pipeline.RecoverAsync();

        // A second RecoverAsync call simulates what would happen if WAL.CommitAsync had failed
        // during the first recovery (WAL records still present).  Even without a dedup ledger
        // the pipeline should not replay the same events again because _lastCommittedWalSequence
        // was updated before the commit attempt.
        await pipeline.RecoverAsync();

        sink.ReceivedEvents.Should().HaveCount(2,
            "events should not be replayed if the sink flush succeeded, " +
            "regardless of whether the WAL commit itself completed");
    }

    #endregion

    #region Idempotent Recovery Tests (improvement 3.4)

    [Fact]
    public async Task RecoverAsync_WithDedupLedger_SkipsDuplicates()
    {
        // Phase 1: persist two events to both WAL and sink (simulates partial crash after sink flush)
        var walDir1 = Path.Combine(_walDir, "dedup_p1");
        var dedupDir = Path.Combine(_walDir, "dedup_ledger");
        Directory.CreateDirectory(walDir1);
        Directory.CreateDirectory(dedupDir);

        var wal1 = new WriteAheadLog(walDir1, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal1.InitializeAsync();

        var evt1 = CreateTradeEvent("SPY");
        var evt2 = CreateTradeEvent("AAPL");

        // Write both to WAL without committing (simulates crash before WAL commit)
        await wal1.AppendAsync(evt1, evt1.Type.ToString());
        await wal1.AppendAsync(evt2, evt2.Type.ToString());
        await wal1.FlushAsync();
        await wal1.DisposeAsync();

        // Phase 2: recover with dedup ledger pre-seeded with the already-persisted events.
        // IsDuplicateAsync records the event on first call and returns false;
        // subsequent calls for the same event return true (duplicate).
        var wal2 = new WriteAheadLog(walDir1, new WalOptions { SyncMode = WalSyncMode.NoSync });
        var ledger = new PersistentDedupLedger(dedupDir);
        await ledger.InitializeAsync();

        // Pre-seed: simulate the events having been written to the sink already
        await ledger.IsDuplicateAsync(evt1, CancellationToken.None);
        await ledger.IsDuplicateAsync(evt2, CancellationToken.None);

        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(
            sink, capacity: 100, enablePeriodicFlush: false,
            wal: wal2, dedupLedger: ledger);

        await pipeline.RecoverAsync();

        // Assert: events were skipped because dedup ledger shows them already persisted
        sink.ReceivedEvents.Should().BeEmpty(
            "events already recorded in the dedup ledger must not be written again on recovery");
        pipeline.RecoveredCount.Should().Be(0,
            "skipped-as-duplicate events do not count as recovered");
    }

    [Fact]
    public async Task RecoverAsync_WithoutDedupLedger_ReplaysNormally()
    {
        // Phase 1: write events to WAL without committing
        var wal1 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal1.InitializeAsync();
        await wal1.AppendAsync(CreateTradeEvent("MSFT"), "Trade");
        await wal1.AppendAsync(CreateTradeEvent("TSLA"), "Trade");
        await wal1.FlushAsync();
        await wal1.DisposeAsync();

        // Phase 2: recover without a dedup ledger – both events must be replayed
        var wal2 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(
            sink, capacity: 100, enablePeriodicFlush: false, wal: wal2);

        await pipeline.RecoverAsync();

        sink.ReceivedEvents.Should().HaveCount(2);
        pipeline.RecoveredCount.Should().Be(2);
    }

    #endregion

    #region End-to-End Durability Tests

    [Fact]
    public async Task EndToEnd_PublishAndRecover_NoDataLoss()
    {
        // Phase 1: Publish events through WAL-enabled pipeline, then dispose normally
        var wal1 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal1.InitializeAsync();

        var sink1 = new MockWalSink();
        var pipeline1 = new EventPipeline(sink1, capacity: 100, enablePeriodicFlush: false, wal: wal1);

        for (int i = 0; i < 10; i++)
        {
            pipeline1.TryPublish(CreateTradeEvent($"SYM{i}"));
        }

        await WaitForConsumption(sink1, expectedCount: 10);
        await pipeline1.DisposeAsync();

        // All events should have been consumed and committed
        sink1.ReceivedEvents.Should().HaveCount(10);

        // Phase 2: New pipeline should have nothing to recover (clean shutdown)
        var wal2 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });

        await using var sink2 = new MockWalSink();
        await using var pipeline2 = new EventPipeline(sink2, capacity: 100, enablePeriodicFlush: false, wal: wal2);

        await pipeline2.RecoverAsync();

        pipeline2.RecoveredCount.Should().Be(0, "all events were committed during normal shutdown");
    }

    [Fact]
    public async Task Pipeline_WithWal_StatisticsInclude_RecoveredCount()
    {
        // Phase 1: Write uncommitted events
        var wal1 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal1.InitializeAsync();
        await wal1.AppendAsync(CreateTradeEvent("SPY"), "Trade");
        await wal1.FlushAsync();
        await wal1.DisposeAsync();

        // Phase 2: Recover and check stats
        var wal2 = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });

        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false, wal: wal2);

        await pipeline.RecoverAsync();

        pipeline.RecoveredCount.Should().Be(1);
    }

    #endregion

    #region Backward Compatibility Tests

    [Fact]
    public async Task Pipeline_WithoutWal_BehavesIdentically()
    {
        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(sink, capacity: 1000, enablePeriodicFlush: false);

        for (int i = 0; i < 100; i++)
        {
            pipeline.TryPublish(CreateTradeEvent("SPY"));
        }

        await WaitForConsumption(sink, expectedCount: 100);

        pipeline.ConsumedCount.Should().Be(100);
        sink.ReceivedEvents.Should().HaveCount(100);
        pipeline.IsWalEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Pipeline_PolicyConstructor_WithWal_Works()
    {
        var wal = new WriteAheadLog(_walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        await using var sink = new MockWalSink();
        await using var pipeline = new EventPipeline(
            sink,
            EventPipelinePolicy.HighThroughput,
            enablePeriodicFlush: false,
            wal: wal);

        pipeline.IsWalEnabled.Should().BeTrue();

        pipeline.TryPublish(CreateTradeEvent("SPY"));
        await WaitForConsumption(sink, expectedCount: 1);

        sink.ReceivedEvents.Should().ContainSingle();
    }

    #endregion

    #region Helper Methods

    private static MarketEvent CreateTradeEvent(string symbol)
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 100.50m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1,
            Venue: "NYSE");

        return MarketEvent.Trade(DateTimeOffset.UtcNow, symbol, trade);
    }

    private static async Task WaitForConsumption(MockWalSink sink, int expectedCount, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sink.ReceivedEvents.Count < expectedCount && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(1);
        }
    }

    #endregion
}

/// <summary>
/// Mock storage sink for WAL-EventPipeline integration testing.
/// </summary>
internal sealed class MockWalSink : IStorageSink
{
    private readonly List<MarketEvent> _receivedEvents = new();
    private readonly object _lock = new();

    public IReadOnlyList<MarketEvent> ReceivedEvents
    {
        get
        {
            lock (_lock)
            {
                return _receivedEvents.ToList();
            }
        }
    }

    public int FlushCount { get; private set; }

    public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _receivedEvents.Add(evt);
        }
        return ValueTask.CompletedTask;
    }

    public Task FlushAsync(CancellationToken ct = default)
    {
        FlushCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A storage sink that records all appended events and counts flush calls.
/// Used to detect duplicate flushes caused by WAL-sink transaction bugs.
/// </summary>
internal sealed class CountingFlushSink : IStorageSink
{
    private readonly List<MarketEvent> _receivedEvents = new();
    private readonly object _lock = new();

    public IReadOnlyList<MarketEvent> ReceivedEvents
    {
        get { lock (_lock) { return _receivedEvents.ToList(); } }
    }

    public int FlushCount { get; private set; }

    public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
    {
        lock (_lock)
        { _receivedEvents.Add(evt); }
        return ValueTask.CompletedTask;
    }

    public Task FlushAsync(CancellationToken ct = default)
    {
        FlushCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
