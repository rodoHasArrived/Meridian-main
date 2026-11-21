// Meridian DonutChart — part-to-whole for a SMALL set of parts (roughly ≤7), where the whole
// is a real total worth printing in the middle: allocation by sleeve, risk contribution,
// collateral composition. Past that count the parts stop being distinguishable and a ranked
// bar is the honest chart.
// `views` adds a controlled tab strip (Allocation / Risk) over the same ring — the caller owns
// `view` and `onViewChange`. Segment colour comes from the series-role ledger in order, so two
// donuts in one screen never disagree about what the second slice looks like.
import React from "react";

const MONO = { fontFamily: "var(--font-data)", fontVariantNumeric: "slashed-zero tabular-nums" };
const SR = { position: "absolute", width: 1, height: 1, padding: 0, margin: -1, overflow: "hidden", clip: "rect(0 0 0 0)", whiteSpace: "nowrap", border: 0 };
const PALETTE = ["var(--series-actual)", "var(--series-prior)", "var(--series-benchmark)", "var(--series-scenario)", "var(--series-information)", "var(--series-warning)", "var(--series-estimated)"];

function arcPath(cx, cy, rO, rI, a0, a1) {
  const p = (r, a) => [cx + r * Math.cos(a), cy + r * Math.sin(a)];
  const large = a1 - a0 > Math.PI ? 1 : 0;
  const [x0, y0] = p(rO, a0), [x1, y1] = p(rO, a1), [x2, y2] = p(rI, a1), [x3, y3] = p(rI, a0);
  return `M${x0.toFixed(2)} ${y0.toFixed(2)}A${rO} ${rO} 0 ${large} 1 ${x1.toFixed(2)} ${y1.toFixed(2)}L${x2.toFixed(2)} ${y2.toFixed(2)}A${rI} ${rI} 0 ${large} 0 ${x3.toFixed(2)} ${y3.toFixed(2)}Z`;
}

