using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Reporting;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

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
    public void TemplateRegistry_CreateSubmitApproveRevision_TracksVersionApprovalAndBlocksDraftRender()
    {
        var svc = new ReportTemplateRegistryService();

        svc.List().Should().Contain(record =>
            record.Definition.TemplateId.Name == "investor-monthly-statement" &&
            record.Status == ReportTemplateLifecycleStatusDto.Approved &&
            record.IsBuiltIn);

        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "investor-monthly-statement",
                "Investor Monthly Statement v2",
                ["cover", "performance", "positions", "fees"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "InvestorStatement",
                BasedOnVersion: 1,
                Rationale: "Add management fee detail"),
            "report.author");

        draft.Status.Should().Be(ReportTemplateLifecycleStatusDto.Draft);
        draft.Definition.TemplateId.Version.Should().Be(2);
        draft.BasedOnTemplateId.Should().Be(new VersionedReportTemplateIdDto("investor-monthly-statement", 1));
        draft.ValidationIssues.Should().BeEmpty();
        svc.Get(draft.Definition.TemplateId).Should().BeNull();

        var submitted = svc.Submit(draft.Definition.TemplateId, "report.author", "ready for controller review");
        var approved = svc.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Controller approved fee disclosure", "APP-TPL-2"),
            "controller.admin");

        submitted.Status.Should().Be(ReportTemplateLifecycleStatusDto.InReview);
        approved.Status.Should().Be(ReportTemplateLifecycleStatusDto.Approved);
        approved.IsLatestApproved.Should().BeTrue();
        approved.ApprovalReference.Should().Be("APP-TPL-2");
        approved.AuditTrail.Select(entry => entry.Action).Should().ContainInOrder("draft", "submit", "approve");

        var builtIn = svc.List(includeSuperseded: true).Single(record =>
            record.Definition.TemplateId == new VersionedReportTemplateIdDto("investor-monthly-statement", 1));
        builtIn.Status.Should().Be(ReportTemplateLifecycleStatusDto.Approved);
        builtIn.IsLatestApproved.Should().BeFalse();

        var rendered = svc.Render(new RenderReportTemplateRequestDto(
            approved.Definition.TemplateId,
            new Dictionary<string, string> { ["period"] = "2026-05" }));
        rendered.MissingRequiredParameters.Should().BeEmpty();
        rendered.RenderedContent.Should().Contain("sections=cover,fees,performance,positions");
    }

    [Fact]
    public void TemplateRegistry_InvalidDraft_CannotSubmitForApproval()
    {
        var svc = new ReportTemplateRegistryService();
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "custom-board-pack",
                "Custom Board Pack",
                [],
                [],
                Rationale: "Missing section setup"),
            "report.author");

        Action act = () => svc.Submit(draft.Definition.TemplateId, "report.author");

        draft.ValidationIssues.Should().Contain("At least one report section or report writer grid is required.");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Template custom-board-pack@v1 is not ready for review: At least one report section or report writer grid is required.");
    }

    [Fact]
    public void TemplateRegistry_RenderApprovedReportWriterGridTemplate_ReturnsStructuredGridResults()
    {
        var svc = new ReportTemplateRegistryService();
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "custom-exposure-grid",
                "Custom Exposure Grid",
                [],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "No-code exposure writer",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "sector-pivot",
                        "Sector Pivot",
                        ReportWriterGridKindDto.Pivot,
                        RowFields: ["sector"],
                        Metrics:
                        [
                            new ReportWriterMetricDefinitionDto("marketValue", "marketValue"),
                            new ReportWriterMetricDefinitionDto("pnl", "pnl")
                        ],
                        Formulas: [new ReportWriterFormulaDefinitionDto("returnPct", "{pnl} / {marketValue} * 100")])
                ]),
            "report.author");

        draft.ValidationIssues.Should().BeEmpty();
        svc.Submit(draft.Definition.TemplateId, "report.author", "ready");
        svc.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Controller approved no-code grid", "APP-GRID-1"),
            "controller.admin");

        var rendered = svc.Render(new RenderReportTemplateRequestDto(
            draft.Definition.TemplateId,
            new Dictionary<string, string> { ["period"] = "2026-05" },
            [
                new Dictionary<string, string> { ["sector"] = "Technology", ["marketValue"] = "100", ["pnl"] = "10" },
                new Dictionary<string, string> { ["sector"] = "Technology", ["marketValue"] = "50", ["pnl"] = "5" },
                new Dictionary<string, string> { ["sector"] = "Rates", ["marketValue"] = "50", ["pnl"] = "-2" }
            ]));

        rendered.MissingRequiredParameters.Should().BeEmpty();
        rendered.RenderedContent.Should().Contain("grids=sector-pivot:2r");
        rendered.Grids.Should().ContainSingle(grid => grid.GridId == "sector-pivot");
        var grid = rendered.Grids!.Single();
        grid.Columns.Select(column => column.Key).Should().ContainInOrder("sector", "marketValue", "pnl", "returnPct");
        grid.Rows.Should().Contain(row =>
            row.Values["sector"] == "Technology" &&
            row.Values["marketValue"] == "150" &&
            row.Values["pnl"] == "15" &&
            row.Values["returnPct"] == "10");
    }

    [Fact]
    public void TemplateRegistry_ReloadsCustomDraftsAndApprovedRevisionsFromStore()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"), "report-templates.json");
        var store = new FileReportTemplateGovernanceStore(
            new ReportTemplateGovernanceStoreOptions(snapshotPath),
            NullLogger<FileReportTemplateGovernanceStore>.Instance);
        var svc = new ReportTemplateRegistryService(store);
        var draft = svc.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "investor-monthly-statement",
                "Investor Monthly Statement v2",
                ["cover", "performance", "positions", "fees"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "InvestorStatement",
                BasedOnVersion: 1,
                Rationale: "Add fee disclosure"),
            "report.author");
        svc.Submit(draft.Definition.TemplateId, "report.author", "ready");
        svc.Approve(
            draft.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Approved", "APP-TPL-2"),
            "controller.admin");

        var reloaded = new ReportTemplateRegistryService(store);
        var records = reloaded.List(includeSuperseded: true);

        records.Should().Contain(record =>
            record.Definition.TemplateId == new VersionedReportTemplateIdDto("investor-monthly-statement", 2) &&
            record.Status == ReportTemplateLifecycleStatusDto.Approved &&
            record.ApprovalReference == "APP-TPL-2");
        records.Should().Contain(record =>
            record.Definition.TemplateId == new VersionedReportTemplateIdDto("investor-monthly-statement", 1) &&
            record.IsBuiltIn &&
            !record.IsLatestApproved);
        reloaded.Render(new RenderReportTemplateRequestDto(
                new VersionedReportTemplateIdDto("investor-monthly-statement", 2),
                new Dictionary<string, string> { ["period"] = "2026-05" }))
            .RenderedContent.Should().Contain("fees");
    }

    [Fact]
    public async Task Endpoint_TemplateDraftSubmitApprove_ListExposesGovernedLifecycle()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.Admin);
        var client = app.GetTestClient();
        var draftRequest = new ReportTemplateDraftRequestDto(
            "investor-monthly-statement",
            "Investor Monthly Statement v2",
            ["cover", "performance", "positions", "fees"],
            [new ReportTemplateParameterDefinitionDto("period", Required: true)],
            Family: "InvestorStatement",
            BasedOnVersion: 1,
            Rationale: "Add fee disclosure");

        var draftResponse = await client.PostAsJsonAsync("/api/fund-structure/reporting/templates/drafts", draftRequest, ServerJsonOptions);
        draftResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var draft = await draftResponse.Content.ReadFromJsonAsync<ReportTemplateGovernanceRecordDto>(ServerJsonOptions);
        draft.Should().NotBeNull();
        draft!.Status.Should().Be(ReportTemplateLifecycleStatusDto.Draft);

        var renderDraftResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/templates/render",
            new RenderReportTemplateRequestDto(draft.Definition.TemplateId, new Dictionary<string, string> { ["period"] = "2026-05" }),
            ServerJsonOptions);
        renderDraftResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var submitResponse = await client.PostAsJsonAsync(
            $"/api/fund-structure/reporting/templates/{draft.Definition.TemplateId.Name}/versions/{draft.Definition.TemplateId.Version}/submit",
            new ReportTemplateDecisionRequestDto("Ready for controller review"),
            ServerJsonOptions);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<ReportTemplateGovernanceRecordDto>(ServerJsonOptions);
        submitted.Should().NotBeNull();
        submitted!.Status.Should().Be(ReportTemplateLifecycleStatusDto.InReview);

        var approveResponse = await client.PostAsJsonAsync(
            $"/api/fund-structure/reporting/templates/{draft.Definition.TemplateId.Name}/versions/{draft.Definition.TemplateId.Version}/approve",
            new ReportTemplateDecisionRequestDto("Controller approved fee disclosure", "APP-TPL-2"),
            ServerJsonOptions);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approveResponse.Content.ReadFromJsonAsync<ReportTemplateGovernanceRecordDto>(ServerJsonOptions);
        approved.Should().NotBeNull();
        approved!.Status.Should().Be(ReportTemplateLifecycleStatusDto.Approved);
        approved.ApprovalReference.Should().Be("APP-TPL-2");

        var listResponse = await client.GetAsync("/api/fund-structure/reporting/templates?includeSuperseded=true");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await listResponse.Content.ReadFromJsonAsync<List<ReportTemplateGovernanceRecordDto>>(ServerJsonOptions);
        records.Should().NotBeNull();
        records!.Should().Contain(record =>
            record.Definition.TemplateId == new VersionedReportTemplateIdDto("investor-monthly-statement", 2) &&
            record.Status == ReportTemplateLifecycleStatusDto.Approved &&
            !record.IsBuiltIn);
        records.Should().Contain(record =>
            record.Definition.TemplateId == new VersionedReportTemplateIdDto("investor-monthly-statement", 1) &&
            record.IsBuiltIn &&
            !record.IsLatestApproved);
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
    public void WorkflowStore_ReloadsPublishedAndRestatedReportPackRecords()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"), "report-pack-workflows.json");
        var store = new FileReportPackWorkflowRecordStore(
            new ReportPackWorkflowRecordStoreOptions(snapshotPath),
            NullLogger<FileReportPackWorkflowRecordStore>.Instance);
        var svc = new ReportPackWorkflowService(store);
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

        var restated = svc.Restate(
            published.ReportId,
            "approver",
            "approver",
            "NAV_CORRECTION",
            "controller",
            published.ReportId,
            [
                new ReportPackChangedLineDto(
                    "trial-balance.cash",
                    "100.00",
                    "101.00",
                    [new ReportPackEvidenceLinkDto("cash-restatement-1", "Cash restatement", "/evidence/cash-restatement-1", "reporting")])
            ]);

        var reloaded = new ReportPackWorkflowService(store);
        var record = reloaded.ListRecords().Should().ContainSingle(item => item.ReportId == published.ReportId).Subject;
        record.State.Should().Be(ReportPackWorkflowStateDto.Restated);
        record.Version.Should().Be(restated.Version);
        record.Publication!.ManifestId.Should().Be("manifest-1");
        record.Restatement!.ReasonCode.Should().Be("NAV_CORRECTION");
        record.AuditTrail.Select(static entry => entry.Action).Should().ContainInOrder("create", "inreview", "approved", "published", "restated");
    }

    [Fact]
    public async Task ReportPackRunReadService_UnifiesGenericRunsAndGovernedWorkflowRecords()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var orchestration = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero),
            runStore);
        var manifest = await orchestration.ExecuteAsync(
            new ReportingJobContract(
                "sched-shadow",
                "shadow-nav-daily-pack",
                new DateOnly(2026, 5, 3),
                ReportingRunTrigger.Scheduled,
                0,
                "scheduler",
                new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero),
                ScheduleId: "sched-shadow"),
            CancellationToken.None);

        var workflow = new ReportPackWorkflowService();
        var approved = CreateApprovedPack(
            workflow,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")]);
        var published = workflow.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"));
        workflow.Restate(
            published.ReportId,
            "approver",
            "approver",
            "NAV_CORRECTION",
            "controller",
            published.ReportId,
            [
                new ReportPackChangedLineDto(
                    "trial-balance.cash",
                    "100.00",
                    "101.00",
                    [new ReportPackEvidenceLinkDto("cash-restatement-1", "Cash restatement", "/evidence/cash-restatement-1", "reporting")])
            ]);

        var payload = new ReportPackRunReadService(new DefaultReportingTemplateCatalog(), runStore, workflow).BuildPayload();

        payload.RecentRuns.Select(static run => run.RunId).Should().Contain(manifest.RunId);
        payload.RecentRuns.Select(static run => run.RunId).Should().NotContain("investor-monthly-statement-20260501");
        payload.RecentRuns.Select(static run => run.RunId).Should().NotContain("shadow-nav-daily-pack-20260503");
        var genericRun = payload.RecentRuns.Single(run => run.RunId == manifest.RunId);
        genericRun.Artifacts.Should().Contain("schedule:sched-shadow");
        genericRun.DrilldownLinks.Should().Contain(link =>
            link.Kind == "schedule" &&
            link.Href == "schedule:sched-shadow" &&
            !link.IsBrowserNavigable);
        genericRun.NextActions.Should().Contain(action =>
            action.Kind == "approval-submit" &&
            action.Method == "POST" &&
            action.IsEnabled);
        var workflowRun = payload.RecentRuns.Single(run => run.RunId == $"report-pack:{published.ReportId:D}");
        workflowRun.Family.Should().Be("GovernedReportPack");
        workflowRun.Status.Should().Be(ReportPackWorkflowStateDto.Restated.ToString());
        workflowRun.Artifacts.Should().Contain(item => item.Contains("/evidence-bundle", StringComparison.OrdinalIgnoreCase));
        workflowRun.Artifacts.Should().Contain("publication-manifest:manifest-1");
        workflowRun.Artifacts.Should().Contain("restatement:NAV_CORRECTION");
        workflowRun.Artifacts.Should().Contain("/evidence/cash-restatement-1");
        workflowRun.DrilldownLinks.Should().Contain(link =>
            link.Kind == "evidence" &&
            link.Label == "Evidence bundle" &&
            link.IsBrowserNavigable);
        workflowRun.DrilldownLinks.Should().Contain(link =>
            link.Kind == "publication-evidence" &&
            link.Label == "Line evidence" &&
            link.Href == "/evidence/ledger-evidence-1");
        workflowRun.DrilldownLinks.Should().Contain(link =>
            link.Kind == "restatement-evidence" &&
            link.Label == "Cash restatement" &&
            link.Href == "/evidence/cash-restatement-1");
        workflowRun.NextActions.Should().ContainSingle(action =>
            action.Kind == "archive" &&
            action.Method == "POST" &&
            action.Href.EndsWith($"/reporting/packs/{published.ReportId:D}/archive", StringComparison.Ordinal));
        workflowRun.AuditActions.Should().Contain("published");
        workflowRun.AuditActions.Should().Contain("restated");
        payload.ReportPackDistributions.Should().NotBeEmpty();
        payload.ReportPackDistributions.Should().AllSatisfy(distribution =>
        {
            distribution.Recipient.Should().NotBeNullOrWhiteSpace();
            distribution.Channel.Should().NotBeNullOrWhiteSpace();
            distribution.PendingSummary.Should().Contain(distribution.Recipient);
        });
        payload.ReportPackDistributions.Should().Contain(distribution =>
            distribution.Recipient == "Board reporting committee" &&
            distribution.State == "Pending delivery" &&
            distribution.PendingItems == 1);
    }

    [Fact]
    public void ReportPackDeliveryService_PersistsAttemptsAndUpdatesDistributionState()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var workflow = new ReportPackWorkflowService();
        var approved = CreateApprovedPack(
            workflow,
            [CompleteLineProvenance("trial-balance.cash", "ledger-evidence-1")]);
        var published = workflow.Publish(
            approved.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            CompleteLineProvenanceEvidenceLinks("ledger-evidence-1"));
        var store = new FileReportPackDeliveryRecordStore(
            new ReportPackDeliveryStoreOptions(Path.Combine(root, "report-pack-deliveries.json")),
            NullLogger<FileReportPackDeliveryRecordStore>.Instance);

        var service = new ReportPackDeliveryService(workflow, store);
        var attempt = service.Deliver(
            published.ReportId,
            new ReportPackDeliveryRequestDto(
                "board-reporting-committee",
                Actor: "fund-controller",
                DeliveryReference: "board-portal:packet-1",
                Note: "Delivered after publication.",
                EvidenceLinks: [new ReportPackEvidenceLinkDto("delivery-evidence-1", "Board portal receipt", "/evidence/board-portal-receipt", "delivery")],
                Formats:
                [
                    GovernanceReportArtifactFormatDto.Pdf,
                    GovernanceReportArtifactFormatDto.Xlsx,
                    GovernanceReportArtifactFormatDto.Csv
                ],
                DeliveryMode: ReportPackDeliveryModeDto.EmailLink),
            fallbackActor: "fallback");

        attempt.State.Should().Be(ReportPackDeliveryStateDto.Delivered);
        attempt.AttemptNumber.Should().Be(1);
        attempt.Recipient.Should().Be("Board reporting committee");
        attempt.DeliveryReference.Should().Be("board-portal:packet-1");
        attempt.Package.Should().NotBeNull();
        attempt.Package!.DeliveryMode.Should().Be(ReportPackDeliveryModeDto.EmailLink);
        attempt.Package.Formats.Should().Equal(
            [
                GovernanceReportArtifactFormatDto.Pdf,
                GovernanceReportArtifactFormatDto.Xlsx,
                GovernanceReportArtifactFormatDto.Csv
            ]);
        attempt.Package.Artifacts.Should().Contain(artifact =>
            artifact.Format == GovernanceReportArtifactFormatDto.Pdf &&
            artifact.ContentType == "application/pdf" &&
            artifact.RetainedPath.Contains("/board-reporting-committee/", StringComparison.OrdinalIgnoreCase));
        attempt.Package.Artifacts.Should().Contain(artifact =>
            artifact.Format == GovernanceReportArtifactFormatDto.Xlsx &&
            artifact.ContentType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        attempt.Package.Artifacts.Should().Contain(artifact =>
            artifact.Format == GovernanceReportArtifactFormatDto.Csv &&
            artifact.ContentType == "text/csv");
        attempt.Package.SecureLink.Should().Contain("/package?token=");
        attempt.EvidenceLinks.Should().Contain(link => link.Source == "report-pack-delivery");
        var token = attempt.Package.SecureLink.Split("token=", 2, StringSplitOptions.None)[1];
        service.GetPackage(published.ReportId, attempt.AttemptId, token).PackageId.Should().Be(attempt.Package.PackageId);
        service.GetPortalPackage(attempt.Package.PackageId, token).ReportId.Should().Be(published.ReportId);
        service.Invoking(item => item.GetPackage(published.ReportId, attempt.AttemptId, "bad-token"))
            .Should().Throw<UnauthorizedAccessException>()
            .WithMessage("A valid package token is required.");

        var reloaded = new ReportPackDeliveryService(workflow, store);
        reloaded.GetHistory(published.ReportId).Should().ContainSingle(item =>
            item.DistributionId == "board-reporting-committee" &&
            item.DeliveryReference == "board-portal:packet-1" &&
            item.Package != null &&
            item.Package.DeliveryMode == ReportPackDeliveryModeDto.EmailLink);

        var payload = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            workflowService: workflow,
            deliveryService: reloaded).BuildPayload();
        payload.DeliveryAttempts.Should().ContainSingle(item =>
            item.AttemptId == attempt.AttemptId &&
            item.Package != null &&
            item.Package.Artifacts.Count == 3);
        payload.ReportPackDistributions.Should().Contain(distribution =>
            distribution.DistributionId == "board-reporting-committee" &&
            distribution.State == "Delivered" &&
            distribution.PendingItems == 0 &&
            distribution.LastSentAtUtc != null);
    }

    [Fact]
    public async Task Endpoint_DeliveryPackageLink_ReturnsPackageManifestWhenTokenMatches()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.Admin);
        var workflow = app.Services.GetRequiredService<ReportPackWorkflowService>();
        var delivery = app.Services.GetRequiredService<ReportPackDeliveryService>();
        var created = workflow.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            new VersionedReportTemplateIdDto("board-pack", 1),
            "author");
        workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        var published = workflow.Publish(
            created.ReportId,
            "publisher",
            "publisher",
            "controller",
            "sha256:abc123",
            "manifest-1",
            "vault/report-packs/manifest-1.json",
            [new ReportPackEvidenceLinkDto("report-pack-1", "Report pack manifest", "/evidence/report-pack-1", "reporting")]);
        var attempt = delivery.Deliver(
            published.ReportId,
            new ReportPackDeliveryRequestDto(
                "board-reporting-committee",
                Actor: "fund-controller",
                DeliveryMode: ReportPackDeliveryModeDto.SecurePortal),
            fallbackActor: "fund-controller");
        var client = app.GetTestClient();
        var token = attempt.Package!.SecureLink.Split("token=", 2, StringSplitOptions.None)[1];

        var portalResponse = await client.GetAsync(attempt.Package.SecureLink);
        var emailLinkResponse = await client.GetAsync($"/api/fund-structure/reporting/packs/{published.ReportId:D}/deliveries/{attempt.AttemptId:D}/package?token={token}");
        var badTokenResponse = await client.GetAsync($"/api/fund-structure/reporting/packs/{published.ReportId:D}/deliveries/{attempt.AttemptId:D}/package?token=bad-token");

        portalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var portalPackage = await portalResponse.Content.ReadFromJsonAsync<ReportPackDeliveryPackageDto>(ServerJsonOptions);
        portalPackage.Should().NotBeNull();
        portalPackage!.PackageId.Should().Be(attempt.Package.PackageId);
        portalPackage.DeliveryMode.Should().Be(ReportPackDeliveryModeDto.SecurePortal);
        portalPackage.Artifacts.Should().HaveCount(3);

        emailLinkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var emailPackage = await emailLinkResponse.Content.ReadFromJsonAsync<ReportPackDeliveryPackageDto>(ServerJsonOptions);
        emailPackage.Should().NotBeNull();
        emailPackage!.PackageId.Should().Be(attempt.Package.PackageId);
        badTokenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReportingScheduleService_PersistsSchedulesAndRunsDueSchedules()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var orchestration = new ReportingOrchestrationService(
            new DefaultReportingTemplateCatalog(),
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
            runStore);
        var scheduleStore = new FileReportingScheduleStore(
            new ReportingScheduleStoreOptions(Path.Combine(root, "reporting-schedules.json")),
            NullLogger<FileReportingScheduleStore>.Instance);
        var schedules = new ReportingScheduleService(orchestration, scheduleStore);

        var created = schedules.Upsert(new ReportingScheduleUpsertRequestDto(
            "sched-investor",
            "investor-monthly-statement",
            "0 8 1 * *",
            new DateOnly(2026, 5, 1),
            new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
            2,
            "fund-controller",
            "Monthly investor statement close packet."));

        created.State.Should().Be(ReportingScheduleStateDto.Active);
        var due = await schedules.RunDueAsync(new DateTimeOffset(2026, 5, 1, 8, 5, 0, TimeSpan.Zero));

        due.Runs.Should().ContainSingle();
        var result = due.Runs.Single();
        result.Run.RunId.Should().Be("sched-investor-20260501");
        result.Run.TemplateId.Should().Be("investor-monthly-statement");
        result.Schedule.RunCount.Should().Be(1);
        result.Schedule.LastRunId.Should().Be(result.Run.RunId);
        result.Schedule.DueAtUtc.Should().Be(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero));
        result.Schedule.NextAsOfDate.Should().Be(new DateOnly(2026, 6, 1));

        var reloaded = new ReportingScheduleService(orchestration, scheduleStore);
        reloaded.ListSchedules().Should().ContainSingle(schedule =>
            schedule.ScheduleId == "sched-investor" &&
            schedule.LastRunId == result.Run.RunId &&
            schedule.RunCount == 1);
    }

    [Fact]
    public async Task ReportingRunCommandService_RunsAdHocReportsOnDemand()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var runStore = new FileReportingRunStore(
            new ReportingRunStoreOptions(Path.Combine(root, "reporting-runs")),
            NullLogger<FileReportingRunStore>.Instance);
        var catalog = new DefaultReportingTemplateCatalog();
        var orchestration = new ReportingOrchestrationService(
            catalog,
            new DeterministicReportingSectionRenderer(),
            () => new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero),
            runStore);
        var service = new ReportingRunCommandService(orchestration, catalog);

        var result = await service.RunAsync(
            new ReportingRunRequestDto(
                "investor-monthly-statement",
                new DateOnly(2026, 5, 4),
                JobId: "adhoc-investor"),
            "fund-controller",
            CancellationToken.None);

        result.Run.RunId.Should().Be("adhoc-investor-20260504");
        result.Run.TemplateId.Should().Be("investor-monthly-statement");
        result.Run.Trigger.Should().Be(ReportingRunTrigger.AdHoc.ToString());
        result.Run.Status.Should().Be(ReportingRunStatus.Draft.ToString());
        result.Run.NextActions.Should().ContainSingle(action =>
            action.Kind == "approval-submit" &&
            action.Method == "POST" &&
            action.IsEnabled);
        runStore.GetManifest(result.Run.RunId)!.Trigger.Should().Be(ReportingRunTrigger.AdHoc);
        runStore.GetAudit(result.Run.RunId).Select(static audit => audit.Action).Should().Contain("RunGenerated");
    }

    [Fact]
    public void ReportPackRunReadService_ListsRegistryTemplateDraftsAndApprovals()
    {
        var registry = new ReportTemplateRegistryService();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "custom-board-pack",
                "Custom Board Pack",
                ["cover", "exposures"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "BoardPack",
                Rationale: "Controller-specific exposure section"),
            "report.author");
        var revision = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "investor-monthly-statement",
                "Investor Monthly Statement v2",
                ["cover", "performance", "positions", "fees"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "InvestorStatement",
                BasedOnVersion: 1,
                Rationale: "Add fee disclosure"),
            "report.author");
        registry.Submit(revision.Definition.TemplateId, "report.author", "ready for controller review");
        registry.Approve(
            revision.Definition.TemplateId,
            new ReportTemplateDecisionRequestDto("Controller approved fee disclosure", "APP-TPL-2"),
            "controller.admin");

        var payload = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            templateRegistry: registry).BuildPayload();

        payload.Templates.Should().Contain(template =>
            template.TemplateId == draft.Definition.TemplateId.Name &&
            template.Version == "1" &&
            template.LifecycleStatus == ReportTemplateLifecycleStatusDto.Draft.ToString() &&
            !template.IsBuiltIn &&
            !template.IsLatestApproved &&
            template.AuthoringRoute == "/api/fund-structure/reporting/templates/custom-board-pack/versions/1");
        payload.Templates.Should().Contain(template =>
            template.TemplateId == "investor-monthly-statement" &&
            template.Version == "2" &&
            template.Family == "InvestorStatement" &&
            template.LifecycleStatus == ReportTemplateLifecycleStatusDto.Approved.ToString() &&
            !template.IsBuiltIn &&
            template.IsLatestApproved &&
            template.ApprovalSummary.Contains("APP-TPL-2", StringComparison.Ordinal));
        payload.Templates.Should().Contain(template =>
            template.TemplateId == "investor-monthly-statement" &&
            template.Version == "1" &&
            template.IsBuiltIn &&
            !template.IsLatestApproved);
    }

    [Fact]
    public void ReportPackRunReadService_ProjectsReportWriterGridMetadataForCustomTemplates()
    {
        var registry = new ReportTemplateRegistryService();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "custom-exposure-grid",
                "Custom Exposure Grid",
                ["exposures"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "Expose no-code grid",
                Grids:
                [
                    new ReportWriterGridDefinitionDto(
                        "strategy-contribution",
                        "Strategy Contribution",
                        ReportWriterGridKindDto.Contribution,
                        RowFields: ["strategy"],
                        Metrics: [new ReportWriterMetricDefinitionDto("marketValue", "marketValue")],
                        Formulas: [new ReportWriterFormulaDefinitionDto("weightCheck", "{contributionPercent}")]
                    )
                ]),
            "report.author");

        var payload = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            templateRegistry: registry).BuildPayload();

        var template = payload.Templates.Single(template => template.TemplateId == draft.Definition.TemplateId.Name);
        template.ReportWriterGrids.Should().ContainSingle();
        template.ReportWriterGrids![0].GridId.Should().Be("strategy-contribution");
        template.ReportWriterGrids[0].Kind.Should().Be(ReportWriterGridKindDto.Contribution.ToString());
        template.ReportWriterGrids[0].DimensionCount.Should().Be(1);
        template.ReportWriterGrids[0].MetricCount.Should().Be(1);
        template.ReportWriterGrids[0].FormulaCount.Should().Be(1);
    }

    [Fact]
    public void ReportAccessPolicyEvaluator_MatchesUserGroupCompanyAndOwnerPrincipals()
    {
        var policy = new ReportAccessPolicyDto(
            ReportAccessModeDto.Restricted,
            OwnerPrincipalId: "owner.user",
            Principals:
            [
                new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.User, "report.user"),
                new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, "ops-control"),
                new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Company, "company-a")
            ]);

        ReportAccessPolicyEvaluator.Evaluate(policy, new ReportAccessQueryContext("owner.user")).IsAccessible.Should().BeTrue();
        ReportAccessPolicyEvaluator.Evaluate(policy, new ReportAccessQueryContext("report.user")).IsAccessible.Should().BeTrue();
        ReportAccessPolicyEvaluator.Evaluate(policy, new ReportAccessQueryContext("viewer.user", ["ops-control"])).IsAccessible.Should().BeTrue();
        ReportAccessPolicyEvaluator.Evaluate(policy, new ReportAccessQueryContext("viewer.user", CompanyId: "company-a")).IsAccessible.Should().BeTrue();
        ReportAccessPolicyEvaluator.Evaluate(policy, new ReportAccessQueryContext("viewer.user", ["unrelated"])).IsAccessible.Should().BeFalse();
    }

    [Fact]
    public void ReportPackRunReadService_FiltersTemplatesAndPacksByAccessPolicy()
    {
        var registry = new ReportTemplateRegistryService();
        var privateTemplate = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "owner-only-pack",
                "Owner Only Pack",
                ["summary"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "User-locked report",
                AccessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.Private, OwnerPrincipalId: "owner.user")),
            "owner.user");
        var groupTemplate = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "ops-control-pack",
                "Ops Control Pack",
                ["summary"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "CustomReport",
                Rationale: "Ops control report",
                AccessPolicy: new ReportAccessPolicyDto(
                    ReportAccessModeDto.Restricted,
                    Principals: [new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, "ops-control")])),
            "owner.user");
        var workflow = new ReportPackWorkflowService();
        var privatePack = workflow.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            privateTemplate.Definition.TemplateId,
            "owner.user",
            accessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.Private, OwnerPrincipalId: "owner.user"));
        var groupPack = workflow.Create(
            "fund-a",
            "acct-1",
            "2026-03",
            groupTemplate.Definition.TemplateId,
            "owner.user",
            accessPolicy: new ReportAccessPolicyDto(
                ReportAccessModeDto.Restricted,
                Principals: [new ReportAccessPrincipalDto(ReportAccessPrincipalKindDto.Group, "ops-control")]));

        var readService = new ReportPackRunReadService(
            new DefaultReportingTemplateCatalog(),
            workflowService: workflow,
            templateRegistry: registry);
        var groupPayload = readService.BuildPayload(new ReportAccessQueryContext("viewer.user", ["ops-control"]));
        var strangerPayload = readService.BuildPayload(new ReportAccessQueryContext("viewer.user"));

        groupPayload.Templates.Select(static template => template.TemplateId).Should().Contain(groupTemplate.Definition.TemplateId.Name);
        groupPayload.Templates.Select(static template => template.TemplateId).Should().NotContain(privateTemplate.Definition.TemplateId.Name);
        groupPayload.RecentRuns.Select(static run => run.RunId).Should().Contain($"report-pack:{groupPack.ReportId:D}");
        groupPayload.RecentRuns.Select(static run => run.RunId).Should().NotContain($"report-pack:{privatePack.ReportId:D}");
        groupPayload.Templates.Single(template => template.TemplateId == groupTemplate.Definition.TemplateId.Name).AccessMode.Should().Be(ReportAccessModeDto.Restricted.ToString());

        strangerPayload.Templates.Select(static template => template.TemplateId).Should().NotContain(groupTemplate.Definition.TemplateId.Name);
        strangerPayload.Templates.Select(static template => template.TemplateId).Should().NotContain(privateTemplate.Definition.TemplateId.Name);
        strangerPayload.RecentRuns.Select(static run => run.RunId).Should().NotContain($"report-pack:{groupPack.ReportId:D}");
        strangerPayload.RecentRuns.Select(static run => run.RunId).Should().NotContain($"report-pack:{privatePack.ReportId:D}");
    }

    [Fact]
    public async Task Endpoint_RenderPrivateTemplate_WhenCallerIsNotOwner_ReturnsForbidden()
    {
        await using var app = await CreateFundStructureAppAsync(UserRole.Analysis, "viewer.user");
        var registry = app.Services.GetRequiredService<ReportTemplateRegistryService>();
        var draft = registry.CreateDraft(
            new ReportTemplateDraftRequestDto(
                "private-investor-pack",
                "Private Investor Pack",
                ["summary"],
                [new ReportTemplateParameterDefinitionDto("period", Required: true)],
                Family: "InvestorStatement",
                Rationale: "Private report test",
                AccessPolicy: new ReportAccessPolicyDto(ReportAccessModeDto.Private, OwnerPrincipalId: "owner.user")),
            "owner.user");
        registry.Submit(draft.Definition.TemplateId, "owner.user", "ready");
        registry.Approve(draft.Definition.TemplateId, new ReportTemplateDecisionRequestDto("approved", "APP-PRIVATE-1"), "controller.admin");
        var client = app.GetTestClient();

        var listResponse = await client.GetAsync("/api/fund-structure/reporting/templates");
        var renderResponse = await client.PostAsJsonAsync(
            "/api/fund-structure/reporting/templates/render",
            new RenderReportTemplateRequestDto(
                draft.Definition.TemplateId,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["period"] = "2026-03" }),
            ServerJsonOptions);

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<ReportTemplateGovernanceRecordDto>>(ServerJsonOptions);
        records.Should().NotBeNull();
        records!.Select(static record => record.Definition.TemplateId.Name).Should().NotContain(draft.Definition.TemplateId.Name);
        renderResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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

    private static async Task<WebApplication> CreateFundStructureAppAsync(UserRole role, string username = "controller.admin")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ReportTemplateRegistryService>();
        builder.Services.AddSingleton<ReportPackWorkflowService>();
        builder.Services.AddSingleton<ReportPackDeliveryService>();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = username;
            context.Items[LoginSessionMiddleware.CurrentUserRoleKey] = role;
            await next();
        });
        app.MapFundStructureEndpoints(ServerJsonOptions);

        await app.StartAsync();
        return app;
    }
}
