import * as React from "react";
import type { FrameGeometry } from "./ChartFrame";

export type ChartAnnotationKind = "event" | "positive" | "negative" | "warning" | "information" | "scenario";
export interface ChartAnnotationBand {
  /** Stable identity shared by the overlay and the list. Defaults to `band-<position>`. */
  id?: string | number;
  /** Inclusive x-index bounds in the frame's coordinate system. */
  from: number;
  to: number;
  label?: string;
  kind?: ChartAnnotationKind;
  /** Explanation shown when the list entry is opened. */
  detail?: string;
}
export interface ChartAnnotationEvent {
  /** Defaults to `event-<position>`. */
  id?: string | number;
  index: number;
  label?: string;
  /** Defaults to the event's one-based position. */
  badge?: string | number;
  kind?: ChartAnnotationKind;
  /** Explanation shown when the list entry is opened. */
  detail?: string;
}
export interface ChartAnnotationCallout {
  /** Defaults to `callout-<position>`. */
  id?: string | number;
  index: number;
  value: number;
  text: string;
  /** Pixel offset; defaults to 26. */
  dx?: number;
  /** Pixel offset; defaults to -26. */
  dy?: number;
  anchor?: "start" | "middle" | "end";
  kind?: ChartAnnotationKind;
  /** Explanation shown when the list entry is opened. */
  detail?: string;
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
  /** Id of the annotation to emphasise, normally from the list's `onActiveChange`. */
  activeId?: string | null;
}
export interface ChartAnnotationListProps {
  bands?: ChartAnnotationBand[];
  events?: ChartAnnotationEvent[];
  callouts?: ChartAnnotationCallout[];
  /** Names the list. @default "Chart annotations" */
  ariaLabel?: string;
  /** x-index to the label the axis shows (a date, a period). @default "#<index>" */
  formatIndex?: (index: number) => string;
  /** Callout value to its printed figure, unit included. */
  formatValue?: (value: number) => string;
  activeId?: string | null;
  /** Called with an annotation id on focus/hover and with null on blur/leave. */
  onActiveChange?: (id: string | null) => void;
  emptyLabel?: string;
}
/** SVG overlay: render inside ChartFrame's SVG, not as a standalone chart. */
export declare const ChartAnnotations: {
  (props: ChartAnnotationsProps): React.ReactElement | null;
  displayName?: string;
  /**
   * The same annotations as an HTML list in time order: the keyboard and screen-reader path.
   * Render it beside the chart, outside ChartFrame.
   */
  List: (props: ChartAnnotationListProps) => React.ReactElement;
  /** The identity the overlay and list use for an annotation. */
  idOf: (type: "band" | "event" | "callout", item: { id?: string | number }, position: number) => string;
};
