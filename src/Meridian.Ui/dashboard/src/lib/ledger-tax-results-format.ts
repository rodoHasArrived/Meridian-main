/** JSON numbers have already lost precision; never coerce them back into retained evidence. */
export function isTaxDecimalText(value: unknown): value is string {
  return typeof value === "string" && /^-?(?:0|[1-9]\d*)(?:\.\d+)?$/.test(value);
}

/** Group digit strings directly; retain every fractional digit without floating-point conversion. */
export function formatTaxDecimal(value: string, minimumFractionDigits = 0): string {
  if (!isTaxDecimalText(value)) return "Invalid decimal evidence";
  const negative = value.startsWith("-");
  const [integer, fraction = ""] = (negative ? value.slice(1) : value).split(".");
  const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  const displayedFraction = fraction.padEnd(minimumFractionDigits, "0");
  const sign = negative && !/^0(?:\.0+)?$/.test(value.slice(1)) ? "-" : "";
  return `${sign}${grouped}${displayedFraction ? `.${displayedFraction}` : ""}`;
}

export function formatTaxAmount(value: string | null, currency: string): string {
  if (value === null) return "Missing evidence";
  return `${formatTaxDecimal(value, 2)}${currency ? ` ${currency}` : ""}`;
}
