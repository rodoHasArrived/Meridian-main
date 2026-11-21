using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

/// <summary>
/// Projects reviewed-automation guidance and artifacts from retained workflow state and evidence.
/// This read model does not authorize or execute material workflow actions.
/// </summary>
internal static class OperationsReviewedAutomationProjection
{
    private static readonly string[] ReviewedAutomationAllowedUseCases =
    [
        "Extract source fields",
        "Classify source records",
        "Suggest reconciliation matches",
        "Explain variances",
        "Detect duplicate records",
        "Draft journal templates",
        "Summarize retained evidence",
        "Flag missing support",
        "Draft report commentary",
        "Draft audit request lists"
    ];

    private static readonly string[] ReviewedAutomationProhibitedActions =
    [
        "Approve its own work",
        "Post material journals without approval",
        "Override period locks",
        "Release payments",
        "Publish reports",
        "Edit posted entries",
        "Erase evidence"
    ];

    internal static OperationsReviewedAutomationSummaryDto Build(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks)
    {
        if (workflow.IsClosed)
        {
            var retainedEvidence = (workflow.ClosePackage?.EvidenceLinks ?? evidenceLinks)
                .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return ReviewedAutomationSummary(
                stage: "Reviewed evidence retained",
                status: EvidenceStatusDto.Ready,
                requiresHumanReview: false,
                summary: "Reviewed automation suggestions are closed behind retained approval and evidence package history.",
                evidenceLinks: retainedEvidence,
                requiredActions: []);
        }

        if (workflow.BrokerIntakeState is OperationsBrokerIntakeStateDto.Imported or OperationsBrokerIntakeStateDto.Normalized)
        {
            return ReviewedAutomationSummary(
                stage: "Extraction review",
                status: EvidenceStatusDto.ReviewRequired,
                requiresHumanReview: true,
                summary: "Automation may extract and classify intake data, but normalized source records require review before matching.",
                evidenceLinks,
                requiredActions:
                [
                    "Review extracted fields and classification confidence before reconciliation.",
                    "Retain intake evidence before downstream ledger or report usage."
                ]);
        }

        if (workflow.ReconciliationState == OperationsReconciliationStateDto.AutoMatched)
        {
            return ReviewedAutomationSummary(
                stage: "Suggested matches require review",
                status: EvidenceStatusDto.ReviewRequired,
                requiresHumanReview: true,
                summary: "Automation may suggest reconciliation matches, but match promotion remains operator-reviewed.",
                evidenceLinks,
                requiredActions:
                [
                    "Review suggested matches, variance explanations, and duplicate flags before approval.",
                    "Resolve or approve exceptions before downstream ledger, close, or reporting actions."
                ]);
        }

        if (workflow.LedgerPostingState is OperationsLedgerPostingStateDto.Drafted or OperationsLedgerPostingStateDto.Validated)
        {
            return ReviewedAutomationSummary(
                stage: "Journal draft review",
                status: EvidenceStatusDto.ReviewRequired,
                requiresHumanReview: true,
                summary: "Automation may draft journal templates, but material journal posting remains approval-gated.",
                evidenceLinks,
                requiredActions:
                [
                    "Review the ledger draft and retained source evidence before posting.",
                    "Do not post material journals from automation output without governed approval."
                ]);
        }

        if (workflow.ReportPackReadiness.IsReady &&
            workflow.ApprovalState == OperationsApprovalStateDto.Pending)
        {
            return ReviewedAutomationSummary(
                stage: "Report commentary and audit request list draft review",
                status: EvidenceStatusDto.ReviewRequired,
                requiresHumanReview: true,
                summary: "Automation may draft report commentary and audit request lists from retained evidence, but report approval and publication remain human-gated.",
                evidenceLinks,
                requiredActions:
                [
                    "Review drafted report commentary and audit request lists against retained evidence before submission.",
                    "Do not publish reports or release support packages from automation output without governed approval."
                ]);
        }

        if (workflow.ApprovalState is OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned)
        {
            return ReviewedAutomationSummary(
                stage: "Reviewer approval required",
                status: EvidenceStatusDto.ReviewRequired,
                requiresHumanReview: true,
                summary: "Automation may draft or summarize support, but approval remains a human reviewer control.",
                evidenceLinks,
                requiredActions:
                [
                    "Complete reviewer approval before close evidence can be released.",
                    "Retain approval rationale and evidence links before publishing reports or closing the period."
                ]);
        }

        var review = workflow.Approvals.LastOrDefault();
        if (workflow.ApprovalState == OperationsApprovalStateDto.Approved &&
            workflow.Gates.All(static gate => gate.Status == OperationsGateStatusDto.Passed) &&
            workflow.ReportPackReadiness.IsReady && !string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId) &&
            review is { Status: OperationsApprovalStateDto.Approved, DecidedAtUtc: not null } &&
            !string.IsNullOrWhiteSpace(review.Reviewer) && !string.IsNullOrWhiteSpace(review.Rationale) &&
            review.EvidenceLinks.Any(link => string.Equals(link.EvidenceId, workflow.ReportPackReadiness.ReportPackId, StringComparison.Ordinal)))
        {
            return ReviewedAutomationSummary(
                stage: "Approved evidence ready for publication",
                status: EvidenceStatusDto.Ready,
                requiresHumanReview: false,
                summary: "The current reviewer decision and report-pack evidence are retained; publication remains a governed operator action.",
                evidenceLinks: review.EvidenceLinks.Concat(evidenceLinks).DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase).ToArray(),
                requiredActions: []);
        }

        return ReviewedAutomationSummary(
            stage: "Suggestions only",
            status: EvidenceStatusDto.ReviewRequired,
            requiresHumanReview: true,
            summary: "Automation is limited to suggestions, summaries, drafts, and missing-support flags until an operator reviews the workflow.",
            evidenceLinks,
            requiredActions:
            [
                "Keep automation output in the review queue before approval, posting, publication, payment, or evidence-retention actions."
            ]);
    }

    private static OperationsReviewedAutomationSummaryDto ReviewedAutomationSummary(
        string stage,
        EvidenceStatusDto status,
        bool requiresHumanReview,
        string summary,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        IReadOnlyList<string> requiredActions)
        => new(
            SummaryId: "reviewed-automation",
            Stage: stage,
            Status: status,
            RequiresHumanReview: requiresHumanReview,
            Summary: summary,
            AllowedUseCases: ReviewedAutomationAllowedUseCases,
            ProhibitedActions: ReviewedAutomationProhibitedActions,
            EvidenceLinks: evidenceLinks,
            RequiredActions: requiredActions,
            Artifacts: BuildReviewedAutomationArtifacts(stage, status, requiresHumanReview, evidenceLinks, requiredActions));

    private static IReadOnlyList<OperationsReviewedAutomationArtifactDto> BuildReviewedAutomationArtifacts(
        string stage,
        EvidenceStatusDto status,
        bool requiresHumanReview,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        IReadOnlyList<string> requiredActions)
    {
        var reviewChecklist = requiredActions.Count == 0
            ? ["Confirm retained approvals and evidence before relying on automation output."]
            : requiredActions;
        var linkedEvidence = evidenceLinks
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        if (stage.Equals("Extraction review", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ReviewedAutomationArtifact(
                    "intake-extraction",
                    "Extraction",
                    "Intake field extraction draft",
                    status,
                    requiresHumanReview,
                    82m,
                    "Broker and administrator intake fields are suggested from retained source evidence.",
                    "Review extracted fields and classification confidence before reconciliation.",
                    "Cannot normalize source records or promote matches without human review.",
                    linkedEvidence,
                    reviewChecklist),
                ReviewedAutomationArtifact(
                    "intake-classification",
                    "Classification",
                    "Source classification suggestion",
                    status,
                    requiresHumanReview,
                    78m,
                    "Automation can classify imported activity for downstream matching.",
                    "Confirm source category and retained evidence before ledger or report usage.",
                    "Cannot mutate normalized records from classification output.",
                    linkedEvidence,
                    reviewChecklist)
            ];
        }

        if (stage.Equals("Suggested matches require review", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ReviewedAutomationArtifact(
                    "reconciliation-match-suggestion",
                    "Suggested match",
                    "Reconciliation match candidate",
                    status,
                    requiresHumanReview,
                    88m,
                    "Suggested matches, variance explanations, and duplicate flags are retained for operator review.",
                    "Review match rationale and variance evidence before resolving breaks.",
                    "Cannot resolve reconciliation breaks or approve exceptions.",
                    linkedEvidence,
                    reviewChecklist),
                ReviewedAutomationArtifact(
                    "duplicate-variance-flag",
                    "Variance flag",
                    "Duplicate and variance explanation",
                    status,
                    requiresHumanReview,
                    74m,
                    "Potential duplicate movements and unresolved variance drivers are flagged from evidence links.",
                    "Confirm duplicate disposition and variance explanation before downstream close use.",
                    "Cannot clear exceptions or change report readiness.",
                    linkedEvidence,
                    reviewChecklist)
            ];
        }

        if (stage.Equals("Journal draft review", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ReviewedAutomationArtifact(
                    "journal-template-draft",
                    "Journal draft",
                    "Journal template draft",
                    status,
                    requiresHumanReview,
                    81m,
                    "Automation can draft journal templates from validated source and reconciliation evidence.",
                    "Review the draft, balance, period, and retained source evidence before posting.",
                    "Cannot post material journals or edit posted entries.",
                    linkedEvidence,
                    reviewChecklist)
            ];
        }

        if (stage.Equals("Report commentary and audit request list draft review", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ReviewedAutomationArtifact(
                    "report-commentary-draft",
                    "Report commentary",
                    "Report commentary draft",
                    status,
                    requiresHumanReview,
                    84m,
                    "Draft commentary is generated from retained close, ledger, reconciliation, and report-pack evidence.",
                    "Review commentary against retained evidence before report approval or publication.",
                    "Cannot publish reports or release support packages.",
                    linkedEvidence,
                    reviewChecklist),
                ReviewedAutomationArtifact(
                    "audit-request-list-draft",
                    "Audit request list",
                    "Audit request list draft",
                    status,
                    requiresHumanReview,
                    79m,
                    "Draft audit request lists summarize missing support and unresolved evidence gaps.",
                    "Review each requested support item and assign an owner before audit release.",
                    "Cannot erase evidence or satisfy audit requests without retained support.",
                    linkedEvidence,
                    reviewChecklist),
                ReviewedAutomationArtifact(
                    "missing-support-flag",
                    "Missing support",
                    "Missing support flag",
                    status,
                    requiresHumanReview,
                    72m,
                    "Missing support flags are derived from incomplete evidence package categories.",
                    "Attach or waive missing support through governed human review.",
                    "Cannot approve its own missing-support disposition.",
                    linkedEvidence,
                    reviewChecklist)
            ];
        }

        if (stage.Equals("Reviewer approval required", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ReviewedAutomationArtifact(
                    "approval-support-summary",
                    "Evidence summary",
                    "Reviewer support summary",
                    status,
                    requiresHumanReview,
                    86m,
                    "Automation may summarize retained support while approval remains human-owned.",
                    "Complete reviewer approval and retain rationale before publication or close.",
                    "Cannot approve its own work or release evidence packages.",
                    linkedEvidence,
                    reviewChecklist)
            ];
        }

        if (stage.Equals("Approved evidence ready for publication", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ReviewedAutomationArtifact(
                    "approved-review-record", "Retained review", "Human review retained", status, requiresHumanReview, null,
                    "The current reviewer decision binds the report-pack support for this workflow.",
                    "Use the governed close command after all shared evidence checks pass.",
                    "Automation cannot approve, publish, or release evidence packages.", linkedEvidence, reviewChecklist)
            ];
        }

        if (stage.Equals("Reviewed evidence retained", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ReviewedAutomationArtifact(
                    "retained-review-record",
                    "Retained review",
                    "Reviewed automation closure record",
                    status,
                    requiresHumanReview,
                    null,
                    "Automation output is closed behind retained approval and evidence package history.",
                    "Use retained audit and evidence package history for downstream support.",
                    "No material automation action is permitted after close.",
                    linkedEvidence,
                    reviewChecklist)
            ];
        }

        return
        [
            ReviewedAutomationArtifact(
                "review-queue-intake",
                "Review queue",
                "Reviewed automation intake queue",
                status,
                requiresHumanReview,
                70m,
                "Automation output is retained as suggestions, summaries, drafts, and missing-support flags.",
                "Route automation output through a human review queue before material action.",
                "Cannot approve, post, publish, pay, reopen, or erase evidence.",
                linkedEvidence,
                reviewChecklist)
        ];
    }

    private static OperationsReviewedAutomationArtifactDto ReviewedAutomationArtifact(
        string artifactId,
        string artifactKind,
        string title,
        EvidenceStatusDto status,
        bool requiresHumanReview,
        decimal? confidencePercent,
        string sourceSummary,
        string suggestedOperatorAction,
        string blockedMaterialAction,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        IReadOnlyList<string> reviewChecklist)
        => new(
            ArtifactId: $"reviewed-automation:{artifactId}",
            ArtifactKind: artifactKind,
            Title: title,
            Status: status,
            RequiresHumanReview: requiresHumanReview,
            ConfidencePercent: confidencePercent,
            SourceSummary: sourceSummary,
            SuggestedOperatorAction: suggestedOperatorAction,
            BlockedMaterialAction: blockedMaterialAction,
            EvidenceLinks: evidenceLinks,
            ReviewChecklist: reviewChecklist);
}
