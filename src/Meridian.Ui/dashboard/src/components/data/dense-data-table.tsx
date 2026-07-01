// Meridian dense data grid (Concrete) — the flat institutional table. White paper panel,
// small-caps muted headers, hairline row borders, off-white raised zebra rows, and a
// steel-blue left rail + wash on the hovered/selected row. ~34px rows, mono tabular
// numerics. Optional checkbox column for multi-select workflows and header sort arrows.
import type { ReactNode } from "react";
import { injectStyle } from "../operations/inject-style";

const CSS = `
.dds-wrap{overflow-x:auto;border:1px solid var(--border,#CBD3DC);background:var(--bg-panel,var(--bg-light,#fff));}
.dds{width:100%;min-width:100%;border-collapse:separate;border-spacing:0;
  font-family:var(--font-data,monospace);font-size:12px;}
.dds thead{position:sticky;top:0;z-index:1;background:var(--bg-medium,#F3F6F9);}
.dds th{padding:9px 12px;text-align:left;white-space:nowrap;
  font-family:var(--font-body);font-size:10px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#59636F);
  border-bottom:1px solid var(--border-strong,#99A5B2);border-right:1px solid var(--border-divider,#DDE3EA);}
.dds th:last-child{border-right:none;}
.dds th.dds--r{text-align:right;}
.dds th.dds--checkbox{padding:9px 10px;width:34px;text-align:center;}
.dds td{padding:8px 12px;white-space:nowrap;color:var(--text-primary,#22272E);
  border-top:1px solid var(--border,#CBD3DC);border-right:1px solid var(--border-divider,#DDE3EA);
  font-variant-numeric:tabular-nums;height:34px;}
.dds td:last-child{border-right:none;}
.dds td.dds--checkbox{padding:6px 10px;text-align:center;}
.dds tbody tr:first-child td{border-top:none;}
.dds td.dds--r{text-align:right;}
.dds tbody tr{background:var(--bg-panel,var(--bg-light,#fff));}
.dds tbody tr:nth-child(even){background:var(--card-surface-raised,#F3F6F9);}
.dds tbody tr.dds--sel{cursor:pointer;}
.dds tbody tr:hover,.dds tbody tr.dds--on{background:var(--bg-active,#E6EEF5);box-shadow:inset 3px 0 0 var(--accent,#2F6F8F);}
.dds tbody tr.dds--sel:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:-2px;}
.dds__ckbox{width:16px;height:16px;cursor:pointer;border:1px solid var(--border,#CBD3DC);
  border-radius:var(--radius-chip,2px);appearance:none;background:var(--bg-panel,var(--bg-light,#fff));}
.dds__ckbox:hover{border-color:var(--accent,#2F6F8F);}
.dds__ckbox:checked{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);
  background-image:url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><path d="M13.3 4.3L6 11.6 2.7 8.3" stroke="white" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/></svg>');
  background-size:12px;background-position:center;background-repeat:no-repeat;}
.dds__sort{margin-left:6px;font-size:9px;opacity:.4;}
.dds th.dds--sorted{color:var(--text-secondary,#4D5967);}
.dds th.dds--sorted .dds__sort{opacity:1;color:var(--accent,#2F6F8F);}
`;

export interface DataTableColumn<Row = Record<string, unknown>> {
  key: string;
  label: ReactNode;
  align?: "left" | "right";
  /** Show a sort affordance for this column. @default true when onSort is set */
  sortable?: boolean;
  /** Custom cell renderer; receives the row object. */
  render?: (row: Row) => ReactNode;
}

export interface DenseDataTableProps<Row = Record<string, unknown>> {
  columns: DataTableColumn<Row>[];
  rows: Row[];
  /** Row index highlighted with the steel-blue rail. @default -1 */
  selectedIndex?: number;
  /** Active sort column key. */
  sortKey?: string;
  /** @default "asc" */
  sortDir?: "asc" | "desc";
  onRowClick?: (row: Row, index: number) => void;
  onSort?: (key: string) => void;
  /** Enable the checkbox column for multi-select. @default false */
  selectable?: boolean;
  /** Row indices currently selected. */
  selectedRows?: number[];
  /** Fired when a row checkbox is toggled. Receives (row, index, isSelected). */
  onSelectRow?: (row: Row, index: number, isSelected: boolean) => void;
  /** Fired when the "select all" checkbox is toggled. Receives isSelected. */
  onSelectAll?: (isSelected: boolean) => void;
}

/**
 * Dense mono data grid — the workhorse of every Meridian screen.
 *
 * @example
 * <DenseDataTable
 *   selectedIndex={2}
 *   sortKey="pnl" sortDir="desc" onSort={setSort}
 *   onRowClick={(row, i) => select(i)}
 *   columns={[
 *     { key: "symbol", label: "Symbol" },
 *     { key: "qty", label: "Qty", align: "right" },
 *   ]}
 *   rows={positions}
 * />
 */
export function DenseDataTable<Row extends Record<string, unknown> = Record<string, unknown>>({
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
}: DenseDataTableProps<Row>) {
  injectStyle("dds", CSS);
  const allSelected = selectable && selectedRows.length === rows.length && rows.length > 0;
  const someSelected = selectable && selectedRows.length > 0 && selectedRows.length < rows.length;

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
              const cls = `${c.align === "right" ? "dds--r " : ""}${sorted ? "dds--sorted" : ""}`.trim();
              return (
                <th
                  key={c.key}
                  className={cls}
                  onClick={sortable ? () => onSort?.(c.key) : undefined}
                  style={{ cursor: sortable ? "pointer" : "default" }}
                >
                  {c.label}
                  {sortable && (
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
            const on = i === selectedIndex || isSelected;
            return (
              <tr
                key={i}
                className={`${onRowClick ? "dds--sel " : ""}${on ? "dds--on" : ""}`.trim()}
                tabIndex={onRowClick ? 0 : undefined}
                onClick={onRowClick ? () => onRowClick(row, i) : undefined}
              >
                {selectable && (
                  <td className="dds--checkbox">
                    <input
                      type="checkbox"
                      className="dds__ckbox"
                      checked={isSelected}
                      onChange={() => onSelectRow?.(row, i, !isSelected)}
                      onClick={(e) => e.stopPropagation()}
                      aria-label={`Select row ${i + 1}`}
                    />
                  </td>
                )}
                {columns.map((c) => (
                  <td key={c.key} className={c.align === "right" ? "dds--r" : ""}>
                    {c.render ? c.render(row) : (row[c.key] as ReactNode)}
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
