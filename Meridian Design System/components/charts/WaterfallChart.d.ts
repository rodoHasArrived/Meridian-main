/**
 * WaterfallChart — additive bridge from a starting level through signed contributions to a
 * net (P&L attribution: gross → carry → funding → fees → net; NAV bridges; fee decomposition).
 * Items are `start` (absolute bar from zero), `delta` (floats from the running level — green
 * up, red down), or `total` (drops the running level back to zero, accent). A computed total
 * bar labeled `totalLabel` is appended unless the items already include one. Dashed connectors
 * carry the running level across gaps. Flat, token-driven.
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
}
export declare function WaterfallChart(props: WaterfallChartProps): JSX.Element;
