// Meridian money primitive — a single right-aligned mono amount. Encodes sign, currency, fixed
// decimals, accounting parentheses for negatives, zero-as-dash, and an optional P&L color tone
// (negative red / positive green). The atom every accounting table is built from.
import type { CSSProperties } from "react";
import { createElement } from "react";
import { formatMoney, toNumber } from "./money";

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
  style?: CSSProperties;
  /** Extra attributes (className, title, aria-*) forwarded to the element. */
  [key: string]: unknown;
}

export function AmountCell({
  value,
  currency,
  decimals = 2,
  mode = "plain",
  parens = false,
  zeroDash = false,
  signed = false,
  strong = false,
  as = "span",
  style = {},
  ...rest
}: AmountCellProps) {
  const num = toNumber(value);
  const text = formatMoney(value, { currency, decimals, parens, zeroDash, signed });

  let color = "var(--text-primary, #22272E)";
  if (mode === "muted") {
    color = "var(--text-muted, #59636F)";
  } else if (mode === "pnl") {
    color =
      !Number.isFinite(num) || num === 0
        ? "var(--text-muted, #59636F)"
        : num < 0
          ? "var(--red-dim, #8C2F40)"
          : "var(--green-dim, #10663F)";
  }

  return createElement(
    as,
    {
      style: {
        fontFamily: "var(--font-data, monospace)",
        fontVariantNumeric: "tabular-nums",
        fontFeatureSettings: '"tnum" 1',
        fontWeight: strong ? 600 : 400,
        whiteSpace: "nowrap",
        color,
        ...style
      },
      ...rest
    },
    text
  );
}

AmountCell.displayName = "AmountCell";
