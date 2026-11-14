// Meridian accounting — shared money helpers. No React, no DOM. Used by AmountCell and the
// ledger/statement primitives so every amount formats identically: mono tabular, fixed decimals,
// accounting parentheses for negatives, zero-as-dash, explicit signs on deltas.
//
// SCOPE: this module is a thin statement-convention layer over `formatMoney` in `@/lib/format`,
// which owns the one rounding, sign and grouping implementation (via `@/lib/exact-decimal`). It
// keeps the accounting defaults — two decimals, no symbol unless a currency is given, U+2212
// minus, the statement symbol table — and the pass-through of unparseable text that ledger cells
// rely on. A rounding or negative-display policy change belongs in `@/lib/format`.

import { formatMoney as formatSharedMoney } from "@/lib/format";
import { parseExactDecimal, type MoneyValue } from "@/lib/exact-decimal";

export { currencySymbol } from "@/lib/format";
export { parseExactDecimal, sumAmounts, toNumber, type ExactDecimal } from "@/lib/exact-decimal";

/** Options accepted by {@link formatMoney}. */
export interface FormatMoneyOptions {
  /** Currency code (USD, EUR, GBP, JPY, CHF…). Omit for a bare number. */
  currency?: string;
  /** Decimal places. @default 2 */
  decimals?: number;
  /** Render negatives as `(1,234.00)` instead of `−1,234.00` (statement convention). @default false */
  parens?: boolean;
  /** Exact zero renders as an em dash. @default false */
  zeroDash?: boolean;
  /** Positives get an explicit leading `+`. @default false */
  signed?: boolean;
}

/**
 * Format a money value the Meridian statement way — tabular, fixed decimals, optional currency
 * symbol. A decimal string is formatted from its digits, never through a float.
 *  - `parens`   → negatives as `(1,234.00)` instead of `−1,234.00` (statement convention)
 *  - `zeroDash` → exact zero renders as an em dash
 *  - `signed`   → positives get an explicit leading `+`
 *
 * Unparseable input passes through: `null`/`undefined` → `""`, everything else → its string form.
 */
export function formatMoney(value: MoneyValue, opts: FormatMoneyOptions = {}): string {
  if (!parseExactDecimal(value)) return value == null ? "" : String(value);
  const { currency, decimals = 2, parens = false, zeroDash = false, signed = false } = opts;
  return formatSharedMoney(value, {
    convention: "statement",
    currency: currency || null,
    decimals,
    parens,
    zeroDash,
    signed
  });
}
