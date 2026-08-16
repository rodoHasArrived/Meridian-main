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

    [Fact]
    public async Task IsDuplicateAsync_PendingClaimRelease_WaitsAndRecordsIdentityAsNew()
    {
        await using var ledger = await CreateLedgerAsync("ledger_legacy_wait_release");
        var evt = CreateTradeEvent("LEGWAIT", 9);

        var claim = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
        claim.IsReserved.Should().BeTrue();

        // The claim is memory-only and its holder may still fail: reporting it as a duplicate
        // would let this caller discard a delivery while no durable copy exists.
        var duplicateCheck = ledger.IsDuplicateAsync(evt, CancellationToken.None).AsTask();
        await Task.Delay(150);
        duplicateCheck.IsCompleted.Should().BeFalse(
            "an in-flight reservation proves nothing durable, so the legacy check must wait for it to resolve");

        // The holder abandons without persisting: the waiting caller records the identity itself.
        ledger.Release(claim.Reservation).Should().BeTrue();
        (await duplicateCheck.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeFalse(
            "a released claim never became durable, so the waiting caller must treat the event as new");

        (await ledger.IsDuplicateAsync(evt, CancellationToken.None)).Should().BeTrue(
            "the waiting caller must have recorded the identity when the claim resolved as released");
    }

    [Fact]
    public async Task IsDuplicateAsync_PendingClaimCommit_WaitsAndReportsDurableDuplicate()
    {
        await using var ledger = await CreateLedgerAsync("ledger_legacy_wait_commit");
        var evt = CreateTradeEvent("LEGCOMMIT", 10);

        var claim = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
        claim.IsReserved.Should().BeTrue();

        var duplicateCheck = ledger.IsDuplicateAsync(evt, CancellationToken.None).AsTask();
        await Task.Delay(150);
        duplicateCheck.IsCompleted.Should().BeFalse(
            "a pending claim must never resolve the legacy check before it commits or releases");

        // The holder proves sink durability: the waiting caller sees a committed duplicate.
        await ledger.CommitDurableAsync(new[] { claim.Reservation }, CancellationToken.None);
        (await duplicateCheck.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue(
            "a committed claim is a durability-confirmed identity, so the waiting caller must suppress the event");
    }

    [Fact]
    public async Task IsDuplicateAsync_RacingReservation_AdmitsExactlyOneCaller()
    {
        // The legacy check and the reservation path race on the same fresh identity from a
        // common start signal. Legacy admission goes through the same per-key pending slot as
        // TryReserveAsync, so exactly one caller may ever be told the identity is new; a
        // non-atomic check-then-record would intermittently admit both.
        await using var ledger = await CreateLedgerAsync("ledger_legacy_race");

        for (var round = 0; round < 400; round++)
        {
            var evt = CreateTradeEvent("RACE", 10_000 + round);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var legacyAdmitted = Task.Run(async () =>
            {
                await start.Task;
                return !await ledger.IsDuplicateAsync(evt, CancellationToken.None);
            });
            var reservationAdmitted = Task.Run(async () =>
            {
                await start.Task;
                var reservation = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
                if (!reservation.IsReserved)
                    return false;

                await ledger.CommitDurableAsync(new[] { reservation.Reservation }, CancellationToken.None);
                return true;
            });

            start.SetResult();
            var admissions = await Task.WhenAll(legacyAdmitted, reservationAdmitted);

            ((admissions[0] ? 1 : 0) + (admissions[1] ? 1 : 0)).Should().Be(1,
                $"round {round}: exactly one of the racing callers may admit a fresh identity");
        }
    }

    [Fact]
    public async Task TryReserve_PendingElsewhere_DoesNotInflateDuplicateTelemetry()
    {
        await using var ledger = await CreateLedgerAsync("ledger_pending_telemetry");
        var evt = CreateTradeEvent("PENDTEL", 11);

        var holder = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
        holder.IsReserved.Should().BeTrue();

        // A delivery blocked by the claim re-polls it on every retry (the pipeline retries
        // every 250 ms); none of these unresolved waits is a detected duplicate.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None))
                .Status.Should().Be(DedupReservationStatus.PendingElsewhere);
        }

        ledger.TotalDuplicates.Should().Be(0,
            "an unresolved pending claim defers the caller; it must not be counted as a detected duplicate");

        // Once the holder commits, the next retry is suppressed by the committed entry and
        // counted as exactly one detection.
        await ledger.CommitDurableAsync(new[] { holder.Reservation }, CancellationToken.None);
        (await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None))
            .Status.Should().Be(DedupReservationStatus.Duplicate);
        ledger.TotalDuplicates.Should().Be(1,
            "only the committed-entry suppression is a real duplicate detection");
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

    [Fact]
    public async Task Recovery_UnreadablePayload_AlertMode_SignalsCorruptionAndContinues()
    {
        var walDir = Path.Combine(_rootDir, "wal_poison_alert");
        Directory.CreateDirectory(walDir);

        var wal1 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal1.InitializeAsync();
        var healthy = CreateTradeEvent("OK1", 80);
        await wal1.AppendAsync(healthy, healthy.Type.ToString());
        // Checksum-valid record whose payload is not a MarketEvent — a semantic poison record.
        await wal1.AppendAsync("not-a-market-event", "Trade");
        await wal1.FlushAsync();
        await wal1.DisposeAsync();

        var wal2 = new WriteAheadLog(walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Alert
        });
        long corruptionSignals = 0;
        wal2.CorruptionDetected += count => Interlocked.Add(ref corruptionSignals, count);

        var sink = new FaultSink();
        await using var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false, wal: wal2);

        await pipeline.RecoverAsync();

        sink.AppendedEvents.Should().ContainSingle("the healthy record must still be replayed");
        Interlocked.Read(ref corruptionSignals).Should().BeGreaterThanOrEqualTo(1,
            "Alert mode must raise the corruption signal for an undeserializable payload");
        wal2.CorruptedRecordCount.Should().BeGreaterThanOrEqualTo(1,
            "the semantic payload failure must be counted as corruption, never dropped silently");

        // The trailing poison record is committed past after its one signal: a fresh
        // recovery finds nothing to replay or re-alert on.
        var wal3 = new WriteAheadLog(walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Alert
        });
        var sink3 = new FaultSink();
        await using var pipeline3 = new EventPipeline(
            sink3, capacity: 100, enablePeriodicFlush: false, wal: wal3);
        await pipeline3.RecoverAsync();
        sink3.AppendedEvents.Should().BeEmpty();
        pipeline3.RecoveredCount.Should().Be(0);
    }

    [Fact]
    public async Task Recovery_UnreadablePayload_HaltMode_FailsClosed()
    {
        var walDir = Path.Combine(_rootDir, "wal_poison_halt");
        Directory.CreateDirectory(walDir);

        var wal1 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal1.InitializeAsync();
        await wal1.AppendAsync("not-a-market-event", "Trade");
        await wal1.FlushAsync();
        await wal1.DisposeAsync();

        var wal2 = new WriteAheadLog(walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Halt
        });
        var sink = new FaultSink();
        await using var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false, wal: wal2);

        await Assert.ThrowsAsync<InvalidDataException>(() => pipeline.RecoverAsync());
        sink.AppendedEvents.Should().BeEmpty();
        wal2.CorruptedRecordCount.Should().BeGreaterThanOrEqualTo(1,
            "a halt caused by semantic corruption must still be counted as corruption");
        wal2.SkippedRecordCount.Should().Be(0,
            "Halt mode does not skip the record, so the skipped counter must not claim it did");
    }

    [Fact]
    public async Task IsDuplicateAsync_LegacySighting_NeverDowngradesDurabilityConfirmation()
    {
        var ledgerDir = Path.Combine(_rootDir, "ledger_no_downgrade");
        Directory.CreateDirectory(ledgerDir);
        var evt = CreateTradeEvent("NODOWN", 800);

        await using var ledger = new PersistentDedupLedger(ledgerDir, entryTtl: TimeSpan.FromMilliseconds(50));
        await ledger.InitializeAsync();

        var reserved = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
        await ledger.CommitDurableAsync(new[] { reserved.Reservation }, CancellationToken.None);

        // Let the durability-confirmed entry expire, then record a legacy sighting of the same
        // identity — the exact path that could overwrite version 2 with version 1.
        await Task.Delay(150);
        (await ledger.IsDuplicateAsync(evt, CancellationToken.None)).Should().BeFalse(
            "the expired entry no longer suppresses live ingress");

        // The refreshed entry must keep the durability confirmation: sink durability, once
        // proven, is not retracted by a later legacy sighting.
        (await ledger.TryReserveAsync(evt, DedupLookupScope.WalRecovery, CancellationToken.None))
            .Status.Should().Be(DedupReservationStatus.Duplicate,
                "a legacy sighting must never downgrade a durability-confirmed identity");
    }

    [Fact]
    public async Task Consumer_NoWal_DedupCommitFailureAfterSinkFlush_RetriesCommitOnly()
    {
        // No WAL: after a successful sink flush, a transient dedup commit failure must retry
        // just the commit phase — never abandon the batch and release claims for identities
        // that are already durable in the sink.
        var innerLedger = await CreateLedgerAsync("ledger_nowal_commit");
        var sink = new FaultSink();
        var dedupStore = new ObservingDedupStore(innerLedger) { CommitFailuresRemaining = 1 };

        await using (var pipeline = new EventPipeline(
            sink, capacity: 100, enablePeriodicFlush: false, dedupLedger: dedupStore))
        {
            var evt = CreateTradeEvent("NWC", 90);
            pipeline.TryPublish(evt);

            await WaitUntilAsync(() => dedupStore.CommitSuccesses >= 1);

            sink.AppendedEvents.Should().ContainSingle(
                "the commit-only retry must never re-append the already-flushed sink");
            pipeline.GetStatistics().ConsumerIterationFailures.Should().BeGreaterThanOrEqualTo(1);
            dedupStore.CommitAttempts.Should().BeGreaterThanOrEqualTo(2);

            // The identity stayed claimed through the retry and is now durability-confirmed.
            pipeline.TryPublish(evt);
            await WaitUntilAsync(() => pipeline.DeduplicatedCount >= 1);
            sink.AppendedEvents.Should().ContainSingle();
        }

        await innerLedger.DisposeAsync();
    }

    [Fact]
    public async Task Recovery_LargeBacklog_CommitsInBoundedChunks()
    {
        var walDir = Path.Combine(_rootDir, "wal_chunks");
        Directory.CreateDirectory(walDir);

        var wal1 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal1.InitializeAsync();
        const int backlog = 25;
        for (var i = 0; i < backlog; i++)
        {
            var evt = CreateTradeEvent($"CHK{i}", 100 + i);
            await wal1.AppendAsync(evt, evt.Type.ToString());
        }

        await wal1.FlushAsync();
        await wal1.DisposeAsync();

        var ledger = await CreateLedgerAsync("ledger_chunks");
        var sink = new FaultSink();
        var wal2 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await using (var pipeline = new EventPipeline(
            sink, capacity: 100, enablePeriodicFlush: false, wal: wal2, dedupLedger: ledger))
        {
            pipeline.RecoveryCommitBatchSize = 10;
            await pipeline.RecoverAsync();

            pipeline.RecoveredCount.Should().Be(backlog);
            sink.AppendedEvents.Should().HaveCount(backlog);
            sink.SuccessfulFlushCount.Should().BeGreaterThanOrEqualTo(3,
                "a 25-record backlog with a 10-record chunk size must reach the durable boundary per chunk");
        }

        // Every chunk committed its WAL horizon: a fresh recovery finds nothing uncommitted.
        var wal3 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        var sink2 = new FaultSink();
        await using (var pipeline2 = new EventPipeline(
            sink2, capacity: 100, enablePeriodicFlush: false, wal: wal3, dedupLedger: ledger))
        {
            await pipeline2.RecoverAsync();
            pipeline2.RecoveredCount.Should().Be(0);
            sink2.AppendedEvents.Should().BeEmpty();
        }

        await ledger.DisposeAsync();
    }

    [Fact]
    public async Task Recovery_ChunkFailure_CountsCommittedChunks_AndResumesFromTheFailedChunk()
    {
        var walDir = Path.Combine(_rootDir, "wal_chunk_fail");
        Directory.CreateDirectory(walDir);

        var wal1 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal1.InitializeAsync();
        const int backlog = 25;
        for (var i = 0; i < backlog; i++)
        {
            var evt = CreateTradeEvent($"CHF{i}", 200 + i);
            await wal1.AppendAsync(evt, evt.Type.ToString());
        }

        await wal1.FlushAsync();
        await wal1.DisposeAsync();

        // First recovery fails inside the second chunk (append #15 of 25, chunk size 10).
        var ledger = await CreateLedgerAsync("ledger_chunk_fail");
        var failingSink = new FaultSink { FailOnAppendNumber = 15 };
        var wal2 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        var pipeline1 = new EventPipeline(
            failingSink, capacity: 100, enablePeriodicFlush: false, wal: wal2, dedupLedger: ledger);
        pipeline1.RecoveryCommitBatchSize = 10;

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline1.RecoverAsync());
        pipeline1.RecoveredCount.Should().Be(10,
            "the first chunk crossed its durable boundary and must be counted even though a later chunk failed");
        await pipeline1.DisposeAsync();

        // Second recovery resumes from the failed chunk: only the 15 uncommitted records replay.
        var healthySink = new FaultSink();
        var wal3 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await using var pipeline2 = new EventPipeline(
            healthySink, capacity: 100, enablePeriodicFlush: false, wal: wal3, dedupLedger: ledger);
        pipeline2.RecoveryCommitBatchSize = 10;

        await pipeline2.RecoverAsync();

        pipeline2.RecoveredCount.Should().Be(backlog - 10,
            "records committed by the first pass must not replay; everything after the horizon must");
        healthySink.AppendedEvents.Should().HaveCount(backlog - 10);
        await ledger.DisposeAsync();
    }

    [Fact]
    public async Task Recovery_ExternalPendingClaim_FailsClosed_IntraPassDuplicateStillSuppressed()
    {
        var walDir = Path.Combine(_rootDir, "wal_external_claim");
        Directory.CreateDirectory(walDir);

        // Two WAL copies of one identity plus a distinct second identity, all uncommitted.
        var timestamp = DateTimeOffset.UtcNow;
        var duplicated = CreateTradeEvent("EXT", 300, timestamp);
        var duplicatedCopy = CreateTradeEvent("EXT", 301, timestamp);
        var other = CreateTradeEvent("OTH", 302);
        var wal1 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await wal1.InitializeAsync();
        await wal1.AppendAsync(duplicated, duplicated.Type.ToString());
        await wal1.AppendAsync(duplicatedCopy, duplicatedCopy.Type.ToString());
        await wal1.AppendAsync(other, other.Type.ToString());
        await wal1.FlushAsync();
        await wal1.DisposeAsync();

        var ledger = await CreateLedgerAsync("ledger_external_claim");

        // An external (live-ingress) holder claims the "OTH" identity before recovery runs.
        var externalClaim = await ledger.TryReserveAsync(other, DedupLookupScope.LiveIngress, CancellationToken.None);
        externalClaim.IsReserved.Should().BeTrue();

        var failingSinkRun = new FaultSink();
        var wal2 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        var pipeline1 = new EventPipeline(
            failingSinkRun, capacity: 100, enablePeriodicFlush: false, wal: wal2, dedupLedger: ledger);

        // The intra-pass duplicate ("EXT" twice) must be suppressed, but the external claim on
        // "OTH" must fail recovery closed instead of being acknowledged away.
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline1.RecoverAsync());
        await pipeline1.DisposeAsync();

        // Once the external holder resolves (releases here), a retried recovery replays all
        // records that were never durably acknowledged.
        ledger.Release(externalClaim.Reservation).Should().BeTrue();
        var sink = new FaultSink();
        var wal3 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.NoSync });
        await using var pipeline2 = new EventPipeline(
            sink, capacity: 100, enablePeriodicFlush: false, wal: wal3, dedupLedger: ledger);

        await pipeline2.RecoverAsync();

        sink.AppendedEvents.Select(evt => evt.Symbol).Should().BeEquivalentTo(new[] { "EXT", "OTH" },
            "one copy of the duplicated identity and the externally-claimed record must both replay");
        pipeline2.RecoveredCount.Should().Be(2);
    }

    [Fact]
    public async Task Consumer_NoWal_PartialBatchAppendFailure_CommitsPersistedPrefixAndReleasesRest()
    {
        // No WAL: gate the consumer inside a first single-event batch, queue three more events
        // so they form one batch, then fail the middle append of that batch. The appended
        // prefix must stay durably deduplicated; only the unpersisted remainder is released.
        var ledger = await CreateLedgerAsync("ledger_prefix");
        var sink = new FaultSink { GateFirstAppend = true };

        await using (var pipeline = new EventPipeline(
            sink, capacity: 100, batchSize: 3, enablePeriodicFlush: false, dedupLedger: ledger))
        {
            var gateEvent = CreateTradeEvent("GATE0", 400);
            var eventA = CreateTradeEvent("PFA", 401);
            var eventB = CreateTradeEvent("PFB", 402);
            var eventC = CreateTradeEvent("PFC", 403);

            pipeline.TryPublish(gateEvent);
            await sink.FirstAppendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            pipeline.TryPublish(eventA);
            pipeline.TryPublish(eventB);
            pipeline.TryPublish(eventC);

            // Appends so far: #1 = GATE0 (held). Fail #3 = the batch's middle event (PFB).
            sink.FailOnAppendNumber = 3;
            sink.ReleaseGate();

            await WaitUntilAsync(() => pipeline.GetStatistics().ConsumerIterationFailures >= 1);

            // The persisted prefix (PFA) must be suppressed on re-send; the failed event (PFB)
            // must be claimable and persist.
            pipeline.TryPublish(eventA);
            pipeline.TryPublish(eventB);
            await WaitUntilAsync(() => sink.AppendedEvents.Count >= 3);
            await pipeline.FlushAsync(CancellationToken.None);

            sink.AppendedEvents.Select(evt => evt.Symbol).Should().BeEquivalentTo(
                new[] { "GATE0", "PFA", "PFB" },
                "the flushed prefix must not be re-appended and the unpersisted remainder must be re-acceptable");
            pipeline.DeduplicatedCount.Should().BeGreaterThanOrEqualTo(1,
                "the re-sent persisted prefix must be suppressed by its durably committed identity");
        }

        await ledger.DisposeAsync();
    }

    [Fact]
    public async Task Recovery_TrailingUnreadablePayload_CommittedPastAfterSingleAlert()
    {
        var walDir = Path.Combine(_rootDir, "wal_poison_tail");
        Directory.CreateDirectory(walDir);

        // A WAL whose only uncommitted record is semantic poison (checksum-valid, unreadable).
        var wal1 = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal1.InitializeAsync();
        await wal1.AppendAsync("not-a-market-event", "Trade");
        await wal1.FlushAsync();
        await wal1.DisposeAsync();

        var wal2 = new WriteAheadLog(walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Alert
        });
        long firstPassSignals = 0;
        wal2.CorruptionDetected += count => Interlocked.Add(ref firstPassSignals, count);
        await using (var pipeline1 = new EventPipeline(
            new FaultSink(), capacity: 100, enablePeriodicFlush: false, wal: wal2))
        {
            await pipeline1.RecoverAsync();
        }

        Interlocked.Read(ref firstPassSignals).Should().BeGreaterThanOrEqualTo(1,
            "the poison record must raise the Alert-mode signal on first discovery");

        // The horizon must have been committed past the poison record: a fresh recovery
        // neither replays nor re-alerts, matching the drop-once-with-signal contract.
        var wal3 = new WriteAheadLog(walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Alert
        });
        long secondPassSignals = 0;
        wal3.CorruptionDetected += count => Interlocked.Add(ref secondPassSignals, count);
        var sink2 = new FaultSink();
        await using (var pipeline2 = new EventPipeline(
            sink2, capacity: 100, enablePeriodicFlush: false, wal: wal3))
        {
            await pipeline2.RecoverAsync();
        }

        Interlocked.Read(ref secondPassSignals).Should().Be(0,
            "a committed-past poison record must not re-alert on every startup");
        sink2.AppendedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Consumer_ExternalPendingClaim_RetainsDeliveryUntilClaimResolves()
    {
        var walDir = Path.Combine(_rootDir, "wal_live_external");
        Directory.CreateDirectory(walDir);
        var wal = new WriteAheadLog(walDir, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await wal.InitializeAsync();

        var ledger = await CreateLedgerAsync("ledger_live_external");
        var evt = CreateTradeEvent("EXTLIVE", 500);

        // A concurrent caller sharing the store holds an in-flight claim on the identity.
        var externalClaim = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
        externalClaim.IsReserved.Should().BeTrue();

        var sink = new FaultSink();
        await using (var pipeline = new EventPipeline(
            sink, capacity: 100, enablePeriodicFlush: false, wal: wal, dedupLedger: ledger))
        {
            pipeline.TryPublish(evt);

            // The delivery must be retained (batch retrying), not discarded as a duplicate.
            await WaitUntilAsync(() => pipeline.GetStatistics().ConsumerIterationFailures >= 1);
            sink.AppendedEvents.Should().BeEmpty(
                "an event blocked by an external memory-only claim must not be persisted or dropped yet");
            pipeline.DeduplicatedCount.Should().Be(0,
                "an external pending claim is not a durability-confirmed duplicate");

            // Once the external holder releases without persisting, the retained delivery
            // claims the identity and persists — nothing was lost.
            ledger.Release(externalClaim.Reservation).Should().BeTrue();
            await WaitUntilAsync(() => sink.AppendedEvents.Count == 1);
            await pipeline.FlushAsync(CancellationToken.None);

            sink.AppendedEvents.Should().ContainSingle().Which.Symbol.Should().Be("EXTLIVE");
        }

        await ledger.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_WalAppendFailure_RetainsBatchUntilWalRecovers()
    {
        var walDir = Path.Combine(_rootDir, "wal_admission_fail");
        Directory.CreateDirectory(walDir);
        // Uninitialized with age-based rotation disabled: the first append throws instead of
        // lazily creating a segment, simulating a transiently unavailable WAL.
        var wal = new WriteAheadLog(walDir, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            MaxWalFileAge = null
        });

        var ledger = await CreateLedgerAsync("ledger_admission_fail");
        var sink = new FaultSink();
        await using (var pipeline = new EventPipeline(
            sink, capacity: 100, enablePeriodicFlush: false, wal: wal, dedupLedger: ledger))
        {
            var evt = CreateTradeEvent("WALDOWN", 600);
            pipeline.TryPublish(evt);

            // Even with no WAL sequence assigned anywhere in the batch, the failure must be
            // retained and retried — never abandoned into silent loss.
            await WaitUntilAsync(() => pipeline.GetStatistics().ConsumerIterationFailures >= 1);
            sink.AppendedEvents.Should().BeEmpty(
                "an unavailable WAL is an unavailable durability store; the batch must wait for it");

            // The WAL recovers: the retained batch completes its durable boundary.
            await wal.InitializeAsync();
            await WaitUntilAsync(() => sink.AppendedEvents.Count == 1);
            await pipeline.FlushAsync(CancellationToken.None);

            sink.AppendedEvents.Should().ContainSingle().Which.Symbol.Should().Be("WALDOWN");
        }

        await ledger.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_ForcedCancellation_ReleasesPendingClaimsForSuccessorPipelines()
    {
        var ledger = await CreateLedgerAsync("ledger_cancel_release");
        var sink = new FaultSink { GateFirstAppend = true };
        var evt = CreateTradeEvent("CXL", 700);

        var pipeline = new EventPipeline(
            sink, capacity: 100, enablePeriodicFlush: false, dedupLedger: ledger,
            finalFlushTimeout: TimeSpan.FromMilliseconds(250));
        pipeline.TryPublish(evt);
        await sink.FirstAppendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Disposal force-cancels the consumer while it is blocked inside the sink append with
        // the identity claim held. The claim must not outlive the pipeline: the dedup store is
        // an injected singleton that later pipelines keep using.
        await pipeline.DisposeAsync();

        var successorClaim = await ledger.TryReserveAsync(evt, DedupLookupScope.LiveIngress, CancellationToken.None);
        successorClaim.IsReserved.Should().BeTrue(
            "a cancelled consumer must release its pending claims so a successor can process the identity");
        ledger.Release(successorClaim.Reservation).Should().BeTrue();

        await ledger.DisposeAsync();
    }

    [Fact]
    public async Task Consumer_ExternalPendingClaim_ReleasesOwnBatchClaimsWhileWaiting()
    {
        // Two consumers sharing one dedup store can admit the same identities in crossed order:
        // if each waited on the other's claim while holding its own, both would deadlock in
        // their retry loops. A batch blocked by an external claim must therefore release every
        // claim it holds before waiting.
        var ledger = await CreateLedgerAsync("ledger_claim_release_wait");
        var sink = new FaultSink { GateFirstAppend = true };
        var gateEvt = CreateTradeEvent("GATE2", 800);
        var heldEvt = CreateTradeEvent("HELDX", 801);
        var blockedEvt = CreateTradeEvent("EXTY", 802);

        await using (var pipeline = new EventPipeline(
            sink, capacity: 100, batchSize: 3, enablePeriodicFlush: false, dedupLedger: ledger))
        {
            // Hold the consumer inside a first single-event batch so the two follow-up events
            // drain together as one batch.
            pipeline.TryPublish(gateEvt);
            await sink.FirstAppendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // A concurrent caller sharing the store claims the second event's identity.
            var externalClaim = await ledger.TryReserveAsync(blockedEvt, DedupLookupScope.LiveIngress, CancellationToken.None);
            externalClaim.IsReserved.Should().BeTrue();

            pipeline.TryPublish(heldEvt);
            pipeline.TryPublish(blockedEvt);
            sink.ReleaseGate();

            await WaitUntilAsync(() => pipeline.GetStatistics().ConsumerIterationFailures >= 1);

            // While the batch waits on the external claim, the first event's identity must be
            // reservable by a concurrent caller — the observable proof that the batch released
            // its own claims instead of holding them across the wait.
            DedupReservation probeClaim = default;
            var probeTimer = System.Diagnostics.Stopwatch.StartNew();
            while (!probeClaim.IsHeld && probeTimer.ElapsedMilliseconds < 10_000)
            {
                var probe = await ledger.TryReserveAsync(heldEvt, DedupLookupScope.LiveIngress, CancellationToken.None);
                if (probe.IsReserved)
                    probeClaim = probe.Reservation;
                else
                    await Task.Delay(5);
            }

            probeClaim.IsHeld.Should().BeTrue(
                "a batch waiting on an external claim must release its own claims so concurrent holders can make progress");

            // Both external claims resolve without persisting: the retained batch re-admits
            // every event and nothing is lost or double-counted.
            ledger.Release(probeClaim).Should().BeTrue();
            ledger.Release(externalClaim.Reservation).Should().BeTrue();

            await WaitUntilAsync(() => sink.AppendedEvents.Count >= 3);
            await pipeline.FlushAsync(CancellationToken.None);

            sink.AppendedEvents.Select(evt => evt.Symbol).Should().BeEquivalentTo(
                new[] { "GATE2", "HELDX", "EXTY" },
                "the retained batch must persist every event exactly once after the claims resolve");
            pipeline.DeduplicatedCount.Should().Be(0,
                "external pending claims must be waited out, never counted as duplicates");
        }

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
        private readonly SemaphoreSlim _gate = new(0);
        private int _appendFailuresRemaining;
        private int _flushFailuresRemaining;
        private int _successfulFlushCount;
        private int _appendAttempts;

        /// <summary>Fails exactly the Nth (1-based) append attempt; 0 disables.</summary>
        public int FailOnAppendNumber { get; set; }

        /// <summary>Blocks the first append attempt on a gate until <see cref="ReleaseGate"/>.</summary>
        public bool GateFirstAppend { get; init; }

        public TaskCompletionSource FirstAppendEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseGate() => _gate.Release(10_000);

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

        public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
        {
            var attempt = Interlocked.Increment(ref _appendAttempts);
            if (GateFirstAppend && attempt == 1)
            {
                FirstAppendEntered.TrySetResult();
                await _gate.WaitAsync(ct).ConfigureAwait(false);
            }

            if (FailOnAppendNumber > 0 && attempt == FailOnAppendNumber)
            {
                throw new InvalidOperationException("Injected sink append failure (positional)");
            }

            if (Volatile.Read(ref _appendFailuresRemaining) > 0)
            {
                Interlocked.Decrement(ref _appendFailuresRemaining);
                throw new InvalidOperationException("Injected sink append failure");
            }

            lock (_lock)
            { _appendedEvents.Add(evt); }
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
