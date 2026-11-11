import * as React from "react";
import type { FrameGeometry } from "./ChartFrame";

export type ChartAnnotationKind = "event" | "positive" | "negative" | "warning" | "information" | "scenario";
export interface ChartAnnotationBand {
  /** Inclusive x-index bounds in the frame's coordinate system. */
  from: number;
  to: number;
  label?: string;
  kind?: ChartAnnotationKind;
}
export interface ChartAnnotationEvent {
  index: number;
  label?: string;
  /** Defaults to the event's one-based position. */
  badge?: string | number;
  kind?: ChartAnnotationKind;
}
export interface ChartAnnotationCallout {
  index: number;
  value: number;
  text: string;
  /** Pixel offset; defaults to 26. */
  dx?: number;
  /** Pixel offset; defaults to -26. */
  dy?: number;
  anchor?: "start" | "middle" | "end";
  kind?: ChartAnnotationKind;
}
export interface ChartAnnotationsProps {
  /** Geometry from ChartFrame's children render prop. Missing geometry renders nothing. */
  g?: FrameGeometry | null;
  /** Required for value-positioned callouts; bands/events can span the full frame. */
  pane?: string | null;
  bands?: ChartAnnotationBand[];
  events?: ChartAnnotationEvent[];
  callouts?: ChartAnnotationCallout[];
  /** Show the events' vertical rules. @default true */
  showRules?: boolean;
  /** Event badge baseline in pixels; defaults to the pane top. */
  badgeY?: number | null;
}
/** SVG overlay: render inside ChartFrame's SVG, not as a standalone chart. */
export declare function ChartAnnotations(props: ChartAnnotationsProps): React.ReactElement | null;
