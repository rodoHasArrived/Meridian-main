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
  type MouseEvent as ReactMouseEvent,
  type ReactNode
} from "react";
import {
  DENSE_ROW_DETAIL_KEYBOARD_INSTRUCTIONS,
  buildDenseRowDetailAnnouncement,
  focusDenseRowDetailPanel,
  registerDenseRowRevealHandler
} from "@/components/meridian/dense-row-detail-accessibility";
import { eyebrowClassName, usesTrackedCaps } from "@/design-system/label-case";
import {
  computeRevealScrollTop,
  computeRowWindow,
  resolveTypeaheadIndex
} from "@/lib/dense-virtualization";
import type { Density } from "@/lib/density";
import { getLocalStorage } from "@/lib/theme";
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
            <span className={usesTrackedCaps(item.label) ? undefined : "meridian-toolbar-chip-sentence"}>{item.label}</span>
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
  /**
   * Raw value for the table's built-in sorting. Giving one makes the column sortable when the
   * table has no `onToggleSort` handler; numbers sort numerically, strings naturally
   * ("A2" before "A10"), and null/undefined/NaN always sort last.
   */
  sortValue?: (row: T) => string | number | null | undefined;
  /** Numeric column: right-aligned with tabular figures so digits line up down the column. */
  numeric?: boolean;
  /** Operators may hide this column from the table's Columns menu. */
  hideable?: boolean;
  /** Starts hidden until an operator shows it (implies `hideable`). */
  defaultHidden?: boolean;
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
   * Initial order for built-in sorting (columns with `sortValue`). Ignored when the caller owns
   * sorting through `onToggleSort`.
   */
  defaultSort?: DenseDataTableSortState | null;
  /**
   * Row rhythm for this table. Omit to follow the operator's workstation density
   * (`data-theme-density` on the body, set from Settings).
   */
  density?: Density;
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
  /**
   * Right-click (context-menu gesture) on a data row. Receives the row and the
   * originating event so callers can open a positioned context menu at the
   * pointer. Optional; when omitted rows carry no context-menu behaviour.
   */
  onRowContextMenu?: (row: T, event: ReactMouseEvent<HTMLTableRowElement>) => void;
}

