import type { WorkflowContinuityStepStatus } from "@/app-shell.workflow-continuity";
import type { TradingWorkspaceResponse } from "@/types";

export function buildPaperReadinessContinuityStatus(trading: TradingWorkspaceResponse | null): WorkflowContinuityStepStatus {
  const readiness = trading?.readiness ?? null;
  if (!readiness) {
    return trading ? { label: "Review", tone: "review" } : { label: "Waiting", tone: "pending" };
  }

  const criticalCount = (readiness.workItems ?? []).filter((item) => item.tone === "Critical").length;
  if (readiness.overallStatus === "Blocked" || criticalCount > 0) {
    return { label: criticalCount > 0 ? `${criticalCount} critical` : "Blocked", tone: "blocked" };
  }

  const attentionCount = (readiness.workItems ?? []).filter((item) => item.tone === "Warning" || item.tone === "Info").length;
  if (readiness.overallStatus === "ReviewRequired" || attentionCount > 0) {
    return { label: attentionCount > 0 ? `${attentionCount} review` : "Review", tone: "review" };
  }

  return readiness.readyForPaperOperation
    ? { label: "Ready", tone: "ready" }
    : { label: "Review", tone: "review" };
}
