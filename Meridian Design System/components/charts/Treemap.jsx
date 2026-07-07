// Meridian Treemap — squarified area map of positive weights (exposure by position/sector,
// allocation by book). Tiles are alpha washes with hairline borders — never solid fills.
// Color by day-change `delta` (green/red, intensity ∝ magnitude), by `group` (fixed wash
// palette), or none. Div-based with measured layout so labels stay crisp at any size.
import React from "react";

// Squarified treemap (Bruls et al.) — items pre-sorted desc, areas scaled to w*h.
function worst(sum, mn, mx, side) {
  const s2 = sum * sum, w2 = side * side;
  return Math.max((w2 * mx) / s2, s2 / (w2 * mn));
}
function squarify(list, x, y, w, h, out) {
  if (!list.length || w <= 0 || h <= 0) return;
  const side = Math.min(w, h);
  let i = 0, sum = 0, mn = Infinity, mx = 0, prev = Infinity;
  while (i < list.length) {
    const v = list[i].area;
    const ns = sum + v, nmn = Math.min(mn, v), nmx = Math.max(mx, v);
    const wst = worst(ns, nmn, nmx, side);
    if (wst > prev && i > 0) break;
    sum = ns; mn = nmn; mx = nmx; prev = wst; i++;
  }
  const row = list.slice(0, i);
  const t = sum / side; // row thickness
  let off = 0;
  if (w >= h) {
    for (const it of row) { const len = it.area / t; out.push({ it, x, y: y + off, w: t, h: len }); off += len; }
    squarify(list.slice(i), x + t, y, w - t, h, out);
  } else {
    for (const it of row) { const len = it.area / t; out.push({ it, x: x + off, y, w: len, h: t }); off += len; }
    squarify(list.slice(i), x, y + t, w, h - t, out);
  }
}

const GROUP_WASHES = ["--accent", "--purple", "--green", "--orange", "--red"];

export function Treemap({
  items = [],                    // [{ label, value, delta?, group? }]
  colorBy = "auto",              // "auto" | "delta" | "group" | "none"
  valueFmt = (v) => String(v),
  deltaFmt = (d) => (d < 0 ? "\u2212" : "+") + Math.abs(d).toFixed(2) + "%",
  onSelect = null,
  minTileLabel = 44,             // px below which a tile renders empty (title attr only)
}) {
  const ref = React.useRef(null);
  const [dim, setDim] = React.useState({ w: 0, h: 0 });

  React.useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const upd = () => setDim({ w: el.clientWidth, h: el.clientHeight });
    upd();
    const ro = new ResizeObserver(upd);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const mode = colorBy === "auto"
    ? (items.some((d) => d.delta != null) ? "delta" : items.some((d) => d.group) ? "group" : "none")
    : colorBy;

  const clean = items.filter((d) => d.value > 0).slice().sort((a, b) => b.value - a.value);
  const total = clean.reduce((a, d) => a + d.value, 0);
  const rects = [];
  if (dim.w > 4 && dim.h > 4 && total > 0) {
    const scale = (dim.w * dim.h) / total;
    squarify(clean.map((d) => ({ ...d, area: d.value * scale })), 0, 0, dim.w, dim.h, rects);
  }

  const maxAbs = Math.max(1e-9, ...clean.map((d) => Math.abs(d.delta || 0)));
  const groups = [...new Set(clean.map((d) => d.group).filter(Boolean))];

  const wash = (d) => {
    if (mode === "delta" && d.delta != null) {
      const pctv = 8 + 20 * (Math.abs(d.delta) / maxAbs);
      return `color-mix(in srgb, var(${d.delta < 0 ? "--red" : "--green"}) ${pctv.toFixed(1)}%, transparent)`;
    }
    if (mode === "group" && d.group) {
      const tok = GROUP_WASHES[groups.indexOf(d.group) % GROUP_WASHES.length];
      return `color-mix(in srgb, var(${tok}) 12%, transparent)`;
    }
    return "var(--bg-raised)";
  };

  const mono = { fontFamily: "var(--font-data)", fontVariantNumeric: "tabular-nums" };

  return (
    <div ref={ref} style={{ position: "relative", width: "100%", height: "100%", minHeight: 120,
      background: "var(--chart-plot)", border: "1px solid var(--border)", overflow: "hidden", boxSizing: "border-box" }}>
      {rects.map(({ it, x, y, w, h }, i) => {
        const showAny = w >= minTileLabel && h >= 20;
        const showValue = w >= 72 && h >= 42;
        const showDelta = it.delta != null && w >= 72 && h >= 58;
        const tip = it.label + " \u00b7 " + valueFmt(it.value) + (it.delta != null ? " \u00b7 " + deltaFmt(it.delta) : "");
        return (
          <div key={i} title={tip}
            onClick={onSelect ? () => onSelect(it) : undefined}
            style={{ position: "absolute", left: x, top: y, width: w, height: h, boxSizing: "border-box",
              border: "1px solid var(--border)", background: wash(it), padding: "4px 6px", overflow: "hidden",
              cursor: onSelect ? "pointer" : "default" }}>
            {showAny && (
              <div style={{ fontSize: 10.5, fontWeight: 600, fontVariant: "all-small-caps", letterSpacing: ".04em",
                color: "var(--text-primary)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{it.label}</div>
            )}
            {showValue && (
              <div style={{ ...mono, fontSize: 11, color: "var(--text-secondary)", whiteSpace: "nowrap" }}>{valueFmt(it.value)}</div>
            )}
            {showDelta && (
              <div style={{ ...mono, fontSize: 10.5, color: it.delta < 0 ? "var(--red-dim)" : "var(--green-dim)", whiteSpace: "nowrap" }}>
                {deltaFmt(it.delta)}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

Treemap.displayName = "Treemap";
