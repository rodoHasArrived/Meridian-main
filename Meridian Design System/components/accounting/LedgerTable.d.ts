/**
 * General-ledger / journal table — double-entry rows with Debit / Credit columns and a running
 * Balance, plus a totals footer proving Σdebit = Σcredit (the footer balance turns red when the
 * two sides disagree). Light institutional grid; all figures mono and tabular.
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
}
export interface LedgerTableProps {
  rows: LedgerRow[];
  /** @default "USD" */
  currency?: string;
  /** Opening balance — renders a leading muted row and seeds the running balance. */
  opening?: number | string;
  /** Show an Account column (journal spanning multiple accounts). @default false */
  showAccount?: boolean;
  /** Which side increases the running balance. @default "debit" */
  normalSide?: "debit" | "credit";
  /** Accessible caption / region label. */
  caption?: string;
}
export declare function LedgerTable(props: LedgerTableProps): JSX.Element;
