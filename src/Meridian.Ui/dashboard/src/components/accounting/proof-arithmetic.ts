// Meridian accounting — the arithmetic the proof components print. No React, no DOM.
//
// RollforwardTrace and TieOutPanel prove their own figures, so the sums and the tie tests live
// here where a view model can run the same check the component renders (for a summary chip or a
// default-open disclosure) without parsing a formatted label. Two rules hold throughout:
//
//  - A missing input is not a zero. A null, blank or non-finite amount makes a proof
//    *indeterminate*; it is never coerced to 0 and never reads as tied.
//  - Money is rounded under an explicit policy (banker's by default) before a tie test, so
//    binary floating-point dust cannot manufacture or hide a difference.
import { toNumber } from "./money";

export type MoneyRoundingMode = "half-even" | "half-up" | "trunc";

/**
 * Round a money value to `decimals` places under an explicit policy. Ported from the design
 * system's `accounting/money.js`:
 *  - "half-even" banker's rounding, the books default (unbiased across many postings)
 *  - "half-up"   half away from zero on the positive side (invoice convention)
 *  - "trunc"     toward zero (withholding / fee convention)
 * Returns `NaN` for blank or unparseable input.
 */
export function roundMoney(
  value: number | string | null | undefined,
  decimals = 2,
  mode: MoneyRoundingMode = "half-even"
): number {
  const n = toNumber(value);
  if (!Number.isFinite(n)) return NaN;
  const f = Math.pow(10, decimals);
  const scaled = +(n * f).toPrecision(15); // strip FP dust so exact-half detection works
  if (mode === "trunc") return Math.trunc(scaled) / f;
  const floor = Math.floor(scaled);
  if (scaled - floor !== 0.5) return Math.round(scaled) / f;
  if (mode === "half-up") return (n < 0 ? floor : floor + 1) / f;
  return (floor % 2 === 0 ? floor : floor + 1) / f; // half-even
}

/** A usable amount, or `null`. Blank, null, undefined and non-finite values are not zero. */
export function usableAmount(value: number | null | undefined): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

// ── Rollforward ──────────────────────────────────────────────────────────────────────────────

export interface RollforwardProofMovement {
  label: string;
  /** Signed amount. `null` / non-finite means the movement has no amount. */
  value: number | null | undefined;
}

export interface RollforwardProofInput {
  openingLabel?: string;
  opening: number | null | undefined;
  movements: readonly RollforwardProofMovement[];
  closingLabel?: string;
  /**
   * Stated closing balance. Omit it (`undefined`) to use opening + movements. `null` or a
   * non-finite value means a closing was expected and is not available — the proof is then
   * indeterminate, because agreeing with a closing nobody stated proves nothing.
   */
  closing?: number | null;
  decimals?: number;
}

export interface RollforwardProof {
  /** False when an opening or movement amount is missing; nothing below is then computed. */
  computable: boolean;
  /** Every reason the rollforward cannot be computed, in operator words. Empty when computable. */
  gaps: string[];
  /** Sum of the movements. `null` when not computable. */
  movementTotal: number | null;
  /** opening + movements, rounded. `null` when not computable. */
  derivedClosing: number | null;
  /** The closing the trace states: the supplied closing, else the derived one. */
  statedClosing: number | null;
  /** stated − derived, rounded. `null` when not computable. */
  difference: number | null;
  /** True only when computable and the stated closing agrees with opening + movements. */
  ties: boolean;
}

/**
 * Prove a rollforward: opening + Σ movements against the stated closing, rounded half-even at
 * `decimals`, tied when the difference is under half a minor unit.
 */
