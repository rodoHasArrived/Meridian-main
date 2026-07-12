import {
  memo,
  useCallback,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent,
  type ReactNode
} from "react";
import {
  DENSE_ROW_DETAIL_KEYBOARD_INSTRUCTIONS,
  buildDenseRowDetailAnnouncement,
  focusDenseRowDetailPanel,
  registerDenseRowRevealHandler
} from "@/components/meridian/dense-row-detail-accessibility";
import {
  computeRevealScrollTop,
  computeRowWindow,
  resolveTypeaheadIndex
} from "@/lib/dense-virtualization";
import { cn } from "@/lib/utils";
import "@/styles/ui-kit-primitives.css";

const DEFAULT_VIRTUALIZATION_OVERSCAN = 6;
const DEFAULT_VIRTUALIZATION_VIEWPORT_ROWS = 12;
const TYPEAHEAD_RESET_MS = 500;

// Default DOM-row guard for non-virtualized tables. Fixed-height tables should
// pass `virtualization` for true windowing; everything else renders at most this
// many rows and surfaces a "Show all N rows" control, so a large data set can
// never dump thousands of nodes into the DOM by omission. Pass `maxVisibleRows={null}`
// to opt a table out of the cap explicitly.
export const DEFAULT_DENSE_TABLE_ROW_CAP = 100;

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
  /** Screen-reader-only header text for control columns whose visible label is empty. */
  srLabel?: string;
  align?: "left" | "right";
  className?: string;
  sortable?: boolean;
  render: (row: T) => ReactNode;
}

export interface DenseDataTableSortState {
  columnId: string;
  direction: "asc" | "desc";
}

export interface DenseDataTableVirtualization {
  /** Fixed pixel height of every row. Stable heights keep the offset math exact. */
  rowHeight: number;
  /** Number of rows the scroll viewport shows at once. Default 12. */
  viewportRowCount?: number;
  /** Rows rendered beyond the viewport on each side to smooth fast scrolls. Default 6. */
  overscan?: number;
}

export interface DenseDataTableProps<T> {
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
  /**
   * Soft cap on rows mounted into the DOM for non-virtualized tables; excess
   * rows collapse behind a "Show all N rows" control. Defaults to
   * {@link DEFAULT_DENSE_TABLE_ROW_CAP}. Pass an explicit number to tune it, or
   * `null` to render every row (only safe for bounded data sets). Ignored while
   * `virtualization` is active, which windows the full set instead.
   */
  maxVisibleRows?: number | null;
  /**
   * Enable row windowing for large data sets. When set, only a window of rows is
   * mounted and the table exposes `aria-rowcount`/`aria-rowindex` so assistive
   * tech reports positions across the full set. Requires stable row heights.
   */
  virtualization?: DenseDataTableVirtualization | null;
  /** Per-row text used for keyboard type-ahead; defaults to `getRowAriaLabel`. */
  getRowTypeaheadText?: (row: T) => string | undefined;
}

