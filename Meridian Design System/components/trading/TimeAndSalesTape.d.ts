import React from "react";

/**
 * Market-wide print tape — every print on the instrument, newest first. `FillsFeed` covers
 * only the desk's OWN executions; this is the public tape beside it. Mono rows: time,
 * optional symbol, tick-direction-coloured price (derived per symbol from the prior print,
 * or an explicit `dir`), size with emphasis tiers, block prints washed and tagged, optional
 * venue. Quiet by design — colour and weight carry the signal, nothing animates.
 */
export interface TapePrint {
  /** Print time — ISO string, epoch ms, or Date (rendered through `Timestamp`). */
  time: string | number | Date;
  /** Instrument symbol — required when `showSymbol`, and used to derive tick direction per symbol. */
  symbol?: string;
  price: number | string;
  size: number | string;
  venue?: string;
  /** Explicit tick direction; wins over the derived value. */
  dir?: "up" | "down" | "flat";
}

export interface TimeAndSalesTapeProps {
  /** Prints, newest first. */
  prints: TapePrint[];
  /** Show the symbol column (multi-instrument tape). @default false */
  showSymbol?: boolean;
  /** Show the venue column. @default false */
  showVenue?: boolean;
  /** Price decimals. @default 2 */
  priceDecimals?: number;
  /** Ascending size thresholds → emphasis tiers. @default [1000, 5000] */
  sizeBuckets?: number[];
  /** Size at or above which a print is a block (wash + "block" tag). @default null */
  blockSize?: number | null;
  /** Scroll cap in px. @default 300 */
  maxHeight?: number | string;
  /** @default "No prints" */
  emptyLabel?: string;
  /** Locale for number formatting. */
  locale?: string;
}

export declare function TimeAndSalesTape(props: TimeAndSalesTapeProps): JSX.Element;
