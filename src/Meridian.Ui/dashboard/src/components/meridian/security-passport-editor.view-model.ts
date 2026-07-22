/**
 * Pure view-model logic for the Security Master passport editor (Phase 4 UI slice 2). Keeps the
 * governed-write lifecycle rules — which action is enabled in which state, how a write failure becomes
 * a recovery banner — out of the React component so they can be unit-tested directly.
 *
 * These are presentation rules only; the server re-validates every transition. The editor never decides
 * readiness locally (no client-local governance rule), matching the design's "no client-local readiness".
 */

import type { SecurityMasterRevisionState, WorkbenchWriteError } from "@/lib/api/security-master-workbench.api";

export type PassportLifecycleAction = "save-draft" | "submit" | "approve" | "publish";

export interface PassportActionViewModel {
  action: PassportLifecycleAction;
  label: string;
  enabled: boolean;
  /** When disabled, a short reason suitable for a tooltip / aria-description. */
  disabledReason: string | null;
}

export type PassportStateTone = "draft" | "submitted" | "approved" | "published" | "rejected";

export interface PassportStateBadgeViewModel {
  label: string;
  tone: PassportStateTone;
}

export type PassportTrustTone = "blocked" | "review" | "trusted";

export interface PassportTrustPostureViewModel {
  label: string;
  tone: PassportTrustTone;
}

export type PassportBannerTone = "error" | "warning" | "info";

export interface PassportBannerViewModel {
  tone: PassportBannerTone;
  title: string;
  message: string;
  /** Present for a stale-version conflict: the editor offers a non-destructive reload to this version. */
  reloadToVersion?: number | null;
}

const STATE_BADGES: Record<SecurityMasterRevisionState, PassportStateBadgeViewModel> = {
  Draft: { label: "Draft", tone: "draft" },
  Submitted: { label: "Submitted", tone: "submitted" },
  Approved: { label: "Approved", tone: "approved" },
  Published: { label: "Published", tone: "published" },
  Rejected: { label: "Rejected", tone: "rejected" }
};

export function buildPassportStateBadge(state: SecurityMasterRevisionState | null): PassportStateBadgeViewModel {
  return state ? STATE_BADGES[state] : { label: "No draft", tone: "draft" };
}

export interface PassportActionContext {
  /** Lifecycle state of the working revision, or null when no draft has been opened yet. */
  state: SecurityMasterRevisionState | null;
  /** True when the form holds an unsaved field edit. */
  hasPendingEdit: boolean;
  /** True while a workbench request is in flight. */
  isBusy: boolean;
}

export function buildPassportActions(context: PassportActionContext): PassportActionViewModel[] {
  const { state, hasPendingEdit, isBusy } = context;
  const busyReason = "A workbench action is already in progress.";

  function build(
    action: PassportLifecycleAction,
    label: string,
    allowed: boolean,
    notAllowedReason: string
  ): PassportActionViewModel {
    if (isBusy) {
      return { action, label, enabled: false, disabledReason: busyReason };
    }
    return {
      action,
      label,
      enabled: allowed,
      disabledReason: allowed ? null : notAllowedReason
    };
  }

  return [
    build("save-draft", "Save draft", hasPendingEdit, "Edit a field to save a draft."),
    build("submit", "Submit for approval", state === "Draft", "Save a draft before submitting for approval."),
    build("approve", "Approve", state === "Submitted", "Only a submitted revision can be approved."),
    build("publish", "Publish", state === "Approved", "Publish is enabled once the revision is approved.")
  ];
}

