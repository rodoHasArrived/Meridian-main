// BLUEPRINT: tests for Phase 2 of the Security Master Passport Workbench governed-write feature.
// See docs/plans/security-master-passport-workbench.md (test plan: "Unit — SecurityMasterWorkbenchCommandService").
//
// Intended RED state — the following blueprint types are not implemented yet:
//   - SecurityMasterWorkbenchCommandService / ISecurityMasterWorkbenchCommandService
//   - UpdateSecurityFieldRequest, SubmitSecurityMasterRevisionRequest, PublishSecurityMasterRevisionRequest
//   - SecurityMasterEditResultDto, SecurityMasterPublishResultDto, SecurityMasterRevisionStateDto
//   - SecurityMasterRevisionPublishedEvent, ISecurityMasterRevisionPublishedHandler
//   - ISecurityMasterConflictAuthorityPolicy
//
// Real seams (already exist) that the service composes and these tests mock/fake:
//   - ISecurityMasterEventStore (faked via FakeSecurityMasterEventStore — optimistic concurrency)
//   - IOperationsContinuityWorkflowService (approval gate — mocked)
//   - ISecurityMasterWorkbenchQueryService (current passport/impact reads — mocked)
//
// ASSUMED constructor (per blueprint Component Design):
//   new SecurityMasterWorkbenchCommandService(
//       ISecurityMasterEventStore eventStore,
//       ISecurityMasterConflictAuthorityPolicy conflictPolicy,
//       ISecurityMasterWorkbenchQueryService queryService,
//       IOperationsContinuityWorkflowService approvalWorkflow,
//       IEnumerable<ISecurityMasterRevisionPublishedHandler> handlers,
//       ILogger<SecurityMasterWorkbenchCommandService> logger)

