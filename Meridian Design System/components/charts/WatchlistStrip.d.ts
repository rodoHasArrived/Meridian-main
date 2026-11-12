import * as React from "react";

export interface WatchlistItem {
  id?: string;
  symbol: string;
  name?: string;
  last: number | null;
  change?: number | null;
  /** Percentage points; 1.25 renders as 1.25%. */
  changePct?: number | null;
  spark?: number[];
  dayRange?: [number, number];
}
export interface WatchlistStripProps {
  items?: WatchlistItem[];
  /** @default 1 */
  columns?: number;
  /** @default false */
  dense?: boolean;
  /** @default true */
  showSpark?: boolean;
  /** @default true */
  showRange?: boolean;
  priceFmt?: (value: number | null) => string;
  selectedId?: string | null;
  /** Receives item.id, falling back to item.symbol. Omit for a static list. */
  onSelect?: ((id: string) => void) | null;
  /** @default "Watchlist" */
  ariaLabel?: string;
}
export declare function WatchlistStrip(props: WatchlistStripProps): React.ReactElement;
