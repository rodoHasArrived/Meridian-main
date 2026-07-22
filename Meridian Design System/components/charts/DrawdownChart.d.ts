/**
 * DrawdownChart — the underwater plot. Drawdown (peak-to-trough decline, always ≤ 0) hangs
 * below a 0% waterline as a filled curve, with an optional dashed limit rule and a marker on
 * the maximum drawdown. Pairs beside an EquityCurve in backtest / strategy reporting.
 */
import * as React from "react";

export interface DrawdownChartProps {
  /** Drawdown series in percent — 0 or negative values (e.g. -12.4). */
  series: number[];
  /** Time-axis labels, parallel to `series`. */
  labels?: string[];
  /** Optional limit line in percent (e.g. -10) — dashed warning rule. */
  threshold?: number | null;
  /** Format for axis + marker values. @default v => `${v.toFixed(0)}%` */
  valueFmt?: (v: number) => string;
  /** Mark the maximum drawdown point. @default true */
  markMax?: boolean;
  /** @default 5 */
  valueTicks?: number;
  /** @default 7 */
  timeTicks?: number;
}
export declare function DrawdownChart(props: DrawdownChartProps): JSX.Element;
