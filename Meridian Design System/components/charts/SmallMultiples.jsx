// Meridian SmallMultiples — a trellis of mini plates, one per METRIC, for a single entity.
// Counterpart to PlateGrid, and the boundary matters: PlateGrid compares the SAME metric
// across many entities and therefore shares one domain by construction; SmallMultiples shows
// DIFFERENT metrics for one entity, so each panel scales to its own range by default (a 0–6
// ratio and a 0–300 bp spread both stay readable). `sharedScale` forces one domain when the
// panels are genuinely commensurate. Geometry comes from the comparison-plate token contract,
// so padding, plot height, and label placement are identical across the set.
import React from "react";
import { domainOf, linePath, scale, roleStyle, useMeasuredWidth } from "./viz.js";

const SR = { position: "absolute", width: 1, height: 1, padding: 0, margin: -1, overflow: "hidden", clip: "rect(0 0 0 0)", whiteSpace: "nowrap", border: 0 };
const MICRO = { fontFamily: "var(--font-data)", fontSize: "var(--viz-type-micro,10px)", fontVariantNumeric: "slashed-zero tabular-nums" };

function Plate({ panel, domain, height, showArea, showScale, baseline, fmt }) {
  const [ref, w] = useMeasuredWidth();
  const vals = panel.values || [];
  const rs = roleStyle(panel.role || "actual");
  const f = panel.fmt || fmt;
  const rail = showScale ? 32 : 4;
  const padT = 8, padB = 8;
  const n = Math.max(2, vals.length);
  const base = height - padB;
  const x = scale([0, n - 1], [rail, Math.max(rail + 12, w - 4)]);
  const y = scale(domain, [base, padT]);
  let first = -1, last = -1;
  vals.forEach((v, i) => { if (v != null && isFinite(v)) { if (first < 0) first = i; last = i; } });
  const d = linePath(vals, x, y);
  const areaD = d && first >= 0 ? `${d}L${x(last).toFixed(2)} ${base}L${x(first).toFixed(2)} ${base}Z` : "";
  const lastVal = last >= 0 ? vals[last] : null;
  const inDomain = (v) => v >= domain[0] && v <= domain[1];
  return (
    <div style={{ background: "var(--surface-plate,var(--bg-light))", border: "1px solid var(--border)", borderRadius: 2, padding: "var(--plate-pad,12px)", display: "flex", flexDirection: "column", gap: 6, minWidth: 0 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline", gap: 8, minWidth: 0 }}>
        <span style={{ fontSize: 10, fontWeight: 600, fontVariant: "all-small-caps", letterSpacing: ".04em", color: "var(--text-muted)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{panel.label}</span>
        {lastVal != null && <span style={{ ...MICRO, fontSize: "var(--viz-type-label,12px)", fontWeight: 600, color: "var(--text-primary)" }}>{f(lastVal)}</span>}
      </div>
      <div ref={ref} style={{ width: "100%" }}>
        {w > 0 ? (
          <svg width={w} height={height} viewBox={`0 0 ${w} ${height}`} style={{ display: "block" }} aria-hidden="true">
            {showScale && [domain[1], domain[0]].map((v, i) => (
              <text key={i} x={rail - 5} y={i === 0 ? padT + 4 : base} fill="var(--viz-axis,var(--chart-axis))" textAnchor="end" style={MICRO}>{f(v)}</text>
            ))}
            <line x1={rail} x2={w - 4} y1={base} y2={base} stroke="var(--viz-frame,var(--chart-border))" strokeWidth="1" />
            {baseline != null && inDomain(baseline) && (
              <line x1={rail} x2={w - 4} y1={y(baseline)} y2={y(baseline)} stroke="var(--viz-zero,var(--chart-border))" strokeWidth="var(--viz-stroke-zero,1.25px)" strokeDasharray="2 3" opacity="0.7" />
            )}
            {showArea && areaD && <path d={areaD} fill={rs.stroke} opacity="0.1" />}
            {d && <path d={d} fill="none" stroke={rs.stroke} strokeWidth={rs.width} strokeDasharray={rs.dash} strokeLinejoin="round" strokeLinecap="round" />}
            {lastVal != null && <circle cx={x(last)} cy={y(lastVal)} r="var(--viz-marker-dense,3px)" fill={rs.stroke} />}
          </svg>
        ) : <div style={{ width: "100%", height }} />}
      </div>
      {panel.sub && <span style={{ ...MICRO, color: "var(--text-secondary)" }}>{panel.sub}</span>}
    </div>
  );
}

export function SmallMultiples({
  panels = [],
  columns = null,              // fixed column count; otherwise auto-fills on --plate-min-width
  sharedScale = false,         // one domain across the set — only when panels are commensurate
  height = null,               // plot height; defaults to --plate-plot-h
  showArea = true,
  showScale = true,            // per-panel min/max micro rail
  baseline = null,             // dashed reference rule (0 for spreads and P&L)
  fmt = (v) => (Math.abs(v) >= 100 ? v.toFixed(0) : v.toFixed(2)),
  ariaLabel,
  caption,
}) {
  const plotH = height || 108;
  const shared = sharedScale ? domainOf(panels.map((p) => p.values || [])) : null;
  return (
    <div role={ariaLabel ? "group" : undefined} aria-label={ariaLabel} style={{ position: "relative" }}>
      <div style={{
        display: "grid",
        gridTemplateColumns: columns ? `repeat(${columns},minmax(0,1fr))` : "repeat(auto-fill,minmax(var(--plate-min-width,208px),1fr))",
        gap: "var(--plate-gap,12px)",
      }}>
        {panels.map((p, i) => (
          <Plate key={p.id || p.label || i} panel={p} height={plotH} showArea={showArea} showScale={showScale}
            baseline={p.baseline !== undefined ? p.baseline : baseline}
            fmt={fmt} domain={p.domain || shared || domainOf([p.values || []])} />
        ))}
      </div>
      {ariaLabel && (
        <dl style={SR}>
          {panels.map((p, i) => {
            const v = (p.values || []).filter((x) => x != null && isFinite(x));
            return <React.Fragment key={i}><dt>{p.label}</dt><dd>{v.length ? (p.fmt || fmt)(v[v.length - 1]) : "no data"}{p.sub ? ` — ${p.sub}` : ""}</dd></React.Fragment>;
          })}
        </dl>
      )}
      {caption && <p style={{ margin: "10px 0 0", fontSize: "var(--viz-type-annotation,12px)", color: "var(--text-secondary)", maxWidth: "62ch" }}>{caption}</p>}
    </div>
  );
}

SmallMultiples.displayName = "SmallMultiples";
