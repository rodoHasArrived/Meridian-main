// Meridian TreeView — expandable hierarchical list (accounts, file trees, taxonomies).
// Controlled or uncontrolled expansion. Functional only: no radius, no shadows.
// Full ARIA tree keyboard model: roving tabindex + Up/Down move, Right expand-then-descend,
// Left collapse-then-ascend, Home/End, Enter/Space select.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-tree{font-family:var(--font-data);font-size:13px;color:var(--text-primary,#22272E);user-select:none;}
.mds-tree-row{display:flex;align-items:center;gap:6px;height:30px;padding:0 8px;cursor:pointer;
  border-left:2px solid transparent;}
.mds-tree-row:hover{background:var(--bg-hover,#F1F4F7);}
.mds-tree-row--sel{background:var(--bg-active,#E1EAF2);border-left-color:var(--accent,#2F6F8F);}
.mds-tree-row:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.mds-tree-caret{width:14px;display:inline-flex;align-items:center;justify-content:center;
  color:var(--text-muted,#59636F);font-size:10px;flex:0 0 auto;}
.mds-tree-caret--leaf{visibility:hidden;}
.mds-tree-label{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.mds-tree-meta{font-family:var(--font-data);font-size:11px;color:var(--text-muted,#59636F);
  font-variant-numeric:tabular-nums;flex:0 0 auto;}
.mds-tree-icon{flex:0 0 auto;color:var(--text-secondary,#4D5967);font-size:12px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "treeview");
  el.textContent = css;
  document.head.appendChild(el);
}

// Flatten the currently-visible nodes (respecting expansion) into a linear list for roving
// keyboard navigation. Each entry carries depth, child/open flags, and its parent id.
function flatten(items, expanded, depth = 0, parentId = null, out = []) {
  for (const node of items) {
    const hasChildren = !!(node.children && node.children.length > 0);
    const isOpen = expanded.has(node.id);
    out.push({ node, depth, hasChildren, isOpen, parentId });
    if (hasChildren && isOpen) flatten(node.children, expanded, depth + 1, node.id, out);
  }
  return out;
}

export function TreeView({ items = [], defaultExpanded = [], selectedId, onSelect }) {
  inject();
  const [expanded, setExpanded] = React.useState(() => new Set(defaultExpanded));
  const rows = flatten(items, expanded);
  const firstId = rows.length ? rows[0].node.id : null;
  const [focusedId, setFocusedId] = React.useState(selectedId ?? firstId);
  const rowRefs = React.useRef({});

  // Keep the roving focus target valid when its row scrolls out of the visible set
  // (e.g. an ancestor collapsed) — fall back to the first row.
  React.useEffect(() => {
    if (focusedId != null && !rows.some((r) => r.node.id === focusedId)) {
      setFocusedId(rows.length ? rows[0].node.id : null);
    }
  }, [rows, focusedId]);

  const toggle = (id) => setExpanded((prev) => {
    const next = new Set(prev);
    next.has(id) ? next.delete(id) : next.add(id);
    return next;
  });

  const focusId = (id) => { setFocusedId(id); rowRefs.current[id] && rowRefs.current[id].focus(); };

  const onKeyDown = (e, row, idx) => {
    const { node, hasChildren, isOpen, parentId } = row;
    switch (e.key) {
      case "ArrowDown":
        e.preventDefault();
        if (idx < rows.length - 1) focusId(rows[idx + 1].node.id);
        break;
      case "ArrowUp":
        e.preventDefault();
        if (idx > 0) focusId(rows[idx - 1].node.id);
        break;
      case "ArrowRight":
        e.preventDefault();
        if (hasChildren && !isOpen) toggle(node.id);
        else if (hasChildren && isOpen) focusId(node.children[0].id);
        break;
      case "ArrowLeft":
        e.preventDefault();
        if (hasChildren && isOpen) toggle(node.id);
        else if (parentId != null) focusId(parentId);
        break;
      case "Home":
        e.preventDefault();
        if (rows.length) focusId(rows[0].node.id);
        break;
      case "End":
        e.preventDefault();
        if (rows.length) focusId(rows[rows.length - 1].node.id);
        break;
      case "Enter":
      case " ":
        e.preventDefault();
        onSelect?.(node);
        if (hasChildren) toggle(node.id);
        break;
      default:
        break;
    }
  };

  return (
    <div className="mds-tree" role="tree">
      {rows.map((row, idx) => {
        const { node, depth, hasChildren, isOpen } = row;
        const isSel = node.id === selectedId;
        return (
          <div
            key={node.id}
            role="treeitem"
            aria-expanded={hasChildren ? isOpen : undefined}
            aria-selected={isSel}
            aria-level={depth + 1}
            tabIndex={node.id === focusedId ? 0 : -1}
            ref={(el) => { if (el) rowRefs.current[node.id] = el; else delete rowRefs.current[node.id]; }}
            className={`mds-tree-row${isSel ? " mds-tree-row--sel" : ""}`}
            style={{ paddingLeft: 8 + depth * 16 }}
            onClick={() => { setFocusedId(node.id); onSelect?.(node); if (hasChildren) toggle(node.id); }}
            onKeyDown={(e) => onKeyDown(e, row, idx)}
          >
            <span className={`mds-tree-caret${hasChildren ? "" : " mds-tree-caret--leaf"}`} aria-hidden="true">
              {hasChildren ? (isOpen ? "▾" : "▸") : "·"}
            </span>
            {node.icon && <span className="mds-tree-icon" aria-hidden="true">{node.icon}</span>}
            <span className="mds-tree-label">{node.label}</span>
            {node.meta != null && <span className="mds-tree-meta">{node.meta}</span>}
          </div>
        );
      })}
    </div>
  );
}

TreeView.displayName = "TreeView";
