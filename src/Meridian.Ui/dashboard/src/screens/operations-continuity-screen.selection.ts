import type { PrivateCapitalCloseCockpitQuery } from "@/lib/api";
import type { OperationsContinuityWorkflowSummary } from "@/types";

export interface OperationsContinuityWorkflowFilters {
  fundAccountId?: string;
  ledgerBookId?: string;
  periodId?: string;
  status?: string;
}

export interface OperationsContinuityScreenSelection {
  initialWorkflowId?: string | null;
  filters?: OperationsContinuityWorkflowFilters;
}

export function normalizeWorkflowSelection(selection: OperationsContinuityScreenSelection) {
  return {
    initialWorkflowId: selection.initialWorkflowId?.trim() || null,
    fundAccountId: selection.filters?.fundAccountId?.trim() || undefined,
    ledgerBookId: selection.filters?.ledgerBookId?.trim() || undefined,
    periodId: selection.filters?.periodId?.trim() || undefined,
    status: selection.filters?.status?.trim() || undefined
  };
}

export function buildWorkflowFilters({ fundAccountId, ledgerBookId, periodId, status }: OperationsContinuityWorkflowFilters): OperationsContinuityWorkflowFilters {
  return {
    ...(fundAccountId ? { fundAccountId } : {}),
    ...(ledgerBookId ? { ledgerBookId } : {}),
    ...(periodId ? { periodId } : {}),
    ...(status ? { status } : {})
  };
}

export function buildRequestedWorkflowUnavailableError(workflowId: string, { ledgerBookId, periodId }: OperationsContinuityWorkflowFilters): string {
  const scopeLabel = [
    ledgerBookId ? `ledger book ${ledgerBookId}` : null,
    periodId ? `accounting period ${periodId}` : null
  ].filter(Boolean).join(" and ");
  return `Requested trusted close workflow ${workflowId} is not available${scopeLabel ? ` for ${scopeLabel}` : " in this workstation scope"}.`;
}

export function selectWorkflowId(workflows: OperationsContinuityWorkflowSummary[], current: string | null): string | null {
  if (current && workflows.some((workflow) => workflow.workflowId === current)) {
    return current;
  }

  return workflows[0]?.workflowId ?? null;
}

export function selectWorkflowSummary(workflows: OperationsContinuityWorkflowSummary[], workflowId: string | null, blocked: boolean): OperationsContinuityWorkflowSummary | null {
  if (blocked) {
    return null;
  }

  return workflows.find((workflow) => workflow.workflowId === workflowId) ?? workflows[0] ?? null;
}

export function buildCloseCockpitQuery(workflow: OperationsContinuityWorkflowSummary): PrivateCapitalCloseCockpitQuery {
  return {
    fundAccountId: workflow.fundAccountId,
    periodId: workflow.periodId
  };
}

export function buildCloseCalendarQuery(workflow: OperationsContinuityWorkflowSummary): { fundAccountId: string; periodId: string } {
  return {
    fundAccountId: workflow.fundAccountId,
    periodId: workflow.periodId
  };
}
