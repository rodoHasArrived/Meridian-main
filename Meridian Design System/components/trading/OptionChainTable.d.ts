/**
 * OptionChainTable — trading-grade option chain in the classic straddle layout: call
 * columns | strike ladder | put columns (mirrored order, bid/ask adjacent to the strike).
 * ITM cells carry the accent alpha-10 wash (calls below spot, puts above); a spot marker
 * row shows where the underlying trades; greeks/IV/OI columns are configurable. The chain
 * is one tab stop — ↑/↓ move the focused strike, Home/End jump, Enter/Space selects.
 */
import * as React from "react";

export type OptionColumnKey =
  | "bid" | "ask" | "last" | "change"
  | "delta" | "gamma" | "theta" | "vega"
  | "iv" | "oi" | "volume";

export interface OptionQuote {
  bid?: number;
  ask?: number;
  last?: number;
  /** Signed change — renders green/red dim with explicit sign. */
  change?: number;
  delta?: number;
  gamma?: number;
  theta?: number;
  vega?: number;
  /** Implied vol in percent, e.g. 24.2. */
  iv?: number;
  oi?: number;
  volume?: number;
}

export interface OptionChainRow {
  strike: number;
  call?: OptionQuote;
  put?: OptionQuote;
}

export interface OptionChainTableProps {
  /** Chain rows (any order — sorted ascending by strike internally). */
  rows: OptionChainRow[];
  /** Underlying price — drives the ITM wash and the spot marker row. */
  underlyingPrice?: number | null;
  /** Label on the spot marker row. @default "underlying" */
  underlyingLabel?: string;
  /** Column keys per side, call-side order (puts mirror automatically).
   *  @default ["oi","volume","iv","delta","bid","ask"] */
  columns?: OptionColumnKey[];
  /** Decimals for prices (bid/ask/last/change and the spot label). @default 2 */
  priceDecimals?: number;
  /** Decimals for the strike column. @default 2 */
  strikeDecimals?: number;
  /** Render the spot marker row between the strikes bracketing the underlying. @default true */
  spotRow?: boolean;
  /** Makes rows selectable (click / Enter / Space) and enables keyboard navigation. */
  onSelect?: (row: OptionChainRow) => void;
  /** Controlled selected strike; use defaultSelectedStrike for uncontrolled. */
  selectedStrike?: number;
  defaultSelectedStrike?: number | null;
  className?: string;
}
export declare function OptionChainTable(props: OptionChainTableProps): JSX.Element;
