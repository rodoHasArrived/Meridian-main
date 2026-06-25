using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Ledger;
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
    public async Task EvidenceGraphService_DuringOperationalGraphReview_ReportsV018ProofChainLayerCoverage()
    {
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "current");
        var source = Node(subject, "source-statement", "broker-statement", EvidenceStatusDto.Ready);
        var normalized = Node(subject, "normalized-activity", "normalized-activity", EvidenceStatusDto.Ready);
        var reconciliation = Node(subject, "reconciliation-run", "reconciliation-run", EvidenceStatusDto.Ready);
        var ledger = Node(subject, "run-ledger", "run-ledger", EvidenceStatusDto.Missing);
        var audit = Node(subject, "audit-manifest", "evidence-vault-manifest", EvidenceStatusDto.Ready);
        var contributors = new IEvidenceContributor[]
        {
            new StubContributor("proof-chain", static _ => true, _ => new EvidenceContribution(
                Nodes: [source, normalized, reconciliation, ledger, audit],
                Edges:
                [
                    new EvidenceEdgeDto(source.EvidenceId, normalized.EvidenceId, "normalizes", "Statement evidence is normalized."),
                    new EvidenceEdgeDto(normalized.EvidenceId, reconciliation.EvidenceId, "reconciles", "Normalized activity reconciles to the close run."),
                    new EvidenceEdgeDto(reconciliation.EvidenceId, ledger.EvidenceId, "posts-to", "Reconciled activity posts to the ledger."),
                    new EvidenceEdgeDto(ledger.EvidenceId, audit.EvidenceId, "retained-by", "Audit manifest retains the posting evidence.")
                ],
                Actions: [],
                RequiredEvidenceIds: [source.EvidenceId, normalized.EvidenceId, reconciliation.EvidenceId, ledger.EvidenceId, "delivery-manifest"],
                Warnings: []))
        };
        var service = CreateGraphService(contributors);

        var packet = await service.GetPacketAsync(subject.SubjectKind, subject.SubjectId);

        packet.Should().NotBeNull();
        packet!.ProofChain.TotalLayerCount.Should().Be(9);
        packet.ProofChain.CoveredLayerCount.Should().Be(5);
        packet.ProofChain.CoveragePercent.Should().Be(56);
        packet.ProofChain.Status.Should().Be(EvidenceStatusDto.Blocked);
        packet.ProofChain.Summary.Should().Contain("v0.18 proof-chain layers");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Source)
            .ReadyEvidenceIds.Should().Contain(source.EvidenceId);
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Normalization)
            .CoveragePercent.Should().Be(100);
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Reconciliation)
            .EvidenceKinds.Should().Contain("reconciliation-run");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Ledger)
            .Status.Should().Be(EvidenceStatusDto.Blocked);
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Delivery)
            .MissingEvidenceIds.Should().Contain("delivery-manifest");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Audit)
            .EvidenceIds.Should().Contain(audit.EvidenceId);
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
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var workflow = CreateOperationsWorkflow(OperationsApprovalStateDto.Approved, ledgerBookId: ledgerBookId);
        var service = CreateOperationsApprovalGraphService(new StubOperationsContinuityWorkflowService([workflow]));

        var packet = await service.GetPacketAsync(EvidenceSubjectResolver.ApprovalKind, workflow.WorkflowId.ToString("D"));

        packet.Should().NotBeNull();
        packet!.Subject.SubjectKind.Should().Be(EvidenceSubjectResolver.ApprovalKind);
        packet.Subject.LedgerBookId.Should().Be(ledgerBookId);
        packet.Subject.Route.Should().Contain($"ledgerBookId={ledgerBookId:D}");
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

        var queryScopedPacket = await service.GetPacketAsync(
            EvidenceSubjectResolver.ApprovalKind,
            $"{workflow.WorkflowId:D}?ledgerBookId={ledgerBookId:D}");
        var mismatchedBookPacket = await service.GetPacketAsync(
            EvidenceSubjectResolver.ApprovalKind,
            $"{workflow.WorkflowId:D}?ledgerBookId={Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"):D}");

        queryScopedPacket.Should().NotBeNull();
        queryScopedPacket!.Subject.SubjectId.Should().Be(workflow.WorkflowId.ToString("D"));
        queryScopedPacket.Subject.LedgerBookId.Should().Be(ledgerBookId);
        mismatchedBookPacket.Should().BeNull();
    }

    [Fact]
    public async Task EvidenceGraphService_DuringAccountingRecordReview_ProjectsAccountingRecordAsFirstClassSubject()
    {
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var workflow = CreateOperationsWorkflow(OperationsApprovalStateDto.Approved, ledgerBookId: ledgerBookId);
        var service = CreateOperationsApprovalGraphService(new StubOperationsContinuityWorkflowService([workflow]));

        var packet = await service.GetPacketAsync(EvidenceSubjectResolver.AccountingRecordKind, workflow.WorkflowId.ToString("D"));

        packet.Should().NotBeNull();
        packet!.Subject.SubjectKind.Should().Be(EvidenceSubjectResolver.AccountingRecordKind);
        packet.Subject.Label.Should().Contain("Accounting record");
        packet.Subject.LedgerBookId.Should().Be(ledgerBookId);
        packet.Subject.Route.Should().Contain($"ledgerBookId={ledgerBookId:D}");
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

        var queryScopedPacket = await service.GetPacketAsync(
            EvidenceSubjectResolver.AccountingRecordKind,
            $"{workflow.WorkflowId:D}?ledgerBookId={ledgerBookId:D}");

        queryScopedPacket.Should().NotBeNull();
        queryScopedPacket!.Subject.SubjectId.Should().Be(workflow.WorkflowId.ToString("D"));
        queryScopedPacket.Subject.LedgerBookId.Should().Be(ledgerBookId);
    }

    [Fact]
    public async Task EvidenceSubjectResolver_DuringOperationsEvidenceReview_ListsAccountingRecordSubjects()
    {
        var ledgerBookId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var workflow = CreateOperationsWorkflow(OperationsApprovalStateDto.Approved, ledgerBookId: ledgerBookId);
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
            subject.Label.Contains(workflow.PeriodId, StringComparison.OrdinalIgnoreCase) &&
            subject.LedgerBookId == ledgerBookId &&
            subject.Route != null &&
            subject.Route.Contains($"ledgerBookId={ledgerBookId:D}", StringComparison.OrdinalIgnoreCase));
        subjects.Should().Contain(subject =>
            subject.SubjectKind == EvidenceSubjectResolver.ApprovalKind &&
            subject.SubjectId == workflow.WorkflowId.ToString("D") &&
            subject.LedgerBookId == ledgerBookId &&
            subject.Route != null &&
            subject.Route.Contains($"ledgerBookId={ledgerBookId:D}", StringComparison.OrdinalIgnoreCase));
        resolver.IsSupportedKind(EvidenceSubjectResolver.AccountingRecordKind).Should().BeTrue();
    }

    [Fact]
    public async Task EvidenceSubjectResolver_DuringPrivateCapitalFundEventReview_ListsFundScopedSubjects()
    {
        var activity = PrivateCapitalActivityProjection();
        var provider = new ServiceCollection()
            .AddSingleton<IManualJournalEntryWorkbenchService>(new StubManualJournalEntryWorkbenchService(activity))
            .BuildServiceProvider();
        var resolver = new EvidenceSubjectResolver(provider);

        var subjects = await resolver.ListAsync();

        subjects.Should().Contain(subject =>
            subject.SubjectKind == EvidenceSubjectResolver.PrivateCapitalFundEventKind &&
            subject.SubjectId == "fund-event:fund-alpha:capital-call:20260630" &&
            subject.Label.Contains("CapitalCall", StringComparison.OrdinalIgnoreCase) &&
            subject.Route != null &&
            subject.Route.Contains("fund-event%3Afund-alpha%3Acapital-call%3A20260630", StringComparison.OrdinalIgnoreCase));
        subjects.Should().Contain(subject =>
            subject.SubjectKind == EvidenceSubjectResolver.PaymentIntentKind &&
            subject.SubjectId == "payment:fund-alpha:capital-call:20260630" &&
            subject.Label.Contains("Ready, execution deferred", StringComparison.OrdinalIgnoreCase) &&
            subject.Route != null &&
            subject.Route.Contains("paymentIntentId=payment%3Afund-alpha%3Acapital-call%3A20260630", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EvidenceGraphService_DuringPrivateCapitalFundEventReview_ProjectsUnifiedLedgerEvidence()
    {
        var activity = PrivateCapitalActivityProjection();
        var provider = new ServiceCollection()
            .AddSingleton<IManualJournalEntryWorkbenchService>(new StubManualJournalEntryWorkbenchService(activity))
            .BuildServiceProvider();
        var graph = new EvidenceGraphService(
            new EvidenceSubjectResolver(provider),
            new EvidenceTemplateRegistry(),
            [new PrivateCapitalFundEventEvidenceContributor(provider)],
            NullLogger<EvidenceGraphService>.Instance);

        var packet = await graph.GetPacketAsync(
            EvidenceSubjectResolver.PrivateCapitalFundEventKind,
            "fund-event:fund-alpha:capital-call:20260630");

        packet.Should().NotBeNull();
        packet!.Subject.SubjectKind.Should().Be(EvidenceSubjectResolver.PrivateCapitalFundEventKind);
        packet.Subject.Route.Should().Contain("/accounting/journal-entries");
        packet.Nodes.Select(static node => node.Kind).Should().Contain([
            "private-capital-fund-event",
            "retained-evidence",
            "approval-state",
            "capital-account-subledger",
            "ledger-impact",
            "report-output"
        ]);
        var subledgerNode = packet.Nodes.Single(node => node.Kind == "capital-account-subledger");
        var reportOutputNode = packet.Nodes.Single(node => node.Kind == "report-output");

        packet.Nodes.Should().Contain(node =>
            node.Kind == "private-capital-fund-event" &&
            node.Status == EvidenceStatusDto.Ready &&
            node.ArtifactRefs.Any(artifact =>
                artifact.Kind == "fund-event-ledger-record-route" &&
                artifact.Route!.Contains("/api/ledger/private-capital/fund-event-record", StringComparison.OrdinalIgnoreCase)));
        packet.Nodes.Should().Contain(node =>
            node.Kind == "retained-evidence" &&
            node.Status == EvidenceStatusDto.Ready &&
            node.ArtifactRefs.Any(artifact => artifact.Retained));
        subledgerNode.Status.Should().Be(EvidenceStatusDto.Ready);
        subledgerNode.Summary.Should().Contain("1 capital-account subledger entry");
        subledgerNode.Summary.Should().Contain("move net activity from 0 to 100 USD");
        subledgerNode.ArtifactRefs.Should().Contain(artifact =>
            artifact.Kind == "capital-account-subledger-route" &&
            artifact.Route!.Contains("/api/ledger/private-capital/capital-account-subledger", StringComparison.OrdinalIgnoreCase) &&
            artifact.Route.Contains("capitalAccountId=capital-account%3Afund-alpha%3Alp-1", StringComparison.OrdinalIgnoreCase));
        packet.Nodes.Should().Contain(node => node.Kind == "ledger-impact" && node.Status == EvidenceStatusDto.Ready);
        reportOutputNode.Status.Should().Be(EvidenceStatusDto.Ready);
        reportOutputNode.Summary.Should().Contain("report readiness is True");
        reportOutputNode.Summary.Should().Contain("publication is False");
        reportOutputNode.ArtifactRefs.Should().Contain(artifact =>
            artifact.Kind == "report-output-route" &&
            artifact.Retained == false &&
            artifact.Route!.Contains("/api/ledger/private-capital/report-output", StringComparison.OrdinalIgnoreCase) &&
            artifact.Route.Contains("reportOutputId=report-output%3Afund-event%3Afund-alpha%3Acapital-call%3A20260630", StringComparison.OrdinalIgnoreCase));
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.Ready);
        packet.Completeness.RequiredIds.Should().Contain(subledgerNode.EvidenceId);
        packet.Completeness.RequiredIds.Should().Contain(reportOutputNode.EvidenceId);
        packet.Completeness.ReadyIds.Should().Contain(subledgerNode.EvidenceId);
        packet.Completeness.ReadyIds.Should().Contain(reportOutputNode.EvidenceId);
        packet.Completeness.MissingIds.Should().BeEmpty();
        packet.ProofChain.CoveredLayerCount.Should().BeGreaterThanOrEqualTo(5);
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Source)
            .EvidenceKinds.Should().Contain("retained-evidence");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.CapitalAccounts)
            .EvidenceIds.Should().Contain(subledgerNode.EvidenceId);
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Ledger)
            .EvidenceKinds.Should().Contain("ledger-impact");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Reporting)
            .EvidenceIds.Should().Contain(reportOutputNode.EvidenceId);
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Close)
            .EvidenceKinds.Should().Contain("approval-state");

        var retainedNode = Node(
            packet.Subject,
            "private-capital-retained-artifact",
            "retained-evidence",
            EvidenceStatusDto.Ready,
            artifacts:
            [
                new EvidenceArtifactRefDto(
                    "private-capital-retained-artifact:canonical-link",
                    "retained-evidence-link",
                    Path: "/evidence/fund-alpha/bank-cash-capital-call.pdf",
                    Route: null,
                    GeneratedAt: DateTimeOffset.UtcNow,
                    Hash: "sha256:private-capital-fund-event",
                    Retained: true,
                    CanonicalSubjectKind: EvidenceSubjectResolver.PrivateCapitalFundEventKind,
                    CanonicalSubjectId: packet.Subject.SubjectId)
            ]);
        var validation = new EvidencePacketValidationService().Validate(
            [retainedNode],
            [],
            new HashSet<string>(["private-capital-retained-artifact"], StringComparer.OrdinalIgnoreCase),
            enforceNoOrphanRule: false);

        validation.Completeness.Status.Should().Be(EvidenceStatusDto.Ready);
        validation.Completeness.ValidationIssues.Should().NotContain(issue =>
            issue.Code.Contains("canonical-subject", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EvidenceGraphService_DuringPrivateCapitalFundEventReview_PreservesLedgerBookScope()
    {
        var ledgerBookId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        var activity = PrivateCapitalActivityProjection(ledgerBookId);
        var manualJournalService = new StubManualJournalEntryWorkbenchService(activity);
        var provider = new ServiceCollection()
            .AddSingleton<IManualJournalEntryWorkbenchService>(manualJournalService)
            .BuildServiceProvider();
        var resolver = new EvidenceSubjectResolver(provider);
        var graph = new EvidenceGraphService(
            resolver,
            new EvidenceTemplateRegistry(),
            [new PrivateCapitalFundEventEvidenceContributor(provider), new PaymentIntentEvidenceContributor(provider)],
            NullLogger<EvidenceGraphService>.Instance);

        var subjects = await resolver.ListAsync();
        var fundEventSubject = subjects.Single(subject =>
            subject.SubjectKind == EvidenceSubjectResolver.PrivateCapitalFundEventKind &&
            subject.SubjectId == "fund-event:fund-alpha:capital-call:20260630");
        fundEventSubject.LedgerBookId.Should().Be(ledgerBookId);
        fundEventSubject.Route.Should().Contain($"ledgerBookId={ledgerBookId:D}");
        manualJournalService.RequiredLedgerBookId = ledgerBookId;
        manualJournalService.PrivateCapitalActivityLedgerBookRequests.Clear();

        var fundEventPacket = await graph.GetPacketAsync(
            EvidenceSubjectResolver.PrivateCapitalFundEventKind,
            fundEventSubject.SubjectId,
            ledgerBookId: ledgerBookId);
        var paymentPacket = await graph.GetPacketAsync(
            EvidenceSubjectResolver.PaymentIntentKind,
            "payment:fund-alpha:capital-call:20260630",
            ledgerBookId: ledgerBookId);

        fundEventPacket.Should().NotBeNull();
        paymentPacket.Should().NotBeNull();
        fundEventPacket!.Subject.LedgerBookId.Should().Be(ledgerBookId);
        paymentPacket!.Subject.LedgerBookId.Should().Be(ledgerBookId);
        manualJournalService.PrivateCapitalActivityLedgerBookRequests.Should().OnlyContain(item => item == ledgerBookId);

        manualJournalService.PrivateCapitalActivityLedgerBookRequests.Clear();
        var queryScopedFundEventSubjectId = $"{fundEventSubject.SubjectId}?ledgerBookId={ledgerBookId:D}";
        var queryScopedPaymentSubjectId = $"payment:fund-alpha:capital-call:20260630?ledgerBookId={ledgerBookId:D}";

        var queryScopedFundEventPacket = await graph.GetPacketAsync(
            EvidenceSubjectResolver.PrivateCapitalFundEventKind,
            queryScopedFundEventSubjectId);
        var queryScopedPaymentPacket = await graph.GetPacketAsync(
            EvidenceSubjectResolver.PaymentIntentKind,
            queryScopedPaymentSubjectId);

        queryScopedFundEventPacket.Should().NotBeNull();
        queryScopedPaymentPacket.Should().NotBeNull();
        queryScopedFundEventPacket!.Subject.SubjectId.Should().Be(fundEventSubject.SubjectId);
        queryScopedPaymentPacket!.Subject.SubjectId.Should().Be("payment:fund-alpha:capital-call:20260630");
        queryScopedFundEventPacket.Subject.LedgerBookId.Should().Be(ledgerBookId);
        queryScopedPaymentPacket.Subject.LedgerBookId.Should().Be(ledgerBookId);
        manualJournalService.PrivateCapitalActivityLedgerBookRequests.Should().OnlyContain(item => item == ledgerBookId);

        manualJournalService.PrivateCapitalActivityLedgerBookRequests.Clear();
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-book-scope", Guid.NewGuid().ToString("N"));
        await using var app = await CreateEvidenceAppAsync(root, manualJournalService: manualJournalService);
        var client = app.GetTestClient();
        var encodedSubjectId = Uri.EscapeDataString(fundEventSubject.SubjectId);
        var packetResponse = await client.GetAsync(
            $"/api/workstation/evidence/subjects/private-capital-fund-event/{encodedSubjectId}/packet?ledgerBookId={ledgerBookId:D}");

        packetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var endpointPacket = await packetResponse.Content.ReadFromJsonAsync<EvidencePacketDto>(ServerJsonOptions);
        endpointPacket.Should().NotBeNull();
        endpointPacket!.Subject.LedgerBookId.Should().Be(ledgerBookId);
        manualJournalService.PrivateCapitalActivityLedgerBookRequests.Should().OnlyContain(item => item == ledgerBookId);
    }

    [Fact]
    public async Task MapEvidenceEndpoints_DuringPaymentIntentCashEvidenceReview_ReturnsPacketValidationAndManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-payment-intent", Guid.NewGuid().ToString("N"));
        var activity = PrivateCapitalActivityProjection();
        var subjectId = "payment:fund-alpha:capital-call:20260630";
        var encodedSubjectId = Uri.EscapeDataString(subjectId);
        await using var app = await CreateEvidenceAppAsync(
            root,
            manualJournalService: new StubManualJournalEntryWorkbenchService(activity));
        var client = app.GetTestClient();

        var templatesResponse = await client.GetAsync("/api/workstation/evidence/templates");
        templatesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var templates = await templatesResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceTemplateDto>>(ServerJsonOptions);
        templates.Should().Contain(template =>
            template.WorkflowId == "payment-intent-cash-evidence-review" &&
            template.RequiredEvidenceKinds.Contains("bank-cash-evidence") &&
            template.RequiredEvidenceKinds.Contains("execution-deferred"));

        var subjectsResponse = await client.GetAsync("/api/workstation/evidence/subjects");
        subjectsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var subjects = await subjectsResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceSubjectDto>>(ServerJsonOptions);
        subjects.Should().Contain(subject =>
            subject.SubjectKind == EvidenceSubjectResolver.PaymentIntentKind &&
            subject.SubjectId == subjectId);

        var packetResponse = await client.GetAsync($"/api/workstation/evidence/subjects/payment-intent/{encodedSubjectId}/packet");
        packetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var packet = await packetResponse.Content.ReadFromJsonAsync<EvidencePacketDto>(ServerJsonOptions);
        packet.Should().NotBeNull();
        packet!.Subject.SubjectKind.Should().Be(EvidenceSubjectResolver.PaymentIntentKind);
        packet.Nodes.Select(static node => node.Kind).Should().Contain([
            "payment-intent",
            "payment-requester",
            "approval-chain",
            "expected-cash-movement",
            "bank-cash-evidence",
            "reconciliation-linkage",
            "audit-history",
            "execution-deferred",
            "private-capital-fund-event",
            "capital-account-subledger",
            "ledger-impact",
            "report-output"
        ]);
        packet.Nodes.Single(node => node.Kind == "payment-intent").Status.Should().Be(EvidenceStatusDto.Ready);
        packet.Nodes.Single(node => node.Kind == "execution-deferred")
            .Summary.Contains("Full payment execution is explicitly deferred", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        var expectedCashNode = packet.Nodes.Single(node => node.Kind == "expected-cash-movement");
        expectedCashNode.Summary.Should().Contain("payee fund:fund-alpha");
        expectedCashNode.Summary.Should().Contain("approval policy Controller approval retained before execution-deferred reliance");
        expectedCashNode.ArtifactRefs.Should().Contain(artifact => artifact.Kind == "payment-intent-source-evidence");
        var bankCashNode = packet.Nodes.Single(node => node.Kind == "bank-cash-evidence");
        var bankCashArtifact = bankCashNode.ArtifactRefs.Should()
            .ContainSingle(artifact => artifact.Kind == "retained-cash-evidence")
            .Which;
        bankCashArtifact.Capture.Should().NotBeNull();
        bankCashArtifact.Capture!.CaptureChannel.Should().Be("Upload");
        bankCashArtifact.Capture.SourceReference.Should().Be("settlement:fund-alpha:capital-call:20260630");
        bankCashArtifact.ExtractedFields.Should().ContainSingle(field =>
            field.FieldName == "amount" &&
            field.ExtractedValue == "100" &&
            field.ExpectedValue == "100" &&
            field.ConfidenceScore == 1m &&
            field.ReviewState == "Reviewed" &&
            field.ValidationStatus == EvidenceStatusDto.Ready &&
            field.LinkedRecordKind == "payment-intent" &&
            field.LinkedRecordId == "payment:fund-alpha:capital-call:20260630");
        bankCashArtifact.ExtractedFields.Should().ContainSingle(field =>
            field.FieldName == "externalReference" &&
            field.ExtractedValue == "settlement:fund-alpha:capital-call:20260630" &&
            field.ExpectedValue == "settlement:fund-alpha:capital-call:20260630" &&
            field.ValidationStatus == EvidenceStatusDto.Ready &&
            field.LinkedRecordKind == "settlement-reference");
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.Ready);
        packet.Completeness.ValidationIssues.Should().NotContain(issue => issue.Code == "orphan-evidence");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Source)
            .EvidenceKinds.Should().Contain("expected-cash-movement");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Reconciliation)
            .EvidenceKinds.Should().Contain("bank-cash-evidence");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.CapitalAccounts)
            .EvidenceKinds.Should().Contain("capital-account-subledger");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Ledger)
            .EvidenceKinds.Should().Contain("ledger-impact");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Reporting)
            .EvidenceKinds.Should().Contain("report-output");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Audit)
            .EvidenceKinds.Should().Contain("execution-deferred");

        var validationResponse = await client.PostAsync(
            $"/api/workstation/evidence/subjects/payment-intent/{encodedSubjectId}/validate",
            content: null);
        validationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var completeness = await validationResponse.Content.ReadFromJsonAsync<EvidenceCompletenessDto>(ServerJsonOptions);
        completeness!.Status.Should().Be(EvidenceStatusDto.Ready);

        var exportResponse = await client.PostAsJsonAsync(
            $"/api/workstation/evidence/subjects/payment-intent/{encodedSubjectId}/export-manifest",
            new EvidencePacketExportRequest("controller", "payment intent cash evidence retention", IncludeWarnings: false),
            ServerJsonOptions);
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var export = await exportResponse.Content.ReadFromJsonAsync<EvidencePacketExportResponse>(ServerJsonOptions);
        export.Should().NotBeNull();
        export!.VaultIdentity.Should().NotBeNull();
        export.VaultIdentity!.SubjectKind.Should().Be(EvidenceSubjectResolver.PaymentIntentKind);
        export.VaultIdentity.SubjectId.Should().Be(subjectId);

        var manifestJson = await client.GetStringAsync(export.ManifestRoute);
        manifestJson.Should().Contain("\"subjectKind\": \"payment-intent\"");
        manifestJson.Should().Contain("\"evidenceSubject\": \"payment-intent/payment:fund-alpha:capital-call:20260630\"");
        manifestJson.Should().Contain("\"kind\": \"execution-deferred\"");
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
        var attemptId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var deliveryAttempt = BuildReportPackDeliveryAttempt(reportId, attemptId, DateTimeOffset.UtcNow);
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, reportId.ToString("D"));
        using var provider = new ServiceCollection()
            .AddSingleton<IGovernanceReportPackRepository>(new InMemoryReportPackRepository(BuildReportPackSnapshot(reportId)))
            .AddSingleton<IReportPackDeliveryRecordStore>(new InMemoryReportPackDeliveryRecordStore([deliveryAttempt]))
            .BuildServiceProvider();
        var contributor = new ReportPackEvidenceContributor(provider);

        var contribution = await contributor.ContributeAsync(new EvidenceContributionContext(subject, CancellationToken.None));

        contribution.Nodes.Should().ContainSingle(node =>
            node.Kind == "report-pack" &&
            node.SourceSystem == "report-pack-repository");
        contribution.Nodes.Should().NotContain(node =>
            node.SourceSystem.Contains("Governance", StringComparison.OrdinalIgnoreCase));
        var deliverySubjectId = $"{reportId:D}:{attemptId:D}";
        contribution.Nodes.Should().Contain(node =>
            node.Kind == "delivery-record" &&
            node.Status == EvidenceStatusDto.Ready &&
            node.ArtifactRefs.Any(artifact =>
                artifact.CanonicalSubjectKind == EvidenceSubjectResolver.ReportPackDeliveryKind &&
                artifact.CanonicalSubjectId == deliverySubjectId &&
                artifact.Route!.Contains(Uri.EscapeDataString(deliverySubjectId), StringComparison.OrdinalIgnoreCase)));
        contribution.Nodes.Should().Contain(node =>
            node.Kind == "delivery-evidence-packet" &&
            node.RelatedWorkItemIds.Contains("investor-monthly-statement-202606:1:RunGenerated"));
        contribution.Edges.Should().Contain(edge =>
            edge.Relationship == "delivered-by" &&
            edge.FromId == $"{EvidenceSubjectResolver.ReportPackKind}:{reportId:D}:report-pack");

        var graph = new EvidenceGraphService(
            new EvidenceSubjectResolver(provider),
            new EvidenceTemplateRegistry(),
            [contributor],
            NullLogger<EvidenceGraphService>.Instance);
        var packet = await graph.GetPacketAsync(EvidenceSubjectResolver.ReportPackKind, reportId.ToString("D"));
        packet!.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Delivery)
            .EvidenceKinds.Should().Contain("delivery-record");
    }

    [Fact]
    public async Task ReportPackDeliveryEvidenceContributor_DuringDeliveryReview_BuildsDeliveryAndAuditNodes()
    {
        var reportId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var attemptId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var attempt = BuildReportPackDeliveryAttempt(reportId, attemptId, DateTimeOffset.UtcNow);
        var subjectId = $"{reportId:D}:{attemptId:D}";
        var subject = Subject(EvidenceSubjectResolver.ReportPackDeliveryKind, subjectId);
        using var provider = new ServiceCollection()
            .AddSingleton<IReportPackDeliveryRecordStore>(new InMemoryReportPackDeliveryRecordStore([attempt]))
            .BuildServiceProvider();
        var contributor = new ReportPackDeliveryEvidenceContributor(provider);

        var contribution = await contributor.ContributeAsync(new EvidenceContributionContext(subject, CancellationToken.None));

        contribution.Warnings.Should().BeEmpty();
        contribution.RequiredEvidenceIds.Should().Contain([
            $"{EvidenceSubjectResolver.ReportPackDeliveryKind}:{subjectId}:delivery-record",
            $"{EvidenceSubjectResolver.ReportPackDeliveryKind}:{subjectId}:package",
            $"{EvidenceSubjectResolver.ReportPackDeliveryKind}:{subjectId}:audit-manifest",
            $"{EvidenceSubjectResolver.ReportPackDeliveryKind}:{subjectId}:line-provenance"
        ]);
        contribution.Nodes.Should().Contain(node =>
            node.Kind == "delivery-record" &&
            node.Status == EvidenceStatusDto.Ready);
        contribution.Nodes.Select(static node => node.Kind).Should().Contain(
        [
            "publication-manifest",
            "report-line-provenance",
            "approval-chain",
            "branding-theme",
            "restatement-lineage"
        ]);
        contribution.Nodes.Should().Contain(node =>
            node.Kind == "delivery-artifact" &&
            node.ArtifactRefs.Any(artifact =>
                artifact.CanonicalSubjectKind == EvidenceSubjectResolver.ReportPackDeliveryKind &&
                artifact.CanonicalSubjectId == subjectId &&
                artifact.Route!.Contains("/artifacts/board-pack.pdf", StringComparison.OrdinalIgnoreCase) &&
                artifact.Retained));
        contribution.Nodes.Should().Contain(node =>
            node.Kind == "audit-history" &&
            node.Status == EvidenceStatusDto.Ready &&
            node.RelatedWorkItemIds.Contains("investor-monthly-statement-202606:1:RunGenerated"));
        contribution.Edges.Should().Contain(edge =>
            edge.Relationship == "retained-by" &&
            edge.ToId.EndsWith(":audit-manifest", StringComparison.OrdinalIgnoreCase));
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
        templates.Should().Contain(template =>
            template.WorkflowId == "accounting-records-evidence-review" &&
            template.NoOrphanRule &&
            template.RequiredEvidenceKinds.Contains("accounting-record-category"));

        var packetResponse = await client.GetAsync("/api/workstation/evidence/subjects/report-pack/current/packet");
        packetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var packet = await packetResponse.Content.ReadFromJsonAsync<EvidencePacketDto>(ServerJsonOptions);
        packet.Should().NotBeNull();
        packet!.Subject.SubjectKind.Should().Be(EvidenceSubjectResolver.ReportPackKind);
        packet.Nodes.Should().Contain(node => node.Kind == "analysis-export");
        packet.ProofChain.TotalLayerCount.Should().Be(9);
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Reporting)
            .EvidenceKinds.Should().Contain("analysis-export");
        packet.Warnings.Should().Contain(warning => warning.Contains("report-pack repository is not registered", StringComparison.OrdinalIgnoreCase));
        packet.Warnings.Should().NotContain(warning => warning.Contains("Governance report-pack repository", StringComparison.OrdinalIgnoreCase));

        var graphResponse = await client.GetAsync("/api/workstation/evidence/subjects/report-pack/current/graph");
        graphResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var graph = await graphResponse.Content.ReadFromJsonAsync<EvidenceGraphDto>(ServerJsonOptions);
        graph!.Nodes.Should().Contain(node => node.EvidenceId == "report-pack:current:analysis-export");
        graph.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Reporting)
            .EvidenceIds.Should().Contain("report-pack:current:analysis-export");

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
    public async Task MapEvidenceEndpoints_DuringReportPackDeliveryReview_ReturnsPacketValidationAndManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-report-pack-delivery", Guid.NewGuid().ToString("N"));
        var reportId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var attemptId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var attempt = BuildReportPackDeliveryAttempt(reportId, attemptId, DateTimeOffset.UtcNow);
        var subjectId = $"{reportId:D}:{attemptId:D}";
        var encodedSubjectId = Uri.EscapeDataString(subjectId);
        await using var app = await CreateEvidenceAppAsync(
            root,
            deliveryRecordStore: new InMemoryReportPackDeliveryRecordStore([attempt]));
        var client = app.GetTestClient();

        var templatesResponse = await client.GetAsync("/api/workstation/evidence/templates");
        templatesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var templates = await templatesResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceTemplateDto>>(ServerJsonOptions);
        templates.Should().Contain(template =>
            template.WorkflowId == "report-pack-delivery-review" &&
            template.RequiredEvidenceKinds.Contains("delivery-record") &&
            template.RequiredEvidenceKinds.Contains("audit-history") &&
            template.OptionalEvidenceKinds.Contains("report-line-provenance"));

        var subjectsResponse = await client.GetAsync("/api/workstation/evidence/subjects");
        subjectsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var subjects = await subjectsResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceSubjectDto>>(ServerJsonOptions);
        subjects.Should().Contain(subject =>
            subject.SubjectKind == EvidenceSubjectResolver.ReportPackDeliveryKind &&
            subject.SubjectId == subjectId &&
            subject.Route!.Contains($"deliveryAttemptId={attemptId:D}", StringComparison.OrdinalIgnoreCase));

        var packetResponse = await client.GetAsync($"/api/workstation/evidence/subjects/report-pack-delivery/{encodedSubjectId}/packet");
        packetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var packet = await packetResponse.Content.ReadFromJsonAsync<EvidencePacketDto>(ServerJsonOptions);
        packet.Should().NotBeNull();
        packet!.Subject.SubjectKind.Should().Be(EvidenceSubjectResolver.ReportPackDeliveryKind);
        var deliveryPackageNode = packet.Nodes.Should()
            .ContainSingle(node => node.Kind == "delivery-package")
            .Which;
        deliveryPackageNode.Metadata.Should().Contain("reportPackDeliveryAttemptId", attemptId.ToString("D"));
        deliveryPackageNode.Metadata.Should().Contain("reportPackDeliveryPackageId", "pkg-board-1");
        packet.Nodes.Should().Contain(node => node.Kind == "delivery-evidence-packet");
        packet.Nodes.Should().Contain(node => node.Kind == "audit-history");
        packet.Nodes.Should().Contain(node => node.Kind == "publication-manifest");
        packet.Nodes.Should().Contain(node => node.Kind == "report-line-provenance");
        packet.Nodes.Should().Contain(node => node.Kind == "branding-theme");
        packet.Nodes.Should().Contain(node => node.Kind == "restatement-lineage");
        packet.Completeness.ValidationIssues.Should().NotContain(issue => issue.Code == "orphan-evidence");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Delivery)
            .EvidenceKinds.Should().Contain("delivery-evidence-packet");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Reporting)
            .EvidenceKinds.Should().Contain("report-line-provenance");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Audit)
            .EvidenceKinds.Should().Contain("audit-history");

        var validationResponse = await client.PostAsync(
            $"/api/workstation/evidence/subjects/report-pack-delivery/{encodedSubjectId}/validate",
            content: null);
        validationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var completeness = await validationResponse.Content.ReadFromJsonAsync<EvidenceCompletenessDto>(ServerJsonOptions);
        completeness!.Status.Should().Be(EvidenceStatusDto.Ready);
        completeness.ReadyIds.Should().Contain(id => id.EndsWith(":audit-manifest", StringComparison.OrdinalIgnoreCase));

        var exportResponse = await client.PostAsJsonAsync(
            $"/api/workstation/evidence/subjects/report-pack-delivery/{encodedSubjectId}/export-manifest",
            new EvidencePacketExportRequest("controller", "delivery record retention", IncludeWarnings: false),
            ServerJsonOptions);
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var export = await exportResponse.Content.ReadFromJsonAsync<EvidencePacketExportResponse>(ServerJsonOptions);
        export.Should().NotBeNull();
        export!.VaultIdentity.Should().NotBeNull();
        export.VaultIdentity!.SubjectKind.Should().Be(EvidenceSubjectResolver.ReportPackDeliveryKind);
        export.VaultIdentity.SubjectId.Should().Be(subjectId);

        var manifestJson = await client.GetStringAsync(export.ManifestRoute);
        manifestJson.Should().Contain("\"subjectKind\": \"report-pack-delivery\"");
        manifestJson.Should().Contain($"\"evidenceSubject\": \"{EvidenceSubjectResolver.ReportPackDeliveryKind}/{subjectId}\"");
        manifestJson.Should().Contain($"\"reportPackDeliveryAttemptId\": \"{attemptId:D}\"");
        manifestJson.Should().Contain("\"reportPackDeliveryPackageId\": \"pkg-board-1\"");

        var vaultSearchResponse = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/search",
            new EvidenceVaultLookupRequestDto(
                null,
                null,
                null,
                null,
                null,
                ReportPackDeliveryAttemptId: attemptId.ToString("D")),
            ServerJsonOptions);
        vaultSearchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var vaultMatches = await vaultSearchResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceVaultIdentityDto>>(ServerJsonOptions);
        vaultMatches.Should().ContainSingle(match =>
            match.SubjectKind == EvidenceSubjectResolver.ReportPackDeliveryKind &&
            match.SubjectId == subjectId &&
            match.VaultId == export.VaultIdentity.VaultId);

        var packageSearchResponse = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/search",
            new EvidenceVaultLookupRequestDto(
                null,
                null,
                null,
                null,
                null,
                ReportPackDeliveryPackageId: "pkg-board-1"),
            ServerJsonOptions);
        packageSearchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var packageMatches = await packageSearchResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceVaultIdentityDto>>(ServerJsonOptions);
        packageMatches.Should().ContainSingle(match =>
            match.SubjectKind == EvidenceSubjectResolver.ReportPackDeliveryKind &&
            match.SubjectId == subjectId &&
            match.VaultId == export.VaultIdentity.VaultId);
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
        packet.Completeness.ValidationIssues.Should().NotContain(issue => issue.Code == "orphan-evidence");
        packet.ProofChain.Layers.Single(layer => layer.Layer == EvidenceProofChainLayerKindDto.Delivery)
            .EvidenceKinds.Should().Contain("accounting-record-category");

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
    public async Task FileEvidenceArtifactStore_DuringReportPackDeliveryExport_UsesTypedPackageMetadataForVaultLinkage()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var reportId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var attemptId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var subjectId = $"{reportId:D}:{attemptId:D}";
        var subject = Subject(EvidenceSubjectResolver.ReportPackDeliveryKind, subjectId);
        var deliveryPackage = Node(
            subject,
            $"{EvidenceSubjectResolver.ReportPackDeliveryKind}:{subjectId}:delivery-package",
            "delivery-package",
            EvidenceStatusDto.Ready) with
        {
            Summary = "Delivery package metadata is retained without embedding the package id in prose.",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["reportPackDeliveryAttemptId"] = attemptId.ToString("D"),
                ["reportPackDeliveryPackageId"] = "pkg-metadata-only"
            }
        };
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [deliveryPackage],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(100, EvidenceStatusDto.Ready, [deliveryPackage.EvidenceId], [deliveryPackage.EvidenceId], [], [], []),
            Actions: [],
            Warnings: []);

        var response = await store.WriteManifestAsync(packet, new EvidencePacketExportRequest("controller", "metadata linkage"));

        var manifestPath = Path.Combine(root, response.ManifestPath.Replace('/', Path.DirectorySeparatorChar));
        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        manifestJson.Should().Contain("\"reportPackDeliveryAttemptId\": \"22222222-2222-2222-2222-222222222222\"");
        manifestJson.Should().Contain("\"reportPackDeliveryPackageId\": \"pkg-metadata-only\"");
        var matches = await store.FindByLinkageAsync(new EvidenceVaultLookupRequestDto(
            null,
            null,
            null,
            null,
            null,
            ReportPackDeliveryPackageId: "pkg-metadata-only"));
        matches.Should().ContainSingle(match =>
            match.SubjectKind == EvidenceSubjectResolver.ReportPackDeliveryKind &&
            match.SubjectId == subjectId &&
            match.VaultId == response.VaultIdentity!.VaultId);
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringManifestExport_FreezesSupportRequestListForMissingAndBlockedEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "close-2026-05");
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes:
            [
                Node(subject, "source-node", "source-document", EvidenceStatusDto.Ready),
                Node(
                    subject,
                    "audit-support",
                    "audit-history",
                    EvidenceStatusDto.Missing,
                    workItemIds: ["audit-request:close-2026-05"])
            ],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(
                50,
                EvidenceStatusDto.Blocked,
                ["source-node", "audit-support"],
                ["source-node"],
                ["audit-support"],
                [],
                ["audit-request:close-2026-05"])
            {
                ValidationIssues =
                [
                    new EvidenceValidationIssueDto(
                        Code: "missing-required-evidence",
                        Severity: EvidenceValidationSeverityDto.Critical,
                        Message: "Audit support package is missing.",
                        EvidenceId: "audit-support",
                        EvidenceKind: "audit-history",
                        SourceSystem: "test")
                ]
            },
            Actions: [],
            Warnings: []);

        var response = await store.WriteManifestAsync(packet, new EvidencePacketExportRequest("operator", "support package freeze"));

        response.VaultIdentity.Should().NotBeNull();
        var requestLists = response.VaultIdentity!.RequestLists;
        requestLists.Should().ContainSingle(list =>
            list.RequestListKind == "AuditRequestList" &&
            list.RequestListKindCode == EvidenceRequestListKindDto.Audit &&
            list.TargetKind == "audit" &&
            list.TargetId == "close-2026-05" &&
            list.HighestSeverity == EvidenceValidationSeverityDto.Critical &&
            list.Status == "Open" &&
            list.RequestCount == 2 &&
            list.RequestIds.SequenceEqual(
                new[]
                {
                    "support-request:blockedworkitem:audit-support:audit-request-close-2026-05",
                    "support-request:missingevidence:audit-support"
                }) &&
            list.EvidenceKinds.SequenceEqual(new[] { "audit-history" }) &&
            list.BlockedOutputs.SequenceEqual(new[] { "report-pack/close-2026-05" }) &&
            list.Summary == "audit/close-2026-05 has 2 frozen requests; 2 open requests remain.");
        var supportRequests = response.VaultIdentity!.SupportRequests;
        supportRequests.Should().HaveCount(2);
        supportRequests.Should().ContainSingle(request =>
            request.RequestKind == "MissingEvidence" &&
            request.EvidenceId == "audit-support" &&
            request.EvidenceKind == "audit-history" &&
            request.Severity == EvidenceValidationSeverityDto.Critical &&
            request.Status == "Open" &&
            request.Summary == "Audit support package is missing." &&
            request.BlockedOutput == "report-pack/close-2026-05");
        supportRequests.Should().ContainSingle(request =>
            request.RequestKind == "BlockedWorkItem" &&
            request.EvidenceId == "audit-support" &&
            request.WorkItemId == "audit-request:close-2026-05" &&
            request.BlockedOutput == "report-pack/close-2026-05");
        response.VaultIdentity.ManifestSnapshot.Should().NotBeNull();
        response.VaultIdentity.ManifestSnapshot!.PackageKind.Should().Be(EvidenceSubjectResolver.ReportPackKind);
        response.VaultIdentity.ManifestSnapshot.PackageKindCode.Should().Be(EvidenceManifestPackageKindDto.AuditPacket);
        response.VaultIdentity.ManifestSnapshot.PackageId.Should().Be("close-2026-05");
        response.VaultIdentity.ManifestSnapshot.ContentHashSha256.Should().Be(response.VaultIdentity.ContentHashSha256);
        response.VaultIdentity.ManifestSnapshot.Requests.Should().HaveCount(2);
        response.VaultIdentity.ManifestSnapshot.Requests.Should().ContainSingle(request =>
            request.RequestKind == "MissingEvidence" &&
            request.TargetKind == "audit" &&
            request.TargetId == "close-2026-05");

        var manifestPath = Path.Combine(root, response.ManifestPath.Replace('/', Path.DirectorySeparatorChar));
        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        manifestJson.Should().Contain("\"requestLists\": [");
        manifestJson.Should().Contain("\"requestListKind\": \"AuditRequestList\"");
        manifestJson.Should().Contain("\"supportRequests\": [");
        manifestJson.Should().Contain("\"requestKind\": \"MissingEvidence\"");
        manifestJson.Should().Contain("\"workItemId\": \"audit-request:close-2026-05\"");

        var indexPath = Path.Combine(root, "workstation", "evidence", "_vault", $"{response.VaultIdentity.VaultId}.json");
        var indexJson = await File.ReadAllTextAsync(indexPath);
        var indexedIdentity = JsonSerializer.Deserialize<EvidenceVaultIdentityDto>(indexJson, ServerJsonOptions);
        indexedIdentity.Should().NotBeNull();
        indexedIdentity!.RequestLists.Should().BeEquivalentTo(
            response.VaultIdentity.RequestLists,
            options => options.WithStrictOrdering());
        indexedIdentity!.SupportRequests.Should().BeEquivalentTo(
            response.VaultIdentity.SupportRequests,
            options => options.WithStrictOrdering());
        indexedIdentity.ManifestSnapshot.Should().BeEquivalentTo(response.VaultIdentity.ManifestSnapshot);

        var requestListIndex = await store.ListRequestListsAsync(new EvidenceVaultRequestListQueryDto(
            RequestListKind: "AuditRequestList",
            TargetKind: "audit",
            TargetId: "close-2026-05",
            Status: "Open"));
        var indexedRequestList = requestListIndex.Should().ContainSingle().Subject;
        indexedRequestList.VaultId.Should().Be(response.VaultIdentity.VaultId);
        indexedRequestList.RequestListKindCode.Should().Be(EvidenceRequestListKindDto.Audit);
        indexedRequestList.ManifestRoute.Should().Be(response.VaultIdentity.ManifestRoute);
        indexedRequestList.OpenRequestCount.Should().Be(2);
        indexedRequestList.SupportRequests.Should().HaveCount(2);
        indexedRequestList.SupportRequests.Should().Contain(request => request.RequestKind == "MissingEvidence");
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringManifestExport_FreezesTypedSupportRequestListsForCloseAuditTaxReportAndEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "close-2026-06");
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes:
            [
                Node(subject, "close-nav-support", "close-support", EvidenceStatusDto.Missing),
                Node(subject, "audit-confirmation", "audit-support", EvidenceStatusDto.Missing),
                Node(subject, "tax-k1-support", "tax-support", EvidenceStatusDto.Missing),
                Node(subject, "report-package-support", "report-support", EvidenceStatusDto.Missing),
                Node(subject, "capital-call-event-support", "fund-event-support", EvidenceStatusDto.Missing)
            ],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(
                0,
                EvidenceStatusDto.Blocked,
                ["close-nav-support", "audit-confirmation", "tax-k1-support", "report-package-support", "capital-call-event-support"],
                [],
                ["close-nav-support", "audit-confirmation", "tax-k1-support", "report-package-support", "capital-call-event-support"],
                [],
                [])
            {
                ValidationIssues =
                [
                    new EvidenceValidationIssueDto(
                        Code: "missing-required-evidence",
                        Severity: EvidenceValidationSeverityDto.Critical,
                        Message: "Close NAV support is missing.",
                        EvidenceId: "close-nav-support",
                        EvidenceKind: "close-support",
                        SourceSystem: "test"),
                    new EvidenceValidationIssueDto(
                        Code: "missing-required-evidence",
                        Severity: EvidenceValidationSeverityDto.Critical,
                        Message: "Audit confirmation support is missing.",
                        EvidenceId: "audit-confirmation",
                        EvidenceKind: "audit-support",
                        SourceSystem: "test"),
                    new EvidenceValidationIssueDto(
                        Code: "missing-required-evidence",
                        Severity: EvidenceValidationSeverityDto.Critical,
                        Message: "Tax K-1 support is missing.",
                        EvidenceId: "tax-k1-support",
                        EvidenceKind: "tax-support",
                        SourceSystem: "test"),
                    new EvidenceValidationIssueDto(
                        Code: "missing-required-evidence",
                        Severity: EvidenceValidationSeverityDto.Critical,
                        Message: "Report-package support is missing.",
                        EvidenceId: "report-package-support",
                        EvidenceKind: "report-support",
                        SourceSystem: "test"),
                    new EvidenceValidationIssueDto(
                        Code: "missing-required-evidence",
                        Severity: EvidenceValidationSeverityDto.Critical,
                        Message: "Capital-call fund-event support is missing.",
                        EvidenceId: "capital-call-event-support",
                        EvidenceKind: "fund-event-support",
                        SourceSystem: "test")
                ]
            },
            Actions: [],
            Warnings: []);

        var response = await store.WriteManifestAsync(packet, new EvidencePacketExportRequest("controller", "typed request-list freeze"));

        response.VaultIdentity.Should().NotBeNull();
        var requestLists = response.VaultIdentity!.RequestLists;
        requestLists.Should().HaveCount(5);
        requestLists.Should().ContainSingle(list =>
            list.RequestListKind == "EventRequestList" &&
            list.RequestListKindCode == EvidenceRequestListKindDto.OperationalEvent &&
            list.TargetKind == "event");
        requestLists.Should().ContainSingle(list =>
            list.RequestListKind == "CloseRequestList" &&
            list.RequestListKindCode == EvidenceRequestListKindDto.Close &&
            list.TargetKind == "close");
        requestLists.Should().ContainSingle(list =>
            list.RequestListKind == "AuditRequestList" &&
            list.RequestListKindCode == EvidenceRequestListKindDto.Audit &&
            list.TargetKind == "audit");
        requestLists.Should().ContainSingle(list =>
            list.RequestListKind == "TaxRequestList" &&
            list.RequestListKindCode == EvidenceRequestListKindDto.Tax &&
            list.TargetKind == "tax");
        requestLists.Should().ContainSingle(list =>
            list.RequestListKind == "ReportPackageRequestList" &&
            list.RequestListKindCode == EvidenceRequestListKindDto.ReportPackage &&
            list.TargetKind == "report-package");
        response.VaultIdentity.ManifestSnapshot.Should().NotBeNull();
        response.VaultIdentity.ManifestSnapshot!.PackageKindCode.Should().Be(EvidenceManifestPackageKindDto.CloseBinder);
        response.VaultIdentity.ManifestSnapshot!.Requests.Should().HaveCount(5);

        var eventLists = await store.ListRequestListsAsync(new EvidenceVaultRequestListQueryDto(
            RequestListKindCode: EvidenceRequestListKindDto.OperationalEvent,
            Status: "Open"));
        eventLists.Should().ContainSingle(entry =>
            entry.VaultId == response.VaultIdentity.VaultId &&
            entry.RequestListKindCode == EvidenceRequestListKindDto.OperationalEvent &&
            entry.SupportRequests.Any(request => request.EvidenceId == "capital-call-event-support"));
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringManifestExport_StampsReportSupportPackageManifestFamily()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-store", Guid.NewGuid().ToString("N"));
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "board-report-2026-06");
        var packet = new EvidencePacketDto(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes: [Node(subject, "report-package-support", "report-support", EvidenceStatusDto.Missing)],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(
                0,
                EvidenceStatusDto.Blocked,
                ["report-package-support"],
                [],
                ["report-package-support"],
                [],
                [])
            {
                ValidationIssues =
                [
                    new EvidenceValidationIssueDto(
                        Code: "missing-required-evidence",
                        Severity: EvidenceValidationSeverityDto.Critical,
                        Message: "Report-package support is missing.",
                        EvidenceId: "report-package-support",
                        EvidenceKind: "report-support",
                        SourceSystem: "test")
                ]
            },
            Actions: [],
            Warnings: []);

        var response = await store.WriteManifestAsync(packet, new EvidencePacketExportRequest("controller", "report support package"));

        response.VaultIdentity.Should().NotBeNull();
        response.VaultIdentity!.ManifestSnapshot.Should().NotBeNull();
        response.VaultIdentity.ManifestSnapshot!.PackageKindCode.Should().Be(EvidenceManifestPackageKindDto.ReportSupportPackage);
        response.VaultIdentity.RequestLists.Should().ContainSingle(list =>
            list.RequestListKindCode == EvidenceRequestListKindDto.ReportPackage &&
            list.TargetKind == "report-package");
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
            CanonicalSubjectId: "close-2026-05")
        {
            Capture = new EvidenceArtifactCaptureDto(
                "Upload",
                "Evidence Vault upload",
                new DateTimeOffset(2026, 5, 28, 14, 30, 0, TimeSpan.Zero),
                "ops-user",
                "portal-upload:broker-statement:close-2026-05",
                statementHash),
            ExtractedFields =
            [
                new EvidenceArtifactExtractionFieldDto(
                    "cashAmount",
                    "0",
                    "0",
                    0.98m,
                    "Reviewed",
                    EvidenceStatusDto.Ready,
                    "Extracted cash amount tied to the expected normalized cash record.",
                    "reconciliation-case",
                    "case:close-2026-05:cash"),
                new EvidenceArtifactExtractionFieldDto(
                    "tradeDate",
                    "2026-05-28",
                    "2026-05-28",
                    0.95m,
                    "Reviewed",
                    EvidenceStatusDto.Ready,
                    "Extracted trade date matched the expected source record.",
                    "report-line",
                    "report-line:close-2026-05:statement")
            ]
        };
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
        retained.Capture.Should().NotBeNull();
        retained.Capture!.CaptureChannel.Should().Be("Upload");
        retained.Capture.SourceReference.Should().Be("portal-upload:broker-statement:close-2026-05");
        retained.ExtractedFields.Should().HaveCount(2);
        retained.ExtractedFields.Should().ContainSingle(field =>
            field.FieldName == "cashAmount" &&
            field.ExtractedValue == "0" &&
            field.ExpectedValue == "0" &&
            field.ConfidenceScore == 0.98m &&
            field.ReviewState == "Reviewed" &&
            field.ValidationStatus == EvidenceStatusDto.Ready &&
            field.LinkedRecordKind == "reconciliation-case" &&
            field.LinkedRecordId == "case:close-2026-05:cash");
        retained.ExtractedFields.Should().ContainSingle(field =>
            field.FieldName == "tradeDate" &&
            field.LinkedRecordKind == "report-line" &&
            field.LinkedRecordId == "report-line:close-2026-05:statement");
        var retainedPath = Path.Combine(root, retained.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(retainedPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(retainedPath)).Should().Equal(statementBytes);

        var manifestPath = Path.Combine(root, response.ManifestPath.Replace('/', Path.DirectorySeparatorChar));
        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        manifestJson.Should().Contain("\"storageKind\": \"file-bundle\"");
        manifestJson.Should().Contain("\"artifacts\": [");
        manifestJson.Should().Contain("\"relativePath\": \"workstation/evidence/_vault/");
        manifestJson.Should().Contain("\"captureChannel\": \"Upload\"");
        manifestJson.Should().Contain("\"confidenceScore\": 0.98");
        manifestJson.Should().Contain("\"expectedValue\": \"0\"");
        manifestJson.Should().Contain("\"linkedRecordKind\": \"report-line\"");

        var indexPath = Path.Combine(root, "workstation", "evidence", "_vault", $"{response.VaultIdentity.VaultId}.json");
        var indexJson = await File.ReadAllTextAsync(indexPath);
        var indexedIdentity = JsonSerializer.Deserialize<EvidenceVaultIdentityDto>(indexJson, ServerJsonOptions);
        indexedIdentity.Should().NotBeNull();
        var indexedArtifact = indexedIdentity!.Artifacts.Should().ContainSingle().Which;
        indexedArtifact.Capture.Should().BeEquivalentTo(retained.Capture);
        indexedArtifact.ExtractedFields.Should().BeEquivalentTo(
            retained.ExtractedFields,
            options => options.WithStrictOrdering());
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
    public async Task EvidenceEndpoints_VaultDocuments_QuerySupportsAdminAndTaxAuditSupportVocabulary()
    {
        var root = Path.Combine(Path.GetTempPath(), $"evidence-vault-support-vocabulary-{Guid.NewGuid():N}");
        await using var app = await CreateEvidenceAppAsync(root);
        var client = app.GetTestClient();
        var bytes = Encoding.UTF8.GetBytes("support_id,status\r\naudit-2026-06,missing-admin-confirmation\r\n");

        var response = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/intake",
            new EvidenceVaultIntakeRequestDto(
                SubjectKind: EvidenceSubjectResolver.ReportPackKind,
                SubjectId: "close-audit-2026-06",
                IntakeChannel: "upload",
                FileName: "admin-tax-audit-support.csv",
                ContentBase64: Convert.ToBase64String(bytes),
                ContentType: "text/csv",
                SourceSystem: "fund-admin-portal",
                SourceReference: "portal://fund-admin/close-audit-2026-06",
                ReceivedBy: "controller")
            {
                Classification = EvidenceDocumentClassificationDto.TaxAuditSupport,
                IntakeChannelKind = EvidenceDocumentIntakeChannelDto.Upload,
                ExtractionStatus = EvidenceExtractionStatusDto.Pending,
                TenantId = "tenant-alpha",
                Scope = "fund-alpha",
                ObjectLinks =
                [
                    new EvidenceDocumentLinkDto(EvidenceDocumentLinkKindDto.Fund, "fund-alpha", "Fund Alpha"),
                    new EvidenceDocumentLinkDto(EvidenceDocumentLinkKindDto.ReportLine, "report-line:tax-support", "Tax support line")
                ]
            },
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var documents = await client.GetFromJsonAsync<IReadOnlyList<EvidenceVaultDocumentEntryDto>>(
            "/api/workstation/evidence/vault/documents?classification=TaxAuditSupport&extractionStatus=Pending&linkKind=Fund&objectId=fund-alpha&tenantId=tenant-alpha&scope=fund-alpha",
            ServerJsonOptions);
        var entry = documents.Should().ContainSingle().Subject;
        entry.Document.Classification.Should().Be(EvidenceDocumentClassificationDto.TaxAuditSupport);
        entry.Document.ChannelKind.Should().Be(EvidenceDocumentIntakeChannelDto.Upload);
        entry.Document.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.Fund &&
            link.ObjectId == "fund-alpha");
        entry.Document.SourceSystem.Should().Be("fund-admin-portal");

        var adminPackageResponse = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/intake",
            new EvidenceVaultIntakeRequestDto(
                SubjectKind: EvidenceSubjectResolver.ReportPackKind,
                SubjectId: "admin-package-2026-06",
                IntakeChannel: "portal-download",
                FileName: "admin-package.csv",
                ContentBase64: Convert.ToBase64String(bytes),
                ContentType: "text/csv",
                ReceivedBy: "controller")
            {
                Classification = EvidenceDocumentClassificationDto.AdminPackage,
                IntakeChannelKind = EvidenceDocumentIntakeChannelDto.PortalDownload,
                TenantId = "tenant-alpha",
                Scope = "fund-alpha"
            },
            ServerJsonOptions);
        adminPackageResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var adminDocuments = await client.GetFromJsonAsync<IReadOnlyList<EvidenceVaultDocumentEntryDto>>(
            "/api/workstation/evidence/vault/documents?classification=AdminPackage&channelKind=PortalDownload&tenantId=tenant-alpha&scope=fund-alpha",
            ServerJsonOptions);
        adminDocuments.Should().ContainSingle(entry =>
            entry.Document.Classification == EvidenceDocumentClassificationDto.AdminPackage &&
            entry.Document.ChannelKind == EvidenceDocumentIntakeChannelDto.PortalDownload);
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringVaultApiIntake_RetainsCapturedDocumentWithReviewMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"evidence-vault-intake-store-{Guid.NewGuid():N}");
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var subjectId = "payment:fund-alpha:capital-call:20260630";
        var bytes = Encoding.UTF8.GetBytes("settlement_id,amount,currency\r\nsettle-001,1250.00,USD\r\n");
        var expectedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var response = await store.WriteIntakeArtifactAsync(new EvidenceVaultIntakeRequestDto(
            SubjectKind: EvidenceSubjectResolver.PaymentIntentKind,
            SubjectId: subjectId,
            IntakeChannel: "api",
            FileName: "settlement-proof.csv",
            ContentBase64: Convert.ToBase64String(bytes),
            ContentType: "text/csv",
            SourceSystem: "bank-api",
            SourceReference: "api://bank/settlements/settle-001",
            ReceivedBy: "fund-controller",
            ExpectedContentHashSha256: $"sha256:{expectedHash}",
            ExtractedFields:
            [
                new EvidenceArtifactExtractionFieldDto(
                    FieldName: "amount",
                    ExtractedValue: "1250.00",
                    ExpectedValue: "1250.00",
                    ConfidenceScore: 0.99m,
                    ReviewState: "Ready",
                    ValidationStatus: EvidenceStatusDto.Ready,
                    ValidationMessage: null,
                    LinkedRecordKind: "payment-intent",
                    LinkedRecordId: subjectId),
                new EvidenceArtifactExtractionFieldDto(
                    FieldName: "auditSupportReference",
                    ExtractedValue: null,
                    ExpectedValue: "audit-support:capital-call-20260630",
                    ConfidenceScore: 0.42m,
                    ReviewState: "Missing",
                    ValidationStatus: EvidenceStatusDto.Missing,
                    ValidationMessage: "Audit support reference is missing from the Evidence Vault intake.",
                    LinkedRecordKind: "payment-intent",
                    LinkedRecordId: subjectId)
            ],
            Linkage: new EvidenceSubjectLinkageDto(
                $"payment-intent/{subjectId}",
                null,
                "2026-06",
                null,
                "recon-case-77"))
        {
            Classification = EvidenceDocumentClassificationDto.BankStatement,
            IntakeChannelKind = EvidenceDocumentIntakeChannelDto.Api,
            TenantId = "tenant-alpha",
            Scope = "fund-alpha",
            ObjectLinks =
            [
                new EvidenceDocumentLinkDto(
                    EvidenceDocumentLinkKindDto.Fund,
                    "fund-alpha",
                    "Fund Alpha",
                    "/workstation/accounting/funds/fund-alpha",
                    "scope"),
                new EvidenceDocumentLinkDto(
                    EvidenceDocumentLinkKindDto.CloseTask,
                    "close-task:cash-support",
                    "Cash support",
                    "/workstation/accounting/close/tasks/cash-support",
                    "blocks-close-readiness")
            ],
            ReviewerState = new EvidenceDocumentReviewStateDto(
                EvidenceDocumentReviewStatusDto.NeedsReview,
                "fund-controller",
                null,
                "Audit support reference still required.")
        });

        response.SubjectKind.Should().Be(EvidenceSubjectResolver.PaymentIntentKind);
        response.SubjectId.Should().Be(subjectId);
        response.ContentHashSha256.Should().Be(expectedHash);
        response.Capture.CaptureChannel.Should().Be("api");
        response.Capture.ChannelKind.Should().Be(EvidenceDocumentIntakeChannelDto.Api);
        response.Capture.SourceSystem.Should().Be("bank-api");
        response.ExtractedFields.Should().Contain(field =>
            field.FieldName == "amount" &&
            field.LinkedRecordId == subjectId &&
            field.ValidationStatus == EvidenceStatusDto.Ready);
        response.VaultIdentity.SupportRequests.Should().ContainSingle(request =>
            request.RequestKind == "ValidationIssue" &&
            request.EvidenceKind == "vault-intake" &&
            request.Severity == EvidenceValidationSeverityDto.Critical &&
            request.BlockedOutput == $"payment-intent/{subjectId}" &&
            request.Summary.Contains("Audit support reference is missing", StringComparison.Ordinal));
        response.VaultIdentity.RequestLists.Should().ContainSingle(requestList =>
            requestList.RequestListKind == "AuditRequestList" &&
            requestList.Status == "Open" &&
            requestList.RequestCount == 1);

        var retainedPath = Path.Combine(root, response.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(retainedPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(retainedPath)).Should().Equal(bytes);

        var identity = await store.TryGetVaultIdentityAsync(response.VaultIdentity.VaultId);
        identity.Should().NotBeNull();
        var artifact = identity!.Artifacts.Should().ContainSingle().Subject;
        artifact.ContentHashSha256.Should().Be(expectedHash);
        artifact.Capture!.ReceivedBy.Should().Be("fund-controller");
        artifact.ExtractedFields.Should().Contain(field => field.FieldName == "amount");
        artifact.Document.Should().NotBeNull();
        var retainedDocument = artifact.Document!;
        retainedDocument.ExtractedFields.Should().Contain(field =>
            field.FieldName == "amount" &&
            field.ExtractedValue == "1250.00" &&
            field.ValidationStatus == EvidenceStatusDto.Ready);
        retainedDocument.ExtractedFields.Should().Contain(field =>
            field.FieldName == "auditSupportReference" &&
            field.ValidationStatus == EvidenceStatusDto.Missing);
        retainedDocument.Classification.Should().Be(EvidenceDocumentClassificationDto.BankStatement);
        retainedDocument.SourceHashSha256.Should().Be(expectedHash);
        retainedDocument.SourceChannel.Should().Be("api");
        retainedDocument.ChannelKind.Should().Be(EvidenceDocumentIntakeChannelDto.Api);
        retainedDocument.Actor.Should().Be("fund-controller");
        retainedDocument.TenantId.Should().Be("tenant-alpha");
        retainedDocument.Scope.Should().Be("fund-alpha");
        retainedDocument.SourceRecord.Should().NotBeNull();
        retainedDocument.SourceRecord!.SourceHashSha256.Should().Be(expectedHash);
        retainedDocument.SourceRecord.ReceiptHash.Should().Be(expectedHash);
        retainedDocument.SourceRecord.ReceivedAt.Should().Be(retainedDocument.ReceivedAt);
        retainedDocument.SourceRecord.SourceChannel.Should().Be("api");
        retainedDocument.SourceRecord.ChannelKind.Should().Be(EvidenceDocumentIntakeChannelDto.Api);
        retainedDocument.SourceRecord.Actor.Should().Be("fund-controller");
        retainedDocument.SourceRecord.TenantId.Should().Be("tenant-alpha");
        retainedDocument.SourceRecord.Scope.Should().Be("fund-alpha");
        retainedDocument.SourceRecord.SourceSystem.Should().Be("bank-api");
        retainedDocument.SourceRecord.SourceReference.Should().Be("api://bank/settlements/settle-001");
        retainedDocument.Authority.CanSupport.Should().BeTrue();
        retainedDocument.Authority.CanBlock.Should().BeTrue();
        retainedDocument.Authority.CanSuggest.Should().BeTrue();
        retainedDocument.Authority.CanLink.Should().BeTrue();
        retainedDocument.Authority.CanApprove.Should().BeFalse();
        retainedDocument.Authority.CanPost.Should().BeFalse();
        retainedDocument.Authority.CanCertify.Should().BeFalse();
        retainedDocument.Authority.CanRelease.Should().BeFalse();
        retainedDocument.ExtractionStatus.Should().Be(EvidenceExtractionStatusDto.NeedsReview);
        retainedDocument.ReviewerState.Status.Should().Be(EvidenceDocumentReviewStatusDto.NeedsReview);
        retainedDocument.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.Fund &&
            link.ObjectId == "fund-alpha");
        retainedDocument.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.CloseTask &&
            link.ObjectId == "close-task:cash-support");
        retainedDocument.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.Period &&
            link.ObjectId == "2026-06");
        retainedDocument.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.ReconciliationCase &&
            link.ObjectId == "recon-case-77");
        retainedDocument.AuditTrail.Should().ContainSingle(evt =>
            evt.Action == "DocumentIntakeRetained" &&
            evt.Actor == "fund-controller");
        response.Document.Should().BeEquivalentTo(retainedDocument);
        identity.Documents.Should().ContainSingle(document => document.DocumentId == retainedDocument.DocumentId);
        identity.RequestLists.Should().BeEquivalentTo(response.VaultIdentity.RequestLists);
        identity.SupportRequests.Should().BeEquivalentTo(response.VaultIdentity.SupportRequests);
        identity.ManifestSnapshot.Should().NotBeNull();
        identity.ManifestSnapshot!.PackageKind.Should().Be(EvidenceSubjectResolver.PaymentIntentKind);
        identity.ManifestSnapshot.PackageId.Should().Be(subjectId);
        identity.ManifestSnapshot.Documents.Should().ContainSingle(document =>
            document.DocumentId == retainedDocument.DocumentId &&
            document.SourceHashSha256 == expectedHash &&
            document.SourceRecord != null &&
            document.SourceRecord.SourceHashSha256 == expectedHash &&
            document.SourceRecord.ChannelKind == EvidenceDocumentIntakeChannelDto.Api &&
            document.SourceRecord.Actor == "fund-controller" &&
            document.SourceRecord.TenantId == "tenant-alpha" &&
            document.SourceRecord.Scope == "fund-alpha" &&
            document.ExtractedFields.Any(field =>
                field.FieldName == "amount" &&
                field.ExtractedValue == "1250.00") &&
            document.Authority.CanSupport &&
            !document.Authority.CanApprove &&
            !document.Authority.CanPost &&
            !document.Authority.CanCertify &&
            !document.Authority.CanRelease);
        identity.ManifestSnapshot.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.CloseTask &&
            link.ObjectId == "close-task:cash-support");
        identity.ManifestSnapshot.Requests.Should().ContainSingle(request =>
            request.RequestKind == "ValidationIssue" &&
            request.TargetKind == "audit" &&
            request.TargetId == subjectId);

        var matches = await store.FindByLinkageAsync(new EvidenceVaultLookupRequestDto(
            $"payment-intent/{subjectId}",
            null,
            null,
            null,
            null));
        matches.Should().ContainSingle(match => match.VaultId == response.VaultIdentity.VaultId);

        var requestLists = await store.ListRequestListsAsync(new EvidenceVaultRequestListQueryDto(
            RequestListKind: "AuditRequestList",
            TargetKind: "audit",
            TargetId: subjectId,
            Status: "Open"));
        requestLists.Should().ContainSingle(entry =>
            entry.VaultId == response.VaultIdentity.VaultId &&
            entry.OpenRequestCount == 1 &&
            entry.SupportRequests.Count == 1);

        var documents = await store.ListDocumentsAsync(new EvidenceVaultDocumentQueryDto(
            Classification: EvidenceDocumentClassificationDto.BankStatement,
            ChannelKind: EvidenceDocumentIntakeChannelDto.Api,
            ReviewStatus: EvidenceDocumentReviewStatusDto.NeedsReview,
            LinkKind: EvidenceDocumentLinkKindDto.CloseTask,
            ObjectId: "close-task:cash-support",
            TenantId: "tenant-alpha",
            Scope: "fund-alpha"));
        var documentEntry = documents.Should().ContainSingle(entry =>
            entry.VaultId == response.VaultIdentity.VaultId).Subject;
        documentEntry.Document.DocumentId.Should().Be(artifact.Document.DocumentId);
        documentEntry.ManifestRoute.Should().Be(response.VaultIdentity.ManifestRoute);
        documentEntry.OpenRequestCount.Should().Be(1);

        var fundDocuments = await store.ListDocumentsAsync(new EvidenceVaultDocumentQueryDto(
            Classification: EvidenceDocumentClassificationDto.BankStatement,
            LinkKind: EvidenceDocumentLinkKindDto.Fund,
            ObjectId: "fund-alpha",
            TenantId: "tenant-alpha",
            Scope: "fund-alpha"));
        fundDocuments.Should().ContainSingle(entry =>
            entry.VaultId == response.VaultIdentity.VaultId &&
            entry.Document.ObjectLinks.Any(link => link.LinkKind == EvidenceDocumentLinkKindDto.Fund));
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringVaultDocumentReview_RetainsReviewerStateAndAuditTrail()
    {
        var root = Path.Combine(Path.GetTempPath(), $"evidence-vault-document-review-{Guid.NewGuid():N}");
        var bytes = Encoding.UTF8.GetBytes("account,ending_cash\r\nfund-alpha,1024.50\r\n");
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var intake = await store.WriteIntakeArtifactAsync(new EvidenceVaultIntakeRequestDto(
            SubjectKind: "account",
            SubjectId: "fund-alpha-cash",
            IntakeChannel: "api",
            FileName: "bank-statement.csv",
            ContentBase64: Convert.ToBase64String(bytes),
            ReceivedBy: "fund-controller")
        {
            Classification = EvidenceDocumentClassificationDto.BankEvidence,
            ExtractionStatus = EvidenceExtractionStatusDto.NeedsReview,
            ReviewerState = new EvidenceDocumentReviewStateDto(EvidenceDocumentReviewStatusDto.NeedsReview)
        }, cts.Token);

        var review = await store.ReviewDocumentAsync(
            intake.VaultIdentity.VaultId,
            intake.Document!.DocumentId,
            new EvidenceVaultDocumentReviewRequestDto(
                EvidenceDocumentReviewStatusDto.Accepted,
                "controller",
                "Reviewed bank support for close package.",
                CorrelationId: "corr-review-1")
            {
                ConfirmedFields =
                [
                    new EvidenceDocumentConfirmedFieldDto(
                        "endingCash",
                        "1024.50",
                        "controller",
                        default,
                        "ending_cash",
                        "Confirmed against retained bank statement.")
                ]
            },
            cts.Token);

        review.Should().NotBeNull();
        review!.Entry.Document.ReviewerState.Status.Should().Be(EvidenceDocumentReviewStatusDto.Accepted);
        review.Entry.Document.ReviewerState.Reviewer.Should().Be("controller");
        review.Entry.Document.ReviewerState.Notes.Should().Be("Reviewed bank support for close package.");
        review.Entry.Document.ReviewerState.ConfirmedFields.Should().ContainSingle(field =>
            field.FieldName == "endingCash" &&
            field.ConfirmedValue == "1024.50" &&
            field.ConfirmedBy == "controller" &&
            field.ConfirmedAt != default);
        review.Entry.Document.SourceRecord.Should().NotBeNull();
        review.Entry.Document.SourceRecord!.SourceHashSha256.Should().Be(intake.ContentHashSha256);
        review.Entry.Document.SourceRecord.SourceChannel.Should().Be("api");
        review.Entry.Document.SourceRecord.ChannelKind.Should().Be(EvidenceDocumentIntakeChannelDto.Api);
        review.Entry.Document.SourceRecord.Actor.Should().Be("fund-controller");
        review.Entry.Document.Authority.CanSupport.Should().BeTrue();
        review.Entry.Document.Authority.CanBlock.Should().BeTrue();
        review.Entry.Document.Authority.CanSuggest.Should().BeTrue();
        review.Entry.Document.Authority.CanLink.Should().BeTrue();
        review.Entry.Document.Authority.CanApprove.Should().BeFalse();
        review.Entry.Document.Authority.CanPost.Should().BeFalse();
        review.Entry.Document.Authority.CanCertify.Should().BeFalse();
        review.Entry.Document.Authority.CanRelease.Should().BeFalse();
        review.Entry.Document.ExtractionStatus.Should().Be(EvidenceExtractionStatusDto.Accepted);
        review.AuditEvent.Action.Should().Be("DocumentReviewRecorded");
        review.AuditEvent.CorrelationId.Should().Be("corr-review-1");
        review.AuditEvent.Summary.Should().Contain("1 human-confirmed field");

        var identity = await store.TryGetVaultIdentityAsync(intake.VaultIdentity.VaultId, cts.Token);
        identity.Should().NotBeNull();
        identity!.Documents.Should().ContainSingle(document =>
            document.DocumentId == intake.Document.DocumentId &&
            document.ReviewerState.Status == EvidenceDocumentReviewStatusDto.Accepted &&
            document.ReviewerState.ConfirmedFields.Any(field => field.FieldName == "endingCash") &&
            document.AuditTrail.Any(evt => evt.Action == "DocumentReviewRecorded"));
        identity.ManifestSnapshot.Should().NotBeNull();
        identity.ManifestSnapshot!.Documents.Should().ContainSingle(document =>
            document.DocumentId == intake.Document.DocumentId &&
            document.SourceRecord != null &&
            document.SourceRecord.SourceHashSha256 == intake.ContentHashSha256 &&
            document.ReviewerState.Status == EvidenceDocumentReviewStatusDto.Accepted &&
            document.ReviewerState.ConfirmedFields.Any(field => field.FieldName == "endingCash") &&
            !document.Authority.CanApprove &&
            !document.Authority.CanPost &&
            !document.Authority.CanCertify &&
            !document.Authority.CanRelease);

        await using var manifest = (await store.TryOpenManifestByVaultIdAsync(intake.VaultIdentity.VaultId, cts.Token))!.Content;
        using var reader = new StreamReader(manifest);
        var manifestJson = await reader.ReadToEndAsync(cts.Token);
        manifestJson.Should().Contain("\"action\": \"DocumentReviewRecorded\"");
        manifestJson.Should().Contain("\"status\": \"Accepted\"");
        manifestJson.Should().Contain("\"fieldName\": \"endingCash\"");

        var unconfirmedReview = () => store.ReviewDocumentAsync(
            intake.VaultIdentity.VaultId,
            intake.Document.DocumentId,
            new EvidenceVaultDocumentReviewRequestDto(EvidenceDocumentReviewStatusDto.Accepted, "controller"),
            cts.Token);
        await unconfirmedReview.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*require at least one human-confirmed field*");
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringVaultLocalFileIntake_CopiesImportedReferenceIntoVault()
    {
        var root = Path.Combine(Path.GetTempPath(), $"evidence-vault-local-intake-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(root, "incoming");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "custodian-statement.csv");
        var bytes = Encoding.UTF8.GetBytes("account,ending_cash\r\nfund-alpha,1024.50\r\n");
        await File.WriteAllBytesAsync(sourcePath, bytes);
        var expectedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);

        var response = await store.WriteIntakeArtifactAsync(new EvidenceVaultIntakeRequestDto(
            SubjectKind: "account",
            SubjectId: "fund-alpha-cash",
            IntakeChannel: "local-file",
            FileName: "custodian-statement.csv",
            ContentType: "text/csv",
            SourceSystem: "custodian-import",
            ReceivedBy: "fund-controller",
            Linkage: new EvidenceSubjectLinkageDto(
                "account/fund-alpha-cash",
                null,
                "2026-06",
                "report-pack-2026-06",
                null))
        {
            Classification = EvidenceDocumentClassificationDto.CustodianFile,
            IntakeSource = new EvidenceDocumentIntakeSourceDto(
                EvidenceDocumentIntakeSourceKindDto.LocalFile,
                Path: sourcePath,
                Uri: "file://imports/custodian-statement.csv",
                DisplayName: "June custodian statement",
                ExpectedContentHashSha256: expectedHash),
            ReviewerState = new EvidenceDocumentReviewStateDto(
                EvidenceDocumentReviewStatusDto.Accepted,
                "fund-controller",
                DateTimeOffset.Parse("2026-06-30T18:00:00Z"),
                "Accepted imported custodian file for close support.")
            {
                ConfirmedFields =
                [
                    new EvidenceDocumentConfirmedFieldDto(
                        "sourceHashSha256",
                        expectedHash,
                        "fund-controller",
                        DateTimeOffset.Parse("2026-06-30T18:00:00Z"),
                        "sourceHashSha256",
                        "Confirmed retained file hash.")
                ]
            }
        });

        response.ContentHashSha256.Should().Be(expectedHash);
        response.Capture.CaptureChannel.Should().Be("local-file");
        response.Capture.SourceReference.Should().Be("file://imports/custodian-statement.csv");
        response.RelativePath.Should().Contain("custodian-statement");
        response.RelativePath.Should().NotContain("incoming");
        var retainedPath = Path.Combine(root, response.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(retainedPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(retainedPath)).Should().Equal(bytes);

        var identity = await store.TryGetVaultIdentityAsync(response.VaultIdentity.VaultId);
        identity.Should().NotBeNull();
        var artifact = identity!.Artifacts.Should().ContainSingle().Subject;
        artifact.SourcePath.Should().Be(Path.GetFullPath(sourcePath));
        artifact.SourceRoute.Should().Be("file://imports/custodian-statement.csv");
        artifact.Document.Should().NotBeNull();
        artifact.Document!.Classification.Should().Be(EvidenceDocumentClassificationDto.CustodianFile);
        artifact.Document.SourceHashSha256.Should().Be(expectedHash);
        artifact.Document.SourceReference.Should().Be("file://imports/custodian-statement.csv");
        artifact.Document.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.Account &&
            link.ObjectId == "fund-alpha-cash");
        artifact.Document.ObjectLinks.Should().Contain(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.Period &&
            link.ObjectId == "2026-06");
        response.Document.Should().BeEquivalentTo(artifact.Document);
    }

    [Fact]
    public async Task FileEvidenceArtifactStore_DuringAdapterSeamIntake_RetainsTypedSourceRecordWithoutFetching()
    {
        var root = Path.Combine(Path.GetTempPath(), $"evidence-vault-adapter-seams-{Guid.NewGuid():N}");
        var store = new FileEvidenceArtifactStore(root, NullLogger<FileEvidenceArtifactStore>.Instance);
        var adapterCases = new[]
        {
            (EvidenceDocumentIntakeSourceKindDto.Email, EvidenceDocumentIntakeChannelDto.Email, "mailbox://ap/fund-alpha/invoice-001.eml"),
            (EvidenceDocumentIntakeSourceKindDto.Sftp, EvidenceDocumentIntakeChannelDto.Sftp, "sftp://admin.example.com/packages/fund-alpha/nav.zip"),
            (EvidenceDocumentIntakeSourceKindDto.Api, EvidenceDocumentIntakeChannelDto.Api, "api://fund-admin/packages/nav-2026-06"),
            (EvidenceDocumentIntakeSourceKindDto.PortalDownload, EvidenceDocumentIntakeChannelDto.PortalDownload, "portal://custodian/fund-alpha/statement-2026-06.pdf")
        };

        foreach (var (sourceKind, channelKind, uri) in adapterCases)
        {
            var bytes = Encoding.UTF8.GetBytes($"adapter,{sourceKind},fund-alpha\r\n");
            var expectedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var response = await store.WriteIntakeArtifactAsync(new EvidenceVaultIntakeRequestDto(
                SubjectKind: "account",
                SubjectId: $"fund-alpha-{sourceKind.ToString().ToLowerInvariant()}",
                IntakeChannel: "upload",
                FileName: $"{sourceKind.ToString().ToLowerInvariant()}-support.csv",
                ContentBase64: Convert.ToBase64String(bytes),
                ContentType: "text/csv",
                SourceSystem: $"{sourceKind.ToString().ToLowerInvariant()}-adapter",
                ReceivedBy: "operations-operator")
            {
                Classification = EvidenceDocumentClassificationDto.AdminPackage,
                IntakeSource = new EvidenceDocumentIntakeSourceDto(
                    sourceKind,
                    Path: $"adapter/{sourceKind}/support.csv",
                    Uri: uri,
                    DisplayName: $"{sourceKind} support",
                    ExpectedContentHashSha256: expectedHash),
                TenantId = "tenant-alpha",
                Scope = "fund-alpha"
            });

            response.ContentHashSha256.Should().Be(expectedHash);
            response.Capture.ChannelKind.Should().Be(channelKind);
            response.Capture.SourceReference.Should().Be(uri);
            response.Document.Should().NotBeNull();
            response.Document!.ChannelKind.Should().Be(channelKind);
            response.Document.SourceRecord.Should().NotBeNull();
            response.Document.SourceRecord!.SourceHashSha256.Should().Be(expectedHash);
            response.Document.SourceRecord.ChannelKind.Should().Be(channelKind);
            response.Document.SourceRecord.SourceReference.Should().Be(uri);
            response.Document.SourceRecord.SourceSystem.Should().Be($"{sourceKind.ToString().ToLowerInvariant()}-adapter");
            response.Document.SourceRecord.Actor.Should().Be("operations-operator");
            response.Document.SourceRecord.TenantId.Should().Be("tenant-alpha");
            response.Document.SourceRecord.Scope.Should().Be("fund-alpha");
        }

        var missingContent = () => store.WriteIntakeArtifactAsync(new EvidenceVaultIntakeRequestDto(
            SubjectKind: "account",
            SubjectId: "fund-alpha-email",
            IntakeChannel: "email",
            FileName: "missing-content.csv")
        {
            IntakeSource = new EvidenceDocumentIntakeSourceDto(
                EvidenceDocumentIntakeSourceKindDto.Email,
                Uri: "mailbox://ap/fund-alpha/missing-content.eml")
        });
        await missingContent.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*adapter seam in v1 and requires contentBase64*");
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

    [Fact]
    public async Task EvidenceEndpoints_VaultIntake_RetainsUploadedEvidenceAndRejectsInvalidPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"evidence-vault-intake-endpoint-{Guid.NewGuid():N}");
        await using var app = await CreateEvidenceAppAsync(root);
        var client = app.GetTestClient();
        var subjectId = "payment:fund-alpha:capital-call:20260630";
        var bytes = Encoding.UTF8.GetBytes("settlement_id,amount,currency\r\nsettle-001,1250.00,USD\r\n");

        var response = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/intake",
            new EvidenceVaultIntakeRequestDto(
                SubjectKind: EvidenceSubjectResolver.PaymentIntentKind,
                SubjectId: subjectId,
                IntakeChannel: "api",
                FileName: "settlement-proof.csv",
                ContentBase64: Convert.ToBase64String(bytes),
                ContentType: "text/csv",
                SourceSystem: "bank-api",
                SourceReference: "api://bank/settlements/settle-001",
                ReceivedBy: "fund-controller",
                ExpectedContentHashSha256: null,
                ExtractedFields:
                [
                    new EvidenceArtifactExtractionFieldDto(
                        FieldName: "currency",
                        ExtractedValue: "USD",
                        ExpectedValue: "USD",
                        ConfidenceScore: 0.97m,
                        ReviewState: "Ready",
                        ValidationStatus: EvidenceStatusDto.Ready,
                        ValidationMessage: null,
                        LinkedRecordKind: "payment-intent",
                        LinkedRecordId: subjectId)
                ],
                Linkage: new EvidenceSubjectLinkageDto(
                    $"payment-intent/{subjectId}",
                    null,
                    null,
                    null,
                    null))
            {
                Classification = EvidenceDocumentClassificationDto.BankEvidence,
                Actor = "fund-controller",
                TenantId = "tenant-alpha",
                Scope = "fund-alpha",
                ObjectLinks =
                [
                    new EvidenceDocumentLinkDto(
                        EvidenceDocumentLinkKindDto.Account,
                        "bank-account:operating",
                        "Operating bank account")
                ]
            },
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var intake = await response.Content.ReadFromJsonAsync<EvidenceVaultIntakeResponseDto>(ServerJsonOptions);
        intake.Should().NotBeNull();
        intake!.VaultIdentity.StorageKind.Should().Be("file-bundle");
        intake.VaultIdentity.Artifacts.Should().ContainSingle(artifact =>
            artifact.ContentHashSha256 == intake.ContentHashSha256 &&
            artifact.Capture!.SourceSystem == "bank-api");
        intake.Document.Should().NotBeNull();
        intake.Document!.Classification.Should().Be(EvidenceDocumentClassificationDto.BankEvidence);
        intake.Document.ExtractionStatus.Should().Be(EvidenceExtractionStatusDto.Extracted);
        intake.Document.ExtractorId.Should().Be(ManualEvidenceDocumentExtractor.ExtractorId);
        intake.Document.ObjectLinks.Should().ContainSingle(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.Account &&
            link.ObjectId == "bank-account:operating");
        intake.VaultIdentity.Documents.Should().ContainSingle(document =>
            document.DocumentId == intake.Document.DocumentId &&
            document.SourceHashSha256 == intake.ContentHashSha256);

        var documentResponse = await client.GetAsync(
            "/api/workstation/evidence/vault/documents?classification=BankEvidence&extractionStatus=Extracted&linkKind=Account&objectId=bank-account%3Aoperating&tenantId=tenant-alpha&scope=fund-alpha");
        documentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var documentEntries = await documentResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceVaultDocumentEntryDto>>(ServerJsonOptions);
        var documentEntry = documentEntries.Should().ContainSingle(entry =>
            entry.VaultId == intake.VaultIdentity.VaultId).Subject;
        documentEntry.SubjectKind.Should().Be(EvidenceSubjectResolver.PaymentIntentKind);
        documentEntry.Document.ExtractorId.Should().Be(ManualEvidenceDocumentExtractor.ExtractorId);
        documentEntry.Document.ObjectLinks.Should().ContainSingle(link =>
            link.LinkKind == EvidenceDocumentLinkKindDto.Account &&
            link.ObjectId == "bank-account:operating");

        var invalidDocumentQuery = await client.GetAsync(
            "/api/workstation/evidence/vault/documents?classification=NotAClassification");
        invalidDocumentQuery.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var manifestJson = await client.GetStringAsync(intake.VaultIdentity.ManifestRoute);
        manifestJson.Should().Contain("\"captureChannel\": \"api\"");
        manifestJson.Should().Contain("\"classification\": \"BankEvidence\"");
        manifestJson.Should().Contain("\"file-bundle\"");
        manifestJson.Should().Contain("settlement-proof.csv");

        var searchResponse = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/search",
            new EvidenceVaultLookupRequestDto(
                $"payment-intent/{subjectId}",
                null,
                null,
                null,
                null),
            ServerJsonOptions);
        searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var matches = await searchResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceVaultIdentityDto>>(ServerJsonOptions);
        matches.Should().NotBeNull();
        matches!.Should().ContainSingle(match => match.VaultId == intake.VaultIdentity.VaultId);

        var invalidResponse = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/intake",
            new EvidenceVaultIntakeRequestDto(
                SubjectKind: EvidenceSubjectResolver.PaymentIntentKind,
                SubjectId: subjectId,
                IntakeChannel: "api",
                FileName: "settlement-proof.csv",
                ContentBase64: "not-base64"),
            ServerJsonOptions);
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await invalidResponse.Content.ReadFromJsonAsync<EvidenceEndpointErrorDto>(ServerJsonOptions);
        error!.Code.Should().Be("invalid-evidence-vault-intake");
    }

    [Fact]
    public async Task EvidenceEndpoints_VaultIntake_RetainsAdapterSeamSourceRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), $"evidence-vault-adapter-intake-endpoint-{Guid.NewGuid():N}");
        await using var app = await CreateEvidenceAppAsync(root);
        var client = app.GetTestClient();
        var subjectId = "fund-alpha-admin-package-202606";
        var bytes = Encoding.UTF8.GetBytes("package,period,status\r\nadmin-package,2026-06,received\r\n");
        var expectedHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var response = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/intake",
            new EvidenceVaultIntakeRequestDto(
                SubjectKind: "account",
                SubjectId: subjectId,
                IntakeChannel: "upload",
                FileName: "admin-package.csv",
                ContentBase64: Convert.ToBase64String(bytes),
                ContentType: "text/csv",
                SourceSystem: "fund-admin-portal",
                ReceivedBy: "fund-admin-operator",
                ExpectedContentHashSha256: expectedHash)
            {
                Classification = EvidenceDocumentClassificationDto.AdminPackage,
                IntakeSource = new EvidenceDocumentIntakeSourceDto(
                    EvidenceDocumentIntakeSourceKindDto.PortalDownload,
                    Path: "portal/fund-alpha/admin-package-202606.csv",
                    Uri: "portal://fund-admin/fund-alpha/admin-package-202606",
                    DisplayName: "Fund Alpha June admin package",
                    ExpectedContentHashSha256: expectedHash),
                TenantId = "tenant-alpha",
                Scope = "fund-alpha",
                ObjectLinks =
                [
                    new EvidenceDocumentLinkDto(
                        EvidenceDocumentLinkKindDto.Fund,
                        "fund-alpha",
                        "Fund Alpha")
                ]
            },
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var intake = await response.Content.ReadFromJsonAsync<EvidenceVaultIntakeResponseDto>(ServerJsonOptions);
        intake.Should().NotBeNull();
        intake!.ContentHashSha256.Should().Be(expectedHash);
        intake.Capture.ChannelKind.Should().Be(EvidenceDocumentIntakeChannelDto.PortalDownload);
        intake.Capture.SourceReference.Should().Be("portal://fund-admin/fund-alpha/admin-package-202606");
        intake.Document.Should().NotBeNull();
        intake.Document!.Classification.Should().Be(EvidenceDocumentClassificationDto.AdminPackage);
        intake.Document.ChannelKind.Should().Be(EvidenceDocumentIntakeChannelDto.PortalDownload);
        intake.Document.SourceReference.Should().Be("portal://fund-admin/fund-alpha/admin-package-202606");
        intake.Document.SourceRecord.Should().NotBeNull();
        intake.Document.SourceRecord!.SourceHashSha256.Should().Be(expectedHash);
        intake.Document.SourceRecord.ChannelKind.Should().Be(EvidenceDocumentIntakeChannelDto.PortalDownload);
        intake.Document.SourceRecord.SourceSystem.Should().Be("fund-admin-portal");
        intake.Document.SourceRecord.SourceReference.Should().Be("portal://fund-admin/fund-alpha/admin-package-202606");
        intake.Document.SourceRecord.Actor.Should().Be("fund-admin-operator");
        intake.Document.SourceRecord.TenantId.Should().Be("tenant-alpha");
        intake.Document.SourceRecord.Scope.Should().Be("fund-alpha");
        intake.VaultIdentity.ManifestSnapshot.Should().NotBeNull();
        intake.VaultIdentity.ManifestSnapshot!.Documents.Should().ContainSingle(document =>
            document.DocumentId == intake.Document.DocumentId &&
            document.SourceRecord != null &&
            document.SourceRecord.ChannelKind == EvidenceDocumentIntakeChannelDto.PortalDownload &&
            document.SourceRecord.SourceHashSha256 == expectedHash);

        var documentResponse = await client.GetAsync(
            "/api/workstation/evidence/vault/documents?classification=AdminPackage&channelKind=PortalDownload&linkKind=Fund&objectId=fund-alpha&tenantId=tenant-alpha&scope=fund-alpha");
        documentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var documentEntries = await documentResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceVaultDocumentEntryDto>>(ServerJsonOptions);
        documentEntries.Should().ContainSingle(entry =>
            entry.VaultId == intake.VaultIdentity.VaultId &&
            entry.Document.SourceRecord != null &&
            entry.Document.SourceRecord.ChannelKind == EvidenceDocumentIntakeChannelDto.PortalDownload);

        var invalidResponse = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/intake",
            new EvidenceVaultIntakeRequestDto(
                SubjectKind: "account",
                SubjectId: subjectId,
                IntakeChannel: "portal-download",
                FileName: "admin-package.csv")
            {
                IntakeSource = new EvidenceDocumentIntakeSourceDto(
                    EvidenceDocumentIntakeSourceKindDto.PortalDownload,
                    Uri: "portal://fund-admin/fund-alpha/missing-content")
            },
            ServerJsonOptions);
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await invalidResponse.Content.ReadFromJsonAsync<EvidenceEndpointErrorDto>(ServerJsonOptions);
        error!.Message.Should().Contain("adapter seam in v1");
    }

    [Fact]
    public async Task EvidenceEndpoints_VaultIntake_WithFixtureMetadata_UsesDeterministicFixtureExtraction()
    {
        var root = Path.Combine(Path.GetTempPath(), $"evidence-vault-fixture-intake-{Guid.NewGuid():N}");
        await using var app = await CreateEvidenceAppAsync(root);
        var client = app.GetTestClient();
        var bytes = Encoding.UTF8.GetBytes("account,ending_cash\r\nfund-alpha-cash,1024.50\r\n");

        var response = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/intake",
            new EvidenceVaultIntakeRequestDto(
                SubjectKind: "account",
                SubjectId: "fund-alpha-cash",
                IntakeChannel: "fixture",
                FileName: "fixture-bank-statement.csv",
                ContentBase64: Convert.ToBase64String(bytes),
                ContentType: "text/csv",
                SourceSystem: "fixture-bank",
                SourceReference: "fixture://bank/statement/fund-alpha-cash",
                ReceivedBy: "fixture-operator",
                Linkage: new EvidenceSubjectLinkageDto(
                    "account/fund-alpha-cash",
                    null,
                    "2026-06",
                    null,
                    null))
            {
                Classification = EvidenceDocumentClassificationDto.BankEvidence,
                TenantId = "tenant-alpha",
                Scope = "fund-alpha"
            },
            ServerJsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var intake = await response.Content.ReadFromJsonAsync<EvidenceVaultIntakeResponseDto>(ServerJsonOptions);
        intake.Should().NotBeNull();
        intake!.ExtractedFields.Should().Contain(field =>
            field.FieldName == "statementDate" &&
            field.ValidationStatus == EvidenceStatusDto.Ready &&
            field.ReviewState == "Fixture");
        intake.ExtractedFields.Should().Contain(field =>
            field.FieldName == "endingCash" &&
            field.ExtractedValue == "1024.50");
        intake.Document.Should().NotBeNull();
        intake.Document!.ExtractionStatus.Should().Be(EvidenceExtractionStatusDto.Extracted);
        intake.Document.ExtractorId.Should().Be(ManualEvidenceDocumentExtractor.FixtureExtractorId);
        intake.VaultIdentity.SupportRequests.Should().BeEmpty();
        intake.VaultIdentity.ManifestSnapshot.Should().NotBeNull();
        intake.VaultIdentity.ManifestSnapshot!.Documents.Should().ContainSingle(document =>
            document.DocumentId == intake.Document.DocumentId &&
            document.ExtractorId == ManualEvidenceDocumentExtractor.FixtureExtractorId);

        var documentResponse = await client.GetAsync(
            "/api/workstation/evidence/vault/documents?classification=BankEvidence&extractionStatus=Extracted&tenantId=tenant-alpha&scope=fund-alpha");
        documentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var documentEntries = await documentResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceVaultDocumentEntryDto>>(ServerJsonOptions);
        documentEntries.Should().ContainSingle(entry =>
            entry.VaultId == intake.VaultIdentity.VaultId &&
            entry.Document.ExtractorId == ManualEvidenceDocumentExtractor.FixtureExtractorId);
    }

    [Fact]
    public async Task EvidenceEndpoints_VaultDocumentReview_RetainsOperatorReviewWithoutAccountingMutation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"evidence-vault-endpoint-review-{Guid.NewGuid():N}");
        await using var app = await CreateEvidenceAppAsync(root);
        var client = app.GetTestClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var bytes = Encoding.UTF8.GetBytes("invoice,total\r\nINV-1,125.00\r\n");
        var intakeResponse = await client.PostAsJsonAsync(
            "/api/workstation/evidence/vault/intake",
            new EvidenceVaultIntakeRequestDto(
                SubjectKind: EvidenceSubjectResolver.AccountingRecordKind,
                SubjectId: "workflow-2026-06",
                IntakeChannel: "upload",
                FileName: "invoice-support.csv",
                ContentBase64: Convert.ToBase64String(bytes),
                ContentType: "text/csv",
                ReceivedBy: "fund-operator")
            {
                Classification = EvidenceDocumentClassificationDto.Invoice,
                ExtractionStatus = EvidenceExtractionStatusDto.NeedsReview,
                ReviewerState = new EvidenceDocumentReviewStateDto(EvidenceDocumentReviewStatusDto.NeedsReview),
                TenantId = "tenant-alpha",
                Scope = "fund-alpha",
                ObjectLinks =
                [
                    new EvidenceDocumentLinkDto(EvidenceDocumentLinkKindDto.ReportLine, "expenses.invoice-1")
                ]
            },
            ServerJsonOptions,
            cts.Token);
        intakeResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var intake = await intakeResponse.Content.ReadFromJsonAsync<EvidenceVaultIntakeResponseDto>(ServerJsonOptions, cts.Token);
        intake.Should().NotBeNull();
        intake!.Document.Should().NotBeNull();

        var reviewResponse = await client.PostAsJsonAsync(
            $"/api/workstation/evidence/vault/{Uri.EscapeDataString(intake.VaultIdentity.VaultId)}/documents/{Uri.EscapeDataString(intake.Document!.DocumentId)}/review",
            new EvidenceVaultDocumentReviewRequestDto(
                EvidenceDocumentReviewStatusDto.Accepted,
                "controller",
                "Invoice support reviewed for retained package.",
                CorrelationId: "corr-endpoint-review")
            {
                ConfirmedFields =
                [
                    new EvidenceDocumentConfirmedFieldDto(
                        "invoiceTotal",
                        "125.00",
                        "controller",
                        default,
                        "total",
                        "Confirmed invoice total before accepting evidence.")
                ]
            },
            ServerJsonOptions,
            cts.Token);

        reviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var review = await reviewResponse.Content.ReadFromJsonAsync<EvidenceVaultDocumentReviewResponseDto>(ServerJsonOptions, cts.Token);
        review.Should().NotBeNull();
        review!.Entry.VaultId.Should().Be(intake.VaultIdentity.VaultId);
        review.Entry.Document.DocumentId.Should().Be(intake.Document.DocumentId);
        review.Entry.Document.ReviewerState.Status.Should().Be(EvidenceDocumentReviewStatusDto.Accepted);
        review.Entry.Document.ReviewerState.ConfirmedFields.Should().ContainSingle(field =>
            field.FieldName == "invoiceTotal" &&
            field.ConfirmedValue == "125.00" &&
            field.ConfirmedBy == "controller");
        review.Entry.Document.Authority.CanSupport.Should().BeTrue();
        review.Entry.Document.Authority.CanBlock.Should().BeTrue();
        review.Entry.Document.Authority.CanSuggest.Should().BeTrue();
        review.Entry.Document.Authority.CanLink.Should().BeTrue();
        review.Entry.Document.Authority.CanApprove.Should().BeFalse();
        review.Entry.Document.Authority.CanPost.Should().BeFalse();
        review.Entry.Document.Authority.CanCertify.Should().BeFalse();
        review.Entry.Document.Authority.CanRelease.Should().BeFalse();
        review.Entry.Document.ExtractionStatus.Should().Be(EvidenceExtractionStatusDto.Accepted);
        review.Entry.Document.AuditTrail.Should().Contain(evt =>
            evt.Action == "DocumentReviewRecorded" &&
            evt.Actor == "controller" &&
            evt.CorrelationId == "corr-endpoint-review");

        var acceptedDocuments = await client.GetFromJsonAsync<IReadOnlyList<EvidenceVaultDocumentEntryDto>>(
            "/api/workstation/evidence/vault/documents?reviewStatus=Accepted&extractionStatus=Accepted&linkKind=ReportLine&objectId=expenses.invoice-1",
            ServerJsonOptions,
            cts.Token);
        acceptedDocuments.Should().ContainSingle(entry =>
            entry.VaultId == intake.VaultIdentity.VaultId &&
            entry.Document.DocumentId == intake.Document.DocumentId &&
            entry.Document.ReviewerState.ConfirmedFields.Any(field => field.FieldName == "invoiceTotal"));

        var unconfirmedAcceptedResponse = await client.PostAsJsonAsync(
            $"/api/workstation/evidence/vault/{Uri.EscapeDataString(intake.VaultIdentity.VaultId)}/documents/{Uri.EscapeDataString(intake.Document.DocumentId)}/review",
            new EvidenceVaultDocumentReviewRequestDto(EvidenceDocumentReviewStatusDto.Accepted, "controller"),
            ServerJsonOptions,
            cts.Token);
        unconfirmedAcceptedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var missingResponse = await client.PostAsJsonAsync(
            $"/api/workstation/evidence/vault/{Uri.EscapeDataString(intake.VaultIdentity.VaultId)}/documents/missing-document/review",
            new EvidenceVaultDocumentReviewRequestDto(EvidenceDocumentReviewStatusDto.Rejected, "controller"),
            ServerJsonOptions,
            cts.Token);
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EvidenceEndpoints_VaultRequestLists_ReturnsFrozenSupportRequestIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), $"evidence-vault-request-lists-{Guid.NewGuid():N}");
        await using var app = await CreateEvidenceAppAsync(root);
        var client = app.GetTestClient();
        var store = app.Services.GetRequiredService<IEvidenceArtifactStore>();
        var subject = Subject(EvidenceSubjectResolver.ReportPackKind, "close-2026-05");
        var export = await store.WriteManifestAsync(
            BlockedAuditSupportPacket(subject),
            new EvidencePacketExportRequest("operator", "seed request-list index"));

        var response = await client.GetAsync(
            "/api/workstation/evidence/vault/request-lists?targetKind=audit&targetId=close-2026-05&status=Open");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await response.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceVaultRequestListEntryDto>>(ServerJsonOptions);
        entries.Should().NotBeNull();
        var entry = entries!.Should().ContainSingle().Subject;
        entry.VaultId.Should().Be(export.VaultIdentity!.VaultId);
        entry.SubjectKind.Should().Be(EvidenceSubjectResolver.ReportPackKind);
        entry.SubjectId.Should().Be("close-2026-05");
        entry.RequestListKind.Should().Be("AuditRequestList");
        entry.RequestListKindCode.Should().Be(EvidenceRequestListKindDto.Audit);
        entry.OpenRequestCount.Should().Be(2);
        entry.ManifestRoute.Should().Be(export.VaultIdentity.ManifestRoute);
        entry.SupportRequests.Should().Contain(request =>
            request.RequestKind == "BlockedWorkItem" &&
            request.WorkItemId == "audit-request:close-2026-05");

        var typedResponse = await client.GetAsync(
            "/api/workstation/evidence/vault/request-lists?requestListKindCode=Audit&status=Open");
        typedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var typedEntries = await typedResponse.Content.ReadFromJsonAsync<IReadOnlyList<EvidenceVaultRequestListEntryDto>>(ServerJsonOptions);
        typedEntries.Should().ContainSingle(typedEntry =>
            typedEntry.VaultId == export.VaultIdentity.VaultId &&
            typedEntry.RequestListKindCode == EvidenceRequestListKindDto.Audit);

        var invalidResponse = await client.GetAsync("/api/workstation/evidence/vault/request-lists?maxResults=0");
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var invalidKindResponse = await client.GetAsync("/api/workstation/evidence/vault/request-lists?requestListKindCode=NotARealKind");
        invalidKindResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
        bool accountingRecordAuditReady = true,
        Guid? ledgerBookId = null)
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
            AccountingRecordSummary: BuildAccountingRecordSummary(workflowId, now, accountingRecordAuditReady),
            LedgerBookId: ledgerBookId);
    }

    private static PrivateCapitalActivityProjectionDto PrivateCapitalActivityProjection(Guid? ledgerBookId = null)
    {
        var now = new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.Zero);
        var journalEntryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var fundEvent = new PrivateCapitalFundEventDto(
            "fund-event:fund-alpha:capital-call:20260630",
            "CapitalCall",
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryStatusDto.Submitted,
            journalEntryId,
            new DateOnly(2026, 6, 30),
            "capital-account:fund-alpha:lp-1",
            "investor:lp-1",
            "USD",
            100m,
            100m,
            "Capital call for Fund Alpha LP",
            "payment:fund-alpha:capital-call:20260630",
            "settlement:fund-alpha:capital-call:20260630",
            ["/evidence/fund-alpha/bank-cash-capital-call.pdf"],
            [],
            now,
            ApprovalId: "approval:fund-alpha:capital-call:20260630");
        var subledgerEntry = new PrivateCapitalCapitalAccountSubledgerEntryDto(
            "capital-account-subledger:capital-account:fund-alpha:lp-1:fund-event:fund-alpha:capital-call:20260630:11111111-1111-1111-1111-111111111111",
            "capital-account:fund-alpha:lp-1",
            "investor:lp-1",
            "USD",
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            ManualJournalEntryTypeDto.CapitalCall,
            ManualJournalEntryStatusDto.Submitted,
            journalEntryId,
            new DateOnly(2026, 6, 30),
            100m,
            100m,
            100m,
            "Capital call for Fund Alpha LP",
            ["/evidence/fund-alpha/bank-cash-capital-call.pdf"],
            [],
            now);
        var ledgerImpact = new PrivateCapitalLedgerImpactDto(
            "ledger-impact:fund-event:fund-alpha:capital-call:20260630",
            journalEntryId,
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            ManualJournalEntryStatusDto.Submitted,
            new DateOnly(2026, 6, 30),
            "USD",
            100m,
            100m,
            0m,
            2,
            IsBalanced: true,
            IsPostingReady: true,
            ["/evidence/fund-alpha/bank-cash-capital-call.pdf"],
            [
                new PrivateCapitalLedgerLineImpactDto("line-debit", "Assets:Cash", AccountingTemplateLineSideDto.Debit, 100m, "USD", null, null, null, "/evidence/fund-alpha/bank-cash-capital-call.pdf"),
                new PrivateCapitalLedgerLineImpactDto("line-credit", "Equity:Capital Contributions", AccountingTemplateLineSideDto.Credit, 100m, "USD", null, null, null, "/evidence/fund-alpha/bank-cash-capital-call.pdf")
            ],
            []);
        var reportOutput = new PrivateCapitalReportOutputDto(
            "report-output:fund-event:fund-alpha:capital-call:20260630",
            "CapitalCallNotice",
            "Capital call notice",
            "/api/fund-structure/reporting/runs?fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630",
            fundEvent.FundEventId,
            fundEvent.FundEventType,
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            ManualJournalEntryStatusDto.Submitted,
            new DateOnly(2026, 6, 30),
            "USD",
            100m,
            1,
            ["/evidence/fund-alpha/bank-cash-capital-call.pdf"],
            IsReportReady: true,
            [],
            ReportOutputRoute: "/api/ledger/private-capital/report-output?fundProfileId=fund-alpha&reportOutputId=report-output%3Afund-event%3Afund-alpha%3Acapital-call%3A20260630&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1");
        var capitalAccount = new PrivateCapitalCapitalAccountActivityDto(
            fundEvent.CapitalAccountId,
            fundEvent.InvestorId,
            "USD",
            Contributions: 100m,
            Distributions: 0m,
            Subscriptions: 0m,
            Redemptions: 0m,
            ManagementFees: 0m,
            NetActivity: 100m,
            FundEventCount: 1,
            LastEffectiveDate: new DateOnly(2026, 6, 30),
            LastFundEventType: fundEvent.FundEventType,
            FundEventIds: [fundEvent.FundEventId]);
        var records = PrivateCapitalFundEventLedgerRecordBuilder.Build(
            "fund-alpha",
            [fundEvent],
            [subledgerEntry],
            [ledgerImpact],
            [reportOutput]);
        var subledgers = PrivateCapitalCapitalAccountSubledgerBuilder.Build(
            "fund-alpha",
            ledgerBookId,
            now,
            [capitalAccount],
            records,
            [subledgerEntry],
            [ledgerImpact],
            [reportOutput],
            []);
        var record = records.Single();
        var paymentIntent = new PaymentIntentWorkflowDto(
            fundEvent.PaymentIntentId!,
            fundEvent.SettlementReference,
            "fund-alpha",
            ledgerBookId,
            fundEvent.FundEventId,
            journalEntryId,
            "fund-controller",
            now,
            PaymentIntentWorkflowStatusDto.ExecutionDeferred,
            "Ready, execution deferred",
            record.PaymentIntentEvidence!.Summary,
            "Full payment execution is explicitly deferred in v0.18; this layer only retains intent, control, cash-evidence, reconciliation, and audit history before any bank-side instruction.",
            new PaymentIntentExpectedCashMovementDto(
                fundEvent.PaymentIntentId!,
                PaymentIntentCashDirectionDto.Inflow,
                100m,
                "USD",
                new DateOnly(2026, 6, 30),
                fundEvent.SettlementReference,
                fundEvent.FundEventId,
                fundEvent.FundEventType,
                fundEvent.CapitalAccountId,
                fundEvent.InvestorId,
                "Capital call for Fund Alpha LP",
                "fund:fund-alpha",
                "fund:fund-alpha / capital-account:fund-alpha:lp-1 / investor:lp-1",
                "Capital call for Fund Alpha LP",
                "Controller approval retained before execution-deferred reliance",
                ["/evidence/fund-alpha/bank-cash-capital-call.pdf"]),
            PrivateCapitalActivityRoutes.BuildPaymentIntentEvidenceRoute(fundEvent.PaymentIntentId!),
            PrivateCapitalActivityRoutes.BuildPaymentIntentWorkbenchRoute("fund-alpha", ledgerBookId, fundEvent.PaymentIntentId!),
            ApprovalChain:
            [
                new PaymentIntentApprovalStepDto(
                    1,
                    "Requester",
                    "fund-controller",
                    "Approved",
                    now,
                    PrivateCapitalActivityRoutes.BuildApprovalRoute("fund-alpha", journalEntryId, fundEvent.ApprovalId)),
                new PaymentIntentApprovalStepDto(
                    2,
                    "Controller",
                    "fund-controller",
                    "Approved",
                    now,
                    PrivateCapitalActivityRoutes.BuildApprovalRoute("fund-alpha", journalEntryId, fundEvent.ApprovalId))
            ],
            BankEvidence:
            [
                new PaymentIntentBankEvidenceDto(
                    "bank-evidence:fund-alpha:capital-call:20260630",
                    "retained-cash-evidence",
                    "Retained",
                    "Retained cash evidence confirms the expected capital-call inflow.",
                    Amount: 100m,
                    Currency: "USD",
                    EffectiveDate: new DateOnly(2026, 6, 30),
                    RecordedAtUtc: now,
                    ExternalRef: fundEvent.SettlementReference,
                    EvidenceRoute: "/evidence/fund-alpha/bank-cash-capital-call.pdf")
            ],
            ReconciliationLinks:
            [
                new PaymentIntentReconciliationLinkDto(
                    "reconciliation:fund-alpha:capital-call:20260630",
                    "Ready",
                    "Settlement reference reconciles retained cash evidence to the fund-event ledger record.",
                    EvidenceRoute: "/evidence/fund-alpha/bank-cash-capital-call.pdf",
                    ReconciliationCaseId: "reconciliation-case:fund-alpha:capital-call:20260630",
                    ReconciliationRunId: "reconciliation-run:fund-alpha:20260630")
            ],
            AuditHistory:
            [
                new PaymentIntentAuditEventDto(
                    "payment-intent-requested:fund-alpha:capital-call:20260630",
                    now,
                    "fund-controller",
                    "payment-intent.requested",
                    "Payment intent was requested from the private-capital fund event.",
                    ["/evidence/fund-alpha/bank-cash-capital-call.pdf"]),
                new PaymentIntentAuditEventDto(
                    "payment-intent-execution-deferred:fund-alpha:capital-call:20260630",
                    now,
                    "system",
                    "payment-intent.execution-deferred",
                    "Live treasury execution remains deferred while retained evidence is reviewed.",
                    ["/evidence/fund-alpha/bank-cash-capital-call.pdf"])
            ]);

        return new PrivateCapitalActivityProjectionDto(
            "fund-alpha",
            ledgerBookId,
            now,
            FundEventCount: 1,
            CapitalAccountCount: 1,
            SubmittedFundEventCount: 1,
            ApprovalQueueCount: 1,
            PostedFundEventCount: 0,
            PublishedReportOutputCount: 0,
            NetCapitalActivity: 100m,
            Currency: "USD",
            FundEvents: [fundEvent],
            CapitalAccounts: [capitalAccount],
            CapitalAccountSubledgerEntries: [subledgerEntry],
            LedgerImpacts: [ledgerImpact],
            ReportOutputs: [reportOutput],
            ValidationIssues: [],
            FundEventRecords: records,
            CapitalAccountSubledgers: subledgers,
            PaymentIntents: [paymentIntent]);
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
        IOperationsContinuityWorkflowService? operationsWorkflowService = null,
        IReportPackDeliveryRecordStore? deliveryRecordStore = null,
        IManualJournalEntryWorkbenchService? manualJournalService = null)
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

        if (deliveryRecordStore is not null)
        {
            builder.Services.AddSingleton(deliveryRecordStore);
        }

        if (manualJournalService is not null)
        {
            builder.Services.AddSingleton(manualJournalService);
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

    private static EvidencePacketDto BlockedAuditSupportPacket(EvidenceSubjectDto subject)
        => new(
            Subject: subject,
            GeneratedAt: DateTimeOffset.UtcNow,
            Nodes:
            [
                Node(subject, "source-node", "source-document", EvidenceStatusDto.Ready),
                Node(
                    subject,
                    "audit-support",
                    "audit-history",
                    EvidenceStatusDto.Missing,
                    workItemIds: ["audit-request:close-2026-05"])
            ],
            Edges: [],
            Completeness: new EvidenceCompletenessDto(
                50,
                EvidenceStatusDto.Blocked,
                ["source-node", "audit-support"],
                ["source-node"],
                ["audit-support"],
                [],
                ["audit-request:close-2026-05"])
            {
                ValidationIssues =
                [
                    new EvidenceValidationIssueDto(
                        Code: "missing-required-evidence",
                        Severity: EvidenceValidationSeverityDto.Critical,
                        Message: "Audit support package is missing.",
                        EvidenceId: "audit-support",
                        EvidenceKind: "audit-history",
                        SourceSystem: "test")
                ]
            },
            Actions: [],
            Warnings: []);

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

    private static ReportPackDeliveryAttemptDto BuildReportPackDeliveryAttempt(
        Guid reportId,
        Guid attemptId,
        DateTimeOffset generatedAt)
    {
        var packageRoute = $"/api/fund-structure/reporting/packs/{reportId:D}/deliveries/{attemptId:D}/package?token=test-token";
        var artifactRoute = $"/api/fund-structure/reporting/packs/{reportId:D}/deliveries/{attemptId:D}/artifacts/board-pack.pdf?token=test-token";
        var artifact = new ReportPackDeliveryArtifactDto(
            GovernanceReportArtifactFormatDto.Pdf,
            "board-pack.pdf",
            "application/pdf",
            "workstation/reporting/deliveries/pkg-board-1/board-pack.pdf",
            2048,
            "delivery-artifact:pdf",
            new string('b', 64),
            "delivery-artifact:report-pack-delivery:pdf",
            artifactRoute);
        var deliveryEvidence = new ReportPackEvidenceLinkDto(
            "delivery-artifact:pdf",
            "board-pack.pdf",
            artifactRoute,
            "report-pack-delivery",
            generatedAt);
        var lineEvidence = new ReportPackEvidenceLinkDto(
            "ledger-evidence-1",
            "Line evidence",
            "/evidence/ledger-evidence-1",
            "reporting",
            generatedAt.AddMinutes(-5));
        var restatementEvidence = new ReportPackEvidenceLinkDto(
            "cash-restatement-1",
            "Cash restatement",
            "/evidence/cash-restatement-1",
            "reporting",
            generatedAt.AddMinutes(-2));
        var package = new ReportPackDeliveryPackageDto(
            PackageId: "pkg-board-1",
            ReportId: reportId,
            DistributionId: "board-reporting-committee",
            DeliveryMode: ReportPackDeliveryModeDto.SecurePortal,
            SecureLink: packageRoute,
            PortalRoute: "/portal/reporting/packages/pkg-board-1",
            Formats: [GovernanceReportArtifactFormatDto.Pdf, GovernanceReportArtifactFormatDto.Xlsx, GovernanceReportArtifactFormatDto.Csv],
            Artifacts: [artifact],
            CreatedAtUtc: generatedAt,
            RetainedManifestPath: "workstation/reporting/deliveries/pkg-board-1/manifest.json",
            PublicationEvidenceHash: new string('c', 64),
            IntegritySummary: "1 artifact retained with SHA-256 checksum.",
            ReportingRunId: "investor-monthly-statement-202606",
            ReportingTemplateId: "investor-monthly-statement",
            ReportingScheduleId: "sched-investor",
            SourceArtifacts: ["/api/workstation/reporting/runs/investor-monthly-statement-202606/manifest"],
            PublicationManifestId: "pub-board-1",
            PublicationRetainedManifestPath: "workstation/reporting/publications/pub-board-1/manifest.json",
            PublicationSignedOffBy: "fund-controller",
            PublicationSignedOffAtUtc: generatedAt.AddMinutes(-4),
            PublicationEvidenceLinks:
            [
                lineEvidence,
                new ReportPackEvidenceLinkDto(
                    "publication-evidence:report-pack",
                    "Published report pack",
                    "/api/workstation/evidence/subjects/report-pack/current/packet",
                    "report-pack-publication",
                    generatedAt.AddMinutes(-4))
            ],
            LineProvenance:
            [
                new ReportPackLineProvenanceDto(
                    "trial-balance.cash",
                    "ledger",
                    "ledger-entry-1",
                    "ledger-evidence-1",
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
                    ApprovalId: "approval-1")
            ],
            RestatementReasonCode: "NAV_CORRECTION",
            RestatementPriorVersionReportId: reportId,
            RestatementApprover: "fund-controller",
            RestatementChangedLines:
            [
                new ReportPackChangedLineDto(
                    "trial-balance.cash",
                    "100.00",
                    "101.00",
                    [restatementEvidence])
            ],
            RestatementEvidenceLinks: [restatementEvidence],
            DeliveryEvidencePacket: new ReportPackDeliveryEvidencePacketDto(
                PacketId: "reporting-run-delivery:pkg-board-1",
                PacketKind: "ReportingRunDelivery",
                PackageId: "pkg-board-1",
                ReportId: reportId,
                FundProfileId: "fund-alpha",
                FundAccountId: "investor-monthly-statement",
                Period: "2026-06",
                PackageContents: ["board-pack.pdf"],
                SupportEvidenceIds: ["delivery-artifact:pdf", "publication-evidence:report-pack"],
                RecipientList:
                [
                    new ReportPackDeliveryRecipientDto(
                        "board-reporting-committee",
                        "Board reporting committee",
                        "Board",
                        "Board portal")
                ],
                EntitlementScope: "CompanyWide",
                ApprovalChain:
                [
                    new ReportPackDeliveryApprovalStepDto(
                        generatedAt.AddMinutes(-4),
                        "fund-controller",
                        "Published",
                        ReportPackWorkflowStateDto.Approved,
                        ReportPackWorkflowStateDto.Published,
                        "Approved package for delivery.")
                ],
                DatasetVersion: "investor-monthly-statement-202606",
                TemplateVersion: "investor-monthly-statement",
                DeliveryChannel: "SecurePortal via Board portal",
                DeliveredAtUtc: generatedAt,
                DeliveryEvidence: [deliveryEvidence],
                RequestHistory:
                [
                    "reporting-run:investor-monthly-statement-202606:Scheduled:Draft",
                    "schedule:sched-investor",
                    "delivery-request:board-reporting-committee"
                ],
                AmendmentReason: "NAV_CORRECTION",
                RestatementLineage: "prior-version:11111111-1111-1111-1111-111111111111;changed-lines:trial-balance.cash",
                AuditEventReferences: ["investor-monthly-statement-202606:1:RunGenerated"],
                BlockedDownstreamOutputs: []),
            BrandingTheme: new ReportBrandingThemeDto(
                "board-theme",
                "Board Pack",
                "Meridian Capital",
                "#0f766e",
                "#c2410c",
                "#111827",
                "#ffffff",
                LogoUri: "/brand/meridian.svg",
                FooterText: "Confidential",
                Disclaimer: "For approved recipients only.",
                IsBuiltIn: false),
            ReportingRunSectionCount: 4,
            ReportingRunLineageLinkedSections: 4);

        return new ReportPackDeliveryAttemptDto(
            AttemptId: attemptId,
            ReportId: reportId,
            DistributionId: "board-reporting-committee",
            Recipient: "Board reporting committee",
            RecipientRole: "Board",
            Channel: "Board portal",
            State: ReportPackDeliveryStateDto.Delivered,
            AttemptedAtUtc: generatedAt,
            Actor: "fund-controller",
            AttemptNumber: 1,
            DeliveryReference: "board-portal:packet-1",
            Note: "Delivered after approval.",
            EvidenceLinks: [deliveryEvidence],
            Package: package);
    }

    private static bool IsRouteOnlyArtifact(JsonElement artifact, string kind, string route)
        => artifact.GetProperty("kind").GetString() == kind &&
           artifact.GetProperty("route").GetString() == route &&
           artifact.GetProperty("path").ValueKind == JsonValueKind.Null &&
           artifact.GetProperty("hash").ValueKind == JsonValueKind.Null &&
           artifact.GetProperty("canonicalSubjectKind").GetString() == EvidenceSubjectResolver.StrategyRunKind &&
           artifact.GetProperty("canonicalSubjectId").GetString() == "run-ledger-proof";

    private sealed class StubManualJournalEntryWorkbenchService : IManualJournalEntryWorkbenchService
    {
        private readonly PrivateCapitalActivityProjectionDto _activity;

        public StubManualJournalEntryWorkbenchService(PrivateCapitalActivityProjectionDto activity)
        {
            _activity = activity;
        }

        public Guid? RequiredLedgerBookId { get; set; }

        public List<Guid?> PrivateCapitalActivityLedgerBookRequests { get; } = [];

        public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>([_activity.FundProfileId]);
        }

        public Task<ManualJournalEntryWorkbenchDto> GetWorkbenchAsync(
            string? fundProfileId = null,
            Guid? ledgerBookId = null,
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
            => Task.FromResult(new ManualJournalEntryWorkbenchDto(
                _activity.FundProfileId,
                _activity.LedgerBookId,
                _activity.ProjectedAtUtc,
                LedgerBooks: [],
                ChartOfAccounts: [],
                Drafts: [],
                AuditTrail: [],
                PrivateCapitalActivity: _activity));

        public Task<PrivateCapitalActivityProjectionDto> GetPrivateCapitalActivityAsync(
            string? fundProfileId = null,
            Guid? ledgerBookId = null,
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
        {
            PrivateCapitalActivityLedgerBookRequests.Add(ledgerBookId);
            if (RequiredLedgerBookId.HasValue && ledgerBookId != RequiredLedgerBookId)
            {
                throw new InvalidOperationException(
                    $"Expected ledger book '{RequiredLedgerBookId:D}' but received '{ledgerBookId?.ToString("D") ?? "null"}'.");
            }

            return Task.FromResult(_activity);
        }

        public Task<ManualJournalEntryDraftDto> SaveDraftAsync(
            SaveManualJournalEntryDraftRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("Evidence review tests do not mutate manual journal drafts.");

        public Task<ManualJournalEntryDraftDto> ValidateDraftAsync(
            ValidateManualJournalEntryDraftRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("Evidence review tests do not mutate manual journal drafts.");

        public Task<ManualJournalEntryDraftDto> SubmitApprovalAsync(
            SubmitManualJournalEntryApprovalRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("Evidence review tests do not mutate manual journal drafts.");
    }

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

    private sealed class InMemoryReportPackDeliveryRecordStore(IReadOnlyList<ReportPackDeliveryAttemptDto> attempts)
        : IReportPackDeliveryRecordStore
    {
        public IReadOnlyList<ReportPackDeliveryAttemptDto> Load() => attempts;

        public void Save(IReadOnlyList<ReportPackDeliveryAttemptDto> attempts)
        {
        }
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

        public Task<OperationsTransitionResultDto> AssignBreakCaseAsync(Guid workflowId, string breakId, OperationsAssignBreakCaseRequestDto request, CancellationToken ct = default)
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
            CancellationToken ct = default,
            Guid? ledgerBookId = null)
        {
            ct.ThrowIfCancellationRequested();
            var summaries = _workflows.Values
                .Where(workflow => !fundAccountId.HasValue || workflow.FundAccountId == fundAccountId.Value)
                .Where(workflow => string.IsNullOrWhiteSpace(periodId) || string.Equals(workflow.PeriodId, periodId, StringComparison.OrdinalIgnoreCase))
                .Where(workflow => !ledgerBookId.HasValue || workflow.LedgerBookId == ledgerBookId.Value)
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
                    workflow.NextActions,
                    workflow.LedgerBookId))
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
