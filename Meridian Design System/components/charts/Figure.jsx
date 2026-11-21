// Meridian Figure — the parametric analytical figure. One frame, a small set of
// geometries, series assigned by ROLE (never by chart instance). Reach for this
// instead of writing a new named chart whose only difference from an existing one
// is a bucket definition or a label: a new ladder is geometry="ladder", a new
// side-by-side matrix is geometry="plate".
//
// 4.0 scope note: this does NOT replace the existing domain ladders
// (CashFlowLadder / MaturityLadder / ScheduleLadder / IncomeSchedule /
// DurationContribution) or matrices (BasisComparisonMatrix / ComparisonPlate /
// RollforwardMatrix). Each of those carries a contract Figure does not model —
// a stated bucket definition, two-sided comparison mode, per-point value
// envelopes. Consolidating them is the 4.1 item D-01 in the release plan.
// Pair Figure with AnalyticalFigure: Figure is the plot, AnalyticalFigure is the
// question / unit / source / condition / fallback contract around it.
import React from "react";
import { formatNumber } from "../core/format.js";

/** Series roles — the ledger. Color is assigned here and nowhere else. */
export const FIGURE_ROLES = {
  actual:    { label: "Actual",    stroke: "var(--series-actual, #29323A)",    dash: null,    fill: "var(--series-actual, #29323A)" },
  prior:     { label: "Prior",     stroke: "var(--series-prior, #7890A4)",     dash: null,    fill: "var(--series-prior, #7890A4)" },
  benchmark: { label: "Benchmark", stroke: "var(--series-benchmark, #8C918F)", dash: "4 3",   fill: "var(--series-benchmark, #8C918F)" },
  scenario:  { label: "Scenario",  stroke: "var(--series-scenario, #C56343)",        dash: "5 3",   fill: "var(--series-scenario, #C56343)" },
  target:    { label: "Target",    stroke: "var(--text-muted, #606870)",    dash: "1 3",   fill: "none" },
  estimated: { label: "Estimated", stroke: "var(--series-actual, #29323A)",    dash: "3 2",   fill: "var(--series-prior, #7890A4)" },
};

export const GEOMETRIES = ["ladder", "plate", "distribution", "trace"];

