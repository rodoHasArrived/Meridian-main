/**
 * YieldCurve — term-structure line chart (yield vs tenor) for rates and bond surfaces
 * (Basket Builder, fixed-income research). Tenors position on a sqrt-of-years x-scale so
 * the money-market end doesn't crush. Prior-period overlays recede down the series ladder
 * (--chart-compare-1/2); scenario overlays render dashed --chart-scenario (terracotta) so a
 * shock can never read as observed data. An optional tenor-pair spread readout ("2Y–10Y")
 * prints top-left in bp, red with an "inverted" note when negative. Renders through
 * ChartFrame (real-pixel geometry).
 */
import * as React from "react";

export interface YieldCurvePoint {
  /** Tenor label drawn on the x-axis, e.g. "3M", "2Y", "10Y". */
  tenor: string;
  /** Tenor in years (0.25 = 3M) — drives x position. */
  years: number;
  /** Yield in percent, e.g. 4.32. */
  value: number;
}

export interface YieldCurveOverlay {
  points: YieldCurvePoint[];
  /** Legend label, e.g. "1M ago". @default "Series N" */
  label?: string;
  /** Force dashed rendering. Scenario overlays are always dashed. */
  dashed?: boolean;
  /** Hypothetical/shocked curve — renders dashed in --chart-scenario (terracotta). */
  scenario?: boolean;
  /** Override the ladder color (defaults: --chart-compare-1, then --chart-compare-2). */
  color?: string;
}

export interface YieldCurveProps {
  /** Primary curve ("Today"), shortest tenor first — drawn in --chart-primary (navy data ink). */
  points: YieldCurvePoint[];
  /** Prior-period / scenario curves, drawn behind the primary in listed order — each recedes
   *  down the series ladder unless it sets scenario or color. Renders a stacked legend. */
  overlays?: YieldCurveOverlay[] | null;
  /** Shorthand for one dashed overlay (prior date, benchmark) — ignored when overlays is set. */
  compare?: YieldCurvePoint[] | null;
  /** Legend label for the primary curve. @default "Current" */
  label?: string;
  /** Legend label for the compare shorthand. @default "Prior" */
  compareLabel?: string;
  /** Format for y-axis ticks. @default v => v.toFixed(2) + "%" */
  yFmt?: (v: number) => string;
  /** @default 5 */
  yTicks?: number;
  /** X positioning of tenors: "sqrt" (default, spreads the short end), "linear",
   *  or a custom (years) => number transform. */
  xScale?: "sqrt" | "linear" | ((years: number) => number);
  /** Print a spread readout for two tenors, e.g. { a: "2Y", b: "10Y" } — value(b)−value(a)
   *  in bp; renders red with "· inverted" when negative. */
  spread?: { a: string; b: string } | null;
  /** Draw point markers on the curves. @default true */
  markers?: boolean;
  /** @default "var(--chart-primary)" */
  lineColor?: string;
  /** @default "var(--chart-secondary)" */
  compareColor?: string;
  /** SVG height in px. @default 300 */
  height?: number;
  /** 3.5 · Forwarded to ChartFrame — names the figure (svg role="img"). */
  ariaLabel?: string;
  /** 3.5 · Forwarded to ChartFrame — linked description. */
  ariaDescription?: string;
  /** 3.5 · Forwarded to ChartFrame — visually-hidden data table/list behind the pixels. */
  accessibleData?: string | { label: string; value: React.ReactNode }[] | { caption?: string; columns: React.ReactNode[]; rows: React.ReactNode[][] };
}
export declare function YieldCurve(props: YieldCurveProps): React.JSX.Element;
