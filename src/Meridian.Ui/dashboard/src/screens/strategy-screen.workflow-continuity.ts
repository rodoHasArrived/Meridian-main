import type { WorkflowContinuityStepStatus } from "@/app-shell.workflow-continuity";
import type { StrategyWorkspaceResponse } from "@/types";

export function buildStrategyContinuityStatus(
  strategy: StrategyWorkspaceResponse | null,
  workflowError: string | null
): WorkflowContinuityStepStatus {
  if (workflowError) {
    return { label: "Catalog degraded", tone: "review" };
  }

  if (!strategy) {
    return { label: "Waiting", tone: "pending" };
  }

  const reviewCount = (strategy.runs ?? []).filter((run) => run.status === "Needs Review").length;
  if (reviewCount > 0) {
    return { label: `${reviewCount} review`, tone: "review" };
  }

  const activeCount = (strategy.runs ?? []).filter((run) => run.status === "Running" || run.status === "Queued").length;
  if (activeCount > 0) {
    return { label: `${activeCount} active`, tone: "review" };
  }

  return { label: (strategy.runs ?? []).length > 0 ? `${strategy.runs.length} runs` : "Ready", tone: "ready" };
}
