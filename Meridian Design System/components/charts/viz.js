// Meridian visualization kernel — scales, paths, ticks, formatters, and the series-role
// ledger resolved to concrete SVG treatments. Bundle-internal; every chart that
// draws a role-coloured trace goes through `roleStyle` so color, dash, and
// marker stay assigned by ROLE, never by chart instance.
import React from "react";

import { formatNumber } from "../core/format.js";

export const ROLES = {
  actual:    { stroke: "var(--series-actual)",      width: "var(--viz-stroke-actual,2.25px)",    dash: "none",                          marker: "filled" },
  prior:     { stroke: "var(--series-prior)",       width: "var(--viz-stroke-secondary,1.5px)",  dash: "none",                          marker: "filled" },
  benchmark: { stroke: "var(--series-benchmark)",   width: "var(--viz-stroke-secondary,1.5px)",  dash: "var(--viz-dash-benchmark,4 4)", marker: "none" },
  scenario:  { stroke: "var(--series-scenario)",    width: "var(--viz-stroke-secondary,1.5px)",  dash: "var(--viz-dash-scenario,7 5)",  marker: "open" },
  target:    { stroke: "var(--series-target)",      width: "1px",                                 dash: "var(--viz-dash-target,2 4)",    marker: "none" },
  estimated: { stroke: "var(--series-estimated)",   width: "var(--viz-stroke-secondary,1.5px)",  dash: "var(--viz-dash-estimated,5 4)", marker: "open" },
  positive:  { stroke: "var(--series-positive)",    width: "var(--viz-stroke-secondary,1.5px)",  dash: "none",                          marker: "filled" },
  negative:  { stroke: "var(--series-negative)",    width: "var(--viz-stroke-secondary,1.5px)",  dash: "none",                          marker: "filled" },
};

export function roleStyle(role) {
  return ROLES[role] || ROLES.actual;
}

/** Linear scale factory: domain [d0,d1] → range [r0,r1]. */
export function scale([d0, d1], [r0, r1]) {
  const span = d1 - d0 || 1;
  const fn = (v) => r0 + ((v - d0) / span) * (r1 - r0);
  fn.invert = (p) => d0 + ((p - r0) / (r1 - r0 || 1)) * span;
  return fn;
}

/** Round tick values covering [min,max] — 4–6 major ticks, interpretable steps. */
export function niceTicks(min, max, count = 5) {
  if (!(isFinite(min) && isFinite(max)) || min === max) return [min];
  const raw = (max - min) / Math.max(1, count - 1);
  const mag = Math.pow(10, Math.floor(Math.log10(raw)));
  const norm = raw / mag;
  const step = (norm >= 7.5 ? 10 : norm >= 3.5 ? 5 : norm >= 1.5 ? 2 : 1) * mag;
  const start = Math.ceil(min / step) * step;
  const out = [];
  for (let v = start; v <= max + step * 0.001; v += step) out.push(Number(v.toFixed(10)));
  return out;
}

/** Padded domain across any number of numeric arrays (nulls ignored). */
export function domainOf(arrays, pad = 0.08) {
  const vals = [];
  arrays.forEach((a) => (a || []).forEach((v) => { if (v !== null && v !== undefined && isFinite(v)) vals.push(v); }));
  if (!vals.length) return [0, 1];
  const min = Math.min(...vals), max = Math.max(...vals);
  const p = (max - min || Math.abs(max) || 1) * pad;
  return [min - p, max + p];
}

/** Polyline path with null-gap support (a missing value breaks the line). */
export function linePath(values, x, y) {
  let d = "", pen = false;
  values.forEach((v, i) => {
    if (v === null || v === undefined || !isFinite(v)) { pen = false; return; }
    d += `${pen ? "L" : "M"}${x(i).toFixed(2)} ${y(v).toFixed(2)}`;
    pen = true;
  });
  return d;
}

/** Closed band path between two series (model interval). */
export function bandPath(upper, lower, x, y) {
  const up = upper.map((v, i) => `${i ? "L" : "M"}${x(i).toFixed(2)} ${y(v).toFixed(2)}`).join("");
  const dn = lower.map((v, i) => `L${x(lower.length - 1 - i).toFixed(2)} ${y(lower[lower.length - 1 - i]).toFixed(2)}`).join("");
  return `${up}${dn}Z`;
}

/** Orthogonal elbow between two node ports — operational traceability routing. */
export function elbow(x1, y1, x2, y2) {
  if (Math.abs(y1 - y2) < 0.5) return `M${x1} ${y1}L${x2} ${y2}`;
  const mx = x1 + (x2 - x1) / 2;
  return `M${x1} ${y1}L${mx} ${y1}L${mx} ${y2}L${x2} ${y2}`;
}

/** Width observer — charts render at real pixel width, never transform-scaled.
 *  Returns a disposer; call from a layout effect in the component. */
export function observeWidth(el, cb) {
  if (!el || typeof ResizeObserver === "undefined") return () => {};
  const ro = new ResizeObserver((entries) => {
    const w = entries[0]?.contentRect?.width;
    if (w) cb(w);
  });
  ro.observe(el);
  return () => ro.disconnect();
}

/** Hook form of observeWidth: `const [ref, width] = useMeasuredWidth()` — attach ref to
 *  the chart's wrapper div; width is its live clientWidth (0 before first measure). */
export function useMeasuredWidth() {
  const ref = React.useRef(null);
  const [w, setW] = React.useState(0);
  React.useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return undefined;
    setW(el.clientWidth || 0);
    return observeWidth(el, setW);
  }, []);
  return [ref, w];
}

/* ── Named formatters — the kit's default number/date treatments. Charts take these as
   `fmt`-prop defaults; consumers reach them at window.<Namespace>.Fmt. ─────────────── */
const tidy = (x, d) => {
  let s = x.toFixed(d);
  if (s.indexOf(".") >= 0) s = s.replace(/0+$/, "").replace(/\.$/, "");
  return s;
};
export const Fmt = {
  /** 184.20 — two decimals, always. Pair with tabular-nums CSS. */
  price: (v) => formatNumber(Number(v), { decimals: 2 }),
  /** $1.24k / $3.6M / -$840 — compact, sign-preserving. */
  currency: (v) => {
    const a = Math.abs(v), s = v < 0 ? "-$" : "$";
    if (a >= 1e9) return s + tidy(a / 1e9, 2) + "B";
    if (a >= 1e6) return s + tidy(a / 1e6, 2) + "M";
    if (a >= 1e3) return s + tidy(a / 1e3, 2) + "k";
    return s + tidy(a, a < 10 ? 2 : 0);
  },
  /** 42 bp — integer basis points. */
  bp: (v) => `${Math.round(v)} bp`,
  /** 1.2M / 840k / 2.4B — unsigned quantity. */
  volume: (v) => {
    const a = Math.abs(v);
    if (a >= 1e9) return tidy(a / 1e9, 1) + "B";
    if (a >= 1e6) return tidy(a / 1e6, 1) + "M";
    if (a >= 1e3) return tidy(a / 1e3, 1) + "k";
    return String(Math.round(a));
  },
  /** Mar 4 — short month + day, from Date | ISO string | epoch ms. */
  date: (d) => {
    const dt = d instanceof Date ? d : new Date(d);
    return isNaN(dt) ? String(d) : dt.toLocaleDateString("en-US", { month: "short", day: "numeric" });
  },
};
