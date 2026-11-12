// Meridian CorrelationHeatmap — a square matrix of pairwise correlations (-1..1). Positive
// correlation washes green, negative red, intensity by magnitude; the diagonal reads 1.00.
// Built as a token-driven grid (crisp cells, mono labels) rather than SVG so values stay
// legible and the diagonal stays square at any size.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-corr{display:inline-grid;gap:1px;background:var(--border,#E4E3DE);
  border:1px solid var(--border-strong,#AFABA1);font-family:var(--font-data,monospace);}
.mds-corr__h{background:var(--bg-medium,#EDEAE4);color:var(--text-secondary,#4E5258);
  font-size:11px;font-weight:600;display:flex;align-items:center;justify-content:center;
  padding:4px 6px;white-space:nowrap;}
.mds-corr__rh{justify-content:flex-end;padding-right:8px;}
.mds-corr__cell{display:flex;align-items:center;justify-content:center;font-size:11px;
  color:var(--text-primary,#22252A);font-variant-numeric:tabular-nums;cursor:default;}
.mds-corr__cell--diag{color:var(--text-secondary,#4E5258);background:var(--bg-active,#F2E3DB)!important;}
.mds-corr__corner{background:var(--bg-medium,#EDEAE4);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "corr-heatmap");
  el.textContent = css;
  document.head.appendChild(el);
}

function cellColor(v) {
  if (v == null || Number.isNaN(v)) return "var(--bg-light,#FBFAF8)";
  const mag = Math.min(1, Math.abs(v));
  const pct = (mag * 62).toFixed(0);
  const base = v >= 0 ? "var(--green,#3A7A56)" : "var(--red,#A8443C)";
  return `color-mix(in srgb, ${base} ${pct}%, var(--bg-light,#FBFAF8))`;
}

export function CorrelationHeatmap({
  labels = [],
  matrix = [],                 // number[][] of correlations, -1..1
  valueFmt = (v) => v.toFixed(2),
  cellSize = 46,
  headerSize = 56,
  showValues = true,
  onCellHover = null,
}) {
  inject();
  const n = labels.length;
  const cols = `${headerSize}px repeat(${n}, ${cellSize}px)`;

  return (
    <div className="mds-corr" style={{ gridTemplateColumns: cols }} role="table" aria-label="Correlation matrix" aria-rowcount={matrix.length + 1} aria-colcount={n + 1}>
      <div role="row" aria-rowindex={1} style={{ display: "contents" }}>
      <div className="mds-corr__corner" role="columnheader" aria-label="Instrument" />
      {labels.map((l) => (
        <div key={"ch" + l} className="mds-corr__h" role="columnheader">{l}</div>
      ))}
      </div>
      {matrix.map((row, r) => (
        <div key={"r" + r} role="row" aria-rowindex={r + 2} style={{ display: "contents" }}>
          <div className="mds-corr__h mds-corr__rh" role="rowheader">{labels[r]}</div>
          {row.map((v, c) => {
            const diag = r === c;
            return (
              <div
                key={"c" + r + "-" + c}
                role="cell"
                aria-colindex={c + 2}
                aria-label={`${labels[r]} · ${labels[c]} = ${v == null ? "—" : valueFmt(v)}`}
                className={`mds-corr__cell${diag ? " mds-corr__cell--diag" : ""}`}
                style={{ background: cellColor(v), height: cellSize }}
                title={`${labels[r]} · ${labels[c]} = ${v == null ? "—" : valueFmt(v)}`}
                onMouseEnter={onCellHover ? () => onCellHover({ row: r, col: c, value: v }) : undefined}
              >
                {showValues && v != null && !Number.isNaN(v) ? valueFmt(v) : ""}
              </div>
            );
          })}
        </div>
      ))}
    </div>
  );
}

CorrelationHeatmap.displayName = "CorrelationHeatmap";
