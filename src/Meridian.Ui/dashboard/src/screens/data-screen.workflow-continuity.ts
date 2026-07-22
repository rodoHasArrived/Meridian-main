import type { WorkflowContinuityStepStatus } from "@/app-shell.workflow-continuity";
import type { DataWorkspaceResponse } from "@/types";

export function buildTrustedDataContinuityStatus(data: DataWorkspaceResponse | null): WorkflowContinuityStepStatus {
  if (!data) {
    return { label: "Waiting", tone: "pending" };
  }

  const providerAttentionCount = (data.providers ?? []).filter((provider) => provider.status !== "Healthy").length;
  const backfillAttentionCount = (data.backfills ?? []).filter((backfill) => backfill.status === "Review").length;
  const attentionCount = providerAttentionCount + backfillAttentionCount;
  return attentionCount > 0
    ? { label: `${attentionCount} review`, tone: "review" }
    : { label: "Trusted", tone: "ready" };
}
