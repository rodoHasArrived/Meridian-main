import type { ReferenceDataEndpointDefinition } from "./workstation-endpoints";

// Pure response inspection for Reference Data workbench probes; transport stays in api.ts.
export type ReferenceDataEndpointProbeStatus = "Ready" | "Empty" | "Missing" | "Blocked" | "Error" | "Deferred";

export interface ReferenceDataEndpointProbeResult extends ReferenceDataEndpointDefinition {
  status: ReferenceDataEndpointProbeStatus;
  statusCode: number | null;
  durationMs: number | null;
  responseCount: number | null;
  responseSummary: string;
  responsePreview: string | null;
  errorSummary: string | null;
  errorDetails: string[];
}

export interface ReferenceDataWorkbenchCoverage {
  requestedAtUtc: string;
  endpoints: ReferenceDataEndpointProbeResult[];
}

export function summarizeReferenceDataPayload(payload: unknown): { count: number; summary: string } {
  if (payload === null || payload === undefined) {
    return { count: 0, summary: "No payload returned." };
  }

  if (Array.isArray(payload)) {
    return {
      count: payload.length,
      summary: payload.length > 0 ? `${payload.length} records returned.` : "No records returned."
    };
  }

  if (typeof payload === "object") {
    const keys = Object.keys(payload as Record<string, unknown>);
    return {
      count: keys.length > 0 ? 1 : 0,
      summary: keys.length > 0 ? `${keys.length} fields returned.` : "Empty object returned."
    };
  }

  return { count: 1, summary: "Scalar response returned." };
}

export function previewReferenceDataPayload(payload: unknown): string | null {
  if (payload === null || payload === undefined) {
    return null;
  }

  const previewPayload = Array.isArray(payload) ? payload.slice(0, 3) : payload;
  try {
    const preview = JSON.stringify(previewPayload, null, 2);
    return preview.length > 1600 ? `${preview.slice(0, 1600)}...` : preview;
  } catch {
    return String(previewPayload).slice(0, 1600);
  }
}

export function classifyReferenceDataApiError(status: number): ReferenceDataEndpointProbeStatus {
  if (status === 401 || status === 403) {
    return "Blocked";
  }

  if (status === 404) {
    return "Missing";
  }

  return "Error";
}

export function buildReferenceDataApiErrorDetails(error: { path: string; status: number; title: string | null; detail: string | null; responseBody: string | null; validationIssues: Array<{ label: string; messages: string[] }> }): string[] {
  const details: string[] = [`Endpoint returned ${error.status} for ${error.path}.`];

  if (error.title && error.title !== error.detail) {
    details.push(error.title);
  }

  if (error.detail) {
    details.push(error.detail);
  }

  for (const issue of error.validationIssues) {
    for (const message of issue.messages) {
      details.push(`${issue.label}: ${message}`);
    }
  }

  if (details.length === 1 && error.responseBody) {
    details.push(error.responseBody);
  }

  return details;
}
