// Meridian expandable data table (Concrete) — a DenseDataTable with collapsible detail
// rows. Each row can expand to a full-width detail panel (audit trail, allocations,
// related records, …) rendered via the `expandable` slot. Flat institutional styling,
// steel-blue hover rail, and a light slide on expand/collapse.
import { Fragment, useState, type ReactNode } from "react";
import { injectStyle } from "../operations/inject-style";
import type { DataTableColumn } from "./dense-data-table";

const CSS = `
.edt-wrap{overflow-x:auto;border:1px solid var(--border,#CBD3DC);
  border-radius:var(--radius-card,2px);background:var(--bg-panel,var(--bg-light,#fff));}
.edt{width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data,monospace);font-size:12px;}
.edt thead{position:sticky;top:0;z-index:1;background:var(--bg-medium,#F3F6F9);}
.edt th{padding:9px 12px;text-align:left;white-space:nowrap;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);
  border-bottom:1px solid var(--border-strong,#99A5B2);}
.edt th.edt--r{text-align:right;}
.edt th.edt--expand{width:34px;padding:9px 10px;text-align:center;}
.edt th.edt--checkbox{padding:9px 10px;width:34px;text-align:center;}
.edt td{padding:8px 12px;white-space:nowrap;color:var(--text-primary,#22272E);
  border-top:1px solid var(--border,#CBD3DC);font-variant-numeric:tabular-nums;height:34px;}
.edt td.edt--expand{padding:6px 10px;text-align:center;cursor:pointer;}
.edt__expand-btn{appearance:none;border:none;background:transparent;color:inherit;cursor:pointer;
  padding:6px 10px;display:inline-flex;align-items:center;justify-content:center;border-radius:var(--radius-button,2px);}
.edt__expand-btn:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:1px;}
.edt td.edt--checkbox{padding:6px 10px;text-align:center;}
.edt tbody tr:first-child td{border-top:none;}
.edt td.edt--r{text-align:right;}
.edt tbody tr.edt-main{background:var(--bg-panel,var(--bg-light,#fff));}
.edt tbody tr.edt-main:nth-child(4n+1){background:var(--card-surface-raised,#F3F6F9);}
.edt tbody tr.edt-main:hover{background:var(--bg-active,#E6EEF5);box-shadow:inset 3px 0 0 var(--accent,#2F6F8F);}
.edt tbody tr.edt-main.edt--sel{cursor:pointer;}
.edt tbody tr.edt-expand{background:var(--blue-a10,rgba(47,111,143,.10));}
.edt tbody tr.edt-expand td{padding:0;border:none;}
.edt__detail{padding:16px 12px;}
.edt__chevron{display:inline-block;transition:transform 150ms ease;font-size:12px;}
.edt__chevron.edt--open{transform:rotate(90deg);}
.edt__ckbox{width:16px;height:16px;cursor:pointer;border:1px solid var(--border,#CBD3DC);
  border-radius:var(--radius-chip,2px);appearance:none;background:var(--bg-panel,var(--bg-light,#fff));}
.edt__ckbox:hover{border-color:var(--accent,#2F6F8F);}
.edt__ckbox:checked{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);
  background-image:url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><path d="M13.3 4.3L6 11.6 2.7 8.3" stroke="white" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/></svg>');
  background-size:12px;background-position:center;background-repeat:no-repeat;}
.edt__sort{margin-left:6px;font-size:9px;opacity:.4;}
.edt th.edt--sorted{color:var(--text-secondary,#4D5967);}
.edt th.edt--sorted .edt__sort{opacity:1;color:var(--accent,#2F6F8F);}
`;

export type ExpandableDataTableColumn<Row = Record<string, unknown>> = DataTableColumn<Row>;

export interface ExpandableDataTableProps<Row = Record<string, unknown>> {
  columns: ExpandableDataTableColumn<Row>[];
  rows: Row[];
  /**
   * Render function for expanded detail content. Receives (row, index).
   * If omitted, no expand column is shown.
   */
  expandable?: (row: Row, index: number) => ReactNode;
  /** Row index highlighted with the accent rail. @default -1 */
  selectedIndex?: number;
  /** Active sort column key. */
  sortKey?: string;
  /** @default "asc" */
  sortDir?: "asc" | "desc";
  /** Fired when a row is clicked (main row only, not the expand chevron). */
  onRowClick?: (row: Row, index: number) => void;
  onSort?: (key: string) => void;
  /** Enable the checkbox column for multi-select. @default false */
  selectable?: boolean;
  selectedRows?: number[];
  onSelectRow?: (row: Row, index: number, isSelected: boolean) => void;
  onSelectAll?: (isSelected: boolean) => void;
}

