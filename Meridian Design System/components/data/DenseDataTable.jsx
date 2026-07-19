// Meridian dense data grid — light institutional table. White paper, small-caps muted
// headers, hairline row borders, hover/selected = blue wash + teal-blue left rail, sort
// affordance. Supports optional checkbox column for multi-select workflows. Mirrors the
// WorkstationTablePanel surface + dense operator tables.
//
// Rows past `virtualizeThreshold` (default 500, matching PATTERNS.md's "use VirtualizedList
// past ~500 rows" guidance) are windowed: only the visible slice + overscan render as real
// <tr> elements, with two spacer <tr> cells absorbing the height of the rest so the scrollbar
// and scroll position stay correct. Below the threshold this is a no-op — same markup as
// always. Virtualized rows are real <tr>/<td>, not VirtualizedList's <div> rows, because a
// CSS table needs real table cells for column alignment; VirtualizedList remains the right
// choice for a non-tabular list.
import React from "react";
import { useThemeRowHeight } from "./useThemeRowHeight";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.dds-wrap{overflow-x:auto;border:1px solid var(--border,#D7DCE2);background:var(--bg-light,#fff);}
.dds{width:100%;min-width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data);font-size:12px;}
.dds thead{position:sticky;top:0;z-index:1;background:var(--bg-medium,#F5F7FA);}
.dds th{padding:9px 12px;text-align:left;white-space:nowrap;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);
  border-bottom:1px solid var(--border-strong,#AAB4BF);border-right:1px solid var(--border-divider,#DDE3EA);}
.dds th:last-child{border-right:none;}
.dds th.dds--r{text-align:right;}
.dds th.dds--checkbox{padding:9px 10px;width:34px;text-align:center;}
.dds td{padding:7px 12px;white-space:nowrap;color:var(--text-primary,#22272E);
  border-top:1px solid var(--border,#D7DCE2);border-right:1px solid var(--border-divider,#DDE3EA);
  font-variant-numeric:tabular-nums;height:var(--theme-row-height,32px);box-sizing:border-box;}
.dds td:last-child{border-right:none;}
/* density: row height tracks --theme-row-height; vertical padding compresses to fit */
body[data-theme-density="compact"] .dds td{padding-top:5px;padding-bottom:5px;}
body[data-theme-density="spacious"] .dds td{padding-top:15px;padding-bottom:15px;}
.dds td.dds--checkbox{padding:7px 10px;text-align:center;}
.dds tbody tr:first-child td{border-top:none;}
.dds td.dds--r{text-align:right;}
.dds tbody tr{background:var(--bg-light,#fff);}
.dds tbody tr:nth-child(even){background:var(--card-surface-raised,#FAFBFC);}
.dds tbody tr.dds--sel{cursor:pointer;}
.dds tbody tr:hover,.dds tbody tr.dds--on{background:var(--bg-active,#E6EEF5);border-left:3px solid var(--accent,#2F6F8F);}
.dds tbody tr.dds--sel:focus-visible{outline:var(--focus-ring);outline-offset:-2px;}
.dds tbody tr.dds--spacer{background:transparent;}
.dds tbody tr.dds--spacer td{border-top:none;border-right:none;padding:0;}
.dds tbody tr.dds--spacer:hover{background:transparent;border-left:none;}
.dds__ckbox{width:18px;height:18px;cursor:pointer;border:1px solid var(--border,#D7DCE2);
  appearance:none;background:var(--bg-light,#fff);}
.dds__ckbox:hover{border-color:var(--accent,#2F6F8F);}
.dds__ckbox:checked{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);
  background-image:url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="white"><path d="M13.3 4.3L6 11.6 2.7 8.3" stroke="white" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/></svg>');
  background-size:12px;background-position:center;background-repeat:no-repeat;}
.dds__sort{margin-left:6px;font-size:9px;opacity:.4;}
.dds th.dds--sorted{color:var(--text-secondary,#4D5967);}
.dds th.dds--sorted .dds__sort{opacity:1;color:var(--accent,#2F6F8F);}
.dds__rank{margin-left:3px;font-size:8px;font-weight:700;color:var(--accent,#2F6F8F);vertical-align:super;}
/* pinned columns: sticky to the left edge, raised above scrolling cells, right divider */
.dds th.dds--pin,.dds td.dds--pin{position:sticky;z-index:var(--z-raised,10);
  background:var(--bg-medium,#F5F7FA);}
.dds td.dds--pin{background:var(--bg-light,#fff);box-shadow:1px 0 0 var(--border-strong,#AAB4BF);}
.dds th.dds--pin{box-shadow:1px 0 0 var(--border-strong,#AAB4BF);}
.dds tbody tr:nth-child(even) td.dds--pin{background:var(--card-surface-raised,#FAFBFC);}
.dds tbody tr:hover td.dds--pin,.dds tbody tr.dds--on td.dds--pin{background:var(--bg-active,#E6EEF5);}
/* column resize grip on the header's right edge */
.dds__grip{position:absolute;top:0;right:0;width:7px;height:100%;cursor:col-resize;
  user-select:none;touch-action:none;}
.dds__grip:hover,.dds__grip--active{background:var(--accent,#2F6F8F);opacity:.35;}
.dds th{position:relative;}
.dds th[tabindex]:focus-visible{outline:var(--focus-ring);outline-offset:-2px;color:var(--accent,#2F6F8F);}
.dds th.dds--drag{opacity:.5;}
.dds th.dds--drop{box-shadow:inset 2px 0 0 var(--accent,#2F6F8F);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "dds");
  el.textContent = css;
  document.head.appendChild(el);
}

export function DenseDataTable({
  columns,
  rows,
  selectedIndex = -1,
  sortKey,
  sortDir = "asc",
  sortInfo,
  onRowClick,
  onSort,
  onColumnResize,
  onColumnReorder,
  onColumnPin,
  selectable = false,
  selectedRows = [],
  isRowSelected,
  onSelectRow,
  onSelectAll,
  virtualize,
  virtualizeThreshold = 500,
  rowHeight, // px — defaults to --theme-row-height (26 terminal · 32 default/compact · 48 spacious)
  maxHeight = 560,
}) {
  inject();
  const effRow = useThemeRowHeight(rowHeight);
  const rowIsSel = (row, realIndex) => (isRowSelected ? isRowSelected(row, realIndex) : selectedRows.includes(realIndex));
  const allSelected = selectable && selectedRows.length === rows.length && rows.length > 0;
  const someSelected = selectable && selectedRows.length > 0 && selectedRows.length < rows.length;
  const shouldVirtualize = virtualize ?? rows.length > virtualizeThreshold;
  const [dragKey, setDragKey] = React.useState(null);
  const [dropKey, setDropKey] = React.useState(null);
  const resizeRef = React.useRef(null);

  const startResize = (col, e) => {
    e.preventDefault();
    e.stopPropagation();
    const startX = e.clientX;
    const startW = col.width || 140;
    resizeRef.current = col.key;
    const onMove = (ev) => onColumnResize && onColumnResize(col.key, startW + (ev.clientX - startX));
    const onUp = () => {
      resizeRef.current = null;
      document.removeEventListener("mousemove", onMove);
      document.removeEventListener("mouseup", onUp);
    };
    document.addEventListener("mousemove", onMove);
    document.addEventListener("mouseup", onUp);
  };

  const [scrollTop, setScrollTop] = React.useState(0);
  const overscan = 8;
  const viewportRows = Math.ceil(maxHeight / effRow);
  const start = shouldVirtualize ? Math.max(0, Math.floor(scrollTop / effRow) - overscan) : 0;
  const end = shouldVirtualize ? Math.min(rows.length, start + viewportRows + overscan * 2) : rows.length;
  const visibleRows = shouldVirtualize ? rows.slice(start, end) : rows;
  const topSpacer = start * effRow;
  const bottomSpacer = (rows.length - end) * effRow;
  const colCount = columns.length + (selectable ? 1 : 0);

  const handleSelectAll = (e) => {
    onSelectAll?.(!allSelected);
  };

  const handleSelectRow = (row, i, e) => {
    e.stopPropagation();
    onSelectRow?.(row, i, !selectedRows.includes(i));
  };

  return (
    <div
      className="dds-wrap"
      style={shouldVirtualize ? { maxHeight, overflowY: "auto" } : undefined}
      onScroll={shouldVirtualize ? (e) => setScrollTop(e.currentTarget.scrollTop) : undefined}
    >
      <table className="dds">
        <thead>
          <tr>
            {selectable && (
              <th className="dds--checkbox">
                <input
                  type="checkbox"
                  className="dds__ckbox"
                  checked={allSelected}
                  ref={(el) => {
                    if (el && someSelected) el.indeterminate = true;
                  }}
                  onChange={handleSelectAll}
                  aria-label="Select all rows"
                />
              </th>
            )}
            {columns.map((c) => {
              const info = sortInfo ? sortInfo(c.key) : (sortKey === c.key ? { direction: sortDir } : null);
              const sorted = !!info;
              const dir = info ? info.direction : sortDir;
              const canSort = onSort && c.sortable !== false;
              const pinStyle = c.pinned ? { left: c.pinnedOffset || 0 } : undefined;
              const cls = `${c.align === "right" ? "dds--r " : ""}${sorted ? "dds--sorted " : ""}${c.pinned ? "dds--pin " : ""}${dragKey === c.key ? "dds--drag " : ""}${dropKey === c.key ? "dds--drop" : ""}`.trim();
              return (
                <th
                  key={c.key}
                  className={cls}
                  style={{ cursor: canSort ? "pointer" : "default", width: c.width, minWidth: c.width, maxWidth: c.width, ...pinStyle }}
                  draggable={!!onColumnReorder}
                  onDragStart={onColumnReorder ? () => setDragKey(c.key) : undefined}
                  onDragOver={onColumnReorder ? (e) => { e.preventDefault(); setDropKey(c.key); } : undefined}
                  onDrop={onColumnReorder ? () => { if (dragKey && dragKey !== c.key) onColumnReorder(dragKey, c.key); setDragKey(null); setDropKey(null); } : undefined}
                  onDragEnd={onColumnReorder ? () => { setDragKey(null); setDropKey(null); } : undefined}
                  scope="col"
                  tabIndex={canSort ? 0 : undefined}
                  aria-sort={sorted ? (dir === "asc" ? "ascending" : "descending") : (canSort ? "none" : undefined)}
                  onClick={canSort ? (e) => onSort(c.key, e.shiftKey) : undefined}
                  onKeyDown={canSort ? (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onSort(c.key, e.shiftKey); } } : undefined}
                  title={onColumnPin ? "Double-click to pin" : undefined}
                  onDoubleClick={onColumnPin ? () => onColumnPin(c.key) : undefined}
                >
                  {c.label}
                  {canSort && (
                    <span className="dds__sort">{sorted ? (dir === "asc" ? "▲" : "▼") : "↕"}</span>
                  )}
                  {info && info.multi && <span className="dds__rank">{info.rank}</span>}
                  {onColumnResize && (
                    <span
                      className={`dds__grip${resizeRef.current === c.key ? " dds__grip--active" : ""}`}
                      onMouseDown={(e) => startResize(c, e)}
                      onClick={(e) => e.stopPropagation()}
                    />
                  )}
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody>
          {topSpacer > 0 && (
            <tr className="dds--spacer" aria-hidden="true">
              <td colSpan={colCount} style={{ height: topSpacer }} />
            </tr>
          )}
          {visibleRows.map((row, i) => {
            const realIndex = start + i;
            const isSelected = rowIsSel(row, realIndex);
            return (
              <tr
                key={realIndex}
                className={`${onRowClick ? "dds--sel " : ""}${realIndex === selectedIndex ? "dds--on" : ""}${isSelected ? "dds--on" : ""}`.trim()}
                aria-selected={isSelected || undefined}
                tabIndex={onRowClick ? 0 : undefined}
                onClick={onRowClick ? (e) => onRowClick(row, realIndex, e) : undefined}
              >
                {selectable && (
                  <td className="dds--checkbox">
                    <input
                      type="checkbox"
                      className="dds__ckbox"
                      checked={isSelected}
                      onChange={(e) => handleSelectRow(row, realIndex, e)}
                      onClick={(e) => e.stopPropagation()}
                      aria-label={`Select row ${realIndex + 1}`}
                    />
                  </td>
                )}
                {columns.map((c) => (
                  <td key={c.key} className={`${c.align === "right" ? "dds--r " : ""}${c.pinned ? "dds--pin" : ""}`.trim()}
                    style={c.pinned ? { left: c.pinnedOffset || 0, width: c.width, minWidth: c.width, maxWidth: c.width } : (c.width ? { width: c.width, minWidth: c.width, maxWidth: c.width } : undefined)}>
                    {c.render ? c.render(row) : row[c.key]}
                  </td>
                ))}
              </tr>
            );
          })}
          {bottomSpacer > 0 && (
            <tr className="dds--spacer" aria-hidden="true">
              <td colSpan={colCount} style={{ height: bottomSpacer }} />
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
