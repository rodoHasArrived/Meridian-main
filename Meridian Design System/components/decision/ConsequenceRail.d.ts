import * as React from "react";
import type { ObjectRef } from "../core/ObjectModel";
import type { ActionGuardValue } from "../operations/ActionGuard";
import type { ReversibilityClass } from "../operations/DecisionFlow";

/**
 * ConsequenceRail — what changes if the proposed action proceeds.
 *
 * The forward-looking counterpart to `EvidenceRail`. Reversibility is stated FIRST because it
 * decides how much of the rest matters, impacts are grouped by who feels them, and the guards that
 * would block the attempt are listed beside the consequence rather than hidden behind a disabled
 * button. Reversibility vocabulary is imported from `DecisionFlow` — this rail declares none.
 *
 * @example
 * <ConsequenceRail
 *   object={{ type: "journal", id: "JE-20418", version: 3 }}
 *   proposal={{ id: "CS-1042", changes: [{ key: "statClassification" }] }}
 *   consequences={{
 *     financial: [{ label: "STAT income", delta: 17256.38, currency: "USD", formatted: "17,256.38" }],
 *     controls: [{ id: "C-204", label: "Price approval", effect: "Re-runs" }],
 *     approvals: [{ id: "July close", label: "July close attestation", effect: "Superseded" }],
 *     reversibility: "compensatable",
 *   }}
 *   guards={guards}
 *   onInspect={(kind, item) => openImpact(kind, item)}
 * />
 */

export type ConsequenceKindId = "financial" | "accounting" | "controls" | "reports" | "approvals";

export interface ConsequenceKindMeta {
  id: ConsequenceKindId;
  label: string;
  /** Singular noun for the group count — "3 controls", "1 posting". */
  noun: string;
}
/** The five consequence groups, in reading order. */
export declare const CONSEQUENCE_KINDS: ConsequenceKindMeta[];

export interface ConsequenceItem {
  id?: string;
  label?: React.ReactNode;
  /** What happens to it — "Re-runs", "Reopens", "Superseded". Never a bare colour. */
  effect?: React.ReactNode;
  /** Signed change. Drives the sign treatment; pass `formatted` for the display string. */
  delta?: number;
  amount?: number;
  formatted?: React.ReactNode;
  currency?: string;
}

export interface ConsequenceSet extends Partial<Record<ConsequenceKindId, ConsequenceItem[]>> {
  /** Required for a consequential action: the one fact that decides the rest. */
  reversibility?: ReversibilityClass;
}

export interface ConsequenceRailProps extends Omit<React.HTMLAttributes<HTMLElement>, "title"> {
  object?: ObjectRef;
  /** The staged change under review — id and field count are stated, not the diff. */
  proposal?: { id?: string; version?: string | number; changes?: unknown[] };
  consequences?: ConsequenceSet;
  /** Preconditions, from `ActionGuard`. Listed with their status, never collapsed to disabled. */
  guards?: ActionGuardValue[];
  title?: React.ReactNode;
  /** Makes each impact row a button — selecting one opens the impact in the work plane. */
  onInspect?: (kind: ConsequenceKindId, item: ConsequenceItem) => void;
  selectedId?: string;
  dense?: boolean;
}
/** Renders an honest empty state: an action with no declared consequence is not review-ready. */
export declare function ConsequenceRail(props: ConsequenceRailProps): React.JSX.Element;
