using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Storage.Archival;
using Meridian.Storage.Interfaces;
using Xunit;

namespace Meridian.Tests.Application.Pipeline;

/// <summary>
/// Deterministic fault-injection coverage for the irreversible dedup/WAL ordering boundaries:
/// <c>validate → reserve → WAL append → WAL flush → sink append → sink flush →
/// dedup commit/flush → WAL commit</c>. Verifies that a sink failure never causes a premature
/// dedup mark, a dedup failure never re-appends the sink, legacy version-1 entries never
/// suppress WAL replay, and producer-channel acceptance stays admission-only.
/// </summary>
public sealed class DedupWalOrderingTests : IAsyncLifetime
{
    private string _rootDir = null!;

    public Task InitializeAsync()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), $"dedup_wal_ordering_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDir);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_rootDir))
                    Directory.Delete(_rootDir, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4) { await Task.Delay(20); }
            catch (UnauthorizedAccessException) when (attempt < 4) { await Task.Delay(20); }
        }
    }

    #region Ledger reservation contract

    [Fact]
    public async Task TryReserve_ClaimsOnce_ReleaseMakesIdentityAvailableAgain()
    {
        await using var ledger = await CreateLedgerAsync("ledger_claim");
        var evt = CreateTradeEvent("SPY", 1);

        var first = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
        first.IsReserved.Should().BeTrue();

        var second = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
        second.Status.Should().Be(DedupReservationStatus.PendingElsewhere,
            "an in-flight reservation must suppress a concurrent claim of the same identity");

        ledger.Release(first.Reservation).Should().BeTrue();

        var third = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
        third.IsReserved.Should().BeTrue("a released identity must be claimable again");
    }

    [Fact]
    public async Task Release_WithStaleToken_DoesNotReleaseTheCurrentHolder()
    {
        await using var ledger = await CreateLedgerAsync("ledger_stale");
        var evt = CreateTradeEvent("SPY", 2);

        var first = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
        ledger.Release(first.Reservation).Should().BeTrue();

        var second = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
        second.IsReserved.Should().BeTrue();

        // Replaying the stale first token must not disturb the second holder's claim.
        ledger.Release(first.Reservation).Should().BeFalse();
        var stillPending = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
        stillPending.Status.Should().Be(DedupReservationStatus.PendingElsewhere);
    }

    [Fact]
    public async Task CommitDurable_PersistsVersion2_AndSuppressesBothScopesAcrossRestart()
    {
        var ledgerDir = Path.Combine(_rootDir, "ledger_commit");
        var evt = CreateTradeEvent("AAPL", 3);

        await using (var ledger = await CreateLedgerAsync("ledger_commit"))
        {
            var reserved = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
            await ledger.CommitDurableAsync(new[] { reserved.Reservation }, CancellationToken.None);
        }

        var lines = await File.ReadAllLinesAsync(Path.Combine(ledgerDir, "dedup_ledger.jsonl"));
        lines.Should().ContainSingle().Which.Should().Contain("\"v\":2",
            "a committed identity must be persisted as a durability-confirmed (version 2) entry");

        await using var reloaded = await CreateLedgerAsync("ledger_commit");
        (await reloaded.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None))
            .Status.Should().Be(DedupReservationStatus.Duplicate);
        (await reloaded.TryReserveAsync(evt, DedupLookupScope.WalRecovery, CancellationToken.None))
            .Status.Should().Be(DedupReservationStatus.Duplicate,
                "version-2 entries prove sink durability and therefore suppress WAL replay");
    }

    [Fact]
    public async Task LegacyV1Entry_SuppressesLiveIngress_ButNotWalRecovery()
    {
        var ledgerDir = Path.Combine(_rootDir, "ledger_legacy");
        Directory.CreateDirectory(ledgerDir);
        var evt = CreateTradeEvent("MSFT", 4);

        // Discover the identity key by letting a scratch ledger record the legacy line, then
        // reuse that exact file as the pre-existing (version-1) ledger state.
        await using (var seeder = new PersistentDedupLedger(ledgerDir))
        {
            await seeder.InitializeAsync();
            (await seeder.IsDuplicateAsync(evt, CancellationToken.None)).Should().BeFalse();
            await seeder.FlushAsync();
        }

        var legacyLine = (await File.ReadAllLinesAsync(Path.Combine(ledgerDir, "dedup_ledger.jsonl")))
            .Should().ContainSingle().Subject;
        legacyLine.Should().NotContain("\"v\":", "the legacy line shape has no version field");

        await using var ledger = new PersistentDedupLedger(ledgerDir);
        await ledger.InitializeAsync();

        (await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None))
            .Status.Should().Be(DedupReservationStatus.Duplicate,
                "legacy entries remain valid for live-ingress suppression");

        var recoveryLookup = await ledger.TryReserveAsync(evt, DedupLookupScope.WalRecovery, CancellationToken.None);
        recoveryLookup.IsReserved.Should().BeTrue(
            "legacy entries are untrusted during WAL recovery and the record must be replayed");

        // Committing the recovery reservation upgrades the identity to durability-confirmed.
        await ledger.CommitDurableAsync(new[] { recoveryLookup.Reservation }, CancellationToken.None);
        (await ledger.TryReserveAsync(evt, DedupLookupScope.WalRecovery, CancellationToken.None))
            .Status.Should().Be(DedupReservationStatus.Duplicate);
    }

    [Fact]
    public async Task CommitDurable_WithStaleToken_SkipsEntryWithoutThrowing()
    {
        var ledgerDir = Path.Combine(_rootDir, "ledger_stale_commit");
        await using var ledger = await CreateLedgerAsync("ledger_stale_commit");
        var evt = CreateTradeEvent("TSLA", 5);

        var reserved = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
        ledger.Release(reserved.Reservation).Should().BeTrue();

        // The token is stale: the commit must skip it (logged), never claim the identity.
        await ledger.CommitDurableAsync(new[] { reserved.Reservation }, CancellationToken.None);

        var ledgerPath = Path.Combine(ledgerDir, "dedup_ledger.jsonl");
        if (File.Exists(ledgerPath))
        {
            (await File.ReadAllTextAsync(ledgerPath)).Should().NotContain("\"v\":2");
        }

        (await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None))
            .IsReserved.Should().BeTrue("a skipped stale commit must leave the identity unclaimed");
    }

    [Fact]
    public async Task Compact_PreservesEntryVersions_AndNeverPersistsPendingReservations()
    {
        var ledgerDir = Path.Combine(_rootDir, "ledger_compact");
        await using var ledger = await CreateLedgerAsync("ledger_compact");

        var legacyEvt = CreateTradeEvent("LEG", 6);
        var durableEvt = CreateTradeEvent("DUR", 7);
        var pendingEvt = CreateTradeEvent("PEN", 8);

        (await ledger.IsDuplicateAsync(legacyEvt, CancellationToken.None)).Should().BeFalse();
        var durableReservation = await ledger.TryReserveAsync(durableEvt, DedupLookupScope.LiveIngress, CancellationToken.None);
        await ledger.CommitDurableAsync(new[] { durableReservation.Reservation }, CancellationToken.None);
        var pendingReservation = await ledger.TryReserveAsync(pendingEvt, DedupLookupScope.LiveIngress, CancellationToken.None);
        pendingReservation.IsReserved.Should().BeTrue();

        await ledger.CompactAsync();

        var lines = await File.ReadAllLinesAsync(Path.Combine(ledgerDir, "dedup_ledger.jsonl"));
        lines.Should().HaveCount(2, "only committed entries are compacted; pending reservations are memory-only");
        lines.Should().ContainSingle(line => line.Contains(":LEG:") && !line.Contains("\"v\":"),
            "compaction must keep the legacy entry in its version-1 shape, never upgrading its trust");
        lines.Should().ContainSingle(line => line.Contains(":DUR:") && line.Contains("\"v\":2"),
            "compaction must preserve the durability-confirmed version");
    }

    #endregion

    #region Pipeline ordering and fault injection

    [Fact]
    public async Task Consumer_WalRecordsAreDurableBeforeFirstSinkAppend()
    {
        var walDir = Path.Combine(_rootDir, "wal_superset");
        Directory.CreateDirectory(walDir);

        // NoSync leaves WAL durability entirely to the pipeline's explicit per-batch flush —
        // exactly the boundary this test proves.
        var wal = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal.InitializeAsync();

        var sink = new WalInspectingSink(walDir);
        await using var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false, wal: wal);

        pipeline.TryPublish(CreateTradeEvent("ORD1", 10));
        pipeline.TryPublish(CreateTradeEvent("ORD2", 11));
        pipeline.TryPublish(CreateTradeEvent("ORD3", 12));

        await pipeline.FlushAsync(CancellationToken.None);

        sink.Observations.Should().HaveCount(3);
        sink.Observations.Should().OnlyContain(observation => observation.WalContainedEvent,
            "every event must be durable in the WAL before its sink append begins");
    }

    [Fact]
    public async Task Consumer_SinkFlushFailure_RetriesWithoutReappendingAndCommitsDedupAfterFlush()
    {
        var walDir = Path.Combine(_rootDir, "wal_flushfail");
        Directory.CreateDirectory(walDir);
        var wal = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal.InitializeAsync();

        var ledgerDir = Path.Combine(_rootDir, "ledger_flushfail");
        var innerLedger = await CreateLedgerAsync("ledger_flushfail");
        var sink = new FaultSink { FlushFailuresRemaining = 1 };
        var dedupStore = new ObservingDedupStore(innerLedger)
        {
            OnCommit = _ => sink.SuccessfulFlushCount
        };

        await using (var pipeline = new EventPipeline(
            sink, capacity: 100, batchSize: 2, enablePeriodicFlush: false,
            wal: wal, dedupLedger: dedupStore))
        {
            pipeline.TryPublish(CreateTradeEvent("FLU1", 20));
            pipeline.TryPublish(CreateTradeEvent("FLU2", 21));

            await WaitUntilAsync(() => dedupStore.CommitAttempts > 0 && sink.SuccessfulFlushCount > 0);
            await pipeline.FlushAsync(CancellationToken.None);

            sink.AppendedEvents.Should().HaveCount(2,
                "a sink flush failure must retry the flush, never re-append already-acknowledged events");
            pipeline.GetStatistics().ConsumerIterationFailures.Should().BeGreaterThanOrEqualTo(1);
            dedupStore.SinkFlushCountsAtCommit.Should().NotBeEmpty()
                .And.OnlyContain(count => count >= 1,
                    "the dedup commit must only ever run after the sink flush succeeded");
        }

        var ledgerLines = await File.ReadAllLinesAsync(Path.Combine(ledgerDir, "dedup_ledger.jsonl"));
        ledgerLines.Where(line => line.Contains("\"v\":2")).Should().HaveCount(2,
            "both identities must end durability-confirmed exactly once");
        await innerLedger.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_DedupCommitFailure_RetriesCommitOnly_NeverReappendsSink()
    {
        var walDir = Path.Combine(_rootDir, "wal_dedupfail");
        Directory.CreateDirectory(walDir);
        var wal = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal.InitializeAsync();

        var innerLedger = await CreateLedgerAsync("ledger_dedupfail");
        var sink = new FaultSink();
        var dedupStore = new ObservingDedupStore(innerLedger) { CommitFailuresRemaining = 2 };

        await using (var pipeline = new EventPipeline(
            sink, capacity: 100, batchSize: 2, enablePeriodicFlush: false,
            wal: wal, dedupLedger: dedupStore))
        {
            pipeline.TryPublish(CreateTradeEvent("DCF1", 30));
            pipeline.TryPublish(CreateTradeEvent("DCF2", 31));

            // While the dedup commit keeps failing, no durability-confirmed identity may exist.
            await WaitUntilAsync(() => dedupStore.CommitAttempts >= 1);
            var ledgerPath = Path.Combine(Path.Combine(_rootDir, "ledger_dedupfail"), "dedup_ledger.jsonl");
            if (File.Exists(ledgerPath))
            {
                (await File.ReadAllTextAsync(ledgerPath)).Should().NotContain("\"v\":2",
                    "a failed dedup commit must not have persisted any durability confirmation");
            }

            await WaitUntilAsync(() => dedupStore.CommitSuccesses >= 1);
            await pipeline.FlushAsync(CancellationToken.None);

            sink.AppendedEvents.Should().HaveCount(2,
                "a dedup commit failure must retry only the commit — the sink must never be re-appended");
            dedupStore.CommitAttempts.Should().BeGreaterThanOrEqualTo(3);
        }

        await innerLedger.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_DuplicateIdentity_IsSuppressedExactlyOnce()
    {
        var walDir = Path.Combine(_rootDir, "wal_dupe");
        Directory.CreateDirectory(walDir);
        var wal = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal.InitializeAsync();

        var ledger = await CreateLedgerAsync("ledger_dupe");
        var sink = new FaultSink();

        await using (var pipeline = new EventPipeline(
            sink, capacity: 100, enablePeriodicFlush: false, wal: wal, dedupLedger: ledger))
        {
            var evt = CreateTradeEvent("DUP", 40);
            pipeline.TryPublish(evt);
            pipeline.TryPublish(evt);

            await pipeline.FlushAsync(CancellationToken.None);

            sink.AppendedEvents.Should().ContainSingle();
            pipeline.DeduplicatedCount.Should().Be(1);
        }

        await ledger.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_RejectedEvent_DoesNotClaimTheIdentityOfALaterValidEvent()
    {
        var walDir = Path.Combine(_rootDir, "wal_reject");
        Directory.CreateDirectory(walDir);
        var wal = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal.InitializeAsync();

        var ledger = await CreateLedgerAsync("ledger_reject");
        var sink = new FaultSink();
        // The validator keys rejection on the event sequence, which is NOT part of the trade's
        // dedup identity: both events below share one identity but only the first is invalid.
        var validator = new SequenceRejectingValidator(rejectedSequence: 666);

        await using (var pipeline = new EventPipeline(
            sink, capacity: 100, enablePeriodicFlush: false,
            wal: wal, validator: validator, dedupLedger: ledger))
        {
            var timestamp = DateTimeOffset.UtcNow;
            pipeline.TryPublish(CreateTradeEvent("VAL", 666, timestamp));
            pipeline.TryPublish(CreateTradeEvent("VAL", 667, timestamp));

            await pipeline.FlushAsync(CancellationToken.None);

            pipeline.RejectedCount.Should().Be(1);
            sink.AppendedEvents.Should().ContainSingle(
                "a validation-rejected payload must never consume the dedup identity of a later, corrected event")
                .Which.Sequence.Should().Be(667);
        }

        await ledger.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_AbandonedBatch_ReleasesReservations_SoRetriedEventsPersist()
    {
        // No WAL: a persistent failure abandons the batch to the dead-letter path. The pending
        // identity claims must be released so an upstream re-send is not suppressed forever.
        var ledger = await CreateLedgerAsync("ledger_abandon");
        var sink = new FaultSink { AppendFailuresRemaining = 1 };

        await using (var pipeline = new EventPipeline(
            sink, capacity: 100, enablePeriodicFlush: false, dedupLedger: ledger))
        {
            var evt = CreateTradeEvent("ABN", 50);
            pipeline.TryPublish(evt);

            await WaitUntilAsync(() => pipeline.GetStatistics().ConsumerIterationFailures >= 1);

            pipeline.TryPublish(evt);
            await WaitUntilAsync(() => sink.AppendedEvents.Count == 1);

            sink.AppendedEvents.Should().ContainSingle(
                "the re-sent event must be persisted because the abandoned batch released its claim");
        }

        await ledger.DisposeAsync();
    }

    [Fact]
    public async Task PublishAsync_IsAdmissionOnly_NoWalRecordUntilConsumerAdmission()
    {
        var walDir = Path.Combine(_rootDir, "wal_admission");
        Directory.CreateDirectory(walDir);
        var wal = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal.InitializeAsync();

        var sink = new GatedSink();
        await using var pipeline = new EventPipeline(
            sink, capacity: 100, batchSize: 1, enablePeriodicFlush: false, wal: wal);

        // Occupy the consumer inside the sink append of a first event (its own WAL admission is
        // complete at that point), then publish a second event through the awaited path.
        pipeline.TryPublish(CreateTradeEvent("GATE", 60));
        await sink.FirstAppendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await pipeline.PublishAsync(CreateTradeEvent("PUBQ", 61));

        // Producer-channel acceptance is admission-only: the queued event must NOT be in the
        // WAL yet. (Appending at publish time could let a cumulative consumer commit acknowledge
        // it while still queued, silently losing it on a crash.)
        ReadAllWalText(walDir).Should().NotContain("PUBQ",
            "PublishAsync acceptance is admission into the in-memory queue, not a durable acknowledgement");

        sink.ReleaseAll();
        await WaitUntilAsync(() => sink.AppendedEvents.Count == 2);
        await pipeline.FlushAsync(CancellationToken.None);

        ReadAllWalText(walDir).Should().Contain("PUBQ",
            "the consumer must append the event to the WAL during admission");
    }

    [Fact]
    public async Task Recovery_SinkAppendFailure_FailsClosed_AndCanBeRetried()
    {
        var walDir = Path.Combine(_rootDir, "wal_recover_fail");
        Directory.CreateDirectory(walDir);

        var wal1 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal1.InitializeAsync();
        var evt1 = CreateTradeEvent("RC1", 70);
        var evt2 = CreateTradeEvent("RC2", 71);
        await wal1.AppendAsync(evt1, evt1.Type.ToString());
        await wal1.AppendAsync(evt2, evt2.Type.ToString());
        await wal1.FlushAsync();
        await wal1.DisposeAsync();

        var ledger = await CreateLedgerAsync("ledger_recover_fail");

        // First recovery: the sink rejects the first append. Recovery must fail closed —
        // propagating the failure — rather than acknowledging records it could not replay.
        var failingSink = new FaultSink { AppendFailuresRemaining = 1 };
        var wal2 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        var pipeline1 = new EventPipeline(
            failingSink, capacity: 100, enablePeriodicFlush: false, wal: wal2, dedupLedger: ledger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline1.RecoverAsync());
        await pipeline1.DisposeAsync();

        // The failed replay must have released its identity claim so a retry can claim it again.
        var reclaim = await ledger.TryReserveAsync(evt1, DedupLookupScope.WalRecovery, CancellationToken.None);
        reclaim.IsReserved.Should().BeTrue("a failed recovery must release its pending identity claims");
        ledger.Release(reclaim.Reservation);

        // Second recovery with a healthy sink replays both events — nothing was lost.
        var healthySink = new FaultSink();
        var wal3 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await using var pipeline2 = new EventPipeline(
            healthySink, capacity: 100, enablePeriodicFlush: false, wal: wal3, dedupLedger: ledger);

        await pipeline2.RecoverAsync();

        healthySink.AppendedEvents.Should().HaveCount(2,
            "a retried recovery must replay every uncommitted record — at-least-once, never loss");
        await ledger.DisposeAsync();
    }

    #endregion

    #region Helpers and fakes

    private async Task<PersistentDedupLedger> CreateLedgerAsync(string subDirectory)
    {
        var dir = Path.Combine(_rootDir, subDirectory);
        Directory.CreateDirectory(dir);
        var ledger = new PersistentDedupLedger(dir);
        await ledger.InitializeAsync();
        return ledger;
    }

    private static string ReadAllWalText(string walDir)
    {
        var text = new System.Text.StringBuilder();
        foreach (var file in Directory.GetFiles(walDir, "*.wal"))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            text.Append(reader.ReadToEnd());
        }

        return text.ToString();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10_000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(5);
        }

        condition().Should().BeTrue($"the awaited condition should hold within {timeoutMs}ms");
    }

    private static MarketEvent CreateTradeEvent(string symbol, long sequence, DateTimeOffset? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow;
        var trade = new Trade(
            Timestamp: ts,
            Symbol: symbol,
            Price: 101.25m,
            Size: 50,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: sequence,
            StreamId: "TEST",
            Venue: "XNAS");

        return MarketEvent.Trade(ts, symbol, trade, sequence, "TEST");
    }

    /// <summary>
    /// Storage sink with deterministic append/flush fault switches and counters.
    /// </summary>
    private sealed class FaultSink : IStorageSink
    {
        private readonly List<MarketEvent> _appendedEvents = new();
        private readonly object _lock = new();
        private int _appendFailuresRemaining;
        private int _flushFailuresRemaining;
        private int _successfulFlushCount;

        public int AppendFailuresRemaining
        {
            get => Volatile.Read(ref _appendFailuresRemaining);
            set => Volatile.Write(ref _appendFailuresRemaining, value);
        }

        public int FlushFailuresRemaining
        {
            get => Volatile.Read(ref _flushFailuresRemaining);
            set => Volatile.Write(ref _flushFailuresRemaining, value);
        }

        public int SuccessfulFlushCount => Volatile.Read(ref _successfulFlushCount);

        public IReadOnlyList<MarketEvent> AppendedEvents
        {
            get { lock (_lock) { return _appendedEvents.ToList(); } }
        }

        public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
        {
            if (Volatile.Read(ref _appendFailuresRemaining) > 0)
            {
                Interlocked.Decrement(ref _appendFailuresRemaining);
                throw new InvalidOperationException("Injected sink append failure");
            }

            lock (_lock)
            { _appendedEvents.Add(evt); }
            return ValueTask.CompletedTask;
        }

        public Task FlushAsync(CancellationToken ct = default)
        {
            if (Volatile.Read(ref _flushFailuresRemaining) > 0)
            {
                Interlocked.Decrement(ref _flushFailuresRemaining);
                throw new InvalidOperationException("Injected sink flush failure");
            }

            Interlocked.Increment(ref _successfulFlushCount);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Sink that records, per append, whether the event was already durable in the WAL files —
    /// the observable proof of the WAL-flush-before-sink-append ordering.
    /// </summary>
    private sealed class WalInspectingSink : IStorageSink
    {
        private readonly string _walDir;
        private readonly ConcurrentQueue<(string Symbol, bool WalContainedEvent)> _observations = new();

        public WalInspectingSink(string walDir) => _walDir = walDir;

        public IReadOnlyList<(string Symbol, bool WalContainedEvent)> Observations => _observations.ToList();

        public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
        {
            var symbol = evt.Symbol ?? string.Empty;
            _observations.Enqueue((symbol, ReadAllWalText(_walDir).Contains(symbol)));
            return ValueTask.CompletedTask;
        }

        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Sink whose appends block on a gate; used to hold the consumer at a known point.
    /// </summary>
    private sealed class GatedSink : IStorageSink
    {
        private readonly SemaphoreSlim _gate = new(0);
        private readonly List<MarketEvent> _appendedEvents = new();
        private readonly object _lock = new();
        private int _firstAppendSignalled;

        public TaskCompletionSource FirstAppendEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<MarketEvent> AppendedEvents
        {
            get { lock (_lock) { return _appendedEvents.ToList(); } }
        }

        public void ReleaseAll() => _gate.Release(1_000);

        public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _firstAppendSignalled, 1) == 0)
            {
                FirstAppendEntered.TrySetResult();
            }

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            lock (_lock)
            { _appendedEvents.Add(evt); }
        }

        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Delegating dedup store with commit fault injection and ordering observation hooks.
    /// </summary>
    private sealed class ObservingDedupStore : IDedupStore
    {
        private readonly IDedupStore _inner;
        private int _commitFailuresRemaining;
        private int _commitAttempts;
        private int _commitSuccesses;

        public ObservingDedupStore(IDedupStore inner) => _inner = inner;

        public Func<IReadOnlyList<DedupReservation>, int>? OnCommit { get; init; }

        public int CommitFailuresRemaining
        {
            get => Volatile.Read(ref _commitFailuresRemaining);
            set => Volatile.Write(ref _commitFailuresRemaining, value);
        }

        public int CommitAttempts => Volatile.Read(ref _commitAttempts);
        public int CommitSuccesses => Volatile.Read(ref _commitSuccesses);
        public ConcurrentQueue<int> SinkFlushCountsAtCommit { get; } = new();

        public long TotalChecked => _inner.TotalChecked;
        public long TotalDuplicates => _inner.TotalDuplicates;

        public Task InitializeAsync(CancellationToken ct = default) => _inner.InitializeAsync(ct);

        public ValueTask<bool> IsDuplicateAsync(MarketEvent evt, CancellationToken ct = default)
            => _inner.IsDuplicateAsync(evt, ct);

        public ValueTask<DedupReservationResult> TryReserveAsync(
            MarketEvent evt, DedupLookupScope scope, CancellationToken ct = default)
            => _inner.TryReserveAsync(evt, scope, ct);

        public async Task CommitDurableAsync(
            IReadOnlyList<DedupReservation> reservations, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _commitAttempts);
            if (OnCommit is not null)
            {
                SinkFlushCountsAtCommit.Enqueue(OnCommit(reservations));
            }

            if (Volatile.Read(ref _commitFailuresRemaining) > 0)
            {
                Interlocked.Decrement(ref _commitFailuresRemaining);
                throw new InvalidOperationException("Injected dedup commit failure");
            }

            await _inner.CommitDurableAsync(reservations, ct).ConfigureAwait(false);
            Interlocked.Increment(ref _commitSuccesses);
        }

        public bool Release(in DedupReservation reservation) => _inner.Release(in reservation);

        public Task FlushAsync(CancellationToken ct = default) => _inner.FlushAsync(ct);

        public Task CompactAsync(CancellationToken ct = default) => _inner.CompactAsync(ct);
    }

    /// <summary>
    /// Validator that rejects exactly the event whose sequence matches the configured marker.
    /// The sequence is intentionally outside the trade content identity used by the dedup key.
    /// </summary>
    private sealed class SequenceRejectingValidator : IEventValidator
    {
        private readonly long _rejectedSequence;

        public SequenceRejectingValidator(long rejectedSequence) => _rejectedSequence = rejectedSequence;

        public ValidationResult Validate(in MarketEvent evt)
            => evt.Sequence == _rejectedSequence
                ? ValidationResult.Failed("injected validation rejection")
                : ValidationResult.Valid;
    }

    #endregion
}
