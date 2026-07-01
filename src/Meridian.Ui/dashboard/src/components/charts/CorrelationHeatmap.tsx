// Meridian CorrelationHeatmap — a square matrix of pairwise correlations (-1..1). Positive
// correlation washes green, negative red, intensity by magnitude; the diagonal reads 1.00. A
// token-driven grid (not SVG) so values stay legible and cells stay square at any size.
import { Fragment } from "react";

export interface CorrelationHeatmapProps {
  /** Axis labels (tickers/series), length N. Used for both rows and columns. */
  labels: string[];
  /** N×N correlation values in -1..1; `matrix[r][c]`. */
  matrix: Array<Array<number | null>>;
  /** Cell value formatter. @default v => v.toFixed(2) */
  valueFmt?: (v: number) => string;
  /** Square cell edge in px. @default 46 */
  cellSize?: number;
  /** Row-header column width in px. @default 56 */
  headerSize?: number;
  /** Print the numeric value inside each cell. @default true */
  showValues?: boolean;
  /** Hover callback with { row, col, value }. */
  onCellHover?: ((cell: { row: number; col: number; value: number | null }) => void) | null;
}

let injected = false;
function inject(): void {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-corr{display:inline-grid;gap:1px;background:var(--border,#CBD3DC);
  border:1px solid var(--border-strong,#99A5B2);font-family:var(--font-data,monospace);}
.mds-corr__h{background:var(--bg-medium,#EBEFF4);color:var(--text-secondary,#4D5967);
  font-size:11px;font-weight:600;display:flex;align-items:center;justify-content:center;
  padding:4px 6px;white-space:nowrap;}
.mds-corr__rh{justify-content:flex-end;padding-right:8px;}
.mds-corr__cell{display:flex;align-items:center;justify-content:center;font-size:11px;
  color:var(--text-primary,#22272E);font-variant-numeric:tabular-nums;cursor:default;}
.mds-corr__cell--diag{color:var(--text-muted,#59636F);background:var(--bg-active,#E6EEF5)!important;}
.mds-corr__corner{background:var(--bg-medium,#EBEFF4);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "corr-heatmap");
  el.textContent = css;
  document.head.appendChild(el);
}

function cellColor(v: number | null): string {
  if (v == null || Number.isNaN(v)) return "var(--bg-light,#FFFFFF)";
  const mag = Math.min(1, Math.abs(v));
  const pct = (mag * 62).toFixed(0);
  const base = v >= 0 ? "var(--green,#16885F)" : "var(--red,#BA3F55)";
  return `color-mix(in srgb, ${base} ${pct}%, var(--bg-light,#FFFFFF))`;
}

export function CorrelationHeatmap({
  labels = [],
  matrix = [],
  valueFmt = (v) => v.toFixed(2),
  cellSize = 46,
  headerSize = 56,
  showValues = true,
  onCellHover = null
}: CorrelationHeatmapProps) {
  inject();
  const n = labels.length;
  const cols = `${headerSize}px repeat(${n}, ${cellSize}px)`;

  return (
    <div className="mds-corr" style={{ gridTemplateColumns: cols }} role="grid" aria-label="Correlation matrix">
      <div className="mds-corr__corner" />
      {labels.map((l) => (
        <div key={"ch" + l} className="mds-corr__h" role="columnheader">
          {l}
        </div>
      ))}
      {matrix.map((row, r) => (
        <Fragment key={"r" + r}>
          <div className="mds-corr__h mds-corr__rh" role="rowheader">
            {labels[r]}
          </div>
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
        </Fragment>
      ))}
    </div>
  );
}

CorrelationHeatmap.displayName = "CorrelationHeatmap";
