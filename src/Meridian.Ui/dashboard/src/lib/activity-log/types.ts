// Client-side action-history / undo model. Every mutating operation the operator
// performs can be recorded here so there is a scannable ledger of "what did I just
// do" — sibling to the notification center — with an Undo affordance where the
// operation has a real compensating action.

export type ActivityTone = "default" | "success" | "warning" | "danger";

/**
 * Undo lifecycle for an entry:
 * - `none`     — the action is not reversible (no compensating operation).
 * - `idle`     — reversible; a live undo thunk is available this session.
 * - `running`  — an undo is in flight.
 * - `undone`   — the compensating action completed.
 * - `failed`   — the compensating action threw; the thunk remains for retry.
 * - `expired`  — the entry was reversible but the session ended, so the in-memory
 *                thunk is gone (loaded from persistence). History only, no undo.
 */
export type ActivityUndoStatus = "none" | "idle" | "running" | "undone" | "failed" | "expired";

/** A compensating operation captured at the call site of a reversible action. */
export interface ActivityUndoHandler {
  /** Runs the real inverse operation (e.g. add back a removed symbol). */
  run: () => Promise<void>;
  /** Optional short description of what undo will do, surfaced in the drawer. */
  description?: string;
}

/** What a call site passes to `record`. */
export interface ActivityEntryInput {
  /** Stable id; generated when omitted. Re-recording the same id replaces the entry. */
  id?: string;
  /** Machine-readable action type, e.g. "symbol.add". */
  kind: string;
  title: string;
  detail?: string;
  /** Deep link to the datum or the compensating workflow. */
  route?: string;
  routeLabel?: string;
  tone?: ActivityTone;
  /** ISO timestamp; defaults to record time. */
  timestamp?: string;
  /** Present only when the action has a genuine compensating operation. */
  undo?: ActivityUndoHandler;
}

/** The stored, presentation-ready shape of a recorded action. */
export interface ActivityEntry {
  id: string;
  kind: string;
  title: string;
  detail?: string;
  route?: string;
  routeLabel?: string;
  tone: ActivityTone;
  timestamp: string;
  undoStatus: ActivityUndoStatus;
  /** Last undo failure message, when undoStatus === "failed". */
  undoError?: string;
}
