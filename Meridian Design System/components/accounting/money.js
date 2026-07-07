// Meridian accounting — shared money helpers. No React, no DOM. Used by AmountCell and the
// ledger/statement primitives so every amount formats identically (tabular, fixed decimals,
// accounting parentheses for negatives, zero-as-dash) — and by the books components for the
// arithmetic they prove: explicit rounding policy, remainder-exact allocation, FX translation,
// and normal-balance classification. Exposed to consumers via the `Money` carrier object.

const SYMBOLS = { USD: "$", EUR: "\u20AC", GBP: "\u00A3", JPY: "\u00A5", CNY: "\u00A5", CHF: "CHF\u202F", CAD: "$", AUD: "$" };

// ISO-4217 minor units where they differ from 2.
const MINOR_UNITS = { JPY: 0, KRW: 0, VND: 0, CLP: 0, ISK: 0, BHD: 3, KWD: 3, OMR: 3, TND: 3 };

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

/** Coerce a value (number or money-ish string like "(1,234.00)") to a Number. NaN if unparseable. */
export function toNumber(value) {
  if (value == null || value === "") return NaN;
  if (typeof value === "number") return value;
  const s = String(value).trim();
  const neg = /^\(.*\)$/.test(s); // accounting parentheses = negative
  const n = parseFloat(s.replace(/[(),\s]/g, "").replace(/[^0-9.\-]/g, ""));
  return neg ? -Math.abs(n) : n;
}

/**
 * Round a money value to `decimals` places under an explicit policy.
 * mode: "half-even" (banker's — the books default: unbiased across many postings)
 *     | "half-up"   (half away from zero — invoice convention)
 *     | "trunc"     (toward zero — withholding/fee convention).
 */
export function roundMoney(value, decimals = 2, mode = "half-even") {
  const n = toNumber(value);
  if (!isFinite(n)) return NaN;
  const f = Math.pow(10, decimals);
  const scaled = +(n * f).toPrecision(15); // strip FP dust so exact-half detection works
  if (mode === "trunc") return Math.trunc(scaled) / f;
  const floor = Math.floor(scaled);
  if (scaled - floor !== 0.5) return Math.round(scaled) / f;
  if (mode === "half-up") return (n < 0 ? floor : floor + 1) / f;
  return (floor % 2 === 0 ? floor : floor + 1) / f; // half-even
}

/**
 * Split `total` across `weights` so the parts sum to the rounded total EXACTLY — no lost or
 * invented cents. Largest-remainder method at the currency's minor unit; zero/invalid weight
 * sets fall back to an equal split. Returns numbers in weight order.
 *   allocateAmount(100, [1, 1, 1]) → [33.34, 33.33, 33.33]
 */
export function allocateAmount(total, weights, decimals = 2) {
  const t = toNumber(total);
  if (!isFinite(t) || !Array.isArray(weights) || weights.length === 0) return [];
  const f = Math.pow(10, decimals);
  const T = Math.round(+(Math.abs(t) * f).toPrecision(15)); // total in minor units
  const sign = t < 0 ? -1 : 1;
  let ws = weights.map((w) => {
    const n = toNumber(w);
    return isFinite(n) && n > 0 ? n : 0;
  });
  let sum = ws.reduce((a, b) => a + b, 0);
  if (sum === 0) { ws = weights.map(() => 1); sum = ws.length; }
  const exact = ws.map((w) => (T * w) / sum);
  const base = exact.map(Math.floor);
  let rem = T - base.reduce((a, b) => a + b, 0);
  const order = exact
    .map((e, i) => [e - base[i], i])
    .sort((a, b) => b[0] - a[0] || a[1] - b[1]); // biggest remainder first, then stable
  for (let k = 0; k < rem; k++) base[order[k % order.length][1]] += 1;
  return base.map((u) => (sign * u) / f);
}

/** Translate a local-currency amount at `rate` (base units per 1 local unit), half-even rounded. */
export function convertAmount(value, rate, decimals = 2) {
  const v = toNumber(value);
  const r = toNumber(rate);
  if (!isFinite(v) || !isFinite(r)) return NaN;
  return roundMoney(v * r, decimals, "half-even");
}

const DEBIT_NORMAL = /^(asset|expense|loss|dividend|draw|contra.?(liability|equity|revenue|income))/;
const CREDIT_NORMAL = /^(liability|equity|capital|revenue|income|gain|contra.?(asset|expense))/;

/** Normal balance side for an account type — "asset"/"expense" → debit; "liability"/"equity"/"revenue" → credit. */
export function accountNormalSide(type) {
  const t = String(type || "").toLowerCase().trim();
  if (DEBIT_NORMAL.test(t)) return "debit";
  if (CREDIT_NORMAL.test(t)) return "credit";
  return "debit";
}

/**
 * Format a money value the Meridian way.
 * opts: { currency, decimals=2, parens=false, zeroDash=false, signed=false }
 *  - parens   → negatives as (1,234.00) instead of -1,234.00 (statement convention)
 *  - zeroDash → exact zero renders as an em dash
 *  - signed   → positives get an explicit leading +
 */
