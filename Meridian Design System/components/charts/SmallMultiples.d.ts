/**
 * SmallMultiples — a trellis of mini plates, one per **metric**, for a single entity.
 *
 * Know the boundary before you reach for it: **PlateGrid** compares the *same* metric across
 * *many entities* and shares one domain by construction, so a visible difference is a real
 * difference. **SmallMultiples** shows *different metrics* for *one entity*, so each panel
 * scales to its own range by default — a 0–6 coverage ratio and a 0–300 bp spread both stay
 * readable side by side. Set `sharedScale` only when the panels are genuinely commensurate.
 *
 * Plate geometry comes from the comparison-plate token contract (`--plate-*`), so padding,
 * plot height, and label placement are identical across the set.
 *
 * @example
 * <SmallMultiples
 *   ariaLabel="Risk metrics — Meridian Global Macro"
 *   panels={[
 *     { label:"Gross exposure", values:grossSeries, sub:"vs 180% limit", baseline:180, fmt:(v)=>v.toFixed(0)+"%" },
 *     { label:"Beta to SPX",    values:betaSeries,  sub:"60d rolling", baseline:1 },
 *     { label:"Credit spread",  values:oasSeries,   sub:"OAS, bp", role:"prior" },
 *   ]}
 *   columns={3} />
 */
import * as React from "react";

export interface SmallMultiplePanel {
  id?: string;
  /** Metric name — shown small-caps at the plate head. */
  label: string;
  values: Array<number | null>;
  /** Context line under the plot — limit, window, units. */
  sub?: string;
  /** Series role for the trace. @default "actual" */
  role?: "actual" | "prior" | "benchmark" | "scenario" | "target" | "estimated" | "positive" | "negative";
  /** Per-panel domain override; otherwise computed from this panel's values. */
  domain?: [number, number];
  /** Per-panel dashed reference rule (overrides the set-level `baseline`). */
  baseline?: number | null;
  /** Per-panel value format (overrides the set-level `fmt`). */
  fmt?: (v: number) => string;
}

export interface SmallMultiplesProps {
  panels: SmallMultiplePanel[];
  /** Fixed column count. Omit to auto-fill on `--plate-min-width`. */
  columns?: number | null;
  /** One domain across the whole set. Only for commensurate metrics. @default false */
  sharedScale?: boolean;
  /** Plot height per plate in px. @default 108 */
  height?: number | null;
  /** @default true */
  showArea?: boolean;
  /** Per-panel min/max micro rail on the left of each plot. @default true */
  showScale?: boolean;
  /** Dashed reference rule across every plate (0 for spreads and P&L). */
  baseline?: number | null;
  fmt?: (v: number) => string;
  /** Names the set and emits a visually-hidden metric/last-value list. */
  ariaLabel?: string;
  /** Note under the grid — what the window is, what the panels share. */
  caption?: string;
}
export declare function SmallMultiples(props: SmallMultiplesProps): JSX.Element;
