/**
 * OrderTicket — order-entry primitive. Side toggle (washed green/red, never solid), qty,
 * market/limit, TIF, live notional estimate, and a hard confirm gate when
 * `environment="live"`: the submit button stays disabled until the operator checks the
 * real-capital acknowledgement. Owns its own draft state; emits the composed order once.
 *
 * @example
 * <OrderTicket symbol="AAPL" lastPrice={201.12} environment="live"
 *   onSubmit={(o) => api.placeOrder(o)} />
 * // o: { symbol, side: "buy"|"sell", qty, type: "market"|"limit", limitPrice, tif, environment }
 */
export interface OrderTicketOrder {
  symbol: string;
  side: "buy" | "sell";
  qty: number;
  type: "market" | "limit";
  limitPrice: number | null;
  tif: string;
  environment: string;
}
export interface OrderTicketProps {
  symbol?: string;
  /** Drives the header readout and market-order notional estimate. */
  lastPrice?: number | null;
  /** "live" renders the red confirm gate; "paper"/"fixture" badge otherwise. @default "paper" */
  environment?: "live" | "paper" | "fixture" | string;
  /** @default "buy" */
  defaultSide?: "buy" | "sell";
  /** @default ["DAY","GTC","IOC","FOK"] */
  tifOptions?: string[];
  /** Fired with the composed order when submit is pressed and valid. */
  onSubmit?: (order: OrderTicketOrder) => void;
}
export declare function OrderTicket(props: OrderTicketProps): JSX.Element;
