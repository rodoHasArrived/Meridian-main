// Meridian FilteredDataTable — compound component unifying FilterBar + DenseDataTable + export.
// Wraps useTableState hook; manages search, column filters, sort, and CSV export in one UI.
// Pass data and columns; renders a complete filtered table with action toolbar.
import React from "react";
import { useTableState } from "./useTableState.js";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.fdt-wrap { display: flex; flex-direction: column; gap: 12px; }
.fdt-toolbar {
  display: flex; align-items: center; gap: 12px; padding: 12px;
  background: var(--bg-medium, #F5F7FA); border-radius: var(--radius-chip, 4px);
  border: 1px solid var(--border, #D7DCE2);
}
.fdt-search { flex: 1; min-width: 200px; }
.fdt-search input {
  width: 100%; height: 32px; padding: 6px 10px; border: 1px solid var(--border, #D7DCE2);
  border-radius: var(--radius-button, 6px); background: var(--bg-light, #fff);
  font-family: var(--font-data); font-size: 12px;
  transition: border-color 100ms ease, box-shadow 100ms ease;
}
.fdt-search input:focus {
  outline: none; border-color: var(--accent, #2F6F8F);
  box-shadow: 0 0 0 2px rgba(47, 111, 143, 0.20);
}
.fdt-actions { display: flex; gap: 8px; }
.fdt-btn {
  padding: 6px 12px; border: 1px solid var(--border, #D7DCE2);
  border-radius: var(--radius-button, 6px); background: var(--bg-light, #fff);
  color: var(--text-primary, #22272E); font-family: var(--font-body);
  font-size: 12px; font-weight: 500; cursor: pointer;
  transition: background 100ms ease, border-color 100ms ease;
}
.fdt-btn:hover { background: var(--bg-active, #E6EEF5); border-color: var(--accent, #2F6F8F); }
.fdt-btn--primary {
  background: var(--accent, #2F6F8F); color: white; border-color: var(--accent, #2F6F8F);
}
.fdt-btn--primary:hover { background: var(--accent-dim, #255B75); }
.fdt-badge {
  display: inline-block; padding: 4px 8px; background: var(--accent, #2F6F8F);
  color: white; border-radius: 3px; font-size: 11px; font-weight: 600;
}
.fdt-table { border: 1px solid var(--border, #D7DCE2); border-radius: var(--radius-chip, 4px); overflow: hidden; }
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
  color: var(--text-muted, #6E7781); border-bottom: 1px solid var(--border, #D7DCE2);
  cursor: pointer; user-select: none;
  transition: background 100ms ease;
}
.fdt-table th:hover { background: var(--bg-light, #fff); }
.fdt-table th.fdt--sorted {
  background: var(--bg-light, #fff); color: var(--accent, #2F6F8F);
}
.fdt-table th.fdt--r { text-align: right; }
.fdt-table td {
  padding: 7px 12px; border-top: 1px solid var(--border, #D7DCE2);
  color: var(--text-primary, #22272E); font-variant-numeric: tabular-nums;
}
.fdt-table tbody tr {
  background: var(--bg-light, #fff); transition: background 100ms ease;
}
.fdt-table tbody tr:nth-child(even) { background: var(--card-surface-raised, #FAFBFC); }
.fdt-table tbody tr:hover { background: var(--bg-active, #E6EEF5); box-shadow: inset 3px 0 0 var(--accent, #2F6F8F); }
.fdt-table td.fdt--r { text-align: right; }
.fdt-empty {
  padding: 48px 24px; text-align: center; color: var(--text-muted, #6E7781);
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
}) {
  inject();

  const state = useTableState(data, localStorageKey);

  const handleSort = (colKey) => {
    const col = columns.find((c) => c.key === colKey);
    if (col?.sortable !== false) state.toggleSort(colKey);
  };

  return (
    <div className="fdt-wrap">
      {title && <h3 style={{ margin: "0 0 12px 0", fontSize: "15px", fontFamily: "var(--font-body)", color: "var(--text-primary, #22272E)" }}>{title}</h3>}

      <div className="fdt-toolbar">
        <div className="fdt-search">
          <input
            type="text"
            placeholder="Search all fields…"
            value={state.query}
            onChange={(e) => state.setQuery(e.target.value)}
          />
        </div>
        <div style={{ fontSize: "12px", color: "var(--text-muted, #6E7781)" }}>
          {state.resultCount} of {state.rawData.length}
          {state.filterCount > 0 && <span className="fdt-badge" style={{ marginLeft: "8px" }}>{state.filterCount} filter{state.filterCount !== 1 ? "s" : ""}</span>}
        </div>
        <div className="fdt-actions">
          {state.filterCount > 0 && (
            <button className="fdt-btn" onClick={state.clearAllFilters}>
              Clear filters
            </button>
          )}
          {onExport ? (
            <button className="fdt-btn fdt-btn--primary" onClick={() => onExport(state)}>
              Export
            </button>
          ) : (
            <button className="fdt-btn" onClick={() => state.exportCSV()}>
              Export CSV
            </button>
          )}
        </div>
      </div>

      <div className="fdt-table">
        {state.data.length === 0 ? (
          <div className="fdt-empty">
            {state.resultCount === 0 && data.length > 0
              ? "No results match your search"
              : "No data"}
          </div>
        ) : (
          <table>
            <thead>
              <tr>
                {columns.map((col) => (
                  <th
                    key={col.key}
                    className={`${state.sortBy?.column === col.key ? "fdt--sorted" : ""} ${col.align === "right" ? "fdt--r" : ""}`}
                    onClick={() => handleSort(col.key)}
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
              {state.data.map((row, idx) => (
                <tr key={idx}>
                  {columns.map((col) => (
                    <td key={col.key} className={col.align === "right" ? "fdt--r" : ""}>
                      {row[col.key]}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
