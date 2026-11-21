// Meridian DotMatrix — a MAGNITUDE grid: rows × columns where each cell encodes a number,
// not a status. Two encodings from one component: `shape="dot"` sizes a marker by magnitude
// (a summary matrix that stays legible at small cell sizes) and `shape="square"` tints the
// full cell (the exposure mini-map — a dense heat block you read as terrain).
// Boundary: CoverageMatrix owns STATUS grids (full/partial/gap — categorical, five states);
// DotMatrix owns continuous values. Use `diverging` when the sign is the point (long/short,
// over/under) — negatives take the loss hue, positives the gain hue.
import React from "react";

const SR = { position: "absolute", width: 1, height: 1, padding: 0, margin: -1, overflow: "hidden", clip: "rect(0 0 0 0)", whiteSpace: "nowrap", border: 0 };
const MONO = { fontFamily: "var(--font-data)", fontVariantNumeric: "slashed-zero tabular-nums" };

const cellValue = (c) => (c == null ? null : typeof c === "object" ? c.value : c);

export function DotMatrix({
  rows = [],                   // [{ id?, label, sub?, cells: (number | null | {value, note})[] }]
  columns = [],                // string[] | [{ id?, label }]
  shape = "dot",               // "dot" (size-encoded) | "square" (tint-encoded)
  diverging = false,
  domainMax = null,            // override the magnitude that saturates the scale
  cellSize = 26,
  gap = 3,
  rowLabelWidth = 128,
  colLabels = true,
  legend = true,
  fmt = (v) => (Math.abs(v) >= 100 ? v.toFixed(0) : v.toFixed(1)),
  emptyMark = "–",
  onCellClick = null,
  ariaLabel,
  caption,
}) {
  const cols = columns.map((c, i) => (typeof c === "string" ? { id: String(i), label: c } : c));
  const all = [];
  rows.forEach((r) => (r.cells || []).forEach((c) => { const v = cellValue(c); if (v != null && isFinite(v)) all.push(Math.abs(v)); }));
  const max = domainMax != null ? domainMax : (all.length ? Math.max(...all) : 1);
  const hue = (v) => (!diverging ? "var(--chart-primary)" : v < 0 ? "var(--series-negative)" : "var(--series-positive)");
  const mag = (v) => Math.min(1, Math.abs(v) / (max || 1));

  const cellNode = (c, row, col, ci) => {
    const v = cellValue(c);
    const note = c && typeof c === "object" ? c.note : null;
    const title = v == null ? `${row.label} · ${col.label}: no data` : `${row.label} · ${col.label}: ${fmt(v)}${note ? ` — ${note}` : ""}`;
    const inner = v == null ? (
      <span style={{ ...MONO, fontSize: "var(--viz-type-micro,10px)", color: "var(--cond-na-fg,var(--text-muted))" }}>{emptyMark}</span>
    ) : shape === "square" ? null : (
      <span style={{ display: "block", width: Math.max(4, Math.round(cellSize * 0.78 * Math.sqrt(mag(v)))), height: Math.max(4, Math.round(cellSize * 0.78 * Math.sqrt(mag(v)))), borderRadius: "50%", background: hue(v), opacity: 0.86 }} />
    );
    const bg = v != null && shape === "square"
      ? `color-mix(in srgb, ${hue(v)} ${Math.round(8 + mag(v) * 78)}%, transparent)` : "transparent";
    const style = {
      width: cellSize, height: cellSize, display: "grid", placeItems: "center", background: bg,
      border: "1px solid var(--border-divider,var(--border))", borderRadius: shape === "square" ? 0 : 2,
      padding: 0, margin: 0, font: "inherit", cursor: onCellClick ? "pointer" : "default",
    };
    return onCellClick
      ? <button key={ci} type="button" title={title} onClick={() => onCellClick(row, col, v)} style={style}>{inner}</button>
      : <div key={ci} title={title} style={style}>{inner}</div>;
  };

  return (
    <div role={ariaLabel ? "group" : undefined} aria-label={ariaLabel} style={{ position: "relative", display: "inline-flex", flexDirection: "column", gap: 8, maxWidth: "100%" }}>
      <div style={{ overflowX: "auto" }}>
        <div style={{ display: "grid", gridTemplateColumns: `${rowLabelWidth}px repeat(${cols.length},${cellSize}px)`, gap, alignItems: "center" }}>
          {colLabels && <div />}
          {colLabels && cols.map((c, i) => (
            <div key={c.id || i} style={{ ...MONO, fontSize: "var(--viz-type-micro,10px)", color: "var(--text-muted)", textAlign: "center", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{c.label}</div>
          ))}
          {rows.map((r, ri) => (
            <React.Fragment key={r.id || r.label || ri}>
              <div style={{ minWidth: 0, paddingRight: 8 }}>
                <div style={{ fontSize: "var(--viz-type-label,12px)", color: "var(--text-primary)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{r.label}</div>
                {r.sub && <div style={{ ...MONO, fontSize: "var(--viz-type-micro,10px)", color: "var(--text-muted)" }}>{r.sub}</div>}
              </div>
              {cols.map((c, ci) => cellNode((r.cells || [])[ci], r, c, ci))}
            </React.Fragment>
          ))}
        </div>
      </div>
      {legend && (
        <div style={{ display: "flex", alignItems: "center", gap: 10, fontFamily: "var(--font-data)", fontSize: "var(--viz-type-micro,10px)", color: "var(--text-secondary)" }}>
          <span>{diverging ? fmt(-max) : "0"}</span>
          <span style={{ display: "flex", gap: 2 }}>
            {(diverging ? [-1, -0.6, -0.25, 0.25, 0.6, 1] : [0.12, 0.35, 0.6, 0.8, 1]).map((t, i) => (
              <span key={i} style={{
                width: 16, height: 10,
                background: shape === "square"
                  ? `color-mix(in srgb, ${hue(t)} ${Math.round(8 + Math.abs(t) * 78)}%, transparent)`
                  : "transparent",
                display: "grid", placeItems: "center",
              }}>
                {shape !== "square" && <span style={{ width: Math.max(3, 9 * Math.sqrt(Math.abs(t))), height: Math.max(3, 9 * Math.sqrt(Math.abs(t))), borderRadius: "50%", background: hue(t), opacity: 0.86 }} />}
              </span>
            ))}
          </span>
          <span>{fmt(max)}</span>
        </div>
      )}
      {ariaLabel && (
        <table style={SR}>
          {caption && <caption>{caption}</caption>}
          <thead><tr><th scope="col">Row</th>{cols.map((c, i) => <th key={i} scope="col">{c.label}</th>)}</tr></thead>
          <tbody>{rows.map((r, ri) => (
            <tr key={ri}><th scope="row">{r.label}</th>
              {cols.map((c, ci) => { const v = cellValue((r.cells || [])[ci]); return <td key={ci}>{v == null ? "no data" : fmt(v)}</td>; })}
            </tr>
          ))}</tbody>
        </table>
      )}
      {caption && <p style={{ margin: 0, fontSize: "var(--viz-type-annotation,12px)", color: "var(--text-secondary)", maxWidth: "62ch" }}>{caption}</p>}
    </div>
  );
}

DotMatrix.displayName = "DotMatrix";
