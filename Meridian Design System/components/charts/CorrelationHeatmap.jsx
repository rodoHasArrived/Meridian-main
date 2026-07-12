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
.mds-corr{display:inline-grid;gap:1px;background:var(--border,#D7DCE2);
  border:1px solid var(--border-strong,#AAB4BF);font-family:var(--font-data,monospace);}
.mds-corr__h{background:var(--bg-medium,#F5F7FA);color:var(--text-secondary,#4D5967);
  font-size:11px;font-weight:600;display:flex;align-items:center;justify-content:center;
  padding:4px 6px;white-space:nowrap;}
.mds-corr__rh{justify-content:flex-end;padding-right:8px;}
.mds-corr__cell{display:flex;align-items:center;justify-content:center;font-size:11px;
  color:var(--text-primary,#22272E);font-variant-numeric:tabular-nums;cursor:default;}
.mds-corr__cell--diag{color:var(--text-muted,#59636F);background:var(--bg-active,#E6EEF5)!important;}
.mds-corr__corner{background:var(--bg-medium,#F5F7FA);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "corr-heatmap");
  el.textContent = css;
  document.head.appendChild(el);
}

function cellColor(v) {
  if (v == null || Number.isNaN(v)) return "var(--bg-light,#FFFFFF)";
  const mag = Math.min(1, Math.abs(v));
  const pct = (mag * 62).toFixed(0);
  const base = v >= 0 ? "var(--green,#16885F)" : "var(--red,#BA3F55)";
  return `color-mix(in srgb, ${base} ${pct}%, var(--bg-light,#FFFFFF))`;
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
    <div className="mds-corr" style={{ gridTemplateColumns: cols }} role="grid" aria-label="Correlation matrix">
      <div className="mds-corr__corner" />
      {labels.map((l) => (
        <div key={"ch" + l} className="mds-corr__h" role="columnheader">{l}</div>
      ))}
      {matrix.map((row, r) => (
        <React.Fragment key={"r" + r}>
          <div className="mds-corr__h mds-corr__rh" role="rowheader">{labels[r]}</div>
          {row.map((v, c) => {
            const diag = r === c;
            return (
              <div
                key={"c" + r + "-" + c}
                role="gridcell"
                className={`mds-corr__cell${diag ? " mds-corr__cell--diag" : ""}`}
                style={{ background: cellColor(v), height: cellSize }}
                title={`${labels[r]} · ${labels[c]} = ${v == null ? "—" : valueFmt(v)}`}
                onMouseEnter={onCellHover ? () => onCellHover({ row: r, col: c, value: v }) : undefined}
              >
                {showValues && v != null && !Number.isNaN(v) ? valueFmt(v) : ""}
              </div>
            );
          })}
        </React.Fragment>
      ))}
    </div>
  );
}

CorrelationHeatmap.displayName = "CorrelationHeatmap";