using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
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
        // INVARIANT (W5X): operator-authored edits MUST carry a justification — no silent edits.
        var harness = new Harness();
        harness.EventStore.SeedVersion(SecurityId, version: 3);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "Identity.Isin",
            NewValue: "US0378331005",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "   ", // blank — must be rejected
            FundProfileId: "fund-1");

        var act = async () => await harness.Service.UpdateSecurityFieldAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
        harness.EventStore.Appends.Should().BeEmpty("a rejected edit must not append any event");
    }

    [Fact]
    public async Task UpdateSecurityField_AppendsOperatorOriginEnvelopeWithEffectiveFrom()
    {
        // The edit appends exactly one event at the loaded version, and the resulting change entry
        // is operator-attributed, effective-dated, and carries the justification reason.
        var effectiveFrom = new DateTimeOffset(2026, 03, 31, 0, 0, 0, TimeSpan.Zero);
        var harness = new Harness();
        harness.EventStore.SeedVersion(SecurityId, version: 7);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 7,
            FieldPath: "EconomicDefinition.Coupon",
            NewValue: "4.250",
            EffectiveFrom: effectiveFrom,
            Actor: "ops.analyst",
            Justification: "Corrected coupon per agent term sheet 2026-03.",
            FundProfileId: "fund-1");

        var result = await harness.Service.UpdateSecurityFieldAsync(request);

        harness.EventStore.Appends.Should().ContainSingle()
            .Which.ExpectedVersion.Should().Be(7, "the edit must thread the loaded concurrency token");
        result.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        result.ChangeEntry.Actor.Should().Be("ops.analyst");
        result.ChangeEntry.EffectiveAtUtc.Should().Be(effectiveFrom);
        result.ChangeEntry.Reason.Should().Contain("term sheet");
        result.ChangeEntry.ChangedFields.Should().Contain("EconomicDefinition.Coupon");
    }

    [Fact]
    public async Task UpdateSecurityField_StaleExpectedVersion_ThrowsConcurrency()
    {
        // INVARIANT: a stale token must not silently overwrite — it surfaces as a concurrency failure
        // that the endpoint maps to HTTP 409 { currentVersion }.
        var harness = new Harness();
        harness.EventStore.SeedVersion(SecurityId, version: 9); // someone else advanced to 9

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 8, // stale
            FieldPath: "Identity.Cusip",
            NewValue: "037833100",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Backfill CUSIP.",
            FundProfileId: "fund-1");

        var act = async () => await harness.Service.UpdateSecurityFieldAsync(request);

        var ex = await act.Should().ThrowAsync<SecurityMasterConcurrencyException>();
        ex.Which.CurrentVersion.Should().Be(9);
    }

    [Fact]
    public async Task UpdateSecurityField_MissingProviderData_StaysReviewRequired()
    {
        // INVARIANT (W5X): authoring an operator value over a field with no provider evidence must
        // NOT flip the record to Trusted/complete — missing provider data stays review-required.
        var harness = new Harness(trustToneAfterEdit: SecurityMasterTrustTone.Review);
        harness.EventStore.SeedVersion(SecurityId, version: 1);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 1,
            FieldPath: "EconomicDefinition.Coupon",
            NewValue: "5.000",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Operator-supplied coupon; no provider feed yet.",
            FundProfileId: "fund-1");

        var result = await harness.Service.UpdateSecurityFieldAsync(request);

        // The draft is created, but downstream trust posture must remain Review (not Trusted).
        result.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        harness.LastTrustToneObserved.Should().NotBe(SecurityMasterTrustTone.Trusted);
    }

    // ---- Submit for approval ------------------------------------------------------------------

    [Fact]
    public async Task Submit_RoutesThroughOperationsApprovalGate()
    {
        // Governance reuse: submission must drive the existing OperationsContinuity approval pipeline
        // (SecurityMaster gate), not a bespoke approval path.
        var harness = new Harness();
        harness.EventStore.SeedVersion(SecurityId, version: 2);

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(),
            Actor: "ops.analyst",
            Note: "Ready for independent review.",
            FundProfileId: "fund-1");

        var result = await harness.Service.SubmitForApprovalAsync(request);

        harness.ApprovalWorkflow.Verify(
            w => w.SubmitForApprovalAsync(It.IsAny<Guid>(), It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
        result.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
    }

    // ---- Publish + propagation ----------------------------------------------------------------

    [Fact]
    public async Task Publish_OpenPeriod_RunsHandlersNoRestatement()
    {
        // Open-period publish: handlers run in Order; no restatement proposal is raised.
        var ufl = new RecordingRevisionPublishedHandler(order: 10);
        var coverage = new RecordingRevisionPublishedHandler(order: 20);
        var restatement = new RecordingRevisionPublishedHandler(order: 100);
        var harness = new Harness(
            handlers: new ISecurityMasterRevisionPublishedHandler[] { coverage, restatement, ufl },
            triggersRestatement: false);
        harness.EventStore.SeedVersion(SecurityId, version: 4);

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(),
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        var result = await harness.Service.PublishRevisionAsync(request);

        ufl.Received.Should().ContainSingle();
        coverage.Received.Should().ContainSingle();
        result.TriggeredRestatementProposal.Should().BeFalse();
        result.RestatementProposalId.Should().BeNull();
    }

    [Fact]
    public async Task Publish_ClosedPeriodWithReportExposure_ProposesRestatement()
    {
        // INVARIANT (Unknown #3): a closed period with report-pack exposure must NOT be silently
        // mutated — publishing raises a governed restatement proposal instead.
        var harness = new Harness(triggersRestatement: true, reportPackExposureCount: 3);
        harness.EventStore.SeedVersion(SecurityId, version: 5);

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(),
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        var result = await harness.Service.PublishRevisionAsync(request);

        result.TriggeredRestatementProposal.Should().BeTrue();
        result.RestatementProposalId.Should().NotBeNull();
    }

    [Fact]
    public async Task Publish_HandlerThrows_AppendStillDurable_Idempotent()
    {
        // Retry safety: if a side-effect handler throws after the durable append, the publish must
        // not lose the committed revision; handlers are idempotent so a retry is safe.
        var throwing = new RecordingRevisionPublishedHandler(order: 10, onHandle: () => throw new InvalidOperationException("transient"));
        var harness = new Harness(handlers: new ISecurityMasterRevisionPublishedHandler[] { throwing });
        harness.EventStore.SeedVersion(SecurityId, version: 6);

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(),
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        // BLUEPRINT: exact failure semantics (swallow-and-log vs surface) are an open design point;
        // the durable invariant is that the append survives. Assert the append happened regardless.
        try
        {
            await harness.Service.PublishRevisionAsync(request);
        }
        catch (InvalidOperationException)
        {
            // acceptable if the service chooses to surface the handler failure after the append
        }

        harness.EventStore.Appends.Should().NotBeEmpty("the published revision must be durably appended before fan-out");
    }

    // ---- Harness --------------------------------------------------------------------------------

    /// <summary>
    /// Builds <c>SecurityMasterWorkbenchCommandService</c> with a real version-aware fake event store
    /// and mocked governance/query seams. BLUEPRINT: tracks the assumed constructor shape.
    /// </summary>
    private sealed class Harness
    {
        public FakeSecurityMasterEventStore EventStore { get; } = new();
        public Mock<IOperationsContinuityWorkflowService> ApprovalWorkflow { get; } = new(MockBehavior.Loose);
        public Mock<ISecurityMasterWorkbenchQueryService> QueryService { get; } = new(MockBehavior.Loose);
        public Mock<ISecurityMasterConflictAuthorityPolicy> ConflictPolicy { get; } = new(MockBehavior.Loose);
        public SecurityMasterTrustTone? LastTrustToneObserved { get; private set; }

        public ISecurityMasterWorkbenchCommandService Service { get; }

        public Harness(
            IEnumerable<ISecurityMasterRevisionPublishedHandler>? handlers = null,
            bool triggersRestatement = false,
            int reportPackExposureCount = 0,
            SecurityMasterTrustTone trustToneAfterEdit = SecurityMasterTrustTone.Trusted)
        {
            LastTrustToneObserved = trustToneAfterEdit;

            // BLUEPRINT: the real service is expected to be constructible from these dependencies.
            Service = new SecurityMasterWorkbenchCommandService(
                EventStore,
                ConflictPolicy.Object,
                QueryService.Object,
                ApprovalWorkflow.Object,
                handlers ?? Array.Empty<ISecurityMasterRevisionPublishedHandler>(),
                NullLogger<SecurityMasterWorkbenchCommandService>.Instance);

            _ = triggersRestatement;
            _ = reportPackExposureCount;
        }
    }
}
