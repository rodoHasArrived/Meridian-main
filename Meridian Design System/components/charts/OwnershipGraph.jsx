// Meridian ownership graph — entity/control structure diagram mirroring
// `FamilyOwnershipGraphDto` (nodes + edges with relationship type and ownership %).
// Layered top-down layout: roots (no incoming edge) on top, each node one level below
// its deepest parent. Nodes are HTML chips; edges are measured SVG curves with a mono
// ownership-% label. Re-measures on resize. Selection is controlled and optional.
import React, { useLayoutEffect, useRef, useState } from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-own{position:relative;padding:18px 12px;overflow:auto;}
.mds-own__svg{position:absolute;inset:0;width:100%;height:100%;pointer-events:none;overflow:visible;}
.mds-own__edge{fill:none;stroke:var(--border-strong,#B2BAC3);stroke-width:1.25;}
.mds-own__pct{font-family:var(--font-data,"Cascadia Mono",monospace);font-size:9px;font-weight:700;
  fill:var(--text-muted,#6E7781);}
.mds-own__pctbg{fill:var(--bg,#F5F7FA);}
.mds-own__row{position:relative;display:flex;justify-content:center;align-items:stretch;gap:20px;}
.mds-own__row + .mds-own__row{margin-top:56px;}
.mds-own__node{position:relative;z-index:1;display:flex;flex-direction:column;gap:3px;min-width:132px;max-width:200px;
  padding:8px 11px;border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-card,6px);
  background:var(--bg-light,#FFFFFF);box-shadow:var(--shadow-raised,0 1px 2px rgba(26,32,39,.06));
  text-align:left;cursor:default;font-family:var(--font-body,"Segoe UI Variable Text",sans-serif);}
.mds-own__node--click{cursor:pointer;}
.mds-own__node--click:hover{border-color:var(--border-strong,#B2BAC3);background:var(--bg-hover,#F0F2F5);}
.mds-own__node:focus-visible{outline:2px solid var(--focus-ring,#2F6F8F);outline-offset:2px;}
.mds-own__node--sel{outline:2px solid var(--accent,#2F6F8F);outline-offset:-1px;border-color:var(--accent,#2F6F8F);}
.mds-own__type{font-size:8.5px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;
  color:var(--text-muted,#6E7781);font-family:var(--font-data,"Cascadia Mono",monospace);}
.mds-own__name{font-size:12px;font-weight:600;color:var(--text-primary,#1A2027);line-height:1.25;}
.mds-own__meta{font-size:9.5px;color:var(--text-muted,#6E7781);
  font-family:var(--font-data,"Cascadia Mono",monospace);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "ownership-graph");
  el.textContent = css;
  document.head.appendChild(el);
}

function norm(nodes, edges) {
  const N = (nodes || []).map((n) => ({
    id: n.id ?? n.nodeId,
    label: n.label ?? n.displayName ?? String(n.id ?? n.nodeId),
    type: n.type ?? n.nodeType ?? "",
    jurisdiction: n.jurisdiction ?? null,
    currency: n.currency ?? null,
  }));
  const E = (edges || []).map((e, i) => ({
    id: e.id ?? e.edgeId ?? `e${i}`,
    from: e.from ?? e.sourceNodeId,
    to: e.to ?? e.targetNodeId,
    relationship: e.relationship ?? e.relationshipType ?? "",
    percent: e.percent ?? e.ownershipPercent ?? null,
  }));
  return { N, E };
}

/** Longest-path layering with cycle guard: level = 1 + max(parent levels). */
function layer(N, E) {
  const levels = {};
  const parents = {};
  N.forEach((n) => { parents[n.id] = []; });
  E.forEach((e) => { if (parents[e.to]) parents[e.to].push(e.from); });
  const visiting = new Set();
  const depth = (id) => {
    if (levels[id] != null) return levels[id];
    if (visiting.has(id)) return 0; // cycle guard
    visiting.add(id);
    const ps = parents[id] || [];
    const d = ps.length ? 1 + Math.max(...ps.map(depth)) : 0;
    visiting.delete(id);
    levels[id] = d;
    return d;
  };
  N.forEach((n) => depth(n.id));
  const rows = [];
  N.forEach((n) => {
    const d = levels[n.id] || 0;
    (rows[d] = rows[d] || []).push(n);
  });
  return rows.filter(Boolean);
}

export function OwnershipGraph({
  nodes = [],
  edges = [],
  selectedId,
  onSelectNode,
  className = "",
  ...rest
}) {
  inject();
  const wrapRef = useRef(null);
  const nodeRefs = useRef({});
  const [paths, setPaths] = useState([]);

  const { N, E } = norm(nodes, edges);
  const rows = layer(N, E);

  useLayoutEffect(() => {
    const wrap = wrapRef.current;
    if (!wrap) return;
    const measure = () => {
      const box = wrap.getBoundingClientRect();
      const next = [];
      E.forEach((e) => {
        const a = nodeRefs.current[e.from];
        const b = nodeRefs.current[e.to];
        if (!a || !b) return;
        const ra = a.getBoundingClientRect();
        const rb = b.getBoundingClientRect();
        const x1 = ra.left - box.left + ra.width / 2 + wrap.scrollLeft;
        const y1 = ra.bottom - box.top + wrap.scrollTop;
        const x2 = rb.left - box.left + rb.width / 2 + wrap.scrollLeft;
        const y2 = rb.top - box.top + wrap.scrollTop;
        const bend = Math.max(18, (y2 - y1) / 2);
        next.push({
          id: e.id,
          d: `M ${x1} ${y1} C ${x1} ${y1 + bend}, ${x2} ${y2 - bend}, ${x2} ${y2}`,
          lx: (x1 + x2) / 2,
          ly: (y1 + y2) / 2,
          label: e.percent != null ? `${e.percent}%` : "",
          title: [e.relationship, e.percent != null ? `${e.percent}%` : null].filter(Boolean).join(" · "),
        });
      });
      setPaths(next);
    };
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(wrap);
    return () => ro.disconnect();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [JSON.stringify(nodes), JSON.stringify(edges)]);

  const clickable = !!onSelectNode;
  return (
    <div className={`mds-own${className ? " " + className : ""}`} ref={wrapRef} {...rest}>
      <svg className="mds-own__svg" aria-hidden="true">
        {paths.map((p) => (
          <g key={p.id}>
            <path className="mds-own__edge" d={p.d} />
            {p.label ? (
              <g>
                <rect className="mds-own__pctbg" x={p.lx - 17} y={p.ly - 8} width="34" height="14" rx="2" />
                <text className="mds-own__pct" x={p.lx} y={p.ly + 3} textAnchor="middle">{p.label}</text>
              </g>
            ) : null}
          </g>
        ))}
      </svg>
      {rows.map((row, i) => (
        <div className="mds-own__row" key={i}>
          {row.map((n) => {
            const Tag = clickable ? "button" : "div";
            const meta = [n.jurisdiction, n.currency].filter(Boolean).join(" · ");
            return (
              <Tag
                key={n.id}
                {...(clickable ? { type: "button", onClick: () => onSelectNode(n.id, n) } : {})}
                ref={(el) => { nodeRefs.current[n.id] = el; }}
                className={`mds-own__node${clickable ? " mds-own__node--click" : ""}${n.id === selectedId ? " mds-own__node--sel" : ""}`}
              >
                {n.type ? <span className="mds-own__type">{n.type}</span> : null}
                <span className="mds-own__name">{n.label}</span>
                {meta ? <span className="mds-own__meta">{meta}</span> : null}
              </Tag>
            );
          })}
        </div>
      ))}
    </div>
  );
}
