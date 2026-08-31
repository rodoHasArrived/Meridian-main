/**
 * Trial balance — one row per account with the net balance on its normal side, grouped into
 * Assets / Liabilities / Equity / Revenue / Expenses sections with per-section subtotals, and
 * a grand-total footer proving Σdebit = Σcredit (the difference cell turns red on mismatch).
 * Feed it raw postings through `Money.buildTrialBalance`, or pass rows directly. Rows carrying
 * a signed `balance` instead of debit/credit are placed by `type` normal side automatically.
 */
export interface TrialBalanceRow {
  /** Account code — column shown when any row has one. */
  code?: string;
  account: string;
  /** Account type ("asset", "liability", "equity", "revenue", "expense", contra-…) — drives grouping and normal side. */
  type?: string;
  debit?: number | string;
  credit?: number | string;
  /** Signed balance in normal-side terms — used when debit/credit are omitted; negatives flip sides. */
  balance?: number | string;
}
export interface TrialBalanceProps {
  rows: TrialBalanceRow[];
  /** @default "USD" */
  currency?: string;
  /** Group into type sections with subtotals. @default true */
  grouped?: boolean;
  /** Accessible caption / region label. */
  caption?: string;
  /** Row inspect handler `(row, index)` — index is the position in `rows`. */
  onRowClick?: (row: TrialBalanceRow, index: number) => void;
  /** Highlight one row (original index). */
  selectedIndex?: number;
}
export declare function TrialBalance(props: TrialBalanceProps): JSX.Element;
