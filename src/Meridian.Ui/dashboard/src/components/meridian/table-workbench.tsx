import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { DenseDataTable, nextDenseSort, sortRowsByValue, type DenseDataTableColumn, type DenseDataTableProps, type DenseDataTableSortState } from "@/components/meridian/ui-kit-primitives";
import { downloadCsv, type CsvCell } from "@/lib/csv";
import { readStoredDensity } from "@/lib/density";
import { pluralizeCount } from "@/lib/format";
import { loadTableViews, safeTableCsv, saveTableView, type TableView } from "@/lib/table-workbench";
import { cn } from "@/lib/utils";
import "@/styles/table-workbench.css";

/** Search earns its toolbar space only once a table is long enough to need it. */
export const TABLE_SEARCH_MIN_ROWS = 10;
/** Priorities a table gives up, in order, when it would otherwise overflow its frame. */
const DROP_ORDER = ["low", "medium"] as const;

/** The table density that follows the operator's global density setting (Settings > Preferences). */
function tableDensity(): "compact" | "comfortable" {
  const scope = typeof document === "undefined" ? null : document.body.getAttribute("data-theme-density");
  return (scope ?? readStoredDensity()) === "spacious" ? "comfortable" : "compact";
}

function useGlobalTableDensity(): "compact" | "comfortable" {
  const [density, setDensity] = useState(tableDensity);
  useEffect(() => {
    const update = () => setDensity(tableDensity());
    update();
    if (typeof MutationObserver === "undefined") return;
    const observer = new MutationObserver(update);
    observer.observe(document.body, { attributes: true, attributeFilter: ["data-theme-density"] });
    return () => observer.disconnect();
  }, []);
  return density;
}

/** Bulk actions for checked rows. The bar floats over the table foot and exists only while rows are selected. */
export interface TableWorkbenchSelection {
  count: number;
  /** Singular noun for the selected things, e.g. "inbox item". Defaults to "row". */
  noun?: string;
  actions: ReactNode;
  onClear: () => void;
}

export interface TableWorkbenchColumn<T> extends DenseDataTableColumn<T> {
  /**
   * How much the column matters when the table sits beside a detail rail. "low" columns are
   * dropped first, then "medium", so the table never clips or scrolls sideways; "high" (the
   * default) always stays. A dropped column is still in the Columns menu and in exports.
   */
  priority?: "high" | "medium" | "low";
  /**
   * Let this column's text wrap (a title with a sentence under it). The table then narrows by
   * wrapping it before any column is dropped; figures and identifiers stay on one line.
   */
  wrap?: boolean;
  /** Original source value, before financial display formatting. Omit for action columns. */
  exportValue?: (row: T) => CsvCell;
  /** Use only for exact, unformatted decimal source strings. Text never acquires numeric semantics implicitly. */
  exportType?: "decimal" | "text";
}

/** Bind explicit source cells; rendered JSX is never parsed back into financial values. */
export function tableColumnsWithExport<T>(columns: DenseDataTableColumn<T>[], accessors: Record<string, (row: T) => CsvCell>): TableWorkbenchColumn<T>[] {
  return columns.map(column => ({ ...column, exportValue: accessors[column.id] }));
}

export interface TableWorkbenchProps<T> extends Omit<DenseDataTableProps<T>, "columns"> {
  workbenchId: string;
  /** Include tenant/account/book identity when the same table remains mounted across scope changes. */
  sourceKey?: string;
  columns: TableWorkbenchColumn<T>[];
  searchText?: string;
  onSearchTextChange?: (value: string) => void;
  getSearchText?: (row: T) => string;
  filters?: ReactNode;
  /** Keep query controls visible; disclose column, density, export and saved-view tools. */
  compactToolbar?: boolean;
  filterState?: Record<string, string>;
  onRestoreFilters?: (filters: Record<string, string>) => void;
  onRestoreView?: (view: TableView) => void;
  /** True only when rows cover the entire authorized query population, before the DOM row cap. */
  completePopulation?: boolean;
  sourceDescription: string;
  /** Source-provided scope, denomination, basis and observation time; never inferred from display values. */
  exportContext?: Record<string, string>;
  /** Name a deliberately bounded source explicitly, for example Export preview CSV. */
  exportLabel?: string;
  /** Bulk-selection bar, shown only while `selection.count > 0`. */
  selection?: TableWorkbenchSelection;
  /** Server export for paginated or incomplete populations. Must use the active query. */
  onExportAll?: (columns: string[], search: string, sort?: DenseDataTableSortState | null, signal?: AbortSignal) => Promise<void>;
}

