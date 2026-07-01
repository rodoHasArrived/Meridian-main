// Meridian chart-of-accounts tree — an expandable account hierarchy with roll-up balances. A
// parent with no explicit `balance` sums its descendants; parents render bold, leaves muted.
// Disclosure triangles toggle branches; depth indents the name column; balances are mono and
// right-aligned. Flat Concrete theme.
import { useState } from "react";
import type { ReactNode } from "react";
import { AmountCell } from "./AmountCell";
import { toNumber } from "./money";

export interface AccountNode {
  /** Account code/number (mono prefix). Also the expand/select key — must be unique. */
  code: string;
  name: string;
  /** Explicit balance. Omit on a parent to roll up its children. */
  balance?: number | string;
  /** Optional classification tag (asset/liability/equity/revenue/expense) — for your own use. */
  type?: string;
  children?: AccountNode[];
}

export interface AccountTreeProps {
  nodes: AccountNode[];
  /** @default "USD" */
  currency?: string;
  /** Branches at or above this depth start expanded (root = 0). @default 1 */
  defaultExpandedDepth?: number;
  /** Code of the row to highlight with the accent rail. */
  selectedCode?: string;
  /** Row click handler — makes rows selectable. */
  onSelect?: (node: AccountNode) => void;
  /** Header label over the balance column. @default "Balance" */
  valueLabel?: string;
}

let injected = false;
function inject(): void {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.act{border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-chip,2px);
  overflow:hidden;background:var(--bg-light,#FFFFFF);}
.act__head{display:grid;grid-template-columns:1fr auto;gap:12px;padding:8px 12px;
  background:var(--bg-medium,#EBEFF4);border-bottom:1px solid var(--border,#CBD3DC);
  font-family:var(--font-body,inherit);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);}
.act__head .act--r{text-align:right;}
.act__row{display:grid;grid-template-columns:1fr auto;gap:12px;align-items:center;
  padding:6px 12px;border-top:1px solid var(--border,#CBD3DC);}
.act__row:hover{background:var(--bg-hover,#F3F6F9);}
.act__row--sel{cursor:pointer;}
.act__row--on{background:var(--bg-active,#E6EEF5);box-shadow:inset 3px 0 0 var(--accent,#2F6F8F);}
.act__name{display:flex;align-items:center;gap:8px;min-width:0;
  font-family:var(--font-body,inherit);font-size:12px;color:var(--text-primary,#22272E);}
.act__name--group{font-weight:600;}
.act__tw{width:14px;height:14px;flex:0 0 auto;display:inline-flex;align-items:center;
  justify-content:center;font-size:9px;color:var(--text-muted,#59636F);
  cursor:pointer;border-radius:var(--radius-chip,2px);user-select:none;}
.act__tw:hover{background:var(--bg-active,#E6EEF5);color:var(--text-secondary,#4D5967);}
.act__tw--leaf{cursor:default;opacity:0;}
.act__code{font-family:var(--font-data,monospace);font-size:11px;color:var(--text-muted,#59636F);
  flex:0 0 auto;}
.act__label{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "accounttree");
  el.textContent = css;
  document.head.appendChild(el);
}

function rollup(node: AccountNode): number {
  if (node.children && node.children.length) {
    const explicit = toNumber(node.balance);
    if (node.balance != null && Number.isFinite(explicit)) return explicit;
    return node.children.reduce((a, c) => a + rollup(c), 0);
  }
  const own = toNumber(node.balance);
  return Number.isFinite(own) ? own : 0;
}

// Seed the expanded set with every node code down to defaultExpandedDepth (0-based root=0).
function seedExpanded(nodes: AccountNode[], depth: number, maxDepth: number, set: Set<string>): void {
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
  valueLabel = "Balance"
}: AccountTreeProps) {
  inject();
  const [expanded, setExpanded] = useState<Set<string>>(() => {
    const s = new Set<string>();
    seedExpanded(nodes, 0, defaultExpandedDepth, s);
    return s;
  });

  const toggle = (code: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(code)) next.delete(code);
      else next.add(code);
      return next;
    });

  const out: ReactNode[] = [];
  const walk = (list: AccountNode[], depth: number) => {
    for (const node of list) {
      const hasKids = !!(node.children && node.children.length);
      const open = expanded.has(node.code);
      const isGroup = hasKids;
      const selectable = !!onSelect;
      const on = selectedCode != null && selectedCode === node.code;
      out.push(
        <div
          key={node.code}
          className={`act__row${selectable ? " act__row--sel" : ""}${on ? " act__row--on" : ""}`}
          onClick={onSelect ? () => onSelect(node) : undefined}
          role={onSelect ? "button" : "treeitem"}
          aria-expanded={hasKids ? open : undefined}
          aria-selected={on || undefined}
          tabIndex={onSelect ? 0 : undefined}
        >
          <div className={`act__name${isGroup ? " act__name--group" : ""}`} style={{ paddingLeft: depth * 18 }}>
            <span
              className={`act__tw${hasKids ? "" : " act__tw--leaf"}`}
              onClick={
                hasKids
                  ? (e) => {
                      e.stopPropagation();
                      toggle(node.code);
                    }
                  : undefined
              }
              aria-hidden={!hasKids}
            >
              {hasKids ? (open ? "▾" : "▸") : "•"}
            </span>
            {node.code && <span className="act__code">{node.code}</span>}
            <span className="act__label">{node.name}</span>
          </div>
          <AmountCell
            value={rollup(node)}
            currency={currency}
            parens
            strong={isGroup}
            style={isGroup ? undefined : { color: "var(--text-secondary, #4D5967)" }}
          />
        </div>
      );
      if (hasKids && open) walk(node.children!, depth + 1);
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

AccountTree.displayName = "AccountTree";
