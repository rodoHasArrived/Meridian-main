/**
 * OrderTicket — order-entry primitive. Side toggle (washed green/red, never solid), qty,
 * market/limit, TIF, live notional estimate, and a hard confirm gate when
 * `environment="live"`: the submit button stays disabled until the operator checks the
 * real-capital acknowledgement for that exact draft. Editing any submitted field,
 * identity/environment or prefill context revokes it. Accepts complete positive
 * decimal entries; malformed numeric prefixes, exponent notation and non-finite
 * values stay invalid, including after blur. Owns its draft state and clears after
 * submit. Server-side validation, authorization, risk limits and idempotency remain
 * the consuming application's responsibility.
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
  notional: number | null;
}
/** Seed for the draft — applied once per distinct `token` (a ladder click, an amend). */
export interface OrderTicketPrefill {
  side?: "buy" | "sell";
  qty?: number | string;
  type?: "market" | "limit";
  limitPrice?: number | string | null;
  tif?: string;
  /** Changing this re-seeds the draft; equal tokens leave the operator's typing alone. */
  token?: string | number;
}
/**
 * 
 */
export interface OrderTicketProps {
  symbol?: string;
  /** Drives the header readout and market-order notional estimate. */
  lastPrice?: number | null;
  /** live/paper/fixture enable submit; unrecognized environments fail closed. @default "paper" */
  environment?: "live" | "paper" | "fixture" | string;
  /** @default "buy" */
  defaultSide?: "buy" | "sell";
  /** @default ["DAY","GTC","IOC","FOK"] */
  tifOptions?: string[];
  /** Tick precision (integer 0–20) for valid limit entries; invalid configuration uses 2. @default 2 */
  priceDecimals?: number;
  /** Seeds the draft from outside (ladder price click, amend of a working order). */
  prefill?: OrderTicketPrefill | null;
  /** Pre-trade checks rendered as an `ActionGuardList`; any `blocking` guard disables submit. */
  guards?: import("../operations/ActionGuard").ActionGuardValue[];
  /** Fired on every draft edit — wire a risk panel or notional readout to the live draft. */
  onDraftChange?: (draft: { symbol: string; side: string; qty: number | null; type: string; limitPrice: number | null; tif: string; notional: number | null; valid: boolean }) => void;
  /** Fired with the composed order when submit is pressed and valid. The draft clears after. */
  onSubmit?: (order: OrderTicketOrder) => void;
}
export declare function OrderTicket(props: OrderTicketProps): JSX.Element;
