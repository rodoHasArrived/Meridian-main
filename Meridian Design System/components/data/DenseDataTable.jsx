// Meridian dense data grid — light institutional table. White paper, small-caps muted
// headers, hairline row borders, hover/selected = blue wash + teal-blue left rail, sort
// affordance. Supports optional checkbox column for multi-select workflows. Mirrors the
// WorkstationTablePanel surface + dense operator tables.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.dds-wrap{overflow-x:auto;border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-chip,4px);background:var(--bg-light,#fff);}
.dds{width:100%;min-width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data);font-size:12px;}
.dds thead{position:sticky;top:0;z-index:1;background:var(--bg-medium,#F5F7FA);}
.dds th{padding:9px 12px;text-align:left;white-space:nowrap;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#6E7781);
  border-bottom:1px solid var(--border,#D7DCE2);}
.dds th.dds--r{text-align:right;}
.dds th.dds--checkbox{padding:9px 10px;width:34px;text-align:center;}
.dds td{padding:10px 12px;white-space:nowrap;color:var(--text-primary,#22272E);
  border-top:1px solid var(--border,#D7DCE2);font-variant-numeric:tabular-nums;}
.dds td.dds--checkbox{padding:7px 10px;text-align:center;}
.dds tbody tr:first-child td{border-top:none;}
.dds td.dds--r{text-align:right;}
.dds tbody tr{background:var(--bg-light,#fff);transition:background 100ms ease;}
.dds tbody tr:nth-child(even){background:var(--card-surface-raised,#FAFBFC);}
.dds tbody tr.dds--sel{cursor:pointer;}
.dds tbody tr:hover,.dds tbody tr.dds--on{background:var(--bg-active,#E6EEF5);
  box-shadow:inset 3px 0 0 var(--accent,#2F6F8F);}
.dds tbody tr.dds--sel:focus-visible{outline:2px solid rgba(47,111,143,.40);outline-offset:-2px;}
.dds__ckbox{width:18px;height:18px;cursor:pointer;border:1px solid var(--border,#D7DCE2);
  border-radius:3px;appearance:none;background:var(--bg-light,#fff);
  transition:border-color 100ms ease,background-color 100ms ease;}
.dds__ckbox:hover{border-color:var(--accent,#2F6F8F);}
.dds__ckbox:checked{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);
  background-image:url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="white"><path d="M13.3 4.3L6 11.6 2.7 8.3" stroke="white" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/></svg>');
  background-size:12px;background-position:center;background-repeat:no-repeat;}
.dds__sort{margin-left:6px;font-size:9px;opacity:.4;}
.dds th.dds--sorted{color:var(--text-secondary,#4D5967);}
.dds th.dds--sorted .dds__sort{opacity:1;color:var(--accent,#2F6F8F);}
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
  onRowClick,
  onSort,
  selectable = false,
  selectedRows = [],
  onSelectRow,
  onSelectAll,
}) {
  inject();
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
    <div className="dds-wrap">
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
              const sorted = sortKey === c.key;
              const cls = `${c.align === "right" ? "dds--r " : ""}${sorted ? "dds--sorted" : ""}`.trim();
              return (
                <th
                  key={c.key}
                  className={cls}
                  onClick={onSort && c.sortable !== false ? () => onSort(c.key) : undefined}
                  style={{ cursor: onSort && c.sortable !== false ? "pointer" : "default" }}
                >
                  {c.label}
                  {onSort && c.sortable !== false && (
                    <span className="dds__sort">{sorted ? (sortDir === "asc" ? "▲" : "▼") : "↕"}</span>
                  )}
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => {
            const isSelected = selectedRows.includes(i);
            return (
              <tr
                key={i}
                className={`${onRowClick ? "dds--sel " : ""}${i === selectedIndex ? "dds--on" : ""}${isSelected ? "dds--on" : ""}`.trim()}
                tabIndex={onRowClick ? 0 : undefined}
                onClick={onRowClick ? () => onRowClick(row, i) : undefined}
              >
                {selectable && (
                  <td className="dds--checkbox">
                    <input
                      type="checkbox"
                      className="dds__ckbox"
                      checked={isSelected}
                      onChange={(e) => handleSelectRow(row, i, e)}
                      onClick={(e) => e.stopPropagation()}
                      aria-label={`Select row ${i + 1}`}
                    />
                  </td>
                )}
                {columns.map((c) => (
                  <td key={c.key} className={c.align === "right" ? "dds--r" : ""}>
                    {c.render ? c.render(row) : row[c.key]}
                  </td>
                ))}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
