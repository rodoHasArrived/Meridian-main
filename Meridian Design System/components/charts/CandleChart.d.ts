/**
 * CandleChart — candlestick price chart with MA overlays, optional volume subpane, and a
 * crosshair readout chip on the price rail. Up = --chart-equity, down = --chart-drawdown.
 * Renders through ChartFrame (real-pixel geometry — type and strokes never scale).
 * Interactive crosshair: pair with ChartCursor.useChartCrosshair({padLeft:10, rail}).
 */
import * as React from "react";

export interface CandleBar {
  /** Time label shown on the x axis, e.g. "09:30". */
  t: string;
  o: number;
  h: number;
  l: number;
  c: number;
  /** Volume (used when showVolume). */
  v?: number;
}

export interface CandleOverlay {
  label: string;
  /** CSS color (token var). */
  color: string;
  /** Moving-average window in bars. */
  win: number;
}

export interface CandleChartProps {
  bars: CandleBar[];
  /** @default MA20 (--accent) + MA50 (--orange) */
  overlays?: CandleOverlay[];
  /** Index of the highlighted bar; null hides the crosshair. @default null */
  crosshairIndex?: number | null;
  /** @default true */
  showVolume?: boolean;
  /** Price-rail formatter. @default Fmt.price */
  priceFmt?: (v: number) => string;
  /** @default 6 */
  priceTicks?: number;
  /** @default 7 */
  timeTicks?: number;
  /** SVG height in px. @default 420 */
  height?: number;
  /** Price-rail width in px. @default 64 */
  rail?: number;
  /** 3.5 · Forwarded to ChartFrame — names the figure (svg role="img"). */
  ariaLabel?: string;
  /** 3.5 · Forwarded to ChartFrame — linked description. */
  ariaDescription?: string;
  /** 3.5 · Forwarded to ChartFrame — visually-hidden data table/list behind the pixels. */
  accessibleData?: string | { label: string; value: React.ReactNode }[] | { caption?: string; columns: React.ReactNode[]; rows: React.ReactNode[][] };
}
export declare function CandleChart(props: CandleChartProps): React.JSX.Element;
