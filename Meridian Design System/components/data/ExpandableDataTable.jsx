// Meridian expandable data table — DenseDataTable with collapsible detail rows.
// Each row can expand to show a full-width detail panel (audit trail, allocations, etc.).
// Detail content is a slot (template, custom JSX). Smooth slide animation on expand/collapse.

import React from "react";
const { useState } = React;

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.edt-wrap{overflow-x:auto;border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-chip,2px);background:var(--bg-light,#fff);}
.edt{width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data);font-size:12px;}
.edt thead{position:sticky;top:0;z-index:1;background:var(--bg-medium,#F5F7FA);}
.edt th{padding:9px 12px;text-align:left;white-space:nowrap;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);
  border-bottom:1px solid var(--border,#D7DCE2);}
.edt th.edt--r{text-align:right;}
.edt th:focus-visible{outline:var(--focus-ring);outline-offset:-2px;color:var(--accent,#2F6F8F);}
.edt th.edt--expand{width:34px;padding:9px 10px;text-align:center;}
.edt th.edt--checkbox{padding:9px 10px;width:34px;text-align:center;}
.edt td{padding:11px 12px;white-space:nowrap;color:var(--text-primary,#22272E);
  border-top:1px solid var(--border,#D7DCE2);font-variant-numeric:tabular-nums;height:40px;}
.edt td.edt--expand{padding:7px 10px;text-align:center;cursor:pointer;}
.edt td.edt--checkbox{padding:7px 10px;text-align:center;}
.edt tbody tr:first-child td{border-top:none;}
.edt td.edt--r{text-align:right;}
.edt tbody tr.edt-main{background:var(--bg-light,#fff);transition:background 100ms ease;}
.edt tbody tr.edt-main:nth-child(4n+1){background:var(--card-surface-raised,#FAFBFC);}
.edt tbody tr.edt-main:hover{background:var(--bg-active,#E6EEF5);
  box-shadow:inset 3px 0 0 var(--accent,#2F6F8F);}
.edt tbody tr.edt-main.edt--sel{cursor:pointer;}
.edt tbody tr.edt-expand{background:var(--blue-a10,rgba(47,111,143,0.10));}
.edt tbody tr.edt-expand td{padding:0;border:none;}
.edt__detail{max-height:0;overflow:hidden;transition:max-height 200ms ease-out;
  padding:0;}
.edt__detail.edt--open{max-height:500px;padding:16px 12px;overflow:visible;}
.edt__chevron{display:inline-block;transition:transform 200ms ease;font-size:12px;}
.edt__chevron.edt--open{transform:rotate(90deg);}
.edt__ckbox{width:18px;height:18px;cursor:pointer;border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-chip,2px);appearance:none;background:var(--bg-light,#fff);
  transition:border-color 100ms ease,background-color 100ms ease;}
.edt__ckbox:hover{border-color:var(--accent,#2F6F8F);}
.edt__ckbox:checked{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);
  background-image:url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="white"><path d="M13.3 4.3L6 11.6 2.7 8.3" stroke="white" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/></svg>');
  background-size:12px;background-position:center;background-repeat:no-repeat;}
.edt__sort{margin-left:6px;font-size:9px;opacity:.4;}
.edt th.edt--sorted{color:var(--text-secondary,#4D5967);}
.edt th.edt--sorted .edt__sort{opacity:1;color:var(--accent,#2F6F8F);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "edt");
  el.textContent = css;
  document.head.appendChild(el);
}

export function ExpandableDataTable({
  columns,
  rows,
  expandable = null, // fn: (row, index) => JSX or React node for detail content
  selectedIndex = -1,
  sortKey,
  sortDir = "asc",
  onRowClick,
  onSort,
  selectable = false,
  selectedRows = [],
  onSelectRow,
  onSelectAll,
}) {
  inject();
  const [expandedIndices, setExpandedIndices] = useState(new Set());

  const toggleExpand = (index) => {
    const next = new Set(expandedIndices);
    if (next.has(index)) {
      next.delete(index);
    } else {
      next.add(index);
    }
    setExpandedIndices(next);
  };

  const allSelected = selectable && selectedRows.length === rows.length && rows.length > 0;
  const someSelected = selectable && selectedRows.length > 0 && selectedRows.length < rows.length;

  const handleSelectAll = (e) => {
    onSelectAll?.(!allSelected);
  };

  const handleSelectRow = (row, i, e) => {
    e.stopPropagation();
    onSelectRow?.(row, i, !selectedRows.includes(i));
  };

  return (
    <div className="edt-wrap">
      <table className="edt">
        <thead>
          <tr>
            {expandable && <th className="edt--expand"></th>}
            {selectable && (
              <th className="edt--checkbox">
                <input
                  type="checkbox"
                  className="edt__ckbox"
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
              const sorted = sortKey === c.key;
              const cls = `${c.align === "right" ? "edt--r " : ""}${sorted ? "edt--sorted" : ""}`.trim();
              return (
                <th
                  key={c.key}
                  className={cls}
                  scope="col"
                  tabIndex={onSort && c.sortable !== false ? 0 : undefined}
                  aria-sort={sorted ? (sortDir === "asc" ? "ascending" : "descending") : (onSort && c.sortable !== false ? "none" : undefined)}
                  onClick={onSort && c.sortable !== false ? () => onSort(c.key) : undefined}
                  onKeyDown={onSort && c.sortable !== false ? (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); onSort(c.key); } } : undefined}
                  style={{ cursor: onSort && c.sortable !== false ? "pointer" : "default" }}
                >
                  {c.label}
                  {onSort && c.sortable !== false && (
                    <span className="edt__sort">{sorted ? (sortDir === "asc" ? "▲" : "▼") : "↕"}</span>
                  )}
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => {
            const isSelected = selectedRows.includes(i);
            const isExpanded = expandedIndices.has(i);
            const colSpan = (expandable ? 1 : 0) + (selectable ? 1 : 0) + columns.length;

            return (
              <React.Fragment key={i}>
                {/* Main row */}
                <tr
                  className={`edt-main ${onRowClick ? "edt--sel " : ""}${i === selectedIndex ? "edt--sel" : ""}${isSelected ? "edt--sel" : ""}`.trim()}
                  tabIndex={onRowClick ? 0 : undefined}
                  onClick={onRowClick ? () => onRowClick(row, i) : undefined}
                >
                  {expandable && (
                    <td
                      className="edt--expand"
                      onClick={(e) => {
                        e.stopPropagation();
                        toggleExpand(i);
                      }}
                    >
                      <span className={`edt__chevron ${isExpanded ? "edt--open" : ""}`}>›</span>
                    </td>
                  )}
                  {selectable && (
                    <td className="edt--checkbox">
                      <input
                        type="checkbox"
                        className="edt__ckbox"
                        checked={isSelected}
                        onChange={(e) => handleSelectRow(row, i, e)}
                        onClick={(e) => e.stopPropagation()}
                        aria-label={`Select row ${i + 1}`}
                      />
                    </td>
                  )}
                  {columns.map((c) => (
                    <td key={c.key} className={c.align === "right" ? "edt--r" : ""}>
                      {c.render ? c.render(row) : row[c.key]}
                    </td>
                  ))}
                </tr>

                {/* Expand row (detail panel) */}
                {expandable && isExpanded && (
                  <tr className="edt-expand">
                    <td colSpan={colSpan}>
                      <div className={`edt__detail ${isExpanded ? "edt--open" : ""}`}>
                        {expandable(row, i)}
                      </div>
                    </td>
                  </tr>
                )}
              </React.Fragment>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