export function DonutChart({
  segments = null,             // [{ id?, label, value, color? }]
  views = null,                // [{ id, label, segments, centerLabel?, centerValue? }]
  view = null,
  onViewChange = null,
  size = 196,
  thickness = 28,
  gapDeg = 1.4,
  centerLabel = null,
  centerValue = null,
  total: totalProp = null,
  fmt = (v) => String(v),
  pctFmt = (p) => `${(p * 100).toFixed(1)}%`,
  legend = true,
  ariaLabel,
  caption,
}) {
  const active = views ? (views.find((v) => v.id === view) || views[0]) : null;
  const segs = (active ? active.segments : segments) || [];
  const cLabel = (active && active.centerLabel != null ? active.centerLabel : centerLabel);
  const cValue = (active && active.centerValue != null ? active.centerValue : centerValue);
  const total = totalProp != null ? totalProp : segs.reduce((a, s) => a + Math.abs(s.value || 0), 0);
  const cx = size / 2, cy = size / 2, rO = size / 2 - 2, rI = rO - thickness;
  const gap = (gapDeg * Math.PI) / 180;
  let a = -Math.PI / 2;
  const drawn = segs.map((s, i) => {
    const frac = total ? Math.abs(s.value || 0) / total : 0;
    const sweep = frac * Math.PI * 2;
    const a0 = a + (segs.length > 1 ? gap / 2 : 0), a1 = a + sweep - (segs.length > 1 ? gap / 2 : 0);
    a += sweep;
    return { ...s, frac, color: s.color || PALETTE[i % PALETTE.length], d: a1 > a0 ? arcPath(cx, cy, rO, rI, a0, a1) : null };
  });

  return (
    <div style={{ position: "relative" }}>
      {views && (
        <div role="tablist" aria-label={ariaLabel || "View"} style={{ display: "flex", gap: 2, marginBottom: 12, border: "1px solid var(--border)", borderRadius: 2, padding: 2, width: "fit-content" }}>
          {views.map((v) => {
            const on = v.id === active.id;
            return (
              <button key={v.id} type="button" role="tab" aria-selected={on}
                onClick={onViewChange ? () => onViewChange(v.id) : undefined}
                style={{
                  font: "inherit", fontSize: "var(--viz-type-micro,10px)", fontWeight: 600, letterSpacing: ".04em",
                  fontVariant: "all-small-caps", padding: "3px 10px", borderRadius: 1, border: "none",
                  cursor: onViewChange ? "pointer" : "default",
                  background: on ? "var(--viz-select-surface,var(--selected-surface))" : "transparent",
                  color: on ? "var(--text-primary)" : "var(--text-muted)",
                  boxShadow: on ? "inset 0 -2px 0 var(--viz-select-rail,var(--selected))" : "none",
                }}>{v.label}</button>
            );
          })}
        </div>
      )}
      <div style={{ display: "flex", alignItems: "center", gap: 24, flexWrap: "wrap" }}>
        <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} style={{ display: "block", flex: "0 0 auto" }}
          role={ariaLabel ? "img" : undefined} aria-label={ariaLabel} aria-hidden={ariaLabel ? undefined : "true"}>
          <circle cx={cx} cy={cy} r={(rO + rI) / 2} fill="none" stroke="var(--viz-grid,var(--chart-grid))" strokeWidth={thickness} />
          {drawn.map((s, i) => s.d && <path key={s.id || i} d={s.d} fill={s.color} opacity="0.9" />)}
          {(cValue != null || cLabel != null) && (
            <g>
              {cValue != null && <text x={cx} y={cy + (cLabel != null ? 2 : 6)} textAnchor="middle" style={{ ...MONO, fontSize: Math.round(size * 0.15), fontWeight: 600 }} fill="var(--text-primary)">{cValue}</text>}
              {cLabel != null && <text x={cx} y={cy + Math.round(size * 0.115)} textAnchor="middle" style={{ fontFamily: "var(--font-body)", fontSize: "var(--viz-type-micro,10px)", fontVariant: "all-small-caps", letterSpacing: ".05em" }} fill="var(--text-muted)">{cLabel}</text>}
            </g>
          )}
        </svg>
        {legend && (
          <ul style={{ listStyle: "none", margin: 0, padding: 0, display: "flex", flexDirection: "column", gap: 6, minWidth: 0, flex: "1 1 180px" }}>
            {drawn.map((s, i) => (
              <li key={s.id || i} style={{ display: "flex", alignItems: "baseline", gap: 8, minWidth: 0 }}>
                <span aria-hidden="true" style={{ width: 10, height: 10, flex: "0 0 auto", background: s.color, opacity: 0.9, borderRadius: 1, transform: "translateY(1px)" }} />
                <span style={{ flex: "1 1 auto", minWidth: 0, fontSize: "var(--viz-type-label,12px)", color: "var(--text-primary)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{s.label}</span>
                <span style={{ ...MONO, flex: "0 0 auto", fontSize: "var(--viz-type-label,12px)", color: "var(--text-secondary)" }}>{fmt(s.value)}</span>
                <span style={{ ...MONO, flex: "0 0 auto", fontSize: "var(--viz-type-micro,10px)", color: "var(--text-muted)", minWidth: 44, textAlign: "right" }}>{pctFmt(s.frac)}</span>
              </li>
            ))}
          </ul>
        )}
      </div>
      {ariaLabel && (
        <table style={SR}>
          <caption>{caption || ariaLabel}</caption>
          <thead><tr><th scope="col">Part</th><th scope="col">Value</th><th scope="col">Share</th></tr></thead>
          <tbody>{drawn.map((s, i) => <tr key={i}><th scope="row">{s.label}</th><td>{fmt(s.value)}</td><td>{pctFmt(s.frac)}</td></tr>)}</tbody>
        </table>
      )}
      {caption && <p style={{ margin: "12px 0 0", fontSize: "var(--viz-type-annotation,12px)", color: "var(--text-secondary)", maxWidth: "62ch" }}>{caption}</p>}
    </div>
  );
}

DonutChart.displayName = "DonutChart";
