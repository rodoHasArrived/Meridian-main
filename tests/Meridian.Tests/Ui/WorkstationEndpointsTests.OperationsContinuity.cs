using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.ProviderRouting;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Monitoring;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Meridian.ProviderSdk;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using IReconciliationApiService = Meridian.Contracts.Reconciliation.IReconciliationApiService;
using ReconciliationCaseSummaryDto = Meridian.Contracts.Reconciliation.ReconciliationCaseSummaryDto;
using ReconciliationQueueAccountStatusDto = Meridian.Contracts.Reconciliation.ReconciliationQueueAccountStatusDto;
using StatementImportSummaryDto = Meridian.Contracts.Reconciliation.StatementImportSummaryDto;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityDetail_ShouldExposeExplicitContinuityContractFields()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();
        var fundAccountId = Guid.NewGuid();
        var evidence = new OperationsEvidenceLinkDto(
            EvidenceId: "ev-close-checklist",
            Label: "Close Checklist",
            Route: "/api/workstation/reporting/close-checklist/ev-close-checklist",
            Source: "ops-runbook",
            CapturedAtUtc: new DateTimeOffset(2026, 5, 20, 9, 30, 0, TimeSpan.Zero));

        var start = await PostTransitionAsync(client, "/api/workstation/operations/continuity", new OperationsStartWorkflowRequestDto(
            fundAccountId,
            "2026-05",
            Guid.NewGuid(),
            "custodian",
            "spoofed-user",
            EvidenceLinks: [evidence]));
        var workflowId = start.Workflow!.WorkflowId;

        var import = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/import",
            new OperationsTransitionRequestDto(start.Workflow.Version, "spoofed-user", EvidenceLinks: [evidence]));
        var normalized = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/normalize",
            new OperationsTransitionRequestDto(import.Workflow!.Version, "spoofed-user", EvidenceLinks: [evidence]));
        var security = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/security-master/resolve",
            new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "spoofed-user", EvidenceLinks: [evidence]));
        var draft = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/draft",
            new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "spoofed-user", "ledger-preview-1", true, EvidenceLinks: [evidence]));
        var validated = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/validate",
            new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "spoofed-user", true, true, EvidenceLinks: [evidence]));
        var posted = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/post",
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version,
                "spoofed-user",
                "ledger-batch-1",
                "period-close",
                true,
                EvidenceLinks: [evidence],
                JournalCandidate: CreateOperationsLedgerJournalCandidate(start.Workflow!.FundAccountId)));
        var reconciled = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/reconciliation/run",
            new OperationsReconciliationRunRequestDto(posted.Workflow!.Version, "spoofed-user", BreakCases: [], EvidenceLinks: [evidence]));
        var posture = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/posture/refresh",
            new OperationsGatePostureRequestDto(
                reconciled.Workflow!.Version,
                "spoofed-user",
                ReportPackReady: false,
                ReportPackId: "report-pack-2026-05",
                EvidenceLinks: [evidence]));

        posture.Workflow.Should().NotBeNull();
        var workflow = posture.Workflow!;
        workflow.EvidenceLinks.Should().NotBeEmpty();
        workflow.ReportPackReadiness.ReportPackId.Should().Be("report-pack-2026-05");
        workflow.ReportPackReadiness.IsReady.Should().BeFalse();
        workflow.NextActions.Should().NotBeEmpty();
        workflow.Blockers.Should().Contain(blocker => blocker.EvidenceLinks.Any());
        workflow.AccountingRecordSummary.Should().NotBeNull();
        workflow.AccountingRecordSummary!.RequiredCategoryCount.Should().Be(8);
        workflow.AccountingRecordSummary.CompleteCategoryCount.Should().Be(4);
        workflow.AccountingRecordSummary.IsAuditReady.Should().BeFalse();
        workflow.AccountingRecordSummary.AuditPackReadiness.Should().NotBeNull();
        workflow.AccountingRecordSummary.AuditPackReadiness!.MissingEvidenceCategories.Should().Contain(FundAuditEvidenceCategoryKeyDto.ReportPack);
        workflow.AccountingRecordSummary.EvidenceCategories.Select(category => category.Key).Should().BeEquivalentTo(
        [
            "source-records",
            "normalized-activity",
            "reconciliation-case-history",
            "ledger-evidence",
            "approvals",
            "report-pack",
            "exports",
            "restatement-lineage"
        ]);
        workflow.AccountingRecordSummary.EvidenceCategories
            .Should().Contain(category => category.Key == "report-pack" && !category.IsComplete);
        workflow.AccountingRecordSummary.EvidenceCategories
            .Should().Contain(category =>
                category.Key == "exports" &&
                category.Status.Contains("Export manifest", StringComparison.OrdinalIgnoreCase) &&
                category.Status.Contains("retained evidence hash", StringComparison.OrdinalIgnoreCase));
        workflow.AccountingRecordSummary.EvidenceCategories
            .Should().Contain(category =>
                category.Key == "restatement-lineage" &&
                category.RequiredEvidence != null &&
                category.RequiredEvidence.Contains("published baseline") &&
                category.RequiredEvidence.Contains("changed-line evidence"));
        workflow.AccountingRecordSummary.EvidenceCategories
            .Should().Contain(category => category.Key == "ledger-evidence" && category.IsComplete);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityReadRoutes_ShouldRequireReadPermission()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.ViewStrategies);
        var client = app.GetTestClient();
        var workflowId = Guid.NewGuid().ToString("D");
        var routes = new[]
        {
            UiApiRoutes.OperationsContinuity,
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityById, "workflowId", workflowId),
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityTimeline, "workflowId", workflowId),
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityBreaks, "workflowId", workflowId),
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityLedgerPreview, "workflowId", workflowId)
        };

        foreach (var route in routes)
        {
            var response = await client.GetAsync(route);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"route {route} exposes operations continuity evidence");
        }
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityRoutes_ShouldUseTrustedActorInsteadOfRequestActor()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();

        var startResponse = await client.PostAsJsonAsync(
            "/api/workstation/operations/continuity",
            new OperationsStartWorkflowRequestDto(
                Guid.NewGuid(),
                "2026-05",
                SecurityMasterSnapshotId: Guid.NewGuid(),
                BrokerSource: "custodian",
                Actor: "spoofed-user",
                Rationale: "Open monthly close lane"),
            ServerJsonOptions);

        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var startJson = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
        var workflowId = startJson.RootElement.GetProperty("workflow").GetProperty("workflowId").GetGuid();

        using var timeline = await ReadJsonAsync(client, $"/api/workstation/operations/continuity/{workflowId}/timeline");
        timeline.RootElement[0].GetProperty("actor").GetString().Should().Be("ops-user");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityMaterialCommands_ShouldRejectAssistantOrigin()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();

        var start = await PostTransitionAsync(client, "/api/workstation/operations/continuity", new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "spoofed-user"));
        var workflowId = start.Workflow!.WorkflowId;
        var import = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/import",
            new OperationsTransitionRequestDto(start.Workflow.Version, "spoofed-user"));
        var normalized = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/normalize",
            new OperationsTransitionRequestDto(import.Workflow!.Version, "spoofed-user"));
        var security = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/security-master/resolve",
            new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "spoofed-user"));
        var draft = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/draft",
            new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "spoofed-user", "ledger-preview-1", true));
        var validated = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/validate",
            new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "spoofed-user", true, true));

        var response = await client.PostAsJsonAsync(
            $"/api/workstation/operations/continuity/{workflowId}/ledger/post",
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version,
                "spoofed-user",
                "ledger-batch-1",
                "period-close",
                true,
                JournalCandidate: CreateOperationsLedgerJournalCandidate(start.Workflow!.FundAccountId),
                ActionOrigin: OperationsActionOriginDto.AutomationAssistant),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var result = await response.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("REVIEWED_AUTOMATION_REVIEW_REQUIRED");
        result.Blockers.Should().Contain(blocker =>
            blocker.Code == "REVIEWED_AUTOMATION_MATERIAL_ACTION_REJECTED" &&
            blocker.Gate == OperationsGateKeyDto.LedgerPosting);

        using var detail = await ReadJsonAsync(client, $"/api/workstation/operations/continuity/{workflowId}");
        detail.RootElement.GetProperty("ledgerPostingState").GetString().Should().Be(nameof(OperationsLedgerPostingStateDto.Validated));
        detail.RootElement.GetProperty("timeline")
            .EnumerateArray()
            .Select(entry => entry.GetProperty("eventType").GetString())
            .Should()
            .NotContain("ledger-posted");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityBreakAssign_ShouldTrustSessionActorAndRetainEscalation()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();

        var start = await PostTransitionAsync(client, "/api/workstation/operations/continuity", new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "spoofed-user"));
        var workflowId = start.Workflow!.WorkflowId;
        var import = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/import",
            new OperationsTransitionRequestDto(start.Workflow.Version, "spoofed-user"));
        var normalized = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/normalize",
            new OperationsTransitionRequestDto(import.Workflow!.Version, "spoofed-user"));
        var security = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/security-master/resolve",
            new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "spoofed-user"));
        var draft = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/draft",
            new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "spoofed-user", "ledger-preview-1", true));
        var validated = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/validate",
            new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "spoofed-user", true, true));
        var posted = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/post",
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version,
                "spoofed-user",
                "ledger-batch-1",
                "period-close",
                true,
                JournalCandidate: CreateOperationsLedgerJournalCandidate(start.Workflow.FundAccountId)));
        var reconciled = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/reconciliation/run",
            new OperationsReconciliationRunRequestDto(
                posted.Workflow!.Version,
                "spoofed-user",
                BreakCases: [CreateOperationsContinuityOpenBreak(start.Workflow.FundAccountId, "break-http-assign")]));

        var assignRoute = UiApiRoutes.WithParam(
            UiApiRoutes.WithParam(UiApiRoutes.OperationsContinuityReconciliationBreakAssign, "workflowId", workflowId.ToString("D")),
            "breakId",
            "break-http-assign");
        var assigned = await PostTransitionAsync(client, assignRoute, new OperationsAssignBreakCaseRequestDto(
            reconciled.Workflow!.Version,
            "spoofed-user",
            Owner: "fund-controller",
            Rationale: "Escalate aged cash exception",
            EscalationLevel: "Level 2",
            EscalationReason: "Aged past SLA",
            DueDate: new DateOnly(2026, 6, 3),
            EvidenceLinks:
            [
                new OperationsEvidenceLinkDto(
                    "break-assignment-evidence",
                    "Break assignment evidence",
                    "/api/workstation/evidence/break-assignment-evidence",
                    "operator-note",
                    new DateTimeOffset(2026, 5, 31, 13, 30, 0, TimeSpan.Zero))
            ]));

        var breakCase = assigned.Workflow!.BreakCases.Single(item => item.BreakId == "break-http-assign");
        breakCase.Owner.Should().Be("fund-controller");
        breakCase.Status.Should().Be("InReview");
        breakCase.DueDate.Should().Be(new DateOnly(2026, 6, 3));
        breakCase.EscalationLevel.Should().Be("Level 2");
        breakCase.EscalationReason.Should().Be("Aged past SLA");
        breakCase.EvidenceLinks.Should().Contain(link => link.EvidenceId == "break-assignment-evidence");
        assigned.Workflow.Timeline.Should().Contain(entry =>
            entry.EventType == "reconciliation-break-escalated" &&
            entry.Actor == "ops-user" &&
            !string.IsNullOrWhiteSpace(entry.CorrelationId) &&
            entry.CorrelationId == assigned.Outcome.CorrelationId);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityStartRoute_ShouldRequireMutationPermission()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.ViewStrategies);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/workstation/operations/continuity",
            new OperationsStartWorkflowRequestDto(
                Guid.NewGuid(),
                "2026-05",
                SecurityMasterSnapshotId: Guid.NewGuid(),
                BrokerSource: "custodian",
                Actor: "spoofed-user",
                Rationale: "Open monthly close lane"),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityImportRoute_ShouldRequireMutationPermission()
    {
        await using var privilegedApp = await CreateAppAsync(RegisterOperationsContinuityServices);
        var privilegedClient = privilegedApp.GetTestClient();

        var startResponse = await privilegedClient.PostAsJsonAsync(
            "/api/workstation/operations/continuity",
            new OperationsStartWorkflowRequestDto(
                Guid.NewGuid(),
                "2026-05",
                SecurityMasterSnapshotId: Guid.NewGuid(),
                BrokerSource: "custodian",
                Actor: "ops-user",
                Rationale: "Open monthly close lane"),
            ServerJsonOptions);
        using var startJson = await JsonDocument.ParseAsync(await startResponse.Content.ReadAsStreamAsync());
        var workflow = startJson.RootElement.GetProperty("workflow");
        var workflowId = workflow.GetProperty("workflowId").GetGuid();
        var expectedVersion = workflow.GetProperty("version").GetInt64();

        await using var restrictedApp = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.ViewStrategies);
        var restrictedClient = restrictedApp.GetTestClient();

        var response = await restrictedClient.PostAsJsonAsync(
            $"/api/workstation/operations/continuity/{workflowId}/broker/import",
            new OperationsTransitionRequestDto(
                expectedVersion,
                "spoofed-user",
                "Import broker statement"),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityReopenRoute_ShouldRequireGovernedAdminPermission()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.ManageDirectLending);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            $"/api/workstation/operations/continuity/{Guid.NewGuid()}/reopen",
            new OperationsReopenWorkflowRequestDto(
                1,
                "spoofed-user",
                "Reopen workflow",
                "incident-123",
                IsGovernedAdmin: true),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuitySecurityMasterOverrideApprovalRoute_ShouldRequireOverrideApprovalPermission()
    {
        await using var app = await CreateAppAsync(
            RegisterOperationsContinuityServices,
            currentUserPermissions: UserPermission.ManageDirectLending);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            $"/api/workstation/operations/continuity/{Guid.NewGuid()}/security-master/overrides/override-1/approve",
            new OperationsSecurityMasterOverrideApprovalRequestDto(
                1,
                "spoofed-user",
                "override-1",
                "approve override",
                "policy-1",
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))),
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapWorkstationEndpoints_OperationsContinuityCommandRoutes_ShouldRequireAccountingCloseAuthority(bool controller)
    {
        var scope = new CloseReadinessScopeDto("test-fund-profile", Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid().ToString("D"), "2026-05");
        var closeAuthority = new HttpTransitionCloseAuthority(scope);
        await using var app = await CreateAppAsync(services =>
        {
            RegisterOperationsContinuityServices(services);
            RegisterRetainedReportBook(services, scope.LedgerBookId!.Value, scope.FundAccountId!.Value);
            services.AddHttpContextAccessor();
            services.AddSingleton<IWorkstationTenantContextAccessor, HttpContextWorkstationTenantContextAccessor>();
            services.AddSingleton<IFundProfileTenantGuard, RegistryFundProfileTenantGuard>();
            services.AddSingleton<IFinancialOperationsCommandCenterReadService>(closeAuthority);
            services.AddSingleton<IClosePublicationReadinessGuard>(sp => new ClosePublicationReadinessGuard(
                () => sp.GetRequiredService<IFinancialOperationsCommandCenterReadService>(),
                sp.GetRequiredService<IWorkstationTenantContextAccessor>(),
                () => new OperationsReportPackAuthority(
                    sp.GetRequiredService<Meridian.FinancialOperations.AccountingClose.IAccountingReportPackageService>(),
                    sp.GetRequiredService<ILedgerBookService>(),
                    sp.GetRequiredService<IFundProfileTenancyRegistry>(),
                    sp.GetRequiredService<ILedgerJournalStore>())));
        }, currentUserPermissions: UserPermission.AdminMaintenance,
            currentUserRole: controller ? UserRole.Controller : UserRole.FundAccountant);
        var client = app.GetTestClient();

        var start = await PostTransitionAsync(client, "/api/workstation/operations/continuity", new OperationsStartWorkflowRequestDto(
            scope.FundAccountId!.Value,
            scope.PeriodId!,
            null,
            "custodian",
            "spoofed-user",
            LedgerBookId: scope.LedgerBookId));
        var workflowId = start.Workflow!.WorkflowId;

        var import = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/import",
            new OperationsTransitionRequestDto(start.Workflow.Version, "spoofed-user"));
        var normalized = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/normalize",
            new OperationsTransitionRequestDto(import.Workflow!.Version, "spoofed-user"));
        var security = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/security-master/resolve",
            new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "spoofed-user"));
        var draft = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/draft",
            new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "spoofed-user", "ledger-preview-1", true));
        var validated = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/validate",
            new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "spoofed-user", true, true));
        var posted = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/post",
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version,
                "spoofed-user",
                "ledger-batch-1",
                "period-close",
                true,
                JournalCandidate: CreateOperationsLedgerJournalCandidate(start.Workflow!.FundAccountId)));
        var reconciled = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/reconciliation/run",
            new OperationsReconciliationRunRequestDto(posted.Workflow!.Version, "spoofed-user", BreakCases: []));
        var retainedPackageId = await RetainPreCloseReportAsync(app.Services, scope.LedgerBookId!.Value);
        var posture = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/posture/refresh",
            new OperationsGatePostureRequestDto(reconciled.Workflow!.Version, "spoofed-user", ReportPackReady: true, ReportPackId: retainedPackageId));
        var acknowledged = await AcknowledgeOperationsChecklistAsync(client, posture.Workflow!);
        var submitted = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/approval/submit",
            new OperationsSubmitApprovalRequestDto(
                acknowledged.Version,
                "spoofed-user",
                "independent-reviewer",
                "Submit evidence",
                retainedPackageId,
                ChecklistControlApprovals: RequiredOperationsChecklistControlApprovals(acknowledged)));
        client.DefaultRequestHeaders.Add("X-Meridian-Test-User", "independent-reviewer");
        var approved = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/approval/approve",
            new OperationsApprovalDecisionRequestDto(
                submitted.Workflow!.Version,
                "spoofed-user",
                "spoofed-reviewer",
                "Approve close",
                retainedPackageId,
                ChecklistControlApprovals: RequiredOperationsChecklistControlApprovals(submitted.Workflow!)));
        closeAuthority.Workflow = approved.Workflow;
        client.DefaultRequestHeaders.Remove("X-Meridian-Test-User");
        var closeRoute = $"/api/workstation/operations/continuity/{workflowId}/close";
        var closeRequest = new OperationsCloseWorkflowRequestDto(approved.Workflow!.Version, "spoofed-user",
            "Close period", retainedPackageId, ChecklistControlApprovals: RequiredOperationsChecklistControlApprovals(approved.Workflow!), CloseScope: scope);
        using var missingScope = await client.PostAsJsonAsync(closeRoute, closeRequest with { CloseScope = null }, ServerJsonOptions);
        missingScope.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var refusal = await missingScope.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
        refusal!.Success.Should().BeFalse();
        refusal.Blockers.Should().Contain(blocker => blocker.Code == "CLOSE_READINESS_REQUIRED");
        closeAuthority.Requests.Should().BeEmpty("missing scope must stop before evaluating or mutating a close");
        var unchanged = await app.Services.GetRequiredService<IOperationsContinuityWorkflowService>().GetAsync(workflowId);
        unchanged!.Version.Should().Be(approved.Workflow.Version);
        unchanged.Status.Should().NotBe(OperationsWorkflowStatusDto.Closed);

        using var wrongScope = await client.PostAsJsonAsync(closeRoute,
            closeRequest with { CloseScope = scope with { EntityId = "another-entity" } }, ServerJsonOptions);
        wrongScope.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrongScope.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions))!.Success.Should().BeFalse();

        using var deniedClose = await client.PostAsJsonAsync(closeRoute, closeRequest, ServerJsonOptions);
        if (controller)
        {
            deniedClose.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var accountingRefusal = await deniedClose.Content.ReadFromJsonAsync<OperationsTransitionResultDto>(ServerJsonOptions);
            accountingRefusal!.ErrorCode.Should().Be("ACCOUNTING_CLOSE_REQUIRED");
            accountingRefusal.Blockers.Should().Contain(blocker => blocker.Code == "ClosePeriodMutationConsistencyGateUnavailable",
                "Controller compatibility requests must execute the governed accounting hard-close service");
        }
        else
        {
            deniedClose.StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "Operations mutation permission does not confer Controller authority for hard close");
        }
        var retained = await app.Services.GetRequiredService<IOperationsContinuityWorkflowService>().GetAsync(workflowId);
        closeAuthority.Requests.Last().Scope.Should().Be(scope);
        closeAuthority.Requests.Should().OnlyContain(request => request.TenantId == "tenant-test" && request.CompanyId == "tenant-test");

        retained!.Status.Should().Be(OperationsWorkflowStatusDto.ReadyForClose);
        retained.Timeline.Should().NotContain(entry => entry.EventType == "workflow-closed");
        retained.Approvals.Should().Contain(approval =>
            approval.Status == OperationsApprovalStateDto.Approved &&
            approval.Operator == "ops-user" &&
            approval.Reviewer == "independent-reviewer");
        retained.Timeline.Should().Contain(entry =>
            entry.EventType == "approval-approved" && entry.Actor == "independent-reviewer");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityApprovalSubmit_ShouldPreserveAssignedReviewerFromRequest()
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await using var app = await CreateAppAsync(services =>
        {
            RegisterOperationsContinuityServices(services);
            RegisterRetainedReportBook(services, bookId, accountId);
        });
        var client = app.GetTestClient();

        var start = await PostTransitionAsync(client, "/api/workstation/operations/continuity", new OperationsStartWorkflowRequestDto(
            accountId,
            "2026-05",
            null,
            "custodian",
            "spoofed-user", LedgerBookId: bookId));
        var workflowId = start.Workflow!.WorkflowId;
        var import = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/import",
            new OperationsTransitionRequestDto(start.Workflow.Version, "spoofed-user"));
        var normalized = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/normalize",
            new OperationsTransitionRequestDto(import.Workflow!.Version, "spoofed-user"));
        var security = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/security-master/resolve",
            new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "spoofed-user"));
        var draft = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/draft",
            new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "spoofed-user", "ledger-preview-1", true));
        var validated = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/validate",
            new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "spoofed-user", true, true));
        var posted = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/post",
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version,
                "spoofed-user",
                "ledger-batch-1",
                "period-close",
                true,
                JournalCandidate: CreateOperationsLedgerJournalCandidate(start.Workflow!.FundAccountId)));
        var reconciled = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/reconciliation/run",
            new OperationsReconciliationRunRequestDto(posted.Workflow!.Version, "spoofed-user", BreakCases: []));
        var retainedPackageId = await RetainPreCloseReportAsync(app.Services, bookId);
        var posture = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/posture/refresh",
            new OperationsGatePostureRequestDto(reconciled.Workflow!.Version, "spoofed-user", ReportPackReady: true, ReportPackId: retainedPackageId));

        const string assignedReviewer = "independent-reviewer";
        var acknowledged = await AcknowledgeOperationsChecklistAsync(client, posture.Workflow!);
        var submitted = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/approval/submit",
            new OperationsSubmitApprovalRequestDto(
                acknowledged.Version,
                "spoofed-user",
                assignedReviewer,
                "Submit evidence",
                retainedPackageId,
                ChecklistControlApprovals: RequiredOperationsChecklistControlApprovals(acknowledged)));

        submitted.Workflow!.Approvals.Should().Contain(approval =>
            approval.Status == OperationsApprovalStateDto.Submitted &&
            approval.Operator == "ops-user" &&
            approval.Reviewer == assignedReviewer);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityReconciliationRun_ShouldBridgeRealReconciliationOutputsIntoGatePosture()
    {
        var bankEntityId = Guid.NewGuid();
        var fundAccountId = Guid.NewGuid();
        var ledgerBookId = Guid.NewGuid();
        var accountingPeriodId = Guid.NewGuid();
        var accountingAsOf = new DateOnly(2026, 5, 31);
        var reconciliation = BuildOperationsContinuityReconciliationDetail("recon-ops-1", "run-ops-1");
        var reconciliationService = new StaticReconciliationRunService(reconciliation);
        var statementAuthority = Substitute.For<IReconciliationApiService>();
        statementAuthority
            .GetAuthorizedFundAccountAsync(
                fundAccountId,
                Arg.Any<ReconciliationBreakQueueScope>(),
                Arg.Any<CancellationToken>())
            .Returns(new Meridian.Contracts.Reconciliation.ReconciliationFundAccountAuthorization(
                fundAccountId,
                "test-fund-profile"));
        statementAuthority
            .GetStatementRunAuthorizationAsync(
                "run-ops-1",
                Arg.Any<ReconciliationBreakQueueScope>(),
                Arg.Any<CancellationToken>())
            .Returns(new Meridian.Contracts.Reconciliation.StatementReconciliationRunAuthorization(
                "run-ops-1",
                fundAccountId,
                "test-fund-profile",
                ledgerBookId,
                accountingPeriodId,
                accountingAsOf,
                new DateOnly(2026, 5, 1),
                accountingAsOf));
        await using var app = await CreateAppAsync(services =>
        {
            RegisterOperationsContinuityServices(services);
            services.RemoveAll<IReconciliationRunService>();
            services.AddSingleton<IReconciliationRunService>(reconciliationService);
            services.RemoveAll<IReconciliationApiService>();
            services.AddSingleton(statementAuthority);
        });
        var client = app.GetTestClient();

        var start = await PostTransitionAsync(client, "/api/workstation/operations/continuity", new OperationsStartWorkflowRequestDto(
            fundAccountId,
            accountingPeriodId.ToString("D"),
            null,
            "custodian",
            "spoofed-user",
            LedgerBookId: ledgerBookId));
        var workflowId = start.Workflow!.WorkflowId;
        var import = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/import",
            new OperationsTransitionRequestDto(start.Workflow.Version, "spoofed-user"));
        var normalized = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/broker/normalize",
            new OperationsTransitionRequestDto(import.Workflow!.Version, "spoofed-user"));
        var security = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/security-master/resolve",
            new OperationsSecurityMasterResolveRequestDto(normalized.Workflow!.Version, "spoofed-user"));
        var draft = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/draft",
            new OperationsLedgerDraftRequestDto(security.Workflow!.Version, "spoofed-user", "ledger-preview-1", true));
        var validated = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/validate",
            new OperationsLedgerValidationRequestDto(draft.Workflow!.Version, "spoofed-user", true, true));
        var posted = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/ledger/post",
            new OperationsLedgerPostRequestDto(
                validated.Workflow!.Version,
                "spoofed-user",
                "ledger-batch-1",
                "period-close",
                true,
                JournalCandidate: CreateOperationsLedgerJournalCandidate(
                    start.Workflow!.FundAccountId,
                    accountingPeriodId)));
        await app.Services
            .GetRequiredService<IOperationsWorkflowAuditStore>()
            .AppendAsync(new OperationsWorkflowAuditDraft(
                workflowId,
                fundAccountId,
                accountingPeriodId.ToString("D"),
                "StatementIntakeRetained",
                posted.Workflow!.Status,
                posted.Workflow!.Status,
                OperationsGateKeyDto.BrokerIngest,
                OperationsGateStatusDto.Passed,
                OperationsGateStatusDto.Passed,
                "statement-intake",
                "Retained statement intake for the exact Operations workflow.",
                "run-ops-1",
                [
                    new OperationsEvidenceLinkDto(
                        "statement-intake:run-ops-1",
                        "Retained statement",
                        "/api/workstation/reconciliation/statement-reconciliation-report/run-ops-1",
                        "statement-reconciliation-report",
                        new DateTimeOffset(2026, 5, 31, 23, 59, 0, TimeSpan.Zero))
                ]));

        var bridged = await PostTransitionAsync(client, $"/api/workstation/operations/continuity/{workflowId}/reconciliation/run",
            new OperationsReconciliationRunRequestDto(
                posted.Workflow!.Version,
                "spoofed-user",
                SourceRunId: "run-ops-1",
                BankEntityId: bankEntityId,
                AmountTolerance: 0.05m,
                MaxAsOfDriftMinutes: 10));

        reconciliationService.RunAsyncCallCount.Should().Be(0,
            "the Operations bridge must project the retained reconciliation result instead of starting another run");
        reconciliationService.GetLatestForRunCallCount.Should().Be(1);
        reconciliationService.LastRunRequest.Should().BeNull();
        bridged.Success.Should().BeTrue();
        bridged.Workflow!.Status.Should().Be(OperationsWorkflowStatusDto.Blocked);
        bridged.Workflow.BreakCases.Should().ContainSingle(breakCase =>
            breakCase.BreakId == "recon-ops-1:bank-break-1" &&
            breakCase.Severity == nameof(ReconciliationBreakSeverity.Critical) &&
            breakCase.EvidenceLinks.Any(link => link.Source == "reconciliation-run"));
        bridged.Workflow.BreakCases.Should().Contain(breakCase =>
            breakCase.CheckId == "SM_RECON_SECURITY_UNRESOLVED" &&
            breakCase.Category == "SecurityMasterCoverage" &&
            breakCase.Symbol == "BOND1" &&
            breakCase.EvidenceLinks.Any(link => link.Source == "security-master-coverage"));
        bridged.Workflow.BreakCases.Should().Contain(breakCase =>
            breakCase.CheckId == "SM_ACCOUNTING_TERMS_INCOMPLETE" &&
            breakCase.Category == "SecurityMasterAccounting" &&
            breakCase.Symbol == "BOND1" &&
            breakCase.EvidenceLinks.Any(link => link.Source == "security-master-accounting"));
        bridged.Workflow.EvidenceLinks.Should().Contain(link =>
            link.EvidenceId == "bank-normalized-activity:recon-ops-1" &&
            link.Source == "bank-normalized-activity");
        bridged.Workflow.Blockers.Should().Contain(blocker =>
            blocker.Code == "SM_ACCOUNTING_TERMS_INCOMPLETE" &&
            blocker.Gate == OperationsGateKeyDto.SecurityMaster);
        bridged.Workflow.Blockers.Should().Contain(blocker =>
            blocker.Code == "RECONCILIATION_CRITICAL_BREAKS_OPEN" &&
            blocker.Gate == OperationsGateKeyDto.Reconciliation);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_OperationsContinuityRoutes_ShouldReturnConsistentValidationShapeForMissingBody()
    {
        await using var app = await CreateAppAsync(RegisterOperationsContinuityServices);
        var client = app.GetTestClient();
        var start = await PostTransitionAsync(client, "/api/workstation/operations/continuity", new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(),
            "2026-05",
            null,
            "custodian",
            "spoofed-user"));
        var workflowId = start.Workflow!.WorkflowId;

        var endpoints = new[]
        {
            "/api/workstation/operations/continuity",
            $"/api/workstation/operations/continuity/{workflowId}/broker/import",
            $"/api/workstation/operations/continuity/{workflowId}/broker/normalize",
            $"/api/workstation/operations/continuity/{workflowId}/security-master/resolve",
            $"/api/workstation/operations/continuity/{workflowId}/ledger/draft",
            $"/api/workstation/operations/continuity/{workflowId}/ledger/validate",
            $"/api/workstation/operations/continuity/{workflowId}/ledger/post",
            $"/api/workstation/operations/continuity/{workflowId}/reconciliation/run",
            $"/api/workstation/operations/continuity/{workflowId}/posture/refresh",
            $"/api/workstation/operations/continuity/{workflowId}/approval/submit",
            $"/api/workstation/operations/continuity/{workflowId}/approval/approve",
            $"/api/workstation/operations/continuity/{workflowId}/approval/reject",
            $"/api/workstation/operations/continuity/{workflowId}/close"
        };

        foreach (var endpoint in endpoints)
        {
            var response = await client.PostAsync(endpoint, content: null);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            json.RootElement.GetProperty("errors").GetProperty("request")[0].GetString().Should().NotBeNullOrWhiteSpace();
        }

        var reopenResponse = await client.PostAsync($"/api/workstation/operations/continuity/{workflowId}/reopen", content: null);
        reopenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_DataOperationsProviderMetrics_ShouldExposeDk1TrustRationale()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "provider-metrics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "_status"));
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, """{"DataRoot":"."}""");

        var metrics = new ProviderMetricsStatus(
            Timestamp: new DateTimeOffset(2026, 4, 24, 17, 0, 0, TimeSpan.Zero),
            Providers:
            [
                new ProviderMetrics(
                    ProviderId: "yahoo",
                    ProviderType: "Historical bars",
                    IsConnected: true,
                    TradesReceived: 0,
                    DepthUpdatesReceived: 0,
                    QuotesReceived: 2400,
                    ConnectionAttempts: 1,
                    ConnectionFailures: 0,
                    MessagesDropped: 0,
                    ActiveSubscriptions: 4,
                    AverageLatencyMs: 42,
                    MinLatencyMs: 25,
                    MaxLatencyMs: 80,
                    DataQualityScore: 0.96,
                    ConnectionSuccessRate: 1,
                    Timestamp: new DateTimeOffset(2026, 4, 24, 16, 59, 0, TimeSpan.Zero)),
                new ProviderMetrics(
                    ProviderId: "alpaca",
                    ProviderType: "Streaming equities",
                    IsConnected: false,
                    TradesReceived: 12,
                    DepthUpdatesReceived: 0,
                    QuotesReceived: 30,
                    ConnectionAttempts: 4,
                    ConnectionFailures: 4,
                    MessagesDropped: 0,
                    ActiveSubscriptions: 2,
                    AverageLatencyMs: 510,
                    MinLatencyMs: 90,
                    MaxLatencyMs: 900,
                    DataQualityScore: 0.62,
                    ConnectionSuccessRate: 0,
                    Timestamp: new DateTimeOffset(2026, 4, 24, 16, 58, 0, TimeSpan.Zero))
            ],
            TotalProviders: 2,
            HealthyProviders: 1);
        var metricsJson = JsonSerializer.Serialize(metrics, ServerJsonOptions);
        await File.WriteAllTextAsync(Path.Combine(root, "_status", "providers.json"), metricsJson);

        await using var app = await CreateAppAsync(services =>
        {
            RegisterConfigStores(services, configPath);
        },
            currentUserPermissions: UserPermission.ModifySecurityMaster | UserPermission.ManageCredentials | UserPermission.ViewHistoricalData);
        var client = app.GetTestClient();

        using var dataOperations = await ReadJsonAsync(client, "/api/workstation/data-operations");
        var providers = dataOperations.RootElement.GetProperty("providers");
        providers.GetArrayLength().Should().Be(2);

        var healthy = providers.EnumerateArray()
            .First(provider => provider.GetProperty("providerId").GetString() == "yahoo");
        healthy.GetProperty("provider").GetString().Should().Be("yahoo");
        healthy.GetProperty("displayName").GetString().Should().NotBeNullOrWhiteSpace();
        healthy.GetProperty("status").GetString().Should().Be("Healthy");
        healthy.GetProperty("trustScore").GetString().Should().Be("96%");
        healthy.GetProperty("reasonCode").GetString().Should().Be("HEALTHY_BASELINE");
        healthy.GetProperty("recommendedAction").GetString().Should().Contain("no DK1 action");
        healthy.GetProperty("diagnostics").GetArrayLength().Should().BeGreaterThan(0);

        var degraded = providers.EnumerateArray()
            .First(provider => provider.GetProperty("providerId").GetString() == "alpaca");
        degraded.GetProperty("provider").GetString().Should().Be("alpaca");
        degraded.GetProperty("displayName").GetString().Should().NotBeNullOrWhiteSpace();
        degraded.GetProperty("status").GetString().Should().Be("Degraded");
        degraded.GetProperty("trustScore").GetString().Should().Be("62%");
        degraded.GetProperty("signalSource").GetString().Should().Be("Provider quote/trade stream health telemetry");
        degraded.GetProperty("reasonCode").GetString().Should().Be("PROVIDER_STREAM_DEGRADED");
        degraded.GetProperty("recommendedAction").GetString().Should().Contain("Verify provider connectivity");
        degraded.GetProperty("gateImpact").GetString().Should().Be("Critical");
        degraded.GetProperty("connectionSummary").ValueKind.Should().Be(JsonValueKind.Object);
        degraded.GetProperty("diagnostics").EnumerateArray()
            .Should()
            .Contain(item =>
                item.GetProperty("id").GetString() == "provider-health" &&
                item.GetProperty("statusLabel").GetString() == "Degraded");

        using var data = await ReadJsonAsync(client, "/api/workstation/data");
        data.RootElement.GetProperty("providers").GetArrayLength().Should().Be(2);
    }
}
