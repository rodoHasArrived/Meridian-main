/**
 * Client functions for the Security Master review queues that sit beside the review inbox:
 * amendments held by source ranking (review A2) and the global exception queue (review D2).
 *
 * The shapes mirror the shared C# contracts in `Meridian.Contracts/Workstation/SecurityMasterReviewQueueDtos.cs`,
 * which the WPF desktop reads through `SecurityMasterReviewQueueApiClient`. The server decides ranking, SLA
 * breaches and every permission; these wrappers only carry the operator's request.
 */

import { apiGetJson, apiPostJson } from "@/lib/api";
import { isApiError } from "@/lib/api-errors";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import {
  securityMasterExceptionCaseAssignEndpoint,
  securityMasterExceptionCasesEndpoint,
  securityMasterExceptionCasesSyncEndpoint,
  securityMasterHeldAmendmentDiscardEndpoint,
  securityMasterHeldAmendmentsEndpoint
} from "@/lib/workstation-endpoints";

export type SecurityMasterHeldAmendmentStatus = "Held" | "Replayed" | "Discarded";

/** An ingest amendment held because a lower-ranked source tried to overwrite a higher-ranked value. */
export interface SecurityMasterHeldAmendment {
  heldAmendmentId: string;
  securityId: string;
  sourceSystem: string;
  requestJson: string;
  dominatedFieldPaths: string[];
  conflictIds: string[];
  heldAtVersion: number;
  heldAt: string;
  status: SecurityMasterHeldAmendmentStatus | string;
  closedBy?: string | null;
  closedAt?: string | null;
}

export type SecurityMasterExceptionCaseStatus = "Open" | "Closed";

export interface SecurityMasterExceptionCase {
  caseId: string;
  caseKey: string;
  category: string;
  ruleCode: string;
  securityId: string | null;
  severity: string;
  title: string;
  detail: string;
  status: SecurityMasterExceptionCaseStatus | string;
  assignee: string | null;
  openedAt: string;
  lastSeenAt: string;
  slaDueAt: string;
  closedAt: string | null;
  closedReason: string | null;
}

/** A queue row: the case plus the server's SLA verdict and age at read time. */
export interface SecurityMasterExceptionCaseQueueItem {
  row: SecurityMasterExceptionCase;
  slaBreached: boolean;
  ageHours: number;
}

export interface SecurityMasterExceptionSyncResult {
  opened: number;
  refreshed: number;
  closed: number;
}

export function listSecurityMasterHeldAmendments(
  filter: { securityId?: string | null; status?: SecurityMasterHeldAmendmentStatus | null } = {},
  signal?: AbortSignal
): Promise<SecurityMasterHeldAmendment[]> {
  return apiGetJson<SecurityMasterHeldAmendment[]>(securityMasterHeldAmendmentsEndpoint(filter), { signal });
}

/** Closes a Held row as Discarded. 204 on success; 409 when it is no longer held. */
export async function discardSecurityMasterHeldAmendment(heldAmendmentId: string, signal?: AbortSignal): Promise<void> {
  await apiPostJson<unknown>(securityMasterHeldAmendmentDiscardEndpoint(heldAmendmentId), {}, { signal });
}

export function listSecurityMasterExceptionCases(
  status?: SecurityMasterExceptionCaseStatus | null,
  signal?: AbortSignal
): Promise<SecurityMasterExceptionCaseQueueItem[]> {
  return apiGetJson<SecurityMasterExceptionCaseQueueItem[]>(securityMasterExceptionCasesEndpoint(status), { signal });
}

/**
 * The signed-in operator's username from `/api/auth/me`, the same principal the server records as the
 * actor of a governed write and the desktop pre-fills as the assignee. Null without a named session
 * (401, or an anonymous development host), so callers simply leave the field empty.
 *
 * The workstation session's `displayName` is not used: the server fills it with a desk label
 * ("Meridian Operator", "<strategy> Desk"), not the person.
 */
export async function getSignedInUsername(signal?: AbortSignal): Promise<string | null> {
  try {
    const caller = await apiGetJson<{ username?: unknown }>(UI_API_ROUTES.AuthApiMe, { signal });
    return typeof caller.username === "string" && caller.username.trim() ? caller.username.trim() : null;
  } catch (error: unknown) {
    if (isApiError(error) && (error.status === 401 || error.status === 403 || error.status === 404)) return null;
    throw error;
  }
}

/** Reconciles open cases with the current conflicts, identifier ambiguities and violations. */
export function syncSecurityMasterExceptionCases(signal?: AbortSignal): Promise<SecurityMasterExceptionSyncResult> {
  return apiPostJson<SecurityMasterExceptionSyncResult>(securityMasterExceptionCasesSyncEndpoint(), {}, { signal });
}

/** Assigns an open case. 204 on success; 409 when the case is no longer open. */
export async function assignSecurityMasterExceptionCase(caseId: string, assignee: string, signal?: AbortSignal): Promise<void> {
  await apiPostJson<unknown>(securityMasterExceptionCaseAssignEndpoint(caseId), { assignee: assignee.trim() }, { signal });
}

function serverSentence(responseBody: string | null): string | null {
  if (!responseBody) return null;
  try {
    const parsed = JSON.parse(responseBody) as unknown;
    if (typeof parsed !== "object" || parsed === null) return null;
    const body = parsed as { error?: unknown; message?: unknown; detail?: unknown };
    for (const candidate of [body.message, body.detail, body.error]) {
      // Queue routes answer {"error": "<sentence>"}; a bare code has no spaces and is not shown.
      if (typeof candidate === "string" && candidate.includes(" ")) return candidate;
    }
    return null;
  } catch {
    return null;
  }
}

/**
 * The operator-facing sentence for a review-queue failure, matching the desktop's
 * `SecurityMasterWorkbenchFailure.DescribeQueueAction`.
 */
export function describeReviewQueueFailure(error: unknown, action: string): string {
  if (!isApiError(error)) {
    return error instanceof Error && error.message ? error.message : "The server could not be reached.";
  }
  const sentence = serverSentence(error.responseBody) ?? (error.detail && error.detail.includes(" ") ? error.detail : null);
  switch (error.status) {
    case 401:
      return "Your session is no longer authenticated. Sign in again to continue.";
    case 403:
      return `You don't have permission to ${action}.`;
    case 404:
      return sentence ?? "The item no longer exists. Refresh the queue.";
    case 409:
      return sentence ?? "The item changed since the queue was loaded. Refresh the queue.";
    case 501:
    case 503:
      return sentence ?? "This queue is not available in this host.";
    default:
      return sentence ?? `The request failed (${error.status}).`;
  }
}

/** Mirrors the desktop SLA cell: the server's breach verdict first, otherwise the UTC due time. */
export function describeExceptionSla(item: SecurityMasterExceptionCaseQueueItem): string {
  const due = new Date(item.row.slaDueAt);
  const text = Number.isNaN(due.getTime())
    ? item.row.slaDueAt
    : `${due.toISOString().slice(0, 16).replace("T", " ")} UTC`;
  return item.slaBreached ? `Breached (due ${text})` : `Due ${text}`;
}
