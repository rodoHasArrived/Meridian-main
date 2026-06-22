/**
 * Tax-lot table — cost-basis lots with holding-period classification and unrealized (or realized)
 * P&L. Each lot shows acquisition date + days held, quantity, cost basis, market value (or
 * proceeds), and gain/loss with a percent, plus a short/long-term badge derived from days held vs.
 * `longTermDays` (amber short-term / teal-blue long-term). The footer rolls up basis, value, and
 * total gain. All figures mono and tabular; P&L is red/green.
 */
export interface TaxLot {
  id?: string | number;
  /** Acquisition date (ISO "2025-02-14"). Drives days-held and the holding-period badge. */
  acquired: string;
  /** Instrument symbol — shown only when `showSymbol` is set. */
  symbol?: string;
  quantity: number | string;
  costBasis: number | string;
  /** Current market value — used when mode = "unrealized". */
  marketValue?: number | string;
  /** Sale proceeds — used when mode = "realized". */
  proceeds?: number | string;
  /** Override computed holding days (e.g. server-supplied). */
  daysHeld?: number;
  /** Force the holding-period classification instead of deriving it. */
  term?: "short" | "long";
}
export interface TaxLotTableProps {
  lots: TaxLot[];
  /** @default "USD" */
  currency?: string;
  /** ISO date the holding period and market value are measured at. @default now */
  asOf?: string;
  /** Days held at or above which a lot is long-term. @default 365 */
  longTermDays?: number;
  /** "unrealized" uses marketValue; "realized" uses proceeds. @default "unrealized" */
  mode?: "unrealized" | "realized";
  /** Include a Symbol column (cross-instrument lot list). @default false */
  showSymbol?: boolean;
  /** Accessible caption / region label. */
  caption?: string;
}
export declare function TaxLotTable(props: TaxLotTableProps): JSX.Element;
