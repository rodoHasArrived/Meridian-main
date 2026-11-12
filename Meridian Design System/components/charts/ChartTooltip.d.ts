/**
 * ChartTooltip — floating readout for the chart interaction layer. Renders label/value rows
 * in a small card that follows the crosshair index (auto-flips left of center). Position it
 * inside the crosshair-bound wrapper (position:relative — useChartCrosshair's bind supplies
 * it). Pixel-based: pass the SAME padLeft/rail the chart uses (ChartFrame defaults 10/62).
 */
import * as React from "react";

export interface TooltipRow {
  label: string;
  value: string;
  /** CSS color for the value (token var). */
  color?: string;
}

export interface ChartTooltipProps {
  /** Crosshair index; null renders nothing. */
  index: number | null;
  /** Number of x positions in the paired chart. */
  count: number;
  /** Small-caps header line. */
  title?: React.ReactNode;
  rows: TooltipRow[];
  /** Chart's left inset in px. @default 10 */
  padLeft?: number;
  /** Chart's value-rail width in px. @default 62 */
  rail?: number;
}
export declare function ChartTooltip(props: ChartTooltipProps): React.JSX.Element;
