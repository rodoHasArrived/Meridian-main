import { formatExactMagnitude, parseExactDecimal as parseSharedDecimal } from "@/lib/exact-decimal";

/** Strict retained-value adapter over the workstation's shared exact-decimal engine. */
export interface ExactDecimal { coefficient: bigint; scale: number }

/** Compatibility for existing display-valued reads; never supplies a currency code or proof. */
export function readLegacyFinancialText(value: string): { value: string | null; unit?: string } {
  let text = value.trim().replace(/−/g, "-");
  const negative = /^\(.*\)$/.test(text);
  if (negative) text = text.slice(1, -1);
  const match = /^([+-]?)([$€£¥]?)(\d{1,3}(?:,\d{3})+|\d+)(\.\d+)?$/.exec(text);
  if (!match) return { value: null };
  return { value: `${negative ? "-" : match[1]}${match[3].replace(/,/g, "")}${match[4] ?? ""}`, unit: match[2] || undefined };
}

/** Mirrors Meridian.Contracts.Ledger.CurrencyMinorUnits. */
export function currencyMinorUnits(currency?: string): number {
  const code = currency?.toUpperCase();
  if (["CLF", "UYW"].includes(code ?? "")) return 4;
  if (["BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"].includes(code ?? "")) return 3;
  if (["BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "UYI", "VND", "VUV", "XAF", "XOF", "XPF"].includes(code ?? "")) return 0;
  return 2;
}

export function parseExactDecimal(value: string | number | null | undefined): ExactDecimal | null {
  if (value == null || (typeof value === "number" && !Number.isFinite(value))) return null;
  const text = String(value).trim().replace(/−/g, "-");
  // Exponents occur in legacy number-valued responses; bound both size and exponent.
  if (text.length > 512) return null;
  const match = /^([+-]?)(\d+)(?:\.(\d*))?(?:[eE]([+-]?\d{1,3}))?$/.exec(text);
  if (!match) return null;
  const parsed = parseSharedDecimal(text);
  return parsed ? { coefficient: parsed.negative ? -parsed.units : parsed.units, scale: parsed.scale } : null;
}

export function exactDecimalText(decimal: ExactDecimal): string {
  const negative = decimal.coefficient < 0n;
  const digits = (negative ? -decimal.coefficient : decimal.coefficient).toString().padStart(decimal.scale + 1, "0");
  return `${negative ? "-" : ""}${decimal.scale ? `${digits.slice(0, -decimal.scale)}.${digits.slice(-decimal.scale)}` : digits}`;
}

export function equalExactDecimals(left: string | number | null | undefined, right: string | number | null | undefined): boolean {
  const a = parseExactDecimal(left);
  const b = parseExactDecimal(right);
  if (!a || !b) return false;
  const scale = Math.max(a.scale, b.scale);
  return a.coefficient * 10n ** BigInt(scale - a.scale) === b.coefficient * 10n ** BigInt(scale - b.scale);
}

export function sumExactDecimals(values: readonly (string | number)[]): string | null {
  const parsed = values.map(parseExactDecimal);
  if (parsed.some((value) => !value)) return null;
  const decimals = parsed as ExactDecimal[];
  const scale = decimals.reduce((maximum, value) => Math.max(maximum, value.scale), 0);
  return exactDecimalText({ scale, coefficient: decimals.reduce((sum, value) => sum + value.coefficient * 10n ** BigInt(scale - value.scale), 0n) });
}

/** Subtract source operands without a binary-number conversion or a missing-to-zero coercion. */
export function subtractExactDecimals(left: string | number | null | undefined, right: string | number | null | undefined): string | null {
  const a = parseExactDecimal(left);
  const b = parseExactDecimal(right);
  if (!a || !b) return null;
  const scale = Math.max(a.scale, b.scale);
  return exactDecimalText({ scale, coefficient: a.coefficient * 10n ** BigInt(scale - a.scale) - b.coefficient * 10n ** BigInt(scale - b.scale) });
}

/** Presentation rounding is half away from zero. Source precision remains unchanged. */
export function formatExactDecimal(value: string | number | null | undefined, options: {
  currency?: string; decimals?: number; signed?: boolean; parens?: boolean;
} = {}): string | null {
  const parsed = parseExactDecimal(value);
  if (!parsed) return null;
  const decimals = options.decimals ?? currencyMinorUnits(options.currency);
  if (!Number.isInteger(decimals) || decimals < 0 || decimals > 28) return null;
  const negative = parsed.coefficient < 0n;
  const magnitude = formatExactMagnitude({ negative, units: negative ? -parsed.coefficient : parsed.coefficient, scale: parsed.scale }, decimals);
  if (negative && !magnitude.roundsToZero && options.parens) return `(${magnitude.text})`;
  return `${negative && !magnitude.roundsToZero ? "−" : options.signed && !magnitude.roundsToZero ? "+" : ""}${magnitude.text}`;
}
