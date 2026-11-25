/**
 * Client functions for the Security Master Passport Workbench governed-write surface (Phase 4 UI slice 1).
 *
 * Thin wrappers over the shared dashboard fetch helpers so aborts, headers, and backend problem-detail
 * handling stay aligned with the rest of the workstation. Identity is server-derived: the acting
 * principal (and, on approve, the reviewer) is resolved from the session and overwrites the body, so
 * these request inputs deliberately omit `actor`/`reviewer`-on-approve. The path `securityId` is
 * authoritative, so it is passed as an argument rather than trusted from the body.
 */

import { apiGetJson, apiPostJson } from "@/lib/api";
import { isApiError } from "@/lib/api-errors";
import {
  securityMasterWorkbenchApproveEndpoint,
  securityMasterWorkbenchDiscardEndpoint,
  securityMasterWorkbenchFieldEndpoint,
  securityMasterWorkbenchPendingRevisionsEndpoint,
  securityMasterWorkbenchPublishEndpoint,
  securityMasterWorkbenchResolveConflictEndpoint,
  securityMasterWorkbenchSubmitEndpoint
} from "@/lib/workstation-endpoints";

export type SecurityMasterRevisionState = "Draft" | "Submitted" | "Approved" | "Published" | "Rejected";

/** One governed revision awaiting action, across securities (review inbox). */
export interface SecurityMasterPendingRevision {
  revisionId: string;
  securityId: string;
  state: SecurityMasterRevisionState;
  actor: string;
  createdAt: string;
  updatedAt: string;
  fieldPath: string | null;
  fieldValue: string | null;
  justification: string | null;
  fundProfileId: string | null;
  baseVersion: number | null;
  ageHours: number;
}

/** Withdraws an abandoned Draft or gate-rejected Submitted revision. `actor` is server-derived. */
export interface DiscardSecurityMasterRevisionInput {
  revisionId: string;
  reason?: string | null;
}

export interface SecurityMasterChangeHistoryItem {
  changeId: string;
  streamVersion: number;
  eventType: string;
  changedAtUtc: string;
  effectiveAtUtc: string | null;
  actor: string;
  origin: string;
  sourceSystem: string;
  sourceRecordId: string | null;
  reason: string | null;
  summary: string;
  changedFields: string[];
  changedFieldsSummary: string;
  /** ADR-034 valid time: instant the event's full state applies from; absent for non-economic entries. */
  validFromUtc?: string | null;
  /** Exclusive end of the governed period as known now; absent when open-ended or never governing. */
  validToUtc?: string | null;
  /** Later same-valid-from recording that replaced this event for its whole period (a correction). */
  supersededByStreamVersion?: number | null;
}

export interface SecurityMasterEditResult {
  securityId: string;
  revisionId: string;
  newVersion: number;
  state: SecurityMasterRevisionState;
  changeEntry: SecurityMasterChangeHistoryItem;
}

export interface SecurityMasterConflictResolution {
  conflictId: string;
  policyWinnerSource: string;
  chosenWinnerSource: string;
  isPolicyDeviation: boolean;
  reason: string;
  newVersion: number;
  /** Set when choosing a non-current value staged a governed draft instead of resolving immediately. */
  pendingRevisionId?: string | null;
}

export interface RestatementCandidate {
  reportId: string;
  priorVersionReportId: string;
  periodLabel: string;
  summary: string;
  changedLines: unknown[];
}

/** What publishing an approved revision did to the canonical (golden) record. */
export type SecurityMasterCanonicalMergeOutcomeKind =
  | "NotApplicable"
  | "OverlayOnly"
  | "AlreadyCanonical"
  | "Merged";

export interface SecurityMasterCanonicalMergeOutcome {
  outcome: SecurityMasterCanonicalMergeOutcomeKind;
  detail: string;
  canonicalVersion: number | null;
}

export interface SecurityMasterPublishResult {
  securityId: string;
  revisionId: string;
  newVersion: number;
  restatementRequired: boolean;
  restatementCandidates: RestatementCandidate[];
  invalidatedProjections: string[];
  /** Null when no publish handler reported a canonical-merge outcome. */
  canonicalMerge?: SecurityMasterCanonicalMergeOutcome | null;
}

/** Effective-dated edit to a single passport field. `actor` is server-derived and not supplied here. */
export interface UpdateSecurityFieldInput {
  expectedVersion: number;
  fieldPath: string;
  newValue: string | null;
  effectiveFrom: string;
  justification: string;
  sourceRecordId?: string | null;
  fundProfileId?: string | null;
  correlationId?: string | null;
}

/** Operator resolution of a source conflict. A deviation from the policy winner must be acknowledged. */
export interface ResolveSourceConflictInput {
  conflictId: string;
  expectedVersion: number;
  chosenWinnerSource: string;
  reason: string;
  acknowledgePolicyDeviation?: boolean;
  correlationId?: string | null;
}

/** Submit a draft into the governed approval gate. `workflowId` is required by the endpoint. */
export interface SubmitSecurityMasterRevisionInput {
  revisionId: string;
  note?: string | null;
  fundProfileId?: string | null;
  workflowId: string;
  expectedWorkflowVersion: number;
  reviewer: string;
  reportPackId?: string | null;
}

/** Approve a submitted revision. `actor`/`reviewer` are server-derived (the caller is the reviewer). */
export interface ApproveSecurityMasterRevisionInput {
  revisionId: string;
  workflowId: string;
  expectedWorkflowVersion: number;
  rationale: string;
  reportPackId: string;
  correlationId?: string | null;
}

