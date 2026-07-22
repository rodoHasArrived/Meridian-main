import { normalizeLocalWorkstationRoute } from "@/lib/workspace";
import type {
  OperationsContinuityWorkflow,
  OperationsReviewedAutomationArtifact,
  EvidenceStatus
} from "@/types";
import type {
  OperationsContinuityTone,
  OperationsReviewedAutomationArtifactRow,
  OperationsReviewedAutomationViewModel
} from "@/screens/operations-continuity-screen.view-model";

export function buildReviewedAutomationViewModel(
  reviewedAutomation: NonNullable<OperationsContinuityWorkflow["reviewedAutomation"]> | null,
  loading: boolean
): OperationsReviewedAutomationViewModel {
  if (!reviewedAutomation) {
    return {
      title: "Reviewed automation",
      statusLabel: loading ? "Loading" : "Missing",
      statusTone: loading ? "neutral" : "review",
      stageLabel: loading ? "Loading review stage" : "Review stage unavailable",
      reviewLabel: loading ? "Review state pending" : "No reviewed automation posture returned",
      summaryLabel: loading
        ? "Loading reviewed automation posture from the shared operations API."
        : "The shared operations API did not return reviewed automation posture.",
      allowedUseCasesLabel: loading ? "Loading allowed uses" : "No allowed use cases returned",
      prohibitedActionsLabel: loading ? "Loading prohibited actions" : "No prohibited actions returned",
      evidenceLabel: "No retained review evidence",
      requiredActionsLabel: loading ? "Loading required actions" : "Return reviewed automation posture before relying on local automation state.",
      artifactsEmptyText: loading ? "Loading reviewed automation output queue..." : "No reviewed automation output queue returned.",
      artifacts: []
    };
  }

  const requiredActions = (reviewedAutomation.requiredActions ?? [])
    .map((action) => action.trim())
    .filter(Boolean);
  const evidenceCount = reviewedAutomation.evidenceLinks.length;
  const artifacts = buildReviewedAutomationArtifactRows(reviewedAutomation.artifacts ?? []);

  return {
    title: "Reviewed automation",
    statusLabel: evidenceStatusLabel(reviewedAutomation.status),
    statusTone: reviewedAutomation.requiresHumanReview
      ? evidenceStatusTone(reviewedAutomation.status)
      : reviewedAutomation.status === "Ready"
        ? "ready"
        : evidenceStatusTone(reviewedAutomation.status),
    stageLabel: `Stage: ${reviewedAutomation.stage?.trim() || "Stage pending"}`,
    reviewLabel: reviewedAutomation.requiresHumanReview ? "Human review required" : "Human review complete",
    summaryLabel: reviewedAutomation.summary?.trim() || "No reviewed automation summary returned.",
    allowedUseCasesLabel: formatReviewedAutomationList(reviewedAutomation.allowedUseCases, "No allowed use cases returned"),
    prohibitedActionsLabel: formatReviewedAutomationList(reviewedAutomation.prohibitedActions, "No prohibited actions returned"),
    evidenceLabel: evidenceCount === 0 ? "No retained review evidence" : `${evidenceCount} retained review evidence link${evidenceCount === 1 ? "" : "s"}`,
    requiredActionsLabel: requiredActions.length === 0 ? "No required actions" : requiredActions.join("; "),
    artifactsEmptyText: "No reviewed automation outputs are queued for this workflow stage.",
    artifacts
  };
}

export function evidenceStatusTone(status: EvidenceStatus): OperationsContinuityTone {
  switch (status) {
    case "Ready":
      return "ready";
    case "Blocked":
    case "Missing":
      return "blocked";
    case "ReviewRequired":
    case "Stale":
      return "review";
    default:
      return "neutral";
  }
}

export function evidenceStatusLabel(status: EvidenceStatus): string {
  return status
    .replace(/[-_]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/\s+/g, " ")
    .trim();
}

function buildReviewedAutomationArtifactRows(
  artifacts: OperationsReviewedAutomationArtifact[]
): OperationsReviewedAutomationArtifactRow[] {
  return artifacts.map((artifact) => {
    const evidenceCount = artifact.evidenceLinks.length;
    const evidenceHref = normalizeLocalWorkstationRoute(artifact.evidenceLinks[0]?.route) ?? null;
    const checklist = (artifact.reviewChecklist ?? [])
      .map((item) => item.trim())
      .filter(Boolean);
    const title = artifact.title?.trim() || "Reviewed automation output";
    const kindLabel = artifact.artifactKind?.trim() || "Automation output";
    return {
      id: artifact.artifactId,
      kindLabel,
      title,
      statusLabel: evidenceStatusLabel(artifact.status),
      statusTone: artifact.requiresHumanReview ? evidenceStatusTone(artifact.status) : "ready",
      reviewLabel: artifact.requiresHumanReview ? "Human review required" : "Review complete",
      confidenceLabel: formatReviewedAutomationConfidence(artifact.confidencePercent),
      sourceSummary: artifact.sourceSummary?.trim() || "No source summary returned.",
      evidenceLabel: evidenceCount === 0 ? "No retained evidence" : `${evidenceCount} evidence link${evidenceCount === 1 ? "" : "s"}`,
      evidenceHref,
      evidenceRouteLabel: evidenceHref ? "Open reviewed automation evidence" : "No local evidence route",
      suggestedActionLabel: artifact.suggestedOperatorAction?.trim() || "No suggested operator action returned.",
      blockedActionLabel: artifact.blockedMaterialAction?.trim() || "No blocked material action returned.",
      checklistLabel: checklist.length === 0 ? "No review checklist returned" : checklist.join("; "),
      ariaLabel: `${kindLabel} ${title}, ${evidenceStatusLabel(artifact.status)}, ${artifact.requiresHumanReview ? "human review required" : "review complete"}`
    };
  });
}

function formatReviewedAutomationList(values: string[], fallback: string): string {
  const normalized = values
    .map((value) => value.trim())
    .filter(Boolean);
  return normalized.length === 0 ? fallback : normalized.join(", ");
}

function formatReviewedAutomationConfidence(value: number | null | undefined): string {
  return typeof value === "number" && Number.isFinite(value)
    ? `${Math.round(value)}% confidence`
    : "Confidence not scored";
}
