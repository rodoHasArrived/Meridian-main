/**
 * Money primitive — one right-aligned mono amount with consistent Meridian formatting:
 * fixed decimals, tabular figures, optional currency symbol, accounting parentheses for
 * negatives, zero-as-dash, and a P&L color tone. Every accounting table cell is one of these.
 */
export interface AmountCellProps {
  /** Number, or a money-ish string ("(1,234.00)", "-921.00"). */
  value: number | string;
  /** Currency code (USD, EUR, GBP, JPY, CHF…). Omit for a bare number. */
  currency?: string;
  /** Decimal places. @default 2 */
  decimals?: number;
  /**
   * Color tone:
   *  - "plain"  primary text (balances, neutral amounts) — default
   *  - "pnl"    negative red / positive green / zero muted (deltas, realized gains)
   *  - "muted"  muted text (secondary / opening rows)
   * @default "plain"
   */
  mode?: "plain" | "pnl" | "muted";
  /** Render negatives as (1,234.00) instead of −1,234.00 (statement convention). @default false */
  parens?: boolean;
  /** Exact zero renders as an em dash. @default false */
  zeroDash?: boolean;
  /** Positives get an explicit leading +. @default false */
  signed?: boolean;
  /** 600 weight for subtotal / total rows. @default false */
  strong?: boolean;
  /** Element tag to render. @default "span" */
  as?: keyof JSX.IntrinsicElements;
  style?: React.CSSProperties;
}
export declare function AmountCell(props: AmountCellProps): JSX.Element;
