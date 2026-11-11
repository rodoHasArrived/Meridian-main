// Meridian lineage graph — directional provenance and dependency, laid out in
// LAYERS with orthogonal routing, because ownership and data flow have a
// dominant direction and force-directed drift destroys traceability. Node order
// is stable between refreshes, edges route around real node boxes, labels sit
// outside nodes, and every graph ships a text outline of nodes and
// relationships for the non-visual path.
import React from "react";
import { elbow } from "./viz";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-lin{display:flex;flex-direction:column;gap:10px;width:100%;font-family:var(--font-body);}
.mds-lin__vp{position:relative;overflow:auto;background:var(--bg-light);border:1px solid var(--border);
  border-radius:var(--radius-chip,2px);}
.mds-lin__canvas{position:relative;}
.mds-lin__edges{position:absolute;inset:0;pointer-events:none;}
.mds-lin__elabel{font-family:var(--font-body);font-size:var(--viz-type-micro,10px);fill:var(--text-muted);
  paint-order:stroke;stroke:var(--bg-light);stroke-width:3px;stroke-linejoin:round;}
.mds-lin__node{position:absolute;display:flex;flex-direction:column;justify-content:center;gap:2px;
  padding:7px 10px;background:var(--surface-plate);border:1px solid var(--border-strong);
  border-radius:var(--radius-chip,2px);text-align:left;cursor:pointer;overflow:hidden;
  font:inherit;color:var(--text-secondary);}