function DenseDataTableComponent<T>({
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
  onToggleSort,
  maxVisibleRows = DEFAULT_DENSE_TABLE_ROW_CAP,
  virtualization = null,
  getRowTypeaheadText
}: DenseDataTableProps<T>) {
  const generatedKeyboardInstructionsId = useId();
  const [showAllRows, setShowAllRows] = useState(false);
  const [selectionAnnouncement, setSelectionAnnouncement] = useState("");
  const [scrollTop, setScrollTop] = useState(0);
  const [pendingFocusRowId, setPendingFocusRowId] = useState<string | null>(null);

  // The wrap is itself the scroll viewport (max-height + overflow), so windowing
  // reads and drives its scrollTop directly rather than nesting a second scroller.
  const wrapRef = useRef<HTMLDivElement | null>(null);
  const scrollRafRef = useRef<number | null>(null);
  const typeaheadRef = useRef<{ buffer: string; at: number }>({ buffer: "", at: 0 });

  const rowHeight = virtualization?.rowHeight ?? 0;
  const isVirtual = virtualization != null && rowHeight > 0 && rows.length > 0;
  const overscan = virtualization?.overscan ?? DEFAULT_VIRTUALIZATION_OVERSCAN;
  const viewportRowCount = virtualization?.viewportRowCount ?? DEFAULT_VIRTUALIZATION_VIEWPORT_ROWS;
  const viewportHeight = rowHeight * viewportRowCount;

  const visibleRowLimit = typeof maxVisibleRows === "number" && maxVisibleRows > 0 ? maxVisibleRows : null;
  // Windowing supersedes the "show all" soft cap: a virtualized table renders the
  // full set through spacer rows, so the cap only applies when not virtualizing.
  const cappedRows = !isVirtual && visibleRowLimit && !showAllRows ? rows.slice(0, visibleRowLimit) : rows;
  const hiddenRowCount = Math.max(0, rows.length - cappedRows.length);
  // navRows is the addressable set for keyboard traversal — the full data set when
  // virtualizing, the rendered slice otherwise (cappedRows === rows when virtual).
  const navRows = cappedRows;

  const rowWindow = isVirtual
    ? computeRowWindow({ scrollTop, rowHeight, rowCount: rows.length, viewportHeight, overscan })
    : null;
  const renderRows = rowWindow ? rows.slice(rowWindow.startIndex, rowWindow.endIndex) : cappedRows;
  const renderBaseIndex = rowWindow ? rowWindow.startIndex : 0;

  const selectableRows = onRowSelect !== undefined && cappedRows.length > 0;
  const exposesExpandedRows = getRowAriaExpanded !== undefined;
  const keyboardInstructionsId = `${tableId ?? generatedKeyboardInstructionsId}-keyboard-instructions`;
  const focusableRowId = resolveFocusableDenseRowId(renderRows, getRowId, selectedRowId);

  const getAnnouncementLabel = (row: T) => getRowSelectAriaLabel?.(row) ?? getRowAriaLabel?.(row);
  // Precompute type-ahead labels once per row-set change rather than on every
  // keystroke (the map is O(N) and allocates a fresh array each call).
  const typeaheadLabels = useMemo(
    () => navRows.map((row) => getRowTypeaheadText?.(row) ?? getRowAriaLabel?.(row) ?? ""),
    [navRows, getRowTypeaheadText, getRowAriaLabel]
  );

  // `scrollTop` state is the source of truth for the window math (it is what the
  // current render used); we also push it onto the DOM node so the real browser
  // actually scrolls. Reading el.scrollTop here would be wrong under jsdom, which
  // has no layout.
  const revealRowIndex = (index: number) => {
    if (rowHeight <= 0) return;
    const next = computeRevealScrollTop({ index, rowHeight, scrollTop, viewportHeight });
    if (next !== scrollTop) {
      if (wrapRef.current) {
        wrapRef.current.scrollTop = next;
      }
      setScrollTop(next);
    }
  };

  const focusRowById = useCallback((rowId: string) => {
    const el = wrapRef.current?.querySelector<HTMLTableRowElement>(
      `tr[data-dense-row-id="${escapeAttributeSelectorValue(rowId)}"]`
    );
    if (el) {
      el.focus();
      return true;
    }
    return false;
  }, []);

  const moveToRow = (targetIndex: number) => {
    if (!onRowSelect) return;
    const targetRow = navRows[targetIndex];
    if (!targetRow) return;
    const targetId = getRowId(targetRow);
    if (isVirtual) {
      // Bring the target into the window; if it is not mounted yet, defer focus
      // until the reveal-driven re-render mounts it.
      if (focusRowById(targetId)) {
        revealRowIndex(targetIndex);
      } else {
        revealRowIndex(targetIndex);
        setPendingFocusRowId(targetId);
      }
    } else {
      focusRowById(targetId);
    }
    selectDenseRow(
      targetRow,
      getAnnouncementLabel(targetRow),
      getRowAriaControls?.(targetRow),
      false,
      onRowSelect,
      setSelectionAnnouncement
    );
  };

  const handleRowKeyDown = (event: KeyboardEvent<HTMLTableRowElement>, currentIndex: number) => {
    if (!onRowSelect) return;
    if (event.target !== event.currentTarget && isInteractiveTableTarget(event.target)) return;

    const currentRow = navRows[currentIndex];

    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      if (currentRow) {
        selectDenseRow(
          currentRow,
          getAnnouncementLabel(currentRow),
          getRowAriaControls?.(currentRow),
          true,
          onRowSelect,
          setSelectionAnnouncement
        );
      }
      return;
    }

    const targetIndex = resolveKeyboardTargetRowIndex(event.key, currentIndex, navRows.length);
    if (targetIndex !== null) {
      event.preventDefault();
      moveToRow(targetIndex);
      return;
    }

    // Type-ahead: a printable character jumps to the next matching row across the
    // full addressable set (so windowed rows remain reachable without scrolling).
    if (event.key.length === 1 && event.key !== " " && !event.ctrlKey && !event.metaKey && !event.altKey) {
      const now = Date.now();
      const store = typeaheadRef.current;
      const continued = now - store.at < TYPEAHEAD_RESET_MS && store.buffer.length > 0;
      const buffer = continued ? store.buffer + event.key : event.key;
      store.buffer = buffer;
      store.at = now;
      const matchIndex = resolveTypeaheadIndex({
        labels: typeaheadLabels,
        buffer,
        fromIndex: currentIndex,
        inclusive: continued
      });
      if (matchIndex !== null) {
        event.preventDefault();
        moveToRow(matchIndex);
      }
    }
  };

  const handleScroll = useCallback(() => {
    if (scrollRafRef.current != null) return;
    scrollRafRef.current = window.requestAnimationFrame(() => {
      scrollRafRef.current = null;
      const current = wrapRef.current;
      if (current) {
        setScrollTop(current.scrollTop);
      }
    });
  }, []);

  useEffect(
    () => () => {
      if (scrollRafRef.current != null) {
        cancelAnimationFrame(scrollRafRef.current);
      }
    },
    []
  );

  // Focus a row that was not mounted when navigation targeted it, once the
  // reveal-driven scroll has mounted it into the window.
  useEffect(() => {
    if (!pendingFocusRowId) return;
    const el = wrapRef.current?.querySelector<HTMLTableRowElement>(
      `tr[data-dense-row-id="${escapeAttributeSelectorValue(pendingFocusRowId)}"]`
    );
    if (el) {
      el.focus();
      setPendingFocusRowId(null);
    }
  }, [pendingFocusRowId, scrollTop]);

  // Let Escape from the detail panel return focus to a selected row that windowing
  // has scrolled out of view.
  // Memoized so scroll-driven re-renders don't repeat the O(N) lookup; the deps
  // are stable across scroll (only scrollTop state changes then).
  const selectedRow = useMemo(
    () => (selectedRowId != null ? rows.find((row) => getRowId(row) === selectedRowId) : undefined),
    [rows, selectedRowId, getRowId]
  );
  const selectedPanelId = selectedRow ? getRowAriaControls?.(selectedRow) : undefined;
  const revealSelectedRef = useRef<() => boolean>(() => false);
  revealSelectedRef.current = () => {
    if (!isVirtual || selectedRowId == null) return false;
    const index = rows.findIndex((row) => getRowId(row) === selectedRowId);
    if (index < 0) return false;
    revealRowIndex(index);
    setPendingFocusRowId(selectedRowId);
    return true;
  };
  useEffect(() => {
    if (!isVirtual || !selectedPanelId) return;
    return registerDenseRowRevealHandler(selectedPanelId, () => revealSelectedRef.current());
  }, [isVirtual, selectedPanelId]);

  const tableElement = (
    <table
      id={tableId}
      role={exposesExpandedRows ? "treegrid" : undefined}
      className="dense-data-table"
      aria-label={ariaLabel}
      aria-describedby={selectableRows ? keyboardInstructionsId : undefined}
      aria-rowcount={isVirtual ? rows.length + 1 : undefined}
    >
      {caption ? <caption className="sr-only">{caption}</caption> : null}
      <thead>
        <tr aria-rowindex={isVirtual ? 1 : undefined}>
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
                      {sorted ? sort.direction === "asc" ? "↑" : "↓" : "↕"}
                    </span>
                  </button>
                ) : column.label || (column.srLabel ? <span className="sr-only">{column.srLabel}</span> : null)}
              </th>
            );
          })}
        </tr>
      </thead>
      <tbody>
        {rows.length === 0 ? (
          <tr>
            <td colSpan={columns.length} className="dense-data-table-empty">
              {emptyText}
            </td>
          </tr>
        ) : (
          <>
            {rowWindow && rowWindow.topPad > 0 ? (
              <tr aria-hidden="true" className="dense-data-table-spacer">
                <td colSpan={columns.length} style={{ height: rowWindow.topPad, padding: 0 }} />
              </tr>
            ) : null}
            {renderRows.map((row, index) => {
              const absoluteIndex = renderBaseIndex + index;
              const rowId = getRowId(row);
              const selected = selectedRowId === rowId;
              const selectable = onRowSelect !== undefined;
              const rowAriaLabel = selectable
                ? getRowSelectAriaLabel?.(row) ?? getRowAriaLabel?.(row)
                : getRowAriaLabel?.(row);
              const rowAriaExpanded = selectable ? getRowAriaExpanded?.(row) : undefined;
              const rowStyle: CSSProperties | undefined = isVirtual ? { height: rowHeight } : undefined;
              return (
                <tr
                  key={rowId}
                  aria-label={rowAriaLabel}
                  aria-controls={selectable ? getRowAriaControls?.(row) : undefined}
                  aria-expanded={rowAriaExpanded}
                  aria-selected={selected || undefined}
                  aria-rowindex={isVirtual ? absoluteIndex + 2 : undefined}
                  tabIndex={selectable ? rowId === focusableRowId ? 0 : -1 : undefined}
                  data-selectable={selectable ? "true" : undefined}
                  data-dense-row-id={selectable ? rowId : undefined}
                  style={rowStyle}
                  className={cn(selectable ? "selectable" : undefined, selected ? "selected" : undefined, getRowClassName?.(row))}
                  onClick={selectable ? (event) => {
                    if (isInteractiveTableTarget(event.target)) return;
                    selectDenseRow(row, rowAriaLabel, getRowAriaControls?.(row), false, onRowSelect, setSelectionAnnouncement);
                  } : undefined}
                  onKeyDown={selectable ? (event) => handleRowKeyDown(event, absoluteIndex) : undefined}
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
            })}
            {rowWindow && rowWindow.bottomPad > 0 ? (
              <tr aria-hidden="true" className="dense-data-table-spacer">
                <td colSpan={columns.length} style={{ height: rowWindow.bottomPad, padding: 0 }} />
              </tr>
            ) : null}
          </>
        )}
      </tbody>
    </table>
  );

  return (
    <div
      className={cn("dense-data-table-wrap", isVirtual ? "dense-data-table-wrap-virtual" : undefined)}
      data-empty={rows.length === 0 ? "true" : undefined}
      ref={wrapRef}
      style={isVirtual ? { maxHeight: viewportHeight } : undefined}
      onScroll={isVirtual ? handleScroll : undefined}
    >
      {selectableRows ? (
        <p id={keyboardInstructionsId} className="sr-only">
          {DENSE_ROW_DETAIL_KEYBOARD_INSTRUCTIONS}
        </p>
      ) : null}
      {selectableRows ? (
        <div role="status" aria-live="polite" aria-atomic="true" className="sr-only">
          {selectionAnnouncement}
        </div>
      ) : null}
      {tableElement}
      {hiddenRowCount > 0 ? (
        <div className="dense-data-table-row-cap">
          <button type="button" onClick={() => setShowAllRows(true)}>
            Show all {rows.length} rows
          </button>
        </div>
      ) : null}
    </div>
  );
}

