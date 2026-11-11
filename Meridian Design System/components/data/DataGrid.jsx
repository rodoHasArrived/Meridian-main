// Meridian DataGrid — ONE tabular primitive with opt-in capabilities
// (selection, grouping, expansion, inline edit, totals) plus two things the
// generic tables never had: a column schema and a *proof* contract, so a table
// proves its own arithmetic. Domain tables (ledger, trial balance, tax lots,
// blotter, aging) are schemas over this grid, not separate components.
import React from "react";
import { formatNumber } from "../core/format.js";
import { formatGridSigned as formatSigned } from "./gridFormat";
import { useThemeRowHeight } from "./useThemeRowHeight";

const num = (v) => (typeof v === "number" && !Number.isNaN(v) ? v : 0);

export const CELL_FORMATS = {
  text: (v) => (v === null || v === undefined || v === "" ? "\u2014" : String(v)),
  num: (v, c) => (v === null || v === undefined ? "\u2014" : formatNumber(Number(v), { decimals: c.digits ?? 0 })),
  money: (v, c) => (v === null || v === undefined || v === 0 ? "\u2014" : formatSigned(v, { style: "accounting", digits: c.digits ?? 2 })),
  signed: (v, c) => formatSigned(v, { digits: c.digits ?? 2 }),
  price: (v, c) => (v === null || v === undefined ? "\u2014" : Number(v).toFixed(c.digits ?? 4)),
  pct: (v, c) => (v === null || v === undefined ? "\u2014" : formatSigned(v, { digits: c.digits ?? 2, suffix: "%" })),
};

/** Column-schema recipes — the domain tables, as data. */
export const GRID_SCHEMAS = {
  ledger: {
    proof: { type: "balance", debit: "debit", credit: "credit" },
    columns: [
      { key: "date", label: "Date", format: "text", width: 96 },
      { key: "account", label: "Account", format: "text" },
      { key: "memo", label: "Memo", format: "text" },
      { key: "debit", label: "Debit", format: "money", align: "right", total: "sum", width: 116 },
      { key: "credit", label: "Credit", format: "money", align: "right", total: "sum", width: 116 },
    ],
  },
  trialBalance: {
    proof: { type: "balance", debit: "debit", credit: "credit", label: "\u03A3 debit = \u03A3 credit" },
    columns: [
      { key: "code", label: "Code", format: "text", width: 80 },
      { key: "account", label: "Account", format: "text" },
      { key: "debit", label: "Debit", format: "money", align: "right", total: "sum", width: 120 },
      { key: "credit", label: "Credit", format: "money", align: "right", total: "sum", width: 120 },
    ],
  },
  taxLots: {
    proof: { type: "sum", column: "basis", label: "\u03A3 basis" },
    columns: [
      { key: "lot", label: "Lot", format: "text", width: 96 },
      { key: "opened", label: "Opened", format: "text", width: 96 },
      { key: "qty", label: "Qty", format: "num", align: "right", width: 88 },
      { key: "basis", label: "Basis", format: "money", align: "right", total: "sum", width: 116 },
      { key: "unrealized", label: "Unrealized", format: "signed", align: "right", total: "sum", width: 116 },
      { key: "term", label: "Term", format: "text", width: 72 },
    ],
  },
  blotter: {
    columns: [
      { key: "time", label: "Time", format: "text", width: 96 },
      { key: "symbol", label: "Symbol", format: "text", width: 88 },
      { key: "side", label: "Side", format: "text", width: 60 },
      { key: "qty", label: "Qty", format: "num", align: "right", width: 84 },
      { key: "price", label: "Price", format: "price", align: "right", width: 96 },
      { key: "status", label: "Status", format: "text", width: 104 },
    ],
  },
  aging: {
    proof: { type: "sum", column: "total", label: "\u03A3 outstanding" },
    columns: [
      { key: "counterparty", label: "Counterparty", format: "text" },
      { key: "current", label: "Current", format: "money", align: "right", total: "sum" },
      { key: "d30", label: "1\u201330", format: "money", align: "right", total: "sum" },
      { key: "d60", label: "31\u201360", format: "money", align: "right", total: "sum" },
      { key: "d90", label: "60+", format: "money", align: "right", total: "sum" },
      { key: "total", label: "Total", format: "money", align: "right", total: "sum" },
    ],
  },
};

