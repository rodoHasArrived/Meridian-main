import { useCallback, useState } from "react";
import { runDataQuery } from "@/lib/api";
import type { DataQueryResult } from "@/types";

export const DEFAULT_DATA_QUERY_SQL =
  "SELECT symbol, event_type, date, format, size_bytes, event_count\nFROM meridian_files\nORDER BY symbol\nLIMIT 100";

export type DataQueryRunner = typeof runDataQuery;

export interface DataQueryPanelViewModel {
  sql: string;
  setSql: (sql: string) => void;
  busy: boolean;
  result: DataQueryResult | null;
  error: string | null;
  run: () => Promise<void>;
}

/**
 * View-model for the Data workspace SQL query panel. Guard violations and SQL errors arrive
 * as a successful response with `success: false`, so they render inline; only transport
 * failures surface through the catch path.
 */
export function useDataQueryPanel(runQuery: DataQueryRunner = runDataQuery): DataQueryPanelViewModel {
  const [sql, setSql] = useState(DEFAULT_DATA_QUERY_SQL);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<DataQueryResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const run = useCallback(async () => {
    if (busy) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const response = await runQuery({ sql });
      setResult(response.success ? response : null);
      setError(response.success ? null : (response.error ?? "Query failed."));
    } catch (err) {
      setResult(null);
      setError(err instanceof Error ? err.message : "Query failed.");
    } finally {
      setBusy(false);
    }
  }, [busy, runQuery, sql]);

  return { sql, setSql, busy, result, error, run };
}
