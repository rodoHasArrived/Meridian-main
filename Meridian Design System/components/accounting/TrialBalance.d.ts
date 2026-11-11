/**
 * Trial balance — one row per account with the net balance on its normal side, grouped into
 * Assets / Liabilities / Equity / Revenue / Expenses sections with per-section subtotals, and
 * a grand-total footer proving Σdebit = Σcredit (the difference cell turns red on mismatch).
 * Feed it raw postings through `Money.buildTrialBalance`, or pass rows directly. Rows carrying
 * a signed `balance` instead of debit/credit are placed by `type` normal side automatically.
 *
 * **Value semantics.** A row may carry `envelope` facets (or an envelope-shaped side value) — the
 * proof arithmetic reads the figure, `AmountCell` renders condition, authority, evidence, clocks
 * and materiality with the number. Law: `guidelines/VALUE_ENVELOPE.md`.
 */
import type { MeridianValueEnvelope } from "../operations/ValueEnvelope";
import type { EnvelopeValue } from "./AmountCell";
export interface TrialBalanceRow {
  /** Account code — column shown when any row has one. */
  code?: string;
  account: string;
  /** Account type ("asset", "liability", "equity", "revenue", "expense", contra-…) — drives grouping and normal side. */
  type?: string;
  debit?: number | string | EnvelopeValue;
  credit?: number | string | EnvelopeValue;
  /** Signed balance in normal-side terms — used when debit/credit are omitted; negatives flip sides. */
  balance?: number | string | EnvelopeValue;
  /** Envelope facets for this row's figure. */
  envelope?: Omit<MeridianValueEnvelope, "value">;
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
  /** Extra trailing cell per account row — e.g. an `AssuranceChip` (4.3 · O-4). Subtotals and totals leave it blank. */
  trailing?: (row: TrialBalanceRow, index: number) => React.ReactNode;
  /** @default "Assurance" */
  trailingLabel?: string;
}
export declare function TrialBalance(props: TrialBalanceProps): JSX.Element;
