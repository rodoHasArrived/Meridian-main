using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.SecurityMaster.Workbench;

public sealed class SecurityMasterWorkbenchCommandServiceTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ---- UpdateSecurityField ------------------------------------------------------------------

    [Fact]
    public async Task UpdateSecurityField_OperatorOriginNoJustification_Throws()
    {
        var harness = new Harness(currentVersion: 3);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "Identity.Isin",
            NewValue: "US0378331005",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "   ");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>();
        harness.EventStore.Appends.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateSecurityField_StaleExpectedVersion_ThrowsConcurrency()
    {
        var harness = new Harness(currentVersion: 9);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 8, // stale
            FieldPath: "Identity.Cusip",
            NewValue: "037833100",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Backfill CUSIP.");

        var ex = await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<SecurityMasterConcurrencyException>();
        ex.Which.CurrentVersion.Should().Be(9);
        ex.Which.ExpectedVersion.Should().Be(8);
        harness.EventStore.Appends.Should().BeEmpty("a stale edit is rejected before any append");
    }

    [Fact]
    public async Task UpdateSecurityField_HappyPath_StagesOverrideAnnotation_WithoutEconomicStreamAppend()
    {
        var effectiveFrom = new DateTimeOffset(2026, 03, 31, 0, 0, 0, TimeSpan.Zero);
        var harness = new Harness(currentVersion: 7);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 7,
            FieldPath: "EconomicDefinition.Coupon",
            NewValue: "4.250",
            EffectiveFrom: effectiveFrom,
            Actor: "ops.analyst",
            Justification: "Corrected coupon per agent term sheet.");

        var result = await harness.Service.UpdateSecurityFieldAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        result.NewVersion.Should().Be(7, "an overlay annotation does not advance the canonical security version");
        result.ChangeEntry.EffectiveAtUtc.Should().Be(effectiveFrom);
        result.ChangeEntry.ChangedFields.Should().Contain("EconomicDefinition.Coupon");

        // The edit is staged purely as an override read-model annotation. It must NOT be appended to
        // the economic event stream — that stream is replayed verbatim to rebuild the passport, so a
        // partial field-edit payload would corrupt the economic definition on the next reload.
        harness.EventStore.Appends.Should().BeEmpty("overlay edits never enter the economic replay stream");
        harness.Overrides.Verify(
            o => o.PatchAsync(
                SecurityId,
                It.Is<OperatorOverridesPatchRequest>(p => p.SetValues != null && p.SetValues.ContainsKey("EconomicDefinition.Coupon")),
                "ops.analyst",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---- ResolveSourceConflict (validation guards) --------------------------------------------

    [Fact]
    public async Task ResolveSourceConflict_NoReason_Throws()
    {
        var harness = new Harness(currentVersion: 2);

        var request = new ResolveSourceConflictRequest(
            SecurityId: SecurityId,
            ConflictId: Guid.NewGuid(),
            ExpectedVersion: 2,
            ChosenWinnerSource: "Edgar",
            Actor: "ops.analyst",
            Reason: "  ");

        await harness.Service.Invoking(s => s.ResolveSourceConflictAsync(request))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ResolveSourceConflict_StaleExpectedVersion_ThrowsConcurrency()
    {
        var harness = new Harness(currentVersion: 5);

        var request = new ResolveSourceConflictRequest(
            SecurityId: SecurityId,
            ConflictId: Guid.NewGuid(),
            ExpectedVersion: 4, // stale
            ChosenWinnerSource: "Edgar",
            Actor: "ops.analyst",
            Reason: "Prefer Edgar.");

        await harness.Service.Invoking(s => s.ResolveSourceConflictAsync(request))
            .Should().ThrowAsync<SecurityMasterConcurrencyException>();
    }

    // ---- Submit / Approve through the gate ----------------------------------------------------

    [Fact]
    public async Task Submit_WithoutWorkflow_ReturnsSubmittedState()
    {
        var harness = new Harness(currentVersion: 2);
        var revisionId = Guid.NewGuid();

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            Note: "Ready for review.");

        var result = await harness.Service.SubmitForApprovalAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
        result.RevisionId.Should().Be(revisionId);
        harness.Workflow.Verify(
            w => w.SubmitForApprovalAsync(It.IsAny<Guid>(), It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Submit_WithWorkflow_RoutesThroughApprovalGate()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 2);
        harness.Workflow
            .Setup(w => w.SubmitForApprovalAsync(workflowId, It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 3));

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(),
            Actor: "ops.analyst",
            Note: "Submit through gate.",
            FundProfileId: null,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 2,
            Reviewer: "ops.reviewer",
            ReportPackId: "rp-1");

        var result = await harness.Service.SubmitForApprovalAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
        harness.Workflow.Verify(
            w => w.SubmitForApprovalAsync(workflowId, It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Approve_RoutesThroughApprovalGate_AndReturnsApproved()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 5));

        var request = new ApproveSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(),
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 4,
            Actor: "ops.reviewer",
            Reviewer: "ops.reviewer",
            Rationale: "Approved.",
            ReportPackId: "rp-1");

        var result = await harness.Service.ApproveRevisionAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
        harness.Workflow.Verify(
            w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Approve_WhenGateBlocks_Throws()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsTransitionResultDto(false, "BLOCKED", "Independent reviewer required.", null, [], []));

        var request = new ApproveSecurityMasterRevisionRequest(
            SecurityId, Guid.NewGuid(), workflowId, 4, "ops.analyst", "ops.analyst", "Approve.", "rp-1");

        await harness.Service.Invoking(s => s.ApproveRevisionAsync(request))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- Publish ------------------------------------------------------------------------------

    [Fact]
    public async Task Publish_FansOutHandlersInOrder_AndReturnsResult()
    {
        var log = new List<int>();
        var ufl = new RecordingHandler(order: 10, invocationLog: log);
        var coverage = new RecordingHandler(order: 20, invocationLog: log);
        var harness = new Harness(currentVersion: 4, handlers: [coverage, ufl]); // intentionally out of order

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(),
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        var result = await harness.Service.PublishRevisionAsync(request);

        ufl.Received.Should().ContainSingle();
        coverage.Received.Should().ContainSingle();
        log.Should().Equal(new[] { 10, 20 });
        result.RestatementRequired.Should().BeFalse();
        result.RestatementCandidates.Should().BeEmpty();
        result.InvalidatedProjections.Should().HaveCount(2);
    }

    [Fact]
    public async Task Publish_HandlerThrows_DoesNotFailPublish()
    {
        var throwing = new RecordingHandler(order: 10, onHandle: () => throw new InvalidOperationException("transient"));
        var harness = new Harness(currentVersion: 6, handlers: [throwing]);

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(),
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        var result = await harness.Service.PublishRevisionAsync(request);

        result.Should().NotBeNull();
        result.InvalidatedProjections.Should().BeEmpty();
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static OperationsTransitionResultDto SuccessTransition(long newVersion)
        => new(true, null, null, null, [], [], newVersion);

    private sealed class Harness
    {
        public FakeEventStore EventStore { get; }
        public Mock<IOperatorOverridesStore> Overrides { get; } = new(MockBehavior.Loose);
        public Mock<ISecurityMasterConflictAuthorityPolicy> Policy { get; } = new(MockBehavior.Loose);
        public Mock<ISecurityMasterConflictService> ConflictService { get; } = new(MockBehavior.Loose);
        public Mock<ISecurityMasterWorkbenchQueryService> QueryService { get; } = new(MockBehavior.Loose);
        public Mock<IOperationsContinuityWorkflowService> Workflow { get; } = new(MockBehavior.Loose);
        public SecurityMasterWorkbenchCommandService Service { get; }

        public Harness(long currentVersion, IEnumerable<ISecurityMasterRevisionPublishedHandler>? handlers = null)
        {
            EventStore = new FakeEventStore(currentVersion);

            Overrides
                .Setup(o => o.PatchAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, OperatorOverridesPatchRequest _, string actor, CancellationToken _) =>
                    new OperatorOverridesDto(id, new Dictionary<string, string>(), actor, DateTimeOffset.UtcNow));

            QueryService
                .Setup(q => q.GetTrustSnapshotAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SecurityMasterTrustSnapshotDto?)null);

            Service = new SecurityMasterWorkbenchCommandService(
                EventStore,
                Overrides.Object,
                Policy.Object,
                ConflictService.Object,
                QueryService.Object,
                Workflow.Object,
                handlers ?? Array.Empty<ISecurityMasterRevisionPublishedHandler>(),
                NullLogger<SecurityMasterWorkbenchCommandService>.Instance);
        }
    }

    /// <summary>Version-aware fake: LoadAsync reports the configured stream version; AppendAsync is recorded.</summary>
    private sealed class FakeEventStore : ISecurityMasterEventStore
    {
        private readonly long _version;

        public FakeEventStore(long version) => _version = version;

        public List<(Guid SecurityId, long ExpectedVersion, IReadOnlyList<SecurityMasterEventEnvelope> Events)> Appends { get; } = new();

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> LoadAsync(Guid securityId, CancellationToken ct = default)
        {
            if (_version <= 0)
            {
                return Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);
            }

            var envelope = new SecurityMasterEventEnvelope(
                GlobalSequence: _version,
                SecurityId: securityId,
                StreamVersion: _version,
                EventType: "seed",
                EventTimestamp: DateTimeOffset.UnixEpoch,
                Actor: "seed",
                CorrelationId: null,
                CausationId: null,
                Payload: default,
                Metadata: default);
            return Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([envelope]);
        }

        public Task AppendAsync(Guid securityId, long expectedVersion, IReadOnlyList<SecurityMasterEventEnvelope> events, CancellationToken ct = default)
        {
            Appends.Add((securityId, expectedVersion, events));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> LoadSinceSequenceAsync(long sequenceExclusive, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);

        public Task<long> GetLatestSequenceAsync(CancellationToken ct = default) => Task.FromResult(_version);

        public Task AppendCorporateActionAsync(CorporateActionDto action, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<CorporateActionDto>> LoadCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionDto>>([]);
    }

    private sealed class RecordingHandler : ISecurityMasterRevisionPublishedHandler
    {
        private readonly Action? _onHandle;
        private readonly List<int>? _invocationLog;

        public RecordingHandler(int order, Action? onHandle = null, List<int>? invocationLog = null)
        {
            Order = order;
            _onHandle = onHandle;
            _invocationLog = invocationLog;
        }

        public int Order { get; }

        public List<SecurityMasterRevisionPublishedEvent> Received { get; } = new();

        public Task HandleAsync(SecurityMasterRevisionPublishedEvent evt, CancellationToken ct = default)
        {
            _invocationLog?.Add(Order);
            Received.Add(evt);
            _onHandle?.Invoke();
            return Task.CompletedTask;
        }
    }
}
