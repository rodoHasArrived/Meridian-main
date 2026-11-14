import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { DenseDataTable, nextDenseSort, sortRowsByValue, type DenseDataTableColumn, type DenseDataTableProps, type DenseDataTableSortState } from "@/components/meridian/ui-kit-primitives";
import { downloadCsv, type CsvCell } from "@/lib/csv";
import { pluralizeCount } from "@/lib/format";
import { loadTableViews, safeTableCsv, saveTableView, type TableView } from "@/lib/table-workbench";
import { cn } from "@/lib/utils";
import "@/styles/table-workbench.css";

export interface TableWorkbenchColumn<T> extends DenseDataTableColumn<T> {
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
  /** Keep search and filters on one compact row; disclose secondary tools without changing their behavior. */
  compactToolbar?: boolean;
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
  /** Server export for paginated or incomplete populations. Must use the active query. */
  onExportAll?: (columns: string[], search: string, sort?: DenseDataTableSortState | null, signal?: AbortSignal) => Promise<void>;
}

export function TableWorkbench<T>({ workbenchId, compactToolbar = false, sourceKey = "", columns, rows, searchText: controlledSearch,
  onSearchTextChange, getSearchText, filters, filterState, onRestoreFilters, onRestoreView, completePopulation = false,
  sourceDescription, exportContext, exportLabel = "Export CSV", onExportAll, sort, onToggleSort, defaultSort = null,
  hiddenColumnIds: controlledHiddenColumnIds, onHiddenColumnIdsChange, ...tableProps }: TableWorkbenchProps<T>) {
  const id = useId();
  const toolsRef = useRef<HTMLDetailsElement>(null);
  useEffect(() => {
    if (!compactToolbar) return;
    if (toolsRef.current) toolsRef.current.open = false;
    const dismiss = (event: PointerEvent) => {
      if (event.target instanceof Node && toolsRef.current && !toolsRef.current.contains(event.target)) toolsRef.current.open = false;
    };
    document.addEventListener("pointerdown", dismiss);
    return () => document.removeEventListener("pointerdown", dismiss);
  }, [compactToolbar, sourceKey, workbenchId]);
  const [localSearch, setLocalSearch] = useState("");
  const search = controlledSearch ?? localSearch;
  const [visibleIds, setVisibleIds] = useState<string[]>(columns.filter(column => !column.defaultHidden).map(column => column.id));
  const columnSchema = JSON.stringify(columns.map(column => [column.id, Boolean(column.defaultHidden)]));
  const previousSchema = useRef({ workbenchId, columnIds: columns.map(column => column.id) });
  const [density, setDensity] = useState<"compact" | "comfortable">("compact");
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
  // TableWorkbench owns the single Columns control, including caller-owned URL state.
  // DenseDataTable must render these exact fields without a second visibility layer.
  const tableColumns = visibleColumns.map(column => ({ ...column, hideable: false, defaultHidden: false,
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
    };
    update();
    scroller.addEventListener("scroll", update, { passive: true });
    window.addEventListener("resize", update);
    const observer = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(update);
    observer?.observe(scroller);
    const table = scroller.querySelector("table");
    if (table) observer?.observe(table);
    return () => { scroller.removeEventListener("scroll", update); window.removeEventListener("resize", update); observer?.disconnect(); };
  }, [visibleIds, rows, density, search]);
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
    setDensity(view.density === "comfortable" ? "comfortable" : "compact");
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
  const secondaryControls = <>
        <details className="table-workbench-disclosure"><summary>Columns</summary><div className="table-workbench-menu">
          {hideableColumns.map(column => <label key={column.id}><input type="checkbox" checked={effectiveVisibleIds.includes(column.id)}
            disabled={visibleColumns.length === 1 && effectiveVisibleIds.includes(column.id)}
            onChange={event => changeVisibleIds(event.target.checked ? columns.filter(item => effectiveVisibleIds.includes(item.id) || item.id === column.id).map(item => item.id) : effectiveVisibleIds.filter(value => value !== column.id))} />{column.label || column.srLabel || column.id}</label>)}
        </div></details>
        <label>Density<select aria-label={`${tableProps.ariaLabel} density`} value={density} onChange={event => setDensity(event.target.value as typeof density)}><option value="compact">Compact</option><option value="comfortable">Comfortable</option></select></label>
        <Button size="sm" variant="outline" onClick={() => void exportAll()} disabled={exporting || !exportColumns.length || (!completePopulation && !onExportAll)}
          title={!completePopulation && !onExportAll ? "A complete authorized source is required before export." : `Export all ${pluralizeCount(filteredRows.length, "filtered match", { plural: "filtered matches" })}. ${sourceDescription}`}>{exporting ? "Exporting…" : exportLabel}</Button>
        <details className="table-workbench-disclosure"><summary>Save view</summary><div className="table-workbench-menu">
          <label>Saved views<select value={selectedView} onChange={event => restoreView(event.target.value)}><option value="">Current view</option>{views.map(view => <option key={view.viewId} value={view.viewId}>{view.label}{view.isReadOnly ? " · Legacy tenant view" : ""}</option>)}</select></label>
          <label>View name<input value={viewName} maxLength={100} onChange={event => setViewName(event.target.value)} /></label>
          <Button size="sm" onClick={() => void saveView()} disabled={saving}>{saving ? "Saving…" : "Save to my account"}</Button>
        </div></details>
  </>;
  return (
    <section className={cn("table-workbench", `table-workbench--${density}`)} aria-label={`${tableProps.ariaLabel} controls and results`}>
      <div className={cn("table-workbench-toolbar", compactToolbar && "table-workbench-toolbar--compact")} role="group" aria-label={`${tableProps.ariaLabel} table controls`}>
        {getSearchText || onSearchTextChange ? <label className="table-workbench-search" htmlFor={`${id}-search`}>Search
          <input id={`${id}-search`} type="search" value={search} onChange={event => setSearch(event.target.value)} />
        </label> : null}
        {filters}
        {compactToolbar ? (
          <details ref={toolsRef} className="table-workbench-disclosure table-workbench-tools" onKeyDown={(event) => {
            if (event.key !== "Escape" || !event.currentTarget.open) return;
            const nested = event.target instanceof Element ? event.target.closest("details") : null;
            const disclosure = nested?.open ? nested : event.currentTarget;
            disclosure.open = false;
            disclosure.querySelector("summary")?.focus();
            event.preventDefault();
            event.stopPropagation();
          }}>
            <summary>Table tools</summary>
            <div className="table-workbench-menu table-workbench-tools-menu" role="group" aria-label={`${tableProps.ariaLabel} secondary table controls`}>{secondaryControls}</div>
          </details>
        ) : secondaryControls}
        <span className="table-workbench-count">{pluralizeCount(filteredRows.length, "match", { plural: "matches" })}</span>
        {unavailableViews ? <span role="status" title={notice} className="table-workbench-view-notice">Views unavailable</span> : null}
      </div>
      {error ? <p role="alert" className="text-danger text-sm">{error}</p> : null}
      {notice && !unavailableViews ? <p role="status" className="text-muted-foreground text-xs">{notice}</p> : null}
      <div ref={frame} className="table-workbench-frame" data-scroll-left={scrollEdges.left} data-scroll-right={scrollEdges.right}>
        <DenseDataTable {...tableProps} columns={tableColumns} rows={filteredRows} sort={effectiveSort} onToggleSort={toggleSort} />
      </div>
    </section>
  );
}
