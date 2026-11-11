import { useLocation, useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import type { DenseDataTableSortState } from "@/components/meridian/ui-kit-primitives";

export function filterTradingRows<T>(
  rows: T[], search: string, filter: string,
  searchable: (row: T) => string[], filterValue: (row: T) => string,
  sort: DenseDataTableSortState, sortValue: (row: T, columnId: string) => string
): T[] {
  const needle = search.trim().toLocaleLowerCase();
  return rows.filter((row) => (!filter || filterValue(row) === filter)
    && (!needle || searchable(row).some((value) => value.toLocaleLowerCase().includes(needle))))
    .sort((left, right) => sortValue(left, sort.columnId).localeCompare(sortValue(right, sort.columnId), undefined, { numeric: true })
      * (sort.direction === "asc" ? 1 : -1));
}

/** View preferences only; retained records and command scope never derive from filters. */
export function useTradingTableView<T extends { id: string }>({
  rows, prefix, defaultSort, allowedSorts, searchable, filterValue, sortValue, selectedId
}: {
  rows: T[]; prefix: string; defaultSort: string; allowedSorts: readonly string[];
  searchable: (row: T) => string[]; filterValue: (row: T) => string;
  sortValue: (row: T, columnId: string) => string;
  selectedId: string | null;
}) {
  const location = useLocation();
  const navigate = useNavigate();
  const params = new URLSearchParams(location.search);
  const search = params.get(`${prefix}Search`) ?? "";
  const filter = params.get(`${prefix}Filter`) ?? "";
  const requestedSort = params.get(`${prefix}Sort`) ?? defaultSort;
  const columnId = allowedSorts.includes(requestedSort) ? requestedSort : defaultSort;
  const direction = params.get(`${prefix}Direction`) === "desc" ? "desc" : "asc";
  const sort: DenseDataTableSortState = { columnId, direction };
  const visibleRows = filterTradingRows(rows, search, filter, searchable, filterValue, sort, sortValue);
  const update = (changes: Record<string, string | null>, replace = false) => {
    const next = new URLSearchParams(location.search);
    Object.entries(changes).forEach(([key, value]) => value ? next.set(`${prefix}${key}`, value) : next.delete(`${prefix}${key}`));
    navigate({ pathname: location.pathname, search: next.toString(), hash: location.hash }, { replace });
  };
  return {
    rows: visibleRows, search, filter, sort,
    options: [...new Set([...rows.map(filterValue), ...(filter ? [filter] : [])])].sort(),
    hiddenSelection: selectedId !== null && rows.some((row) => row.id === selectedId) && !visibleRows.some((row) => row.id === selectedId),
    setSearch: (value: string) => update({ Search: value }, true),
    setFilter: (value: string) => update({ Filter: value }),
    toggleSort: (id: string) => update({ Sort: id, Direction: columnId === id && direction === "asc" ? "desc" : "asc" }),
    clear: () => update({ Search: null, Filter: null })
  };
}

export function TradingTableToolbar({ label, filterLabel, total, view, compact = false }: {
  label: string; filterLabel: string; total: number; compact?: boolean;
  view: { rows: unknown[]; search: string; filter: string; options: string[]; hiddenSelection: boolean;
    setSearch: (value: string) => void; setFilter: (value: string) => void; clear: () => void };
}) {
  return <div className={compact ? "trading-orders-table-toolbar space-y-1" : "space-y-2"}>
    <div className={`flex flex-wrap ${compact ? "items-center" : "items-end"} gap-2`} role="group" aria-label={`${label} view controls`}>
      <label className={`${compact ? "flex items-center gap-2" : "grid gap-1"} min-w-40 flex-1 text-xs font-medium text-muted-foreground`}>
        <span className="whitespace-nowrap">Search <span className={compact ? "sr-only" : undefined}>{label.toLowerCase()}</span></span>
        <Input type="search" value={view.search} onChange={(event) => view.setSearch(event.target.value)} placeholder="Symbol or record identity" />
      </label>
      <label className={`${compact ? "flex items-center gap-2" : "grid gap-1"} text-xs font-medium text-muted-foreground`}>
        <span>{filterLabel}</span>
        <Select value={view.filter} onChange={(event) => view.setFilter(event.target.value)}>
          <option value="">All {filterLabel.toLowerCase()}</option>
          {view.options.map((value) => <option key={value} value={value}>{value}</option>)}
        </Select>
      </label>
      {(view.search || view.filter) ? <Button variant="outline" size="sm" onClick={view.clear}>Clear filters</Button> : null}
      <p className={`${compact ? "" : "pb-2"} text-xs text-muted-foreground`} role="status">{view.rows.length} of {total} loaded {label.toLowerCase()}</p>
    </div>
    {view.hiddenSelection ? <p role="status" className="text-xs text-warning">Selected record is outside these results. Its inspector remains available.</p> : null}
  </div>;
}
