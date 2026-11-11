/**
 * ScatterChart — X/Y relationship across a sample (spread vs implied vol, factor exposure,
 * tracking error vs return). History points optionally fade in with recency so the most
 * recent observations read as more saturated; an optional highlighted "current" point sits
 * over the cloud; an optional least-squares trendline overlays. Renders through ChartFrame
 * (real-pixel geometry, shared nice ticks on both axes).
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
  /** SVG height in px. @default 300 */
  height?: number;
  /** 3.5 · Forwarded to ChartFrame — names the figure (svg role="img"). */
  ariaLabel?: string;
  /** 3.5 · Forwarded to ChartFrame — linked description. */
  ariaDescription?: string;
  /** 3.5 · Forwarded to ChartFrame — visually-hidden data table/list behind the pixels. */
  accessibleData?: string | { label: string; value: React.ReactNode }[] | { caption?: string; columns: React.ReactNode[]; rows: React.ReactNode[][] };
}
export declare function ScatterChart(props: ScatterChartProps): JSX.Element;
