import type { KeyboardEvent, ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface ToolbarStripItem {
  id: string;
  label: string;
  value?: string;
  active?: boolean;
}

export function ToolbarStrip({
  items,
  right,
  ariaLabel
}: {
  items: ToolbarStripItem[];
  right?: ReactNode;
  ariaLabel: string;
}) {
  return (
    <div className="meridian-toolbar-strip" role="toolbar" aria-label={ariaLabel}>
      <div className="meridian-toolbar-strip-items">
        {items.map((item) => (
          <span
            key={item.id}
            className={cn("meridian-toolbar-chip", item.active ? "active" : "")}
            aria-label={item.value ? `${item.label}: ${item.value}` : item.label}
          >
            <span>{item.label}</span>
            {item.value ? <b>{item.value}</b> : null}
          </span>
        ))}
      </div>
      {right ? <div className="meridian-toolbar-right">{right}</div> : null}
    </div>
  );
}

export interface DenseDataTableColumn<T> {
  id: string;
  label: string;
  align?: "left" | "right";
  className?: string;
  sortable?: boolean;
  render: (row: T) => ReactNode;
}

export interface DenseDataTableSortState {
  columnId: string;
  direction: "asc" | "desc";
}

export function DenseDataTable<T>({
  columns,
  rows,
  getRowId,
  getRowAriaLabel,
  getRowAriaControls,
  getRowAriaExpanded,
  getRowClassName,
  getRowSelectAriaLabel,
  onRowSelect,
  selectedRowId,
  emptyText,
  ariaLabel,
  tableId,
  caption,
  sort = null,
  onToggleSort
}: {
  columns: DenseDataTableColumn<T>[];
  rows: T[];
  getRowId: (row: T) => string;
  getRowAriaLabel?: (row: T) => string;
  getRowAriaControls?: (row: T) => string | undefined;
  getRowAriaExpanded?: (row: T) => boolean | undefined;
  getRowClassName?: (row: T) => string | undefined;
  getRowSelectAriaLabel?: (row: T) => string;
  onRowSelect?: (row: T) => void;
  selectedRowId?: string | null;
  emptyText: string;
  ariaLabel: string;
  tableId?: string;
  caption?: string | null;
  sort?: DenseDataTableSortState | null;
  onToggleSort?: (columnId: string) => void;
}) {
  return (
    <div className="dense-data-table-wrap">
      <table id={tableId} className="dense-data-table" aria-label={ariaLabel}>
        {caption ? <caption className="sr-only">{caption}</caption> : null}
        <thead>
          <tr>
            {columns.map((column) => {
              const sortable = Boolean(column.sortable && onToggleSort);
              const sorted = sortable && sort?.columnId === column.id;
              return (
                <th
                  key={column.id}
                  scope="col"
                  aria-sort={sortable ? sorted ? sort.direction === "asc" ? "ascending" : "descending" : "none" : undefined}
                  className={cn(
                    column.align === "right" ? "text-right" : "text-left",
                    sortable ? "dense-data-table-sortable" : undefined,
                    sorted ? "dense-data-table-sorted" : undefined,
                    column.className
                  )}
                >
                  {sortable ? (
                    <button
                      type="button"
                      className="dense-data-table-sort-button"
                      onClick={() => onToggleSort?.(column.id)}
                      aria-label={buildSortButtonAriaLabel(column, sorted ? sort : null)}
                    >
                      <span>{column.label}</span>
                      <span className="dense-data-table-sort-indicator" aria-hidden="true">
                        {sorted ? sort.direction === "asc" ? "^" : "v" : "-"}
                      </span>
                    </button>
                  ) : column.label}
                </th>
              );
            })}
          </tr>
        </thead>
        <tbody>
          {rows.length > 0 ? rows.map((row) => {
            const rowId = getRowId(row);
            const selected = selectedRowId === rowId;
            const selectable = onRowSelect !== undefined;
            const rowAriaLabel = selectable
              ? getRowSelectAriaLabel?.(row) ?? getRowAriaLabel?.(row)
              : getRowAriaLabel?.(row);
            const rowAriaExpanded = selectable ? getRowAriaExpanded?.(row) : undefined;
            return (
              <tr
                key={rowId}
                aria-label={rowAriaLabel}
                aria-controls={selectable ? getRowAriaControls?.(row) : undefined}
                aria-expanded={rowAriaExpanded}
                aria-selected={selected || undefined}
                tabIndex={selectable ? 0 : undefined}
                data-selectable={selectable ? "true" : undefined}
                className={cn(selectable ? "selectable" : undefined, selected ? "selected" : undefined, getRowClassName?.(row))}
                onClick={selectable ? (event) => {
                  if (isInteractiveTableTarget(event.target)) return;
                  onRowSelect(row);
                } : undefined}
                onKeyDown={selectable ? (event) => {
                  handleSelectableRowKeyDown(event, row, onRowSelect);
                } : undefined}
              >
                {columns.map((column) => (
                  <td
                    key={column.id}
                    className={cn(column.align === "right" ? "text-right" : "text-left", column.className)}
                  >
                    {column.render(row)}
                  </td>
                ))}
              </tr>
            );
          }) : (
            <tr>
              <td colSpan={columns.length} className="dense-data-table-empty">
                {emptyText}
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}

function buildSortButtonAriaLabel<T>(
  column: DenseDataTableColumn<T>,
  sort: DenseDataTableSortState | null
): string {
  if (!sort) {
    return `Sort by ${column.label}`;
  }

  return `${column.label} sorted ${sort.direction === "asc" ? "ascending" : "descending"}. Activate to change sort.`;
}

function handleSelectableRowKeyDown<T>(
  event: KeyboardEvent<HTMLTableRowElement>,
  row: T,
  onRowSelect: (row: T) => void
) {
  if (event.target !== event.currentTarget) return;
  if (event.key !== "Enter" && event.key !== " ") return;

  event.preventDefault();
  onRowSelect(row);
}

function isInteractiveTableTarget(target: EventTarget): boolean {
  if (!(target instanceof Element)) return false;
  return target.closest("a,button,input,select,textarea,[role='button'],[role='link']") !== null;
}

export interface EntitySummaryField {
  label: string;
  value: string;
}

export function EntitySummary({
  eyebrow,
  title,
  subtitle,
  description,
  status,
  fields,
  actions,
  ariaLabel
}: {
  eyebrow: string;
  title: string;
  subtitle: string;
  description: string;
  status: ReactNode;
  fields: EntitySummaryField[];
  actions?: ReactNode;
  ariaLabel: string;
}) {
  return (
    <section className="entity-summary" role="region" aria-label={ariaLabel}>
      <div className="entity-summary-head">
        <div className="min-w-0">
          <div className="eyebrow-label">{eyebrow}</div>
          <h3 className="entity-summary-title">{title}</h3>
          <p className="entity-summary-subtitle">{subtitle}</p>
          <p className="entity-summary-description">{description}</p>
        </div>
        <div className="entity-summary-actions">
          {status}
          {actions}
        </div>
      </div>
      <dl className="entity-summary-grid">
        {fields.map((field) => (
          <div key={field.label} className="entity-summary-field">
            <dt>{field.label}</dt>
            <dd>{field.value}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
