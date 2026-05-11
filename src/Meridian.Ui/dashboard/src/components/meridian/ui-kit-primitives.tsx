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
  render: (row: T) => ReactNode;
}

export function DenseDataTable<T>({
  columns,
  rows,
  getRowId,
  getRowAriaLabel,
  getRowAriaControls,
  getRowAriaExpanded,
  getRowSelectAriaLabel,
  onRowSelect,
  selectedRowId,
  emptyText,
  ariaLabel,
  caption
}: {
  columns: DenseDataTableColumn<T>[];
  rows: T[];
  getRowId: (row: T) => string;
  getRowAriaLabel?: (row: T) => string;
  getRowAriaControls?: (row: T) => string | undefined;
  getRowAriaExpanded?: (row: T) => boolean | undefined;
  getRowSelectAriaLabel?: (row: T) => string;
  onRowSelect?: (row: T) => void;
  selectedRowId?: string | null;
  emptyText: string;
  ariaLabel: string;
  caption?: string | null;
}) {
  return (
    <div className="dense-data-table-wrap">
      <table className="dense-data-table" aria-label={ariaLabel}>
        {caption ? <caption className="sr-only">{caption}</caption> : null}
        <thead>
          <tr>
            {columns.map((column) => (
              <th
                key={column.id}
                scope="col"
                className={cn(column.align === "right" ? "text-right" : "text-left", column.className)}
              >
                {column.label}
              </th>
            ))}
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
                className={cn(selectable ? "selectable" : undefined, selected ? "selected" : undefined)}
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
