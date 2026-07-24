/**
 * FillsFeed — streaming execution tape, newest first. Mono rows: time-of-day, symbol, washed
 * side, qty @ price. Deliberately quiet — no per-row animation. Prepend new fills to the array.
 *
 * @example
 * <FillsFeed maxHeight={300} fills={[
 *   { id: "F-88", time: t, symbol: "AAPL", side: "Buy", qty: "100", price: "201.1200" },
 * ]} />
 */
export interface Fill {
  id?: string;
  time: Date | number | string;
  symbol: string;
  side: "Buy" | "Sell" | string;
  qty: React.ReactNode;
  price: React.ReactNode;
}
export interface FillsFeedProps {
  /** Newest first. */
  fills?: Fill[];
  /** Scroll cap, px. @default 260 */
  maxHeight?: number | string;
  emptyLabel?: React.ReactNode;
}
export declare function FillsFeed(props: FillsFeedProps): JSX.Element;
