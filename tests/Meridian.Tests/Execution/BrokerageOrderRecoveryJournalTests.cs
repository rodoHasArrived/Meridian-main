using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class BrokerageOrderRecoveryJournalTests
{
    [Fact]
    public async Task CheckpointProcessed_WaitsForRecoveryLock_ReadsNewAmendmentAndBrokerIdentityAfterAdmission()
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        var journal = new BrokerageOrderRecoveryJournal(store);
        var identities = new OrderBrokerIdentityIndex();
        var amendments = new ConcurrentDictionary<string, PendingOrderAmendment>(StringComparer.Ordinal);
        var acknowledged = State();
        var otherOrder = State() with { OrderId = "other-order" };
        identities.Confirm(acknowledged.OrderId, "broker-1");
        journal.RetainDispatch(acknowledged, "broker-1", ["broker-1"]);
        journal.RetainDispatch(otherOrder, null, []);
        var acknowledgmentVersion = journal.CaptureVersion(acknowledged.OrderId);
        var lockEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseLock = new ManualResetEventSlim();
        using var evidenceRead = new ManualResetEventSlim();
        var blocker = Task.Run(() => journal.CheckpointProcessed(otherOrder, Report(otherOrder), 0, () =>
        {
            lockEntered.TrySetResult();
            if (!releaseLock.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("Checkpoint test did not release the recovery lock.");
            return new BrokerageOrderCheckpointEvidence(null, [], false, null);
        }));
        var acknowledgmentFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acknowledgment = new Thread(() =>
        {
            try
            {
                journal.CheckpointProcessed(acknowledged, Report(acknowledged), acknowledgmentVersion, () =>
                {
                    evidenceRead.Set();
                    var hasPending = amendments.ContainsKey(acknowledged.OrderId);
                    identities.TryGetActiveId(acknowledged.OrderId, out var brokerId);
                    return new BrokerageOrderCheckpointEvidence(brokerId, identities.GetAliases(acknowledged.OrderId),
                        hasPending, amendments.GetValueOrDefault(acknowledged.OrderId));
                });
                acknowledgmentFinished.TrySetResult();
            }
            catch (Exception failure)
            {
                acknowledgmentFinished.TrySetException(failure);
            }
        })
        { IsBackground = true };
        var started = false;
        var pending = new PendingOrderAmendment(acknowledged, acknowledged with { Quantity = 20m }, "broker-1")
        { CandidateBrokerOrderId = "broker-2" };
        try
        {
            await lockEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            acknowledgment.Start();
            started = true;
            // This dedicated thread performs no other waits. Observe actual monitor contention,
            // or catch an incorrectly early evidence read, without relying on a scheduling delay.
            SpinWait.SpinUntil(() => evidenceRead.IsSet
                || (acknowledgment.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5)).Should().BeTrue();
            evidenceRead.IsSet.Should().BeFalse("checkpoint evidence must be read after entering the recovery lock");

            amendments[acknowledged.OrderId] = pending;
            identities.Confirm(acknowledged.OrderId, "broker-2");
            journal.SavePendingAmendment(acknowledged, "broker-2", identities.GetAliases(acknowledged.OrderId), pending);
            store.Load().Single(item => item.State.OrderId == acknowledged.OrderId).PendingAmendment.Should().Be(pending);
        }
        finally
        {
            releaseLock.Set();
            await blocker.WaitAsync(TimeSpan.FromSeconds(5));
            if (started)
                await acknowledgmentFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var retained = directory.CreateStore().Load().Single(item => item.State.OrderId == acknowledged.OrderId);
        retained.BrokerOrderId.Should().Be("broker-2");
        retained.BrokerOrderAliases.Should().Equal("broker-1", "broker-2");
        retained.PendingAmendment.Should().Be(pending);
        retained.RequiresAmendmentRecovery.Should().BeTrue();
        retained.RequiresRecovery.Should().BeTrue();
        journal.RequiresRecovery(acknowledged.OrderId).Should().BeTrue();
    }

    [Fact]
    public void CheckpointProcessed_PendingFlagOutlivesSnapshotRead_PreservesObservedUncertainty()
    {
        using var directory = new StoreDirectory();
        var journal = new BrokerageOrderRecoveryJournal(directory.CreateStore());
        var state = State();
        journal.RetainDispatch(state, "broker-1", ["broker-1"]);

        journal.CheckpointProcessed(state, Report(state), 0,
            () => new BrokerageOrderCheckpointEvidence("broker-1", ["broker-1"], true, null));

        var retained = directory.CreateStore().Load().Should().ContainSingle().Which;
        retained.PendingAmendment.Should().BeNull();
        retained.RequiresAmendmentRecovery.Should().BeTrue();
        retained.RequiresRecovery.Should().BeTrue();
        journal.RequiresRecovery(state.OrderId).Should().BeTrue();
    }

    [Fact]
    public void CheckpointProcessed_NewerRecoveryVersion_PersistsFillWithoutClearingUncertainty()
    {
        using var directory = new StoreDirectory();
        var journal = new BrokerageOrderRecoveryJournal(directory.CreateStore());
        var accepted = State();
        journal.RetainDispatch(accepted, "broker-1", ["broker-1"]);
        var reportVersion = journal.CaptureVersion(accepted.OrderId);

        // A report begins before a disconnect, then finishes its durable accounting handoff
        // after the disconnect marks newer uncertainty. Its fill is still a valid watermark.
        journal.RequireRecovery(accepted.OrderId);
        var partial = accepted with { FilledQuantity = 4m, AverageFillPrice = 101m, Status = OrderStatus.PartiallyFilled };
        journal.CheckpointProcessed(partial, Report(partial), reportVersion, Evidence("broker-1", ["broker-1"]));

        journal.RequiresRecovery(accepted.OrderId).Should().BeTrue();
        var reopened = new BrokerageOrderRecoveryJournal(directory.CreateStore());
        var restored = reopened.Restore("alpaca");
        restored.Orders.Should().ContainSingle().Which.State.Should().Be(partial);
        restored.Orders.Single().RequiresRecovery.Should().BeTrue();
        reopened.RequiresRecovery(accepted.OrderId).Should().BeTrue();

        // Evidence begun at the current version may now reconcile that uncertainty.
        journal.CheckpointProcessed(partial, Report(partial), journal.CaptureVersion(accepted.OrderId),
            Evidence("broker-1", ["broker-1"]));
        journal.RequiresRecovery(accepted.OrderId).Should().BeFalse();
        directory.CreateStore().Load().Should().ContainSingle().Which.RequiresRecovery.Should().BeFalse();
    }

    [Fact]
    public void CheckpointProcessed_DelayedAcknowledgement_DoesNotCheckpointUnfinishedNewerFill()
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        var journal = new BrokerageOrderRecoveryJournal(store);
        var accepted = State();
        journal.RetainDispatch(accepted, "broker-1", ["broker-1"]);
        var inFlightFill = accepted with { FilledQuantity = 4m, Status = OrderStatus.PartiallyFilled };

        journal.CheckpointProcessed(inFlightFill, Report(accepted), journal.CaptureVersion(accepted.OrderId),
            Evidence("broker-1", ["broker-1"]));

        store.Load().Should().ContainSingle().Which.State.FilledQuantity.Should().Be(0m);
        journal.RequiresRecovery(accepted.OrderId).Should().BeTrue();
        directory.CreateStore().Load().Should().ContainSingle().Which.State.FilledQuantity.Should().Be(0m);
    }

    [Fact]
    public void CheckpointProcessed_PendingAmendment_PreservesBothTermsAndScopedSizingAcrossRestart()
    {
        using var directory = new StoreDirectory();
        var journal = new BrokerageOrderRecoveryJournal(directory.CreateStore());
        var original = State() with
        {
            FundAccountId = Guid.Parse("ad311fb1-f517-4692-8eb9-a8dc7cbd211a"),
            RunId = "run-1",
            UsesFaceValuePercentageOfPar = true,
            RoutedNotional = 1000m,
            ContractMultiplier = 100m
        };
        var pending = new PendingOrderAmendment(original, original with { Quantity = 20m }, "broker-1")
        { CandidateBrokerOrderId = "candidate", CandidateClientOrderId = "candidate-client", ProposedTrail = 2m };
        journal.RetainDispatch(original, "broker-1", ["broker-1"]);

        journal.CheckpointProcessed(original, Report(original) with { ReportType = ExecutionReportType.ModificationPending },
            journal.CaptureVersion(original.OrderId), Evidence("broker-1", ["broker-1"], pending));

        var restored = new BrokerageOrderRecoveryJournal(directory.CreateStore()).Restore("alpaca");
        var retained = restored.Orders.Should().ContainSingle().Which;
        retained.State.Should().Be(original);
        retained.RequiresRecovery.Should().BeTrue();
        retained.RequiresAmendmentRecovery.Should().BeTrue();
        retained.PendingAmendment.Should().Be(pending);
        retained.BrokerOrderAliases.Should().Equal("broker-1");
    }

    [Fact]
    public void RetainDispatch_WriteFails_DoesNotPublishDispatchIdentity()
    {
        using var directory = new StoreDirectory();
        var journal = new BrokerageOrderRecoveryJournal(directory.CreateStore());
        directory.BlockWrites();

        var retain = () => journal.RetainDispatch(State(), null, []);

        AssertStorageFailure(retain);
        journal.IsDispatched("client-1").Should().BeFalse();
        journal.RequiresRecovery("client-1").Should().BeFalse();
    }

    [Fact]
    public void CheckpointProcessed_WriteFails_RetainsLastDurableWatermarkAndRecoveryBlock()
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        var journal = new BrokerageOrderRecoveryJournal(store);
        var accepted = State();
        journal.RetainDispatch(accepted, "broker-1", ["broker-1"]);
        directory.BlockWrites();
        var filled = accepted with { FilledQuantity = 10m, Status = OrderStatus.Filled };

        var checkpoint = () => journal.CheckpointProcessed(filled, Report(filled),
            journal.CaptureVersion(accepted.OrderId), Evidence("broker-1", ["broker-1"]));

        AssertStorageFailure(checkpoint);
        journal.RequiresRecovery(accepted.OrderId).Should().BeTrue();
        var retained = store.Load().Should().ContainSingle().Which;
        retained.State.Should().Be(accepted);
        retained.RequiresRecovery.Should().BeTrue();
    }

    [Fact]
    public void RequireRecovery_WriteFails_StillAdvancesUncertaintyWithoutAdvancingFillWatermark()
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        var journal = new BrokerageOrderRecoveryJournal(store);
        var accepted = State();
        journal.RetainDispatch(accepted, "broker-1", ["broker-1"]);
        journal.CheckpointProcessed(accepted, Report(accepted), 0, Evidence("broker-1", ["broker-1"]));
        var version = journal.CaptureVersion(accepted.OrderId);
        directory.BlockWrites();

        var require = () => journal.RequireRecovery(accepted.OrderId);

        AssertStorageFailure(require);
        journal.RequiresRecovery(accepted.OrderId).Should().BeTrue();
        journal.CaptureVersion(accepted.OrderId).Should().BeGreaterThan(version);
        store.Load().Should().ContainSingle().Which.State.Should().Be(accepted);
    }

    [Fact]
    public void SavePendingAmendment_ResolutionWriteFails_KeepsDurableCandidateAndRecoveryBlock()
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        var journal = new BrokerageOrderRecoveryJournal(store);
        var original = State();
        var pending = new PendingOrderAmendment(original, original with { Quantity = 20m }, "broker-1")
        { CandidateBrokerOrderId = "broker-2" };
        journal.RetainDispatch(original, "broker-1", ["broker-1"]);
        journal.SavePendingAmendment(original, "broker-1", ["broker-1"], pending);
        directory.BlockWrites();

        var resolve = () => journal.SavePendingAmendment(pending.Proposed, "broker-2", ["broker-1", "broker-2"], null);

        AssertStorageFailure(resolve);
        journal.RequiresRecovery(original.OrderId).Should().BeTrue();
        var retained = store.Load().Should().ContainSingle().Which;
        retained.PendingAmendment.Should().BeSameAs(pending);
        retained.State.Should().Be(original);
        retained.RequiresAmendmentRecovery.Should().BeTrue();
    }

    [Fact]
    public void TryCompactAndRemove_ExpectedStateReplaced_KeepsReplacementAndPermanentIdentity()
    {
        using var directory = new StoreDirectory();
        var journal = new BrokerageOrderRecoveryJournal(directory.CreateStore());
        var candidate = State() with { FilledQuantity = 10m, Status = OrderStatus.Filled };
        journal.RetainDispatch(candidate, "broker-2", ["broker-1", "broker-2"]);
        journal.CheckpointProcessed(candidate, Report(candidate), 0, Evidence("broker-2", ["broker-1", "broker-2"]));
        var replacement = candidate with { LastUpdatedAt = DateTimeOffset.UtcNow.AddMinutes(1) };
        var orders = new ConcurrentDictionary<string, OrderState>(StringComparer.Ordinal)
        { [candidate.OrderId] = replacement };

        var removed = journal.TryCompactAndRemove(candidate,
            () => orders.TryRemove(new KeyValuePair<string, OrderState>(candidate.OrderId, candidate)));

        removed.Should().BeFalse();
        orders[candidate.OrderId].Should().BeSameAs(replacement);
        journal.IsDispatched(candidate.OrderId).Should().BeTrue();
        journal.IsCompactedReport(Report(candidate)).Should().BeTrue();
        var restoredJournal = new BrokerageOrderRecoveryJournal(directory.CreateStore());
        var restored = restoredJournal.Restore("alpaca");
        restored.Orders.Should().BeEmpty();
        var tombstone = restored.Tombstones.Should().ContainSingle().Which;
        tombstone.BrokerOrderAliases.Should().Equal("broker-1", "broker-2");
        restoredJournal.IsDispatched(candidate.OrderId).Should().BeTrue();
        var identities = new OrderBrokerIdentityIndex();
        identities.Restore(tombstone);
        identities.TryResolveAlias("broker-1", out var originalOwner).Should().BeTrue();
        originalOwner.Should().Be(candidate.OrderId);
        identities.TryResolveAlias("broker-2", out var replacementOwner).Should().BeTrue();
        replacementOwner.Should().Be(candidate.OrderId);

        var contradiction = () => restoredJournal.IsCompactedReport(Report(candidate) with { FilledQuantity = 11m });
        contradiction.Should().Throw<InvalidDataException>()
            .WithMessage("Broker execution contradicts a completed order recovery watermark.");
    }

    [Fact]
    public void TryCompactAndRemove_RecoveryMarkedAfterSelection_DoesNotRemoveOrCompact()
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        var journal = new BrokerageOrderRecoveryJournal(store);
        var candidate = State() with { FilledQuantity = 10m, Status = OrderStatus.Filled };
        journal.RetainDispatch(candidate, "broker-1", ["broker-1"]);
        journal.CheckpointProcessed(candidate, Report(candidate), 0, Evidence("broker-1", ["broker-1"]));
        journal.RequireRecovery(candidate.OrderId);
        var removalAttempted = false;

        var removed = journal.TryCompactAndRemove(candidate, () => removalAttempted = true);

        removed.Should().BeFalse();
        removalAttempted.Should().BeFalse();
        journal.RequiresRecovery(candidate.OrderId).Should().BeTrue();
        store.Load().Should().ContainSingle().Which.State.Should().Be(candidate);
        directory.CreateStore().LoadTombstones().Should().BeEmpty();
    }

    [Fact]
    public void TryCompactAndRemove_WriteFails_DoesNotRemoveOrPublishTombstone()
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        var journal = new BrokerageOrderRecoveryJournal(store);
        var candidate = State() with { FilledQuantity = 10m, Status = OrderStatus.Filled };
        journal.RetainDispatch(candidate, "broker-1", ["broker-1"]);
        journal.CheckpointProcessed(candidate, Report(candidate), 0, Evidence("broker-1", ["broker-1"]));
        directory.BlockWrites();
        var removalAttempted = false;

        var compact = () => { journal.TryCompactAndRemove(candidate, () => removalAttempted = true); };

        AssertStorageFailure(compact);
        removalAttempted.Should().BeFalse();
        journal.IsCompactedReport(Report(candidate)).Should().BeFalse();
        journal.IsDispatched(candidate.OrderId).Should().BeTrue();
        store.Load().Should().ContainSingle().Which.State.Should().Be(candidate);
    }

    [Theory]
    [InlineData(OrderStatus.Accepted, true)]
    [InlineData(OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Expired, true)]
    [InlineData(OrderStatus.Filled, false)]
    [InlineData(OrderStatus.Rejected, false)]
    public void Restore_ReopensWorkingOrLateFillExposureEvenAfterPreviouslyCompletedCheckpoint(
        OrderStatus status, bool requiresRecovery)
    {
        using var directory = new StoreDirectory();
        var store = directory.CreateStore();
        var state = State() with { Status = status, FilledQuantity = status == OrderStatus.Filled ? 10m : 0m };
        store.Save(new RetainedBrokerageOrder(state, null, false));
        var journal = new BrokerageOrderRecoveryJournal(directory.CreateStore());

        journal.Restore("alpaca");

        journal.IsDispatched(state.OrderId).Should().BeTrue();
        journal.RequiresRecovery(state.OrderId).Should().Be(requiresRecovery);
    }

    private static Func<BrokerageOrderCheckpointEvidence> Evidence(
        string? brokerOrderId, string[] aliases, PendingOrderAmendment? pending = null) =>
        () => new BrokerageOrderCheckpointEvidence(brokerOrderId, aliases, pending is not null, pending);

    private static void AssertStorageFailure(Action action)
    {
        var failure = action.Should().Throw<Exception>().Which;
        (failure is IOException or UnauthorizedAccessException).Should().BeTrue();
    }

    private static OrderState State() => new()
    {
        OrderId = "client-1",
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 100m,
        Status = OrderStatus.Accepted
    };

    private static ExecutionReport Report(OrderState state) => new()
    {
        OrderId = state.OrderId,
        ClientOrderId = state.OrderId,
        Symbol = state.Symbol,
        Side = state.Side,
        ReportType = ExecutionReportType.Fill,
        OrderStatus = state.Status,
        OrderQuantity = state.Quantity,
        FilledQuantity = state.FilledQuantity
    };

    private sealed class StoreDirectory : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"meridian-recovery-journal-{Guid.NewGuid():N}");
        private string SnapshotPath => Path.Combine(_directory, "orders.json");

        internal FileBrokerageOrderRecoveryStore CreateStore()
        {
            Directory.CreateDirectory(_directory);
            var store = new FileBrokerageOrderRecoveryStore(SnapshotPath, "alpaca");
            store.BindScope("alpaca:paper:account-1");
            return store;
        }

        internal void BlockWrites()
        {
            File.Delete(SnapshotPath);
            Directory.CreateDirectory(SnapshotPath);
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
