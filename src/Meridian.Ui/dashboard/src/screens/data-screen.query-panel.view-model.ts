import { useCallback, useEffect, useMemo, useState } from "react";
import { runDataQuery } from "@/lib/api";
import { copyTextToClipboard, downloadCsv, serializeCsv } from "@/lib/csv";
import {
  deleteSavedSqlQuery,
  loadSqlWorkbenchState,
  recordSqlQueryHistory,
  saveSqlWorkbenchState,
  upsertSavedSqlQuery,
  type SqlWorkbenchHistoryEntry,
  type SqlWorkbenchSavedQuery,
  type SqlWorkbenchStorageState,
  type StorageLike
} from "@/lib/sql-workbench-storage";
import type { DataQueryResult } from "@/types";

export const DEFAULT_DATA_QUERY_SQL =
  "SELECT symbol, event_type, date, format, size_bytes, event_count\nFROM meridian_files\nORDER BY symbol\nLIMIT 100";

export type DataQueryRunner = typeof runDataQuery;
export const DATA_QUERY_ROWS_PER_PAGE = 100;

export interface DataQueryPanelViewModel {
  sql: string;
  setSql: (sql: string) => void;
  busy: boolean;
  result: DataQueryResult | null;
  error: string | null;
  run: () => Promise<void>;
  history: SqlWorkbenchHistoryEntry[];
  savedQueries: SqlWorkbenchSavedQuery[];
  saveCurrentQuery: (name: string) => void;
  loadQuery: (sql: string) => void;
  deleteSavedQuery: (queryId: string) => void;
  currentPage: number;
  totalPages: number;
  rowsPerPage: number;
  setPage: (page: number) => void;
  visibleRows: DataQueryResult["rows"];
  resultRowCount: number;
  resultStatus: string | null;
  truncationMessage: string | null;
  canCopyOrExport: boolean;
  copyStatus: string | null;
  exportCsv: () => boolean;
  copyResults: () => Promise<boolean>;
}

/**
 * View-model for the Data workspace SQL query panel. Guard violations and SQL errors arrive
 * as a successful response with `success: false`, so they render inline; only transport
 * failures surface through the catch path.
 */
export function useDataQueryPanel(
  runQuery: DataQueryRunner = runDataQuery,
  storage?: StorageLike | null,
): DataQueryPanelViewModel {
  const [sql, setSql] = useState(DEFAULT_DATA_QUERY_SQL);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<DataQueryResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [currentPage, setCurrentPage] = useState(1);
  const [copyStatus, setCopyStatus] = useState<string | null>(null);
  const [workbenchState, setWorkbenchState] = useState<SqlWorkbenchStorageState>(() =>
    loadSqlWorkbenchState(storage),
  );

  const persistWorkbenchState = useCallback(
    (mutate: (state: SqlWorkbenchStorageState) => SqlWorkbenchStorageState) => {
      setWorkbenchState((current) => {
        const updated = mutate(current);
        saveSqlWorkbenchState(updated, storage);
        return updated;
      });
    },
    [storage],
  );

  const run = useCallback(async () => {
    if (busy) {
      return;
    }
    const activeSql = sql.trim();
    setBusy(true);
    setError(null);
    setCopyStatus(null);
    try {
      const response = await runQuery({ sql: activeSql });
      setResult(response.success ? response : null);
      setError(response.success ? null : (response.error ?? "Query failed."));
      if (activeSql) {
        persistWorkbenchState((state) => recordSqlQueryHistory(state, activeSql));
      }
    } catch (err) {
      setResult(null);
      setError(err instanceof Error ? err.message : "Query failed.");
    } finally {
      setBusy(false);
    }
  }, [busy, persistWorkbenchState, runQuery, sql]);

  useEffect(() => {
    setCurrentPage(1);
  }, [result]);

  const resultRowCount = result?.rows.length ?? 0;
  const totalPages = Math.max(1, Math.ceil(resultRowCount / DATA_QUERY_ROWS_PER_PAGE));
  const clampedPage = Math.min(currentPage, totalPages);
  const visibleRows = useMemo(() => {
    if (!result) {
      return [];
    }

    const start = (clampedPage - 1) * DATA_QUERY_ROWS_PER_PAGE;
    return result.rows.slice(start, start + DATA_QUERY_ROWS_PER_PAGE);
  }, [clampedPage, result]);

  const saveCurrentQuery = useCallback(
    (name: string) => {
      persistWorkbenchState((state) => upsertSavedSqlQuery(state, name, sql));
    },
    [persistWorkbenchState, sql],
  );

  const loadQuery = useCallback((nextSql: string) => {
    setSql(nextSql);
    setCopyStatus(null);
  }, []);

  const deleteSavedQuery = useCallback(
    (queryId: string) => {
      persistWorkbenchState((state) => deleteSavedSqlQuery(state, queryId));
    },
    [persistWorkbenchState],
  );

  const csv = useMemo(() => {
    if (!result || result.columns.length === 0) {
      return "";
    }

    return serializeCsv(result.columns, result.rows);
  }, [result]);

  const canCopyOrExport = csv.length > 0;

  const exportCsv = useCallback(() => {
    if (!csv) {
      return false;
    }

    return downloadCsv("meridian-query-results.csv", csv);
  }, [csv]);

  const copyResults = useCallback(async () => {
    if (!csv) {
      setCopyStatus("No query results to copy.");
      return false;
    }

    const copied = await copyTextToClipboard(csv);
    setCopyStatus(copied ? "Query results copied." : "Clipboard is unavailable.");
    return copied;
  }, [csv]);

  const setClampedPage = useCallback(
    (page: number) => {
      setCurrentPage(Math.max(1, Math.min(totalPages, page)));
    },
    [totalPages],
  );

  const resultStatus = result
    ? `${result.rowCount} row${result.rowCount === 1 ? "" : "s"} · ${result.elapsedMs} ms`
    : null;
  const truncationMessage = result?.truncated
    ? `Server result was truncated after ${result.rows.length} rendered row${result.rows.length === 1 ? "" : "s"}. Refine the query or add a smaller LIMIT for full review.`
    : result && result.rows.length > DATA_QUERY_ROWS_PER_PAGE
      ? `Showing ${visibleRows.length} of ${result.rows.length} returned rows on this page.`
      : null;

  return {
    sql,
    setSql,
    busy,
    result,
    error,
    run,
    history: workbenchState.history,
    savedQueries: workbenchState.savedQueries,
    saveCurrentQuery,
    loadQuery,
    deleteSavedQuery,
    currentPage: clampedPage,
    totalPages,
    rowsPerPage: DATA_QUERY_ROWS_PER_PAGE,
    visibleRows,
    resultRowCount,
    resultStatus,
    truncationMessage,
    canCopyOrExport,
    copyStatus,
    exportCsv,
    copyResults,
    setPage: setClampedPage
  };
}
