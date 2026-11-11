// Meridian numeric authority — the ONE place a number becomes text.
//
// The rule, and it is the same shape as localDate.js's rule for calendar dates: no module outside
// this file constructs an `Intl` number formatter. A locale, a currency, a negative style and a
// decimal count are properties of the *surface*, not of the call site — ten modules that each wrote
// `new Intl.NumberFormat("en-US", …)` were ten surfaces that could not be told otherwise, and two
// separate 4.1 defects were decimals fixed by hand because precision had no owner.
//
// Locale/currency/negative style come from `setNumberDefaults` (a `FormatProvider` at the shell
// calls it) or from an explicit `locale`/`currency` opt for the one table that must differ.
// Enforced by `tests/conformance.html` rule C-01; law in `guidelines/NUMERIC_AUTHORITY.md`.
//
// No React, no DOM. Reach it from a consumer as `MeridianDesignSystem_205d17.Format`.

const SYMBOLS = { USD: "$", EUR: "\u20AC", GBP: "\u00A3", JPY: "\u00A5", CNY: "\u00A5", CHF: "CHF\u202F", CAD: "$", AUD: "$" };

// ISO-4217 minor units where they differ from 2.
const MINOR_UNITS = { JPY: 0, KRW: 0, VND: 0, CLP: 0, ISK: 0, BHD: 3, KWD: 3, OMR: 3, TND: 3 };

const MINUS = "\u2212"; // U+2212, never a hyphen — house style for negative figures
const DASH = "\u2014";

const BASE = { locale: "en-US", currency: "USD", negative: "minus", numerals: null, priceDecimals: 4, quantityDecimals: 4 };
let DEFAULTS = { ...BASE };

/**
 * Set the system-wide numeric defaults. `FormatProvider` calls this so module-level formatters
 * (the ones that cannot hold a React context) follow the same surface.
 * @param {{locale?:string,currency?:string,negative?:"minus"|"parens",numerals?:string,priceDecimals?:number,quantityDecimals?:number}} next
 */
export function setNumberDefaults(next = {}) {
  DEFAULTS = { ...DEFAULTS, ...Object.fromEntries(Object.entries(next).filter(([, v]) => v !== undefined && v !== null)) };
  return DEFAULTS;
}

/** Reset to the shipped defaults (en-US · USD · minus). Used by tests. */
export function resetNumberDefaults() { DEFAULTS = { ...BASE }; return DEFAULTS; }

/** The active numeric defaults. */
export function getNumberDefaults() { return { ...DEFAULTS }; }

// The only Intl number formatter constructor in the system. Memoized: a blotter formats 50k cells
// and constructing a formatter per cell is the measurable cost, not the formatting.
const CACHE = new Map();
export function numberFormatter(locale, opts = {}) {
  const loc = locale || DEFAULTS.locale;
  const tag = DEFAULTS.numerals ? `${loc}-u-nu-${DEFAULTS.numerals}` : loc;
  const key = tag + "\u0000" + JSON.stringify(opts);
  let f = CACHE.get(key);
  if (!f) {
    try { f = new Intl.NumberFormat(tag, opts); }
    catch (e) { f = new Intl.NumberFormat("en-US", opts); }
    CACHE.set(key, f);
  }
  return f;
}

/** Coerce a value (number or money-ish string like "(1,234.00)") to a Number. NaN if unparseable. */
export function toNumber(value) {
  if (value == null || value === "") return NaN;
  if (typeof value === "number") return value;
  const s = String(value).trim();
  const neg = /^\(.*\)$/.test(s); // accounting parentheses = negative
  // U+2212 is the minus sign formatMoney emits — read it back as a hyphen-minus before stripping.
  const n = parseFloat(s.replace(/\u2212/g, "-").replace(/[(),\s]/g, "").replace(/[^0-9.\-]/g, ""));
  return neg ? -Math.abs(n) : n;
}

/** Currency code → leading symbol. Literal symbols ("$", "€") pass through; unknown ISO codes render as "CODE " prefix. */
export function currencySymbol(code) {
  if (!code) return "";
  const s = String(code);
  if (SYMBOLS[s]) return SYMBOLS[s];
  if (!/^[A-Za-z]{3}$/.test(s)) return s; // already a symbol, not a code
  return s + "\u202F";
}

