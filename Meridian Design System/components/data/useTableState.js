// Meridian useTableState — lightweight query/sort/filter state for data tables.
// Syncs across the screen: one hook manages filter, sort, search; passed to FilteredDataTable.
// Persists to localStorage under a key you provide. Sort supports a multi-column stack:
// shift-toggleSort appends a secondary/tertiary key instead of replacing.
import React from "react";

export function useTableState(initialData = [], localStorageKey = null) {
  const [data, setData] = React.useState(initialData);
  const [query, setQuery] = React.useState("");
  const [sortBy, setSortBy] = React.useState(null); // { column, direction } — primary (compat)
  const [sortStack, setSortStack] = React.useState([]); // [{ column, direction }, ...] full multi-sort
  const [filters, setFilters] = React.useState({}); // { columnName: [values] }

  // Persist state to localStorage if key provided
  React.useEffect(() => {
    if (!localStorageKey) return;
    const state = { query, sortStack, filters };
    localStorage.setItem(localStorageKey, JSON.stringify(state));
  }, [query, sortStack, filters, localStorageKey]);

  // Restore from localStorage on mount
  React.useEffect(() => {
    if (!localStorageKey) return;
    const saved = localStorage.getItem(localStorageKey);
    if (saved) {
      try {
        const parsed = JSON.parse(saved);
        setQuery(parsed.query || "");
        const stack = parsed.sortStack || (parsed.sortBy ? [parsed.sortBy] : []);
        setSortStack(stack);
        setSortBy(stack[0] || null);
        setFilters(parsed.filters || {});
      } catch (e) {
        // ignore parse errors
      }
    }
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
  };

  // View snapshot in/out — what SavedViews captures and re-applies.
  const getViewState = () => ({ query, sortStack, filters });
  const applyViewState = (state = {}) => {
    setQuery(state.query || "");
    const stack = state.sortStack || [];
    setSortStack(stack);
    setSortBy(stack[0] || null);
    setFilters(state.filters || {});
  };

  const exportCSV = (filename = "data.csv") => {
    if (sorted.length === 0) return;
    const headers = Object.keys(sorted[0]);
    const csv = [
      headers.join(","),
      ...sorted.map((row) =>
        headers.map((h) => {
          const v = row[h];
          if (v === null || v === undefined) return "";
          const str = String(v);
          return str.includes(",") ? `"${str}"` : str;
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
    getViewState,
    applyViewState,
    clearAllFilters,
    exportCSV,
    filterCount: Object.values(filters).filter((v) => v && v.length > 0).length,
    resultCount: sorted.length,
  };
}
