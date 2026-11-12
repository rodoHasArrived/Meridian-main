// Meridian ChartFrame — the one axis/plot chrome primitive every indexed-series chart renders
// through. Owns real-pixel geometry (measures its own width; SVG is never transform-scaled, so
// an 11px tick label is 11px at every container size), pane stacking, nice-tick gridlines,
// value rails, x labels, legend row, and the crosshair vline. Charts draw only their marks
// via the children render prop.
// A11y (3.5): `ariaLabel` names the figure (svg role="img"), `ariaDescription` ships as a
// linked description, and `accessibleData` renders a visually-hidden data table/list so the
// numbers are readable without the pixels. Unlabeled frames stay presentational (aria-hidden).
import React from "react";
import { niceTicks, useMeasuredWidth } from "./viz.js";

export const AXIS_FONT = { fontFamily: "var(--font-data)", fontSize: 11, fontVariantNumeric: "slashed-zero tabular-nums" };

const SR = { position: "absolute", width: 1, height: 1, padding: 0, margin: -1, overflow: "hidden", clip: "rect(0 0 0 0)", whiteSpace: "nowrap", border: 0 };
function AccessibleData({ data }) {
  if (!data) return null;
  if (typeof data === "string") return <p style={SR}>{data}</p>;
  if (Array.isArray(data)) return (
    <dl style={SR}>{data.map((d, i) => <React.Fragment key={i}><dt>{d.label}</dt><dd>{d.value}</dd></React.Fragment>)}</dl>
  );
  if (data.columns && data.rows) return (
    <table style={SR}>
      {data.caption && <caption>{data.caption}</caption>}
      <thead><tr>{data.columns.map((c, i) => <th key={i} scope="col">{c}</th>)}</tr></thead>
      <tbody>{data.rows.map((r, i) => <tr key={i}>{r.map((v, j) => <td key={j}>{v}</td>)}</tr>)}</tbody>
    </table>
  );
  return null;
}

/** Right-rail value chip (crosshair / last-value readouts). */
export function AxisChip({ x, y, width = 58, text, fill = "var(--chart-crosshair)", rx = 4 }) {
  return (
    <g>
      <rect x={x} y={y - 10} width={width} height={20} rx={rx} fill={fill} opacity="0.95" />
      <text x={x + width / 2} y={y + 5} fill="var(--text-on-fill,#fff)" textAnchor="middle"
        style={{ ...AXIS_FONT, fontSize: 13, fontWeight: 600 }}>{text}</text>
    </g>
  );
}

