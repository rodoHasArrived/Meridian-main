/**
 * ScatterChart — X/Y relationship across a sample (spread vs implied vol, factor exposure,
 * tracking error vs return). History points optionally fade in with recency so the most
 * recent observations read as more saturated; an optional highlighted "current" point sits
 * over the cloud; an optional least-squares trendline overlays. Flat, token-driven.
 */
import * as React from "react";

export interface ScatterPoint {
  x: number;
  y: number;
}

export interface ScatterChartProps {
  /** Historical sample, oldest first (drives the recency fade). */
  points: ScatterPoint[];
  /** Optional highlighted "current" observation, drawn larger and solid. */
  current?: ScatterPoint | null;
  /** Draw a least-squares regression line through `points`. @default true */
  trendline?: boolean;
  /** Format for x-axis tick values. @default v => v.toFixed(0) */
  xFmt?: (v: number) => string;
  /** Format for y-axis tick values. @default v => v.toFixed(0) */
  yFmt?: (v: number) => string;
  /** @default 6 */
  xTicks?: number;
  /** @default 5 */
  yTicks?: number;
  /** Fade older points lighter, newest points more saturated. @default true */
  fadeByRecency?: boolean;
  /** @default "var(--chart-secondary)" */
  pointColor?: string;
  /** @default "var(--orange)" */
  currentColor?: string;
}
export declare function ScatterChart(props: ScatterChartProps): JSX.Element;
