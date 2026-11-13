import type React from "react";
export type DatumRole = "baseline" | "selection" | "current-time" | "target" | "zero" | "warning";
export interface DatumProps extends React.HTMLAttributes<HTMLDivElement> {
  role?: DatumRole;
  orientation?: "vertical" | "horizontal";
  /** Normalized 0–1 position inside the nearest positioned container. */
  position?: number;
  /** Second normalized position. Supplied, the datum becomes a band spanning
   *  `position`→`to` (alpha-10 wash + hairline edges) instead of a single line.
   *  Use for embargo windows, holiday gaps, tolerance corridors — never for
   *  model uncertainty, which belongs to `IntervalBar`. */
  to?: number;
  label?: React.ReactNode;
  value?: React.ReactNode;
  marker?: boolean;
  labelPlacement?: "before" | "after";
  interactive?: boolean;
  /** Render as an SVG `<g>` of `<line>`/`<rect>`/`<text>` for use inside a plot's
   *  own `<svg>`, so the datum shares the chart's coordinate space and prints
   *  with it. Default false renders the absolutely-positioned HTML element. */
  svg?: boolean;
  /** Label stacking lane. Normally assigned by `DatumGroup`, not by hand. */
  labelLane?: number;
  onActivate?: (event: React.MouseEvent | React.KeyboardEvent) => void;
}
export declare function Datum(props: DatumProps): React.JSX.Element;

export interface DatumGroupProps extends React.HTMLAttributes<HTMLElement> {
  /** Container width in px. Omit to self-measure with a ResizeObserver. */
  width?: number;
  /** Vertical step between stacked labels. Default 21. */
  laneHeight?: number;
  /** Minimum px between two labels before they are treated as colliding. Default 4. */
  gap?: number;
  as?: React.ElementType;
  children?: React.ReactNode;
}
/** Assigns non-overlapping label lanes to its child Datums. No-op until it has a width. */
export declare function DatumGroup(props: DatumGroupProps): React.JSX.Element;