/** ISO-4217 minor units for a currency code — JPY → 0, BHD → 3, default 2. */
export function currencyDecimals(code) {
  const c = String(code || "").toUpperCase();
  return MINOR_UNITS[c] != null ? MINOR_UNITS[c] : 2;
}

/** Decimals implied by a tick size — 0.01 → 2, 0.0001 → 4, 0.5 → 1. Precision from the instrument, not the call site. */
export function decimalsForTick(tick) {
  const t = Math.abs(toNumber(tick));
  if (!isFinite(t) || t === 0) return DEFAULTS.priceDecimals;
  const s = t.toExponential().split("e");
  const exp = Number(s[1]);
  const mantissaDigits = (s[0].split(".")[1] || "").length;
  return Math.max(0, Math.min(10, mantissaDigits - exp));
}

function sign(n, signed) { return n < 0 ? MINUS : signed && n > 0 ? "+" : ""; }
function wantParens(opts) { return opts.parens === true || (opts.parens !== false && DEFAULTS.negative === "parens"); }

/**
 * A plain number — grouped thousands, fixed or bounded decimals, optional explicit sign/suffix.
 * @param {number|string} value
 * @param {{locale?:string,decimals?:number,minDecimals?:number,maxDecimals?:number,grouping?:boolean,signed?:boolean,parens?:boolean,suffix?:string,zeroDash?:boolean}} [opts]
 */
export function formatNumber(value, opts = {}) {
  const { locale, decimals, minDecimals, maxDecimals, grouping = true, signed = false, suffix = "", zeroDash = false } = opts;
  const n = toNumber(value);
  if (!isFinite(n)) return value == null ? "" : String(value);
  if (zeroDash && n === 0) return DASH;
  const body = numberFormatter(locale, {
    minimumFractionDigits: minDecimals ?? decimals ?? 0,
    maximumFractionDigits: maxDecimals ?? decimals ?? 3,
    useGrouping: grouping,
  }).format(Math.abs(n)) + suffix;
  if (n < 0 && wantParens(opts)) return `(${body})`;
  return sign(n, signed) + body;
}

/** Negatives in parentheses — the statement convention. Shorthand for formatNumber(v,{parens:true}). */
export function formatAccounting(value, opts = {}) {
  return formatNumber(value, { decimals: 2, ...opts, parens: true });
}

// Unsigned magnitude + currency in locale conventions. ISO codes go through Intl's currency style
// (correct symbol placement per locale); symbols/unknown codes fall back to a prefix.
function moneyBody(abs, currency, min, max, locale) {
  const loc = locale || DEFAULTS.locale;
  const digits = { minimumFractionDigits: min, maximumFractionDigits: max };
  if (loc === "en-US") {
    // Byte-identical to the pre-authority output: house prefix symbol + grouped magnitude.
    return currencySymbol(currency) + numberFormatter("en-US", digits).format(abs);
  }
  if (currency && /^[A-Za-z]{3}$/.test(String(currency))) {
    const f = numberFormatter(loc, { style: "currency", currency: String(currency).toUpperCase(), ...digits });
    if (f.resolvedOptions().style === "currency") return f.format(abs);
  }
  const mag = numberFormatter(loc, digits).format(abs);
  return currency ? currencySymbol(currency) + mag : mag;
}

/**
 * Format a money value the Meridian way.
 * @param {number|string} value
 * @param {{currency?:string,decimals?:number,minDecimals?:number,maxDecimals?:number,parens?:boolean,zeroDash?:boolean,signed?:boolean,locale?:string}} [opts]
 *  - parens   → negatives as (1,234.00); also the default when `negative:"parens"` is set
 *  - zeroDash → exact zero renders as an em dash
 *  - signed   → positives get an explicit leading +
 */
export function formatMoney(value, opts = {}) {
  const { currency = DEFAULTS.currency, decimals = 2, minDecimals, maxDecimals, zeroDash = false, signed = false, locale } = opts;
  const n = toNumber(value);
  if (!isFinite(n)) return value == null ? "" : String(value);
  if (zeroDash && n === 0) return DASH;
  const body = moneyBody(Math.abs(n), currency, minDecimals ?? decimals, maxDecimals ?? decimals, locale);
  if (n < 0 && wantParens(opts)) return `(${body})`;
  return sign(n, signed) + body;
}

