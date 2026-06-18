// Meridian chart-of-accounts tree — expandable account hierarchy with roll-up balances.
// Parent rows sum their children unless they carry an explicit balance. Disclosure triangles,
// depth indentation, mono account codes, right-aligned tabular balances. Light theme.
import React, { useState } from "react";
import { AmountCell } from "./AmountCell";
import { toNumber } from "./money";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.act{border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-chip,4px);
  overflow:hidden;background:var(--bg-light,#fff);}
.act__head{display:grid;grid-template-columns:1fr auto;gap:12px;padding:8px 12px;
  background:var(--bg-medium,#F5F7FA);border-bottom:1px solid var(--border,#D7DCE2);
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#6E7781);}
.act__head .act--r{text-align:right;}
.act__row{display:grid;grid-template-columns:1fr auto;gap:12px;align-items:center;
  padding:6px 12px;border-top:1px solid var(--border,#D7DCE2);}
.act__row:hover{background:var(--bg-hover,#F1F4F7);}
.act__row--sel{cursor:pointer;}
.act__row--on{background:var(--bg-active,#E6EEF5);box-shadow:inset 3px 0 0 var(--accent,#2F6F8F);}
.act__name{display:flex;align-items:center;gap:8px;min-width:0;
  font-family:var(--font-body);font-size:12px;color:var(--text-primary,#22272E);}
.act__name--group{font-weight:600;}
.act__tw{width:14px;height:14px;flex:0 0 auto;display:inline-flex;align-items:center;
  justify-content:center;font-size:9px;color:var(--text-muted,#6E7781);
  cursor:pointer;border-radius:3px;user-select:none;}
.act__tw:hover{background:var(--bg-active,#E6EEF5);color:var(--text-secondary,#4D5967);}
.act__tw--leaf{cursor:default;opacity:0;}
.act__code{font-family:var(--font-data);font-size:11px;color:var(--text-muted,#6E7781);
  flex:0 0 auto;}
.act__label{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "accounttree");
  el.textContent = css;
  document.head.appendChild(el);
}

function rollup(node) {
  if (node.children && node.children.length) {
    const own = node.balance != null && isFinite(toNumber(node.balance));
    if (own) return toNumber(node.balance);
    return node.children.reduce((a, c) => a + rollup(c), 0);
  }
  return isFinite(toNumber(node.balance)) ? toNumber(node.balance) : 0;
}

// Seed the expanded set with every node code down to defaultExpandedDepth (0-based root=0).
function seedExpanded(nodes, depth, maxDepth, set) {
  for (const n of nodes) {
    if (n.children && n.children.length) {
      if (depth < maxDepth) set.add(n.code);
      seedExpanded(n.children, depth + 1, maxDepth, set);
    }
  }
}

export function AccountTree({
  nodes,
  currency = "USD",
  defaultExpandedDepth = 1,
  selectedCode,
  onSelect,
  valueLabel = "Balance",
}) {
  inject();
  const [expanded, setExpanded] = useState(() => {
    const s = new Set();
    seedExpanded(nodes, 0, defaultExpandedDepth, s);
    return s;
  });

  const toggle = (code) =>
    setExpanded((prev) => {
      const n = new Set(prev);
      n.has(code) ? n.delete(code) : n.add(code);
      return n;
    });

  const out = [];
  const walk = (list, depth) => {
    for (const node of list) {
      const hasKids = !!(node.children && node.children.length);
      const open = expanded.has(node.code);
      const isGroup = hasKids;
      const sel = !!onSelect;
      const on = selectedCode != null && selectedCode === node.code;
      out.push(
        <div
          key={node.code}
          className={`act__row${sel ? " act__row--sel" : ""}${on ? " act__row--on" : ""}`}
          onClick={onSelect ? () => onSelect(node) : undefined}
          role={onSelect ? "button" : undefined}
          tabIndex={onSelect ? 0 : undefined}
        >
          <div className={`act__name${isGroup ? " act__name--group" : ""}`} style={{ paddingLeft: depth * 18 }}>
            <span
              className={`act__tw${hasKids ? "" : " act__tw--leaf"}`}
              onClick={hasKids ? (e) => { e.stopPropagation(); toggle(node.code); } : undefined}
              aria-hidden={!hasKids}
            >
              {hasKids ? (open ? "\u25BE" : "\u25B8") : "\u2022"}
            </span>
            {node.code && <span className="act__code">{node.code}</span>}
            <span className="act__label">{node.name}</span>
          </div>
          <AmountCell
            value={rollup(node)}
            currency={currency}
            parens
            strong={isGroup}
            style={isGroup ? undefined : { color: "var(--text-secondary,#4D5967)" }}
          />
        </div>
      );
      if (hasKids && open) walk(node.children, depth + 1);
    }
  };
  walk(nodes, 0);

  return (
    <div className="act" role="tree" aria-label="Chart of accounts">
      <div className="act__head">
        <div>Account</div>
        <div className="act--r">{valueLabel}</div>
      </div>
      {out}
    </div>
  );
}
