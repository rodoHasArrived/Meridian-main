import * as React from "react";
import type { ChartFrameProps } from "./ChartFrame";

export interface DistributionBand {
  lower: Array<number | null>;
  upper: Array<number | null>;
  label?: string;
}
export interface DistributionBox {
  label: string;
  min: number;
  q1: number;
  median: number;
  q3: number;
  max: number;
  outliers?: number[];
  kind?: "positive" | "negative";
}
export interface DistributionRangeProps {
  /** Projection over time or categorical five-number summaries. @default "fan" */
  mode?: "fan" | "box";
  median?: Array<number | null>;
  /** Fan bands ordered outermost to innermost; aligned with median/labels. */
  bands?: DistributionBand[];
  /** Observed path; null entries indicate unobserved positions. */
  actual?: Array<number | null> | null;
  labels?: string[];
  boxes?: DistributionBox[];
  domain?: [number, number] | null;
  fmt?: (value: number) => string;
  /** SVG plot height. @default 300 */
  height?: number;
  zero?: number | null;
  /** Axis value-rail width. @default 62 */
  rail?: number;
  note?: React.ReactNode;
  crosshairIndex?: number | null;
  ariaLabel?: string;
  ariaDescription?: string;
  accessibleData?: ChartFrameProps["accessibleData"];
}
/** Caller validates quantile ordering and states probability/model assumptions. */
export declare function DistributionRange(props: DistributionRangeProps): React.ReactElement;
