import type React from "react";
/**
 * The way back out of an object — the return half of a handoff.
 *
 * Meridian's handoffs were one-way (reconciliation → accounting, close → tie-out); the returns
 * were never built, so a journal entry could not open the break that caused it. `ReturnPath`
 * renders the loops `templates/_scaffold/app-map.js` declares for an object type, and only those
 * the object in hand actually carries — an unresolved break offers no "entry that resolved this",
 * so the affordance is never a dead promise.
 *
 * Renders `null` when there are no loops. Pass `emptyText` only when the absence is worth stating.
 *
 * @example
 * <ReturnPath loops={MeridianAppMap.loops("entry", entry)} />
 * <ReturnPath loops={MeridianAppMap.loops("break", brk)} layout="stack" label="Related" />
 */
export interface ReturnLoop {
  /** Object type being returned to — `break`, `entry`, `account`, … */
  to: string;
  /** Identity of the target object, shown as a mono suffix unless `showIds` is false. */
  id?: string | number;
  /** The affordance's words, written from the operator's side of the loop. */
  verb: string;
  /** Real href when the target carries identity in the URL — keeps middle-click working. */
  href?: string;
  /** Navigate to the target. */
  open?: () => void;
}

export interface ReturnPathProps {
  /** Straight from `MeridianAppMap.loops(type, obj)`. */
  loops?: ReturnLoop[];
  /** Small-caps lead-in. Pass `""` to drop it. @default "Back to" */
  label?: React.ReactNode;
  /** `inline` for a context bar, `stack` for an evidence rail. @default "inline" */
  layout?: "inline" | "stack";
  /** Shown instead of nothing when `loops` is empty — use sparingly. */
  emptyText?: React.ReactNode;
  /** Show each target's id after its verb. @default true */
  showIds?: boolean;
}

export declare function ReturnPath(props: ReturnPathProps): React.JSX.Element | null;
