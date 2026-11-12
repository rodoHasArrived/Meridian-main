import * as React from "react";
/**
 * Interval bar — quantile rows for one metric on a shared domain. Use when the
 * question is "how wide is the plausible range for this number" and a fan chart
 * over time would be more machinery than the decision needs: model ranges in an
 * evidence inspector, scenario outcomes, expected-value bands beside a table.
 *
 * Every row states its probability level in the label (`90%`, `50%`, `Mean`) —
 * an unlabelled band is a prohibited encoding. Rows share one domain so widths
 * are comparable; the midpoint tick marks the central estimate.
 *
 * Always pair with a note distinguishing **model uncertainty** from
 * **observed-data condition** — they are different claims and must not share a
 * treatment.
 *
 * @example
 * <IntervalBar
 *   domain={[3.9, 5.1]} format={(v) => v.toFixed(2)}
 *   rows={[
 *     { id:"p90",  label:"90%",  lo:4.07, hi:4.91, mid:4.49, wide:true },
 *     { id:"p50",  label:"50%",  lo:4.29, hi:4.68, mid:4.49 },
 *     { id:"mean", label:"Mean", lo:4.49, hi:4.49, mid:4.49, text:"4.49%" },
 *   ]}
 *   note="Intervals describe the scenario model, not observed market-data uncertainty." />
 */
export interface IntervalRow {
  id?: string;
  /** Probability level or estimator name — required, never implied by opacity. */
  label: string;
  lo: number;
  hi: number;
  /** Central estimate — draws the tick. */
  mid?: number;
  /** Outer (wider) interval styling. */
  wide?: boolean;
  /** Override the printed bounds text. */
  text?: string;
}
export interface IntervalBarProps extends React.HTMLAttributes<HTMLDivElement> {
  rows: IntervalRow[];
  /** Shared domain across rows; computed from the rows when omitted. */
  domain?: [number, number];
  format?: (v: number) => string;
  /** The caveat line — what the interval means and what it does not. */
  note?: string;
}
export declare function IntervalBar(props: IntervalBarProps): React.JSX.Element;