.mds-lin__node:hover{border-color:var(--brand-signal,#C56343);}
.mds-lin__node:focus-visible{outline:2px solid var(--border-focus);outline-offset:2px;}
.mds-lin__node--sel{border-color:var(--viz-select-rail);border-width:2px;background:var(--viz-select-surface);}
.mds-lin__nl{font-size:12px;font-weight:var(--weight-semibold,600);color:var(--text-primary);
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-lin__nm{font-family:var(--font-data);font-size:10px;color:var(--text-muted);
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-lin__out{display:flex;flex-direction:column;gap:3px;margin:0;padding:0;list-style:none;}
.mds-lin__out li{font-size:11.5px;line-height:16px;color:var(--text-secondary);
  padding-left:9px;border-left:2px solid var(--border);}
.mds-lin__out b{font-weight:var(--weight-semibold,600);color:var(--text-primary);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "lineage-graph");
  el.textContent = css;
  document.head.appendChild(el);
}

const KIND_LABEL = {
  source: "Source", transformation: "Transformation", model: "Model",
  field: "Derived field", consumer: "Consumer", output: "Output", control: "Control",
};

/** Longest-path layering — stable, deterministic, no simulation. */
function layout(nodes, edges, nodeW, nodeH, colGap, rowGap) {
  const index = new Map(nodes.map((n, i) => [n.id, i]));
  const layer = new Map(nodes.map((n) => [n.id, typeof n.layer === "number" ? n.layer : 0]));
  for (let pass = 0; pass < nodes.length; pass += 1) {
    let moved = false;
    edges.forEach((e) => {
      if (!index.has(e.from) || !index.has(e.to)) return;
      const want = layer.get(e.from) + 1;
      if (want > layer.get(e.to)) { layer.set(e.to, want); moved = true; }
    });
    if (!moved) break;
  }
  const cols = new Map();
  nodes.forEach((n) => {
    const l = layer.get(n.id);
    if (!cols.has(l)) cols.set(l, []);
    cols.get(l).push(n);
  });
  const maxRows = Math.max(...[...cols.values()].map((c) => c.length), 1);
  const layerKeys = [...cols.keys()];
  const pos = new Map();
  layerKeys.sort((a, b) => a - b).forEach((l) => {
    const col = cols.get(l);
    const offset = ((maxRows - col.length) * (nodeH + rowGap)) / 2;
    col.forEach((n, r) => {
      pos.set(n.id, { x: 16 + l * (nodeW + colGap), y: 16 + offset + r * (nodeH + rowGap) });
    });
  });
  const layerCount = layerKeys.length ? Math.max(...layerKeys) + 1 : 1;
  const width = 32 + layerCount * (nodeW + colGap) - colGap;
  const height = 32 + maxRows * (nodeH + rowGap) - rowGap;
  return { pos, width, height };
}

export function LineageGraph({
  nodes = [], edges = [], selectedId, onSelect, height = 320,
  nodeWidth = 152, nodeHeight = 46, columnGap = 64, rowGap = 26,
  outline = true, outlineTitle = "Accessible outline", className = "", ...rest
}) {
  inject();
  const { pos, width, height: canvasH } = layout(nodes, edges, nodeWidth, nodeHeight, columnGap, rowGap);
  const byId = new Map(nodes.map((n) => [n.id, n]));
  return (
    <div className={`mds-lin${className ? " " + className : ""}`} {...rest}>
      <div className="mds-lin__vp" style={{ height }}>
        <div className="mds-lin__canvas" style={{ width, height: Math.max(canvasH, height - 2) }}>
          <svg className="mds-lin__edges" width={width} height={Math.max(canvasH, height - 2)} aria-hidden="true">
            {edges.map((e, i) => {
              const a = pos.get(e.from), b = pos.get(e.to);
              if (!a || !b) return null;
              const x1 = a.x + nodeWidth, y1 = a.y + nodeHeight / 2;
              const x2 = b.x, y2 = b.y + nodeHeight / 2;
              const on = selectedId && (e.from === selectedId || e.to === selectedId);
              return (
                <g key={`${e.from}-${e.to}-${i}`}>
                  <path d={elbow(x1, y1, x2, y2)} fill="none"
                    style={{
                      stroke: on ? "var(--viz-select-rail)" : "var(--border-strong)",
                      strokeWidth: on ? "var(--viz-stroke-edge-selected,2px)" : "var(--viz-stroke-edge,1.25px)",
                      strokeDasharray: e.dashed ? "var(--viz-dash-flow,3 4)" : "none",
                    }} />
                  <circle cx={x2 - 1} cy={y2} r={2.4} style={{ fill: on ? "var(--viz-select-rail)" : "var(--border-strong)" }} />
                  {e.label ? (
                    <text className="mds-lin__elabel" x={(x1 + x2) / 2} y={(y1 === y2 ? y1 : (y1 + y2) / 2) - 5} textAnchor="middle">{e.label}</text>
                  ) : null}
                </g>
              );
            })}
          </svg>
          {nodes.map((n) => {
            const p = pos.get(n.id);
            if (!p) return null;
            return (
              <button
                key={n.id}
                type="button"
                className={`mds-lin__node${selectedId === n.id ? " mds-lin__node--sel" : ""}`}
                style={{ left: p.x, top: p.y, width: nodeWidth, height: nodeHeight }}
                aria-pressed={selectedId === n.id}
                onClick={() => onSelect && onSelect(n.id)}
              >
                <span className="mds-lin__nl">{n.label}</span>
                <span className="mds-lin__nm">{n.meta || KIND_LABEL[n.kind] || ""}</span>
              </button>
            );
          })}
        </div>
      </div>
      {outline ? (
        <div>
          <div style={{ fontSize: "var(--type-label,10px)", fontWeight: 600, fontVariant: "all-small-caps", letterSpacing: ".05em", color: "var(--text-muted)", marginBottom: 6 }}>{outlineTitle}</div>
          <ul className="mds-lin__out">
            {nodes.map((n) => {
              const outs = edges.filter((e) => e.from === n.id).map((e) => (byId.get(e.to) || {}).label).filter(Boolean);
              return (
                <li key={n.id}>
                  <b>{n.label}</b>
                  {n.meta ? ` — ${n.meta}` : n.kind ? ` — ${KIND_LABEL[n.kind] || n.kind}` : ""}
                  {outs.length ? ` → ${outs.join(", ")}` : ""}
                </li>
              );
            })}
          </ul>
        </div>
      ) : null}
    </div>
  );
}