function DenseDataTableComponent<T>({
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
  sort: controlledSort = null,
  onToggleSort,
  defaultSort = null,
  density,
  maxVisibleRows = DEFAULT_DENSE_TABLE_ROW_CAP,
  virtualization = null,
  getRowTypeaheadText,
  onRowContextMenu,
  rows: sourceRows,
  columns: allColumns
}: DenseDataTableProps<T>) {
  // Built-in sorting applies only when the caller does not own the sort order.
  const builtInSort = onToggleSort === undefined && allColumns.some((column) => column.sortValue !== undefined);
  const [internalSort, setInternalSort] = useState<DenseDataTableSortState | null>(defaultSort);
  const sort = builtInSort ? internalSort : controlledSort;
  const toggleSort = builtInSort
    ? (columnId: string) => setInternalSort((current) => {
      const column = allColumns.find((candidate) => candidate.id === columnId);
      return column ? nextDenseSort(current, column) : current;
    })
    : onToggleSort;
  const sortColumn = builtInSort && sort ? allColumns.find((column) => column.id === sort.columnId) : undefined;
  const rows = useMemo(
    () => (sortColumn?.sortValue && sort ? sortRowsByValue(sourceRows, sortColumn.sortValue, sort.direction) : sourceRows),
    [sourceRows, sortColumn, sort]
  );

  const [hiddenColumnIds, setHiddenColumnIds] = useDenseColumnVisibility(allColumns, tableId);
  const hideableColumns = allColumns.filter(isHideableColumn);
  const columns = hiddenColumnIds.size === 0 ? allColumns : allColumns.filter((column) => !hiddenColumnIds.has(column.id));

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
            const sortable = builtInSort ? column.sortValue !== undefined : Boolean(column.sortable && onToggleSort);
            return (
              <DenseDataTableHeaderCell
                key={column.id}
                column={column}
                sort={sortable ? sort : undefined}
                onToggleSort={sortable ? () => toggleSort?.(column.id) : undefined}
              />
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
                  onContextMenu={onRowContextMenu ? (event) => onRowContextMenu(row, event) : undefined}
                  onKeyDown={selectable ? (event) => handleRowKeyDown(event, absoluteIndex) : undefined}
                >
                  {columns.map((column) => (
                    <td
                      key={column.id}
                      className={cn(columnCellClassName(column), column.className)}
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

  const wrapElement = (
    <div
      className={cn("dense-data-table-wrap", isVirtual ? "dense-data-table-wrap-virtual" : undefined)}
      data-empty={rows.length === 0 ? "true" : undefined}
      data-density={density}
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

  if (hideableColumns.length === 0) {
    return wrapElement;
  }

  const visibleCount = columns.length;
  return (
    <div className="dense-data-table-frame">
      <details className="dense-data-table-columns">
        <summary>Columns</summary>
        <fieldset className="dense-data-table-columns-menu">
          <legend className="sr-only">Show columns in {ariaLabel}</legend>
          {hideableColumns.map((column) => {
            const shown = !hiddenColumnIds.has(column.id);
            return (
              <label key={column.id}>
                <input
                  type="checkbox"
                  checked={shown}
                  // The last visible column stays on so the table never renders empty.
                  disabled={shown && visibleCount <= 1}
                  onChange={() => setHiddenColumnIds(toggleHiddenColumn(hiddenColumnIds, column.id))}
                />
                <span>{column.label || column.srLabel || column.id}</span>
              </label>
            );
          })}
        </fieldset>
      </details>
      {wrapElement}
    </div>
  );
}

export const DenseDataTable = memo(DenseDataTableComponent) as typeof DenseDataTableComponent;

const COLUMN_VISIBILITY_STORAGE_PREFIX = "meridian.workstation.table-columns.v1:";

function isHideableColumn<T>(column: DenseDataTableColumn<T>): boolean {
  return column.hideable === true || column.defaultHidden === true;
}

function columnCellClassName(column: { align?: "left" | "right"; numeric?: boolean }): string {
  const right = column.align === "right" || (column.numeric === true && column.align !== "left");
  return cn(right ? "text-right" : "text-left", column.numeric ? "dense-data-table-numeric" : undefined);
}

function toggleHiddenColumn(hidden: ReadonlySet<string>, columnId: string): Set<string> {
  const next = new Set(hidden);
  if (next.has(columnId)) {
    next.delete(columnId);
  } else {
    next.add(columnId);
  }
  return next;
}

/**
 * Hidden-column state for a table. A table with an id remembers the operator's choice in
 * browser storage (best-effort: blocked storage falls back to the column defaults).
 */
function useDenseColumnVisibility<T>(
  columns: DenseDataTableColumn<T>[],
  tableId: string | undefined
): [ReadonlySet<string>, (next: Set<string>) => void] {
  const storageKey = tableId ? `${COLUMN_VISIBILITY_STORAGE_PREFIX}${tableId}` : null;
  const [hidden, setHidden] = useState<ReadonlySet<string>>(() => {
    const hideable = new Set(columns.filter(isHideableColumn).map((column) => column.id));
    if (hideable.size === 0) {
      return new Set();
    }
    const stored = storageKey ? readStoredHiddenColumns(storageKey) : null;
    const initial = stored ?? columns.filter((column) => column.defaultHidden).map((column) => column.id);
    return new Set(initial.filter((id) => hideable.has(id)));
  });

  const update = useCallback((next: Set<string>) => {
    setHidden(next);
    if (storageKey) {
      try {
        getLocalStorage()?.setItem(storageKey, JSON.stringify([...next]));
      } catch {
        // Column choices are a per-viewer convenience; blocked storage keeps them for the session.
      }
    }
  }, [storageKey]);

  return [hidden, update];
}

function readStoredHiddenColumns(storageKey: string): string[] | null {
  try {
    const raw = getLocalStorage()?.getItem(storageKey);
    if (!raw) {
      return null;
    }
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === "string") : null;
  } catch {
    return null;
  }
}

/**
 * The table system's sort cycle: numeric columns start largest-first, text starts A–Z, and a
 * second activation reverses. Exported for grouped tables that render their own rows.
 */
export function nextDenseSort(
  current: DenseDataTableSortState | null,
  column: { id: string; numeric?: boolean }
): DenseDataTableSortState {
  if (current?.columnId === column.id) {
    return { columnId: column.id, direction: current.direction === "asc" ? "desc" : "asc" };
  }
  return { columnId: column.id, direction: column.numeric ? "desc" : "asc" };
}

export interface DenseDataTableHeaderCellProps {
  column: Pick<DenseDataTableColumn<unknown>, "id" | "label" | "srLabel" | "align" | "numeric" | "className">;
  /** The table's sort state; pass it (even `null`) only for a sortable column. */
  sort?: DenseDataTableSortState | null;
  onToggleSort?: () => void;
}

/**
 * One header cell of the table system: alignment, tabular numerics, sentence case for headers
 * longer than two words, and the sort button with `aria-sort`. DenseDataTable renders it, and
 * grouped tables that render their own body rows (the ledger's entry + posting rows) use it so
 * their headers behave identically.
 */
export function DenseDataTableHeaderCell({ column, sort, onToggleSort }: DenseDataTableHeaderCellProps) {
  const sortable = sort !== undefined && onToggleSort !== undefined;
  const sorted = sortable && sort?.columnId === column.id ? sort : null;
  return (
    <th
      scope="col"
      aria-sort={sortable ? sorted ? sorted.direction === "asc" ? "ascending" : "descending" : "none" : undefined}
      className={cn(
        columnCellClassName(column),
        // Header caps are for one- or two-word labels; longer headers read in sentence case.
        usesTrackedCaps(column.label) ? undefined : "dense-data-table-th-sentence",
        sortable ? "dense-data-table-sortable" : undefined,
        sorted ? "dense-data-table-sorted" : undefined,
        column.className
      )}
    >
      {sortable ? (
        <button
          type="button"
          className="dense-data-table-sort-button"
          onClick={onToggleSort}
          aria-label={buildSortButtonAriaLabel(column, sorted)}
        >
          <span>{column.label}</span>
          <span className="dense-data-table-sort-indicator" aria-hidden="true">
            {sorted ? sorted.direction === "asc" ? "↑" : "↓" : "↕"}
          </span>
        </button>
      ) : column.label || (column.srLabel ? <span className="sr-only">{column.srLabel}</span> : null)}
    </th>
  );
}

function isMissingSortValue(value: string | number | null | undefined): value is null | undefined {
  return value === null || value === undefined || (typeof value === "number" && Number.isNaN(value));
}

/** Stable sort by a column's raw value; missing values stay at the end in either direction. */
export function sortRowsByValue<T>(
  rows: T[],
  sortValue: (row: T) => string | number | null | undefined,
  direction: "asc" | "desc"
): T[] {
  const factor = direction === "asc" ? 1 : -1;
  return rows
    .map((row, index) => ({ row, index, value: sortValue(row) }))
    .sort((left, right) => {
      const leftMissing = isMissingSortValue(left.value);
      const rightMissing = isMissingSortValue(right.value);
      if (leftMissing || rightMissing) {
        return leftMissing === rightMissing ? left.index - right.index : leftMissing ? 1 : -1;
      }
      const compared = typeof left.value === "number" && typeof right.value === "number"
        ? left.value - right.value
        : String(left.value).localeCompare(String(right.value), "en-US", { numeric: true, sensitivity: "base" });
      return compared === 0 ? left.index - right.index : compared * factor;
    })
    .map((entry) => entry.row);
}

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

function buildSortButtonAriaLabel(
  column: { label: string },
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

const FIGURE_VALUE = /^[+\-−]?[$€£¥]?\(?[+\-−]?[$€£¥]?\d[\d,.\s]*\)?\s?(%|[A-Z]{3})?$/;
const IDENTIFIER_VALUE = /^(?=[^\s]*\d)[A-Za-z0-9][A-Za-z0-9._:/-]{3,}$/;

/**
 * Whether a summary value is a figure ("$1,250.00", "(500.00)", "12.5%", "500.00 USD") or an
 * identifier ("brk-2026-0042", "2026-06-30"), which keep the data face; phrases, statuses and
 * written-out dates read in the body face.
 */
export function isDataLikeValue(value: string): boolean {
  const text = value.trim();
  return text.length > 0 && (FIGURE_VALUE.test(text) || IDENTIFIER_VALUE.test(text));
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
          <div className={eyebrowClassName(eyebrow)}>{eyebrow}</div>
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
            <dd className={isDataLikeValue(field.value) ? "entity-summary-value--data" : undefined}>{field.value}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