export function proveRollforward({
  openingLabel = "Opening",
  opening,
  movements,
  closingLabel = "Closing",
  closing,
  decimals = 2
}: RollforwardProofInput): RollforwardProof {
  const gaps: string[] = [];
  const openingValue = usableAmount(opening);
  if (openingValue === null) gaps.push(`${openingLabel} has no value`);
  for (const movement of movements) {
    if (usableAmount(movement.value) === null) {
      gaps.push(`Movement "${movement.label || "unnamed"}" has no amount`);
    }
  }
  const statedInput = usableAmount(closing);
  if (closing !== undefined && statedInput === null) {
    gaps.push(`${closingLabel} has no value`);
  }

  if (openingValue === null || gaps.length > 0) {
    return {
      computable: false,
      gaps,
      movementTotal: null,
      derivedClosing: null,
      statedClosing: statedInput,
      difference: null,
      ties: false
    };
  }

  const movementTotal = movements.reduce((total, movement) => total + (usableAmount(movement.value) ?? 0), 0);
  const derivedClosing = roundMoney(openingValue + movementTotal, decimals, "half-even");
  const statedClosing = statedInput ?? derivedClosing;
  const difference = roundMoney(statedClosing - derivedClosing, decimals, "half-even");
  return {
    computable: true,
    gaps,
    movementTotal,
    derivedClosing,
    statedClosing,
    difference,
    ties: Math.abs(difference) < Math.pow(10, -decimals) / 2
  };
}

// ── Tie-out ──────────────────────────────────────────────────────────────────────────────────

export type TieOutStatus = "tied" | "tolerance" | "exception" | "indeterminate";

/** The data conditions a source or reconciling item can carry. */
export type TieOutCondition =
  | "confirmed"
  | "provisional"
  | "estimated"
  | "stale"
  | "overridden"
  | "missing"
  | "na"
  | "pending"
  | "exception"
  | "reconciled";

export interface TieOutProofSource {
  label: string;
  value?: number | null;
  condition?: TieOutCondition;
}

export interface TieOutProofItem {
  label: string;
  value: number | null | undefined;
  /** `false` marks an item as identified but not yet substantiated; it is listed, not summed. */
  explained?: boolean;
}

export interface TieOutProofInput {
  sourceA: TieOutProofSource | null | undefined;
  sourceB: TieOutProofSource | null | undefined;
  items?: readonly TieOutProofItem[];
  tolerance?: number;
  /** Caller-declared status. It can make the reading stricter, never more favourable. */
  status?: TieOutStatus;
}

export interface TieOutProof {
  status: TieOutStatus;
  /** What the amounts alone say, before any caller-declared status. */
  derivedStatus: TieOutStatus;
  /** Every reason the tie-out cannot be computed, in operator words. Empty when computable. */
  gaps: string[];
  sourceAValue: number | null;
  sourceBValue: number | null;
  /** A − B. `null` when indeterminate. */
  grossDifference: number | null;
  /** Σ of items with `explained !== false`. `null` when one of them has no amount. */
  explainedTotal: number | null;
  explainedCount: number;
  unexplainedItemCount: number;
  /** A + explained − B. `null` when indeterminate. */
  residual: number | null;
}

/** Residuals under half a cent are fully explained — the same epsilon the dashboard uses for "Balanced". */
export const TIE_OUT_EPSILON = 0.005;

// Conditions under which a stated value is not a usable input. Reading one of these as a number
// is the defect a tie-out exists to prevent: `missing` coerced to 0 makes the difference vanish.
const UNUSABLE_CONDITIONS: ReadonlySet<TieOutCondition> = new Set<TieOutCondition>([
  "missing",
  "pending",
  "stale",
  "na",
  "exception"
]);

const STATUS_RANK: Record<Exclude<TieOutStatus, "indeterminate">, number> = {
  tied: 0,
  tolerance: 1,
  exception: 2
};

function sourceGap(source: TieOutProofSource | null | undefined, fallbackLabel: string): string | null {
  const label = source?.label || fallbackLabel;
  if (!source || usableAmount(source.value) === null) return `${label} has no value`;
  if (source.condition && UNUSABLE_CONDITIONS.has(source.condition)) {
    return `${label} is ${source.condition === "na" ? "not applicable" : source.condition}`;
  }
  return null;
}