export function TableWorkbench<T>({ workbenchId, sourceKey = "", columns, rows, searchText: controlledSearch,
  onSearchTextChange, getSearchText, filters, compactToolbar = false, filterState, onRestoreFilters, onRestoreView, completePopulation = false,
  sourceDescription, exportContext, exportLabel = "Export CSV", onExportAll, selection, sort, onToggleSort, defaultSort = null,
  hiddenColumnIds: controlledHiddenColumnIds, onHiddenColumnIdsChange, ...tableProps }: TableWorkbenchProps<T>) {
  const id = useId();
  const [localSearch, setLocalSearch] = useState("");
  const search = controlledSearch ?? localSearch;
  const [visibleIds, setVisibleIds] = useState<string[]>(columns.filter(column => !column.defaultHidden).map(column => column.id));
  const columnSchema = JSON.stringify(columns.map(column => [column.id, Boolean(column.defaultHidden)]));
  const previousSchema = useRef({ workbenchId, columnIds: columns.map(column => column.id) });
  // Density is one global preference, not a control on every table.
  const density = useGlobalTableDensity();
  // How many priority tiers (see DROP_ORDER) are currently dropped to keep the table inside its frame.
  const [dropLevel, setDropLevel] = useState(0);
  const droppedAtWidth = useRef<number[]>([]);
  const [localSort, setLocalSort] = useState<DenseDataTableSortState | null>(defaultSort);
  const ownsSort = onToggleSort === undefined && columns.some(column => column.sortValue !== undefined);
  const sortColumn = ownsSort ? columns.find(column => column.id === localSort?.columnId && column.sortValue) : undefined;
  const effectiveSort = ownsSort ? sortColumn ? localSort : null : sort;
  const toggleSort = ownsSort ? (columnId: string) => {
    const column = columns.find(candidate => candidate.id === columnId);
    if (column?.sortValue) setLocalSort(current => nextDenseSort(current, column));
  } : onToggleSort;
  const [views, setViews] = useState<TableView[]>([]);
  const [selectedView, setSelectedView] = useState("");
  const [viewName, setViewName] = useState("");
  const [saving, setSaving] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState("");
  const frame = useRef<HTMLDivElement>(null);
  const [scrollEdges, setScrollEdges] = useState({ left: false, right: false });
  const session = useRef(0);
  const writeController = useRef<AbortController | null>(null);
  useEffect(() => {
    const schema = JSON.parse(columnSchema) as [string, boolean][];
    const columnIds = schema.map(([columnId]) => columnId);
    const defaults = schema.filter(([, hidden]) => !hidden).map(([columnId]) => columnId);
    const previous = previousSchema.current;
    previousSchema.current = { workbenchId, columnIds };
    setVisibleIds(current => {
      if (previous.workbenchId !== workbenchId) return defaults.length ? defaults : columnIds;
      // Preserve hidden columns and saved-view order for surviving fields, but show
      // new result fields immediately rather than retaining an empty old SQL schema.
      const retained = [...current.filter(columnId => columnIds.includes(columnId)),
        ...defaults.filter(columnId => !previous.columnIds.includes(columnId))];
      const next = retained.length ? retained : defaults.length ? defaults : columnIds;
      return next.length === current.length && next.every((columnId, index) => columnId === current[index]) ? current : next;
    });
    setSelectedView("");
  }, [workbenchId, columnSchema]);
  useEffect(() => {
    const controller = new AbortController();
    const current = ++session.current;
    writeController.current?.abort();
    writeController.current = new AbortController();
    setSaving(false); setExporting(false); setError(null); setNotice("");
    setViews([]);
    setSelectedView("");
    loadTableViews(workbenchId, controller.signal).then(result => {
      if (current === session.current && !controller.signal.aborted) setViews(result);
    }).catch(() => {
      if (!controller.signal.aborted && current === session.current) setNotice("Saved views unavailable. You can continue reviewing this table.");
    });
    return () => { controller.abort(); writeController.current?.abort(); ++session.current; };
  }, [workbenchId, sourceKey]);
  const controlledVisibility = controlledHiddenColumnIds !== undefined && onHiddenColumnIdsChange !== undefined;
  const hasDeclaredHideableColumns = columns.some(column => column.hideable || column.defaultHidden);
  const hideableColumns = hasDeclaredHideableColumns ? columns.filter(column => column.hideable || column.defaultHidden) : columns;
  const effectiveVisibleIds = controlledVisibility
    ? [...visibleIds, ...columns.map(column => column.id).filter(columnId => !visibleIds.includes(columnId))]
      .filter(columnId => !controlledHiddenColumnIds.includes(columnId) || !hideableColumns.some(column => column.id === columnId))
    : visibleIds;
  const visibleColumns = effectiveVisibleIds.map(columnId => columns.find(column => column.id === columnId)).filter((column): column is TableWorkbenchColumn<T> => Boolean(column));
  // Beside a detail rail the table drops its least important columns rather than clipping the
  // last one or scrolling sideways: one tier at a time, only while the table really overflows.
  const droppedPriorities: readonly string[] = DROP_ORDER.slice(0, dropLevel);
  const fittedColumns = visibleColumns.filter(column => !droppedPriorities.includes(column.priority ?? "high"));
  const nextDroppableTier = DROP_ORDER.slice(dropLevel).findIndex(tier => visibleColumns.some(column => column.priority === tier));
  const droppedColumnCount = visibleColumns.length - fittedColumns.length;
  // TableWorkbench owns the single Columns control, including caller-owned URL state.
  // DenseDataTable must render these exact fields without a second visibility layer.
  const tableColumns = fittedColumns.map(column => ({ ...column, hideable: false, defaultHidden: false,
    className: column.wrap ? cn(column.className, "table-workbench-cell--wrap") : column.className,
    sortable: ownsSort ? Boolean(column.sortValue) : column.sortable }));
  function changeVisibleIds(next: string[]) {
    const required = hasDeclaredHideableColumns ? columns.filter(column => !column.hideable && !column.defaultHidden).map(column => column.id) : [];
    const valid = [...next, ...required.filter(columnId => !next.includes(columnId))];
    setVisibleIds(valid);
    if (controlledVisibility) onHiddenColumnIdsChange(columns.filter(column => !valid.includes(column.id)).map(column => column.id));
  }
  useEffect(() => {
    const scroller = frame.current?.querySelector<HTMLElement>(".dense-data-table-wrap");
    if (!scroller) return;
    const update = () => {
      const left = scroller.scrollLeft > 1;
      const right = scroller.scrollLeft + scroller.clientWidth < scroller.scrollWidth - 1;
      setScrollEdges(current => current.left === left && current.right === right ? current : { left, right });
      const width = scroller.clientWidth;
      if (width > 0 && scroller.scrollWidth > width + 1 && nextDroppableTier >= 0) {
        // Still too wide: give up the next tier that has columns, and remember the width it happened at.
        const level = dropLevel + nextDroppableTier + 1;
        droppedAtWidth.current[level] = width;
        setDropLevel(level);
      } else if (dropLevel > 0 && width > (droppedAtWidth.current[dropLevel] ?? Infinity) + 48) {
        // The frame grew (the rail closed, the window widened): try the fuller table again.
        setDropLevel(0);
      }
    };
    update();
    scroller.addEventListener("scroll", update, { passive: true });
    window.addEventListener("resize", update);
    const observer = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(update);
    observer?.observe(scroller);
    const table = scroller.querySelector("table");
    if (table) observer?.observe(table);
    return () => { scroller.removeEventListener("scroll", update); window.removeEventListener("resize", update); observer?.disconnect(); };
  }, [visibleIds, rows, density, search, dropLevel, nextDroppableTier]);
  const filteredRows = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    const matches = query && getSearchText ? rows.filter(row => getSearchText(row).toLocaleLowerCase().includes(query)) : rows;
    return sortColumn?.sortValue && effectiveSort ? sortRowsByValue(matches, sortColumn.sortValue, effectiveSort.direction) : matches;
  }, [rows, search, getSearchText, sortColumn, effectiveSort]);
  const exportColumns = visibleColumns.filter(column => column.exportValue);
  function setSearch(value: string) {
    if (onSearchTextChange) onSearchTextChange(value);
    else setLocalSearch(value);
  }
  function restoreView(viewId: string) {
    const view = views.find(candidate => candidate.viewId === viewId);
    setSelectedView(viewId);
    if (!view) return;
    const validIds = view.columnIds.filter(columnId => columns.some(column => column.id === columnId));
    if (validIds.length) changeVisibleIds(validIds);
    setSearch(view.searchText);
    if (ownsSort) setLocalSort(view.sortColumnId && columns.some(column => column.id === view.sortColumnId && column.sortValue)
      ? { columnId: view.sortColumnId, direction: view.sortDirection === "desc" ? "desc" : "asc" } : null);
    onRestoreFilters?.(view.filters ?? {});
    onRestoreView?.(view);
    setViewName(view.isReadOnly ? `${view.label} copy` : view.label);
    setNotice(view.isReadOnly ? "Legacy tenant view. Save a copy to your account." : `Restored ${view.label}.`);
  }
  async function saveView() {
    if (!viewName.trim()) { setError("Enter a view name."); return; }
    const current = session.current;
    setSaving(true); setError(null);
    try {
      const saved = await saveTableView(workbenchId, { label: viewName.trim(), searchText: search,
        columnIds: visibleColumns.map(column => column.id), density,
        sortColumnId: effectiveSort?.columnId, sortDirection: effectiveSort?.direction, filters: filterState }, writeController.current?.signal);
      if (current !== session.current) return;
      setViews(existing => [...existing, saved]); setSelectedView(saved.viewId); setNotice(`Saved ${saved.label} to your account.`);
    } catch (cause) { if (current === session.current) setError(cause instanceof Error ? cause.message : "View could not be saved."); }
    finally { if (current === session.current) setSaving(false); }
  }
  async function exportAll() {
    const current = session.current;
    const signal = writeController.current?.signal;
    setExporting(true); setError(null);
    try {
      if (onExportAll) {
        await onExportAll(exportColumns.map(column => column.id), search, effectiveSort, signal);
        if (current === session.current && !signal?.aborted) setNotice("Exported all authorized matches for the active query.");
      }
      else if (completePopulation) {
        const context = { Source: sourceDescription, ...exportContext, "Exported at UTC": new Date().toISOString() };
        const csv = safeTableCsv([...exportColumns.map(column => column.label), ...Object.keys(context).map(key => `Context · ${key}`)],
          filteredRows.map(row => [...exportColumns.map(column => {
            const value = column.exportValue!(row);
            return column.exportType === "decimal" && typeof value === "string" ? { kind: "decimal" as const, value } : value;
          }), ...Object.values(context)]));
        if (!downloadCsv(`${workbenchId}-${new Date().toISOString().replace(/[:.]/g, "-")}.csv`, csv)) throw new Error("CSV downloads are unavailable in this browser.");
        setNotice(`Exported all ${pluralizeCount(filteredRows.length, "filtered match", { plural: "filtered matches" })}. ${sourceDescription}`);
      }
    } catch (cause) { if (current === session.current && !signal?.aborted) setError(cause instanceof Error ? cause.message : "Export failed. No complete export was produced."); }
    finally { if (current === session.current) setExporting(false); }
  }
  const columnControls = hideableColumns.map(column => <label key={column.id}><input type="checkbox" checked={effectiveVisibleIds.includes(column.id)}
    disabled={visibleColumns.length === 1 && effectiveVisibleIds.includes(column.id)}
    onChange={event => changeVisibleIds(event.target.checked ? columns.filter(item => effectiveVisibleIds.includes(item.id) || item.id === column.id).map(item => item.id) : effectiveVisibleIds.filter(value => value !== column.id))} />{column.label || column.srLabel || column.id}</label>);
  const searchable = Boolean(getSearchText || onSearchTextChange);
  // A short table is read, not searched. An active query keeps the field so it can be cleared.
  // A server-backed table (`onExportAll`) searches a population larger than the loaded rows, so
  // its row count says nothing about whether search is needed.
  const showSearch = searchable && (rows.length > TABLE_SEARCH_MIN_ROWS || search.trim() !== "" || onExportAll !== undefined);
  const closeOnEscape = (event: KeyboardEvent<HTMLDetailsElement>) => {
    if (event.key === "Escape") {
      event.preventDefault();
      event.currentTarget.open = false;
      event.currentTarget.querySelector("summary")?.focus();
    }
  };
  const exportControl = <Button size="sm" variant="outline" onClick={() => void exportAll()} disabled={exporting || !exportColumns.length || (!completePopulation && !onExportAll)}
    title={!completePopulation && !onExportAll ? "A complete authorized source is required before export." : `Export all ${pluralizeCount(filteredRows.length, "filtered match", { plural: "filtered matches" })}. ${sourceDescription}`}>{exporting ? "Exporting…" : exportLabel}</Button>;
  const viewControls = <>
    <label>Saved views<select value={selectedView} onChange={event => restoreView(event.target.value)}><option value="">Current view</option>{views.map(view => <option key={view.viewId} value={view.viewId}>{view.label}{view.isReadOnly ? " · Legacy tenant view" : ""}</option>)}</select></label>
    <label>View name<input value={viewName} maxLength={100} onChange={event => setViewName(event.target.value)} /></label>
    <Button size="sm" onClick={() => void saveView()} disabled={saving}>{saving ? "Saving…" : "Save to my account"}</Button>
  </>;
  const unavailableViews = compactToolbar && notice === "Saved views unavailable. You can continue reviewing this table.";

  return (
    <section className={cn("table-workbench", `table-workbench--${density}`, fittedColumns.some(column => column.wrap) && "table-workbench--wrapping")} aria-label={`${tableProps.ariaLabel} controls and results`}>
      <div className={cn("table-workbench-toolbar", compactToolbar && "table-workbench-toolbar--compact")} role="group" aria-label={`${tableProps.ariaLabel} table controls`}>
        {showSearch ? <label className="table-workbench-search" htmlFor={`${id}-search`}>Search
          <input id={`${id}-search`} type="search" value={search} onChange={event => setSearch(event.target.value)} />
        </label> : null}
        {filters}
        {showSearch || filters ? <span className="table-workbench-count">{pluralizeCount(filteredRows.length, "match", { plural: "matches" })}</span> : null}
        <div className="table-workbench-tools">
          {droppedColumnCount > 0 ? <span className="table-workbench-view-notice" title="Widen the table or close the detail pane to show every column. Hidden columns are still exported.">{pluralizeCount(droppedColumnCount, "column")} hidden to fit</span> : null}
          {unavailableViews ? <span role="status" title={notice} className="table-workbench-view-notice">Views unavailable</span> : null}
          {compactToolbar ? <details className="table-workbench-disclosure" onKeyDown={closeOnEscape}>
            <summary aria-label={`${tableProps.ariaLabel} table options`}>Table options</summary>
            <div className="table-workbench-menu table-workbench-options">
              <fieldset><legend>Columns</legend>{columnControls}</fieldset>
              {exportControl}
              <fieldset><legend>Save view</legend>{viewControls}</fieldset>
            </div>
          </details> : <>
            <details className="table-workbench-disclosure" onKeyDown={closeOnEscape}><summary>Columns</summary><div className="table-workbench-menu">{columnControls}</div></details>
            <details className="table-workbench-disclosure" onKeyDown={closeOnEscape}>
              <summary className="table-workbench-more" aria-label={`${tableProps.ariaLabel} more table actions`} title="Export and saved views"><span aria-hidden="true">⋯</span></summary>
              <div className="table-workbench-menu table-workbench-options">
                {exportControl}
                <fieldset><legend>Save view</legend>{viewControls}</fieldset>
              </div>
            </details>
          </>}
        </div>
      </div>
      {error ? <p role="alert" className="text-danger text-sm">{error}</p> : null}
      {notice && !unavailableViews ? <p role="status" className="text-muted-foreground text-xs">{notice}</p> : null}
      <div ref={frame} className="table-workbench-frame" data-scroll-left={scrollEdges.left} data-scroll-right={scrollEdges.right}>
        <DenseDataTable {...tableProps} columns={tableColumns} rows={filteredRows} sort={effectiveSort} onToggleSort={toggleSort} />
      </div>
      {selection && selection.count > 0 ? (
        <div className="table-workbench-selection" role="region" aria-label={`${tableProps.ariaLabel} selection actions`}>
          <span className="table-workbench-selection-count" role="status">{pluralizeCount(selection.count, selection.noun ?? "row")} selected</span>
          {selection.actions}
          <Button type="button" size="sm" variant="ghost" onClick={selection.onClear}>Clear selection</Button>
        </div>
      ) : null}
    </section>
  );
}
