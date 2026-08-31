/**
 * General-ledger / journal table — double-entry rows with Debit / Credit columns and a running
 * Balance, plus a totals footer proving Σdebit = Σcredit (the footer balance turns red when the
 * two sides disagree). Rows may carry a posting status — "pending" washes amber, "void" strikes
 * the row and excludes it from totals and the running balance. Headers sort in place unless an
 * `onSort` delegate is given; the running balance stays chronological regardless of sort.
 */
export interface LedgerRow {
  date: string;
  /** Document / journal reference (rendered in accent). */
  ref?: string;
  /** Posting description. */
  memo?: string;
  /** Account name/code — shown only when `showAccount` is set. */
  account?: string;
  /** Debit amount (number or money-ish string). Blank side renders as a dash. */
  debit?: number | string;
  /** Credit amount. */
  credit?: number | string;
  /** Explicit running balance. Omit to auto-compute from `opening` + normal-side deltas. */
  balance?: number | string;
  /** Posting status. "pending" = amber wash + chip; "void" = struck through, excluded from math. @default "posted" */
  status?: "posted" | "pending" | "void";
}
export interface LedgerTableProps {
  rows: LedgerRow[];
  /** @default "USD" */
  currency?: string;
  /** Opening balance — renders a leading muted row and seeds the running balance. */
  opening?: number | string;
  /** Alias of `opening`. */
  openingBalance?: number | string;
  /** Show an Account column (journal spanning multiple accounts). @default false */
  showAccount?: boolean;
  /** Which side increases the running balance. @default "debit" */
  normalSide?: "debit" | "credit";
  /** Accessible caption / region label. */
  caption?: string;
  /** External sort delegate `(key, dir)`. Omit and the table sorts its own rows. */
  onSort?: (key: string, dir: 1 | -1) => void;
  /** Row inspect handler `(row, index)` — index is the original position in `rows`. */
  onRowClick?: (row: LedgerRow, index: number) => void;
  /** Highlight one row (original index) — 3px accent inset, active wash. */
  selectedIndex?: number;
}
export declare function LedgerTable(props: LedgerTableProps): JSX.Element;
