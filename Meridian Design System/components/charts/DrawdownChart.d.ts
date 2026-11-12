/**
 * DrawdownChart — the underwater plot. Drawdown is peak-to-trough decline, always ≤ 0; the
 * curve hangs below the 0% waterline with a filled area, an optional recovery-threshold rule,
 * and a marker + chip on the maximum drawdown. Pair beside an EquityCurve in backtest reports.
 * Renders through ChartFrame (real-pixel geometry — type and strokes never scale).
 */
import * as React from "react";

export interface DrawdownChartProps {
  /** Drawdown % values, 0 or negative (e.g. -12.4). */
  series: number[];
  /** X labels parallel to series; "" leaves a position unlabeled. */
  labels?: string[];
  /** e.g. -10 → dashed warning rule at -10%. @default null */
  threshold?: number | null;
  /** @default v => `${v.toFixed(0)}%` */
  valueFmt?: (v: number) => string;
  /** Marker + chip on the maximum drawdown. @default true */
  markMax?: boolean;
  /** @default 5 */
  valueTicks?: number;
  /** @default 7 */
  timeTicks?: number;
  /** SVG height in px. @default 300 */
  height?: number;
  /** Value-rail width in px. @default 56 */
  rail?: number;
  /** 3.5 · Forwarded to ChartFrame — names the figure (svg role="img"). */
  ariaLabel?: string;
  /** 3.5 · Forwarded to ChartFrame — linked description. */
  ariaDescription?: string;
  /** 3.5 · Forwarded to ChartFrame — visually-hidden data table/list behind the pixels. */
  accessibleData?: string | { label: string; value: React.ReactNode }[] | { caption?: string; columns: React.ReactNode[]; rows: React.ReactNode[][] };
}
export declare function DrawdownChart(props: DrawdownChartProps): React.JSX.Element;
