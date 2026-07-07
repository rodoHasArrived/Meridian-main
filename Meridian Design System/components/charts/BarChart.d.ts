/**
 * BarChart — categorical comparison bars (P&L by sector, fees by venue, fills per venue).
 * Vertical SVG bars by default; `horizontal` renders ranked rows (label · bar · value) suited
 * to exposure-style views. `signed` tints bars red/green by sign; custom per-bar `color`
 * overrides. Flat, token-driven — the missing plain-bars primitive next to Histogram/Scatter.
 */
import * as React from "react";

export interface BarDatum {
  label: string;
  value: number;
  /** CSS color (token var) — overrides signed/primary tinting for this bar. */
  color?: string;
}

export interface BarChartProps {
  data: BarDatum[];
  /** Ranked rows (label · bar · value) instead of vertical SVG bars. @default false */
  horizontal?: boolean;
  /** Tint bars red/green by the sign of their value. @default false */
  signed?: boolean;
  /** Format axis ticks and value labels. @default String */
  valueFmt?: (v: number) => string;
  /** Value labels at bar ends. @default true */
  showValues?: boolean;
  /** Approx. number of value gridlines (vertical mode). @default 5 */
  valueTicks?: number;
  /** Sort by value; null keeps the given order. @default null */
  sort?: "asc" | "desc" | null;
}
export declare function BarChart(props: BarChartProps): JSX.Element;
