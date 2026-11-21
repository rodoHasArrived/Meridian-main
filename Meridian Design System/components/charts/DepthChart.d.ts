/**
 * DepthChart — order-book market depth (volume profile). Cumulative bid size rises as a green
 * step area toward the mid from the left; cumulative ask size rises red to the right. The mid
 * price marks the spread. Reads a live order book at a glance. Renders through ChartFrame
 * (real-pixel geometry).
 */
import * as React from "react";

export interface DepthLevel {
  price: number;
  size: number;
}

export interface DepthChartProps {
  /** Bid levels — any order; cumulated outward from the mid. */
  bids: DepthLevel[];
  /** Ask levels — any order; cumulated outward from the mid. */
  asks: DepthLevel[];
  /** Mid price. @default midpoint of best bid / best ask */
  mid?: number | null;
  /** Format for price-axis values. @default Fmt.price */
  priceFmt?: (p: number) => string;
  /** Format for cumulative-size axis. @default Fmt.volume */
  sizeFmt?: (s: number) => string;
  /** @default 4 */
  sizeTicks?: number;
  /** @default 7 */
  priceTicks?: number;
  /** SVG height in px. @default 320 */
  height?: number;
  /** 3.5 · Forwarded to ChartFrame — names the figure (svg role="img"). */
  ariaLabel?: string;
  /** 3.5 · Forwarded to ChartFrame — linked description. */
  ariaDescription?: string;
  /** 3.5 · Forwarded to ChartFrame — visually-hidden data table/list behind the pixels. */
  accessibleData?: string | { label: string; value: React.ReactNode }[] | { caption?: string; columns: React.ReactNode[]; rows: React.ReactNode[][] };
}
export declare function DepthChart(props: DepthChartProps): React.JSX.Element;
