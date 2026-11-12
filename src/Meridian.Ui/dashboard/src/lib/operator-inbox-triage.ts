import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { routeForOperatorWorkItem } from "@/app-shell.workflow-routing";
import { normalizeLocalWorkstationRoute } from "@/lib/workspace";
import type { OperatorWorkItem } from "@/types";
import { isApiError } from "@/lib/api-errors";

/** Shared contract: Meridian.Contracts.Workstation.OperatorTriageDtos. */
export interface OperatorTriageActivity {
  commandId: string;
  actorId: string;
  action: string;
  reason: string;
  occurredAt: string;
}

export interface OperatorTriageItem {
  workItemId: string;
  label: string;
  detail: string;
  workspace: string | null;
  targetRoute: string | null;
  kind?: OperatorWorkItem["kind"] | null;
  targetPageTag?: string | null;
  tone: "Info" | "Success" | "Warning" | "Critical";
  createdAt: string;
  fundAccountId?: string | null;
  runId?: string | null;
  scope?: string | null;
  ownerId: string | null;
  dueAt: string | null;
  version: number;
  sourceVersion: number | null;
  acknowledgedAt: string | null;
  snoozedUntil: string | null;
  capabilities: {
    canAssign: boolean;
    canAcknowledge: boolean;
    canSnooze: boolean;
    canUnsnooze: boolean;
    disabledReason: string | null;
  };
  activity: OperatorTriageActivity[];
}

export interface OperatorTriageInbox {
  asOf: string;
  actorId: string;
  items: OperatorTriageItem[];
  persistenceAvailable: boolean;
  unavailableReason: string | null;
}

export type OperatorTriageAction = "Assign" | "Acknowledge" | "Snooze" | "Unsnooze";
export interface OperatorTriageCommand {
  commandId: string;
  action: OperatorTriageAction;
  expectedVersion: number;
  expectedSourceVersion?: number | null;
  reason: string;
  assigneeId?: string;
  snoozedUntil?: string;
  fundAccountId?: string | null;
}

export interface OperatorTriageCommandResult {
  status: "Applied" | "Conflict" | "Rejected" | "NotFound" | "Unavailable" | "Forbidden";
  workItemId: string;
  item?: OperatorTriageItem | null;
  error?: string | null;
  replayed: boolean;
}

export interface OperatorTriageBulkAssignment {
  commandId: string;
  action: "Assign";
  reason: string;
  assigneeId: string;
  items: { workItemId: string; expectedVersion: number; expectedSourceVersion?: number | null }[];
  fundAccountId?: string | null;
}

export const OPERATOR_TRIAGE_ENDPOINT = "/api/workstation/operator/triage";

export async function getOperatorTriageInbox(fundAccountId?: string | null, options: ApiRequestOptions = {}): Promise<OperatorTriageInbox> {
  const query = fundAccountId ? `?${new URLSearchParams({ fundAccountId })}` : "";
  const result = await apiGetJson<OperatorTriageInbox>(`${OPERATOR_TRIAGE_ENDPOINT}${query}`, {
    ...options, allowDevelopmentFallback: false
  });
  if (!result || typeof result.actorId !== "string" || !result.actorId.trim()
    || !Array.isArray(result.items) || typeof result.persistenceAvailable !== "boolean"
    || result.items.some((item) => !isTriageItem(item))
    || new Set(result.items.map((item) => item.workItemId)).size !== result.items.length) {
    throw new Error("The operator inbox returned an invalid authority response. Refresh before taking action.");
  }
  return result;
}

function isTriageItem(item: OperatorTriageItem): boolean {
  return Boolean(item && typeof item.workItemId === "string" && item.workItemId.trim()
    && typeof item.label === "string" && typeof item.detail === "string"
    && ["Info", "Success", "Warning", "Critical"].includes(item.tone)
    && Number.isSafeInteger(item.version) && item.version >= 0
    && item.capabilities && ["canAssign", "canAcknowledge", "canSnooze", "canUnsnooze"].every(
      (key) => typeof item.capabilities[key as keyof typeof item.capabilities] === "boolean")
    && Array.isArray(item.activity));
}

export async function commandOperatorTriage(workItemId: string, command: OperatorTriageCommand, options: ApiRequestOptions = {}) {
  try {
    return await apiPostJson<OperatorTriageCommandResult>(`${OPERATOR_TRIAGE_ENDPOINT}/${encodeURIComponent(workItemId)}/actions`, command,
      { ...options, allowDevelopmentFallback: false });
  } catch (failure) {
    // A retained refusal is a known outcome, unlike an interrupted command response.
    if (isApiError(failure) && failure.responseBody && [400, 403, 404, 409, 503].includes(failure.status)) {
      try {
        const result = JSON.parse(failure.responseBody) as OperatorTriageCommandResult;
        if (result.workItemId === workItemId && ["Conflict", "Rejected", "NotFound", "Unavailable", "Forbidden"].includes(result.status)) return result;
      } catch { /* Preserve the original request error when its result is unverifiable. */ }
    }
    throw failure;
  }
}

export function bulkAssignOperatorTriage(command: OperatorTriageBulkAssignment, options: ApiRequestOptions = {}) {
  return apiPostJson<{ results: OperatorTriageCommandResult[] }>(`${OPERATOR_TRIAGE_ENDPOINT}/bulk`, command,
    { ...options, allowDevelopmentFallback: false });
}

export function isActiveTriageSnooze(item: OperatorTriageItem, now = Date.now()): boolean {
  return item.tone !== "Critical" && Date.parse(item.snoozedUntil ?? "") > now;
}

export function safeTriageRoute(route: string | null | undefined): string | null {
  return normalizeLocalWorkstationRoute(route);
}

export function routeForTriageItem(item: OperatorTriageItem): string | null {
  if (!item.kind) return safeTriageRoute(item.targetRoute);
  return safeTriageRoute(routeForOperatorWorkItem({ ...item, kind: item.kind,
    runId: item.runId ?? null, fundAccountId: item.fundAccountId ?? null, auditReference: null }));
}
