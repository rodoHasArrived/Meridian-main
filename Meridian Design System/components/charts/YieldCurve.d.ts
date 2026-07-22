/**
 * YieldCurve — term-structure line chart (yield vs tenor) for rates and bond surfaces
 * (Basket Builder, fixed-income research). Tenors position on a sqrt-of-years x-scale so
 * the money-market end doesn't crush; an optional second series (prior date, benchmark)
 * overlays dashed with a legend; an optional tenor-pair spread readout ("2Y–10Y") prints
 * top-left in bp, red with an "inverted" note when negative. Flat, token-driven.
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

export interface YieldCurveProps {
  /** Primary curve, shortest tenor first. */
  points: YieldCurvePoint[];
  /** Optional comparison curve (prior date, benchmark) — dashed, secondary color. */
  compare?: YieldCurvePoint[] | null;
  /** Legend label for the primary curve. @default "Current" */
  label?: string;
  /** Legend label for the comparison curve. @default "Prior" */
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
}
export declare function YieldCurve(props: YieldCurveProps): JSX.Element;
