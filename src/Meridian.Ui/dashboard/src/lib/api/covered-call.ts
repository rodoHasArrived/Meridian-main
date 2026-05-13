/**
 * Client functions for the covered-call backtest endpoints (slice 1).
 * Thin fetch wrappers — error handling is centralised by the caller.
 */

import { COVERED_CALL_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type {
  CoveredCallBacktestRequest,
  CoveredCallChainPreview,
  CoveredCallChainPreviewRequest,
  CoveredCallRunHandle,
  CoveredCallRunResult,
  CoveredCallRunStatus,
  CoveredCallRunSummary
} from "@/types/covered-call";

async function readJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    let detail = `${response.status} ${response.statusText}`;
    try {
      const text = await response.text();
      if (text) {
        try {
          const parsed = JSON.parse(text) as { detail?: string; title?: string };
          if (parsed.detail) detail = parsed.detail;
          else if (parsed.title) detail = parsed.title;
          else detail = text;
        } catch {
          detail = text;
        }
      }
    } catch {
      // swallow body-read errors and fall back to status
    }
    throw new Error(`${detail} (status ${response.status})`);
  }
  return (await response.json()) as T;
}

export async function startCoveredCallBacktest(
  request: CoveredCallBacktestRequest,
  signal?: AbortSignal
): Promise<CoveredCallRunHandle> {
  const response = await fetch(COVERED_CALL_API_ENDPOINTS.runs, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "same-origin",
    body: JSON.stringify(request),
    signal
  });
  return readJson<CoveredCallRunHandle>(response);
}

export async function listCoveredCallRuns(limit = 50, signal?: AbortSignal): Promise<CoveredCallRunSummary[]> {
  const response = await fetch(`${COVERED_CALL_API_ENDPOINTS.runs}?limit=${encodeURIComponent(limit)}`, {
    method: "GET",
    credentials: "same-origin",
    signal
  });
  return readJson<CoveredCallRunSummary[]>(response);
}

export async function getCoveredCallRunStatus(runId: string, signal?: AbortSignal): Promise<CoveredCallRunStatus> {
  const response = await fetch(COVERED_CALL_API_ENDPOINTS.runStatus(runId), {
    method: "GET",
    credentials: "same-origin",
    signal
  });
  return readJson<CoveredCallRunStatus>(response);
}

export async function getCoveredCallRunResult(runId: string, signal?: AbortSignal): Promise<CoveredCallRunResult> {
  const response = await fetch(COVERED_CALL_API_ENDPOINTS.runResult(runId), {
    method: "GET",
    credentials: "same-origin",
    signal
  });
  return readJson<CoveredCallRunResult>(response);
}

export async function cancelCoveredCallRun(runId: string, signal?: AbortSignal): Promise<CoveredCallRunStatus> {
  const response = await fetch(COVERED_CALL_API_ENDPOINTS.runCancel(runId), {
    method: "POST",
    credentials: "same-origin",
    signal
  });
  return readJson<CoveredCallRunStatus>(response);
}

export async function previewCoveredCallChain(
  request: CoveredCallChainPreviewRequest,
  signal?: AbortSignal
): Promise<CoveredCallChainPreview> {
  const response = await fetch(COVERED_CALL_API_ENDPOINTS.chainPreview, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    credentials: "same-origin",
    body: JSON.stringify(request),
    signal
  });
  return readJson<CoveredCallChainPreview>(response);
}
