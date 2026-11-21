import * as React from "react";
/**
 * ActionQueue — the tray of consequential actions in flight. `ActionEnvelope` states the fate of ONE
 * instruction where it was issued; the queue keeps EVERY unresolved instruction on screen until it
 * resolves, with the ladder submitted → acknowledged → confirmed | rejected | timed out, each row
 * carrying its idempotency key, elapsed clock and retry path. No new vocabulary: rungs derive from
 * `ActionEnvelope`'s four fates (`inflight` stays submitted; `landed` without a receipt is acknowledged,
 * with a receipt is confirmed; `unlanded` is timed out).
 *
 * @example
 * <ActionEnvelopeProvider actions={actions} transport={transport}>
 *   …
 *   <ActionQueue />                       // corner tray, hides when empty
 *   <ActionQueueChip />                   // status-bar count
 * </ActionEnvelopeProvider>
 */
import type { Instruction } from "./ActionEnvelope";

export type QueueRung = "submitted" | "acknowledged" | "confirmed" | "rejected" | "timedout";
export interface QueueRungMeta { id: QueueRung; label: string; meaning: string; }
export declare const QUEUE_RUNGS: ReadonlyArray<QueueRungMeta>;
/** Derive the rung from the answer and receipt. Deprecated clock arguments are accepted and ignored. */
export declare function rungFor(instruction: Instruction | null, ackMs?: number, now?: number): QueueRung;

export interface ActionQueueProps extends React.HTMLAttributes<HTMLElement> {
  /** Omit inside an `ActionEnvelopeProvider` to read the live list. */
  instructions?: Instruction[];
  label?: string;
  /** Compatibility prop, ignored. Elapsed time never establishes acknowledgement. */
  ackMs?: number;
  /** Confirmed and acknowledged-rejected rows leave the tray after this. @default 6000 */
  settleMs?: number;
  /** "corner" floats above the status bar; "inline" renders in flow (a rail, a drawer). @default "corner" */
  docked?: "corner" | "inline";
  defaultOpen?: boolean;
  /** Retry with the SAME idempotency key. Defaults to the provider's `retry`. */
  onRetry?: (instruction: Instruction) => void;
  /** Operator acknowledged a rejection — the row settles. */
  onAcknowledge?: (instruction: Instruction) => void;
  /** Adds an "Open" verb per row — jump to the object the instruction touched. */
  onOpen?: (instruction: Instruction) => void;
  emptyLabel?: string;
  /** Corner tray renders nothing when empty. @default true */
  hideWhenEmpty?: boolean;
}
export declare function ActionQueue(props: ActionQueueProps): React.JSX.Element | null;

export interface ActionQueueChipProps extends React.HTMLAttributes<HTMLSpanElement> {
  instructions?: Instruction[];
  ackMs?: number;
  label?: string;
}
/** Status-bar mark: unresolved count, red when any fate is unknown. */
export declare function ActionQueueChip(props: ActionQueueChipProps): React.JSX.Element;
