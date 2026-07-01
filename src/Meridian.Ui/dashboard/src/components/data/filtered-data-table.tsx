// Meridian FilteredDataTable (Concrete) — a self-contained data surface unifying a search
// toolbar + result count + CSV export over a dense table, driven by `useTableState`.
// Flat institutional styling matching DenseDataTable. Re-implemented natively (no core/*
// dependency) so Wave 2 screens can adopt it without waiting on sibling units.
import type { ReactNode } from "react";
import { injectStyle } from "../operations/inject-style";
import { useTableState, type TableState } from "./use-table-state";

const CSS = `
.fdt-wrap{display:flex;flex-direction:column;gap:12px;}
.fdt-toolbar{display:flex;align-items:center;gap:12px;padding:12px;flex-wrap:wrap;
  background:var(--bg-medium,#F3F6F9);border-radius:var(--radius-chip,2px);
  border:1px solid var(--border,#CBD3DC);}
.fdt-search{flex:1;min-width:200px;}
.fdt-search input{width:100%;box-sizing:border-box;height:30px;padding:4px 10px;
  border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-button,2px);
  background:var(--bg-panel,var(--bg-light,#fff));color:var(--text-primary,#22272E);
  font-family:var(--font-body);font-size:12px;transition:border-color .1s ease;}
.fdt-search input:focus{outline:none;border-color:var(--accent,#2F6F8F);}
.fdt-count{font-family:var(--font-data,monospace);font-size:12px;color:var(--text-muted,#59636F);}
.fdt-badge{display:inline-block;margin-left:8px;padding:2px 7px;background:var(--blue-a10,rgba(47,111,143,.10));
  border:1px solid var(--accent,#2F6F8F);color:var(--accent,#2F6F8F);border-radius:var(--radius-chip,2px);
  font-family:var(--font-body);font-size:11px;font-weight:600;}
.fdt-actions{display:flex;gap:8px;margin-left:auto;}
.fdt-btn{appearance:none;height:30px;padding:4px 12px;border-radius:var(--radius-button,2px);
  font-family:var(--font-body);font-size:12px;font-weight:600;cursor:pointer;
  transition:background .1s ease,border-color .1s ease;}
.fdt-btn--ghost{border:1px solid var(--border,#CBD3DC);background:var(--bg-panel,var(--bg-light,#fff));color:var(--text-secondary,#4D5967);}
.fdt-btn--ghost:hover{border-color:var(--accent,#2F6F8F);background:var(--bg-active,#E6EEF5);}
.fdt-btn--primary{border:1px solid var(--accent,#2F6F8F);background:var(--accent,#2F6F8F);color:#fff;}
.fdt-btn--primary:hover{background:var(--accent-pressed,#255B75);border-color:var(--accent-pressed,#255B75);}
.fdt-btn:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:2px;}
.fdt-title{margin:0;font-family:var(--font-body);font-size:15px;font-weight:600;color:var(--text-primary,#22272E);}
.fdt-table{border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-chip,2px);overflow:hidden;}
.fdt-table table{width:100%;border-collapse:separate;border-spacing:0;font-family:var(--font-data,monospace);font-size:12px;}
.fdt-table thead{position:sticky;top:0;z-index:1;background:var(--bg-medium,#F3F6F9);}
.fdt-table th{padding:9px 12px;text-align:left;white-space:nowrap;user-select:none;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.03em;
  color:var(--text-muted,#59636F);border-bottom:1px solid var(--border-strong,#99A5B2);}
.fdt-table th.fdt--sortable{cursor:pointer;transition:background .1s ease;}
.fdt-table th.fdt--sortable:hover{background:var(--bg-panel,var(--bg-light,#fff));}
.fdt-table th.fdt--sorted{background:var(--bg-panel,var(--bg-light,#fff));color:var(--accent,#2F6F8F);}
.fdt-table th.fdt--r{text-align:right;}
.fdt-table td{padding:8px 12px;height:34px;border-top:1px solid var(--border,#CBD3DC);
  color:var(--text-primary,#22272E);font-variant-numeric:tabular-nums;}
.fdt-table tbody tr{background:var(--bg-panel,var(--bg-light,#fff));}
.fdt-table tbody tr:nth-child(even){background:var(--card-surface-raised,#F3F6F9);}
.fdt-table tbody tr:hover{background:var(--bg-active,#E6EEF5);box-shadow:inset 3px 0 0 var(--accent,#2F6F8F);}
.fdt-table td.fdt--r{text-align:right;}
.fdt-empty{padding:48px 24px;text-align:center;color:var(--text-muted,#59636F);
  font-family:var(--font-body);font-size:13px;}
`;