/**
 * Prove a tie-out between two independently sourced values: gross difference, explained
 * reconciling items, and the unexplained residual.
 *
 *  - Any unusable input makes the result `indeterminate`; a declared status cannot outrank it.
 *  - `tied` needs the residual under {@link TIE_OUT_EPSILON} after the explained items.
 *  - `tolerance` (residual within tolerance but unexplained) is not success.
 *  - A declared status is honoured only when it is at least as strict as the arithmetic, so a
 *    caller can report open breaks behind a zero net difference but cannot declare a residual away.
 */
export function proveTieOut({ sourceA, sourceB, items = [], tolerance = 0, status }: TieOutProofInput): TieOutProof {
  const gaps: string[] = [];
  const gapA = sourceGap(sourceA, "Source A");
  const gapB = sourceGap(sourceB, "Source B");
  if (gapA) gaps.push(gapA);
  if (gapB) gaps.push(gapB);

  const explainedItems = items.filter((item) => item.explained !== false);
  let explainedTotal: number | null = 0;
  for (const item of explainedItems) {
    const value = usableAmount(item.value);
    if (value === null) {
      gaps.push(`Reconciling item "${item.label || "unnamed"}" has no amount`);
      explainedTotal = null;
    } else if (explainedTotal !== null) {
      explainedTotal += value;
    }
  }

  const sourceAValue = usableAmount(sourceA?.value);
  const sourceBValue = usableAmount(sourceB?.value);
  const shared = {
    gaps,
    sourceAValue,
    sourceBValue,
    explainedTotal,
    explainedCount: explainedItems.length,
    unexplainedItemCount: items.length - explainedItems.length
  };

  if (gaps.length > 0 || sourceAValue === null || sourceBValue === null || explainedTotal === null) {
    return { ...shared, status: "indeterminate", derivedStatus: "indeterminate", grossDifference: null, residual: null };
  }

  const grossDifference = sourceAValue - sourceBValue;
  const residual = sourceAValue + explainedTotal - sourceBValue;
  const derivedStatus: Exclude<TieOutStatus, "indeterminate"> = Math.abs(residual) < TIE_OUT_EPSILON
    ? "tied"
    : Math.abs(residual) <= Math.abs(tolerance)
      ? "tolerance"
      : "exception";
  if (status === "indeterminate") {
    // The caller knows an input is unusable in a way the amounts do not show. Believe it.
    return {
      ...shared,
      gaps: ["An input is reported as unavailable"],
      status: "indeterminate",
      derivedStatus,
      grossDifference: null,
      residual: null
    };
  }

  const resolved = status && STATUS_RANK[status] > STATUS_RANK[derivedStatus] ? status : derivedStatus;
  return { ...shared, status: resolved, derivedStatus, grossDifference, residual };
}

// ── Wording ──────────────────────────────────────────────────────────────────────────────────
// The sentences are part of the contract: both proof components and any summary that repeats
// their verdict read from here, so a chip and the strip beside it cannot drift apart.

export const TIE_OUT_STATUS_TEXT: Record<TieOutStatus, string> = {
  tied: "Tied out",
  tolerance: "Within tolerance",
  exception: "Exception",
  indeterminate: "Not tied — input missing"
};

export const TIE_OUT_STATUS_MARK: Record<TieOutStatus, string> = {
  tied: "✓",
  tolerance: "◇",
  exception: "▲",
  indeterminate: "■"
};

export type RollforwardVerdict = "tied" | "out" | "indeterminate" | "unsupported";

export interface RollforwardVerdictOptions {
  /**
   * Set false when opening, movements and closing are not independently stated — for example
   * one net movement that is the closing restated. Such figures agree by construction, so their
   * agreement is reported as `unsupported`, never as `tied`. @default true
   */
  independent?: boolean;
}

/**
 * Which of the four things a rollforward proof can say.
 *
 * `unsupported` only ever replaces `tied`: an out-by or a missing amount is still reported as
 * such, because figures that cannot disagree by construction and do anyway are a finding.
 */
export function rollforwardVerdict(
  proof: RollforwardProof,
  { independent = true }: RollforwardVerdictOptions = {}
): RollforwardVerdict {
  if (!proof.computable) return "indeterminate";
  if (!proof.ties) return "out";
  return independent ? "tied" : "unsupported";
}