/** Compact money for metric surfaces — $12.4M, −€3.2K. Prefix-symbol metric notation in every locale (house style). Never inside a ledger column. */
export function formatMoneyCompact(value, opts = {}) {
  const { currency = DEFAULTS.currency, decimals = 1, signed = false, locale } = opts;
  const n = toNumber(value);
  if (!isFinite(n)) return value == null ? "" : String(value);
  const sym = currencySymbol(currency);
  const abs = Math.abs(n);
  const TIERS = [[1e12, "T"], [1e9, "B"], [1e6, "M"], [1e3, "K"]];
  let text = null;
  for (const [t, suffix] of TIERS) {
    if (abs >= t) {
      text = numberFormatter(locale, { minimumFractionDigits: decimals, maximumFractionDigits: decimals }).format(+(abs / t).toFixed(decimals)) + suffix;
      break;
    }
  }
  if (text == null) text = numberFormatter(locale, { maximumFractionDigits: 2 }).format(abs);
  return sign(n, signed) + sym + text;
}

/** Compact plain number — 12.4M, 3.2K. Axis labels and ladders, never a ledger cell. */
export function formatCompact(value, opts = {}) {
  const { decimals = 1, locale, signed = false } = opts;
  const n = toNumber(value);
  if (!isFinite(n)) return value == null ? "" : String(value);
  const body = numberFormatter(locale, { notation: "compact", maximumFractionDigits: decimals }).format(Math.abs(n));
  return sign(n, signed) + body;
}

/** A share/contract quantity — grouped, up to `maxDecimals` (default 4, or the surface's `quantityDecimals`). */
export function formatQuantity(value, maxDecimals, locale) {
  const n = toNumber(value);
  if (!isFinite(n)) return value == null ? "" : String(value);
  return numberFormatter(locale, { maximumFractionDigits: maxDecimals ?? DEFAULTS.quantityDecimals }).format(n);
}

/**
 * A price at the instrument's precision — decimals from `tick` (or `decimals`, or the surface default).
 * This is the owner of price precision: a call site that types 2dp is a defect, not a decision.
 */
export function formatPrice(value, opts = {}) {
  const { tick, decimals, locale, currency, signed = false } = opts;
  const d = decimals ?? (tick != null ? decimalsForTick(tick) : DEFAULTS.priceDecimals);
  const n = toNumber(value);
  if (!isFinite(n)) return value == null ? "" : String(value);
  const body = (currency ? currencySymbol(currency) : "") + numberFormatter(locale, { minimumFractionDigits: d, maximumFractionDigits: d }).format(Math.abs(n));
  return sign(n, signed) + body;
}

/**
 * A percentage. Input is a fraction by default (0.0421 → "4.21%"); pass `scale:1` when the value
 * is already in percent units.
 */
export function formatPercent(value, opts = {}) {
  const { decimals = 2, signed = false, scale = 100, locale } = opts;
  const n = toNumber(value);
  if (!isFinite(n)) return value == null ? "" : String(value);
  const v = n * scale;
  const body = numberFormatter(locale, { minimumFractionDigits: decimals, maximumFractionDigits: decimals }).format(Math.abs(v)) + "%";
  if (v < 0 && wantParens(opts)) return `(${body})`;
  return sign(v, signed) + body;
}

/** A decimal fraction as basis points — formatBps(0.00124) → "+12.4 bp". */
export function formatBps(fraction, opts = {}) {
  const { signed = true, decimals = 1, locale } = opts;
  const n = toNumber(fraction);
  if (!isFinite(n)) return "";
  const bps = n * 10000;
  return sign(bps, signed) + numberFormatter(locale, { minimumFractionDigits: decimals, maximumFractionDigits: decimals }).format(Math.abs(bps)) + "\u202Fbp";
}

/**
 * An explicit-sign delta around any formatter — the content rule for every change figure.
 *   formatSignedDelta(-1234.5, (v) => formatMoney(v, { currency: "USD" })) → "−$1,234.50"
 */
export function formatSignedDelta(value, format = (v) => formatNumber(v, { decimals: 2 })) {
  const n = toNumber(value);
  if (!isFinite(n)) return value == null ? "" : String(value);
  return (n < 0 ? MINUS : "+") + format(Math.abs(n));
}

/** The house glyphs, so nothing re-types them: minus U+2212, em dash for a blank figure. */
export const GLYPHS = { minus: MINUS, dash: DASH };
