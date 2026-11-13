import * as React from "react";
/**
 * Lifecycle ribbon — the investment-event lifecycle as a selectable rail:
 * `Trade captured → Accounting generated → Settlement → Custody → Income →
 * Valuation → Reporting`. Not every event uses every stage (a corporate action
 * skips settlement; an impairment starts at valuation) but the grammar is the
 * same for bonds, equities, funds, and adjustments — which is what makes an
 * event workspace legible across asset classes.
 *
 * Selecting a stage should synchronize the workspace: the schedule, the
 * rollforward, the next journal preview, the affected lot, the rule inspector,
 * and the reporting destinations.
 *
 * `LifecycleLanes` is the expanded mode for seasoned operators — independent
 * transaction / position / cash / income / book-value / market-value /
 * reporting lanes over the same timeline.
 *
 * @example
 * <LifecycleRibbon selectedId={stage} onSelect={setStage} stages={[
 *   { id:"trade",   label:"Trade captured",       meta:"May 29", state:"done" },
 *   { id:"acct",    label:"Accounting generated", meta:"May 29", state:"current" },
 *   { id:"settle",  label:"Settlement expected",  meta:"Jun 2",  state:"pending" },
 *   { id:"custody", label:"Custody",              meta:"Awaiting", state:"pending" },
 * ]} />
 *
 * <LifecycleLanes lanes={[
 *   { label:"Transaction", steps:[{label:"Trade",state:"done"},{label:"Settlement",state:"pending"}] },
 *   { label:"Book value",  steps:[{label:"Cost",state:"done"},{label:"Accretion",state:"current"}] },
 * ]} />
 */
export interface LifecycleStage {
  id: string;
  label: string;
  /** Date or short qualifier under the label. */
  meta?: string;
  /** `done · current · pending · blocked · skipped` (default `pending`). */
  state?: "done" | "current" | "pending" | "blocked" | "skipped";
}
export interface LifecycleRibbonProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onSelect"> {
  stages: LifecycleStage[];
  selectedId?: string;
  /** Makes stages buttons — wire it to the workspace synchronization. */
  onSelect?: (id: string) => void;
}
export declare function LifecycleRibbon(props: LifecycleRibbonProps): React.JSX.Element;

export interface LifecycleLane {
  label: string;
  steps: Array<{ label: string; state?: "done" | "current" | "pending" | "exception" }>;
}
export interface LifecycleLanesProps extends React.HTMLAttributes<HTMLDivElement> {
  lanes: LifecycleLane[];
}
export declare function LifecycleLanes(props: LifecycleLanesProps): React.JSX.Element;