export interface FilteredDataTableColumn {
  key: string;
  label: ReactNode;
  align?: "left" | "right";
  sortable?: boolean;
  filterable?: boolean;
}

export interface FilteredDataTableProps<Row extends Record<string, unknown> = Record<string, unknown>> {
  data?: Row[];
  columns?: FilteredDataTableColumn[];
  title?: string;
  /** Called instead of the default CSV export; receives the live table state. */
  onExport?: (state: TableState<Row>) => void;
  /** Persist query/sort/filters under this localStorage key. */
  localStorageKey?: string | null;
}

/**
 * FilteredDataTable — search + sort + export over a dense table in one surface.
 *
 * @example
 * <FilteredDataTable title="Positions" data={rows} columns={cols} />
 */
export function FilteredDataTable<Row extends Record<string, unknown> = Record<string, unknown>>({
  data = [],
  columns = [],
  title = "",
  onExport,
  localStorageKey = null,
}: FilteredDataTableProps<Row>) {
  injectStyle("filtered-data-table", CSS);
  const state = useTableState<Row>(data, localStorageKey);

  const handleSort = (colKey: string) => {
    const col = columns.find((c) => c.key === colKey);
    if (col?.sortable !== false) state.toggleSort(colKey);
  };
  const activateKey = (key: string) => key === "Enter" || key === " ";

  return (
    <div className="fdt-wrap">
      {title && <h3 className="fdt-title">{title}</h3>}

      <div className="fdt-toolbar">
        <div className="fdt-search">
          <input
            type="text"
            placeholder="Search all fields…"
            value={state.query}
            onChange={(e) => state.setQuery(e.target.value)}
            aria-label="Search all fields"
          />
        </div>
        <div className="fdt-count">
          {state.resultCount} of {state.rawData.length}
          {state.filterCount > 0 && (
            <span className="fdt-badge">
              {state.filterCount} filter{state.filterCount !== 1 ? "s" : ""}
            </span>
          )}
        </div>
        <div className="fdt-actions">
          {state.filterCount > 0 && (
            <button type="button" className="fdt-btn fdt-btn--ghost" onClick={state.clearAllFilters}>
              Clear filters
            </button>
          )}
          <button
            type="button"
            className="fdt-btn fdt-btn--primary"
            onClick={() => (onExport ? onExport(state) : state.exportCSV())}
          >
            {onExport ? "Export" : "Export CSV"}
          </button>
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
                {columns.map((col) => {
                  const sortable = col.sortable !== false;
                  const sorted = state.sortBy?.column === col.key;
                  const cls = `${sortable ? "fdt--sortable " : ""}${sorted ? "fdt--sorted " : ""}${col.align === "right" ? "fdt--r" : ""}`.trim();
                  return (
                    <th
                      key={col.key}
                      className={cls}
                      onClick={sortable ? () => handleSort(col.key) : undefined}
                      onKeyDown={
                        sortable
                          ? (e) => {
                              if (activateKey(e.key)) {
                                e.preventDefault();
                                handleSort(col.key);
                              }
                            }
                          : undefined
                      }
                      tabIndex={sortable ? 0 : undefined}
                      aria-sort={
                        sorted
                          ? state.sortBy?.direction === "asc"
                            ? "ascending"
                            : "descending"
                          : sortable
                            ? "none"
                            : undefined
                      }
                    >
                      {col.label}
                      {sortable && sorted && (
                        <span style={{ marginLeft: 4 }}>
                          {state.sortBy?.direction === "asc" ? "↑" : "↓"}
                        </span>
                      )}
                    </th>
                  );
                })}
              </tr>
            </thead>
            <tbody>
              {state.data.map((row, idx) => (
                <tr key={idx}>
                  {columns.map((col) => (
                    <td key={col.key} className={col.align === "right" ? "fdt--r" : ""}>
                      {row[col.key] as ReactNode}
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
