import type { ReconciliationBreakQueueItem, ReconciliationCaseLifecycleState } from "@/types";

/**
 * Lifecycle decisions the inspector offers for one break.
 *
 * This mirrors the server's eligibility checks (`ValidateCaseworkCommand` in
 * `FileReconciliationBreakQueueRepository.Casework.cs`) so a button can say why it is disabled.
 * The server stays authoritative: it re-checks role, approval, evidence, taxonomy and version.
 */
export type BreakLifecycleActionId = "waive" | "supersede" | "signOff" | "reopen";

export interface BreakLifecycleAction {
  id: BreakLifecycleActionId;
  label: string;
  enabled: boolean;
  disabledReason: string | null;
  /** Lifecycle after the server accepts the action. */
  nextState: ReconciliationCaseLifecycleState;
  /** Short preview, e.g. "Investigating → Resolved (waived)". */
  preview: string;
}

export interface BreakLifecycleModel {
  lifecycleState: ReconciliationCaseLifecycleState;
  lifecycleLabel: string;
  /** Root cause and resolution codes can still change. */
  canClassify: boolean;
  classifyDisabledReason: string | null;
  actions: BreakLifecycleAction[];
}

const TERMINAL_STATES: readonly ReconciliationCaseLifecycleState[] = ["Resolved", "SignedOff", "Superseded"];
const DISPOSITION_STATES: readonly ReconciliationCaseLifecycleState[] = ["Open", "InReview", "Investigating", "AwaitingEvidence", "Reopened"];

const LIFECYCLE_LABELS: Record<ReconciliationCaseLifecycleState, string> = {
  Open: "Open",
  InReview: "In review",
  AwaitingApproval: "Awaiting approval",
  Approved: "Approved",
  Posted: "Posted",
  Reopened: "Reopened",
  Superseded: "Superseded",
  Investigating: "Investigating",
  AwaitingEvidence: "Awaiting evidence",
  Resolved: "Resolved",
  SignedOff: "Signed off"
};

export function lifecycleStateLabel(state: ReconciliationCaseLifecycleState | null | undefined): string {
  return state ? LIFECYCLE_LABELS[state] ?? state : "Not recorded";
}

/** Returns null when the payload carries no lifecycle state; the inspector then offers no lifecycle decisions. */
export function buildBreakLifecycleModel(
  item: Pick<ReconciliationBreakQueueItem, "lifecycleState" | "version"> | null | undefined
): BreakLifecycleModel | null {
  const state = item?.lifecycleState;
  if (!item || !state) return null;
  const label = lifecycleStateLabel(state);
  const versionMissing = item.version == null ? "The break version is not available, so changes are disabled." : null;
  const terminal = TERMINAL_STATES.includes(state);
  const action = (
    id: BreakLifecycleActionId,
    actionLabel: string,
    allowed: boolean,
    reason: string,
    nextState: ReconciliationCaseLifecycleState,
    suffix = ""
  ): BreakLifecycleAction => ({
    id,
    label: actionLabel,
    enabled: allowed && !versionMissing,
    disabledReason: versionMissing ?? (allowed ? null : reason),
    nextState,
    preview: `${label} → ${lifecycleStateLabel(nextState)}${suffix}`
  });

  return {
    lifecycleState: state,
    lifecycleLabel: label,
    canClassify: !terminal && !versionMissing,
    classifyDisabledReason: versionMissing ?? (terminal ? `A ${label.toLowerCase()} break keeps its root cause and resolution. Reopen it to change them.` : null),
    actions: [
      action(
        "waive",
        "Waive",
        DISPOSITION_STATES.includes(state),
        `Only an open or active break can be waived. This break is ${label.toLowerCase()}.`,
        "Resolved",
        " (waived)"
      ),
      action(
        "supersede",
        "Supersede",
        DISPOSITION_STATES.includes(state),
        `Only an open or active break can be superseded. This break is ${label.toLowerCase()}.`,
        "Superseded"
      ),
      action(
        "signOff",
        "Sign off",
        state === "Resolved",
        state === "SignedOff" ? "This break is already signed off." : "Resolve or waive the break before signing it off.",
        "SignedOff"
      ),
      action(
        "reopen",
        "Reopen",
        state === "SignedOff",
        "Only a signed-off break can be reopened.",
        "Reopened"
      )
    ]
  };
}
