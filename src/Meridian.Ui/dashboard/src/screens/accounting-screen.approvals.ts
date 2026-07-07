import type { OperationsApproval, OperationsApprovalState, OperationsContinuityWorkflow, OperationsContinuityWorkflowSummary, OperationsWorkflowBlocker } from "@/types";

// Approvals-workstream pure helpers extracted from accounting-screen.tsx to keep the
// screen root focused on rendering. Status labels/tones, evidence summaries, blocker and
// next-action derivation, and small formatters for the accounting approvals queue.

export function approvalQueueStatusLabel(workflow: OperationsContinuityWorkflowSummary): string {
  const approvalGate = workflow.gates.find((gate) => gate.gateKey === "Approval") ?? null;
  if (workflow.status === "Blocked" || approvalGate?.status === "Blocked") {
    return "Blocked";
  }

  if (workflow.status === "Closed" || workflow.status === "ReadyForClose" || approvalGate?.status === "Passed") {
    return "Approved";
  }

  return "Pending";
}

export function approvalQueueStatusTone(workflow: OperationsContinuityWorkflowSummary): "success" | "warning" | "danger" | "outline" {
  const label = approvalQueueStatusLabel(workflow);
  if (label === "Approved") return "success";
  if (label === "Blocked") return "danger";
  return "warning";
}

export function approvalStatusTone(status: OperationsApprovalState): "success" | "warning" | "danger" | "outline" {
  if (status === "Approved") return "success";
  if (status === "Rejected") return "danger";
  if (status === "Pending") return "outline";
  return "warning";
}

export function approvalSignerLabel(approvals: OperationsApproval[]): string {
  const signers = approvals
    .flatMap((approval) => [approval.reviewer, approval.operator])
    .map((value) => value?.trim())
    .filter((value): value is string => Boolean(value));
  const unique = [...new Set(signers)];
  return unique.length > 0 ? unique.join(", ") : "Reviewer pending";
}

export function approvalEvidenceSummary(workflow: OperationsContinuityWorkflow | null): string {
  if (!workflow) {
    return "Detail pending";
  }

  const count = workflow.evidenceLinks.length
    + workflow.reportPackReadiness.evidenceLinks.length
    + workflow.approvals.reduce((total, approval) => total + approval.evidenceLinks.length, 0);
  return count === 0 ? "No evidence links" : `${count} evidence link${count === 1 ? "" : "s"}`;
}

export function missingApprovalEvidenceLabel(workflow: OperationsContinuityWorkflow | null): string {
  if (!workflow) {
    return "Detail pending";
  }

  const missing: string[] = [];
  if (!resolveApprovalReportPackId(workflow)) {
    missing.push("report pack");
  }

  if (!workflow.reportPackReadiness.isReady) {
    missing.push("report readiness");
  }

  const blockerEvidenceMissing = approvalBlockers(workflow).some((blocker) => blocker.evidenceLinks.length === 0);
  if (blockerEvidenceMissing) {
    missing.push("blocker evidence");
  }

  return missing.length === 0 ? "None" : missing.join(", ");
}

export function approvalBlockedReason(workflow: OperationsContinuityWorkflow | null): string {
  if (!workflow) {
    return "Approval detail is still loading.";
  }

  const blockers = approvalBlockers(workflow);
  if (blockers.length > 0) {
    return blockers.map((blocker) => blocker.message).join(" ");
  }

  if (!workflow.reportPackReadiness.isReady) {
    return workflow.reportPackReadiness.blockingReason ?? "Report pack readiness is still blocked.";
  }

  return "No approval blockers are surfaced for the selected workflow.";
}

export function approvalNextAction(workflow: OperationsContinuityWorkflow | null, summary: OperationsContinuityWorkflowSummary): string {
  const source = workflow ?? summary;
  const approvalAction = source.nextActions.find((action) => action.gate === "Approval") ?? source.nextActions[0] ?? null;
  if (approvalAction) {
    return approvalAction.label;
  }

  if (approvalQueueStatusLabel(summary) === "Approved") {
    return "Approval is complete; continue to evidence production or close package publication.";
  }

  return "Review blockers, evidence, and required signers before taking an approval action.";
}

export function approvalBlockers(workflow: OperationsContinuityWorkflow): OperationsWorkflowBlocker[] {
  return [
    ...workflow.blockers,
    ...workflow.gates.flatMap((gate) => gate.gateKey === "Approval" ? gate.blockers : [])
  ];
}

export function buildApprovalActionDisabledReason(
  workflow: OperationsContinuityWorkflow | OperationsContinuityWorkflowSummary | null,
  approval: OperationsApproval | null,
  action: string | null
): string | null {
  if (action) {
    return "Wait for the current approval action to finish.";
  }

  if (!workflow) {
    return "Select an approval before approving.";
  }

  if (approval?.status === "Approved") {
    return "This approval is already approved.";
  }

  if ("reportPackReadiness" in workflow && !resolveApprovalReportPackId(workflow)) {
    return "Approval requires a report pack id from the selected workflow.";
  }

  return null;
}

export function resolveApprovalReportPackId(workflow: OperationsContinuityWorkflow | OperationsContinuityWorkflowSummary): string | null {
  if ("closePackage" in workflow && workflow.closePackage?.reportPackId) {
    return workflow.closePackage.reportPackId;
  }

  if ("reportPackReadiness" in workflow && workflow.reportPackReadiness.reportPackId) {
    return workflow.reportPackReadiness.reportPackId;
  }

  return null;
}

export function formatApprovalDate(value: string | null | undefined): string {
  if (!value) {
    return "Not recorded";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("en-US", { timeZone: "UTC" });
}

export function splitApprovalWords(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, "$1 $2");
}

export function formatApprovalError(err: unknown, fallback: string): string {
  return err instanceof Error ? err.message || fallback : fallback;
}
