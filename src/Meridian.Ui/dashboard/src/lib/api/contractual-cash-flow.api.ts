import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type {
  ContractualCashFlowRequest,
  ContractualCashFlowResult,
  ContractualCashFlowRun,
  ContractualCashFlowRunComparison,
  ContractualCashFlowSubjectKind,
  CreateContractualCashFlowRunRequest
} from "@/types/contractual-cash-flow.types";

function subjectRoute(kind: ContractualCashFlowSubjectKind, template: "preview" | "runs" | "run" | "compare", subjectId: string, runId?: string, otherRunId?: string): string {
  const id = encodeURIComponent(subjectId);
  const routes = kind === "Security"
    ? { preview: UI_API_ROUTES.SecurityMasterCashFlowProjectionPreview, runs: UI_API_ROUTES.SecurityMasterCashFlowProjectionRuns, run: UI_API_ROUTES.SecurityMasterCashFlowProjectionRun, compare: UI_API_ROUTES.SecurityMasterCashFlowProjectionRunCompare }
    : { preview: UI_API_ROUTES.DirectLendingProjectionPreview, runs: UI_API_ROUTES.DirectLendingContractualRuns, run: UI_API_ROUTES.DirectLendingContractualRun, compare: UI_API_ROUTES.DirectLendingContractualRunCompare };
  return routes[template]
    .replace("{securityId:guid}", id)
    .replace("{loanId:guid}", id)
    .replace("{subjectId:guid}", id)
    .replace("{runId:guid}", encodeURIComponent(runId ?? ""))
    .replace("{otherRunId:guid}", encodeURIComponent(otherRunId ?? ""));
}

/** Hypothetical projection over current terms, calendars and pinned curves; never posts or retains. */
export function previewContractualCashFlows(
  kind: ContractualCashFlowSubjectKind,
  subjectId: string,
  request: ContractualCashFlowRequest,
  options: ApiRequestOptions = {}
): Promise<ContractualCashFlowResult> {
  return apiPostJson<ContractualCashFlowResult>(subjectRoute(kind, "preview", subjectId), request, { ...options, allowDevelopmentFallback: false });
}

/**
 * Retains a complete replay packet under a client-generated run identity. A retry with the same identity
 * and request returns the retained run; a blocked projection comes back as a result without a run id.
 */
export function retainContractualCashFlowRun(
  kind: ContractualCashFlowSubjectKind,
  subjectId: string,
  request: CreateContractualCashFlowRunRequest,
  options: ApiRequestOptions = {}
): Promise<ContractualCashFlowRun | ContractualCashFlowResult> {
  return apiPostJson<ContractualCashFlowRun | ContractualCashFlowResult>(subjectRoute(kind, "runs", subjectId), request, { ...options, allowDevelopmentFallback: false });
}

export function getContractualCashFlowRun(
  kind: ContractualCashFlowSubjectKind,
  subjectId: string,
  runId: string,
  options: ApiRequestOptions = {}
): Promise<ContractualCashFlowRun> {
  return apiGetJson<ContractualCashFlowRun>(subjectRoute(kind, "run", subjectId, runId), { ...options, allowDevelopmentFallback: false });
}

/** Pure read over two retained runs of the same subject: per-date net cash deltas, blocker differences and replay verification. */
export function compareContractualCashFlowRuns(
  kind: ContractualCashFlowSubjectKind,
  subjectId: string,
  runId: string,
  otherRunId: string,
  options: ApiRequestOptions = {}
): Promise<ContractualCashFlowRunComparison> {
  if (runId.trim() === otherRunId.trim()) return Promise.reject(new Error("Choose two different retained runs to compare."));
  return apiGetJson<ContractualCashFlowRunComparison>(subjectRoute(kind, "compare", subjectId, runId.trim(), otherRunId.trim()), { ...options, allowDevelopmentFallback: false });
}

export function isContractualCashFlowRun(value: ContractualCashFlowRun | ContractualCashFlowResult): value is ContractualCashFlowRun {
  return typeof (value as ContractualCashFlowRun).runId === "string" && "result" in value;
}
