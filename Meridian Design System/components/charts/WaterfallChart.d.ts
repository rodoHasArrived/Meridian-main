/**
 * WaterfallChart — additive bridge from a starting level through signed contributions to a
 * net (P&L attribution: gross → carry → funding → fees → net; NAV bridges; fee decomposition).
 * Items are `start` (absolute bar from zero), `delta` (floats from the running level — green
 * up, red down), or `total` (drops the running level back to zero, accent). A computed total
 * bar labeled `totalLabel` is appended unless the items already include one. Dashed connectors
 * carry the running level across gaps. Renders through ChartFrame (real-pixel geometry).
 */
import * as React from "react";

export interface WaterfallItem {
  label: string;
  /** Contribution for `delta`; absolute level for `start`. Ignored for `total` (computed). */
  value: number;
  /** @default "delta" */
  kind?: "start" | "delta" | "total";
  /** CSS color (token var) — overrides the kind/sign tinting for this bar. */
  color?: string;
}

export interface WaterfallChartProps {
  items: WaterfallItem[];
  /** Append a computed total bar when items don't include a `total`. @default true */
  showTotal?: boolean;
  /** @default "Net" */
  totalLabel?: string;
  /** Format axis ticks and start/total labels. @default v => v.toFixed(1) */
  valueFmt?: (v: number) => string;
  /** Format delta labels. @default explicit-sign valueFmt */
  deltaFmt?: ((v: number) => string) | null;
  /** Approx. number of value gridlines. @default 5 */
  valueTicks?: number;
  /** Dashed level connectors between bars. @default true */
  showConnectors?: boolean;
  /** SVG height in px. @default 300 */
  height?: number;
  /** 3.5 · Forwarded to ChartFrame — names the figure (svg role="img"). */
  ariaLabel?: string;
  /** 3.5 · Forwarded to ChartFrame — linked description. */
  ariaDescription?: string;
  /** 3.5 · Forwarded to ChartFrame — visually-hidden data table/list behind the pixels. */
  accessibleData?: string | { label: string; value: React.ReactNode }[] | { caption?: string; columns: React.ReactNode[]; rows: React.ReactNode[][] };
}
export declare function WaterfallChart(props: WaterfallChartProps): React.JSX.Element;
