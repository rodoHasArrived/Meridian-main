/**
 * Blotter — orders blotter preset over `DenseDataTable`. Desk conventions encoded once:
 * side as washed green/red text (never fills), mono right-aligned qty/price, order status
 * through `SeverityBadge` normalization, UTC time-of-day via `Timestamp`.
 *
 * @example
 * <Blotter orders={[
 *   { id: "ORD-1204", time: t, symbol: "AAPL", side: "Buy", qty: "400", type: "Limit", limit: "201.0000", filled: "400", status: "Filled" },
 * ]} onRowClick={(o) => inspect(o)} />
 */
export interface BlotterOrder {
  id: string;
  /** Date, epoch ms, or ISO string. */
  time: Date | number | string;
  symbol: string;
  side: "Buy" | "Sell" | string;
  qty: React.ReactNode;
  type?: string;
  /** Limit price (mono, right-aligned); omit for market orders (renders "—"). */
  limit?: React.ReactNode;
  filled?: React.ReactNode;
  /** Filled · Partially filled · Working · Pending · Cancelled · Rejected (or any severity string). */
  status?: string;
  [key: string]: unknown;
}
export interface BlotterProps {
  orders?: BlotterOrder[];
  onRowClick?: (order: BlotterOrder, index: number) => void;
  selectedIndex?: number;
  sortKey?: string;
  sortDir?: "asc" | "desc";
  onSort?: (key: string) => void;
  /** Appended DenseDataTable column defs. */
  extraColumns?: { key: string; label: string; align?: "left" | "right"; render?: (row: BlotterOrder) => React.ReactNode }[];
}
export declare function Blotter(props: BlotterProps): JSX.Element;