export function buildWorkbenchErrorBanner(error: WorkbenchWriteError): PassportBannerViewModel {
  switch (error.kind) {
    case "version-conflict":
      return {
        tone: "error",
        title: "This passport changed",
        message:
          error.currentVersion != null
            ? `Another operator published version ${error.currentVersion}. Reload to continue without losing your edit.`
            : "Another operator changed this passport. Reload to continue without losing your edit.",
        reloadToVersion: error.currentVersion
      };
    case "revision-state-conflict":
      return {
        tone: "error",
        title: "Revision state changed",
        message: error.message ?? "This revision is no longer in a state that allows the requested action. Reload the revision."
      };
    case "workflow-required":
      return {
        tone: "warning",
        title: "Approval workflow required",
        message: "Select an approval workflow and an independent reviewer before submitting for governed approval."
      };
    case "unprocessable":
      return {
        tone: "error",
        title: "The edit was rejected",
        message: error.message ?? "The request was understood but could not be processed. Check the required fields."
      };
    case "forbidden":
      return {
        tone: "error",
        title: "Not permitted",
        message: "You don't have permission to edit this security passport."
      };
    case "unauthorized":
      return {
        tone: "error",
        title: "Session expired",
        message: "Your session is no longer authenticated. Sign in again to continue editing."
      };
    case "unknown":
    default:
      return {
        tone: "error",
        title: "The request could not be completed",
        message: error.kind === "unknown" ? error.message : "An unexpected error occurred."
      };
  }
}

export type PassportSeverityTone = "high" | "medium" | "low" | "none";

export interface PassportConflictInput {
  conflictId: string;
  fieldLabel: string;
  policyWinnerSource: string;
  challengerSource: string;
  severity?: string | null;
  isResolved?: boolean;
}

export interface PassportConflictRowViewModel {
  conflictId: string;
  fieldLabel: string;
  policyWinnerSource: string;
  challengerSource: string;
  severityTone: PassportSeverityTone;
  severityLabel: string;
  /** False when resolved or while a write is in flight. */
  canAct: boolean;
}

function classifySeverity(severity: string | null | undefined): { tone: PassportSeverityTone; label: string } {
  const normalized = (severity ?? "").trim().toLowerCase();
  if (normalized === "critical" || normalized === "high") {
    return { tone: "high", label: severity ?? "High" };
  }
  if (normalized === "medium") {
    return { tone: "medium", label: severity ?? "Medium" };
  }
  if (normalized === "low") {
    return { tone: "low", label: severity ?? "Low" };
  }
  return { tone: "none", label: severity ?? "None" };
}

export function buildConflictRows(
  conflicts: readonly PassportConflictInput[],
  context: { isBusy: boolean }
): PassportConflictRowViewModel[] {
  return conflicts.map((conflict) => {
    const severity = classifySeverity(conflict.severity);
    return {
      conflictId: conflict.conflictId,
      fieldLabel: conflict.fieldLabel,
      policyWinnerSource: conflict.policyWinnerSource,
      challengerSource: conflict.challengerSource,
      severityTone: severity.tone,
      severityLabel: severity.label,
      canAct: !conflict.isResolved && !context.isBusy
    };
  });
}

export interface ConflictOverrideValidation {
  valid: boolean;
  /** When invalid, the single blocking reason; null when valid. */
  reason: string | null;
}

/**
 * Client-side guard for the override form (UX only — the server re-validates). A reason is always
 * required, and choosing a winner other than the policy winner requires acknowledging the deviation,
 * mirroring the server's policy-deviation-unacknowledged (422) rule.
 */
export function validateConflictOverride(input: {
  chosenWinnerSource: string;
  policyWinnerSource: string;
  reason: string;
  acknowledgeDeviation: boolean;
}): ConflictOverrideValidation {
  if (input.chosenWinnerSource.trim().length === 0) {
    return { valid: false, reason: "Choose a winning source." };
  }
  if (input.reason.trim().length === 0) {
    return { valid: false, reason: "A reason is required to resolve a source conflict." };
  }
  const isDeviation =
    input.chosenWinnerSource.trim().toLowerCase() !== input.policyWinnerSource.trim().toLowerCase();
  if (isDeviation && !input.acknowledgeDeviation) {
    return { valid: false, reason: "Acknowledge the policy deviation to override the recommended winner." };
  }
  return { valid: true, reason: null };
}

export function buildTrustPosture(posture: string | null | undefined): PassportTrustPostureViewModel {
  const normalized = (posture ?? "").trim().toLowerCase();
  if (normalized === "blocked" || normalized === "critical") {
    return { label: "Blocked", tone: "blocked" };
  }
  if (normalized === "trusted" || normalized === "ready" || normalized === "ok") {
    return { label: "Trusted", tone: "trusted" };
  }
  return { label: "Review", tone: "review" };
}
