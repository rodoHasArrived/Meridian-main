using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// W9-GOV-008 criterion 3, atomicity half. A hash chain proves nobody edited the events that are
/// there; it says nothing about an event that was never written. <c>SaveWithAuditAsync</c> saves the
/// mutation and then, as a separate operation, appends the audit event — so an append that fails
/// after the mutation commits leaves a perfectly valid chain that simply omits the mutation.
/// Tamper-evidence over a record that was never written is not tamper-evidence.
/// </summary>
/// <remarks>
/// These are failure-injection tests: the audit append is made to fail at exactly the point the plan
/// identifies, and the assertions are about what a later recovery can prove. The stores are separate
/// interfaces over separate artifacts, so there is no transaction to share — the marker is what makes
/// the interrupted pair decidable rather than invisible.
/// </remarks>
public sealed class AccountingAuditAtomicityTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-audit-atomicity-").FullName;

    private string SnapshotPath => Path.Combine(_root, "accounting-configuration.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory must never fail a test run.
        }
    }

    [Fact]
    public async Task ACrashBetweenTheMutationAndItsAuditAppend_IsDetectedAndReplayed()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var markers = new FileAccountingAuditPendingMarkerStore(
            FileAccountingAuditPendingMarkerStore.MarkerPathFor(SnapshotPath));
        var audit = new FailableAuditStore(store);

        // The mutation commits; the append does not. Without a marker this is invisible: the chain
        // over the events that did land verifies perfectly.
        audit.FailNextAppend = true;
        var save = async () => await CreateService(store, audit, markers).UpsertChartNodeAsync(ChartRequest());
        await save.Should().ThrowAsync<InvalidOperationException>();

        var retainedBeforeRecovery = await audit.ListAsync("fund-alpha");
        retainedBeforeRecovery.Should().BeEmpty("the append is the operation that failed");

        var marker = await markers.ReadAsync();
        marker.Should().NotBeNull("the interrupted pair must leave evidence that it was interrupted");

        var recovery = await CreateService(store, audit, markers).RecoverPendingAuditAsync();

        recovery.Outcome.Should().Be(AccountingAuditRecoveryOutcome.AuditReplayed);
        recovery.AuditEventId.Should().Be(marker!.AuditEvent.AuditEventId);
        (await audit.ListAsync("fund-alpha")).Should().ContainSingle()
            .Which.AuditEventId.Should().Be(marker.AuditEvent.AuditEventId);
        (await markers.ReadAsync()).Should().BeNull("a resolved marker must not re-fire");
    }

    [Fact]
    public async Task TheNextMutation_ResolvesAnInterruptedPairBeforeStartingItsOwn()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var markers = new FileAccountingAuditPendingMarkerStore(
            FileAccountingAuditPendingMarkerStore.MarkerPathFor(SnapshotPath));
        var audit = new FailableAuditStore(store);

        audit.FailNextAppend = true;
        var save = async () => await CreateService(store, audit, markers).UpsertChartNodeAsync(ChartRequest());
        await save.Should().ThrowAsync<InvalidOperationException>();

        // An operator simply carries on. The interrupted pair must be attributed to the mutation that
        // caused it, not silently absorbed into this one.
        await CreateService(store, audit, markers).UpsertChartNodeAsync(ChartRequest("chart-two"));

        var retained = await audit.ListAsync("fund-alpha");
        retained.Should().HaveCount(2, "both the recovered append and the new one are retained");
        (await markers.ReadAsync()).Should().BeNull();
    }

    [Fact]
    public async Task AMutationThatNeverLanded_IsDiscardedRatherThanAudited()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var markers = new FileAccountingAuditPendingMarkerStore(
            FileAccountingAuditPendingMarkerStore.MarkerPathFor(SnapshotPath));
        var audit = new FailableAuditStore(store);

        var service = CreateService(store, audit, markers);

        // One completed mutation, so the retained audit event's after-hash IS the current workspace
        // hash — the service's own record of the state, rather than one recomputed by the test.
        await service.UpsertChartNodeAsync(ChartRequest());
        var currentHash = (await audit.ListAsync("fund-alpha")).Single().AfterHash;

        // A marker whose before-hash the workspace matches and whose after-hash it does not: the
        // mutation never landed. Auditing it would record something that did not happen.
        await markers.WriteAsync(new AccountingAuditPendingMarker(
            AuditEvent(beforeHash: currentHash, afterHash: new string('9', 64)),
            DateTimeOffset.UtcNow));

        var recovery = await service.RecoverPendingAuditAsync();

        recovery.Outcome.Should().Be(AccountingAuditRecoveryOutcome.MutationDiscarded);
        (await audit.ListAsync("fund-alpha")).Should().ContainSingle(
            "only the completed mutation is audited; the discarded one never happened");
        (await markers.ReadAsync()).Should().BeNull();
    }

    [Fact]
    public async Task AStateMatchingNeitherHash_IsRaisedAsAnIncidentRatherThanGuessed()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var markers = new FileAccountingAuditPendingMarkerStore(
            FileAccountingAuditPendingMarkerStore.MarkerPathFor(SnapshotPath));
        var audit = new FailableAuditStore(store);
        var service = CreateService(store, audit, markers);

        // A retained workspace is what makes this the unreconcilable case. Without one the scope has
        // nothing saved in it, which is decidable -- no save landed -- and is answered by discarding.
        // This test previously omitted the mutation and reached the incident path only because the
        // hash of an absent workspace was unstable, which is the defect the recovery no longer
        // depends on.
        await service.UpsertChartNodeAsync(ChartRequest());

        await markers.WriteAsync(new AccountingAuditPendingMarker(
            AuditEvent(beforeHash: new string('8', 64), afterHash: new string('9', 64)),
            DateTimeOffset.UtcNow));

        // Something changed the workspace between the interrupted mutation and this recovery. Neither
        // replaying nor discarding states the truth, and quietly picking one would put a false record
        // into the log whose only purpose is being trustworthy.
        var recover = async () => await service.RecoverPendingAuditAsync();

        await recover.Should().ThrowAsync<AccountingAuditRecoveryException>();
        (await markers.ReadAsync()).Should().NotBeNull("an unresolved incident must stay visible");
    }

    [Fact]
    public async Task ACrashDuringTheFirstMutationOnAFundProfile_DoesNotBlockEveryMutationAfterIt()
    {
        // The recovery decided whether an interrupted mutation had landed by hashing the workspace
        // and comparing. For a scope with nothing retained, LoadWorkspaceAsync synthesizes an empty
        // workspace stamped with the current instant, so the hash written into the marker and the
        // hash taken during recovery were different values for the same absence. It matched neither
        // BeforeHash nor AfterHash, so recovery raised the unreconcilable incident -- and because
        // every mutation runs recovery first, one crash during the first mutation on a fund profile
        // stopped all of them, permanently, with no way to clear the marker.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var markers = new FileAccountingAuditPendingMarkerStore(
            FileAccountingAuditPendingMarkerStore.MarkerPathFor(SnapshotPath));
        var audit = new FailableAuditStore(store);
        var service = CreateService(store, audit, markers);

        // The marker a crash between the marker write and the save would leave behind: nothing is
        // retained for this fund profile, and the hashes are the unreproducible ones.
        await markers.WriteAsync(new AccountingAuditPendingMarker(
            AuditEvent(beforeHash: new string('8', 64), afterHash: new string('9', 64)),
            DateTimeOffset.UtcNow,
            BeforeStateRetained: false));

        var recovery = await service.RecoverPendingAuditAsync();

        recovery.Outcome.Should().Be(AccountingAuditRecoveryOutcome.MutationDiscarded);
        (await markers.ReadAsync()).Should().BeNull();
        (await audit.ListAsync("fund-alpha")).Should().BeEmpty(
            "a mutation that never landed is not audited");

        // The part that matters: the service still works.
        await service.UpsertChartNodeAsync(ChartRequest());
        (await audit.ListAsync("fund-alpha")).Should().ContainSingle();
    }

    [Fact]
    public async Task AWorkspaceThatVanishedAfterItsMutation_IsAnIncidentRatherThanADiscard()
    {
        // Codex review finding on PR #2871. Absence at recovery time is ambiguous: it reads the same
        // whether the save never landed or the retained state was destroyed afterwards. Treating
        // both as "never landed" clears the one marker recording the loss AND the unaudited
        // mutation. SaveAsync only inserts or replaces, so it cannot produce absence -- which is why
        // the marker records what was retained when the intent was declared.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var markers = new FileAccountingAuditPendingMarkerStore(
            FileAccountingAuditPendingMarkerStore.MarkerPathFor(SnapshotPath));
        var audit = new FailableAuditStore(store);
        var service = CreateService(store, audit, markers);

        await markers.WriteAsync(new AccountingAuditPendingMarker(
            AuditEvent(), DateTimeOffset.UtcNow, BeforeStateRetained: true));

        var recover = async () => await service.RecoverPendingAuditAsync();

        await recover.Should().ThrowAsync<AccountingAuditRecoveryException>();
        (await markers.ReadAsync()).Should().NotBeNull("an unresolved incident must stay visible");
    }

    [Fact]
    public async Task AMarkerFromAnOlderBuild_TakesTheConservativeBranch()
    {
        // BeforeStateRetained defaults to true, so a marker written before the field existed raises
        // rather than silently discarding a mutation whose retained state may have been lost.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var markers = new FileAccountingAuditPendingMarkerStore(
            FileAccountingAuditPendingMarkerStore.MarkerPathFor(SnapshotPath));
        var service = CreateService(store, new FailableAuditStore(store), markers);

        await markers.WriteAsync(new AccountingAuditPendingMarker(AuditEvent(), DateTimeOffset.UtcNow));

        var recover = async () => await service.RecoverPendingAuditAsync();

        await recover.Should().ThrowAsync<AccountingAuditRecoveryException>();
    }

    [Fact]
    public async Task AFirstMutationWhoseSaveLandedAndWasThenLost_IsAnIncidentRatherThanADiscard()
    {
        // Third Codex review round. BeforeStateRetained alone cannot settle the first mutation in a
        // scope: nothing was retained beforehand, so absence at recovery reads the same whether the
        // save never ran or it completed and the state was destroyed afterwards. The marker now
        // records that the save returned, which distinguishes them -- without reintroducing the
        // permanent block that treating every first-mutation crash as an incident would cause.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var markers = new FileAccountingAuditPendingMarkerStore(
            FileAccountingAuditPendingMarkerStore.MarkerPathFor(SnapshotPath));
        var service = CreateService(store, new FailableAuditStore(store), markers);

        await markers.WriteAsync(new AccountingAuditPendingMarker(
            AuditEvent(),
            DateTimeOffset.UtcNow,
            BeforeStateRetained: false,
            Phase: AccountingAuditPendingMarkerPhase.Saved));

        var recover = async () => await service.RecoverPendingAuditAsync();

        await recover.Should().ThrowAsync<AccountingAuditRecoveryException>();
        (await markers.ReadAsync()).Should().NotBeNull("the lost state must stay visible");
    }

    [Fact]
    public async Task AWorkspaceRolledBackToItsBeforeStateAfterASavedMarker_IsAnIncident()
    {
        // Fifth Codex review round. The Saved phase was consulted only when nothing was retained,
        // but absence is not the only shape a rollback takes: a workspace restored to its exact
        // before-state hashes to BeforeHash, and recovery discarded it as a mutation that never
        // happened. Saved proves it did happen, so that is state loss plus an unaudited mutation.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var markers = new FileAccountingAuditPendingMarkerStore(
            FileAccountingAuditPendingMarkerStore.MarkerPathFor(SnapshotPath));
        var audit = new FailableAuditStore(store);
        var service = CreateService(store, audit, markers);

        // One completed mutation, so the retained workspace hash is the service's own record of it.
        await service.UpsertChartNodeAsync(ChartRequest());
        var retainedHash = (await audit.ListAsync("fund-alpha")).Single().AfterHash;

        // A marker whose before-hash the workspace now matches -- but which says the save landed.
        await markers.WriteAsync(new AccountingAuditPendingMarker(
            AuditEvent(beforeHash: retainedHash, afterHash: new string('9', 64)),
            DateTimeOffset.UtcNow,
            BeforeStateRetained: true,
            Phase: AccountingAuditPendingMarkerPhase.Saved));

        var recover = async () => await service.RecoverPendingAuditAsync();

        await recover.Should().ThrowAsync<AccountingAuditRecoveryException>();
        (await markers.ReadAsync()).Should().NotBeNull("the rolled-back state must stay visible");
    }

    [Fact]
    public async Task ACompletedPair_LeavesNoMarkerBehind()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var markers = new FileAccountingAuditPendingMarkerStore(
            FileAccountingAuditPendingMarkerStore.MarkerPathFor(SnapshotPath));
        var audit = new FailableAuditStore(store);

        await CreateService(store, audit, markers).UpsertChartNodeAsync(ChartRequest());

        (await markers.ReadAsync()).Should().BeNull();
        (await audit.ListAsync("fund-alpha")).Should().ContainSingle();
    }

    [Fact]
    public async Task ALateClear_CannotEraseANewerIntent()
    {
        var markers = new FileAccountingAuditPendingMarkerStore(Path.Combine(_root, "marker.json"));
        var superseded = AuditEvent();
        var current = AuditEvent();

        await markers.WriteAsync(new AccountingAuditPendingMarker(superseded, DateTimeOffset.UtcNow));
        await markers.WriteAsync(new AccountingAuditPendingMarker(current, DateTimeOffset.UtcNow));

        // A stale clear arriving after a newer mutation declared its intent would leave that
        // mutation's crash undetectable — the precise failure this store exists to catch.
        await markers.ClearAsync(superseded.AuditEventId);

        var retained = await markers.ReadAsync();
        retained!.AuditEvent.AuditEventId.Should().Be(current.AuditEventId);
    }

    [Fact]
    public async Task WithoutAMarkerStore_TheHistoricalOrderingStillApplies()
    {
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var audit = new FailableAuditStore(store) { FailNextAppend = true };

        var save = async () => await CreateService(store, audit, markers: null).UpsertChartNodeAsync(ChartRequest());
        await save.Should().ThrowAsync<InvalidOperationException>();

        // Documented, not endorsed: the mutation is retained and nothing records that its audit event
        // is missing. This is what the marker store exists to change, and pinning it here keeps the
        // difference visible rather than implied.
        var recovery = await CreateService(store, audit, markers: null).RecoverPendingAuditAsync();
        recovery.Outcome.Should().Be(AccountingAuditRecoveryOutcome.Nothing);
        (await audit.ListAsync("fund-alpha")).Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentMutations_DoNotOverwriteEachOthersPendingMarker()
    {
        // Codex review finding on PR #2866. The marker is a single slot, so overlapping mutations do
        // not merely race -- they destroy each other's evidence. Both callers could finish recovery
        // before either declared, the second declaration overwrote the first, and a crash in the
        // first was then left with no marker at all: permanently unaudited, with nothing recording
        // that it had been interrupted. That is the exact gap the marker exists to close.
        var store = new FileAccountingConfigurationStore(SnapshotPath);
        var markers = new FileAccountingAuditPendingMarkerStore(
            FileAccountingAuditPendingMarkerStore.MarkerPathFor(SnapshotPath));
        var audit = new FailableAuditStore(store);
        var service = CreateService(store, audit, markers);

        await Task.WhenAll(Enumerable
            .Range(0, 8)
            .Select(index => service.UpsertChartNodeAsync(ChartRequest($"node-{index.ToString()}"))));

        // Every mutation is audited exactly once, and none leaves an unresolved marker behind.
        var retained = await audit.ListAsync("fund-alpha");
        retained.Should().HaveCount(8);
        retained.Select(item => item.AuditEventId).Should().OnlyHaveUniqueItems();
        (await markers.ReadAsync()).Should().BeNull();

        // And the chain the appends built is intact, which a torn interleaving would not leave.
        (await store.VerifyAuditChainAsync()).IsValid.Should().BeTrue();
    }

    private static AccountingConfigurationService CreateService(
        FileAccountingConfigurationStore store,
        IAccountingActionAuditStore audit,
        IAccountingAuditPendingMarkerStore? markers)
        => new(store, audit, ledgerBookService: null, pendingAuditMarkers: markers);

    private static UpsertChartOfAccountsNodeRequest ChartRequest(string nodeId = "node-one")
        => new(
            "fund-alpha",
            new ChartOfAccountsNodeDto(nodeId, $"assets.{nodeId}", "Cash", "Asset"),
            Actor: "operator@example.test");

    private static AccountingActionAuditEventDto AuditEvent(
        string? beforeHash = null,
        string? afterHash = null)
        => new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Actor: "operator@example.test",
            Action: "chart.upsert",
            FundProfileId: "fund-alpha",
            LedgerBookId: null,
            CorrelationId: null,
            BeforeHash: beforeHash ?? new string('0', 64),
            AfterHash: afterHash ?? new string('1', 64),
            ValidationIssues: [],
            EvidenceLinks: []);

    /// <summary>An audit store that can be made to fail its next append, on demand.</summary>
    private sealed class FailableAuditStore(FileAccountingConfigurationStore inner) : IAccountingActionAuditStore
    {
        public bool FailNextAppend { get; set; }

        public Task AppendAsync(AccountingActionAuditEventDto auditEvent, CancellationToken ct = default)
        {
            if (FailNextAppend)
            {
                FailNextAppend = false;
                throw new InvalidOperationException("Injected audit append failure.");
            }

            return inner.AppendAsync(auditEvent, ct);
        }

        public Task<IReadOnlyList<AccountingActionAuditEventDto>> ListAsync(
            string? fundProfileId = null,
            Guid? ledgerBookId = null,
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
            => inner.ListAsync(fundProfileId, ledgerBookId, ct, tenantId, companyId);
    }
}
