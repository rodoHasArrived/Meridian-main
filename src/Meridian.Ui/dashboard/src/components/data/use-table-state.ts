// Meridian useTableState (Concrete) — lightweight query / sort / filter state for data
// tables. One hook manages the search query, per-column multi-select filters, and sort;
// its derived `data` feeds FilteredDataTable (and any DenseDataTable). Optionally persists
// query/sort/filters to localStorage under a caller-supplied key.
import { useEffect, useMemo, useState } from "react";

export type SortDirection = "asc" | "desc";

export interface TableSort {
  column: string;
  direction: SortDirection;
}

export type TableFilters = Record<string, string[] | undefined>;

export interface TableState<Row = Record<string, unknown>> {
  /** Rows after filter → search → sort. */
  data: Row[];
  /** The unfiltered source rows. */
  rawData: Row[];
  setData: (rows: Row[]) => void;
  query: string;
  setQuery: (q: string) => void;
  sortBy: TableSort | null;
  toggleSort: (column: string) => void;
  filters: TableFilters;
  toggleFilter: (column: string, value: string) => void;
  clearAllFilters: () => void;
  exportCSV: (filename?: string) => void;
  /** Number of columns with at least one active filter value. */
  filterCount: number;
  /** Number of rows after all narrowing. */
  resultCount: number;
}

function readValue(row: Record<string, unknown>, column: string): unknown {
  return row[column];
}

/**
 * Manage query / sort / filter state for a data table.
 *
 * @param initialData source rows.
 * @param localStorageKey when provided, query/sort/filters persist under this key.
 */
export function useTableState<Row extends Record<string, unknown> = Record<string, unknown>>(
  initialData: Row[] = [],
  localStorageKey: string | null = null,
): TableState<Row> {
  const [data, setData] = useState<Row[]>(initialData);
  const [query, setQuery] = useState("");
  const [sortBy, setSortBy] = useState<TableSort | null>(null);
  const [filters, setFilters] = useState<TableFilters>({});

  // Restore persisted state on mount / key change.
  useEffect(() => {
    if (!localStorageKey || typeof localStorage === "undefined") return;
    const saved = localStorage.getItem(localStorageKey);
    if (!saved) return;
    try {
      const parsed = JSON.parse(saved) as {
        query?: string;
        sortBy?: TableSort | null;
        filters?: TableFilters;
      };
      setQuery(parsed.query ?? "");
      setSortBy(parsed.sortBy ?? null);
      setFilters(parsed.filters ?? {});
    } catch {
      // Ignore malformed persisted state.
    }
  }, [localStorageKey]);

  // Persist state when it changes.
  useEffect(() => {
    if (!localStorageKey || typeof localStorage === "undefined") return;
    localStorage.setItem(localStorageKey, JSON.stringify({ query, sortBy, filters }));
  }, [query, sortBy, filters, localStorageKey]);

  const filtered = useMemo(() => {
    let result = data;
    for (const [column, values] of Object.entries(filters)) {
      if (!values || values.length === 0) continue;
      result = result.filter((row) => values.includes(String(readValue(row, column))));
    }
    return result;
  }, [data, filters]);

  const searched = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return filtered;
    return filtered.filter((row) =>
      Object.values(row).some((value) => String(value).toLowerCase().includes(q)),
    );
  }, [filtered, query]);

  const sorted = useMemo(() => {
    if (!sortBy) return searched;
    const { column, direction } = sortBy;
    const result = [...searched];
    result.sort((a, b) => {
      const aVal = readValue(a, column);
      const bVal = readValue(b, column);
      if (aVal === bVal) return 0;
      const cmp = (aVal as never) < (bVal as never) ? -1 : 1;
      return direction === "asc" ? cmp : -cmp;
    });
    return result;
  }, [searched, sortBy]);

  const toggleSort = (column: string) => {
    setSortBy((current) =>
      current?.column === column
        ? { column, direction: current.direction === "asc" ? "desc" : "asc" }
        : { column, direction: "asc" },
    );
  };

  const toggleFilter = (column: string, value: string) => {
    setFilters((current) => {
      const existing = current[column] ?? [];
      const updated = existing.includes(value)
        ? existing.filter((v) => v !== value)
        : [...existing, value];
      return { ...current, [column]: updated.length > 0 ? updated : undefined };
    });
  };

  const clearAllFilters = () => {
    setQuery("");
    setSortBy(null);
    setFilters({});
  };

  const exportCSV = (filename = "data.csv") => {
    if (sorted.length === 0 || typeof document === "undefined") return;
    const headers = Object.keys(sorted[0]);
    const csv = [
      headers.join(","),
      ...sorted.map((row) =>
        headers
          .map((h) => {
            const value = readValue(row, h);
            if (value === null || value === undefined) return "";
            const str = String(value);
            return str.includes(",") ? `"${str.replace(/"/g, '""')}"` : str;
          })
          .join(","),
      ),
    ].join("\n");
    const blob = new Blob([csv], { type: "text/csv" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = filename;
    anchor.click();
    URL.revokeObjectURL(url);
  };

  return {
    data: sorted,
    rawData: data,
    setData,
    query,
    setQuery,
    sortBy,
    toggleSort,
    filters,
    toggleFilter,
    clearAllFilters,
    exportCSV,
    filterCount: Object.values(filters).filter((v) => v && v.length > 0).length,
    resultCount: sorted.length,
  };
}
