import type { OperationsContinuityWorkflow, OperationsContinuityWorkflowSummary } from "@/types";

export function workflowMatchesSummary(detail: OperationsContinuityWorkflow, summary: OperationsContinuityWorkflowSummary): boolean {
  return detail.workflowId === summary.workflowId
    && detail.version === summary.version
    && detail.fundAccountId === summary.fundAccountId
    && detail.periodId === summary.periodId
    && (detail.ledgerBookId ?? null) === (summary.ledgerBookId ?? null);
}

export function compareWorkflowSummaries(left: OperationsContinuityWorkflowSummary, right: OperationsContinuityWorkflowSummary): number {
  return right.updatedAtUtc.localeCompare(left.updatedAtUtc);
}
