import { formatTaxDecimal, isTaxDecimalText } from "@/lib/ledger-tax-results-format";
import type { BondDecimal } from "@/types/bond-analytics.types";

/**
 * Exact display of a server decimal. Strings keep every retained digit (digit-grouped, never
 * parsed into a float); JSON numbers have already lost precision and are shown as received.
 */
export function bondDecimalText(value: BondDecimal | null | undefined, fallback = "Unavailable"): string {
  if (value == null) return fallback;
  if (typeof value === "number") return Number.isFinite(value) ? String(value) : fallback;
  return isTaxDecimalText(value) ? formatTaxDecimal(value) : value;
}

/** Accepts a plain decimal literal such as 99.5, .5, -0.25 or 100; rejects exponents and blanks. */
export const DECIMAL_LITERAL = /^-?(?:\d+(?:\.\d*)?|\.\d+)$/;

/** A datetime-local value with or without seconds, as an explicit UTC instant. */
export function utcInstantFromLocalInput(value: string): string {
  return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(value) ? `${value}:00Z` : `${value}Z`;
}
