// Meridian useTableState — lightweight query/sort/filter state for data tables.
// Syncs across the screen: one hook manages filter, sort, search; passed to FilteredDataTable.
// Persists to localStorage under a key you provide.

export function useTableState(initialData = [], localStorageKey = null) {
  const [data, setData] = React.useState(initialData);
  const [query, setQuery] = React.useState("");
  const [sortBy, setSortBy] = React.useState(null); // { column, direction: 'asc'|'desc' }
  const [filters, setFilters] = React.useState({}); // { columnName: [values] }

  // Persist state to localStorage if key provided
  React.useEffect(() => {
    if (!localStorageKey) return;
    const state = { query, sortBy, filters };
    localStorage.setItem(localStorageKey, JSON.stringify(state));
  }, [query, sortBy, filters, localStorageKey]);

  // Restore from localStorage on mount
  React.useEffect(() => {
    if (!localStorageKey) return;
    const saved = localStorage.getItem(localStorageKey);
    if (saved) {
      try {
        const { query: q, sortBy: s, filters: f } = JSON.parse(saved);
        setQuery(q || "");
        setSortBy(s || null);
        setFilters(f || {});
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

  // Sort by column
  const sorted = React.useMemo(() => {
    if (!sortBy) return searched;
    const { column, direction } = sortBy;
    const result = [...searched];
    result.sort((a, b) => {
      const aVal = a[column];
      const bVal = b[column];
      if (aVal === bVal) return 0;
      const cmp = aVal < bVal ? -1 : 1;
      return direction === "asc" ? cmp : -cmp;
    });
    return result;
  }, [searched, sortBy]);

  const toggleSort = (column) => {
    if (sortBy?.column === column) {
      setSortBy({
        column,
        direction: sortBy.direction === "asc" ? "desc" : "asc",
      });
    } else {
      setSortBy({ column, direction: "asc" });
    }
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
    setFilters({});
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
    toggleSort,
    filters,
    toggleFilter,
    clearAllFilters,
    exportCSV,
    filterCount: Object.values(filters).filter((v) => v && v.length > 0).length,
    resultCount: sorted.length,
  };
}
