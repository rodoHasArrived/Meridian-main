using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Contracts.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// Scenario tests guard the controller close-reporting path where an operator submits a freshly created W4 report pack for review before approval and publication.
/// </summary>
public sealed class ReportPackWorkflowServiceTests
{
    [Fact]
    public void Transition_FreshDraftSubmitApprovePublish_CompletesW4LifecycleWithoutLegacyIntermediateState()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");

        var submitted = svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        var approved = svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        var published = svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);

        submitted.State.Should().Be(ReportPackWorkflowStateDto.InReview);
        approved.State.Should().Be(ReportPackWorkflowStateDto.Approved);
        published.State.Should().Be(ReportPackWorkflowStateDto.Published);
        published.Publication.Should().NotBeNull();
        published.Publication!.SignedOffBy.Should().Be("controller");
        published.Publication.EvidenceHash.Should().Be("sha256:abc123");
        published.AuditTrail.Should().HaveCount(4);
        published.AuditTrail.Should().ContainSingle(e =>
            e.Action == "create"
            && e.Actor == "author"
            && e.FromState == ReportPackWorkflowStateDto.Draft
            && e.ToState == ReportPackWorkflowStateDto.Draft);
        published.AuditTrail.Should().ContainSingle(e =>
            e.Action == "inreview"
            && e.Actor == "reviewer"
            && e.FromState == ReportPackWorkflowStateDto.Draft
            && e.ToState == ReportPackWorkflowStateDto.InReview);
    }


    [Fact]
    public async Task Endpoint_CreateSubmitApprovePublish_CompletesW4LifecycleWithoutUnreachableIntermediateState()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.Admin);
        var client = app.GetTestClient();
        var request = new ReportPackCreateRequestDto(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1));

        var createResponse = await client.PostAsJsonAsync("/api/fund-structure/reporting/packs", request, ServerJsonOptions);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<ReportPackWorkflowRecordDto>(ServerJsonOptions);

        created.Should().NotBeNull();
        created!.State.Should().Be(ReportPackWorkflowStateDto.Draft);

        var submitResponse = await client.PostAsync($"/api/fund-structure/reporting/packs/{created.ReportId:D}/submit", null);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<ReportPackWorkflowRecordDto>(ServerJsonOptions);

        submitted.Should().NotBeNull();
        submitted!.State.Should().Be(ReportPackWorkflowStateDto.InReview);
        submitted.AuditTrail.Should().ContainSingle(entry =>
            entry.FromState == ReportPackWorkflowStateDto.Draft &&
            entry.ToState == ReportPackWorkflowStateDto.InReview);

        var approveResponse = await client.PostAsync($"/api/fund-structure/reporting/packs/{created.ReportId:D}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approveResponse.Content.ReadFromJsonAsync<ReportPackWorkflowRecordDto>(ServerJsonOptions);

        approved.Should().NotBeNull();
        approved!.State.Should().Be(ReportPackWorkflowStateDto.Approved);

        var publishRequest = new ReportPackPublishRequestDto(
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);
        var publishResponse = await client.PostAsJsonAsync($"/api/fund-structure/reporting/packs/{created.ReportId:D}/publish", publishRequest, ServerJsonOptions);
        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var published = await publishResponse.Content.ReadFromJsonAsync<ReportPackWorkflowRecordDto>(ServerJsonOptions);

        published.Should().NotBeNull();
        published!.State.Should().Be(ReportPackWorkflowStateDto.Published);
        published.AuditTrail.Select(entry => entry.ToState).Should().ContainInOrder(
            ReportPackWorkflowStateDto.Draft,
            ReportPackWorkflowStateDto.InReview,
            ReportPackWorkflowStateDto.Approved,
            ReportPackWorkflowStateDto.Published);
        published.AuditTrail.Should().NotContain(entry =>
            entry.ToState == ReportPackWorkflowStateDto.Validated ||
            entry.ToState == ReportPackWorkflowStateDto.PendingApproval);
    }

    [Fact]
    public void Transition_ToPublished_RequiresGovernedPublicationMetadata()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");

        Action act = () => svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Published, "publisher", "publisher");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack publication requires sign-off, evidence hash, and retained manifest metadata.");
    }

    [Fact]
    public void Publish_RejectsLineProvenanceEvidenceMissingFromRetainedManifest()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "author",
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")]);
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");

        Action act = () => svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("other-evidence", "Other evidence", "/evidence/other", "reporting")]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack publication has orphan evidence: ledger-evidence-1.");
    }

    [Fact]
    public void Publish_RejectsLineProvenancePointersMissingFromRetainedManifest()
    {
        var svc = new ReportPackWorkflowService();
        var approved = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")]);

        Action act = () => svc.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("ledger-evidence-1", "Line evidence", "/evidence/ledger-evidence-1", "reporting")]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack publication has orphan provenance pointers: approval-1, case-1, definition-1, ledger-entry-1, provider-event-1, provider-session-1, recon-run-1, run-1, security-1.");
    }

    [Fact]
    public void Publish_RequiresReportValueAndSourcePointerForLineProvenance()
    {
        var svc = new ReportPackWorkflowService();
        var missingValue = svc.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "author",
            [new ReportPackLineProvenanceDto("trial-balance.cash", "ledger", "ledger-entry-1", "ledger-evidence-1", LedgerEntryId: "ledger-entry-1")]);
        svc.Transition(missingValue.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(missingValue.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");

        Action missingValuePublish = () => svc.Publish(
            missingValue.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("ledger-evidence-1", "Ledger evidence", "/evidence/ledger-evidence-1", "ledger")]);

        missingValuePublish.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack line provenance requires report values for: trial-balance.cash.");

        var missingPointer = svc.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "author",
            [new ReportPackLineProvenanceDto("trial-balance.nav", "report", "nav-line-1", "nav-evidence-1", ReportValue: "125.00")]);
        svc.Transition(missingPointer.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(missingPointer.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");

        Action missingPointerPublish = () => svc.Publish(
            missingPointer.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-2",
            "vault/report-packs/manifest-2.json",
            [new ReportPackEvidenceLinkDto("nav-evidence-1", "NAV evidence", "/evidence/nav-evidence-1", "reporting")]);

        missingPointerPublish.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack line provenance requires run, session, ledger, or reconciliation source pointers for: trial-balance.nav.");
    }

    [Fact]
    public void Publish_RequiresLedgerProviderSecurityReconciliationAndApprovalPointersForLineProvenance()
    {
        var svc = new ReportPackWorkflowService();
        var missingLedger = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1") with { LedgerEntryId = null, RunId = "run-1" }]);

        Action missingLedgerPublish = () => PublishWithLedgerEvidence(svc, missingLedger.ReportId);

        missingLedgerPublish.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack line provenance requires ledger entries for: trial-balance.cash.");

        var missingProvider = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.income", "income-evidence-1") with { ProviderEventId = null }]);

        Action missingProviderPublish = () => PublishWithLedgerEvidence(svc, missingProvider.ReportId, "income-evidence-1");

        missingProviderPublish.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack line provenance requires provider events for: trial-balance.income.");

        var missingSecurityMaster = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.position", "position-evidence-1") with { SecurityMasterId = null, SecurityDefinitionId = null }]);

        Action missingSecurityMasterPublish = () => PublishWithLedgerEvidence(svc, missingSecurityMaster.ReportId, "position-evidence-1");

        missingSecurityMasterPublish.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack line provenance requires Security Master definitions for: trial-balance.position.");

        var missingReconciliation = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.nav", "nav-evidence-1") with { ReconciliationRunId = null, ReconciliationCaseId = null, ReconciliationOutcome = null }]);

        Action missingReconciliationPublish = () => PublishWithLedgerEvidence(svc, missingReconciliation.ReportId, "nav-evidence-1");

        missingReconciliationPublish.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack line provenance requires reconciliation outcomes for: trial-balance.nav.");

        var missingApproval = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.fees", "fees-evidence-1") with { ApprovalId = null }]);

        Action missingApprovalPublish = () => PublishWithLedgerEvidence(svc, missingApproval.ReportId, "fees-evidence-1");

        missingApprovalPublish.Should().Throw<InvalidOperationException>()
            .WithMessage("Report pack line provenance requires approval references for: trial-balance.fees.");
    }

    [Fact]
    public void Publish_AllowsCompleteReportLineProvenanceForRetainedReportPack()
    {
        var svc = new ReportPackWorkflowService();
        var approved = CreateApprovedPack(
            svc,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")]);

        var published = svc.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"));

        var line = published.LineProvenance.Should().ContainSingle().Subject;
        line.LedgerEntryId.Should().Be("ledger-entry-1");
        line.ProviderEventId.Should().Be("provider-event-1");
        line.SecurityMasterId.Should().Be("security-1");
        line.SecurityDefinitionId.Should().Be("definition-1");
        line.ReconciliationRunId.Should().Be("recon-run-1");
        line.ReconciliationOutcome.Should().Be("matched");
        line.ApprovalId.Should().Be("approval-1");
        published.State.Should().Be(ReportPackWorkflowStateDto.Published);
    }

    [Fact]
    public void Create_NormalizesReportValueSessionAndReconciliationRunLineProvenance()
    {
        var svc = new ReportPackWorkflowService();

        var created = svc.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "author",
            [
                new ReportPackLineProvenanceDto(
                    " trial-balance.nav ",
                    " paper-session ",
                    " session-1 ",
                    " session-evidence-1 ",
                    RunId: " run-1 ",
                    ReportValue: " 125.00 ",
                    SourceSessionId: " paper-session-1 ",
                    ReconciliationRunId: " recon-run-1 ",
                    ProviderEventId: " provider-event-1 ",
                    SecurityMasterId: " security-1 ",
                    SecurityDefinitionId: " definition-1 ",
                    ReconciliationOutcome: " matched ",
                    ApprovalId: " approval-1 ")
            ]);

        created.LineProvenance.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ReportPackLineProvenanceDto(
                "trial-balance.nav",
                "paper-session",
                "session-1",
                "session-evidence-1",
                RunId: "run-1",
                ReportValue: "125.00",
                SourceSessionId: "paper-session-1",
                ReconciliationRunId: "recon-run-1",
                ProviderEventId: "provider-event-1",
                SecurityMasterId: "security-1",
                SecurityDefinitionId: "definition-1",
                ReconciliationOutcome: "matched",
                ApprovalId: "approval-1"));
    }

    [Fact]
    public void Transition_RejectsInvalidRole()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");

        Action act = () => svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "user", "reviewer");
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void Reject_AllowsReviewStateAndRecordsReasonMetadata()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");

        var rejected = svc.Reject(
            created.ReportId,
            "NAV tie-out variance exceeds tolerance",
            "senior-reviewer",
            "reviewer",
            [new ReportPackEvidenceLinkDto("tie-out-1", "Tie-out variance", "/evidence/tie-out-1", "reconciliation")]);

        rejected.State.Should().Be(ReportPackWorkflowStateDto.Rejected);
        rejected.Rejection.Should().NotBeNull();
        rejected.Rejection!.Reason.Should().Be("NAV tie-out variance exceeds tolerance");
        rejected.Rejection.Actor.Should().Be("senior-reviewer");
        rejected.Rejection.ActorRole.Should().Be("reviewer");
        rejected.Rejection.EvidenceLinks.Should().ContainSingle(link =>
            link.EvidenceId == "tie-out-1" &&
            link.Label == "Tie-out variance" &&
            link.Route == "/evidence/tie-out-1" &&
            link.Source == "reconciliation");
    }

    [Theory]
    [InlineData(ReportPackWorkflowStateDto.Draft)]
    [InlineData(ReportPackWorkflowStateDto.Published)]
    public void Reject_RejectsDraftAndPublishedStates(ReportPackWorkflowStateDto startingState)
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");

        if (startingState == ReportPackWorkflowStateDto.Published)
        {
            svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
            svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
            svc.Publish(
                created.ReportId,
                "publisher",
                "publisher",
                "controller",
                "sha256:abc123",
                "manifest-1",
                "vault/report-packs/manifest-1.json",
                [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);
        }

        Action act = () => svc.Reject(created.ReportId, "needs reviewer remediation", "senior-reviewer", "reviewer");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"invalid transition {startingState} -> {ReportPackWorkflowStateDto.Rejected}");
    }

    [Fact]
    public void Reject_RejectsInvalidRole()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");

        Action act = () => svc.Reject(created.ReportId, "needs reviewer remediation", "operator", "operator");

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("Role 'operator' cannot transition to Rejected.");
    }

    [Fact]
    public void Reject_AppendsAuditTrailContents()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");

        var rejected = svc.Reject(created.ReportId, "missing controller sign-off evidence", "senior-reviewer", "reviewer");

        rejected.AuditTrail.Should().ContainSingle(entry =>
            entry.Actor == "senior-reviewer" &&
            entry.Action == "rejected" &&
            entry.FromState == ReportPackWorkflowStateDto.InReview &&
            entry.ToState == ReportPackWorkflowStateDto.Rejected &&
            entry.Note == "missing controller sign-off evidence");
    }

    [Fact]
    public void Publish_RejectsRejectedRecordsUntilResubmittedThroughApprovalLifecycle()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Reject(created.ReportId, "missing controller sign-off evidence", "senior-reviewer", "reviewer");

        Action publishRejected = () => svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);

        publishRejected.Should().Throw<InvalidOperationException>()
            .WithMessage("invalid transition Rejected -> Published");

        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Draft, "author", "operator");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        var published = svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);

        published.State.Should().Be(ReportPackWorkflowStateDto.Published);
    }

    [Fact]
    public void Restate_RequiresLineageAndReasonMetadata()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);

        var restated = svc.Restate(created.ReportId, "approver", "approver", "pricing-correction", "chief-approver", created.ReportId,
            [new ReportPackChangedLineDto("line-1", "100", "125", [new ReportPackEvidenceLinkDto("pricing-evidence-1", "Pricing correction", "/evidence/pricing-evidence-1", "pricing")])]);

        restated.State.Should().Be(ReportPackWorkflowStateDto.Restated);
        restated.Restatement.Should().NotBeNull();
        restated.Restatement!.ChangedLines.Should().ContainSingle();
        restated.Restatement.EvidenceLinks.Should().ContainSingle(link => link.EvidenceId == "pricing-evidence-1");
        restated.Version.Should().Be(2);
    }

    [Fact]
    public void Restate_RejectsChangedLinesWithoutEvidenceLinks()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);

        Action act = () => svc.Restate(
            created.ReportId,
            "approver",
            "approver",
            "pricing-correction",
            "chief-approver",
            created.ReportId,
            [new ReportPackChangedLineDto("line-1", "100", "125")]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Restatement changed lines require evidence links: line-1.");
    }

    [Fact]
    public void Transition_ArchivesPublishedOrRestatedPacksOnly()
    {
        var svc = new ReportPackWorkflowService();
        var created = svc.Create("fund-a", "acct-1", "2026-03", new VersionedReportTemplateIdDto("board-pack", 1), "author");

        Action earlyArchive = () => svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Archived, "records", "records-manager");
        earlyArchive.Should().Throw<InvalidOperationException>()
            .WithMessage("invalid transition Draft -> Archived");

        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        svc.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);

        var archived = svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Archived, "records", "records-manager");

        archived.State.Should().Be(ReportPackWorkflowStateDto.Archived);
        archived.AuditTrail.Should().ContainSingle(entry =>
            entry.Action == "archived" &&
            entry.FromState == ReportPackWorkflowStateDto.Published &&
            entry.ToState == ReportPackWorkflowStateDto.Archived);
    }

    private static ReportPackWorkflowRecordDto CreateApprovedPack(
        ReportPackWorkflowService svc,
        IReadOnlyList<ReportPackLineProvenanceDto> lineProvenance)
    {
        var created = svc.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "author",
            lineProvenance);
        svc.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        return svc.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
    }

    private static void PublishWithLedgerEvidence(ReportPackWorkflowService svc, Guid reportId, string evidenceId = "ledger-evidence-1") =>
        svc.Publish(
            reportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto(evidenceId, "Line evidence", $"/evidence/{evidenceId}", "reporting")]);

    private static IReadOnlyList<ReportPackEvidenceLinkDto> CompleteLineProvenanceEvidenceLinks(string evidenceId) =>
    [
        new ReportPackEvidenceLinkDto(evidenceId, "Line evidence", $"/evidence/{evidenceId}", "reporting"),
        new ReportPackEvidenceLinkDto("ledger-entry-1", "Ledger entry", "/evidence/ledger-entry-1", "ledger"),
        new ReportPackEvidenceLinkDto("provider-event-1", "Provider event", "/evidence/provider-event-1", "provider"),
        new ReportPackEvidenceLinkDto("security-1", "Security Master identity", "/evidence/security-1", "security-master"),
        new ReportPackEvidenceLinkDto("definition-1", "Security definition", "/evidence/definition-1", "security-master"),
        new ReportPackEvidenceLinkDto("case-1", "Reconciliation case", "/evidence/case-1", "reconciliation"),
        new ReportPackEvidenceLinkDto("recon-run-1", "Reconciliation run", "/evidence/recon-run-1", "reconciliation"),
        new ReportPackEvidenceLinkDto("approval-1", "Approval", "/evidence/approval-1", "approval"),
        new ReportPackEvidenceLinkDto("run-1", "Strategy run", "/evidence/run-1", "strategy"),
        new ReportPackEvidenceLinkDto("provider-session-1", "Provider source session", "/evidence/provider-session-1", "provider")
    ];

    private static ReportPackLineProvenanceDto CompleteLineProvenance(string lineKey, string evidenceId) =>
        new(
            lineKey,
            "ledger",
            "ledger-entry-1",
            evidenceId,
            RunId: "run-1",
            LedgerEntryId: "ledger-entry-1",
            ReconciliationCaseId: "case-1",
            ReportValue: "100.00",
            SourceSessionId: "provider-session-1",
            ReconciliationRunId: "recon-run-1",
            ProviderEventId: "provider-event-1",
            SecurityMasterId: "security-1",
            SecurityDefinitionId: "definition-1",
            ReconciliationOutcome: "matched",
            ApprovalId: "approval-1");

    private static readonly JsonSerializerOptions ServerJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task<WebApplication> CreateFundStructureAppAsync(UserRole role)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ReportPackWorkflowService>();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "controller.admin";
            context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = role;
            await next();
        });
        app.MapFundStructureEndpoints(ServerJsonOptions);

        await app.StartAsync();
        return app;
    }
}
