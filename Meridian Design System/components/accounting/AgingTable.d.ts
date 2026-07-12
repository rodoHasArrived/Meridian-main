/**
 * AR / AP aging schedule — one row per counterparty with amounts spread across ascending
 * age buckets and a row total; the footer totals every bucket and shows each bucket's share
 * of the whole. Non-zero cells in late buckets wash amber, the last bucket washes red.
 * Bucket membership for raw invoices comes from `Money.agingBucketIndex`.
 */
export interface AgingRow {
  /** Counterparty / account name. */
  name: string;
  /** Reference (account code, invoice group) — column shown when any row has one. */
  ref?: string;
  /** One amount per bucket, aligned with `buckets`. Blanks render as dashes. */
  amounts: Array<number | string>;
}
export interface AgingTableProps {
  rows: AgingRow[];
  /** Bucket column labels. @default ["Current","1–30","31–60","61–90","90+"] */
  buckets?: string[];
  /** @default "USD" */
  currency?: string;
  /** Bucket index from which non-zero cells wash amber (last bucket always washes red). @default 2 */
  warnFrom?: number;
  /** Accessible caption / region label. */
  caption?: string;
  /** Row inspect handler `(row, index)`. */
  onRowClick?: (row: AgingRow, index: number) => void;
  /** Highlight one row. */
  selectedIndex?: number;
}
export declare function AgingTable(props: AgingTableProps): JSX.Element;
