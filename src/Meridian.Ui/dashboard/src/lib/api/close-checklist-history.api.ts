import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { workstationOperationsContinuityChecklistEndpoint } from "@/lib/workstation-endpoints";

export interface CloseChecklistObservation {
  auditId: string; recordedAtUtc: string; workflowVersion: number;
  totalTasks: number; remainingTasks: number; blockedTasks: number; segment: number;
}
export interface CloseChecklistHistory {
  workflowId: string; asOf: string; status: "Ready" | "Unavailable"; reason: string | null;
  coverageStart: string | null; coverageEnd: string | null; isPartial: boolean; missingAuditIds: string[];
  versionGaps: { afterVersion: number; beforeVersion: number; beforeAuditId: string }[];
  observations: CloseChecklistObservation[];
}

export async function getCloseChecklistHistory(workflowId: string, options: ApiRequestOptions = {}) {
  const result = await apiGetJson<CloseChecklistHistory>(`${workstationOperationsContinuityChecklistEndpoint(workflowId)}/history`, { ...options, allowDevelopmentFallback: false });
  if (result.workflowId?.toLowerCase() !== workflowId.toLowerCase() || !Number.isFinite(Date.parse(result.asOf))
    || !["Ready", "Unavailable"].includes(result.status) || !Array.isArray(result.observations) || !Array.isArray(result.missingAuditIds)
    || !Array.isArray(result.versionGaps) || typeof result.isPartial !== "boolean"
    || new Set(result.observations.map((point) => point.auditId)).size !== result.observations.length
    || result.observations.some((point) => !point.auditId || !Number.isFinite(Date.parse(point.recordedAtUtc))
      || Date.parse(point.recordedAtUtc) > Date.parse(result.asOf)
      || ![point.workflowVersion, point.totalTasks, point.remainingTasks, point.blockedTasks, point.segment].every((value) => Number.isSafeInteger(value) && value >= 0)
      || point.remainingTasks > point.totalTasks || point.blockedTasks > point.remainingTasks)) {
    throw new Error("Close checklist history is incomplete or does not match this workflow.");
  }
  return result;
}
