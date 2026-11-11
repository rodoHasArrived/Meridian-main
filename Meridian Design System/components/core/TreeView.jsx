// Meridian TreeView — expandable hierarchy (chart of accounts, security taxonomies, file trees).
// Controlled or uncontrolled expansion. Functional only: no radius, no shadows.
// Full ARIA tree model on a flat DOM: roving tabindex, aria-level/setsize/posinset, Up/Down move,
// Right expand-then-descend, Left collapse-then-ascend, Home/End, "*" expand siblings, printable
// typeahead, Enter select, Space toggle. Disabled nodes are skipped by every motion.
// Row height tracks the live density scope (--theme-row-height: terminal 26 / compact 28 / standard 36 / spacious 44).
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-tree{font-family:var(--font-data);font-size:13px;color:var(--text-primary,#20252B);user-select:none;position:relative;}
.mds-tree:focus-visible{outline:var(--focus-ring);outline-offset:2px;}
.mds-tree-row{display:flex;align-items:center;gap:6px;height:var(--tree-row-height,var(--theme-row-height,36px));padding:0 8px;cursor:pointer;border-left:2px solid transparent;position:relative;}
.mds-tree-row:hover{background:var(--bg-hover,#EDEFF0);}
.mds-tree-row--sel{background:var(--bg-active,#E7EDF2);border-left-color:var(--interaction-selected,#496C8F);}
.mds-tree-row--sel .mds-tree-label{font-weight:600;}
.mds-tree-row:focus-visible{outline:var(--focus-ring);outline-offset:-2px;z-index:1;}
.mds-tree-row--disabled{cursor:default;color:var(--text-disabled,#A4ABB2);}
.mds-tree-row--disabled:hover{background:transparent;}
.mds-tree-row--disabled .mds-tree-meta,.mds-tree-row--disabled .mds-tree-icon{color:var(--text-disabled,#A4ABB2);}
.mds-tree-guides{position:absolute;top:0;bottom:0;left:0;display:flex;pointer-events:none;}
.mds-tree-guide{width:16px;flex:0 0 16px;border-right:1px solid var(--border-divider,#E2DED5);}
.mds-tree-caret{width:14px;height:100%;display:inline-flex;align-items:center;justify-content:center;color:var(--text-muted,#606870);font-size:10px;flex:0 0 auto;background:none;border:0;padding:0;margin:0;font:inherit;cursor:pointer;}
.mds-tree-caret:hover{color:var(--text-primary,#20252B);}
.mds-tree-caret:focus-visible{outline:var(--focus-ring);outline-offset:-1px;}
.mds-tree-caret--leaf{visibility:hidden;cursor:default;}
.mds-tree-label{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.mds-tree-meta{font-family:var(--font-data);font-size:11px;color:var(--text-muted,#606870);font-variant-numeric:tabular-nums;flex:0 0 auto;}
.mds-tree-icon{flex:0 0 auto;color:var(--text-secondary,#535C65);font-size:12px;display:inline-flex;}
.mds-tree-count{font-family:var(--font-data);font-size:10px;color:var(--text-muted,#606870);font-variant-numeric:tabular-nums;flex:0 0 auto;}
.mds-tree-empty{padding:12px;font-family:var(--font-body);font-size:13px;color:var(--text-muted,#606870);}
.mds-tree-loading{font-family:var(--font-data);font-size:10px;color:var(--text-muted,#606870);letter-spacing:.04em;flex:0 0 auto;}
.mds-tree-row--sel .mds-tree-meta{color:var(--text-secondary,#535C65);}
@media print{.mds-tree-row{height:auto;min-height:20px;}.mds-tree-row--sel{background:transparent;}}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "treeview");
  el.textContent = css;
  document.head.appendChild(el);
}

const branchy = (node) => (node.hasChildren != null ? !!node.hasChildren : !!(node.children && node.children.length));

// Flatten currently-visible nodes into a linear list for roving keyboard navigation. Each entry
// carries depth, branch/open flags, parent id, and its 1-based position within its own sibling set
// — a flat DOM tree is only announceable if every item states level, setsize and posinset.
function flatten(items, expanded, depth = 0, parentId = null, out = []) {
  const setsize = items.length;
  items.forEach((node, i) => {
    const isBranch = branchy(node);
    const isOpen = isBranch && expanded.has(node.id);
    out.push({ node, depth, isBranch, isOpen, parentId, setsize, posinset: i + 1, disabled: !!node.disabled });
    if (isOpen && node.children && node.children.length) flatten(node.children, expanded, depth + 1, node.id, out);
  });
  return out;
}

// Typeahead needs a string; labels may be arbitrary nodes, so consumers can pass `text`.
const searchText = (node) => String(node.text ?? (typeof node.label === "string" ? node.label : "")).toLowerCase();

export function TreeView({
  items = [],
  defaultExpanded = [],
  expanded: expandedProp,
  onExpandedChange,
  selectedId,
  onSelect,
  onToggle,
  guides = true,
  emptyLabel = "No items",
  "aria-label": ariaLabel,
  ...rest
}) {
  inject();
  const isControlled = expandedProp != null;
  const [uncontrolled, setUncontrolled] = React.useState(() => new Set(defaultExpanded));
  const expandedSet = React.useMemo(
    () => (isControlled ? new Set(expandedProp) : uncontrolled),
    [isControlled, expandedProp, uncontrolled]
  );

  const rows = React.useMemo(() => flatten(items, expandedSet), [items, expandedSet]);
  const [focusedId, setFocusedId] = React.useState(null);
  const rowRefs = React.useRef({});
  const typeahead = React.useRef({ buffer: "", at: 0 });

  const firstEnabled = rows.find((r) => !r.disabled);
  // Keep the roving focus target valid — an ancestor may have collapsed under it.
  const activeId = rows.some((r) => r.node.id === focusedId && !r.disabled)
    ? focusedId
    : (rows.some((r) => r.node.id === selectedId && !r.disabled) ? selectedId : firstEnabled?.node.id ?? null);

  const commitExpanded = (next) => {
    if (!isControlled) setUncontrolled(next);
    onExpandedChange?.([...next]);
  };

  const setOpen = (node, open) => {
    const next = new Set(expandedSet);
    open ? next.add(node.id) : next.delete(node.id);
    commitExpanded(next);
    onToggle?.(node, open);
  };
  const toggle = (row) => setOpen(row.node, !row.isOpen);

  const focusId = (id) => {
    if (id == null) return;
    setFocusedId(id);
    rowRefs.current[id]?.focus();
  };
  // Move by n visible rows, skipping disabled ones.
  const step = (idx, dir) => {
    for (let i = idx + dir; i >= 0 && i < rows.length; i += dir) if (!rows[i].disabled) return rows[i].node.id;
    return null;
  };
  const edge = (dir) => {
    const list = dir > 0 ? rows : [...rows].reverse();
    return list.find((r) => !r.disabled)?.node.id ?? null;
  };

  const select = (row) => { if (!row.disabled) onSelect?.(row.node); };

  const onKeyDown = (e, row, idx) => {
    const { node, isBranch, isOpen, parentId } = row;
    const k = e.key;
    if (k === "ArrowDown") { e.preventDefault(); focusId(step(idx, 1)); return; }
    if (k === "ArrowUp") { e.preventDefault(); focusId(step(idx, -1)); return; }
    if (k === "ArrowRight") {
      e.preventDefault();
      if (isBranch && !isOpen) setOpen(node, true);
      else if (isOpen) focusId(step(idx, 1));
      return;
    }
    if (k === "ArrowLeft") {
      e.preventDefault();
      if (isBranch && isOpen) setOpen(node, false);
      else if (parentId != null) focusId(parentId);
      return;
    }
    if (k === "Home") { e.preventDefault(); focusId(edge(1)); return; }
    if (k === "End") { e.preventDefault(); focusId(edge(-1)); return; }
    if (k === "Enter") { e.preventDefault(); select(row); return; }
    if (k === " ") {
      e.preventDefault();
      if (isBranch) toggle(row); else select(row);
      return;
    }
    // "*" — expand every sibling at this level (ARIA APG tree pattern).
    if (k === "*") {
      e.preventDefault();
      const next = new Set(expandedSet);
      rows.filter((r) => r.parentId === parentId && r.isBranch).forEach((r) => next.add(r.node.id));
      commitExpanded(next);
      return;
    }
    // Printable typeahead — jump to the next row whose label starts with the typed run.
    if (k.length === 1 && !e.metaKey && !e.ctrlKey && !e.altKey) {
      const now = Date.now();
      const ta = typeahead.current;
      ta.buffer = now - ta.at > 700 ? k.toLowerCase() : ta.buffer + k.toLowerCase();
      ta.at = now;
      const order = [...rows.slice(idx + 1), ...rows.slice(0, idx + 1)];
      const hit = order.find((r) => !r.disabled && searchText(r.node).startsWith(ta.buffer));
      if (hit) { e.preventDefault(); focusId(hit.node.id); }
    }
  };

  if (!rows.length) return <div className="mds-tree" {...rest}><div className="mds-tree-empty">{emptyLabel}</div></div>;

  return (
    <div className="mds-tree" role="tree" aria-label={ariaLabel} {...rest}>
      {rows.map((row, idx) => {
        const { node, depth, isBranch, isOpen, setsize, posinset, disabled } = row;
        const isSel = node.id === selectedId;
        const childCount = node.children ? node.children.length : node.childCount;
        return (
          <div
            key={node.id}
            role="treeitem"
            aria-expanded={isBranch ? isOpen : undefined}
            aria-selected={isSel}
            aria-level={depth + 1}
            aria-setsize={setsize}
            aria-posinset={posinset}
            aria-disabled={disabled || undefined}
            aria-busy={node.loading || undefined}
            tabIndex={node.id === activeId ? 0 : -1}
            ref={(el) => { if (el) rowRefs.current[node.id] = el; else delete rowRefs.current[node.id]; }}
            className={`mds-tree-row${isSel ? " mds-tree-row--sel" : ""}${disabled ? " mds-tree-row--disabled" : ""}`}
            style={{ paddingLeft: 8 + depth * 16 }}
            onClick={() => { if (disabled) return; setFocusedId(node.id); select(row); }}
            onDoubleClick={() => { if (!disabled && isBranch) toggle(row); }}
            onKeyDown={(e) => onKeyDown(e, row, idx)}
          >
            {guides && depth > 0 && (
              <span className="mds-tree-guides" aria-hidden="true">
                {Array.from({ length: depth }, (_, i) => <span key={i} className="mds-tree-guide" style={{ marginLeft: i === 0 ? 14 : 0 }} />)}
              </span>
            )}
            <span
              role="presentation"
              className={`mds-tree-caret${isBranch ? "" : " mds-tree-caret--leaf"}`}
              onClick={(e) => { e.stopPropagation(); if (!disabled && isBranch) toggle(row); }}
              aria-hidden="true"
            >
              {isBranch ? (isOpen ? "▾" : "▸") : "·"}
            </span>
            {node.icon && <span className="mds-tree-icon" aria-hidden="true">{node.icon}</span>}
            <span className="mds-tree-label">{node.label}</span>
            {node.loading && <span className="mds-tree-loading">LOADING</span>}
            {!node.loading && isBranch && !isOpen && childCount != null && (
              <span className="mds-tree-count" aria-hidden="true">{childCount}</span>
            )}
            {node.meta != null && <span className="mds-tree-meta">{node.meta}</span>}
          </div>
        );
      })}
    </div>
  );
}

TreeView.displayName = "TreeView";
