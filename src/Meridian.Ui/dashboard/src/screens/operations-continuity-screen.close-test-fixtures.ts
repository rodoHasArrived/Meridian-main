import type { FinancialOperationsCommandCenter, OperationsContinuityWorkflow } from "@/types";

/** A server response fixture, supplied separately from locally ready workflow diagnostics. */
export function sharedCloseDecision(workflow: OperationsContinuityWorkflow): FinancialOperationsCommandCenter {
  return {
    generatedAtUtc: "2026-09-04T12:00:00Z", fundProfileId: "fund-alpha", ledgerBookId: "book-alpha",
    fundAccountId: workflow.fundAccountId, periodId: workflow.periodId, status: "Ready", isReadyToComplete: true,
    summary: "All required close evidence is current and scoped.", activeItemCount: 0, blockedItemCount: 0,
    reviewItemCount: 0, metrics: [], queueRows: [], activeWorkflow: workflow,
    closeReadiness: {
      scope: { fundProfileId: "fund-alpha", ledgerBookId: "book-alpha", fundAccountId: workflow.fundAccountId,
        entityId: "entity-alpha", periodId: workflow.periodId },
      evaluatedAtUtc: "2026-09-04T12:00:00Z", status: "Ready", isComplete: true, isReadyToClose: true,
      contributors: [], blockers: []
    }
  };
}
