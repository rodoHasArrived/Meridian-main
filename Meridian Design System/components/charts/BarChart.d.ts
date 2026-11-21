/**
 * BarChart — categorical comparison bars (P&L by sector, fees by venue, fills per venue).
 * Vertical SVG bars by default; `horizontal` renders ranked rows (label · bar · value) suited
 * to exposure-style views. `signed` tints bars red/green by sign; per-bar `colors` override.
 * Family data shape: plain `values` + parallel `labels`. Vertical mode renders through
 * ChartFrame (real-pixel geometry, left value rail).
 */
import * as React from "react";

export interface BarChartProps {
  values: number[];
  /** Category labels parallel to values. */
  labels: string[];
  /** Per-bar CSS color overrides (null entries fall through to signed/primary). @default null */
  colors?: (string | null)[] | null;
  /** Ranked rows (label · bar · value) instead of vertical SVG bars. @default false */
  horizontal?: boolean;
  /** Tint bars red/green by the sign of their value. @default false */
  signed?: boolean;
  /** Format rail ticks and value labels. @default String */
  valueFmt?: (v: number) => string;
  /** Value labels at bar ends. @default true */
  showValues?: boolean;
  /** Approx. number of value gridlines (vertical mode). @default 5 */
  valueTicks?: number;
  /** Sort by value; null keeps the given order. @default null */
  sort?: "asc" | "desc" | null;
  /** SVG height in px (vertical mode). @default 300 */
  height?: number;
  /** 3.5 · Forwarded to ChartFrame — names the figure (svg role="img"). */
  ariaLabel?: string;
  /** 3.5 · Forwarded to ChartFrame — linked description. */
  ariaDescription?: string;
  /** 3.5 · Forwarded to ChartFrame — visually-hidden data table/list behind the pixels. */
  accessibleData?: string | { label: string; value: React.ReactNode }[] | { caption?: string; columns: React.ReactNode[]; rows: React.ReactNode[][] };
}
export declare function BarChart(props: BarChartProps): React.JSX.Element;