const ROW_H = { terminal: 26, dense: 28, standard: 36, review: 44 };

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-grid{width:100%;background:var(--bg-surface, #FCFBF8);border:1px solid var(--border,#D2CDC3);border-radius:2px;overflow:auto;}
.mds-grid table{width:100%;border-collapse:collapse;font-family:var(--font-data,ui-monospace);font-variant-numeric:tabular-nums;}
.mds-grid th{position:sticky;top:0;z-index:2;background:var(--bg-light,#F8F7F3);text-align:left;
  font-family:var(--font-body,system-ui);font-size:10px;font-variant:all-small-caps;letter-spacing:.06em;
  color:var(--text-muted,#606870);font-weight:var(--weight-medium,500);padding:0 10px;
  border-bottom:1px solid var(--border-strong,#AAA49A);white-space:nowrap;}
.mds-grid th[data-sortable]{cursor:pointer;user-select:none;}
.mds-grid tbody tr[data-clickable] td{cursor:pointer;}
.mds-grid th.r,.mds-grid td.r{text-align:right;}
.mds-grid td{padding:0 10px;font-size:var(--type-body,13px);color:var(--text-primary,#20252B);
  border-bottom:1px solid var(--border-divider,#E4E0D8);white-space:nowrap;}
.mds-grid tbody tr:hover td{background:var(--bg-hover,#F1EEE7);}
.mds-grid tr[data-selected="true"] td{background:var(--ws-row-selected, #E7EDF2);}
.mds-grid__grp td{background:var(--surface-recessed, #EBE8E1);font-family:var(--font-body,system-ui);font-size:11px;
  font-weight:var(--weight-medium,500);color:var(--text-secondary,#535C65);}
.mds-grid__grp td.r{font-family:var(--font-data,ui-monospace);font-variant-numeric:tabular-nums;color:var(--text-primary,#20252B);}
.mds-grid__grp[data-depth] td{background:var(--bg-medium, #F1EEE7);}
.mds-grid__grp span[role="button"]{outline-offset:2px;}
.mds-grid__grp span[role="button"]:focus-visible{outline:2px solid var(--focus,#2D6FB3);}
.mds-grid__exp{width:22px;text-align:center;color:var(--text-muted,#606870);cursor:pointer;user-select:none;}
.mds-grid__detail td{background:var(--bg-light,#F8F7F3);white-space:normal;font-family:var(--font-body,system-ui);font-size:12px;color:var(--text-secondary,#535C65);}
.mds-grid tfoot td{background:var(--bg-light,#F8F7F3);border-top:1px solid var(--border-strong,#AAA49A);
  border-bottom:0;font-weight:var(--weight-medium,500);}
.mds-grid__proof{display:flex;justify-content:space-between;gap:12px;align-items:baseline;padding:6px 10px;
  border-top:1px solid var(--border-strong,#AAA49A);background:var(--bg-light,#F8F7F3);
  font-family:var(--font-body,system-ui);font-size:11px;}
.mds-grid__proof b{font-family:var(--font-data,ui-monospace);}
.mds-grid__proof--ok{color:var(--green, #2E7655);}
.mds-grid__proof--bad{color:var(--red, #B54B42);}
.mds-grid__cellin{width:100%;box-sizing:border-box;font:inherit;text-align:inherit;border:1px solid var(--focus,#2D6FB3);
  border-radius:2px;padding:1px 4px;background:var(--bg-surface, #FCFBF8);color:inherit;outline:none;}
.mds-grid__sel{width:26px;text-align:center;}
.mds-grid th{position:relative;}
.mds-grid th.mds-grid__pin,.mds-grid td.mds-grid__pin{position:sticky;z-index:1;background:var(--bg-light,#F8F7F3);box-shadow:1px 0 0 var(--border-strong,#AAA49A);}
.mds-grid th.mds-grid__pin{z-index:3;}
.mds-grid__grip{position:absolute;top:0;right:0;width:7px;height:100%;cursor:col-resize;user-select:none;touch-action:none;}
.mds-grid__grip:hover{background:var(--interaction-selected,#496C8F);opacity:.35;}
.mds-grid th[data-drop]{box-shadow:inset 2px 0 0 var(--brand-signal,#C56343);}
.mds-grid__cap{position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap;border:0;}
.mds-grid tbody tr[data-clickable]:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px;}
.mds-grid__drill{width:26px;text-align:center;color:var(--text-muted,#606870);cursor:pointer;user-select:none;font-family:var(--font-body,system-ui);}
.mds-grid tbody tr:hover .mds-grid__drill{color:var(--link,#2D6FB3);}
.mds-grid__drill:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:-2px;}
.mds-grid__withheld{display:flex;justify-content:space-between;gap:12px;align-items:baseline;padding:6px 10px;
  border-top:1px solid var(--border-divider,#E4E0D8);background:var(--bg-light,#F8F7F3);
  font-family:var(--font-body,system-ui);font-size:11px;color:var(--text-muted,#606870);}
.mds-grid__withheld button{padding:0;background:transparent;border:0;font:inherit;font-weight:var(--weight-medium,500);
  color:var(--link,#2D6FB3);cursor:pointer;text-decoration:underline;text-underline-offset:2px;}
.mds-grid__withheld button:hover{color:var(--link-hover,#1F5285);}`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "data-grid");
  el.textContent = css;
  document.head.appendChild(el);
}

function fmt(col, row) {
  const raw = row[col.key];
  if (col.cell) return col.cell(row);
  const f = CELL_FORMATS[col.format || "text"] || CELL_FORMATS.text;
  return f(raw, col);
}

/** The proof contract — a table that states its own arithmetic. */
export function gridProof(proof, rows) {
  if (!proof) return null;
  if (proof.type === "balance") {
    const d = rows.reduce((a, r) => a + num(r[proof.debit]), 0);
    const c = rows.reduce((a, r) => a + num(r[proof.credit]), 0);
    const out = Math.round((d - c) * 100) / 100;
    return { ok: out === 0, label: proof.label || "\u03A3 debit = \u03A3 credit",
      text: out === 0 ? `Balanced \u00B7 ${formatSigned(d, { style: "accounting" })}` : `Out by ${formatSigned(out, { style: "accounting" })}` };
  }
  if (proof.type === "sum") {
    const s = rows.reduce((a, r) => a + num(r[proof.column]), 0);
    const exp = proof.expected;
    const ok = exp === undefined || Math.round((s - exp) * 100) / 100 === 0;
    return { ok, label: proof.label || `\u03A3 ${proof.column}`,
      text: ok ? formatSigned(s, { style: "accounting" }) : `${formatSigned(s, { style: "accounting" })} \u00B7 out by ${formatSigned(s - exp, { style: "accounting" })}` };
  }
  if (proof.type === "custom" && typeof proof.check === "function") return proof.check(rows);
  return null;
}

export function DataGrid({
  columns, rows = [], schema, rowKey = "id", density,
  detail = "full", onDetailChange, onDrill, drillLabel = "Open detail",
  select = false, groupBy, expand, edit = false, proof, totals = false,
  sortable = false, sortKey, sortDir, onSort, onRowClick, activeKey,
  onColumnResize, onColumnReorder, onColumnPin,
  virtualize, virtualizeThreshold = 500, label, caption,
  selected, onSelectedChange, onEdit, emptyText = "No rows", maxHeight, className = "", ...rest
}) {
  inject();
  const base = (schema && GRID_SCHEMAS[schema]) || null;
  const allCols = columns || (base ? base.columns : []);
  // Depth tiers (4.1): a column marked `tier: "full"` withdraws at summary depth, and the
  // grid states how many it withheld. Untiered columns — identity, and the figures the table
  // is about — never withdraw (guidelines/PROGRESSIVE_DISCLOSURE.md).
  const cols = detail === "summary" ? allCols.filter((c) => c.tier !== "full") : allCols;
  const withheld = allCols.length - cols.length;
  const pf = proof !== undefined ? proof : base ? base.proof : null;
  const showTotals = totals || cols.some((c) => c.total);
  // No explicit density → row height tracks the theme density token, like every Meridian table.
  const themeRow = useThemeRowHeight();
  const h = density ? (ROW_H[density] || ROW_H.standard) : themeRow || ROW_H.standard;
  const [openRows, setOpenRows] = React.useState({});
  const [collapsed, setCollapsed] = React.useState({});
  const [editing, setEditing] = React.useState(null);
  const [selfSel, setSelfSel] = React.useState([]);
  const sel = selected || selfSel;
  const setSel = (next) => { setSelfSel(next); if (onSelectedChange) onSelectedChange(next); };
  // rowKey="index" keys rows by position — for static seed tables migrated from
  // index-based selection. Otherwise a field name or fn, falling back to position.
  const keyOf = (r, i) => (rowKey === "index" ? i : typeof rowKey === "function" ? rowKey(r) : r[rowKey] ?? i);

  // Sorting — controlled (the screen owns order via sortKey/sortDir/onSort and passes rows
  // already ordered; the grid renders the indicators) or internal (`sortable`).
  const [selfSort, setSelfSort] = React.useState({ key: null, dir: "desc" });
  const controlledSort = sortKey !== undefined || !!onSort;
  const sk = controlledSort ? sortKey : selfSort.key;
  const sd = controlledSort ? (sortDir || "desc") : selfSort.dir;
  const canSort = sortable || !!onSort;
  const handleSort = (key) =>
    onSort ? onSort(key) : setSelfSort((s) => (s.key === key ? { key, dir: s.dir === "asc" ? "desc" : "asc" } : { key, dir: "desc" }));
  // Column management (from DenseDataTable): grip resize, drag reorder, pin via
  // double-click / P; Alt+←/→ resize and Ctrl+←/→ reorder on the focused header.
  const [dragKey, setDragKey] = React.useState(null);
  const [dropKey, setDropKey] = React.useState(null);
  const startResize = (col, e) => {
    e.preventDefault(); e.stopPropagation();
    const startX = e.clientX, startW = col.width || 140;
    const onMove = (ev) => onColumnResize && onColumnResize(col.key, Math.max(40, startW + (ev.clientX - startX)));
    const onUp = () => { document.removeEventListener("mousemove", onMove); document.removeEventListener("mouseup", onUp); };
    document.addEventListener("mousemove", onMove);
    document.addEventListener("mouseup", onUp);
  };
  const ordered = React.useMemo(() => {
    if (!sk || controlledSort) return rows;
    const cmp = (a, b) => {
      const av = a[sk], bv = b[sk];
      const c = typeof av === "number" && typeof bv === "number" ? av - bv : String(av ?? "").localeCompare(String(bv ?? ""));
      return sd === "asc" ? c : -c;
    };
    return [...rows].sort(cmp);
  }, [rows, sk, sd, controlledSort]);

  // groupBy is one key/fn or an array of them (nested). The result is a flat,
  // ordered list of group headers each carrying its own rows, so a group can
  // state its own subtotals and collapse independently of its siblings.
  const groups = React.useMemo(() => {
    if (!groupBy) return [{ path: null, key: null, depth: 0, rows: ordered }];
    const keys = Array.isArray(groupBy) ? groupBy : [groupBy];
    const out = [];
    const walk = (rs, depth, prefix) => {
      const by = keys[depth];
      const map = new Map();
      rs.forEach((r) => {
        const g = typeof by === "function" ? by(r) : r[by];
        if (!map.has(g)) map.set(g, []);
        map.get(g).push(r);
      });
      map.forEach((sub, key) => {
        const path = prefix ? prefix + " \u203A " + String(key) : String(key);
        out.push({ path, key, depth, rows: sub, leaf: depth + 1 === keys.length });
        if (depth + 1 < keys.length) walk(sub, depth + 1, path);
      });
    };
    walk(ordered, 0, "");
    return out;
  }, [ordered, groupBy]);

  const p = gridProof(pf, rows);
  // Windowing (from DenseDataTable): past the threshold only the visible slice mounts,
  // spacer rows absorb the rest. Grouped/expandable grids render fully — their row
  // heights aren't uniform.
  const shouldVirt = (virtualize ?? rows.length > virtualizeThreshold) && !groupBy && !expand;
  const [scrollTop, setScrollTop] = React.useState(0);
  const vh = typeof maxHeight === "number" ? maxHeight : 560;
  const vStart = shouldVirt ? Math.max(0, Math.floor(scrollTop / h) - 8) : 0;
  const vEnd = shouldVirt ? Math.min(ordered.length, vStart + Math.ceil(vh / h) + 16) : ordered.length;
  const span = cols.length + (select ? 1 : 0) + (expand ? 1 : 0) + (onDrill ? 1 : 0);
  // A collapsed parent hides its nested groups' rows too.
  const isHiddenUnder = (path) =>
    Object.keys(collapsed).some((c) => collapsed[c] && path !== c && path.indexOf(c + " \u203A ") === 0);

  const commit = (row, col, value) => {
    setEditing(null);
    if (onEdit) onEdit({ row, column: col.key, value });
  };

  return (
    <div className={`mds-grid${className ? " " + className : ""}`}
      role={label ? "region" : undefined} aria-label={label}
      style={maxHeight || shouldVirt ? { maxHeight: maxHeight || vh, overflowY: shouldVirt ? "auto" : undefined } : undefined}
      onScroll={shouldVirt ? (e) => setScrollTop(e.currentTarget.scrollTop) : undefined} {...rest}>
      <table aria-rowcount={rows.length + 1}>
        {caption || label ? <caption className="mds-grid__cap">{caption || label}</caption> : null}
        <thead>
          <tr style={{ height: h }}>
            {select ? (
              <th className="mds-grid__sel">
                <input type="checkbox" aria-label="Select all rows"
                  checked={rows.length > 0 && sel.length === rows.length}
                  ref={(node) => { if (node) node.indeterminate = sel.length > 0 && sel.length < rows.length; }}
                  onChange={() => setSel(sel.length === rows.length ? [] : rows.map((r, i) => keyOf(r, i)))} />
              </th>
            ) : null}
            {expand ? <th style={{ width: 22 }} /> : null}
            {cols.map((c) => {
              const sortableCol = canSort && c.sortable !== false;
              const hasColKeys = !!(onColumnResize || onColumnReorder || onColumnPin);
              const onThKey = (e) => {
                if (sortableCol && (e.key === "Enter" || e.key === " ")) { e.preventDefault(); handleSort(c.key); return; }
                if (onColumnResize && e.altKey && (e.key === "ArrowLeft" || e.key === "ArrowRight")) { e.preventDefault(); onColumnResize(c.key, Math.max(40, (c.width || 140) + (e.key === "ArrowRight" ? 16 : -16))); return; }
                if (onColumnReorder && e.ctrlKey && (e.key === "ArrowLeft" || e.key === "ArrowRight")) {
                  e.preventDefault();
                  const idx = cols.findIndex((x) => x.key === c.key), tgt = cols[idx + (e.key === "ArrowRight" ? 1 : -1)];
                  if (tgt) onColumnReorder(c.key, tgt.key);
                  return;
                }
                if (onColumnPin && (e.key === "p" || e.key === "P") && !e.ctrlKey && !e.altKey && !e.metaKey) { e.preventDefault(); onColumnPin(c.key); }
              };
              return (
                <th key={c.key}
                  className={`${c.align === "right" ? "r " : ""}${c.pinned ? "mds-grid__pin" : ""}`.trim()}
                  style={{ ...(c.width ? { width: c.width, minWidth: c.width, maxWidth: c.width } : null), ...(c.pinned ? { left: c.pinnedOffset || 0 } : null) }}
                  data-sortable={sortableCol || undefined} scope="col"
                  tabIndex={sortableCol || hasColKeys ? 0 : undefined}
                  draggable={!!onColumnReorder}
                  onDragStart={onColumnReorder ? () => setDragKey(c.key) : undefined}
                  onDragOver={onColumnReorder ? (e) => { e.preventDefault(); setDropKey(c.key); } : undefined}
                  onDrop={onColumnReorder ? () => { if (dragKey && dragKey !== c.key) onColumnReorder(dragKey, c.key); setDragKey(null); setDropKey(null); } : undefined}
                  onDragEnd={onColumnReorder ? () => { setDragKey(null); setDropKey(null); } : undefined}
                  onDoubleClick={onColumnPin ? () => onColumnPin(c.key) : undefined}
                  data-drop={dropKey === c.key || undefined}
                  aria-sort={sk === c.key ? (sd === "asc" ? "ascending" : "descending") : undefined}
                  onKeyDown={sortableCol || hasColKeys ? onThKey : undefined}
                  onClick={sortableCol ? () => handleSort(c.key) : undefined}>
                  {c.label}{sk === c.key ? (sd === "asc" ? " \u25B4" : " \u25BE") : ""}
                  {onColumnResize ? (
                    <span className="mds-grid__grip" aria-hidden="true" onMouseDown={(e) => startResize(c, e)} onClick={(e) => e.stopPropagation()}></span>
                  ) : null}
                </th>
              );
            })}
            {onDrill ? <th style={{ width: 26 }} scope="col"><span className="mds-grid__cap">{drillLabel}</span></th> : null}
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 ? (
            <tr style={{ height: h }}><td colSpan={span} style={{ color: "var(--text-muted,#606870)", fontFamily: "var(--font-body,system-ui)" }}>{emptyText}</td></tr>
          ) : null}
          {groups.map((g) => (
            <React.Fragment key={g.path === null ? "__all" : g.path}>
              {shouldVirt && g.key === null && vStart > 0 ? (
                <tr aria-hidden="true"><td colSpan={span} style={{ height: vStart * h, padding: 0, border: 0 }}></td></tr>
              ) : null}
              {g.key !== null && !isHiddenUnder(g.path) ? (
                <tr className="mds-grid__grp" style={{ height: h - 4 }} data-depth={g.depth || undefined}>
                  {select ? <td className="mds-grid__sel" /> : null}
                  {expand ? <td /> : null}
                  {cols.map((c, i) => (
                    <td key={c.key} className={c.align === "right" ? "r" : ""}>
                      {i === 0 ? (
                        <span role="button" tabIndex={0} aria-expanded={!collapsed[g.path]}
                          style={{ paddingLeft: g.depth * 12, cursor: "pointer" }}
                          onClick={() => setCollapsed((s) => ({ ...s, [g.path]: !s[g.path] }))}
                          onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); setCollapsed((s) => ({ ...s, [g.path]: !s[g.path] })); } }}>
                          {collapsed[g.path] ? "\u203A" : "\u2304"} {String(g.key)} · {g.rows.length}
                        </span>
                      ) : c.total === "sum"
                        ? (CELL_FORMATS[c.format] || CELL_FORMATS.text)(g.rows.reduce((a, r) => a + num(r[c.key]), 0), c)
                        : ""}
                    </td>
                  ))}
                  {onDrill ? <td /> : null}
                </tr>
              ) : null}
              {(g.key !== null && (g.leaf === false || collapsed[g.path] || isHiddenUnder(g.path))) ? null : (shouldVirt && g.key === null ? g.rows.slice(vStart, vEnd) : g.rows).map((r, i0) => {
                const i = shouldVirt && g.key === null ? vStart + i0 : i0;
                const k = keyOf(r, i);
                const isOpen = !!openRows[k];
                const isSel = sel.indexOf(k) > -1;
                return (
                  <React.Fragment key={k}>
                    <tr style={{ height: h }} data-selected={isSel || (activeKey !== undefined && k === activeKey) ? "true" : undefined}
                      data-clickable={onRowClick ? "true" : undefined} tabIndex={onRowClick ? 0 : undefined}
                      aria-rowindex={shouldVirt ? i + 2 : undefined}
                      onKeyDown={onRowClick ? (e) => { if ((e.key === "Enter" || e.key === " ") && e.target === e.currentTarget) { e.preventDefault(); onRowClick(r, k, i); } } : undefined}
                      onClick={onRowClick ? () => onRowClick(r, k, i) : undefined}>
                      {select ? (
                        <td className="mds-grid__sel" onClick={(e) => e.stopPropagation()}>
                          <input type="checkbox" checked={isSel} aria-label="Select row"
                            onChange={() => setSel(isSel ? sel.filter((x) => x !== k) : [...sel, k])} />
                        </td>
                      ) : null}
                      {expand ? (
                        <td className="mds-grid__exp" role="button" tabIndex={0} aria-expanded={isOpen}
                          onClick={(e) => { e.stopPropagation(); setOpenRows((s) => ({ ...s, [k]: !s[k] })); }}>{isOpen ? "−" : "+"}</td>
                      ) : null}
                      {cols.map((c) => {
                        const editable = edit && c.editable;
                        const isEditing = editing && editing.k === k && editing.col === c.key;
                        return (
                          <td key={c.key} className={`${c.align === "right" ? "r " : ""}${c.pinned ? "mds-grid__pin" : ""}`.trim()}
                            style={c.pinned ? { left: c.pinnedOffset || 0, width: c.width, minWidth: c.width, maxWidth: c.width } : undefined}
                            onDoubleClick={editable ? () => setEditing({ k, col: c.key }) : undefined}>
                            {isEditing ? (
                              <input className="mds-grid__cellin" autoFocus defaultValue={r[c.key] ?? ""}
                                onBlur={(e) => commit(r, c, e.target.value)}
                                onKeyDown={(e) => { if (e.key === "Enter") commit(r, c, e.target.value); if (e.key === "Escape") setEditing(null); }} />
                            ) : fmt(c, r)}
                          </td>
                        );
                      })}
                      {onDrill ? (
                        <td className="mds-grid__drill" role="button" tabIndex={0} title={drillLabel} aria-label={drillLabel}
                          onClick={(e) => { e.stopPropagation(); onDrill(r, k, i); }}
                          onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); e.stopPropagation(); onDrill(r, k, i); } }}>{"\u203A"}</td>
                      ) : null}
                    </tr>
                    {expand && isOpen ? (
                      <tr className="mds-grid__detail"><td colSpan={span}>{typeof expand === "function" ? expand(r) : null}</td></tr>
                    ) : null}
                  </React.Fragment>
                );
              })}
              {shouldVirt && g.key === null && vEnd < ordered.length ? (
                <tr aria-hidden="true"><td colSpan={span} style={{ height: (ordered.length - vEnd) * h, padding: 0, border: 0 }}></td></tr>
              ) : null}
            </React.Fragment>
          ))}
        </tbody>
        {showTotals ? (
          <tfoot>
            <tr style={{ height: h }}>
              {select ? <td className="mds-grid__sel" /> : null}
              {expand ? <td /> : null}
              {cols.map((c, i) => (
                <td key={c.key} className={c.align === "right" ? "r" : ""}>
                  {c.total === "sum"
                    ? (CELL_FORMATS[c.format] || CELL_FORMATS.text)(rows.reduce((a, r) => a + num(r[c.key]), 0), c)
                    : i === 0 ? "Total" : ""}
                </td>
              ))}
              {onDrill ? <td /> : null}
            </tr>
          </tfoot>
        ) : null}
      </table>
      {withheld > 0 ? (
        <div className="mds-grid__withheld">
          <span>{withheld} column{withheld === 1 ? "" : "s"} withheld at summary depth</span>
          {onDetailChange ? <button type="button" onClick={() => onDetailChange("full")}>Show full detail</button> : null}
        </div>
      ) : null}
      {p ? (
        <div className={`mds-grid__proof mds-grid__proof--${p.ok ? "ok" : "bad"}`}>
          <span>{p.label}</span>
          <b>{p.ok ? "\u2713 " : "\u25B2 "}{p.text}</b>
        </div>
      ) : null}
    </div>
  );
}

// The bundler exposes only capitalized exports, so the proof helper hangs off the grid.
DataGrid.proof = gridProof;
DataGrid.schemas = GRID_SCHEMAS;
DataGrid.formats = CELL_FORMATS;