const roleOf = (r) => FIGURE_ROLES[r] || FIGURE_ROLES.actual;
const AX = "var(--text-muted, #606870)";
const RULE = "var(--border, #D2CDC3)";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-fig{display:flex;flex-direction:column;background:var(--bg-light,#F8F7F3);border:1px solid var(--border,#D2CDC3);border-radius:2px;}
.mds-fig__hd{display:flex;justify-content:space-between;align-items:baseline;gap:12px;padding:8px 10px;border-bottom:1px solid var(--border,#D2CDC3);}
.mds-fig__ti{font-family:var(--font-body,system-ui);font-size:13px;font-weight:var(--weight-medium,500);color:var(--text-primary,#20252B);}
.mds-fig__cl{font-family:var(--font-data,ui-monospace);font-size:10px;color:var(--text-muted,#606870);white-space:nowrap;}
.mds-fig__bd{padding:10px;background:var(--bg-surface, #F8F7F3);}
.mds-fig__lg{display:flex;flex-wrap:wrap;gap:12px;padding:6px 10px;border-top:1px solid var(--border-divider,#E2DED5);
  font-family:var(--font-body,system-ui);font-size:11px;color:var(--text-secondary,#535C65);}
.mds-fig__lgi{display:inline-flex;align-items:center;gap:5px;}
.mds-fig__sw{width:14px;height:0;border-top-width:2px;border-top-style:solid;display:inline-block;}
.mds-fig__ax{font-family:var(--font-data,ui-monospace);font-size:9px;fill:var(--text-muted,#606870);}
.mds-fig__grid{display:grid;gap:10px;}`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "figure");
  el.textContent = css;
  document.head.appendChild(el);
}

function Ladder({ series, width, height, zero = true, ariaLabel }) {
  const s = series[0] || { points: [] };
  const pts = s.points || [];
  const n = Math.max(pts.length, 1);
  const max = Math.max(1, ...pts.map((p) => Math.abs(p.value || 0)));
  const padL = 54, padB = 20, padT = 6;
  const w = width - padL - 8, h = height - padB - padT;
  const bw = Math.max(4, (w / n) - 6);
  const mid = zero ? padT + h / 2 : padT + h;
  const scale = zero ? (h / 2) / max : h / max;
  return (
    <svg width={width} height={height} role={ariaLabel?"img":undefined} aria-label={ariaLabel} aria-hidden={ariaLabel?undefined:true}>
      <line x1={padL} y1={mid} x2={padL + w} y2={mid} stroke={RULE} strokeWidth="1" />
      {[max, 0, zero ? -max : null].filter((v) => v !== null).map((v, i) => (
        <text key={i} className="mds-fig__ax" x={padL - 6} y={mid - v * scale + 3} textAnchor="end">{formatNumber(v)}</text>
      ))}
      {pts.map((p, i) => {
        const r = roleOf(p.role || s.role);
        const v = p.value || 0;
        const bh = Math.abs(v) * scale;
        const x = padL + i * (w / n) + 3;
        const y = v >= 0 ? mid - bh : mid;
        return (
          <g key={p.label || i}>
            <rect x={x} y={y} width={bw} height={Math.max(1, bh)} fill={r.fill} opacity={p.role === "estimated" ? 0.55 : 0.9} />
            {p.role === "estimated" ? <rect x={x} y={y} width={bw} height={Math.max(1, bh)} fill="none" stroke={r.stroke} strokeDasharray="3 2" /> : null}
            <text className="mds-fig__ax" x={x + bw / 2} y={height - 6} textAnchor="middle">{p.label}</text>
          </g>
        );
      })}
    </svg>
  );
}

function Trace({ series, width, height, ariaLabel }) {
  const all = series.flatMap((s) => (s.points || []).map((p) => p.value));
  const max = Math.max(...all, 1), min = Math.min(...all, 0);
  const padL = 54, padB = 20, padT = 6;
  const w = width - padL - 8, h = height - padB - padT;
  const sx = (i, n) => padL + (n <= 1 ? 0 : (i * w) / (n - 1));
  const sy = (v) => padT + h - ((v - min) / (max - min || 1)) * h;
  return (
    <svg width={width} height={height} role={ariaLabel?"img":undefined} aria-label={ariaLabel} aria-hidden={ariaLabel?undefined:true}>
      <line x1={padL} y1={padT + h} x2={padL + w} y2={padT + h} stroke={RULE} />
      <line x1={padL} y1={padT} x2={padL} y2={padT + h} stroke={RULE} />
      {[max, (max + min) / 2, min].map((v, i) => (
        <text key={i} className="mds-fig__ax" x={padL - 6} y={sy(v) + 3} textAnchor="end">{Number(v).toFixed(2)}</text>
      ))}
      {series.map((s, si) => {
        const r = roleOf(s.role);
        const pts = s.points || [];
        const d = pts.map((p, i) => `${i ? "L" : "M"}${sx(i, pts.length)},${sy(p.value)}`).join(" ");
        return (
          <g key={s.label || si}>
            {s.band && s.band.length === pts.length ? (
              <path d={[...pts.map((p, i) => `${i ? "L" : "M"}${sx(i, pts.length)},${sy(s.band[i][1])}`),
                ...pts.map((p, i) => `L${sx(pts.length - 1 - i, pts.length)},${sy(s.band[pts.length - 1 - i][0])}`), "Z"].join(" ")}
                fill={r.stroke} opacity=".10" />
            ) : null}
            <path d={d} fill="none" stroke={r.stroke} strokeWidth="1.5" strokeDasharray={r.dash || undefined} />
          </g>
        );
      })}
    </svg>
  );
}

function Distribution({ series, width, height, ariaLabel }) {
  const s = series[0] || { points: [] };
  const pts = s.points || [];
  const max = Math.max(1, ...pts.map((p) => p.value || 0));
  const padL = 34, padB = 20, padT = 6;
  const w = width - padL - 8, h = height - padB - padT;
  const bw = Math.max(2, w / Math.max(pts.length, 1) - 2);
  const r = roleOf(s.role);
  return (
    <svg width={width} height={height} role={ariaLabel?"img":undefined} aria-label={ariaLabel} aria-hidden={ariaLabel?undefined:true}>
      <line x1={padL} y1={padT + h} x2={padL + w} y2={padT + h} stroke={RULE} />
      <text className="mds-fig__ax" x={padL - 6} y={padT + 8} textAnchor="end">{max}</text>
      {pts.map((p, i) => {
        const bh = ((p.value || 0) / max) * h;
        return <rect key={i} x={padL + i * (w / pts.length)} y={padT + h - bh} width={bw} height={Math.max(1, bh)} fill={r.fill} opacity=".85" />;
      })}
      {pts.filter((p) => p.label).map((p, i) => (
        <text key={"l" + i} className="mds-fig__ax" x={padL + pts.indexOf(p) * (w / pts.length) + bw / 2} y={height - 6} textAnchor="middle">{p.label}</text>
      ))}
    </svg>
  );
}

function Plate({ rows = [], columns = [], cells = [] }) {
  const get = (ri, ci) => (cells[ri] || [])[ci];
  return (
    <table style={{ width: "100%", borderCollapse: "collapse", fontFamily: "var(--font-data,ui-monospace)", fontVariantNumeric: "tabular-nums", fontSize: 12 }}>
      <thead>
        <tr>
          <th style={{ textAlign: "left", fontFamily: "var(--font-body,system-ui)", fontSize: 10, fontVariant: "all-small-caps", letterSpacing: ".06em", color: AX, padding: "0 8px 5px", borderBottom: "1px solid var(--border-strong,#AAA49A)" }} />
          {columns.map((c) => (
            <th key={c.key || c.label} style={{ textAlign: "right", fontFamily: "var(--font-body,system-ui)", fontSize: 10, fontVariant: "all-small-caps", letterSpacing: ".06em", color: AX, padding: "0 8px 5px", borderBottom: "1px solid var(--border-strong,#AAA49A)" }}>
              {c.label}{c.role ? <span style={{ color: roleOf(c.role).stroke }}> ·</span> : null}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.map((r, ri) => (
          <tr key={r.key || r.label || ri}>
            <td style={{ padding: "4px 8px", borderBottom: "1px solid var(--border-divider,#E2DED5)", fontFamily: "var(--font-body,system-ui)", color: "var(--text-secondary,#535C65)" }}>{r.label}</td>
            {columns.map((c, ci) => (
              <td key={c.key || ci} style={{ padding: "4px 8px", textAlign: "right", borderBottom: "1px solid var(--border-divider,#E2DED5)", color: "var(--text-primary,#20252B)" }}>{get(ri, ci)}</td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

export function Figure({
  geometry = "trace", series = [], rows, columns, cells, title, clocks, legend = true,
  width = 460, height = 200, zero, small, className = "", children, ...rest
}) {
  inject();
  const ariaLabel=rest["aria-label"]||title;
  const body =
    geometry === "ladder" ? <Ladder ariaLabel={ariaLabel} series={series} width={width} height={height} zero={zero !== false} />
    : geometry === "distribution" ? <Distribution ariaLabel={ariaLabel} series={series} width={width} height={height} />
    : geometry === "plate" ? <Plate rows={rows} columns={columns} cells={cells} />
    : <Trace ariaLabel={ariaLabel} series={series} width={width} height={height} />;
  const legendItems = geometry === "plate" ? (columns || []).filter((c) => c.role) : series.filter((s) => s.role || s.label);
  return (
    <figure className={`mds-fig${className ? " " + className : ""}`} style={{ margin: 0 }} {...rest}>
      {title || clocks ? (
        <div className="mds-fig__hd">
          <figcaption className="mds-fig__ti">{title}</figcaption>
          {clocks ? <span className="mds-fig__cl">{clocks}</span> : null}
        </div>
      ) : null}
      <div className="mds-fig__bd" style={small ? { padding: 6 } : undefined}>{body}</div>
      {legend && legendItems.length ? (
        <div className="mds-fig__lg">
          {legendItems.map((s, i) => {
            const r = roleOf(s.role);
            return (
              <span className="mds-fig__lgi" key={s.label || i}>
                <span className="mds-fig__sw" style={{ borderTopColor: r.stroke, borderTopStyle: r.dash ? "dashed" : "solid" }} />
                {s.label || r.label}
              </span>
            );
          })}
        </div>
      ) : null}
      {children}
    </figure>
  );
}

/** Small multiples / print sets — same geometry, one figure per subject. */
export function FigureSet({ figures = [], columns = 2, className = "", ...rest }) {
  inject();
  return (
    <div className={`mds-fig__grid${className ? " " + className : ""}`} style={{ gridTemplateColumns: `repeat(${columns}, minmax(0,1fr))` }} {...rest}>
      {figures.map((f, i) => <Figure key={f.title || i} small {...f} />)}
    </div>
  );
}
