// Meridian chart-of-accounts tree — an expandable account hierarchy with roll-up balances. A
// parent with no explicit `balance` sums its descendants; parents render bold, leaves muted.
// Disclosure triangles toggle branches; depth indents the name column; balances are mono and
// right-aligned. Flat Concrete theme.
// ARIA tree: role=tree/treeitem with aria-expanded/level/selected and a roving tabindex, so every
// visible row is reachable by keyboard (Up/Down, Right expand-then-descend, Left
// collapse-then-ascend, Home/End, Enter/Space select or toggle).
import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent } from "react";
import { AmountCell } from "./AmountCell";
import { toNumber } from "./money";

export interface AccountNode {
  /** Account code/number. The expand/select key — must be unique. */
  code: string;
  /**
   * What to render as the mono prefix, when the unique key is not itself readable as an account
   * code. A ledger returns one row per account per dimension set, so the same GL account arrives
   * more than once and the account number alone cannot be the key.
   */
  codeLabel?: string;
  name: string;
  /** Explicit balance. Omit on a parent to roll up its children. */
  balance?: number | string;
  /** Optional classification tag (asset/liability/equity/revenue/expense) — for your own use. */
  type?: string;
  children?: AccountNode[];
}

export interface AccountTreeProps {
  nodes?: AccountNode[];
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
.act{border:1px solid var(--border,#D2CDC3);border-radius:var(--radius-chip,2px);
  overflow:hidden;background:var(--bg-light,#F8F7F3);}
.act__head{display:grid;grid-template-columns:1fr auto;gap:12px;padding:8px 12px;
  background:var(--bg-medium,#EBE8E1);border-bottom:1px solid var(--border,#D2CDC3);
  font-family:var(--font-body,inherit);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#606870);}
.act__head .act--r{text-align:right;}
.act__row{display:grid;grid-template-columns:1fr auto;gap:12px;align-items:center;
  padding:6px 12px;border-top:1px solid var(--border,#D2CDC3);}
.act__row:hover{background:var(--bg-hover,#EDEFF0);}
.act__row--sel{cursor:pointer;}
.act__row--on{background:var(--bg-active,#E7EDF2);box-shadow:inset 3px 0 0 var(--interaction-selected,#496C8F);}
.act__name{display:flex;align-items:center;gap:8px;min-width:0;
  font-family:var(--font-body,inherit);font-size:12px;color:var(--text-primary,#20252B);}
.act__name--group{font-weight:600;}
.act__tw{width:14px;height:14px;flex:0 0 auto;display:inline-flex;align-items:center;
  justify-content:center;font-size:9px;color:var(--text-muted,#606870);
  cursor:pointer;border-radius:var(--radius-chip,2px);user-select:none;}
.act__tw:focus-visible,.act__row:focus-visible{outline:2px solid var(--interaction-focus, #2D6FB3);outline-offset:1px;}
.act__tw:hover{background:var(--bg-active,#E7EDF2);color:var(--text-secondary,#535C65);}
.act__tw--leaf{cursor:default;opacity:0;}
.act__code{font-family:var(--font-data,monospace);font-size:11px;color:var(--text-muted,#606870);
  flex:0 0 auto;}
.act__label{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.act__empty{padding:16px 12px;font-family:var(--font-body,inherit);font-size:12px;color:var(--text-muted,#606870);text-align:center;}
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

interface VisibleRow {
  node: AccountNode;
  depth: number;
  hasKids: boolean;
  isOpen: boolean;
  parentCode: string | null;
}

// Flatten the currently visible rows, in document order, for roving keyboard navigation.
function flatten(
  nodes: AccountNode[],
  expanded: Set<string>,
  depth = 0,
  parentCode: string | null = null,
  out: VisibleRow[] = []
): VisibleRow[] {
  for (const node of nodes) {
    const hasKids = !!(node.children && node.children.length);
    const isOpen = expanded.has(node.code);
    out.push({ node, depth, hasKids, isOpen, parentCode });
    if (hasKids && isOpen) flatten(node.children!, expanded, depth + 1, node.code, out);
  }
  return out;
}

export function AccountTree({
  nodes = [],
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

  const rows = flatten(nodes, expanded);
  const firstCode = rows.length ? rows[0].node.code : null;
  const [focusedCode, setFocusedCode] = useState<string | null>(selectedCode ?? firstCode);
  const rowRefs = useRef(new Map<string, HTMLDivElement>());

  // Exactly one visible row holds the tab stop. When the focused row leaves the visible set (its
  // ancestor collapsed, or the nodes changed) the stop falls back to the first row.
  const focusedVisible = focusedCode != null && rows.some((row) => row.node.code === focusedCode);
  const tabStopCode = focusedVisible ? focusedCode : firstCode;
  useEffect(() => {
    if (!focusedVisible && focusedCode !== firstCode) setFocusedCode(firstCode);
  }, [focusedVisible, focusedCode, firstCode]);

  const toggle = (code: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(code)) next.delete(code);
      else next.add(code);
      return next;
    });

  const focusCode = (code: string) => {
    setFocusedCode(code);
    rowRefs.current.get(code)?.focus();
  };

  const onRowKeyDown = (e: KeyboardEvent<HTMLDivElement>, row: VisibleRow, idx: number) => {
    const { node, hasKids, isOpen, parentCode } = row;
    switch (e.key) {
      case "ArrowDown":
        e.preventDefault();
        if (idx < rows.length - 1) focusCode(rows[idx + 1].node.code);
        break;
      case "ArrowUp":
        e.preventDefault();
        if (idx > 0) focusCode(rows[idx - 1].node.code);
        break;
      case "ArrowRight":
        e.preventDefault();
        if (hasKids && !isOpen) toggle(node.code);
        else if (hasKids && isOpen) focusCode(node.children![0].code);
        break;
      case "ArrowLeft":
        e.preventDefault();
        if (hasKids && isOpen) toggle(node.code);
        else if (parentCode != null) focusCode(parentCode);
        break;
      case "Home":
        e.preventDefault();
        if (rows.length) focusCode(rows[0].node.code);
        break;
      case "End":
        e.preventDefault();
        if (rows.length) focusCode(rows[rows.length - 1].node.code);
        break;
      case "Enter":
      case " ":
        e.preventDefault();
        if (onSelect) onSelect(node);
        else if (hasKids) toggle(node.code);
        break;
      default:
        break;
    }
  };

  return (
    <div className="act">
      <div className="act__head" aria-hidden="true">
        <div>Account</div>
        <div className="act--r">{valueLabel}</div>
      </div>
      <div role="tree" aria-label="Chart of accounts">
        {rows.length === 0 && <div className="act__empty">No accounts</div>}
        {rows.map((row, idx) => {
          const { node, depth, hasKids, isOpen } = row;
          const isGroup = hasKids;
          const selectable = !!onSelect;
          const on = selectedCode != null && selectedCode === node.code;
          const onToggleKeyDown = (e: KeyboardEvent<HTMLButtonElement>) => {
            if (e.key === "Enter" || e.key === " ") {
              e.preventDefault();
              e.stopPropagation();
              toggle(node.code);
            }
          };
          return (
            <div
              key={node.code}
              className={`act__row${selectable ? " act__row--sel" : ""}${on ? " act__row--on" : ""}`}
              onClick={() => {
                setFocusedCode(node.code);
                onSelect?.(node);
              }}
              onKeyDown={(e) => onRowKeyDown(e, row, idx)}
              role="treeitem"
              aria-expanded={hasKids ? isOpen : undefined}
              aria-selected={on || undefined}
              aria-level={depth + 1}
              tabIndex={node.code === tabStopCode ? 0 : -1}
              ref={(el) => {
                if (el) rowRefs.current.set(node.code, el);
                else rowRefs.current.delete(node.code);
              }}
            >
              <div className={`act__name${isGroup ? " act__name--group" : ""}`} style={{ paddingLeft: depth * 18 }}>
                {hasKids ? (
                  <button
                    type="button"
                    className="act__tw"
                    onClick={(e) => {
                      e.stopPropagation();
                      toggle(node.code);
                    }}
                    onKeyDown={onToggleKeyDown}
                    aria-label={isOpen ? `Collapse ${node.name}` : `Expand ${node.name}`}
                    aria-expanded={isOpen}
                  >
                    {isOpen ? "▾" : "▸"}
                  </button>
                ) : (
                  <span className="act__tw act__tw--leaf" aria-hidden="true">
                    •
                  </span>
                )}
                {(node.codeLabel ?? node.code) && <><span className="act__code">{node.codeLabel ?? node.code}</span>{" "}</>}
                <span className="act__label">{node.name}</span>
              </div>
              <AmountCell
                value={rollup(node)}
                currency={currency}
                parens
                strong={isGroup}
                style={isGroup ? undefined : { color: "var(--text-secondary, #535C65)" }}
              />
            </div>
          );
        })}
      </div>
    </div>
  );
}

AccountTree.displayName = "AccountTree";
