import type { FinancialOperationsCommandCenter, OperationsContinuityWorkflow } from "@/types";
import type { CloseWorkflowQuery } from "./accounting-screen.close-sources";

const scopeFields = ["fundProfileId", "fundAccountId", "ledgerBookId", "entityId", "periodId"] as const;

export function accountingCloseScopeKey(scope: CloseWorkflowQuery | null) {
  return JSON.stringify(scopeFields.map(field => scope?.[field]?.trim() || null));
}

export function accountingCloseReadinessBlockReason(
  workflow: OperationsContinuityWorkflow | null,
  commandCenter: FinancialOperationsCommandCenter | null,
  selectedScope: CloseWorkflowQuery | null,
): string | null {
  const readiness = commandCenter?.closeReadiness;
  if (!selectedScope || scopeFields.some(field => !selectedScope[field]?.trim())
      || !readiness || accountingCloseScopeKey(readiness.scope) !== accountingCloseScopeKey(selectedScope)) {
    return "Select the full close scope and refresh shared close readiness before locking the period.";
  }
  if (!workflow || commandCenter.activeWorkflow?.workflowId !== workflow.workflowId
      || commandCenter.activeWorkflow.version !== workflow.version) {
    return "Refresh shared close readiness for the selected workflow version before locking the period.";
  }
  return readiness.isComplete && readiness.isReadyToClose && readiness.status === "Ready" && readiness.blockers.length === 0
    ? null
    : readiness.blockers[0]?.message ?? "Resolve the shared close readiness blockers before locking the period.";
}