export function ChartFrame({
  height = 320,
  n,                          // number of x positions
  xMode = "point",            // "point" (first/last on the edges) | "band" (categorical slots)
  xLabels = [],               // string per index; "" = unlabeled position
  xTicks = 7,                 // target count of shown x labels
  rail = 62,                  // value-rail width, px
  railSide = "right",
  padL = 10, padT = 14, axisB = 26, gap = 12,
  panes,                      // [{ key, flex, domain:[lo,hi], ticks, fmt, zero }] top→bottom
  legend = null,              // [{ label, color, dashed?, swatch? }]
  crosshairIndex = null,
  leftRule = false,            // faint rule on the non-rail edge
  ariaLabel,                   // names the figure; without it the svg stays presentational
  ariaDescription,             // one-sentence reading, linked via aria-describedby
  accessibleData,              // string | {label,value}[] | {caption?, columns, rows} — hidden data table
  children,                    // (geo) => marks
}) {
  const uid = React.useId();
  const [ref, w] = useMeasuredWidth();
  const plotL = railSide === "right" ? padL : rail;
  const plotR = railSide === "right" ? Math.max(plotL + 40, w - rail) : Math.max(rail + 40, w - padL);
  const innerT = padT, innerB = height - axisB;
  const flexSum = panes.reduce((a, p) => a + (p.flex || 1), 0);
  const avail = innerB - innerT - gap * (panes.length - 1);
  const geo = {};
  let cy = innerT;
  panes.forEach((p) => {
    const h = (avail * (p.flex || 1)) / flexSum, t = cy, b = cy + h;
    cy = b + gap;
    const [d0, d1] = p.domain, span = d1 - d0 || 1;
    geo[p.key] = { t, b, h, domain: p.domain, y: (v) => t + ((d1 - v) * h) / span };
  });
  const band = (plotR - plotL) / Math.max(1, n);
  const x = xMode === "band"
    ? (i) => plotL + band * i + band / 2
    : (i) => plotL + i * ((plotR - plotL) / Math.max(1, n - 1));
  const g = { w, plotL, plotR, innerT, innerB, n, band, x, pane: (k) => geo[k] };

  // Choose shown x labels from the ones that EXIST — sparse label arrays (every 8th point
  // is a month) must not be re-filtered by a modulo step over raw indices.
  const idx = [];
  xLabels.forEach((l, i) => { if (l !== "" && l != null) idx.push(i); });
  const lstep = idx.length > xTicks ? Math.ceil(idx.length / xTicks) : 1;
  const shown = new Set(idx.filter((_, k) => k % lstep === 0 || k === idx.length - 1));
  const eps = 1e-9;

  const railX = railSide === "right" ? plotR : rail;
  const labelX = (v) => (railSide === "right" ? plotR + 7 : rail - 7);
  const labelAnchor = railSide === "right" ? "start" : "end";

  return (
    <div ref={ref} style={{ width: "100%" }}>
      {legend && (
        <div style={{ display: "flex", gap: 16, padding: "0 8px 8px", flexWrap: "wrap" }}>
          {legend.map((l, k) => (
            <span key={k} style={{ display: "inline-flex", alignItems: "center", gap: 6, fontFamily: "var(--font-data)", fontSize: 11, color: "var(--text-secondary)" }}>
              <span style={l.swatch
                ? { width: 14, height: 8, background: l.color, opacity: 0.25 }
                : { width: 14, height: 0, borderTop: `2px ${l.dashed ? "dashed" : "solid"} ${l.color}` }} />
              {l.label}
            </span>
          ))}
        </div>
      )}
      {w > 0 ? (
        <svg width={w} height={height} viewBox={`0 0 ${w} ${height}`} style={{ display: "block" }}
          role={ariaLabel ? "img" : undefined} aria-label={ariaLabel}
          aria-describedby={ariaDescription ? `${uid}-desc` : undefined}
          aria-hidden={ariaLabel ? undefined : "true"}>
          <rect x="0" y="0" width={w} height={height} fill="var(--chart-plot)" />
          {panes.map((p, pi) => {
            const pg = geo[p.key];
            return (
              <g key={p.key}>
                {p.ticks !== 0 && niceTicks(p.domain[0], p.domain[1], p.ticks || 5)
                  .filter((v) => v >= p.domain[0] - eps && v <= p.domain[1] + eps)
                  .map((v, k) => (
                    <g key={k}>
                      <line x1={plotL} x2={plotR} y1={pg.y(v)} y2={pg.y(v)} stroke="var(--chart-grid)" strokeWidth="1" />
                      <text x={labelX(v)} y={pg.y(v) + 4} fill="var(--chart-axis)" textAnchor={labelAnchor} style={AXIS_FONT}>{(p.fmt || String)(v)}</text>
                    </g>
                  ))}
                {p.zero != null && p.zero >= p.domain[0] - eps && p.zero <= p.domain[1] + eps && (
                  <line x1={plotL} x2={plotR} y1={pg.y(p.zero)} y2={pg.y(p.zero)} stroke="var(--chart-border)" strokeWidth="1.2" />
                )}
                {pi < panes.length - 1 && (
                  <line x1={plotL} x2={plotR} y1={pg.b} y2={pg.b} stroke="var(--chart-border)" strokeWidth="1" />
                )}
              </g>
            );
          })}
          {xLabels.map((lab, i) => (lab && shown.has(i)) ? (
            <g key={"x" + i}>
              {xMode === "point" && (
                <line x1={x(i)} x2={x(i)} y1={innerT} y2={innerB} stroke="var(--chart-grid)" strokeWidth="1" opacity="0.6" />
              )}
              <text
                x={xMode === "point" && i === 0 ? plotL : xMode === "point" && i === n - 1 ? plotR : x(i)}
                y={height - 7} fill="var(--chart-axis)"
                textAnchor={xMode === "point" && i === 0 ? "start" : xMode === "point" && i === n - 1 ? "end" : "middle"}
                style={AXIS_FONT}>{lab}</text>
            </g>
          ) : null)}
          <line x1={plotL} x2={plotR} y1={innerB} y2={innerB} stroke="var(--chart-border)" strokeWidth="1.2" />
          <line x1={railX} x2={railX} y1={innerT} y2={innerB} stroke="var(--chart-border)" strokeWidth="1.2" />
          {leftRule && (
            <line x1={railSide === "right" ? plotL : plotR} x2={railSide === "right" ? plotL : plotR} y1={innerT} y2={innerB}
              stroke="var(--chart-border)" strokeWidth="1" opacity="0.45" />
          )}
          {children(g)}
          {crosshairIndex != null && crosshairIndex >= 0 && crosshairIndex < n && (
            <line x1={x(crosshairIndex)} x2={x(crosshairIndex)} y1={innerT} y2={innerB}
              stroke="var(--chart-crosshair)" strokeWidth="1.2" strokeDasharray="4 2" opacity="0.8" />
          )}
        </svg>
      ) : (
        <div style={{ width: "100%", height }} />
      )}
      {ariaDescription && <p id={`${uid}-desc`} style={SR}>{ariaDescription}</p>}
      <AccessibleData data={accessibleData} />
    </div>
  );
}

ChartFrame.displayName = "ChartFrame";
