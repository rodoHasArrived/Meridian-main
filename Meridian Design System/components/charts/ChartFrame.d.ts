/**
 * ChartFrame — the shared axis/plot chrome primitive behind the Meridian chart family.
 * Measures its own width and renders the SVG at real pixel size (never transform-scaled),
 * so tick type and rule weights are identical at any container width. Owns pane stacking,
 * nice-tick gridlines, the value rail, x labels, legend row, and the crosshair vline;
 * the chart supplies only its marks via the `children` render prop.
 *
 * Also exported: `AxisChip` (right-rail value chip for crosshair/last-value readouts),
 * `AXIS_FONT` (the family's tick-label style), and — from the kernel — `Fmt`, the named
 * formatters (price, currency, bp, volume, date) charts use as `fmt`-prop defaults.
 */
import * as React from "react";

export interface FramePane {
  /** Identifier charts use to fetch this pane's geometry: `geo.pane(key)`. */
  key: string;
  /** Vertical share of the plot area relative to sibling panes. @default 1 */
  flex?: number;
  /** Value domain [lo, hi] mapped to the pane's pixel range. */
  domain: [number, number];
  /** Target gridline count; 0 suppresses grid + rail labels for this pane. @default 5 */
  ticks?: number;
  /** Rail label formatter. @default String */
  fmt?: (v: number) => string;
  /** Draw a heavier zero/waterline rule at this value if inside the domain. */
  zero?: number;
}

export interface FrameGeometry {
  /** Measured pixel width of the frame. */
  w: number;
  plotL: number;
  plotR: number;
  innerT: number;
  innerB: number;
  n: number;
  /** Categorical slot width (plot span / n). */
  band: number;
  /** Index → x px ("point": edges at first/last; "band": slot centers). */
  x: (i: number) => number;
  /** Pane geometry by key: { t, b, h, domain, y(value) → px }. */
  pane: (key: string) => { t: number; b: number; h: number; domain: [number, number]; y: (v: number) => number };
}

export interface LegendEntry {
  label: string;
  /** CSS color (token var). */
  color: string;
  dashed?: boolean;
  /** Render a translucent block instead of a line sample. */
  swatch?: boolean;
}

export interface ChartFrameProps {
  /** SVG height in px (the legend row adds ~27px above). @default 320 */
  height?: number;
  /** Number of x positions. */
  n: number;
  /** "point" = series (first/last on plot edges); "band" = categorical slots. @default "point" */
  xMode?: "point" | "band";
  /** One string per index; "" leaves the position unlabeled. Sparse arrays thin correctly. */
  xLabels?: string[];
  /** Target count of shown x labels. @default 7 */
  xTicks?: number;
  /** Value-rail width in px. @default 62 */
  rail?: number;
  /** @default "right" */
  railSide?: "left" | "right";
  padL?: number;
  padT?: number;
  axisB?: number;
  /** Vertical gap between panes. @default 12 */
  gap?: number;
  panes: FramePane[];
  legend?: LegendEntry[] | null;
  /** Draws the shared vertical crosshair rule. */
  crosshairIndex?: number | null;
  /** Faint rule on the non-rail edge. @default false */
  leftRule?: boolean;
  children: (geo: FrameGeometry) => React.ReactNode;
  /** 3.5 · Names the figure (svg role="img"). Without it the svg is presentational (aria-hidden). */
  ariaLabel?: string;
  /** 3.5 · One-sentence reading of the figure, linked via aria-describedby. */
  ariaDescription?: string;
  /** 3.5 · Visually-hidden data behind the pixels: a summary string, {label,value} pairs, or a
   *  full table ({caption?, columns, rows}). */
  accessibleData?: string | { label: string; value: React.ReactNode }[] | { caption?: string; columns: React.ReactNode[]; rows: React.ReactNode[][] };
}
export declare function ChartFrame(props: ChartFrameProps): JSX.Element;

export interface AxisChipProps {
  x: number;
  /** Vertical center. */
  y: number;
  /** @default 58 */
  width?: number;
  text: string;
  /** @default "var(--chart-crosshair)" */
  fill?: string;
  /** @default 4 */
  rx?: number;
}
export declare function AxisChip(props: AxisChipProps): JSX.Element;