/**
 * ExpandableDataTable — DenseDataTable with collapsible per-row detail panels.
 *
 * @example
 * <ExpandableDataTable
 *   columns={[{ key: "date", label: "Date" }, { key: "amount", label: "Amount", align: "right" }]}
 *   rows={transactions}
 *   expandable={(row) => <p>Account: {row.account}</p>}
 * />
 */
export function ExpandableDataTable<Row extends Record<string, unknown> = Record<string, unknown>>({
  columns,
  rows,
  expandable,
  selectedIndex = -1,
  sortKey,
  sortDir = "asc",
  onRowClick,
  onSort,
  selectable = false,
  selectedRows = [],
  onSelectRow,
  onSelectAll,
}: ExpandableDataTableProps<Row>) {
  injectStyle("edt", CSS);
  const [expandedIndices, setExpandedIndices] = useState<Set<number>>(new Set());

  const toggleExpand = (index: number) => {
    setExpandedIndices((current) => {
      const next = new Set(current);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });
  };

  const allSelected = selectable && selectedRows.length === rows.length && rows.length > 0;
  const someSelected = selectable && selectedRows.length > 0 && selectedRows.length < rows.length;
  const colSpan = (expandable ? 1 : 0) + (selectable ? 1 : 0) + columns.length;
  const activateKey = (key: string) => key === "Enter" || key === " ";

  return (
    <div className="edt-wrap">
      <table className="edt">
        <thead>
          <tr>
            {expandable && <th className="edt--expand" />}
            {selectable && (
              <th className="edt--checkbox">
                <input
                  type="checkbox"
                  className="edt__ckbox"
                  checked={allSelected}
                  ref={(el) => {
                    if (el) el.indeterminate = someSelected;
                  }}
                  onChange={() => onSelectAll?.(!allSelected)}
                  aria-label="Select all rows"
                />
              </th>
            )}
            {columns.map((c) => {
              const sorted = sortKey === c.key;
              const sortable = Boolean(onSort) && c.sortable !== false;
              const cls = `${c.align === "right" ? "edt--r " : ""}${sorted ? "edt--sorted" : ""}`.trim();
              return (
                <th
                  key={c.key}
                  className={cls}
                  onClick={sortable ? () => onSort?.(c.key) : undefined}
                  onKeyDown={
                    sortable
                      ? (e) => {
                          if (activateKey(e.key)) {
                            e.preventDefault();
                            onSort?.(c.key);
                          }
                        }
                      : undefined
                  }
                  tabIndex={sortable ? 0 : undefined}
                  aria-sort={sorted ? (sortDir === "asc" ? "ascending" : "descending") : sortable ? "none" : undefined}
                  style={{ cursor: sortable ? "pointer" : "default" }}
                >
                  {c.label}
                  {sortable && (
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
            const on = onRowClick || i === selectedIndex || isSelected;
            return (
              <Fragment key={i}>
                <tr
                  className={`edt-main${on ? " edt--sel" : ""}`}
                  tabIndex={onRowClick ? 0 : undefined}
                  onClick={onRowClick ? () => onRowClick(row, i) : undefined}
                  onKeyDown={
                    onRowClick
                      ? (e) => {
                          if (activateKey(e.key)) {
                            e.preventDefault();
                            onRowClick(row, i);
                          }
                        }
                      : undefined
                  }
                >
                  {expandable && (
                    <td className="edt--expand">
                      <button
                        type="button"
                        className="edt__expand-btn"
                        onClick={(e) => {
                          e.stopPropagation();
                          toggleExpand(i);
                        }}
                        aria-label={isExpanded ? `Collapse row ${i + 1}` : `Expand row ${i + 1}`}
                        aria-expanded={isExpanded}
                      >
                        <span className={`edt__chevron${isExpanded ? " edt--open" : ""}`} aria-hidden="true">
                          ›
                        </span>
                      </button>
                    </td>
                  )}
                  {selectable && (
                    <td className="edt--checkbox">
                      <input
                        type="checkbox"
                        className="edt__ckbox"
                        checked={isSelected}
                        onChange={() => onSelectRow?.(row, i, !isSelected)}
                        onClick={(e) => e.stopPropagation()}
                        aria-label={`Select row ${i + 1}`}
                      />
                    </td>
                  )}
                  {columns.map((c) => (
                    <td key={c.key} className={c.align === "right" ? "edt--r" : ""}>
                      {c.render ? c.render(row) : (row[c.key] as ReactNode)}
                    </td>
                  ))}
                </tr>

                {expandable && isExpanded && (
                  <tr className="edt-expand">
                    <td colSpan={colSpan}>
                      <div className="edt__detail">{expandable(row, i)}</div>
                    </td>
                  </tr>
                )}
              </Fragment>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
