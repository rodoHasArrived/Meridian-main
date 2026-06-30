import type { WorkflowContinuityStepStatus } from "@/app-shell.workflow-continuity";
import type {
  AccountingWorkspaceResponse,
  OperatorWorkflowHomeSummary,
  WorkspaceWorkflowSummary
} from "@/types";

const financialOperationsStepOrder: Record<string, number> = {
  "receive-activity": 0,
  "match-records": 1,
  "resolve-exceptions": 2,
  "approve-results": 3,
  "produce-evidence": 4,
  "close-support": 5
};

export function buildFinancialOperationsWorkflowStepStatus(
  stepId: string,
  workflowSummary: OperatorWorkflowHomeSummary | null
): WorkflowContinuityStepStatus | null {
  const stepIndex = financialOperationsStepOrder[stepId];
  if (stepIndex === undefined) {
    return null;
  }

  const summary = getAccountingWorkflowSummary(workflowSummary);
  if (!summary || !isFinancialOperationsWorkflowSummary(summary)) {
    return null;
  }

  if (isFinancialOperationsEvidenceProduced(summary)) {
    return {
      label: stepId === "produce-evidence" ? "Produced" : "Complete",
      tone: "ready"
    };
  }

  const activeIndex = resolveFinancialOperationsActiveStepIndex(summary);
  if (stepIndex < activeIndex) {
    return { label: "Complete", tone: "ready" };
  }

  if (stepIndex > activeIndex) {
    return { label: "Waiting", tone: "pending" };
  }

  return {
    label: buildFinancialOperationsActiveStepLabel(stepId, summary),
    tone: workflowSummaryToneToContinuityTone(summary.statusTone, summary.primaryBlocker)
  };
}

export function buildAccountingReconciliationContinuityStatus(
  accounting: AccountingWorkspaceResponse | null
): WorkflowContinuityStepStatus {
  if (!accounting) {
    return { label: "Waiting", tone: "pending" };
  }

  const breakCount = (accounting.breakQueue ?? []).filter((item) => item.status === "Open" || item.status === "InReview").length;
  const runBreakCount = (accounting.reconciliationQueue ?? []).reduce((total, row) => total + row.openBreakCount, 0);
  const attentionCount = Math.max(breakCount, runBreakCount);
  return attentionCount > 0
    ? { label: `${attentionCount} breaks`, tone: "review" }
    : { label: "Balanced", tone: "ready" };
}

export function buildAccountingCloseSupportContinuityStatus(
  accounting: AccountingWorkspaceResponse | null
): WorkflowContinuityStepStatus {
  return accounting
    ? { label: "Review", tone: "review" }
    : { label: "Waiting", tone: "pending" };
}

function getAccountingWorkflowSummary(summary: OperatorWorkflowHomeSummary | null | undefined): WorkspaceWorkflowSummary | null {
  return summary?.workspaces?.find((workspace) => workspace.workspaceId.toLowerCase() === "accounting") ?? null;
}

function isFinancialOperationsWorkflowSummary(summary: WorkspaceWorkflowSummary): boolean {
  const sourceText = [
    summary.statusLabel,
    summary.statusDetail,
    summary.nextAction?.label,
    summary.primaryBlocker?.code,
    summary.primaryBlocker?.label,
    ...((summary.evidence ?? []).map((badge) => `${badge.label} ${badge.value}`))
  ].join(" ").toLowerCase();

  return sourceText.includes("financial operations") || sourceText.includes("core flow");
}

function isFinancialOperationsEvidenceProduced(summary: WorkspaceWorkflowSummary): boolean {
  return normalizeWorkflowText(summary.primaryBlocker?.code).includes("evidence-produced")
    || normalizeWorkflowText(summary.statusLabel).includes("evidence produced")
    || normalizeWorkflowText(summary.nextAction?.label).includes("open evidence packet");
}

function resolveFinancialOperationsActiveStepIndex(summary: WorkspaceWorkflowSummary): number {
  const nextAction = normalizeWorkflowText(summary.nextAction?.label);
  if (nextAction.includes("receive activity")) return 0;
  if (nextAction.includes("match records")) return 1;
  if (nextAction.includes("resolve exceptions")) return 2;
  if (nextAction.includes("approve results")) return 3;
  if (nextAction.includes("close support") || nextAction.includes("close readiness") || nextAction.includes("evidence package")) return 5;
  if (nextAction.includes("evidence packet") || nextAction.includes("produce evidence")) return 4;

  const coreFlow = normalizeWorkflowText(getWorkflowEvidenceValue(summary, "Core flow"));
  if (coreFlow.includes("receive activity")) return 0;
  if (coreFlow.includes("match records")) return 1;
  if (coreFlow.includes("resolve exceptions")) return 2;
  if (coreFlow.includes("approve results")) return 3;
  if (coreFlow.includes("produce evidence")) return 4;
  if (coreFlow.includes("close support")) return 5;

  const status = normalizeWorkflowText(summary.statusLabel);
  if (status.includes("not started")) return 0;
  if (status.includes("exception")) return 2;
  if (status.includes("approval")) return 3;
  if (status.includes("close readiness") || status.includes("evidence package")) return 5;
  return 1;
}

function buildFinancialOperationsActiveStepLabel(stepId: string, summary: WorkspaceWorkflowSummary): string {
  switch (stepId) {
    case "receive-activity":
      return normalizeWorkflowText(summary.statusLabel).includes("not started") ? "Start" : "Received";
    case "match-records":
      return "Match";
    case "resolve-exceptions": {
      const breakCount = getWorkflowEvidenceValue(summary, "Breaks");
      return breakCount && breakCount !== "0" ? `${breakCount} breaks` : "Resolve";
    }
    case "approve-results": {
      const approval = getWorkflowEvidenceValue(summary, "Approval");
      return approval && approval !== "Approved" ? "Approval pending" : "Approve";
    }
    case "produce-evidence":
      return "Evidence";
    case "close-support": {
      const status = normalizeWorkflowText(summary.statusLabel);
      if (status.includes("evidence package")) return "Package review";
      if (status.includes("close readiness")) return "Close blocked";

      const periodLock = getWorkflowEvidenceValue(summary, "Period lock");
      if (periodLock && periodLock !== "Not projected") return periodLock;

      const close = getWorkflowEvidenceValue(summary, "Close");
      return close && close !== "Pending" ? close : "Close review";
    }
    default:
      return summary.nextAction?.label ?? "Review";
  }
}

function getWorkflowEvidenceValue(summary: WorkspaceWorkflowSummary, label: string): string | null {
  return summary.evidence?.find((badge) => badge.label.toLowerCase() === label.toLowerCase())?.value ?? null;
}

function workflowSummaryToneToContinuityTone(
  statusTone: string | null | undefined,
  blocker: WorkspaceWorkflowSummary["primaryBlocker"]
): WorkflowContinuityStepStatus["tone"] {
  const normalizedTone = normalizeWorkflowText(statusTone);
  if (normalizedTone.includes("success")) return "ready";
  if (normalizedTone.includes("danger") || normalizedTone.includes("error")) return "blocked";
  if (blocker?.isBlocking && normalizedTone.includes("warning")) return "review";
  if (normalizedTone.includes("warning") || normalizedTone.includes("info")) return "review";
  return blocker?.isBlocking ? "review" : "pending";
}

function normalizeWorkflowText(value: string | null | undefined): string {
  return value?.trim().toLowerCase() ?? "";
}
