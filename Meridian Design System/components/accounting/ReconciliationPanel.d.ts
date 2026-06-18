/**
 * Reconciliation panel — a two-sided statement vs. ledger comparison rendered as proper
 * multi-column data tables (Date · Reference · Memo · [Category] · Amount). A shared toolbar
 * filters by match status (All / Matched / Open) and free-text searches across the visible
 * columns; every column header is click-to-sort (asc → desc → unsorted). Matched rows carry a
 * green rail, unmatched an amber rail and faint wash. A summary bar totals each side over the
 * FULL data set (not the filtered view) and flags the difference: "Reconciled" (green) within
 * tolerance, "Out by …" (red) otherwise.
 */
export interface ReconciliationItem {
  id?: string | number;
  date?: string;
  /** Document / transaction reference. */
  ref?: string;
  memo?: string;
  /** Optional classification — surfaces a "Category" column when any item sets it. */
  category?: string;
  amount: number | string;
  /** Whether this line has a counterpart on the other side. */
  matched?: boolean;
  /** Arbitrary extra fields are addressable by custom `columns`. */
  [key: string]: unknown;
}
export interface ReconciliationSide {
  /** Side label (e.g. "Statement", "Ledger"). Also used in the summary bar. */
  title: string;
  items: ReconciliationItem[];
}
export interface ReconciliationColumn {
  /** Item field this column reads. */
  key: string;
  /** Column header label. */
  label: string;
  /** Render in the tabular mono/data font (dates, refs). */
  mono?: boolean;
  /** Right-align the header + cells (numeric columns). */
  num?: boolean;
  /** Render the cell through `AmountCell` with accounting formatting. */
  amount?: boolean;
}
export interface ReconciliationPanelProps {
  left: ReconciliationSide;
  right: ReconciliationSide;
  /**
   * Column definitions, applied to both sides. Defaults to Date · Reference · Memo · Amount,
   * with a Category column inserted automatically when any item carries `category`.
   */
  columns?: ReconciliationColumn[];
  /** @default "USD" */
  currency?: string;
  /** Explicit statement-side balance. Defaults to the sum of `left.items`. */
  statementBalance?: number | string;
  /** Explicit book-side balance. Defaults to the sum of `right.items`. */
  bookBalance?: number | string;
  /** Absolute difference treated as reconciled. @default 0.005 */
  tolerance?: number;
  /** Show the free-text search box. @default true */
  searchable?: boolean;
  /** Show the All / Matched / Open status filter. @default true */
  filterable?: boolean;
}
export declare function ReconciliationPanel(props: ReconciliationPanelProps): JSX.Element;
