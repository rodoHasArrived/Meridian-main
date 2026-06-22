// Meridian money primitive — a single right-aligned mono amount. Encodes sign, currency,
// fixed decimals, accounting parentheses for negatives, zero-as-dash, and an optional P&L
// color tone (negative red / positive green). The atom every accounting table is built from.
import React from "react";
import { formatMoney, toNumber } from "./money";

export function AmountCell({
  value,
  currency,
  decimals = 2,
  mode = "plain",        // "plain" | "pnl" | "muted"
  parens = false,        // negatives as (1,234.00)
  zeroDash = false,      // exact 0 → em dash
  signed = false,        // positives get a leading +
  strong = false,        // 600 weight (subtotals / totals)
  as: Tag = "span",
  style = {},
  ...rest
}) {
  const num = toNumber(value);
  const text = formatMoney(value, { currency, decimals, parens, zeroDash, signed });

  let color = "var(--text-primary, #22272E)";
  if (mode === "muted") color = "var(--text-muted, #6E7781)";
  else if (mode === "pnl") {
    color = !isFinite(num) || num === 0
      ? "var(--text-muted, #6E7781)"
      : num < 0 ? "var(--red-dim, #983244)" : "var(--green-dim, #126C4D)";
  }

  return (
    <Tag
      style={{
        fontFamily: "var(--font-data)",
        fontVariantNumeric: "tabular-nums",
        fontFeatureSettings: '"tnum" 1',
        fontWeight: strong ? 600 : 400,
        whiteSpace: "nowrap",
        color,
        ...style,
      }}
      {...rest}
    >
      {text}
    </Tag>
  );
}
