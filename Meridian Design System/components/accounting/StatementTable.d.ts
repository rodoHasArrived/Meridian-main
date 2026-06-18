/**
 * Financial-statement table — P&L, balance sheet, or cash-flow layout: grouped sections with
 * indented line items, per-section subtotals, and a strong double-ruled grand total. Supports
 * one or two amount columns for period comparison. Negatives render in accounting parentheses.
 *
 * Each row/subtotal/total carries either a single `value` (one-column statements) or a `values`
 * map keyed by your column keys (comparison statements).
 */
export interface StatementValue {
  label: string;
  /** Single amount (when there's one column). */
  value?: number | string;
  /** Per-column amounts, keyed by `columns[].key` (comparison statements). */
  values?: Record<string, number | string>;
  /** Indent level for nested line items. @default 0 */
  indent?: number;
  /** Render the label muted (informational sub-lines). */
  muted?: boolean;
}
export interface StatementSection {
  /** Small-caps section header (e.g. "Revenue", "Operating expenses"). Optional. */
  label?: string;
  rows: StatementValue[];
  /** Section subtotal row (top border, bold). */
  subtotal?: StatementValue;
}
export interface StatementTableProps {
  sections: StatementSection[];
  /** Grand-total row — double-ruled, bold. */
  total?: StatementValue;
  /** Amount columns. Omit for a single unlabeled column. */
  columns?: { key: string; label: string }[];
  /** @default "USD" */
  currency?: string;
  /** Negatives in parentheses. @default true */
  parens?: boolean;
  /** Color amounts as gains/losses (negative red / positive green). @default false */
  pnl?: boolean;
}
export declare function StatementTable(props: StatementTableProps): JSX.Element;
