import type { WorkflowContinuityStepStatus } from "@/app-shell.workflow-continuity";
import { countPendingReportPackDistributions, getReportPackDistributions } from "@/lib/reporting-distributions";
import type { ReportingWorkspaceResponse } from "@/types";

export function buildReportingGovernedReportContinuityStatus(
  reporting: ReportingWorkspaceResponse | null
): WorkflowContinuityStepStatus {
  if (!reporting) {
    return { label: "Waiting", tone: "pending" };
  }

  const distributions = getReportPackDistributions(reporting);
  const pendingCount = countPendingReportPackDistributions(reporting);
  return distributions.length > 0
    ? { label: pendingCount > 0 ? `${pendingCount} pending` : `${distributions.length} recipients`, tone: pendingCount > 0 ? "review" : "ready" }
    : { label: "Needs recipient", tone: "review" };
}
