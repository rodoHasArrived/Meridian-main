import type { WorkflowContinuityStepStatus } from "@/app-shell.workflow-continuity";
import type { SessionInfo, SystemOverviewResponse } from "@/types";

export interface ProviderSetupContinuityInput {
  session: SessionInfo | null;
  overview: SystemOverviewResponse | null;
  error: string | null;
  workflowError: string | null;
}

export function buildProviderSetupContinuityStatus({
  session,
  overview,
  error,
  workflowError
}: ProviderSetupContinuityInput): WorkflowContinuityStepStatus {
  if (error || workflowError) {
    return { label: "Review", tone: "review" };
  }

  return session || overview
    ? { label: "Available", tone: "ready" }
    : { label: "Waiting", tone: "pending" };
}
