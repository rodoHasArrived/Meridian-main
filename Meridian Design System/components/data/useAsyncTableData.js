// Meridian useAsyncTableData — the async/paged data contract for large server-backed tables.
// Owns loading/error/paging state so tables don't each reinvent it. You supply one fetcher:
//
//   async fetchPage({ offset, limit, sort, query, signal }) → { rows, total }
//
// The hook debounces query changes, cancels superseded requests, and appends pages for
// infinite scroll (mode:"append") or replaces them for classic pagination (mode:"replace").
// Returns { rows, total, status, error, loadMore, hasMore, reload, setQuery, setSort }.
import React from "react";

export function useAsyncTableData(fetchPage, {
  limit = 50, mode = "append", debounceMs = 250, initialSort = null, initialQuery = "",
} = {}) {
  const [rows, setRows] = React.useState([]);
  const [total, setTotal] = React.useState(null);
  const [status, setStatus] = React.useState("idle"); // idle | loading | loadingMore | ready | error | empty
  const [error, setError] = React.useState(null);
  const [query, setQueryRaw] = React.useState(initialQuery);
  const [sort, setSort] = React.useState(initialSort);
  const offsetRef = React.useRef(0);
  const abortRef = React.useRef(null);
  const reqIdRef = React.useRef(0);

  const run = React.useCallback(async (append) => {
    const reqId = ++reqIdRef.current;
    if (abortRef.current) abortRef.current.abort();
    const controller = typeof AbortController !== "undefined" ? new AbortController() : { abort() {}, signal: undefined };
    abortRef.current = controller;
    const offset = append ? offsetRef.current : 0;
    setStatus(append ? "loadingMore" : "loading");
    setError(null);
    try {
      const res = await fetchPage({ offset, limit, sort, query, signal: controller.signal });
      if (reqId !== reqIdRef.current) return; // superseded
      const pageRows = res.rows || [];
      const nextRows = append ? [...rows, ...pageRows] : pageRows;
      offsetRef.current = offset + pageRows.length;
      setRows(nextRows);
      setTotal(res.total ?? null);
      setStatus(nextRows.length === 0 ? "empty" : "ready");
    } catch (e) {
      if (e && e.name === "AbortError") return;
      if (reqId !== reqIdRef.current) return;
      setError(e);
      setStatus("error");
    }
  }, [fetchPage, limit, sort, query, rows]);

  // Reload from offset 0 whenever query/sort change (query debounced).
  React.useEffect(() => {
    const t = setTimeout(() => { offsetRef.current = 0; run(false); }, debounceMs);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query, sort]);

  const hasMore = mode === "append" && total != null && rows.length < total;
  const loadMore = React.useCallback(() => {
    if (status === "loading" || status === "loadingMore" || !hasMore) return;
    run(true);
  }, [status, hasMore, run]);

  const reload = React.useCallback(() => { offsetRef.current = 0; run(false); }, [run]);
  const setQuery = React.useCallback((q) => setQueryRaw(q), []);

  return { rows, total, status, error, query, setQuery, sort, setSort, loadMore, hasMore, reload };
}
