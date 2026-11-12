import * as React from "react";

export interface DonutSegment {
  id?: string | number;
  label: string;
  /** Rendered as absolute magnitude; use nonnegative part-to-whole inputs. */
  value: number;
  color?: string;
}
export interface DonutView {
  id: string;
  label: string;
  segments: DonutSegment[];
  centerLabel?: React.ReactNode;
  centerValue?: React.ReactNode;
}
export interface DonutChartProps {
  segments?: DonutSegment[] | null;
  views?: DonutView[] | null;
  /** Controlled view id; missing/unrecognized ids display the first view. */
  view?: string | null;
  onViewChange?: ((id: string) => void) | null;
  /** Square SVG edge in pixels. @default 196 */
  size?: number;
  /** @default 28 */
  thickness?: number;
  /** Gap between segments in degrees. @default 1.4 */
  gapDeg?: number;
  centerLabel?: React.ReactNode;
  centerValue?: React.ReactNode;
  /** Denominator; defaults to sum of absolute segment values. */
  total?: number | null;
  fmt?: (value: number) => string;
  /** Receives a fraction, not percentage points. */
  pctFmt?: (fraction: number) => string;
  /** @default true */
  legend?: boolean;
  /** Names the image and emits the accessible data table. */
  ariaLabel?: string;
  caption?: string;
}
export declare function DonutChart(props: DonutChartProps): React.ReactElement;
