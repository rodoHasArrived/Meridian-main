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
