/**
 * Money primitive — a single right-aligned mono amount. Encodes sign, currency, fixed or
 * ISO-minor-unit decimals, accounting parentheses, zero-as-dash, an unsigned Dr/Cr side
 * suffix, compact metric notation, and a P&L color tone. The atom every accounting table
 * is built from — never hand-format currency next to it.
 */
export interface AmountCellProps {
  value: number | string;
  /** ISO code ("USD", "JPY") or a literal symbol ("$", "€"). */
  currency?: string;
  /** Fraction digits, or "auto" for the currency's ISO minor units (JPY 0, BHD 3). @default 2 */
  decimals?: number | "auto";
  /**
   * "pnl" colors negatives red / positives green; "muted" grays the figure;
   * "signed" forces an explicit leading +; "accounting" wraps negatives in parentheses.
   * @default "plain"
   */
  mode?: "plain" | "pnl" | "muted" | "signed" | "accounting";
  /** Negatives as (1,234.00) — statement convention. @default false */
  parens?: boolean;
  /** Exact 0 renders as an em dash. @default false */
  zeroDash?: boolean;
  /** Positives get an explicit leading +. @default false */
  signed?: boolean;
  /** Unsigned magnitude + small-caps Dr/Cr suffix (positive = Dr, negative = Cr). @default false */
  drcr?: boolean;
  /** Compact metric notation — $12.4M — with the full value as a title tooltip. Metric surfaces only. @default false */
  compact?: boolean;
  /** 600 weight — subtotals / totals. @default false */
  strong?: boolean;
  /** Metric emphasis — 600 weight at 1.25em. @default false */
  emphasis?: boolean;
  /** Wrapper element. @default "span" */
  as?: keyof JSX.IntrinsicElements;
  style?: React.CSSProperties;
}
export declare function AmountCell(props: AmountCellProps): JSX.Element;
