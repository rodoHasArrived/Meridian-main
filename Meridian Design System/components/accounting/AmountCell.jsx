// Meridian money primitive — a single right-aligned mono amount. Encodes sign, currency,
// fixed or ISO-minor-unit decimals, accounting parentheses for negatives, zero-as-dash, an
// unsigned Dr/Cr side suffix, compact metric notation, and an optional P&L color tone
// (negative red / positive green). The atom every accounting table is built from.
//
// Value semantics: pass `envelope` (or an envelope-shaped object as `value`) and the formatted
// amount renders THROUGH ValueEnvelope — condition, authority, evidence, clocks and materiality
// travel with the number, per guidelines/VALUE_ENVELOPE.md. Formatting stays here; provenance
// stays there; no facet component is ever stacked beside the amount.
import React from "react";
import { formatMoney, formatMoneyCompact, toNumber, currencyDecimals } from "../core/format.js";
import { ValueEnvelope } from "../operations/ValueEnvelope";

const ENVELOPE_FACETS = ["condition", "detail", "authority", "evidence", "clocks", "materiality", "label"];
/** An envelope-shaped value: an object carrying `value` plus at least one facet. */
export function isEnvelopeValue(v) {
  return !!v && typeof v === "object" && !React.isValidElement(v)
    && "value" in v && ENVELOPE_FACETS.some((k) => k in v);
}
/** Split an envelope-shaped value into `{ value, envelope }` for AmountCell. */
export function splitEnvelopeValue(v) {
  if (!isEnvelopeValue(v)) return { value: v, envelope: undefined };
  const { value, ...envelope } = v;
  return { value, envelope };
}

export function AmountCell({
  value,
  envelope,              // envelope facets — renders the amount through ValueEnvelope
  align = "right",       // envelope mode only: "right" fills the numeric column
  currency,
  decimals = 2,          // number | "auto" → ISO minor units (JPY 0, BHD 3)
  mode = "plain",        // "plain" | "pnl" | "muted" | "signed" | "accounting"
  parens = false,        // negatives as (1,234.00)
  zeroDash = false,      // exact 0 → em dash
  signed = false,        // positives get a leading +
  drcr = false,          // unsigned magnitude + small-caps Dr/Cr suffix (debit-positive)
  compact = false,       // $12.4M metric notation; full value in the title tooltip
  locale,                // BCP-47 tag → CLDR digits/placement (1.234,56 €); overrides Money.setDefaultLocale
  strong = false,        // 600 weight (subtotals / totals)
  emphasis = false,      // metric emphasis — 600 weight at 1.25em
  as: Tag = "span",
  style = {},
  ...rest
}) {
  if (isEnvelopeValue(value)) {
    const split = splitEnvelopeValue(value);
    value = split.value;
    envelope = { ...split.envelope, ...envelope };
  }
  const num = toNumber(value);
  const dp = decimals === "auto" ? currencyDecimals(currency) : decimals;
  const useParens = parens || mode === "accounting";
  const useSigned = signed || mode === "signed";

  let text;
  let suffix = null;
  let title = rest.title;
  if (drcr && isFinite(num) && num !== 0) {
    text = formatMoney(Math.abs(num), { currency, decimals: dp, locale });
    suffix = num > 0 ? "Dr" : "Cr";
  } else if (compact) {
    text = formatMoneyCompact(value, { currency, signed: useSigned, locale });
    if (title == null && isFinite(num)) {
      title = formatMoney(value, { currency, decimals: dp, signed: useSigned, locale });
    }
  } else {
    text = formatMoney(value, { currency, decimals: dp, parens: useParens, zeroDash, signed: useSigned, locale });
  }

  let color = "var(--text-primary, #20252B)";
  if (mode === "muted") color = "var(--text-muted, #6B737B)";
  else if (mode === "pnl") {
    color = !isFinite(num) || num === 0
      ? "var(--text-muted, #6B737B)"
      : num < 0 ? "var(--red-dim, #7D322F)" : "var(--green-dim, #375D46)";
  }

  if (envelope) {
    // The envelope owns the provenance marks and hover disclosure; AmountCell owns the
    // number's shape. Dr/Cr folds into the text so the marks stay with the figure.
    return (
      <ValueEnvelope
        {...envelope}
        value={suffix ? `${text} ${suffix}` : text}
        align={align}
        style={{
          fontWeight: strong || emphasis ? 600 : 400,
          fontSize: emphasis ? "1.25em" : undefined,
          color,
          ...style,
        }}
        {...rest}
        title={title}
      />
    );
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
            color: "var(--text-muted, #6B737B)",
            marginLeft: 5,
          }}
        >
          {suffix}
        </span>
      )}
    </Tag>
  );
}
