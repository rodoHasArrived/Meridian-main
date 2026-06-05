using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

/// <summary>
/// Guards the operator review scenario where multiple workstation contributors assemble one evidence packet before paper operation.
/// </summary>
public sealed class EvidenceWorkflowFabricTests
{
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task EvidenceGraphService_DuringPaperReadinessReview_DeduplicatesNodesAndFlagsInvalidEdges()
    {
        var subject = Subject(EvidenceSubjectResolver.PaperReadinessKind, "current");
        var ready = Node(subject, "ready", "readiness-gate", EvidenceStatusDto.Ready);
        var stale = Node(subject, "stale", "paper-replay", EvidenceStatusDto.Stale, stale: true);
        var review = Node(subject, "review", "provider-trust", EvidenceStatusDto.ReviewRequired, workItemIds: ["provider-trust:sample-review"]);
        var duplicateReady = ready with { Summary = "Duplicate contributor result should not replace the first node." };
        var contributors = new IEvidenceContributor[]
        {
            new StubContributor("readiness", static _ => true, _ => new EvidenceContribution(
                Nodes: [ready, stale],
                Edges:
                [
                    new EvidenceEdgeDto("ready", "stale", "requires", "Replay evidence supports readiness."),
                    new EvidenceEdgeDto("ready", "missing", "requires", "Broken edge should be rejected.")
                ],
                Actions: [],
                RequiredEvidenceIds: ["ready", "stale", "missing"],
                Warnings: [])),
            new StubContributor("provider-trust", static _ => true, _ => new EvidenceContribution(
                Nodes: [duplicateReady, review],
                Edges: [new EvidenceEdgeDto("ready", "review", "requires", "Provider trust supports readiness.")],
                Actions: [],
                RequiredEvidenceIds: ["review"],
                Warnings: ["Optional DK1 sample review is pending."]))
        };
        var service = CreateGraphService(contributors);

        var packet = await service.GetPacketAsync(subject.SubjectKind, subject.SubjectId);

        packet.Should().NotBeNull();
        packet!.Nodes.Should().HaveCount(3);
        packet.Nodes.Single(node => node.EvidenceId == "ready").Summary.Should().Be(ready.Summary);
        packet.Edges.Should().OnlyContain(edge => edge.ToId != "missing");
        packet.Warnings.Should().Contain(warning => warning.Contains("references a missing node", StringComparison.OrdinalIgnoreCase));
        packet.Warnings.Should().Contain("Optional DK1 sample review is pending.");
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.Blocked);
        packet.Completeness.Score.Should().Be(25);
        packet.Completeness.MissingIds.Should().Contain("missing");
        packet.Completeness.StaleIds.Should().Contain("stale");
        packet.Completeness.BlockingWorkItemIds.Should().Contain("provider-trust:sample-review");
        packet.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "invalid-edge" &&
            issue.EvidenceId == "ready");
        packet.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "missing-required-evidence" &&
            issue.EvidenceId == "missing" &&
            issue.Severity == EvidenceValidationSeverityDto.Critical);
        packet.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "stale-required-evidence" &&
            issue.EvidenceId == "stale");
        packet.Completeness.SlaAssessments.Should().Contain(assessment =>
            assessment.PolicyId == "replay-check-freshness" &&
            assessment.EvidenceId == "stale" &&
            assessment.IsBreached);
        packet.Completeness.AssuranceScore.Status.Should().Be(EvidenceStatusDto.Blocked);
        packet.Completeness.AssuranceScore.Components.Should().Contain(component =>
            component.ComponentId == "stale" &&
            component.Status == EvidenceStatusDto.Stale);
        packet.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "review-required-evidence" &&
            issue.EvidenceId == "review" &&
            issue.RelatedWorkItemId == "provider-trust:sample-review");
    }

    [Fact]
    public void EvidencePacketValidationService_DuringGovernedReportReview_ExplainsReadyMissingStaleAndReviewStates()
    {
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "current");
        var ready = Node(subject, "ready", "analysis-export", EvidenceStatusDto.Ready);
        var stale = Node(subject, "stale", "report-pack", EvidenceStatusDto.Stale, stale: true);
        var review = Node(subject, "review", "portfolio-context", EvidenceStatusDto.ReviewRequired, workItemIds: ["report-pack-lineage:current"]);
        var service = new EvidencePacketValidationService();

        var readyResult = service.Validate(
            [ready],
            [],
            new HashSet<string>(["ready"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        readyResult.Completeness.Status.Should().Be(EvidenceStatusDto.Ready);
        readyResult.Completeness.ValidationIssues.Should().BeEmpty();

        var missingResult = service.Validate(
            [ready],
            [],
            new HashSet<string>(["ready", "missing"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        missingResult.Completeness.Status.Should().Be(EvidenceStatusDto.Blocked);
        missingResult.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "missing-required-evidence" &&
            issue.EvidenceId == "missing" &&
            issue.Severity == EvidenceValidationSeverityDto.Critical);

        var staleResult = service.Validate(
            [stale],
            [],
            new HashSet<string>(["stale"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        staleResult.Completeness.Status.Should().Be(EvidenceStatusDto.Stale);
        staleResult.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "stale-required-evidence" &&
            issue.EvidenceId == "stale" &&
            issue.EvidenceKind == "report-pack");

        var reviewResult = service.Validate(
            [review],
            [],
            new HashSet<string>(["review"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        reviewResult.Completeness.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        reviewResult.Completeness.BlockingWorkItemIds.Should().Contain("report-pack-lineage:current");
        reviewResult.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "review-required-evidence" &&
            issue.RelatedWorkItemId == "report-pack-lineage:current");
    }

    [Fact]
    public void EvidencePacketValidationService_DuringAssuranceReview_AppliesEvidenceSlaFreshnessPolicies()
    {
        var subject = Subject(EvidenceSubjectResolver.PaperReadinessKind, "current");
        var provider = Node(subject, "provider", "provider-trust", EvidenceStatusDto.Ready);
        var replay = Node(subject, "replay", "paper-replay", EvidenceStatusDto.Ready);
        var reconciliation = Node(subject, "reconciliation", "reconciliation-run", EvidenceStatusDto.Ready);
        var casework = Node(subject, "casework", "break-queue", EvidenceStatusDto.Ready);
        var approval = Node(subject, "approval", "approval", EvidenceStatusDto.Ready);
        var closeChecklist = Node(subject, "close-checklist", "close-checklist", EvidenceStatusDto.Ready);
        var accountingRecord = Node(subject, "accounting-record", "accounting-record", EvidenceStatusDto.Ready);
        var accountingRecordCategory = Node(subject, "accounting-record-report-pack-lineage", "accounting-record-category", EvidenceStatusDto.Ready) with
        {
            Freshness = new EvidenceFreshnessDto(
                DateTimeOffset.UtcNow.AddDays(-2),
                IsStale: true,
                Reason: "Accounting-record report-pack lineage evidence is outside the close approval freshness window.")
        };
        var report = Node(subject, "report", "report-pack", EvidenceStatusDto.Ready) with
        {
            Freshness = new EvidenceFreshnessDto(
                DateTimeOffset.UtcNow.AddDays(-3),
                IsStale: true,
                Reason: "Report package is outside the governed publication freshness window.")
        };
        var service = new EvidencePacketValidationService();

        var result = service.Validate(
            [provider, replay, reconciliation, casework, approval, closeChecklist, accountingRecord, accountingRecordCategory, report],
            [],
            new HashSet<string>(["provider", "replay", "reconciliation", "casework", "approval", "close-checklist", "accounting-record", "accounting-record-report-pack-lineage", "report"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        var policyIds = result.Completeness.SlaPolicies.Select(policy => policy.PolicyId);
        policyIds.Should().Contain("provider-validation-freshness");
        policyIds.Should().Contain("replay-check-freshness");
        policyIds.Should().Contain("reconciliation-freshness");
        policyIds.Should().Contain("reconciliation-casework-freshness");
        policyIds.Should().Contain("approval-freshness");
        policyIds.Should().Contain("close-checklist-freshness");
        policyIds.Should().Contain("accounting-record-freshness");
        policyIds.Should().Contain("accounting-record-category-freshness");
        policyIds.Should().Contain("report-pack-freshness");
        result.Completeness.SlaAssessments.Should().Contain(assessment =>
            assessment.PolicyId == "reconciliation-casework-freshness" &&
            assessment.EvidenceId == "casework" &&
            !assessment.IsBreached);
        result.Completeness.SlaAssessments.Should().Contain(assessment =>
            assessment.PolicyId == "close-checklist-freshness" &&
            assessment.EvidenceId == "close-checklist" &&
            !assessment.IsBreached);
        result.Completeness.SlaAssessments.Should().Contain(assessment =>
            assessment.PolicyId == "report-pack-freshness" &&
            assessment.EvidenceId == "report" &&
            assessment.IsBreached &&
            assessment.Severity == EvidenceValidationSeverityDto.Warning);
        result.Completeness.SlaAssessments.Should().Contain(assessment =>
            assessment.PolicyId == "accounting-record-freshness" &&
            assessment.EvidenceId == "accounting-record" &&
            !assessment.IsBreached);
        result.Completeness.SlaAssessments.Should().Contain(assessment =>
            assessment.PolicyId == "accounting-record-category-freshness" &&
            assessment.EvidenceId == "accounting-record-report-pack-lineage" &&
            assessment.IsBreached &&
            assessment.Severity == EvidenceValidationSeverityDto.Warning);
        result.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "evidence-sla-breached" &&
            issue.EvidenceId == "report" &&
            issue.EvidenceKind == "report-pack");
        result.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "evidence-sla-breached" &&
            issue.EvidenceId == "accounting-record-report-pack-lineage" &&
            issue.EvidenceKind == "accounting-record-category");
        result.Completeness.AssuranceScore.Status.Should().Be(EvidenceStatusDto.Stale);
        result.Completeness.AssuranceScore.Score.Should().BeLessThan(100);
        result.Completeness.AssuranceScore.Components.Should().Contain(component =>
            component.ComponentId == "report" &&
            component.Status == EvidenceStatusDto.Stale &&
            component.Score == 60);
    }

    [Fact]
    public void EvidencePacketValidationService_DuringLedgerReview_ExplainsLedgerArtifactIntegrityIssues()
    {
        var subject = Subject(EvidenceSubjectResolver.StrategyRunKind, "run-ledger-integrity");
        var ledger = Node(
            subject,
            "ledger",
            "run-ledger",
            EvidenceStatusDto.Ready,
            artifacts:
            [
                new EvidenceArtifactRefDto(
                    "ledger:journal",
                    "ledger-journal",
                    Path: null,
                    Route: null,
                    GeneratedAt: DateTimeOffset.UtcNow,
                    Hash: null,
                    Retained: false)
            ]);
        var service = new EvidencePacketValidationService();

        var result = service.Validate(
            [ledger],
            [],
            new HashSet<string>(["ledger"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        result.Completeness.Status.Should().Be(EvidenceStatusDto.Blocked);
        result.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ledger-missing-trial-balance-artifact" &&
            issue.EvidenceId == "ledger" &&
            issue.Severity == EvidenceValidationSeverityDto.Critical);
        result.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ledger-artifact-not-retained" &&
            issue.EvidenceId == "ledger" &&
            issue.Severity == EvidenceValidationSeverityDto.Warning);
        result.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ledger-artifact-not-addressable" &&
            issue.EvidenceId == "ledger" &&
            issue.Severity == EvidenceValidationSeverityDto.Critical);
    }

    [Fact]
    public void EvidencePacketValidationService_DuringLedgerReview_MarksWarningOnlyRetentionIssuesForReview()
    {
        var subject = Subject(EvidenceSubjectResolver.StrategyRunKind, "run-ledger-retention");
        var generatedAt = DateTimeOffset.UtcNow;
        var ledger = Node(
            subject,
            "ledger",
            "run-ledger",
            EvidenceStatusDto.Ready,
            artifacts:
            [
                new EvidenceArtifactRefDto(
                    "ledger:journal",
                    "ledger-journal",
                    Path: "runs/run-ledger-retention/ledger-journal.json",
                    Route: null,
                    GeneratedAt: generatedAt,
                    Hash: null,
                    Retained: false),
                new EvidenceArtifactRefDto(
                    "ledger:trial-balance",
                    "ledger-trial-balance",
                    Path: "runs/run-ledger-retention/trial-balance.json",
                    Route: null,
                    GeneratedAt: generatedAt,
                    Hash: null,
                    Retained: true,
                    CanonicalSubjectKind: "run",
                    CanonicalSubjectId: "run-ledger-retention")
            ]);
        var service = new EvidencePacketValidationService();

        var result = service.Validate(
            [ledger],
            [],
            new HashSet<string>(["ledger"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        result.Completeness.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        result.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "ledger-artifact-not-retained" &&
            issue.Severity == EvidenceValidationSeverityDto.Warning);
        result.Completeness.ValidationIssues.Should().NotContain(issue =>
            issue.Severity == EvidenceValidationSeverityDto.Critical);
    }

    [Fact]
    public void EvidencePacketValidationService_DetectsOrphansAndCanonicalSubjectLinkageWithoutFalsePositives()
    {
        var subject = Subject(EvidenceSubjectResolver.StrategyRunKind, "run-orphan-check");
        var linkedA = Node(subject, "linked-a", "run-ledger", EvidenceStatusDto.Ready);
        var linkedB = Node(subject, "linked-b", "report-pack", EvidenceStatusDto.Ready);
        var orphan = Node(subject, "orphan", "approval", EvidenceStatusDto.Ready);
        var service = new EvidencePacketValidationService();

        var result = service.Validate(
            [linkedA, linkedB, orphan],
            [new EvidenceEdgeDto("linked-a", "linked-b", "supports", "linked evidence")],
            new HashSet<string>(["linked-a", "linked-b"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: true);

        result.Completeness.OrphanEvidenceIds.Should().Contain("orphan");
        result.Completeness.OrphanEvidenceIds.Should().NotContain("linked-a");
        result.Completeness.WarningIssueCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void EvidencePacketValidationService_DoesNotBlockWhenRetainedArtifactsOmitCanonicalSubject()
    {
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "current");
        var node = Node(
            subject,
            "report",
            "report-pack",
            EvidenceStatusDto.Ready,
            artifacts:
            [
                new EvidenceArtifactRefDto("a1", "report-pack", "/tmp/a1.json", null, DateTimeOffset.UtcNow, null, true)
            ]);
        var service = new EvidencePacketValidationService();

        var result = service.Validate(
            [node],
            [],
            new HashSet<string>(["report"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        result.Completeness.Status.Should().Be(EvidenceStatusDto.Ready);
        result.Completeness.BlockingIssueCount.Should().Be(0);
        result.Completeness.ValidationIssues.Should().NotContain(issue =>
            issue.Code == "retained-artifact-missing-canonical-subject");
    }

    [Fact]
    public async Task EvidenceGraphService_DuringCancelledReview_PreservesCancellation()
    {
        var service = CreateGraphService([new StubContributor("slow", static _ => true, _ => new EvidenceContribution([], [], [], [], []))]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetPacketAsync(EvidenceSubjectResolver.PaperReadinessKind, "current", cts.Token));
    }

    [Fact]
    public async Task EvidenceGraphService_DuringSecurityMasterConflictReview_ProjectsOpenConflictCasework()
    {
        var conflict = new SecurityMasterConflict(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "IdentifierAmbiguity",
            "Identifiers.Cusip",
            "alpaca",
            "security-a",
            "polygon",
            "security-b",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            "Open");
        var service = CreateSecurityMasterConflictGraphService(new StubSecurityMasterConflictService([conflict]));

        var packet = await service.GetPacketAsync(EvidenceSubjectResolver.SecurityMasterConflictKind, "open");

        packet.Should().NotBeNull();
        packet!.Subject.SubjectKind.Should().Be(EvidenceSubjectResolver.SecurityMasterConflictKind);
        packet.Nodes.Should().Contain(node =>
            node.Kind == "security-master-conflict-queue" &&
            node.Status == EvidenceStatusDto.ReviewRequired &&
            node.RelatedWorkItemIds.Contains($"security-master:conflict:{conflict.ConflictId:N}"));
        packet.Nodes.Should().Contain(node =>
            node.Kind == "security-master-conflict" &&
            node.Summary.Contains("Identifiers.Cusip", StringComparison.OrdinalIgnoreCase) &&
            node.RelatedWorkItemIds.Contains($"security-master:conflict:{conflict.ConflictId:N}"));
        packet.Edges.Should().Contain(edge =>
            edge.FromId == "security-master-conflict:open:conflict-queue" &&
            edge.ToId == $"security-master-conflict:open:conflict-{conflict.ConflictId:N}");
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        packet.Completeness.BlockingWorkItemIds.Should().Contain($"security-master:conflict:{conflict.ConflictId:N}");
        packet.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "review-required-evidence" &&
            issue.RelatedWorkItemId == $"security-master:conflict:{conflict.ConflictId:N}");
    }

    [Fact]
    public async Task EvidenceGraphService_DuringSecurityMasterConflictReview_WarnsWhenConflictServiceMissing()
    {
        var service = CreateSecurityMasterConflictGraphService(conflictService: null);

        var packet = await service.GetPacketAsync(EvidenceSubjectResolver.SecurityMasterConflictKind, "open");

        packet.Should().NotBeNull();
        packet!.Nodes.Should().BeEmpty();
        packet.Warnings.Should().Contain(warning =>
            warning.Contains("Security Master conflict service is not registered", StringComparison.OrdinalIgnoreCase));
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.Ready);
    }

    [Fact]
    public async Task EvidenceGraphService_DuringOperationsApprovalReview_ProjectsApprovedWorkflowEvidence()
    {
        var workflow = CreateOperationsWorkflow(OperationsApprovalStateDto.Approved);
        var service = CreateOperationsApprovalGraphService(new StubOperationsContinuityWorkflowService([workflow]));

        var packet = await service.GetPacketAsync(EvidenceSubjectResolver.ApprovalKind, workflow.WorkflowId.ToString("D"));

        packet.Should().NotBeNull();
        packet!.Subject.SubjectKind.Should().Be(EvidenceSubjectResolver.ApprovalKind);
        packet.Nodes.Should().Contain(node =>
            node.Kind == "approval" &&
            node.Status == EvidenceStatusDto.Ready &&
            node.Summary.Contains(nameof(OperationsApprovalStateDto.Approved), StringComparison.OrdinalIgnoreCase));
        packet.Nodes.Should().Contain(node =>
            node.Kind == "approval-audit" &&
            node.Status == EvidenceStatusDto.Ready);
        packet.Nodes.Should().Contain(node =>
            node.Kind == "report-pack" &&
            node.Status == EvidenceStatusDto.Ready);
        packet.Nodes.Should().Contain(node =>
            node.Kind == "accounting-record" &&
            node.Status == EvidenceStatusDto.Ready);
        packet.Nodes.Should().Contain(node =>
            node.Kind == "accounting-record-category" &&
            node.Summary.Contains("export manifest", StringComparison.OrdinalIgnoreCase) &&
            node.Summary.Contains("restatement lineage", StringComparison.OrdinalIgnoreCase));
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.Ready);
        packet.Completeness.BlockingWorkItemIds.Should().BeEmpty();
    }

    [Fact]
    public async Task EvidenceGraphService_DuringAccountingRecordReview_ProjectsAccountingRecordAsFirstClassSubject()
    {
        var workflow = CreateOperationsWorkflow(OperationsApprovalStateDto.Approved);
        var service = CreateOperationsApprovalGraphService(new StubOperationsContinuityWorkflowService([workflow]));

        var packet = await service.GetPacketAsync(EvidenceSubjectResolver.AccountingRecordKind, workflow.WorkflowId.ToString("D"));

        packet.Should().NotBeNull();
        packet!.Subject.SubjectKind.Should().Be(EvidenceSubjectResolver.AccountingRecordKind);
        packet.Subject.Label.Should().Contain("Accounting record");
        packet.Nodes.Should().Contain(node =>
            node.Kind == "accounting-record" &&
            node.Status == EvidenceStatusDto.Ready);
        packet.Nodes.Should().Contain(node =>
            node.Kind == "accounting-record-category" &&
            node.Summary.Contains("export manifest", StringComparison.OrdinalIgnoreCase) &&
            node.Summary.Contains("document attachment", StringComparison.OrdinalIgnoreCase));
        packet.Completeness.SlaPolicies.Select(policy => policy.PolicyId).Should().Contain(
        [
            "accounting-record-freshness",
            "accounting-record-category-freshness"
        ]);
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.Ready);
    }

    [Fact]
    public async Task EvidenceSubjectResolver_DuringOperationsEvidenceReview_ListsAccountingRecordSubjects()
    {
        var workflow = CreateOperationsWorkflow(OperationsApprovalStateDto.Approved);
        var services = new ServiceCollection()
            .AddSingleton<IOperationsContinuityWorkflowService>(new StubOperationsContinuityWorkflowService([workflow]))
            .BuildServiceProvider();
        var resolver = new EvidenceSubjectResolver(services);

        var subjects = await resolver.ListAsync();

        subjects.Should().Contain(subject =>
            subject.SubjectKind == EvidenceSubjectResolver.AccountingRecordKind &&
            subject.SubjectId == "current" &&
            subject.Label == "Current accounting record");
        subjects.Should().Contain(subject =>
            subject.SubjectKind == EvidenceSubjectResolver.AccountingRecordKind &&
            subject.SubjectId == workflow.WorkflowId.ToString("D") &&
            subject.Label.Contains(workflow.PeriodId, StringComparison.OrdinalIgnoreCase));
        resolver.IsSupportedKind(EvidenceSubjectResolver.AccountingRecordKind).Should().BeTrue();
    }

    [Fact]
    public async Task EvidenceGraphService_DuringOperationsApprovalReview_FlagsIncompleteAccountingRecordEvidence()
    {
        var workflow = CreateOperationsWorkflow(
            OperationsApprovalStateDto.Approved,
            accountingRecordAuditReady: false);
        var service = CreateOperationsApprovalGraphService(new StubOperationsContinuityWorkflowService([workflow]));

        var packet = await service.GetPacketAsync(EvidenceSubjectResolver.ApprovalKind, workflow.WorkflowId.ToString("D"));

        packet.Should().NotBeNull();
        packet!.Nodes.Should().Contain(node =>
            node.Kind == "accounting-record" &&
            node.Status == EvidenceStatusDto.ReviewRequired &&
            node.RelatedWorkItemIds.Contains($"operations-accounting-record:review:{workflow.WorkflowId:N}"));
        packet.Nodes.Should().Contain(node =>
            node.Kind == "accounting-record-category" &&
            node.Status == EvidenceStatusDto.ReviewRequired &&
            node.RelatedWorkItemIds.Contains($"operations-accounting-record:exports:{workflow.WorkflowId:N}"));
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
        packet.Completeness.BlockingWorkItemIds.Should().Contain(
        [
            $"operations-accounting-record:review:{workflow.WorkflowId:N}",
            $"operations-accounting-record:exports:{workflow.WorkflowId:N}"
        ]);
        packet.Completeness.ValidationIssues.Should().Contain(issue =>
            issue.Code == "review-required-evidence" &&
            issue.EvidenceKind == "accounting-record-category");
    }

    [Fact]
    public async Task EvidenceGraphService_DuringOperationsApprovalReview_FlagsRejectedWorkflowAsBlocked()
    {
        var workflow = CreateOperationsWorkflow(OperationsApprovalStateDto.Rejected);
        var service = CreateOperationsApprovalGraphService(new StubOperationsContinuityWorkflowService([workflow]));

        var packet = await service.GetPacketAsync(EvidenceSubjectResolver.ApprovalKind, workflow.WorkflowId.ToString("D"));

        packet.Should().NotBeNull();
        packet!.Nodes.Should().Contain(node =>
            node.Kind == "approval" &&
            node.Status == EvidenceStatusDto.Blocked &&
            node.RelatedWorkItemIds.Contains($"operations-approval:rejected:{workflow.WorkflowId:N}"));
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.Blocked);
        packet.Completeness.BlockingWorkItemIds.Should().Contain($"operations-approval:rejected:{workflow.WorkflowId:N}");
    }

    [Fact]
    public async Task EvidenceGraphService_DuringOperationsApprovalReview_UsesLatestWorkflowForCurrentSubject()
    {
        var older = CreateOperationsWorkflow(OperationsApprovalStateDto.Pending, updatedAtUtc: DateTimeOffset.UtcNow.AddHours(-2));
        var latest = CreateOperationsWorkflow(OperationsApprovalStateDto.Submitted, updatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5));
        var service = CreateOperationsApprovalGraphService(new StubOperationsContinuityWorkflowService([older, latest]));

        var packet = await service.GetPacketAsync(EvidenceSubjectResolver.ApprovalKind, "current");

        packet.Should().NotBeNull();
        packet!.Nodes.Should().Contain(node =>
            node.Kind == "approval" &&
            node.Summary.Contains(latest.WorkflowId.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
            node.RelatedWorkItemIds.Contains($"operations-approval:review:{latest.WorkflowId:N}"));
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
    }

    [Fact]
    public async Task EvidenceGraphService_DuringOperationsApprovalReview_WarnsWhenWorkflowServiceMissing()
    {
        var service = CreateOperationsApprovalGraphService(workflowService: null);

        var packet = await service.GetPacketAsync(EvidenceSubjectResolver.ApprovalKind, "current");

        packet.Should().NotBeNull();
        packet!.Nodes.Should().BeEmpty();
        packet.Warnings.Should().Contain(warning =>
            warning.Contains("Operations Continuity workflow service is not registered", StringComparison.OrdinalIgnoreCase));
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.Ready);
    }

    [Fact]
    public async Task ReportPackEvidenceContributor_DuringReportPackReview_UsesNeutralSourceSystemLabel()
    {
        var reportId = Guid.NewGuid();
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, reportId.ToString("D"));
        using var provider = new ServiceCollection()
            .AddSingleton<IGovernanceReportPackRepository>(new InMemoryReportPackRepository(BuildReportPackSnapshot(reportId)))
            .BuildServiceProvider();
        var contributor = new ReportPackEvidenceContributor(provider);

        var contribution = await contributor.ContributeAsync(new EvidenceContributionContext(subject, CancellationToken.None));

        contribution.Nodes.Should().ContainSingle(node =>
            node.Kind == "report-pack" &&
            node.SourceSystem == "report-pack-repository");
        contribution.Nodes.Should().NotContain(node =>
            node.SourceSystem.Contains("Governance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MapEvidenceEndpoints_DuringReportPackReview_ReturnsPacketGraphValidationTemplatesAndManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-workflow", Guid.NewGuid().ToString("N"));
        await using var app = await CreateEvidenceAppAsync(root);
        var client = app.GetTestClient();

        var templatesResponse = await client.GetAsync("/api/workstation/evidence/templates");
        templatesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var templates = await templatesResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceTemplateDto>>(ServerJsonOptions);
        templates.Should().NotBeNull();
        templates!.Should().Contain(template =>
            template.WorkflowId == "portfolio-reporting-output" &&
            template.ExportSettings.ManifestOnly &&
            template.ExportSettings.SchemaVersion == 1);

        var packetResponse = await client.GetAsync("/api/workstation/evidence/subjects/report-pack/current/packet");
        packetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var packet = await packetResponse.Content.ReadFromJsonAsync<EvidencePacketDto>(ServerJsonOptions);
        packet.Should().NotBeNull();
        packet!.Subject.SubjectKind.Should().Be(EvidenceSubjectResolver.ReportPackKind);
        packet.Nodes.Should().Contain(node => node.Kind == "analysis-export");
        packet.Warnings.Should().Contain(warning => warning.Contains("report-pack repository is not registered", StringComparison.OrdinalIgnoreCase));
        packet.Warnings.Should().NotContain(warning => warning.Contains("Governance report-pack repository", StringComparison.OrdinalIgnoreCase));

        var graphResponse = await client.GetAsync("/api/workstation/evidence/subjects/report-pack/current/graph");
        graphResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var graph = await graphResponse.Content.ReadFromJsonAsync<EvidenceGraphDto>(ServerJsonOptions);
        graph!.Nodes.Should().Contain(node => node.EvidenceId == "report-pack:current:analysis-export");

        var validationResponse = await client.PostAsync("/api/workstation/evidence/subjects/report-pack/current/validate", content: null);
        validationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var completeness = await validationResponse.Content.ReadFromJsonAsync<EvidenceCompletenessDto>(ServerJsonOptions);
        completeness!.ReadyIds.Should().Contain("report-pack:current:analysis-export");

        var exportResponse = await client.PostAsJsonAsync(
            "/api/workstation/evidence/subjects/report-pack/current/export-manifest",
            new EvidencePacketExportRequest("operator", "report-pack review", IncludeWarnings: false),
            ServerJsonOptions);
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var export = await exportResponse.Content.ReadFromJsonAsync<EvidencePacketExportResponse>(ServerJsonOptions);
        export.Should().NotBeNull();
        export!.Retained.Should().BeTrue();
        export.VaultIdentity.Should().NotBeNull();
        export.VaultIdentity!.VaultId.Should().StartWith("ev-");
        export.VaultIdentity.ManifestPath.Should().Be(export.ManifestPath);
        export.VaultIdentity.ManifestRoute.Should().Be(export.ManifestRoute);
        export.VaultIdentity.ContentHashSha256.Should().HaveLength(64);
        export.VaultIdentity.StorageKind.Should().Be("file-manifest");
        export.WarningCount.Should().Be(0);
        File.Exists(Path.Combine(root, export.ManifestPath.Replace('/', Path.DirectorySeparatorChar))).Should().BeTrue();
        var indexPath = Path.Combine(root, "workstation", "evidence", "_vault", $"{export.VaultIdentity.VaultId}.json");
        File.Exists(indexPath).Should().BeTrue();
        var indexJson = await File.ReadAllTextAsync(indexPath);
        var indexedIdentity = JsonSerializer.Deserialize<EvidenceVaultIdentityDto>(indexJson, ServerJsonOptions);
        indexedIdentity.Should().BeEquivalentTo(export.VaultIdentity);

        var manifestResponse = await client.GetAsync(export.ManifestRoute);
        manifestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        manifestResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        var manifestJson = await manifestResponse.Content.ReadAsStringAsync();
        manifestJson.Should().Contain("\"manifestOnly\": true");
        manifestJson.Should().Contain("\"requestedBy\": \"operator\"");
        manifestJson.Should().Contain("\"vaultIdentity\": {");
        manifestJson.Should().Contain(export.VaultIdentity.VaultId);

        var vaultResponse = await client.GetAsync($"/workstation/evidence/vault/{export.VaultIdentity.VaultId}");
        vaultResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        vaultResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var missingVaultResponse = await client.GetAsync("/workstation/evidence/vault/ev-000000000000000000000000");
        missingVaultResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var missingVaultError = await missingVaultResponse.Content.ReadFromJsonAsync<EvidenceEndpointErrorDto>(ServerJsonOptions);
        missingVaultError!.Code.Should().Be("evidence-vault-manifest-not-found");
        missingVaultError.VaultId.Should().Be("ev-000000000000000000000000");

        var traversalResponse = await client.GetAsync("/workstation/evidence/report-pack/current/..%2Fsecret-manifest.json");
        traversalResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var traversalError = await traversalResponse.Content.ReadFromJsonAsync<EvidenceEndpointErrorDto>(ServerJsonOptions);
        traversalError!.Code.Should().Be("evidence-manifest-not-found");

        var malformedExportResponse = await client.PostAsync(
            "/api/workstation/evidence/subjects/report-pack/current/export-manifest",
            new StringContent("{", Encoding.UTF8, "application/json"));
        malformedExportResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var malformedExportError = await malformedExportResponse.Content.ReadFromJsonAsync<EvidenceEndpointErrorDto>(ServerJsonOptions);
        malformedExportError!.Code.Should().Be("invalid-evidence-export-request");
        malformedExportError.SubjectKind.Should().Be(EvidenceSubjectResolver.ReportPackKind);
    }

    [Fact]
    public async Task MapEvidenceEndpoints_DuringAccountingRecordReview_ReturnsPacketValidationAndManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-accounting-record", Guid.NewGuid().ToString("N"));
        var workflow = CreateOperationsWorkflow(OperationsApprovalStateDto.Approved);
        await using var app = await CreateEvidenceAppAsync(
            root,
            new StubOperationsContinuityWorkflowService([workflow]));
        var client = app.GetTestClient();
        var subjectId = workflow.WorkflowId.ToString("D");

        var subjectsResponse = await client.GetAsync("/api/workstation/evidence/subjects");
        subjectsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var subjects = await subjectsResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceSubjectDto>>(ServerJsonOptions);
        subjects.Should().Contain(subject =>
            subject.SubjectKind == EvidenceSubjectResolver.AccountingRecordKind &&
            subject.SubjectId == subjectId);

        var packetResponse = await client.GetAsync($"/api/workstation/evidence/subjects/accounting-record/{subjectId}/packet");
        packetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var packet = await packetResponse.Content.ReadFromJsonAsync<EvidencePacketDto>(ServerJsonOptions);
        packet.Should().NotBeNull();
        packet!.Subject.SubjectKind.Should().Be(EvidenceSubjectResolver.AccountingRecordKind);
        packet.Nodes.Should().Contain(node => node.Kind == "accounting-record");
        packet.Nodes.Should().Contain(node =>
            node.Kind == "accounting-record-category" &&
            node.Summary.Contains("restatement lineage", StringComparison.OrdinalIgnoreCase));
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.Ready);

        var validationResponse = await client.PostAsync(
            $"/api/workstation/evidence/subjects/accounting-record/{subjectId}/validate",
            content: null);
        validationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var completeness = await validationResponse.Content.ReadFromJsonAsync<EvidenceCompletenessDto>(ServerJsonOptions);
        completeness!.SlaPolicies.Select(policy => policy.PolicyId).Should().Contain(
        [
            "accounting-record-freshness",
            "accounting-record-category-freshness"
        ]);

        var exportResponse = await client.PostAsJsonAsync(
            $"/api/workstation/evidence/subjects/accounting-record/{subjectId}/export-manifest",
            new EvidencePacketExportRequest("controller", "accounting record retention", IncludeWarnings: false),
            ServerJsonOptions);
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var export = await exportResponse.Content.ReadFromJsonAsync<EvidencePacketExportResponse>(ServerJsonOptions);
        export.Should().NotBeNull();
        export!.VaultIdentity.Should().NotBeNull();
        export.VaultIdentity!.SubjectKind.Should().Be(EvidenceSubjectResolver.AccountingRecordKind);
        export.VaultIdentity.SubjectId.Should().Be(subjectId);

        var manifestJson = await client.GetStringAsync(export.ManifestRoute);
        manifestJson.Should().Contain("\"subjectKind\": \"accounting-record\"");
        manifestJson.Should().Contain("\"requestedBy\": \"controller\"");
        manifestJson.Should().Contain($"\"evidenceSubject\": \"{EvidenceSubjectResolver.AccountingRecordKind}/{subjectId}\"");
        manifestJson.Should().Contain($"\"accountingRecordId\": \"{subjectId}\"");

        var vaultSearchResponse = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/search",
            new EvidenceVaultLookupRequestDto(
                $"{EvidenceSubjectResolver.AccountingRecordKind}/{subjectId}",
                null,
                null,
                null,
                null),
            ServerJsonOptions);
        vaultSearchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var vaultMatches = await vaultSearchResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceVaultIdentityDto>>(ServerJsonOptions);
        vaultMatches.Should().ContainSingle(match =>
            match.SubjectKind == EvidenceSubjectResolver.AccountingRecordKind &&
            match.SubjectId == subjectId &&
            match.VaultId == export.VaultIdentity.VaultId);

        var accountingRecordSearchResponse = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/search",
            new EvidenceVaultLookupRequestDto(null, null, null, null, null, AccountingRecordId: subjectId),
            ServerJsonOptions);
        accountingRecordSearchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var accountingRecordMatches = await accountingRecordSearchResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceVaultIdentityDto>>(ServerJsonOptions);
        accountingRecordMatches.Should().ContainSingle(match =>
            match.SubjectKind == EvidenceSubjectResolver.AccountingRecordKind &&
            match.SubjectId == subjectId &&
            match.VaultId == export.VaultIdentity.VaultId);
    }

    [Fact]
    public async Task MapEvidenceEndpoints_DuringUnsupportedSubjectReview_ReturnsBadRequest()
    {
        await using var app = await CreateEvidenceAppAsync(Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-workflow", Guid.NewGuid().ToString("N")));
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/workstation/evidence/subjects/unknown/current/packet");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<EvidenceEndpointErrorDto>(ServerJsonOptions);
        error!.Code.Should().Be("unsupported-evidence-subject-kind");
        error.SubjectKind.Should().Be("unknown");
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringManifestExport_SanitizesSubjectPathAndHonorsWarningPreference()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject("report-pack", "Review Jan/../2026");
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [Node(subject, "node-1", "analysis-export", EvidenceStatusDto.Ready)],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["node-1"], ["node-1"], [], [], [])
            {
                ValidationIssues =
                [
                    new EvidenceValidationIssueDto(
                        Code: "review-required-evidence",
                        Severity: EvidenceValidationSeverityDto.Warning,
                        Message: "Report-pack approval evidence requires review.",
                        EvidenceId: "node-1",
                        EvidenceKind: "analysis-export",
                        SourceSystem: "test")
                ]
            },
            Actions: [],
            Warnings: ["This warning should be excluded."]);

        var response = await store.WriteManifestAsync(packet, new EvidencePacketExportRequest("operator", "safe export", IncludeWarnings: false));
        var manifestPath = Path.Combine(root, response.ManifestPath.Replace('/', Path.DirectorySeparatorChar));
        var manifestJson = await File.ReadAllTextAsync(manifestPath);

        response.ManifestPath.Should().Contain("review jan-..-2026");
        response.ManifestRoute.Should().Contain("/workstation/evidence/report-pack/review%20jan-..-2026/");
        response.VaultIdentity.Should().NotBeNull();
        response.VaultIdentity!.SubjectId.Should().Be("Review Jan/../2026");
        response.WarningCount.Should().Be(0);
        var retainedManifest = await store.TryOpenManifestByVaultIdAsync(response.VaultIdentity.VaultId);
        retainedManifest.Should().NotBeNull();
        using (var reader = new StreamReader(retainedManifest!.Content))
        {
            var retainedJson = await reader.ReadToEndAsync();
            retainedJson.Should().Contain("\"subjectId\": \"Review Jan/../2026\"");
            retainedJson.Should().Contain(response.VaultIdentity.VaultId);
        }

        manifestJson.Should().Contain("\"schemaVersion\": 1");
        manifestJson.Should().Contain("\"validationIssues\": [");
        manifestJson.Should().Contain("\"vaultIdentity\": {");
        manifestJson.Should().Contain("\"code\": \"review-required-evidence\"");
        manifestJson.Should().NotContain("This warning should be excluded.");
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringManifestRead_RejectsDotSegmentSubjectTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject("report-pack", "current");
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [Node(subject, "node-1", "analysis-export", EvidenceStatusDto.Ready)],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["node-1"], ["node-1"], [], [], []),
            Actions: [],
            Warnings: []);

        var response = await store.WriteManifestAsync(packet, new EvidencePacketExportRequest("operator", "safe export"));
        var generatedFileName = Uri.UnescapeDataString(response.ManifestRoute.Split('/')[^1]);
        var validManifest = await store.TryOpenManifestAsync("report-pack", "current", generatedFileName);
        validManifest.Should().NotBeNull();
        await validManifest!.Content.DisposeAsync();

        var evidenceRoot = Path.Combine(root, "workstation", "evidence");
        Directory.CreateDirectory(evidenceRoot);
        var escapedManifestPath = Path.Combine(evidenceRoot, "secret-manifest.json");
        await File.WriteAllTextAsync(escapedManifestPath, """{"schemaVersion":1}""");

        var subjectIdTraversal = await store.TryOpenManifestAsync("report-pack", "..", "secret-manifest.json");
        var subjectKindTraversal = await store.TryOpenManifestAsync("..", "report-pack", "secret-manifest.json");
        var encodedSeparatorTraversal = await store.TryOpenManifestAsync("report-pack", "current/..", "secret-manifest.json");

        subjectIdTraversal.Should().BeNull();
        subjectKindTraversal.Should().BeNull();
        encodedSeparatorTraversal.Should().BeNull();
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringLedgerManifestExport_PreservesRouteOnlyArtifactRefs()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject(EvidenceSubjectResolver.StrategyRunKind, "run-ledger-proof");
        var generatedAt = new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);
        var artifacts = new[]
        {
            new EvidenceArtifactRefDto(
                "strategy-run:run-ledger-proof:ledger:journal",
                "ledger-journal",
                Path: null,
                Route: "/api/workstation/runs/run-ledger-proof/ledger/journal",
                GeneratedAt: generatedAt,
                Hash: null,
                Retained: true,
                CanonicalSubjectKind: EvidenceSubjectResolver.StrategyRunKind,
                CanonicalSubjectId: "run-ledger-proof"),
            new EvidenceArtifactRefDto(
                "strategy-run:run-ledger-proof:ledger:trial-balance",
                "ledger-trial-balance",
                Path: null,
                Route: "/api/workstation/runs/run-ledger-proof/ledger/trial-balance",
                GeneratedAt: generatedAt,
                Hash: null,
                Retained: true,
                CanonicalSubjectKind: EvidenceSubjectResolver.StrategyRunKind,
                CanonicalSubjectId: "run-ledger-proof")
        };
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: generatedAt,
            Nodes: [Node(subject, "strategy-run:run-ledger-proof:ledger", "run-ledger", EvidenceStatusDto.Ready, artifacts: artifacts)],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["strategy-run:run-ledger-proof:ledger"], ["strategy-run:run-ledger-proof:ledger"], [], [], []),
            Actions: [],
            Warnings: []);

        var response = await store.WriteManifestAsync(packet, new EvidencePacketExportRequest("operator", "ledger proof export"));
        var manifestPath = Path.Combine(root, response.ManifestPath.Replace('/', Path.DirectorySeparatorChar));
        await using var stream = File.OpenRead(manifestPath);
        using var manifest = await JsonDocument.ParseAsync(stream);
        var ledgerNode = manifest.RootElement.GetProperty("nodes")
            .EnumerateArray()
            .Single(node => node.GetProperty("kind").GetString() == "run-ledger");
        var artifactRefs = ledgerNode.GetProperty("artifactRefs")
            .EnumerateArray()
            .ToArray();

        artifactRefs.Should().HaveCount(2);
        artifactRefs.Should().Contain(artifact =>
            IsRouteOnlyArtifact(artifact, "ledger-journal", "/api/workstation/runs/run-ledger-proof/ledger/journal"));
        artifactRefs.Should().Contain(artifact =>
            IsRouteOnlyArtifact(artifact, "ledger-trial-balance", "/api/workstation/runs/run-ledger-proof/ledger/trial-balance"));
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringManifestExport_RetainsLocalArtifactPayloads()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(root, "source-artifacts");
        Directory.CreateDirectory(sourceDirectory);
        var statementPath = Path.Combine(sourceDirectory, "broker-statement.csv");
        var statementBytes = Encoding.UTF8.GetBytes("account,symbol,quantity,price,cashAmount,activityType,tradeDate\nA1,AAPL,1,190,0,position,2026-05-28\n");
        await File.WriteAllBytesAsync(statementPath, statementBytes);
        var statementHash = Convert.ToHexString(SHA256.HashData(statementBytes)).ToLowerInvariant();
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "close-2026-05");
        var artifact = new EvidenceArtifactRefDto(
            "statement-artifact-1",
            "broker-statement",
            statementPath,
            "/api/workstation/reconciliation/statement-runs/import-1",
            DateTimeOffset.UtcNow,
            statementHash,
            Retained: true,
            CanonicalSubjectKind: EvidenceSubjectResolver.ReportPackKind,
            CanonicalSubjectId: "close-2026-05");
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [Node(subject, "statement-node", "broker-statement", EvidenceStatusDto.Ready, artifacts: [artifact])],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["statement-node"], ["statement-node"], [], [], []),
            Actions: [],
            Warnings: []);

        var response = await store.WriteManifestAsync(packet, new EvidencePacketExportRequest("operator", "statement retention"));

        response.VaultIdentity.Should().NotBeNull();
        response.VaultIdentity!.StorageKind.Should().Be("file-bundle");
        var retained = response.VaultIdentity.Artifacts.Should().ContainSingle().Which;
        retained.ArtifactId.Should().Be("statement-artifact-1");
        retained.Kind.Should().Be("broker-statement");
        retained.ContentHashSha256.Should().Be(statementHash);
        retained.SizeBytes.Should().Be(statementBytes.LongLength);
        retained.SourceRoute.Should().Be("/api/workstation/reconciliation/statement-runs/import-1");
        retained.CanonicalSubjectKind.Should().Be(EvidenceSubjectResolver.ReportPackKind);
        retained.CanonicalSubjectId.Should().Be("close-2026-05");
        var retainedPath = Path.Combine(root, retained.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(retainedPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(retainedPath)).Should().Equal(statementBytes);

        var manifestPath = Path.Combine(root, response.ManifestPath.Replace('/', Path.DirectorySeparatorChar));
        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        manifestJson.Should().Contain("\"storageKind\": \"file-bundle\"");
        manifestJson.Should().Contain("\"artifacts\": [");
        manifestJson.Should().Contain("\"relativePath\": \"workstation/evidence/_vault/");
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringManifestExport_RetainsScreenshotArtifactPayloads()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(root, "source-artifacts");
        Directory.CreateDirectory(sourceDirectory);
        var screenshotPath = Path.Combine(sourceDirectory, "close-checklist.png");
        var screenshotBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p94AAAAASUVORK5CYII=");
        await File.WriteAllBytesAsync(screenshotPath, screenshotBytes);
        var screenshotHash = Convert.ToHexString(SHA256.HashData(screenshotBytes)).ToLowerInvariant();
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "close-2026-05");
        var artifact = new EvidenceArtifactRefDto(
            "close-checklist-screenshot",
            "screenshot",
            screenshotPath,
            "/workstation/accounting/operations-continuity/workflow-1#close-checklist",
            DateTimeOffset.UtcNow,
            screenshotHash,
            Retained: true,
            CanonicalSubjectKind: EvidenceSubjectResolver.ReportPackKind,
            CanonicalSubjectId: "close-2026-05");
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [Node(subject, "close-checklist-screenshot", "screenshot", EvidenceStatusDto.Ready, artifacts: [artifact])],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["close-checklist-screenshot"], ["close-checklist-screenshot"], [], [], []),
            Actions: [],
            Warnings: []);

        var response = await store.WriteManifestAsync(packet, new EvidencePacketExportRequest("operator", "close checklist screenshot retention"));

        response.VaultIdentity.Should().NotBeNull();
        response.VaultIdentity!.StorageKind.Should().Be("file-bundle");
        var retained = response.VaultIdentity.Artifacts.Should().ContainSingle().Which;
        retained.ArtifactId.Should().Be("close-checklist-screenshot");
        retained.Kind.Should().Be("screenshot");
        retained.ContentHashSha256.Should().Be(screenshotHash);
        retained.SourceRoute.Should().Be("/workstation/accounting/operations-continuity/workflow-1#close-checklist");
        retained.CanonicalSubjectKind.Should().Be(EvidenceSubjectResolver.ReportPackKind);
        retained.CanonicalSubjectId.Should().Be("close-2026-05");
        retained.RelativePath.Should().EndWith(".png");
        var retainedPath = Path.Combine(root, retained.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(retainedPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(retainedPath)).Should().Equal(screenshotBytes);
    }

    [Fact]
    public async Task EvidenceGraphService_DuringVaultArtifactReview_ProjectsRetainedManifestAndArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-vault-workbench", Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(root, "source-artifacts");
        Directory.CreateDirectory(sourceDirectory);
        var statementPath = Path.Combine(sourceDirectory, "custodian-statement.csv");
        var statementBytes = Encoding.UTF8.GetBytes("account,symbol,quantity,price\nA1,MSFT,2,410\n");
        await File.WriteAllBytesAsync(statementPath, statementBytes);
        var statementHash = Convert.ToHexString(SHA256.HashData(statementBytes)).ToLowerInvariant();
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "close-2026-05");
        var retainedArtifact = new EvidenceArtifactRefDto(
            "custodian-statement-1",
            "custodian-statement",
            statementPath,
            "/api/workstation/reconciliation/statement-runs/stmt-1",
            DateTimeOffset.UtcNow,
            statementHash,
            Retained: true,
            CanonicalSubjectKind: "reconciliation-case",
            CanonicalSubjectId: "case-123");
        var sourcePacket = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [Node(subject, "statement-node", "custodian-statement", EvidenceStatusDto.Ready, artifacts: [retainedArtifact])],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["statement-node"], ["statement-node"], [], [], []),
            Actions: [],
            Warnings: []);
        var exported = await store.WriteManifestAsync(sourcePacket, new EvidencePacketExportRequest("operator", "vault workbench coverage"));
        var services = new ServiceCollection()
            .AddSingleton<IEvidenceArtifactStore>(store)
            .BuildServiceProvider();
        var graph = new EvidenceGraphService(
            new EvidenceSubjectResolver(services),
            new EvidenceTemplateRegistry(),
            [new EvidenceVaultEvidenceContributor(services)],
            NullLogger<EvidenceGraphService>.Instance);

        var packet = await graph.GetPacketAsync(EvidenceSubjectResolver.EvidenceVaultKind, exported.VaultIdentity!.VaultId);

        packet.Should().NotBeNull();
        packet!.Subject.SubjectKind.Should().Be(EvidenceSubjectResolver.EvidenceVaultKind);
        packet.Nodes.Should().Contain(node =>
            node.Kind == "evidence-vault-manifest" &&
            node.Summary.Contains(exported.VaultIdentity.VaultId, StringComparison.Ordinal));
        var artifactNode = packet.Nodes.Should().ContainSingle(node => node.Kind == "retained-vault-artifact").Which;
        artifactNode.Status.Should().Be(EvidenceStatusDto.Ready);
        artifactNode.Summary.Should().Contain("custodian-statement-1");
        artifactNode.ArtifactRefs.Should().ContainSingle(artifact =>
            artifact.ArtifactId.EndsWith(":retained-payload", StringComparison.Ordinal) &&
            artifact.Kind == "custodian-statement" &&
            artifact.Hash == statementHash &&
            artifact.CanonicalSubjectKind == "reconciliation-case" &&
            artifact.CanonicalSubjectId == "case-123");
        packet.Edges.Should().Contain(edge =>
            edge.Relationship == "retains" &&
            edge.ToId == artifactNode.EvidenceId);
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.Ready);
        packet.Completeness.OrphanEvidenceIds.Should().BeEmpty();
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringManifestExport_RejectsMissingRetainedArtifactPayloads()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "close-2026-05");
        var artifact = new EvidenceArtifactRefDto(
            "statement-artifact-missing",
            "broker-statement",
            Path.Combine(root, "missing.csv"),
            null,
            DateTimeOffset.UtcNow,
            null,
            Retained: true,
            CanonicalSubjectKind: EvidenceSubjectResolver.ReportPackKind,
            CanonicalSubjectId: "close-2026-05");
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [Node(subject, "statement-node", "broker-statement", EvidenceStatusDto.Ready, artifacts: [artifact])],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["statement-node"], ["statement-node"], [], [], []),
            Actions: [],
            Warnings: []);

        var act = () => store.WriteManifestAsync(packet, new EvidencePacketExportRequest("operator", "statement retention"));

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*statement-artifact-missing*source file was not found*");
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringManifestExport_RejectsRetainedArtifactWithoutCanonicalSubject()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(root, "source-artifacts");
        Directory.CreateDirectory(sourceDirectory);
        var statementPath = Path.Combine(sourceDirectory, "broker-statement.csv");
        await File.WriteAllTextAsync(statementPath, "account,symbol,quantity\nA1,AAPL,1\n");
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "close-2026-05");
        var artifact = new EvidenceArtifactRefDto(
            "statement-artifact-orphan",
            "broker-statement",
            statementPath,
            "/api/workstation/reconciliation/statement-runs/import-1",
            DateTimeOffset.UtcNow,
            null,
            Retained: true);
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [Node(subject, "statement-node", "broker-statement", EvidenceStatusDto.Ready, artifacts: [artifact])],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["statement-node"], ["statement-node"], [], [], []),
            Actions: [],
            Warnings: []);

        var act = () => store.WriteManifestAsync(packet, new EvidencePacketExportRequest("operator", "statement retention"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*statement-artifact-orphan*missing canonical subject linkage*");
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringManifestExport_RejectsUnsupportedCanonicalSubjectKind()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(root, "source-artifacts");
        Directory.CreateDirectory(sourceDirectory);
        var statementPath = Path.Combine(sourceDirectory, "broker-statement.csv");
        await File.WriteAllTextAsync(statementPath, "account,symbol,quantity\nA1,AAPL,1\n");
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "close-2026-05");
        var artifact = new EvidenceArtifactRefDto(
            "statement-artifact-unsupported-subject",
            "broker-statement",
            statementPath,
            "/api/workstation/reconciliation/statement-runs/import-1",
            DateTimeOffset.UtcNow,
            null,
            Retained: true,
            CanonicalSubjectKind: "scratchpad",
            CanonicalSubjectId: "close-2026-05");
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [Node(subject, "statement-node", "broker-statement", EvidenceStatusDto.Ready, artifacts: [artifact])],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["statement-node"], ["statement-node"], [], [], []),
            Actions: [],
            Warnings: []);

        var act = () => store.WriteManifestAsync(packet, new EvidencePacketExportRequest("operator", "statement retention"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*statement-artifact-unsupported-subject*unsupported canonical subject kind 'scratchpad'*");
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringManifestExport_RejectsRouteOnlyRetainedArtifactWithoutCanonicalSubject()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "close-2026-05");
        var artifact = new EvidenceArtifactRefDto(
            "approval-route-only",
            "approval",
            Path: null,
            Route: "/api/workstation/operations-continuity/workflows/workflow-1/approval",
            GeneratedAt: DateTimeOffset.UtcNow,
            Hash: null,
            Retained: true);
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [Node(subject, "approval-node", "approval", EvidenceStatusDto.Ready, artifacts: [artifact])],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["approval-node"], ["approval-node"], [], [], []),
            Actions: [],
            Warnings: []);

        var act = () => store.WriteManifestAsync(packet, new EvidencePacketExportRequest("operator", "approval retention"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*approval-route-only*missing canonical subject linkage*");
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringManifestExport_RetainsRouteOnlyArtifactWithCanonicalSubjectInManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "close-2026-05");
        var artifact = new EvidenceArtifactRefDto(
            "approval-route-only",
            "approval",
            Path: null,
            Route: "/api/workstation/operations-continuity/workflows/workflow-1/approval",
            GeneratedAt: DateTimeOffset.UtcNow,
            Hash: null,
            Retained: true,
            CanonicalSubjectKind: EvidenceSubjectResolver.ApprovalKind,
            CanonicalSubjectId: "workflow-1");
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [Node(subject, "approval-node", "approval", EvidenceStatusDto.Ready, artifacts: [artifact])],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, ["approval-node"], ["approval-node"], [], [], []),
            Actions: [],
            Warnings: []);

        var response = await store.WriteManifestAsync(packet, new EvidencePacketExportRequest("operator", "approval retention"));
        var manifestPath = Path.Combine(root, response.ManifestPath.Replace('/', Path.DirectorySeparatorChar));
        var manifestJson = await File.ReadAllTextAsync(manifestPath);

        response.VaultIdentity.Should().NotBeNull();
        response.VaultIdentity!.StorageKind.Should().Be("file-manifest");
        response.VaultIdentity.Artifacts.Should().BeEmpty();
        manifestJson.Should().Contain("\"artifactId\": \"approval-route-only\"");
        manifestJson.Should().Contain("\"canonicalSubjectKind\": \"approval\"");
        manifestJson.Should().Contain("\"canonicalSubjectId\": \"workflow-1\"");
    }

    [Fact]
    public async Task EvidenceEndpoints_VaultSearch_RejectsEmptyLookup()
    {
        var root = Path.Combine(Path.GetTempPath(), $"evidence-vault-search-empty-{Guid.NewGuid():N}");
        await using var app = await CreateEvidenceAppAsync(root);
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/search",
            new EvidenceVaultLookupRequestDto(null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EvidenceEndpoints_VaultSearch_FindsBundlesByRunReportPackAndReconciliationCase()
    {
        var root = Path.Combine(Path.GetTempPath(), $"evidence-vault-search-{Guid.NewGuid():N}");
        await using var app = await CreateEvidenceAppAsync(root);
        var client = app.GetTestClient();

        var exportResponse = await client.PostAsJsonAsync(
            "/api/workstation/evidence/subjects/report-pack/current/export-manifest",
            new EvidencePacketExportRequest("operator", "seed")
            {
                Linkage = new EvidenceSubjectLinkageDto("report-pack/current", "run-123", "period-2026-05", "rp-55", "case-77")
            });
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var export = await exportResponse.Content.ReadFromJsonAsync<EvidencePacketExportResponse>(ServerJsonOptions);
        export.Should().NotBeNull();
        var manifestJson = await client.GetStringAsync(export!.ManifestRoute);
        manifestJson.Should().Contain("\"evidenceSubject\": \"report-pack/current\"");
        manifestJson.Should().Contain("\"runId\": \"run-123\"");
        manifestJson.Should().Contain("\"reportPackId\": \"rp-55\"");

        var response = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/search",
            new EvidenceVaultLookupRequestDto(null, "run-123", null, "rp-55", "case-77"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var matches = await response.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceVaultIdentityDto>>(ServerJsonOptions);
        matches.Should().NotBeNull();
        matches!.Should().ContainSingle();
        matches[0].SubjectKind.Should().Be("report-pack");
        matches[0].SubjectId.Should().Be("current");
    }

    private static EvidenceGraphService CreateGraphService(IReadOnlyList<IEvidenceContributor> contributors)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new EvidenceGraphService(
            new EvidenceSubjectResolver(services),
            new EvidenceTemplateRegistry(),
            contributors,
            NullLogger<EvidenceGraphService>.Instance);
    }

    private static EvidenceGraphService CreateSecurityMasterConflictGraphService(ISecurityMasterConflictService? conflictService)
    {
        var services = new ServiceCollection();
        if (conflictService is not null)
        {
            services.AddSingleton(conflictService);
        }

        var provider = services.BuildServiceProvider();
        return new EvidenceGraphService(
            new EvidenceSubjectResolver(provider),
            new EvidenceTemplateRegistry(),
            [new SecurityMasterConflictEvidenceContributor(provider)],
            NullLogger<EvidenceGraphService>.Instance);
    }

    private static EvidenceGraphService CreateOperationsApprovalGraphService(IOperationsContinuityWorkflowService? workflowService)
    {
        var services = new ServiceCollection();
        if (workflowService is not null)
        {
            services.AddSingleton(workflowService);
        }

        var provider = services.BuildServiceProvider();
        return new EvidenceGraphService(
            new EvidenceSubjectResolver(provider),
            new EvidenceTemplateRegistry(),
            [new OperationsApprovalEvidenceContributor(provider)],
            NullLogger<EvidenceGraphService>.Instance);
    }

    private static OperationsContinuityWorkflowDto CreateOperationsWorkflow(
        OperationsApprovalStateDto approvalState,
        DateTimeOffset? updatedAtUtc = null,
        bool accountingRecordAuditReady = true)
    {
        var workflowId = Guid.NewGuid();
        var fundAccountId = Guid.NewGuid();
        var periodId = "2026-05";
        var now = updatedAtUtc ?? DateTimeOffset.UtcNow;
        var eventType = approvalState switch
        {
            OperationsApprovalStateDto.Approved => "approval-approved",
            OperationsApprovalStateDto.Rejected => "approval-rejected",
            OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned => "approval-submitted",
            _ => "workflow-started"
        };
        var approval = new OperationsApprovalDto(
            ApprovalId: $"approval-{workflowId:N}",
            Status: approvalState,
            Operator: "ops-controller",
            Reviewer: "fund-controller",
            Rationale: "Evidence-backed close review.",
            SubmittedAtUtc: now.AddMinutes(-10),
            DecidedAtUtc: approvalState is OperationsApprovalStateDto.Approved or OperationsApprovalStateDto.Rejected
                ? now.AddMinutes(-2)
                : null,
            EvidenceLinks:
            [
                new OperationsEvidenceLinkDto(
                    $"approval-evidence-{workflowId:N}",
                    "Approval packet",
                    $"/api/workstation/operations/continuity/{workflowId:D}",
                    "approval",
                    now)
            ]);

        return new OperationsContinuityWorkflowDto(
            WorkflowId: workflowId,
            FundAccountId: fundAccountId,
            PeriodId: periodId,
            SecurityMasterSnapshotId: Guid.NewGuid(),
            BrokerSource: "alpaca",
            CreatedAtUtc: now.AddHours(-1),
            UpdatedAtUtc: now,
            Version: 7,
            Status: approvalState == OperationsApprovalStateDto.Approved
                ? OperationsWorkflowStatusDto.ReadyForClose
                : OperationsWorkflowStatusDto.ApprovalPending,
            BrokerIntakeState: OperationsBrokerIntakeStateDto.Complete,
            SecurityMasterState: OperationsSecurityMasterStateDto.Complete,
            LedgerPostingState: OperationsLedgerPostingStateDto.Complete,
            ReconciliationState: OperationsReconciliationStateDto.Complete,
            ApprovalState: approvalState,
            Gates:
            [
                new OperationsGateDto(
                    OperationsGateKeyDto.Approval,
                    "Approval and close readiness",
                    approvalState == OperationsApprovalStateDto.Approved
                        ? OperationsGateStatusDto.Passed
                        : OperationsGateStatusDto.ReviewRequired,
                    true,
                    "Requires operator, reviewer, rationale, and linked evidence before close.",
                    [],
                    [],
                    approvalState == OperationsApprovalStateDto.Approved ? now : null,
                    approvalState == OperationsApprovalStateDto.Approved ? "fund-controller" : null)
            ],
            Timeline:
            [
                new OperationsTimelineEntryDto(
                    Guid.NewGuid(),
                    now,
                    workflowId,
                    fundAccountId,
                    periodId,
                    eventType,
                    OperationsWorkflowStatusDto.ApprovalPending,
                    approvalState == OperationsApprovalStateDto.Approved
                        ? OperationsWorkflowStatusDto.ReadyForClose
                        : OperationsWorkflowStatusDto.ApprovalPending,
                    OperationsGateKeyDto.Approval,
                    OperationsGateStatusDto.ReviewRequired,
                    approvalState == OperationsApprovalStateDto.Approved
                        ? OperationsGateStatusDto.Passed
                        : OperationsGateStatusDto.ReviewRequired,
                    "fund-controller",
                    "Evidence-backed close review.",
                    $"corr-{workflowId:N}",
                    null,
                    [],
                    null,
                    $"hash-{workflowId:N}")
            ],
            BreakCases: [],
            LedgerPreview: null,
            Approvals: [approval],
            ReportPackReadiness: new OperationsReportPackReadinessDto(
                true,
                $"report-pack-{workflowId:N}",
                null,
                [
                    new OperationsEvidenceLinkDto(
                        $"report-pack-{workflowId:N}",
                        "Report pack",
                        "/api/fund-structure/report-packs/current",
                        "report-pack",
                        now)
                ]),
            CloseChecklist:
            [
                new OperationsCloseChecklistTaskDto(
                    "approval-control",
                    OperationsGateKeyDto.Approval,
                    "Controller approval",
                    "fund-controller",
                    "Approval packet",
                    1,
                    null,
                    null,
                    "Complete",
                    null,
                    $"approval-evidence-{workflowId:N}",
                    "/accounting",
                    false,
                    now,
                    "fund-controller")
            ],
            EvidenceLinks:
            [
                new OperationsEvidenceLinkDto(
                    $"workflow-evidence-{workflowId:N}",
                    "Workflow evidence",
                    $"/api/workstation/operations/continuity/{workflowId:D}",
                    "approval",
                    now)
            ],
            Blockers: [],
            NextActions: [],
            CloseReadiness: null,
            AccountingRecordSummary: BuildAccountingRecordSummary(workflowId, now, accountingRecordAuditReady));
    }

    private static OperationsAccountingRecordSummaryDto BuildAccountingRecordSummary(
        Guid workflowId,
        DateTimeOffset now,
        bool isAuditReady)
    {
        var readyCategories = new[]
        {
            ("source-records", "Source records", "Provider statement, custodian file, and bank record evidence are retained."),
            ("normalized-activity", "Normalized activity", "Normalized transactions, positions, and balances are retained."),
            ("reconciliation-case-history", "Reconciliation case history", "Reconciliation run and resolved exception history are retained."),
            ("ledger-evidence", "Ledger evidence", "Journal preview, posted batch, and trial-balance support are retained."),
            ("approvals", "Approvals", "Approval submission, reviewer decision, and checklist controls are retained.")
        };

        var categories = readyCategories
            .Select(category => new OperationsAccountingRecordEvidenceCategoryDto(
                category.Item1,
                category.Item2,
                true,
                category.Item3,
                $"/api/workstation/operations/continuity/{workflowId:D}",
                [new OperationsEvidenceLinkDto($"{category.Item1}-{workflowId:N}", category.Item2, $"/api/workstation/operations/continuity/{workflowId:D}", "operations-accounting-record", now)],
                RequiredEvidence: [category.Item2.ToLowerInvariant()]))
            .ToList();

        categories.Add(new OperationsAccountingRecordEvidenceCategoryDto(
            "report-pack",
            "Report pack",
            true,
            $"Report pack report-pack-{workflowId:N} is linked with retained manifest and validation evidence.",
            $"/api/workstation/operations/continuity/{workflowId:D}",
            [new OperationsEvidenceLinkDto($"report-pack-{workflowId:N}", "Report pack", "/api/fund-structure/report-packs/current", "report-pack", now)],
            RequiredEvidence: ["report-pack manifest", "report-pack provenance", "report-pack validation"]));
        categories.Add(new OperationsAccountingRecordEvidenceCategoryDto(
            "exports",
            "Exports and retained evidence",
            isAuditReady,
            isAuditReady
                ? $"Report pack report-pack-{workflowId:N} is linked with retained manifest and export evidence."
                : $"Report pack report-pack-{workflowId:N} still needs export manifest and retained evidence hash.",
            $"/api/workstation/operations/continuity/{workflowId:D}",
            isAuditReady
                ? [new OperationsEvidenceLinkDto($"exports-{workflowId:N}", "Exports", "/api/fund-structure/report-packs/current", "report-pack", now)]
                : [],
            RequiredEvidence: ["export manifest", "retained evidence hash", "close-package publication"]));
        categories.Add(new OperationsAccountingRecordEvidenceCategoryDto(
            "restatement-lineage",
            "Restatement lineage",
            isAuditReady,
            isAuditReady
                ? "Closed package establishes the retained baseline for future restatements."
                : "Restatement baseline is pending until the close package is published.",
            $"/api/workstation/operations/continuity/{workflowId:D}",
            isAuditReady
                ? [new OperationsEvidenceLinkDto($"restatement-lineage-{workflowId:N}", "Restatement lineage", "/api/fund-structure/report-packs/current", "report-pack", now)]
                : [],
            RequiredEvidence: ["published baseline", "prior-version pointer when restated", "changed-line evidence"]));

        return new OperationsAccountingRecordSummaryDto(
            $"accounting-record-{workflowId:N}",
            isAuditReady,
            categories.Count(static category => category.IsComplete),
            8,
            isAuditReady
                ? "Accounting record evidence is audit ready for approval review."
                : "Accounting record evidence requires report-pack lineage review before audit readiness.",
            categories,
            categories.SelectMany(static category => category.EvidenceLinks).ToArray());
    }

    private static async Task<WebApplication> CreateEvidenceAppAsync(
        string root,
        IOperationsContinuityWorkflowService? operationsWorkflowService = null)
    {
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, """{"DataRoot":"."}""");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new Meridian.Application.UI.ConfigStore(configPath));
        builder.Services.AddWorkflowLibrary();
        if (operationsWorkflowService is not null)
        {
            builder.Services.AddSingleton(operationsWorkflowService);
        }

        builder.Services.AddEvidenceWorkflowFabric();

        var app = builder.Build();
        app.MapEvidenceEndpoints(ServerJsonOptions);
        await app.StartAsync();
        return app;
    }

    private static EvidenceSubjectDto Subject(string kind, string id)
        => new(
            SubjectId: id,
            SubjectKind: kind,
            Label: $"{kind} {id}",
            Workspace: "Trading",
            Route: "/trading/readiness",
            PageTag: "TradingReadiness");

    private static EvidenceNodeDto Node(
        EvidenceSubjectDto subject,
        string id,
        string kind,
        EvidenceStatusDto status,
        bool stale = false,
        IReadOnlyList<string>? workItemIds = null,
        IReadOnlyList<EvidenceArtifactRefDto>? artifacts = null)
        => new(
            EvidenceId: id,
            Subject: subject,
            Kind: kind,
            Status: status,
            Freshness: new EvidenceFreshnessDto(
                stale ? DateTimeOffset.UtcNow.AddDays(-8) : DateTimeOffset.UtcNow,
                stale,
                stale ? "Evidence is older than seven days." : null),
            SourceSystem: "test",
            Summary: $"{kind} evidence",
            ArtifactRefs: artifacts ?? [],
            RelatedWorkItemIds: workItemIds ?? []);

    private static FundReportPackSnapshotDto BuildReportPackSnapshot(Guid reportId)
    {
        var generatedAt = new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero);
        return new FundReportPackSnapshotDto(
            ReportId: reportId,
            FundProfileId: "fund-ops",
            DisplayName: "Fund Operations Report Pack",
            ReportKind: GovernanceReportKindDto.TrialBalance,
            Currency: "USD",
            AsOf: generatedAt,
            GeneratedAt: generatedAt,
            TotalNetAssets: 1_000m,
            AuditActor: "controller",
            CorrelationId: "evidence-report-pack",
            DecisionRationale: "monthly close",
            Provenance: new FundReportPackProvenanceDto(
                RelatedRunIds: [],
                JournalEntryCount: 1,
                LedgerEntryCount: 1,
                TrialBalanceLineCount: 1,
                ReconciliationRunCount: 1,
                OpenReconciliationBreakCount: 0,
                SecurityResolvedCount: 1,
                SecurityMissingCount: 0,
                LineagePointers: [],
                SourceSnapshotHash: new string('a', 64)),
            Artifacts: [],
            Warnings: [])
        {
            Status = GovernanceReportPackStatusDto.Validated
        };
    }

    private static bool IsRouteOnlyArtifact(JsonElement artifact, string kind, string route)
        => artifact.GetProperty("kind").GetString() == kind &&
           artifact.GetProperty("route").GetString() == route &&
           artifact.GetProperty("path").ValueKind == JsonValueKind.Null &&
           artifact.GetProperty("hash").ValueKind == JsonValueKind.Null &&
           artifact.GetProperty("canonicalSubjectKind").GetString() == EvidenceSubjectResolver.StrategyRunKind &&
           artifact.GetProperty("canonicalSubjectId").GetString() == "run-ledger-proof";

    private sealed class StubContributor : IEvidenceContributor
    {
        private readonly Func<EvidenceSubjectDto, bool> _supports;
        private readonly Func<EvidenceContributionContext, EvidenceContribution> _contribute;

        public StubContributor(
            string contributorId,
            Func<EvidenceSubjectDto, bool> supports,
            Func<EvidenceContributionContext, EvidenceContribution> contribute)
        {
            ContributorId = contributorId;
            _supports = supports;
            _contribute = contribute;
        }

        public string ContributorId { get; }

        public bool Supports(EvidenceSubjectDto subject) => _supports(subject);

        public Task<EvidenceContribution> ContributeAsync(EvidenceContributionContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_contribute(context));
        }
    }

    private sealed class InMemoryReportPackRepository(FundReportPackSnapshotDto snapshot) : IGovernanceReportPackRepository
    {
        public Task<FundReportPackSnapshotDto> SaveAsync(
            FundReportPackSnapshotDto snapshot,
            IReadOnlyList<GovernanceReportPackArtifactContent> artifacts,
            CancellationToken ct = default)
            => Task.FromResult(snapshot);

        public Task<IReadOnlyList<FundReportPackHistoryItemDto>> GetHistoryAsync(
            string fundProfileId,
            int limit,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FundReportPackHistoryItemDto>>([]);

        public Task<FundReportPackSnapshotDto?> GetAsync(Guid reportId, CancellationToken ct = default)
            => Task.FromResult(reportId == snapshot.ReportId ? snapshot : null);

        public Task<FundReportPackSnapshotDto?> FindLatestByRunIdAsync(string runId, CancellationToken ct = default)
            => Task.FromResult<FundReportPackSnapshotDto?>(null);

        public Task<FundReportPackEvidenceBundleDto> SaveEvidenceBundleAsync(
            FundReportPackSnapshotDto snapshot,
            FundReportPackEvidenceBundleDto bundle,
            CancellationToken ct = default)
            => Task.FromResult(bundle);
    }

    private sealed class StubSecurityMasterConflictService(IReadOnlyList<SecurityMasterConflict> conflicts)
        : ISecurityMasterConflictService
    {
        private readonly Dictionary<Guid, SecurityMasterConflict> _conflicts = conflicts.ToDictionary(static conflict => conflict.ConflictId);

        public Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SecurityMasterConflict>>(
                _conflicts.Values
                    .Where(static conflict => string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase))
                    .ToArray());
        }

        public Task<SecurityMasterConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_conflicts.GetValueOrDefault(conflictId));
        }

        public Task<SecurityMasterConflict?> ResolveAsync(ResolveConflictRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_conflicts.GetValueOrDefault(request.ConflictId));
        }

        public Task RecordConflictsForProjectionAsync(SecurityProjectionRecord projection, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class StubOperationsContinuityWorkflowService(IReadOnlyList<OperationsContinuityWorkflowDto> workflows)
        : IOperationsContinuityWorkflowService
    {
        private readonly Dictionary<Guid, OperationsContinuityWorkflowDto> _workflows =
            workflows.ToDictionary(static workflow => workflow.WorkflowId);

        public Task<OperationsTransitionResultDto> StartWorkflowAsync(OperationsStartWorkflowRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> ImportBrokerDataAsync(Guid workflowId, OperationsTransitionRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> NormalizeBrokerTransactionsAsync(Guid workflowId, OperationsTransitionRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> RefreshGatePostureAsync(Guid workflowId, OperationsGatePostureRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> ResolveSecurityMasterMappingsAsync(Guid workflowId, OperationsSecurityMasterResolveRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> ApproveSecurityMasterOverrideAsync(Guid workflowId, string overrideId, OperationsSecurityMasterOverrideApprovalRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> BuildLedgerDraftAsync(Guid workflowId, OperationsLedgerDraftRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> ValidateLedgerDraftAsync(Guid workflowId, OperationsLedgerValidationRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> PostLedgerEntriesAsync(Guid workflowId, OperationsLedgerPostRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> RunReconciliationAsync(Guid workflowId, OperationsReconciliationRunRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> ResolveBreakCaseAsync(Guid workflowId, string breakId, OperationsResolveBreakCaseRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> SubmitForApprovalAsync(Guid workflowId, OperationsSubmitApprovalRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> ApproveWorkflowAsync(Guid workflowId, OperationsApprovalDecisionRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> RejectWorkflowAsync(Guid workflowId, OperationsRejectWorkflowRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> CloseWorkflowAsync(Guid workflowId, OperationsCloseWorkflowRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OperationsTransitionResultDto> ReopenWorkflowAsync(Guid workflowId, OperationsReopenWorkflowRequestDto request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>> ListAsync(
            Guid? fundAccountId = null,
            string? periodId = null,
            OperationsWorkflowStatusDto? status = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var summaries = _workflows.Values
                .Where(workflow => !fundAccountId.HasValue || workflow.FundAccountId == fundAccountId.Value)
                .Where(workflow => string.IsNullOrWhiteSpace(periodId) || string.Equals(workflow.PeriodId, periodId, StringComparison.OrdinalIgnoreCase))
                .Where(workflow => !status.HasValue || workflow.Status == status.Value)
                .Select(static workflow => new OperationsContinuityWorkflowSummaryDto(
                    workflow.WorkflowId,
                    workflow.FundAccountId,
                    workflow.PeriodId,
                    workflow.SecurityMasterSnapshotId,
                    workflow.BrokerSource,
                    workflow.Status,
                    workflow.Version,
                    workflow.CreatedAtUtc,
                    workflow.UpdatedAtUtc,
                    workflow.Gates,
                    workflow.NextActions))
                .ToArray();
            return Task.FromResult<IReadOnlyList<OperationsContinuityWorkflowSummaryDto>>(summaries);
        }

        public Task<OperationsContinuityWorkflowDto?> GetAsync(Guid workflowId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_workflows.GetValueOrDefault(workflowId));
        }

        public Task<IReadOnlyList<OperationsTimelineEntryDto>> GetTimelineAsync(Guid workflowId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<OperationsTimelineEntryDto>>(
                _workflows.TryGetValue(workflowId, out var workflow) ? workflow.Timeline : []);
        }

        public Task<IReadOnlyList<OperationsCloseChecklistTaskDto>> GetChecklistAsync(Guid workflowId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<OperationsCloseChecklistTaskDto>>(
                _workflows.TryGetValue(workflowId, out var workflow) ? workflow.CloseChecklist : []);
        }

        public Task<OperationsTransitionResultDto> AcknowledgeChecklistTaskAsync(
            Guid workflowId,
            string taskId,
            OperationsChecklistAcknowledgeRequestDto request,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
