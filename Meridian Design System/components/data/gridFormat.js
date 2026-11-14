// Numeric notation for DataGrid — analytical (signed) and accounting
// (parenthesized) negatives, per guidelines/CONTENT_FUNDAMENTALS.md § notation.
// Bundle-internal: reach it through the DataGrid carrier.
export function formatGridSigned(n, { style = "analytical", digits = 2, suffix = "" } = {}) {
  if (n === null || n === undefined || Number.isNaN(n)) return "\u2014";
  const abs = Math.abs(n).toLocaleString("en-US", { minimumFractionDigits: digits, maximumFractionDigits: digits });
  if (style === "accounting") return n < 0 ? `(${abs}${suffix})` : `${abs}${suffix}`;
  const sign = n < 0 ? "\u2212" : n > 0 ? "+" : "";
  return `${sign}${abs}${suffix}`;
}
