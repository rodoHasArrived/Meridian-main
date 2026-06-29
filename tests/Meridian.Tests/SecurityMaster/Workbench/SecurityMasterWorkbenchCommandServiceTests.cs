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
    public async Task UpdateSecurityField_UnknownSecurity_Throws()
    {
        // currentVersion 0 == no events == the security was never created.
        var harness = new Harness(currentVersion: 0);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 0,
            FieldPath: "Identity.Isin",
            NewValue: "US0378331005",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Backfill ISIN.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<InvalidOperationException>();
        harness.Overrides.Verify(
            o => o.PatchAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

        // A durable Draft revision is opened with a real, server-issued id (not a transient client
        // value), carrying the field-edit metadata so a later publish can scope downstream impact.
        var stored = await harness.Revisions.GetAsync(result.RevisionId);
        stored.Should().NotBeNull();
        stored!.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        stored.SecurityId.Should().Be(SecurityId);
        stored.FieldPath.Should().Be("EconomicDefinition.Coupon");
        stored.FieldEffectiveFrom.Should().Be(effectiveFrom);
        stored.FieldJustification.Should().Be("Corrected coupon per agent term sheet.");

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

    // ---- ResolveSourceConflict (winner-candidate validation) ----------------------------------

    [Theory]
    [InlineData("Edgar")]        // the current winning source
    [InlineData("edgar")]        // case-insensitive
    [InlineData("  Edgar  ")]    // whitespace-tolerant
    [InlineData("Polygon")]      // the challenger source
    public void EnsureChosenWinnerIsCandidate_ValidCandidate_DoesNotThrow(string chosen)
    {
        var assessment = BuildAssessment(currentSource: "Edgar", challengerSource: "Polygon");

        var act = () => SecurityMasterWorkbenchCommandService.EnsureChosenWinnerIsCandidate(assessment, chosen);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Bloomberg")]    // never in conflict
    [InlineData("Edgr")]         // typo
    [InlineData("")]             // empty
    public void EnsureChosenWinnerIsCandidate_NonCandidate_Throws(string chosen)
    {
        var assessment = BuildAssessment(currentSource: "Edgar", challengerSource: "Polygon");

        var act = () => SecurityMasterWorkbenchCommandService.EnsureChosenWinnerIsCandidate(assessment, chosen);

        act.Should().Throw<ArgumentException>("an arbitrary or mistyped source must not be allowed to close the conflict");
    }

    private static SecurityMasterConflictAssessmentDto BuildAssessment(string currentSource, string challengerSource)
        => new(
            Conflict: new SecurityMasterConflict(
                ConflictId: Guid.NewGuid(),
                SecurityId: SecurityId,
                ConflictKind: "IdentifierAmbiguity",
                FieldPath: "Identifiers.Cusip",
                ProviderA: currentSource,
                ValueA: "value-a",
                ProviderB: challengerSource,
                ValueB: "value-b",
                DetectedAt: DateTimeOffset.UnixEpoch,
                Status: "Open"),
            CurrentWinningValue: "value-a",
            ChallengerValue: "value-b",
            CurrentWinningSource: currentSource,
            ChallengerSource: challengerSource,
            Recommendation: SecurityMasterConflictRecommendationKind.PreserveWinner,
            RecommendedResolution: "Resolve",
            RecommendedWinner: currentSource,
            ImpactSeverity: SecurityMasterImpactSeverity.Low,
            ImpactSummary: "summary",
            ImpactDetail: "detail",
            IsBulkEligible: false);

    // ---- Submit / Approve through the gate ----------------------------------------------------

    [Fact]
    public async Task Submit_WithoutWorkflow_ReturnsSubmittedState()
    {
        var harness = new Harness(currentVersion: 2);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft);

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            Note: "Ready for review.");

        var result = await harness.Service.SubmitForApprovalAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
        result.RevisionId.Should().Be(revisionId);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
        harness.Workflow.Verify(
            w => w.SubmitForApprovalAsync(It.IsAny<Guid>(), It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Submit_WithWorkflow_RoutesThroughApprovalGate()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 2);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft);
        harness.Workflow
            .Setup(w => w.SubmitForApprovalAsync(workflowId, It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 3));

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            Note: "Submit through gate.",
            FundProfileId: null,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 2,
            Reviewer: "ops.reviewer",
            ReportPackId: "rp-1");

        var result = await harness.Service.SubmitForApprovalAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
        var stored = await harness.Revisions.GetAsync(revisionId);
        stored!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
        stored.WorkflowId.Should().Be(workflowId, "the submitting workflow is bound so approval is restricted to this lane");
        harness.Workflow.Verify(
            w => w.SubmitForApprovalAsync(workflowId, It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Submit_WithWorkflow_BlankReviewer_ThrowsBeforeGate()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 2);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft);

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            Note: "Submit.",
            FundProfileId: null,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 2,
            Reviewer: "   ", // blank — would otherwise default to the submitter
            ReportPackId: "rp-1");

        await harness.Service.Invoking(s => s.SubmitForApprovalAsync(request))
            .Should().ThrowAsync<ArgumentException>();
        harness.Workflow.Verify(
            w => w.SubmitForApprovalAsync(It.IsAny<Guid>(), It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Submit_WithWorkflow_ReviewerEqualsSubmitter_ThrowsBeforeGate()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 2);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft);

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            Note: "Submit.",
            FundProfileId: null,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 2,
            Reviewer: "OPS.ANALYST", // same actor, different case — self-approval attempt
            ReportPackId: "rp-1");

        await harness.Service.Invoking(s => s.SubmitForApprovalAsync(request))
            .Should().ThrowAsync<ArgumentException>();
        harness.Workflow.Verify(
            w => w.SubmitForApprovalAsync(It.IsAny<Guid>(), It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Submit_UnknownRevision_Throws_BeforeTouchingGate()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 2);

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(), // never created
            Actor: "ops.analyst",
            Note: "Submit through gate.",
            FundProfileId: null,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 2,
            Reviewer: "ops.reviewer",
            ReportPackId: "rp-1");

        await harness.Service.Invoking(s => s.SubmitForApprovalAsync(request))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();

        // The approval gate must not be mutated for a stale/mistyped revision id (no orphaned lane).
        harness.Workflow.Verify(
            w => w.SubmitForApprovalAsync(It.IsAny<Guid>(), It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Approve_UnknownRevision_Throws_BeforeTouchingGate()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);

        var request = new ApproveSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(), // never created
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 4,
            Actor: "ops.reviewer",
            Reviewer: "ops.reviewer",
            Rationale: "Approved.",
            ReportPackId: "rp-1");

        await harness.Service.Invoking(s => s.ApproveRevisionAsync(request))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();
        harness.Workflow.Verify(
            w => w.ApproveWorkflowAsync(It.IsAny<Guid>(), It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Approve_WorkflowMismatch_Throws_BeforeTouchingGate()
    {
        var submitWorkflowId = Guid.NewGuid();
        var unrelatedWorkflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, workflowId: submitWorkflowId);

        // Approve via an unrelated, already-approvable workflow lane.
        var request = new ApproveSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            WorkflowId: unrelatedWorkflowId,
            ExpectedWorkflowVersion: 4,
            Actor: "ops.reviewer",
            Reviewer: "ops.reviewer",
            Rationale: "Approved.",
            ReportPackId: "rp-1");

        await harness.Service.Invoking(s => s.ApproveRevisionAsync(request))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();
        harness.Workflow.Verify(
            w => w.ApproveWorkflowAsync(It.IsAny<Guid>(), It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
    }

    [Fact]
    public async Task Approve_RoutesThroughApprovalGate_AndReturnsApproved()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, workflowId: workflowId);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 5));

        var request = new ApproveSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 4,
            Actor: "ops.reviewer",
            Reviewer: "ops.reviewer",
            Rationale: "Approved.",
            ReportPackId: "rp-1");

        var result = await harness.Service.ApproveRevisionAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
        harness.Workflow.Verify(
            w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Approve_WhenGateBlocks_Throws()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, workflowId: workflowId);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsTransitionResultDto(false, "BLOCKED", "Independent reviewer required.", null, [], []));

        var request = new ApproveSecurityMasterRevisionRequest(
            SecurityId, revisionId, workflowId, 4, "ops.analyst", "ops.analyst", "Approve.", "rp-1");

        await harness.Service.Invoking(s => s.ApproveRevisionAsync(request))
            .Should().ThrowAsync<InvalidOperationException>();

        // The gate rejected the approval, so the revision must remain Submitted (not advanced).
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
    }

    // ---- Publish ------------------------------------------------------------------------------

    [Fact]
    public async Task Publish_ApprovedRevision_FansOutHandlersInOrder_AndTransitionsToPublished()
    {
        var log = new List<int>();
        var ufl = new RecordingHandler(order: 10, invocationLog: log);
        var coverage = new RecordingHandler(order: 20, invocationLog: log);
        var harness = new Harness(currentVersion: 4, handlers: [coverage, ufl]); // intentionally out of order
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        var result = await harness.Service.PublishRevisionAsync(request);

        ufl.Received.Should().ContainSingle();
        coverage.Received.Should().ContainSingle();
        log.Should().Equal(new[] { 10, 20 });
        result.RestatementRequired.Should().BeFalse();
        result.RestatementCandidates.Should().BeEmpty();
        result.InvalidatedProjections.Should().HaveCount(2);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
    }

    [Fact]
    public async Task Publish_FieldEditRevision_EmitsEventWithStoredEffectiveDateAndChangedField()
    {
        var effectiveFrom = new DateTimeOffset(2026, 03, 31, 0, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler(order: 10);
        var harness = new Harness(currentVersion: 4, handlers: [handler]);
        var revisionId = await harness.SeedFieldEditRevisionAsync(
            "EconomicDefinition.Coupon", effectiveFrom, "Corrected coupon.");

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        await harness.Service.PublishRevisionAsync(request);

        var evt = handler.Received.Should().ContainSingle().Subject;
        evt.EffectiveFrom.Should().Be(effectiveFrom, "the published event must carry the edit's effective date, not publish time");
        evt.ChangedFields.Should().Equal("EconomicDefinition.Coupon");
    }

    [Fact]
    public async Task Publish_ResolvesAffectedLedgerBooks_AndFlowsThemIntoPublishedEvent()
    {
        var bookA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var bookB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var handler = new RecordingHandler(order: 10);
        var harness = new Harness(currentVersion: 4, handlers: [handler]);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);
        harness.AffectedBooks
            .Setup(r => r.ResolveAsync(It.IsAny<SecurityMasterDownstreamImpactDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)[bookA, bookB]);

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        await harness.Service.PublishRevisionAsync(request);

        // The resolved books must reach the published event so the period-aware resolver and the
        // side-effect handlers route by each book's accounting-period lock status.
        var evt = handler.Received.Should().ContainSingle().Subject;
        evt.AffectedLedgerBookIds.Should().Equal(bookA, bookB);
    }

    [Fact]
    public async Task Publish_ClosedPeriodExposure_FlowsRestatementDecisionIntoResult()
    {
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);
        var candidate = new RestatementCandidateDto(
            ReportId: Guid.NewGuid(),
            PriorVersionReportId: Guid.NewGuid(),
            PeriodLabel: "2026-P03",
            Summary: "Restate Q1 NAV pack for corrected coupon.",
            ChangedLines: []);
        harness.Restatement
            .Setup(r => r.ResolveAsync(It.IsAny<SecurityMasterRevisionPublishedEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityMasterRestatementDecision(RestatementRequired: true, Candidates: [candidate]));

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        var result = await harness.Service.PublishRevisionAsync(request);

        result.RestatementRequired.Should().BeTrue();
        result.RestatementCandidates.Should().ContainSingle().Which.Should().Be(candidate);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
    }

    [Fact]
    public async Task Publish_RevisionNotApproved_Throws_AndDoesNotFanOut()
    {
        var handler = new RecordingHandler(order: 10);
        var harness = new Harness(currentVersion: 4, handlers: [handler]);
        // Submitted, not yet Approved — publish must refuse.
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Submitted);

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        await harness.Service.Invoking(s => s.PublishRevisionAsync(request))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();
        handler.Received.Should().BeEmpty("an unapproved revision must never trigger publish handlers");
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
    }

    [Fact]
    public async Task Publish_UnknownRevision_Throws()
    {
        var harness = new Harness(currentVersion: 4);

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(), // never created
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        await harness.Service.Invoking(s => s.PublishRevisionAsync(request))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();
    }

    [Fact]
    public async Task Publish_HandlerThrows_SurfacesFailure_AndLeavesRevisionApproved()
    {
        var throwing = new RecordingHandler(order: 10, onHandle: () => throw new InvalidOperationException("transient"));
        var harness = new Harness(currentVersion: 6, handlers: [throwing]);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        // A failed required side effect surfaces to the caller instead of a silently-successful publish.
        var ex = await harness.Service.Invoking(s => s.PublishRevisionAsync(request))
            .Should().ThrowAsync<SecurityMasterPublishFailedException>();
        ex.Which.FailedHandlers.Should().ContainSingle();

        // The revision stays Approved so the idempotent fan-out can be retried.
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
    }

    [Fact]
    public async Task Publish_ScopesImpactToRequestFundProfile_OverridingTheDraftScope()
    {
        var harness = new Harness(currentVersion: 5);
        var revisionId = await harness.SeedFieldEditRevisionWithFundAsync("fund-from-edit");

        await harness.Service.PublishRevisionAsync(new PublishSecurityMasterRevisionRequest(
            SecurityId, revisionId, "ops.analyst", "approver.independent", FundProfileId: "fund-explicit"));

        harness.QueryService.Verify(
            q => q.GetTrustSnapshotAsync(SecurityId, "fund-explicit", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Publish_FallsBackToDraftFundScope_WhenRequestScopeOmitted()
    {
        var harness = new Harness(currentVersion: 5);
        var revisionId = await harness.SeedFieldEditRevisionWithFundAsync("fund-from-edit");

        await harness.Service.PublishRevisionAsync(new PublishSecurityMasterRevisionRequest(
            SecurityId, revisionId, "ops.analyst", "approver.independent"));

        harness.QueryService.Verify(
            q => q.GetTrustSnapshotAsync(SecurityId, "fund-from-edit", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Publish_BlankScopes_ResolveToUnscopedNull()
    {
        var harness = new Harness(currentVersion: 5);
        // No fund on the draft revision; a whitespace request scope must normalize to null (unscoped).
        var revisionId = await harness.SeedFieldEditRevisionAsync(
            "EconomicDefinition.Coupon", new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero), "Corrected coupon.");

        await harness.Service.PublishRevisionAsync(new PublishSecurityMasterRevisionRequest(
            SecurityId, revisionId, "ops.analyst", "approver.independent", FundProfileId: "   "));

        harness.QueryService.Verify(
            q => q.GetTrustSnapshotAsync(SecurityId, null, It.IsAny<CancellationToken>()),
            Times.Once);
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
        public Mock<IPeriodAwareRestatementResolver> Restatement { get; } = new(MockBehavior.Loose);
        public Mock<IAffectedLedgerBookResolver> AffectedBooks { get; } = new(MockBehavior.Loose);
        public ISecurityMasterRevisionStore Revisions { get; } = new InMemorySecurityMasterRevisionStore();
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

            // Default: no closed-period exposure. Individual tests override to assert restatement flow-through.
            Restatement
                .Setup(r => r.ResolveAsync(It.IsAny<SecurityMasterRevisionPublishedEvent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SecurityMasterRestatementDecision(RestatementRequired: false, Candidates: []));

            // Default: no affected ledger books resolved. Individual tests override to assert the feed flows.
            AffectedBooks
                .Setup(r => r.ResolveAsync(It.IsAny<SecurityMasterDownstreamImpactDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<Guid>)[]);

            Service = new SecurityMasterWorkbenchCommandService(
                EventStore,
                Overrides.Object,
                Policy.Object,
                ConflictService.Object,
                QueryService.Object,
                Workflow.Object,
                Revisions,
                Restatement.Object,
                AffectedBooks.Object,
                handlers ?? Array.Empty<ISecurityMasterRevisionPublishedHandler>(),
                NullLogger<SecurityMasterWorkbenchCommandService>.Instance);
        }

        /// <summary>
        /// Seeds a revision advanced to <paramref name="state"/> and returns its id. When
        /// <paramref name="workflowId"/> is supplied it is bound on the Draft→Submitted transition so
        /// approval-binding checks can be exercised.
        /// </summary>
        public async Task<Guid> SeedRevisionAsync(
            SecurityMasterRevisionStateDto state, string actor = "ops.analyst", Guid? workflowId = null)
        {
            var draft = await Revisions.CreateDraftAsync(SecurityId, actor);
            var id = draft.RevisionId;
            if (state == SecurityMasterRevisionStateDto.Draft)
            {
                return id;
            }

            await Revisions.TransitionAsync(
                id, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, actor,
                workflowIdForSubmit: workflowId);
            if (state == SecurityMasterRevisionStateDto.Submitted)
            {
                return id;
            }

            await Revisions.TransitionAsync(id, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, actor);
            if (state == SecurityMasterRevisionStateDto.Approved)
            {
                return id;
            }

            await Revisions.TransitionAsync(id, SecurityMasterRevisionStateDto.Approved, SecurityMasterRevisionStateDto.Published, actor);
            return id;
        }

        /// <summary>Seeds an Approved revision carrying field-edit metadata (path + effective date).</summary>
        public async Task<Guid> SeedFieldEditRevisionAsync(
            string fieldPath, DateTimeOffset effectiveFrom, string justification, string actor = "ops.analyst")
        {
            var draft = await Revisions.CreateDraftAsync(SecurityId, actor, fieldPath, effectiveFrom, justification);
            var id = draft.RevisionId;
            await Revisions.TransitionAsync(id, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, actor);
            await Revisions.TransitionAsync(id, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, actor);
            return id;
        }

        /// <summary>Seeds an Approved field-edit revision carrying a fund-profile scope from the edit.</summary>
        public async Task<Guid> SeedFieldEditRevisionWithFundAsync(string fundProfileId, string actor = "ops.analyst")
        {
            var effectiveFrom = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
            var draft = await Revisions.CreateDraftAsync(
                SecurityId, actor, "EconomicDefinition.Coupon", effectiveFrom, "Corrected coupon.", fundProfileId);
            var id = draft.RevisionId;
            await Revisions.TransitionAsync(id, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, actor);
            await Revisions.TransitionAsync(id, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, actor);
            return id;
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
