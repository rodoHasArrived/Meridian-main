/**
 * DepthLadder — classic price ladder (DOM view): bidSize | price | askSize rows around a
 * spread row (absolute + bps). Depth renders as washed green/red horizontal bars behind the
 * size cells — never solid fills; mono numerals throughout. A small marker flags the ladder
 * row nearest `lastPrice`. With `onPriceClick` the price cells become buttons — wire it to
 * OrderTicket limit-price prefill. Complements DepthChart (the cumulative curve); use the
 * ladder when the operator needs per-level actionability, the chart for shape-at-a-glance.
 *
 * @example
 * <DepthLadder
 *   bids={[{ price: 201.10, size: 1200 }, { price: 201.09, size: 3400 }]}
 *   asks={[{ price: 201.12, size: 900 }, { price: 201.13, size: 2100 }]}
 *   lastPrice={201.11}
 *   onPriceClick={(price, side) => ticket.setLimit(price)}
 * />
 */
export interface DepthLevel {
  price: number;
  size: number;
}
export interface DepthLadderProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Best bid first (descending price). */
  bids: DepthLevel[];
  /** Best ask first (ascending price). */
  asks: DepthLevel[];
  /** Flags the nearest ladder row with a marker. */
  lastPrice?: number | null;
  /** @default 2 */
  priceDecimals?: number;
  /** Levels shown per side. @default 8 */
  levels?: number;
  /** Makes price cells buttons (ticket prefill). */
  onPriceClick?: (price: number, side: "bid" | "ask") => void;
}
export declare function DepthLadder(props: DepthLadderProps): JSX.Element;
