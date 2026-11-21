// Meridian useTableState — lightweight query/sort/filter state for data tables.
// Syncs across the screen: one hook manages filter, sort, search; passed to DataGrid.
// Persists to localStorage under a key you provide. Sort supports a multi-column stack:
// shift-toggleSort appends a secondary/tertiary key instead of replacing.
import React from "react";

export function useTableState(initialData = [], localStorageKey = null) {
  const [data, setData] = React.useState(initialData);
  const [query, setQuery] = React.useState("");
  const [sortBy, setSortBy] = React.useState(null); // { column, direction } — primary (compat)
  const [sortStack, setSortStack] = React.useState([]); // [{ column, direction }, ...] full multi-sort
  const [filters, setFilters] = React.useState({}); // { columnName: [values] }
  const [groupBy, setGroupBy] = React.useState(null); // string | string[] | null — feeds DataGrid groupBy
  const [restoredKey, setRestoredKey] = React.useState(null);

  // Persist state to localStorage if key provided
  React.useEffect(() => {
    if (!localStorageKey || restoredKey !== localStorageKey) return;
    const state = { query, sortStack, filters, groupBy };
    try { localStorage.setItem(localStorageKey, JSON.stringify(state)); }
    catch { /* A blocked/quota-limited store must not disable local table controls. */ }
  }, [query, sortStack, filters, groupBy, localStorageKey, restoredKey]);

  // Restore from localStorage on mount
  React.useEffect(() => {
    if (!localStorageKey) return;
    try {
      const saved = localStorage.getItem(localStorageKey);
      if (saved) {
        const parsed = JSON.parse(saved);
        setQuery(typeof parsed.query === "string" ? parsed.query : "");
        const rawStack = parsed.sortStack || (parsed.sortBy ? [parsed.sortBy] : []);
        const stack = Array.isArray(rawStack) ? rawStack.filter((entry) => entry && typeof entry.column === "string" && ["asc", "desc"].includes(entry.direction)) : [];
        setSortStack(stack);
        setSortBy(stack[0] || null);
        setFilters(Object.fromEntries(Object.entries(parsed.filters && typeof parsed.filters === "object" ? parsed.filters : {}).filter(([, values]) => Array.isArray(values))));
        setGroupBy(typeof parsed.groupBy === "string" || Array.isArray(parsed.groupBy) && parsed.groupBy.every((key) => typeof key === "string") ? parsed.groupBy : null);
      }
    } catch { /* Ignore unavailable or malformed retained view state. */ }
    setRestoredKey(localStorageKey);
  }, [localStorageKey]);

  // Filter data by active filters (multi-select per column)
  const filtered = React.useMemo(() => {
    let result = data;
    Object.entries(filters).forEach(([col, values]) => {
      if (!values || values.length === 0) return;
      result = result.filter((row) => values.includes(String(row[col])));
    });
    return result;
  }, [data, filters]);

  // Search across all text/number fields
  const searched = React.useMemo(() => {
    if (!query.trim()) return filtered;
    const q = query.toLowerCase();
    return filtered.filter((row) =>
      Object.values(row).some((val) =>
        String(val).toLowerCase().includes(q)
      )
    );
  }, [filtered, query]);

  // Sort by the full stack: primary key first, ties broken by secondary, etc.
  const sorted = React.useMemo(() => {
    if (!sortStack.length) return searched;
    const result = [...searched];
    result.sort((a, b) => {
      for (const { column, direction } of sortStack) {
        const aVal = a[column], bVal = b[column];
        if (aVal === bVal) continue;
        const cmp = aVal < bVal ? -1 : 1;
        return direction === "asc" ? cmp : -cmp;
      }
      return 0;
    });
    return result;
  }, [searched, sortStack]);

  // Single-column sort: replace the stack (existing behavior). Shift-click appends/advances a
  // secondary key: asc → desc → removed, so operators can build a Status-then-Time ordering.
  const toggleSort = (column, additive = false) => {
    setSortStack((prev) => {
      const idx = prev.findIndex((s) => s.column === column);
      let next;
      if (!additive) {
        if (idx === 0 && prev.length === 1) {
          next = [{ column, direction: prev[0].direction === "asc" ? "desc" : "asc" }];
        } else {
          next = [{ column, direction: "asc" }];
        }
      } else if (idx === -1) {
        next = [...prev, { column, direction: "asc" }];
      } else {
        const cur = prev[idx];
        next = [...prev];
        if (cur.direction === "asc") next[idx] = { column, direction: "desc" };
        else next.splice(idx, 1); // third click removes this key from the stack
      }
      setSortBy(next[0] || null);
      return next;
    });
  };

  // Where a column sits in the sort stack (for header badges): { rank, direction } | null.
  const sortInfo = (column) => {
    const idx = sortStack.findIndex((s) => s.column === column);
    return idx === -1 ? null : { rank: idx + 1, direction: sortStack[idx].direction, multi: sortStack.length > 1 };
  };

  const toggleFilter = (column, value) => {
    const current = filters[column] || [];
    const updated = current.includes(value)
      ? current.filter((v) => v !== value)
      : [...current, value];
    setFilters({
      ...filters,
      [column]: updated.length > 0 ? updated : undefined,
    });
  };

  const clearAllFilters = () => {
    setQuery("");
    setSortBy(null);
    setSortStack([]);
    setFilters({});
    setGroupBy(null);
  };

  // View snapshot in/out — what SavedViews captures and re-applies.
  const getViewState = () => ({ query, sortStack, filters, groupBy });
  const applyViewState = (state = {}) => {
    setQuery(state.query || "");
    const stack = state.sortStack || [];
    setSortStack(stack);
    setSortBy(stack[0] || null);
    setFilters(state.filters || {});
    setGroupBy(state.groupBy || null);
  };

  const exportCSV = (filename = "data.csv") => {
    if (sorted.length === 0) return;
    const headers = Object.keys(sorted[0]);
    const csvCell = (value) => {
      if (value == null) return "";
      const text = String(value);
      return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
    };
    const csv = [
      headers.map(csvCell).join(","),
      ...sorted.map((row) =>
        headers.map((h) => {
          const v = row[h];
          return csvCell(v);
        }).join(",")
      ),
    ].join("\n");
    const blob = new Blob([csv], { type: "text/csv" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
  };

  return {
    data: sorted,
    rawData: data,
    setData,
    query,
    setQuery,
    sortBy,
    sortStack,
    sortInfo,
    toggleSort,
    filters,
    toggleFilter,
    setFilters,
    groupBy,
    setGroupBy,
    getViewState,
    applyViewState,
    clearAllFilters,
    exportCSV,
    filterCount: Object.values(filters).filter((v) => v && v.length > 0).length,
    resultCount: sorted.length,
  };
}