export function formatMoney(value, opts = {}) {
  const { currency, decimals = 2, parens = false, zeroDash = false, signed = false } = opts;
  const num = toNumber(value);
  if (!isFinite(num)) return value == null ? "" : String(value);
  if (zeroDash && num === 0) return "\u2014";
  const sym = currencySymbol(currency);
  const mag = Math.abs(num).toLocaleString("en-US", {
    minimumFractionDigits: decimals, maximumFractionDigits: decimals,
  });
  const neg = num < 0;
  if (neg && parens) return `(${sym}${mag})`;
  const sign = neg ? "\u2212" : signed && num > 0 ? "+" : "";
  return `${sign}${sym}${mag}`;
}

/** Compact money for metric surfaces — $12.4M, −€3.2K. Never use inside ledger columns. */
export function formatMoneyCompact(value, opts = {}) {
  const { currency, decimals = 1, signed = false } = opts;
  const n = toNumber(value);
  if (!isFinite(n)) return value == null ? "" : String(value);
  const sym = currencySymbol(currency);
  const abs = Math.abs(n);
  const TIERS = [[1e12, "T"], [1e9, "B"], [1e6, "M"], [1e3, "K"]];
  let text = null;
  for (const [t, suffix] of TIERS) {
    if (abs >= t) { text = (abs / t).toFixed(decimals) + suffix; break; }
  }
  if (text == null) text = abs.toLocaleString("en-US", { maximumFractionDigits: 2 });
  const sign = n < 0 ? "\u2212" : signed && n > 0 ? "+" : "";
  return `${sign}${sym}${text}`;
}

/** Format a share/contract quantity — thousands separators, up to `maxDecimals` (default 4). */
export function formatQuantity(value, maxDecimals = 4) {
  const n = toNumber(value);
  if (!isFinite(n)) return value == null ? "" : String(value);
  return n.toLocaleString("en-US", { maximumFractionDigits: maxDecimals });
}

/** Format a decimal fraction as basis points — formatBps(0.00124) → "+12.4 bp". */
export function formatBps(fraction, opts = {}) {
  const { signed = true, decimals = 1 } = opts;
  const n = toNumber(fraction);
  if (!isFinite(n)) return "";
  const bps = n * 10000;
  const sign = bps < 0 ? "\u2212" : signed && bps > 0 ? "+" : "";
  return `${sign}${Math.abs(bps).toFixed(decimals)}\u202Fbp`;
}

/** Sum a list of money-ish values, ignoring blanks/NaN. */
export function sumAmounts(values) {
  return values.reduce((a, v) => {
    const n = toNumber(v);
    return a + (isFinite(n) ? n : 0);
  }, 0);
}

/** Whole days from `from` to `to` (ISO strings or Dates). Positive when `to` is later. */
export function daysBetween(from, to) {
  const a = new Date(from);
  const b = new Date(to);
  if (isNaN(a.getTime()) || isNaN(b.getTime())) return NaN;
  return Math.round((b.getTime() - a.getTime()) / 86400000);
}

/**
 * Aging bucket index for days past due against ascending `boundaries`.
 * Default [1, 31, 61, 91] → 0 Current · 1 (1–30) · 2 (31–60) · 3 (61–90) · 4 (90+).
 */
export function agingBucketIndex(daysPastDue, boundaries = [1, 31, 61, 91]) {
  const d = toNumber(daysPastDue);
  if (!isFinite(d)) return 0;
  let i = 0;
  for (const b of boundaries) {
    if (d >= b) i++;
    else break;
  }
  return i;
}

/**
 * Collapse raw journal postings into one trial-balance row per account — the account's net
 * lands on the side of its sign (debit-positive), rounded half-even at `decimals`. The output
 * feeds `TrialBalance` directly; account `type` is kept for section grouping.
 */
export function buildTrialBalance(postings, opts = {}) {
  const { decimals = 2 } = opts;
  const map = new Map();
  for (const p of postings || []) {
    const key = `${p.code || ""}\u0000${p.account || ""}`;
    if (!map.has(key)) map.set(key, { code: p.code, account: p.account, type: p.type, net: 0 });
    const acc = map.get(key);
    if (p.type && !acc.type) acc.type = p.type;
    acc.net += (toNumber(p.debit) || 0) - (toNumber(p.credit) || 0);
  }
  return [...map.values()].map((a) => {
    const net = roundMoney(a.net, decimals, "half-even");
    return {
      code: a.code,
      account: a.account,
      type: a.type,
      debit: net > 0 ? net : 0,
      credit: net < 0 ? -net : 0,
    };
  });
}

/** Straight-line amortization/depreciation schedule — remainder-exact (allocateAmount), so the periods sum to the total with no drift. */
export function amortizeStraightLine(total, periods, decimals = 2) {
  const n = Math.floor(toNumber(periods));
  if (!isFinite(n) || n <= 0) return [];
  return allocateAmount(total, new Array(n).fill(1), decimals);
}

/**
 * Revalue a local-currency balance from its booked rate to the current rate (rates in base
 * units per 1 local unit). Returns { booked, current, gainLoss } in base currency — gainLoss
 * is the unrealized FX gain (+) or loss (−), half-even rounded.
 */
export function fxRevalue(localAmount, bookedRate, currentRate, decimals = 2) {
  const booked = convertAmount(localAmount, bookedRate, decimals);
  const current = convertAmount(localAmount, currentRate, decimals);
  if (!isFinite(booked) || !isFinite(current)) return { booked: NaN, current: NaN, gainLoss: NaN };
  return { booked, current, gainLoss: roundMoney(current - booked, decimals, "half-even") };
}