export const DenseDataTable = memo(DenseDataTableComponent) as typeof DenseDataTableComponent;

function resolveFocusableDenseRowId<T>(
  rows: T[],
  getRowId: (row: T) => string,
  selectedRowId?: string | null
): string | null {
  if (rows.length === 0) {
    return null;
  }

  if (selectedRowId && rows.some((row) => getRowId(row) === selectedRowId)) {
    return selectedRowId;
  }

  return getRowId(rows[0]);
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

function selectDenseRow<T>(
  row: T,
  rowAnnouncementLabel: string | undefined,
  rowDetailPanelId: string | undefined,
  moveFocusToDetailPanel: boolean,
  onRowSelect: (row: T) => void,
  setSelectionAnnouncement: (announcement: string) => void
) {
  onRowSelect(row);
  if (rowAnnouncementLabel) {
    setSelectionAnnouncement(buildDenseRowDetailAnnouncement(rowAnnouncementLabel));
  }
  if (moveFocusToDetailPanel) {
    focusDenseRowDetailPanel(rowDetailPanelId);
  }
}

function resolveKeyboardTargetRowIndex(key: string, rowIndex: number, rowCount: number): number | null {
  switch (key) {
    case "ArrowDown":
      return Math.min(rowCount - 1, rowIndex + 1);
    case "ArrowUp":
      return Math.max(0, rowIndex - 1);
    case "Home":
      return 0;
    case "End":
      return rowCount - 1;
    default:
      return null;
  }
}

function escapeAttributeSelectorValue(value: string): string {
  return value.replace(/\\/g, "\\\\").replace(/"/g, "\\\"");
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
  id,
  eyebrow,
  title,
  subtitle,
  description,
  status,
  fields,
  actions,
  ariaLabel
}: {
  id?: string;
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
    <section id={id} className="entity-summary" role="region" aria-label={ariaLabel}>
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