/** Publish an approved revision. `actor` is server-derived. */
export interface PublishSecurityMasterRevisionInput {
  revisionId: string;
  correlationId?: string | null;
}

export function updateSecurityField(
  securityId: string,
  input: UpdateSecurityFieldInput,
  signal?: AbortSignal
): Promise<SecurityMasterEditResult> {
  return apiPostJson<SecurityMasterEditResult>(securityMasterWorkbenchFieldEndpoint(securityId), input, { signal });
}

export function resolveSourceConflict(
  securityId: string,
  input: ResolveSourceConflictInput,
  signal?: AbortSignal
): Promise<SecurityMasterConflictResolution> {
  return apiPostJson<SecurityMasterConflictResolution>(
    securityMasterWorkbenchResolveConflictEndpoint(securityId),
    input,
    { signal }
  );
}

export function submitSecurityMasterRevision(
  securityId: string,
  input: SubmitSecurityMasterRevisionInput,
  signal?: AbortSignal
): Promise<SecurityMasterEditResult> {
  return apiPostJson<SecurityMasterEditResult>(securityMasterWorkbenchSubmitEndpoint(securityId), input, { signal });
}

export function approveSecurityMasterRevision(
  securityId: string,
  input: ApproveSecurityMasterRevisionInput,
  signal?: AbortSignal
): Promise<SecurityMasterEditResult> {
  return apiPostJson<SecurityMasterEditResult>(securityMasterWorkbenchApproveEndpoint(securityId), input, { signal });
}

export function publishSecurityMasterRevision(
  securityId: string,
  input: PublishSecurityMasterRevisionInput,
  signal?: AbortSignal
): Promise<SecurityMasterPublishResult> {
  return apiPostJson<SecurityMasterPublishResult>(securityMasterWorkbenchPublishEndpoint(securityId), input, { signal });
}

/**
 * A classified governed-write failure, so the editor can render the right recovery affordance:
 * a stale-version reload banner, a workflow prompt, or a generic validation message.
 */
export type WorkbenchWriteError =
  | { kind: "version-conflict"; currentVersion: number | null }
  | { kind: "revision-state-conflict"; message: string | null }
  | { kind: "field-already-staged"; stagedBy: string | null; revisionId: string | null; message: string | null }
  | { kind: "workflow-required" }
  | { kind: "unprocessable"; message: string | null }
  | { kind: "forbidden" }
  | { kind: "unauthorized" }
  | { kind: "unknown"; message: string };

interface WorkbenchErrorBody {
  error?: unknown;
  currentVersion?: unknown;
  message?: unknown;
  stagedBy?: unknown;
  revisionId?: unknown;
}

function parseErrorBody(responseBody: string | null): WorkbenchErrorBody {
  if (!responseBody) {
    return {};
  }
  try {
    const parsed = JSON.parse(responseBody) as unknown;
    return typeof parsed === "object" && parsed !== null ? (parsed as WorkbenchErrorBody) : {};
  } catch {
    return {};
  }
}

/** Lists revisions awaiting action across every security, oldest first (default: Submitted and Approved). */
export function listPendingSecurityMasterRevisions(
  states: readonly SecurityMasterRevisionState[] = ["Submitted", "Approved"],
  signal?: AbortSignal
): Promise<SecurityMasterPendingRevision[]> {
  return apiGetJson<SecurityMasterPendingRevision[]>(securityMasterWorkbenchPendingRevisionsEndpoint(states), { signal });
}

/** Discards a revision so its staged overlay value no longer blocks the security's other edits. */
export function discardSecurityMasterRevision(
  securityId: string,
  input: DiscardSecurityMasterRevisionInput,
  signal?: AbortSignal
): Promise<SecurityMasterEditResult> {
  return apiPostJson<SecurityMasterEditResult>(securityMasterWorkbenchDiscardEndpoint(securityId), input, { signal });
}

/**
 * Maps a thrown {@link ApiError} from a workbench write to a discriminated recovery hint. Non-API errors
 * (network/abort) classify as `unknown` so callers always have a message to surface.
 */
export function classifyWorkbenchWriteError(error: unknown): WorkbenchWriteError {
  if (!isApiError(error)) {
    return { kind: "unknown", message: error instanceof Error ? error.message : "The request could not be completed." };
  }

  const body = parseErrorBody(error.responseBody);
  const code = typeof body.error === "string" ? body.error : null;
  const message = typeof body.message === "string" ? body.message : error.detail;

  switch (error.status) {
    case 401:
      return { kind: "unauthorized" };
    case 403:
      return { kind: "forbidden" };
    case 409:
      if (code === "field-already-staged") {
        return {
          kind: "field-already-staged",
          stagedBy: typeof body.stagedBy === "string" ? body.stagedBy : null,
          revisionId: typeof body.revisionId === "string" ? body.revisionId : null,
          message
        };
      }
      if (code === "version-conflict") {
        return {
          kind: "version-conflict",
          currentVersion: typeof body.currentVersion === "number" ? body.currentVersion : null
        };
      }
      return { kind: "revision-state-conflict", message };
    case 422:
      if (code === "workflow-required") {
        return { kind: "workflow-required" };
      }
      return { kind: "unprocessable", message };
    default:
      return { kind: "unknown", message: error.detail ?? `Request failed (${error.status}).` };
  }
}
