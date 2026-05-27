/**
 * Client functions for the covered-call backtest endpoints (slice 1).
 * Thin wrappers over the shared workstation API client so errors, aborts, and
 * no-host fixture semantics stay aligned with the rest of the dashboard.
 */

import { apiGetJson, apiPostJson } from "@/lib/api";
import {
  COVERED_CALL_API_ENDPOINTS,
  coveredCallRunCancelEndpoint,
  coveredCallRunResultEndpoint,
  coveredCallRunStatusEndpoint,
  coveredCallRunsEndpoint
} from "@/lib/workstation-endpoints";
import type {
  CoveredCallBacktestRequest,
  CoveredCallChainPreview,
  CoveredCallChainPreviewRequest,
  CoveredCallRunHandle,
  CoveredCallRunResult,
  CoveredCallRunStatus,
  CoveredCallRunSummary
} from "@/types/covered-call";

export async function startCoveredCallBacktest(
  request: CoveredCallBacktestRequest,
  signal?: AbortSignal
): Promise<CoveredCallRunHandle> {
  return apiPostJson<CoveredCallRunHandle>(COVERED_CALL_API_ENDPOINTS.runs, request, { signal });
}

export async function listCoveredCallRuns(limit = 50, signal?: AbortSignal): Promise<CoveredCallRunSummary[]> {
  return apiGetJson<CoveredCallRunSummary[]>(coveredCallRunsEndpoint(limit), { signal });
}

export async function getCoveredCallRunStatus(runId: string, signal?: AbortSignal): Promise<CoveredCallRunStatus> {
  return apiGetJson<CoveredCallRunStatus>(coveredCallRunStatusEndpoint(runId), { signal });
}

export async function getCoveredCallRunResult(runId: string, signal?: AbortSignal): Promise<CoveredCallRunResult> {
  return apiGetJson<CoveredCallRunResult>(coveredCallRunResultEndpoint(runId), { signal });
}

export async function cancelCoveredCallRun(runId: string, signal?: AbortSignal): Promise<CoveredCallRunStatus> {
  return apiPostJson<CoveredCallRunStatus>(coveredCallRunCancelEndpoint(runId), undefined, { signal });
}

export async function previewCoveredCallChain(
  request: CoveredCallChainPreviewRequest,
  signal?: AbortSignal
): Promise<CoveredCallChainPreview> {
  return apiPostJson<CoveredCallChainPreview>(COVERED_CALL_API_ENDPOINTS.chainPreview, request, { signal });
}
