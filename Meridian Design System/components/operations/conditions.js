// Meridian data-condition vocabulary — the eleven conditions every analytical value
// can be in. Shared by DataCondition, tables, inspectors, and chart marks so a
// value's condition reads identically wherever it appears. Bundle-internal:
// reach it through the DataCondition carrier.
//
// 4.1: `restricted` is the entitlement condition — the reader is not permitted to
// see this figure. It exists because a blank cell must never stand for a condition:
// withheld is a stated condition, not an empty space. It is the value-layer sibling
// of EvidencePreview's document-level `redacted` / `restriction` / `permissions`.

const CONDITIONS = {
  confirmed:   { label: "Confirmed",      glyph: "●", note: "Observed and validated at source" },
  provisional: { label: "Provisional",    glyph: "◐", note: "Received but subject to revision" },
  estimated:   { label: "Estimated",      glyph: "○", note: "Model-derived, not observed", suffix: "Est." },
  stale:       { label: "Stale",          glyph: "◔", note: "Past its freshness policy" },
  overridden:  { label: "Overridden",     glyph: "◆", note: "Operator value in force over the source" },
  missing:     { label: "Missing",        glyph: "■", note: "Required and not supplied" },
  na:          { label: "Not applicable", glyph: "—", note: "Does not apply to this record" },
  pending:     { label: "Pending",        glyph: "◇", note: "Awaiting an upstream event" },
  exception:   { label: "Exception",      glyph: "▲", note: "Failed a control and needs resolution" },
  reconciled:  { label: "Reconciled",     glyph: "✓", note: "Agreed to an independent source" },
  restricted:  { label: "Restricted",     glyph: "▨", note: "Withheld — the reader is not entitled to this figure" },
};

const ALIASES = {
  complete: "confirmed", verified: "confirmed", matched: "reconciled", ok: "confirmed",
  live: "confirmed", preliminary: "provisional", unaudited: "provisional",
  modeled: "estimated", forecast: "estimated", projected: "estimated", scheduled: "pending",
  delayed: "stale", aged: "stale", override: "overridden", manual: "overridden",
  "missing required": "missing", absent: "missing", unavailable: "missing",
  "not applicable": "na", "n/a": "na", none: "na",
  awaiting: "pending", queued: "pending", unmatched: "exception", break: "exception",
  breaksdetected: "exception", failed: "exception", certified: "reconciled",
  withheld: "restricted", redacted: "restricted", confidential: "restricted",
  masked: "restricted", entitled: "restricted", "not entitled": "restricted",
  "permission denied": "restricted", "no access": "restricted",
};

/** Normalize any incoming status string to one of the eleven conditions. */
export function normalizeCondition(value) {
  if (!value) return "na";
  const k = String(value).trim().toLowerCase().replace(/[_-]+/g, " ");
  if (CONDITIONS[k]) return k;
  const squashed = k.replace(/\s+/g, "");
  if (CONDITIONS[squashed]) return squashed;
  return ALIASES[k] || ALIASES[squashed] || "na";
}

export function conditionMeta(value) {
  const key = normalizeCondition(value);
  return { key, ...CONDITIONS[key] };
}

/** The Meridian notation contract (§ financial notation).
 *  formatConditioned("4.35%", "estimated")      → "4.35% Est."
 *  formatConditioned("4.35%", "stale", "18m")   → "4.35% · Stale 18m"
 *  formatConditioned(null,    "missing")        → "Missing"
 *  formatConditioned(null,    "na")             → "—"
 *  formatConditioned(1234,    "restricted")     → "Restricted"        (never the value)
 *  formatConditioned(1234, "restricted", "Desk policy") → "Restricted · Desk policy"  */
export function formatConditioned(value, condition, detail) {
  const { key } = conditionMeta(condition);
  // Restricted is checked before every other branch and ignores `value` entirely:
  // a withheld figure must not be reachable by passing it in.
  if (key === "restricted") return detail ? `Restricted · ${detail}` : "Restricted";
  if (key === "missing") return "Missing";
  if (key === "na" || value === null || value === undefined || value === "") return "—";
  const v = String(value);
  if (key === "estimated") return `${v} Est.`;
  if (key === "stale") return detail ? `${v} · Stale ${detail}` : `${v} · Stale`;
  if (key === "overridden") return `${v}\u2009*`;
  return detail ? `${v} · ${detail}` : v;
}

/** Analytical vs accounting negative notation (§7.3). */
export function formatSigned(n, { style = "analytical", digits = 2, suffix = "" } = {}) {
  if (n === null || n === undefined || Number.isNaN(n)) return "—";
  const abs = Math.abs(n).toLocaleString("en-US", { minimumFractionDigits: digits, maximumFractionDigits: digits });
  if (style === "accounting") return n < 0 ? `(${abs}${suffix})` : `${abs}${suffix}`;
  const sign = n < 0 ? "\u2212" : n > 0 ? "+" : "";
  return `${sign}${abs}${suffix}`;
}

/** Basis points, always spaced and signed: "+12.45 bp". */
export function formatBp(n, digits = 0) {
  return formatSigned(n, { digits, suffix: " bp" });
}

// 4.0: the top-level CONDITIONS export is retired; DataCondition re-exposes this
// as its public home (`DataCondition.CONDITIONS`). Lowercase alias keeps it bundle-internal.
export const conditionsVocabulary = CONDITIONS;
