import * as React from "react";
/**
 * Rollforward trace — opening balance → the movements that explain the change →
 * closing balance, with the arithmetic proved on screen. Use it for book value,
 * carrying value, position par, NAV, reserve, or any balance a reviewer must be
 * able to reconstruct.
 *
 * Every movement can carry a link to its calculation, journal, schedule, lot,
 * rule, or evidence — a movement that cannot be resolved is a finding, not a
 * feature. The component sums the movements itself: when the stated `closing`
 * disagrees it prints **Out by …** instead of quietly rendering both numbers.
 *
 * @example
 * <RollforwardTrace
 *   opening={9_875_000} closing={9_878_812.40} currency="USD"
 *   movements={[
 *     { label: "Discount accretion", value: 3_812.40, linkLabel: "Journal", onOpen: openJournal },
 *     { label: "Principal movement", value: 0 },
 *     { label: "Other book-value adjustment", value: 0 },
 *   ]} />
 */
export interface RollforwardMovement {
  id?: string;
  label: string;
  /** Signed amount; the sign drives the +/−/± operator column. */
  value: number;
  /** Force the operator glyph ("+", "−", "±"). */
  op?: string;
  /** Mono qualifier after the label — method, rate, period. */
  note?: string;
  /** Resolve-to link text; renders a button when paired with `onOpen`. */
  linkLabel?: string;
  onOpen?: () => void;
  /** Data-condition key tinting the amount ("estimated", "overridden"). */
  tone?: string;
  /** Set false to print 0.00 instead of the zero dash. */
  emptyDash?: boolean;
}
export interface RollforwardTraceProps extends React.HTMLAttributes<HTMLDivElement> {
  openingLabel?: string;
  opening: number;
  movements: RollforwardMovement[];
  closingLabel?: string;
  /** Stated closing balance. Omit to use opening + movements. */
  closing?: number;
  currency?: string;
  decimals?: number;
  /** The tie-out strip. Leave on — the proof is the point. */
  showProof?: boolean;
}
export declare function RollforwardTrace(props: RollforwardTraceProps): React.JSX.Element;
