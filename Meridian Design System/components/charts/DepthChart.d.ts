/**
 * DepthChart — order-book market depth (volume profile). Cumulative bid size rises as a green
 * step area toward the mid from the left; cumulative ask size rises red to the right. The mid
 * price marks the spread. Reads a live order book at a glance.
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
  /** Format for price-axis values. @default p => p.toFixed(2) */
  priceFmt?: (p: number) => string;
  /** Format for cumulative-size axis. @default 1.2k-style */
  sizeFmt?: (s: number) => string;
  /** @default 4 */
  sizeTicks?: number;
  /** @default 7 */
  priceTicks?: number;
}
export declare function DepthChart(props: DepthChartProps): JSX.Element;
