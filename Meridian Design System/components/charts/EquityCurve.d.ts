/**
 * Equity / performance curve — line (with area fill) + benchmark overlay, value-axis labels,
 * gridlines, legend, crosshair readout, and an optional drawdown subpane. Mirrors the
 * performance charts in AdvancedAnalytics and backtest reporting.
 */
export interface EquitySeries {
  label: string;
  /** CSS color (token var). */
  color: string;
  points: number[];
  /** Render dashed (benchmarks). @default false */
  dashed?: boolean;
  /** Set false to skip the area fill on the primary series. */
  area?: boolean;
}
export interface EquityCurveProps {
  /** First series is primary (gets the area fill + crosshair price chip). */
  series: EquitySeries[];
  /** X-axis time labels, aligned to the point index. */
  labels?: string[];
  /** Drawdown values (≤ 0) for the bottom subpane. */
  drawdown?: number[] | null;
  /** Format value-axis + crosshair labels. @default v=>v.toFixed(0) */
  valueFmt?: (v: number) => string;
  /** Point index to mark with the crosshair. */
  crosshairIndex?: number | null;
  /** Approx. number of value gridlines. @default 5 */
  valueTicks?: number;
  /** Approx. number of time labels. @default 6 */
  timeTicks?: number;
  /** Show the legend row. @default true */
  showLegend?: boolean;
  /** Area fill under the primary series. @default true */
  fill?: boolean;
}
export declare function EquityCurve(props: EquityCurveProps): JSX.Element;
