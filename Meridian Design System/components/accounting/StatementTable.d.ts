import * as React from "react";
/**
 * Financial-statement table — P&L, balance sheet, or cash-flow layout: grouped sections with
 * indented line items, per-section subtotals, and a strong double-ruled grand total. Supports
 * one or two amount columns for period comparison. Negatives render in accounting parentheses.
 *
 * Each row/subtotal/total carries either a single `value` (one-column statements) or a `values`
 * map keyed by your column keys (comparison statements).
 *
 * **Value semantics.** Any amount may instead be an envelope-shaped object —
 * `{ value, condition, authority, evidence, clocks, materiality }` — and renders through
 * `ValueEnvelope` with the facets on the figure. Law: `guidelines/VALUE_ENVELOPE.md`.
 */
import type { EnvelopeValue } from "./AmountCell";

export interface StatementValue {
  label: string;
  /** Single amount (when there's one column). */
  value?: number | string | EnvelopeValue;
  /** Per-column amounts, keyed by `columns[].key` (comparison statements). */
  values?: Record<string, number | string | EnvelopeValue>;
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
  /** Accessible caption / region label. */
  caption?: string;
}
export declare function StatementTable(props: StatementTableProps): React.JSX.Element;
