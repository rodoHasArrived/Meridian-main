// Meridian FilteredDataTable — compound component unifying FilterBar + DenseDataTable + export.
// Wraps useTableState hook; manages search, column filters, sort, and CSV export in one UI.
// Uses Core Input, Button, Checkbox, ColumnChooser primitives.
//
// Rows past `virtualizeThreshold` (default 500) window to the visible slice, same technique as
// DenseDataTable — see that file's comment for why this uses spacer <tr>s rather than
// VirtualizedList directly (VirtualizedList renders <div> rows; a CSS table needs real cells).
import React from "react";
import { useTableState } from "./useTableState.js";
import { useThemeRowHeight } from "./useThemeRowHeight";
import { Input } from "../core/Input";
import { Button } from "../core/Button";
import { Checkbox } from "../core/Checkbox";
import { ColumnChooser } from "./ColumnChooser";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.fdt-wrap { display: flex; flex-direction: column; gap: 12px; }
.fdt-toolbar {
  display: flex; align-items: center; gap: 12px; padding: 12px;
  background: var(--bg-medium, #F5F7FA); border-radius: var(--radius-chip,2px);
  border: 1px solid var(--border, #D7DCE2);
}
.fdt-search { flex: 1; min-width: 200px; }
.fdt-actions { display: flex; gap: 8px; }
.fdt-badge {
  display: inline-block; padding: 4px 8px; background: var(--accent, #2F6F8F);
  color: var(--text-on-accent, white); border-radius: var(--radius-chip,2px); font-size: 11px; font-weight: 600;
}
.fdt-table { border: 1px solid var(--border, #D7DCE2); border-radius: var(--radius-chip,2px); overflow: hidden; }
.fdt-table-scroll { overflow-y: auto; }
.fdt-table table {
  width: 100%; border-collapse: separate; border-spacing: 0;
  font-family: var(--font-data); font-size: 12px;
}
.fdt-table thead {
  position: sticky; top: 0; z-index: 1; background: var(--bg-medium, #F5F7FA);
}
.fdt-table th {
  padding: 9px 12px; text-align: left; white-space: nowrap;
  font-family: var(--font-body); font-size: 10px; font-weight: 600;
  font-variant: all-small-caps; letter-spacing: 0.03em;
  color: var(--text-muted, #59636F); border-bottom: 1px solid var(--border, #D7DCE2);
  cursor: pointer; user-select: none;
  transition: background 100ms ease;
}
.fdt-table th:hover { background: var(--bg-light, #fff); }
.fdt-table th:focus-visible { outline: var(--focus-ring); outline-offset: -2px; color: var(--accent, #2F6F8F); }
.fdt-table th.fdt--sorted {
  background: var(--bg-light, #fff); color: var(--accent, #2F6F8F);
}
.fdt-table th.fdt--r { text-align: right; }
.fdt-table td {
  padding: 7px 12px; border-top: 1px solid var(--border, #D7DCE2);
  color: var(--text-primary, #22272E); font-variant-numeric: tabular-nums;
  height: var(--theme-row-height, 32px); box-sizing: border-box;
}
/* density: row height tracks --theme-row-height; vertical padding compresses to fit */
body[data-theme-density="compact"] .fdt-table td { padding-top: 4px; padding-bottom: 4px; }
body[data-theme-density="spacious"] .fdt-table td { padding-top: 13px; padding-bottom: 13px; }
.fdt-table tbody tr {
  background: var(--bg-light, #fff); transition: background 100ms ease;
}
.fdt-table tbody tr:nth-child(even) { background: var(--card-surface-raised, #FAFBFC); }
.fdt-table tbody tr:hover { background: var(--bg-active, #E6EEF5); box-shadow: inset 3px 0 0 var(--accent, #2F6F8F); }
.fdt-table tbody tr.fdt--spacer, .fdt-table tbody tr.fdt--spacer:hover { background: transparent; box-shadow: none; }
.fdt-table tbody tr.fdt--spacer td { border-top: none; padding: 0; }
.fdt-table td.fdt--r { text-align: right; }
.fdt-empty {
  padding: 48px 24px; text-align: center; color: var(--text-muted, #59636F);
  font-family: var(--font-body); font-size: 13px;
}
`;
  const el = document.createElement("style");
  el.setAttribute("data-fdt", "filtered-data-table");
  el.textContent = css;
  document.head.appendChild(el);
}

export function FilteredDataTable({
  data = [],
  columns = [], // [{ key, label, align?: 'left'|'right', sortable?: bool, filterable?: bool }]
  title = "",
  onExport = null,
  localStorageKey = null,
  virtualize,
  virtualizeThreshold = 500,
  rowHeight, // px — defaults to --theme-row-height (26 terminal · 32 default/compact · 48 spacious)
  maxHeight = 560,
}) {
  inject();
  const effRow = useThemeRowHeight(rowHeight);

  const state = useTableState(data, localStorageKey);

  const handleSort = React.useCallback(
    (colKey) => {
      const col = columns.find((c) => c.key === colKey);
      if (col?.sortable !== false) state.toggleSort(colKey);
    },
    [columns, state.toggleSort]
  );

  const rows = state.data;
  const shouldVirtualize = virtualize ?? rows.length > virtualizeThreshold;
  const [scrollTop, setScrollTop] = React.useState(0);
  const overscan = 8;
  const viewportRows = Math.ceil(maxHeight / effRow);
  const start = shouldVirtualize ? Math.max(0, Math.floor(scrollTop / effRow) - overscan) : 0;
  const end = shouldVirtualize ? Math.min(rows.length, start + viewportRows + overscan * 2) : rows.length;
  const visibleRows = shouldVirtualize ? rows.slice(start, end) : rows;
  const topSpacer = start * effRow;
  const bottomSpacer = (rows.length - end) * effRow;

  return (
    <div className="fdt-wrap">
      {title && <h3 style={{ margin: "0 0 12px 0", fontSize: "15px", fontFamily: "var(--font-body)", color: "var(--text-primary, #22272E)" }}>{title}</h3>}

      <div className="fdt-toolbar">
        <div className="fdt-search">
          <Input
            placeholder="Search all fields…"
            value={state.query}
            onChange={(e) => state.setQuery(e.target.value)}
          />
        </div>
        <div style={{ fontSize: "12px", color: "var(--text-muted, #59636F)" }}>
          {state.resultCount} of {state.rawData.length}
          {state.filterCount > 0 && <span className="fdt-badge" style={{ marginLeft: "8px" }}>{state.filterCount} filter{state.filterCount !== 1 ? "s" : ""}</span>}
        </div>
        <div className="fdt-actions">
          {state.filterCount > 0 && (
            <Button variant="ghost" onClick={state.clearAllFilters}>
              Clear filters
            </Button>
          )}
          {onExport ? (
            <Button variant="primary" onClick={() => onExport(state)}>
              Export
            </Button>
          ) : (
            <Button variant="primary" onClick={() => state.exportCSV()}>
              Export CSV
            </Button>
          )}
        </div>
      </div>

      <div className="fdt-table">
        {rows.length === 0 ? (
          <div className="fdt-empty">
            {state.resultCount === 0 && data.length > 0
              ? "No results match your search"
              : "No data"}
          </div>
        ) : (
          <div
            className={shouldVirtualize ? "fdt-table-scroll" : undefined}
            style={shouldVirtualize ? { maxHeight } : undefined}
            onScroll={shouldVirtualize ? (e) => setScrollTop(e.currentTarget.scrollTop) : undefined}
          >
            <table>
              <thead>
                <tr>
                  {columns.map((col) => (
                    <th
                      key={col.key}
                      className={`${state.sortBy?.column === col.key ? "fdt--sorted" : ""} ${col.align === "right" ? "fdt--r" : ""}`}
                      scope="col"
                      tabIndex={col.sortable !== false ? 0 : undefined}
                      aria-sort={state.sortBy?.column === col.key ? (state.sortBy.direction === "asc" ? "ascending" : "descending") : (col.sortable !== false ? "none" : undefined)}
                      onClick={() => handleSort(col.key)}
                      onKeyDown={col.sortable !== false ? (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); handleSort(col.key); } } : undefined}
                    >
                      {col.label}
                      {col.sortable !== false && state.sortBy?.column === col.key && (
                        <span style={{ marginLeft: "4px" }}>
                          {state.sortBy.direction === "asc" ? "↑" : "↓"}
                        </span>
                      )}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {topSpacer > 0 && (
                  <tr className="fdt--spacer" aria-hidden="true">
                    <td colSpan={columns.length} style={{ height: topSpacer }} />
                  </tr>
                )}
                {visibleRows.map((row, i) => (
                  <tr key={start + i}>
                    {columns.map((col) => (
                      <td key={col.key} className={col.align === "right" ? "fdt--r" : ""}>
                        {row[col.key]}
                      </td>
                    ))}
                  </tr>
                ))}
                {bottomSpacer > 0 && (
                  <tr className="fdt--spacer" aria-hidden="true">
                    <td colSpan={columns.length} style={{ height: bottomSpacer }} />
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
