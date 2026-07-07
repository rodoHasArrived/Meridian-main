/**
 * Histogram — distribution of a sample (daily returns, slippage, fill latency). Pass raw
 * `values` (auto-binned into `binCount`) or precomputed `bins`. When `signed`, bars left of
 * zero tint red and right of zero green so a returns distribution reads instantly; an optional
 * dashed mean rule overlays. Flat, token-driven.
 */
import * as React from "react";

export interface HistogramBin {
  x0: number;
  x1: number;
  count: number;
}

export interface HistogramProps {
  /** Raw sample — auto-binned. Provide this OR `bins`. */
  values?: number[] | null;
  /** Precomputed bins. Provide this OR `values`. */
  bins?: HistogramBin[] | null;
  /** Bin count when auto-binning `values`. @default 24 */
  binCount?: number;
  /** Tint bars red/green by the sign of their center (returns view). @default true */
  signed?: boolean;
  /** Draw the dashed mean rule. @default true */
  showMean?: boolean;
  /** Override the computed mean. */
  mean?: number | null;
  /** Format for x-axis bin-edge values. @default v => v.toFixed(1) */
  valueFmt?: (v: number) => string;
  /** Format for y-axis counts. @default String */
  countFmt?: (c: number) => string;
  /** @default 7 */
  xTicks?: number;
}
export declare function Histogram(props: HistogramProps): JSX.Element;
