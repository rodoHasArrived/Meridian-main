// Meridian money primitive — a single right-aligned mono amount. Encodes sign, currency,
// fixed or ISO-minor-unit decimals, accounting parentheses for negatives, zero-as-dash, an
// unsigned Dr/Cr side suffix, compact metric notation, and an optional P&L color tone
// (negative red / positive green). The atom every accounting table is built from.
import React from "react";
import { formatMoney, formatMoneyCompact, toNumber, currencyDecimals } from "./money";

export function AmountCell({
  value,
  currency,
  decimals = 2,          // number | "auto" → ISO minor units (JPY 0, BHD 3)
  mode = "plain",        // "plain" | "pnl" | "muted" | "signed" | "accounting"
  parens = false,        // negatives as (1,234.00)
  zeroDash = false,      // exact 0 → em dash
  signed = false,        // positives get a leading +
  drcr = false,          // unsigned magnitude + small-caps Dr/Cr suffix (debit-positive)
  compact = false,       // $12.4M metric notation; full value in the title tooltip
  strong = false,        // 600 weight (subtotals / totals)
  emphasis = false,      // metric emphasis — 600 weight at 1.25em
  as: Tag = "span",
  style = {},
  ...rest
}) {
  const num = toNumber(value);
  const dp = decimals === "auto" ? currencyDecimals(currency) : decimals;
  const useParens = parens || mode === "accounting";
  const useSigned = signed || mode === "signed";

  let text;
  let suffix = null;
  let title = rest.title;
  if (drcr && isFinite(num) && num !== 0) {
    text = formatMoney(Math.abs(num), { currency, decimals: dp });
    suffix = num > 0 ? "Dr" : "Cr";
  } else if (compact) {
    text = formatMoneyCompact(value, { currency, signed: useSigned });
    if (title == null && isFinite(num)) {
      title = formatMoney(value, { currency, decimals: dp, signed: useSigned });
    }
  } else {
    text = formatMoney(value, { currency, decimals: dp, parens: useParens, zeroDash, signed: useSigned });
  }

  let color = "var(--text-primary, #22272E)";
  if (mode === "muted") color = "var(--text-muted, #59636F)";
  else if (mode === "pnl") {
    color = !isFinite(num) || num === 0
      ? "var(--text-muted, #59636F)"
      : num < 0 ? "var(--red-dim, #8C2F40)" : "var(--green-dim, #10663F)";
  }

  return (
    <Tag
      style={{
        fontFamily: "var(--font-data)",
        fontVariantNumeric: "tabular-nums",
        fontFeatureSettings: '"tnum" 1',
        fontWeight: strong || emphasis ? 600 : 400,
        fontSize: emphasis ? "1.25em" : undefined,
        whiteSpace: "nowrap",
        color,
        ...style,
      }}
      {...rest}
      title={title}
    >
      {text}
      {suffix && (
        <span
          style={{
            fontFamily: "var(--font-body)",
            fontSize: ".78em",
            fontWeight: 600,
            fontVariant: "all-small-caps",
            letterSpacing: ".03em",
            color: "var(--text-muted, #59636F)",
            marginLeft: 5,
          }}
        >
          {suffix}
        </span>
      )}
    </Tag>
  );
}
