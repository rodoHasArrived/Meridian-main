/**
 * EquityCurve — performance line/area chart: primary strategy curve with soft fill, overlay
 * series (benchmark dashed), legend, last-value chip, optional cost-basis baseline, optional
 * drawdown subpane hanging below its own waterline, and a crosshair readout. Renders through
 * ChartFrame (real-pixel geometry — type and strokes never scale).
 */
import * as React from "react";

export interface EquitySeries {
  label: string;
  /** CSS color (token var). */
  color: string;
  points: number[];
  dashed?: boolean;
  /** Set false on the primary series to suppress its area fill. */
  area?: boolean;
}

export interface EquityCurveProps {
  /** First entry is the primary series (fill, last-value chip, crosshair chip). */
  series: EquitySeries[];
  /** X labels parallel to points; "" leaves a position unlabeled. */
  labels?: string[];
  /** Drawdown % values ≤ 0, parallel to points — rendered as a subpane. @default null */
  drawdown?: number[] | null;
  /** Value-rail formatter. @default Fmt.price */
  valueFmt?: (v: number) => string;
  /** @default null */
  crosshairIndex?: number | null;
  /** @default 6 */
  valueTicks?: number;
  /** @default 7 */
  timeTicks?: number;
  /** @default true */
  showLegend?: boolean;
  /** Area fill under the primary series. @default true */
  fill?: boolean;
  /** Reference value (cost basis, initial capital) drawn as a dashed rule. @default null */
  baseline?: number | null;
  /** @default null (falls back to valueFmt(baseline)) */
  baselineLabel?: string | null;
  /** Persistent last-value marker + rail chip. @default true */
  showLast?: boolean;
  /** SVG height in px (legend row adds ~27px above). @default 380 */
  height?: number;
  /** Value-rail width in px. @default 62 */
  rail?: number;
  /** 3.5 · Forwarded to ChartFrame — names the figure (svg role="img"). */
  ariaLabel?: string;
  /** 3.5 · Forwarded to ChartFrame — linked description. */
  ariaDescription?: string;
  /** 3.5 · Forwarded to ChartFrame — visually-hidden data table/list behind the pixels. */
  accessibleData?: string | { label: string; value: React.ReactNode }[] | { caption?: string; columns: React.ReactNode[]; rows: React.ReactNode[][] };
}
export declare function EquityCurve(props: EquityCurveProps): React.JSX.Element;
