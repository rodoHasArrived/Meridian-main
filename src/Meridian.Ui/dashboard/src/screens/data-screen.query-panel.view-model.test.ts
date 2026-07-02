import { act, renderHook } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  DATA_QUERY_ROWS_PER_PAGE,
  DEFAULT_DATA_QUERY_SQL,
  useDataQueryPanel,
  type DataQueryRunner,
} from "@/screens/data-screen.query-panel.view-model";
import {
  SQL_WORKBENCH_STORAGE_KEY,
  type StorageLike
} from "@/lib/sql-workbench-storage";
import type { DataQueryResult } from "@/types";

class MemoryStorage implements StorageLike {
  private readonly values = new Map<string, string>();

  getItem(key: string) {
    return this.values.get(key) ?? null;
  }

  setItem(key: string, value: string) {
    this.values.set(key, value);
  }

  removeItem(key: string) {
    this.values.delete(key);
  }
}

function successResult(overrides: Partial<DataQueryResult> = {}): DataQueryResult {
  return {
    success: true,
    error: null,
    columns: ["symbol", "event_count"],
    columnTypes: ["VARCHAR", "BIGINT"],
    rows: [["SPY", "5000"]],
    rowCount: 1,
    truncated: false,
    elapsedMs: 12,
    ...overrides,
  };
}

describe("data query panel view model", () => {
  it("starts with the discovery query and no result", () => {
    const { result } = renderHook(() => useDataQueryPanel(vi.fn() as DataQueryRunner));

    expect(result.current.sql).toBe(DEFAULT_DATA_QUERY_SQL);
    expect(result.current.busy).toBe(false);
    expect(result.current.result).toBeNull();
    expect(result.current.error).toBeNull();
  });

  it("runs the current sql and stores the result", async () => {
    const runQuery = vi.fn().mockResolvedValue(successResult());
    const { result } = renderHook(() => useDataQueryPanel(runQuery));

    act(() => {
      result.current.setSql("SELECT 1");
    });
    await act(async () => {
      await result.current.run();
    });

    expect(runQuery).toHaveBeenCalledWith({ sql: "SELECT 1" });
    expect(result.current.result?.rowCount).toBe(1);
    expect(result.current.error).toBeNull();
    expect(result.current.busy).toBe(false);
  });

  it("records successful query attempts in persistent history", async () => {
    const storage = new MemoryStorage();
    const runQuery = vi.fn().mockResolvedValue(successResult());
    const { result } = renderHook(() => useDataQueryPanel(runQuery, storage));

    act(() => {
      result.current.setSql(" SELECT symbol FROM meridian_files ");
    });
    await act(async () => {
      await result.current.run();
    });

    expect(result.current.history[0]?.sql).toBe("SELECT symbol FROM meridian_files");
    expect(storage.getItem(SQL_WORKBENCH_STORAGE_KEY)).toContain("SELECT symbol FROM meridian_files");
  });

  it("saves, loads, and deletes named queries", () => {
    const { result } = renderHook(() => useDataQueryPanel(vi.fn() as DataQueryRunner, new MemoryStorage()));

    act(() => {
      result.current.setSql("SELECT * FROM meridian_files");
    });
    act(() => {
      result.current.saveCurrentQuery("Catalog");
    });

    expect(result.current.savedQueries[0]).toMatchObject({
      id: "catalog",
      name: "Catalog",
      sql: "SELECT * FROM meridian_files",
    });

    act(() => {
      result.current.loadQuery("SELECT 1");
    });
    expect(result.current.sql).toBe("SELECT 1");

    act(() => {
      result.current.deleteSavedQuery("catalog");
    });
    expect(result.current.savedQueries).toHaveLength(0);
  });

  it("paginates rendered rows while preserving copy/export source rows", async () => {
    const rows = Array.from({ length: DATA_QUERY_ROWS_PER_PAGE + 2 }, (_, index) => [
      `SYM${index}`,
      String(index),
    ]);
    const runQuery = vi.fn().mockResolvedValue(successResult({ rows, rowCount: rows.length }));
    const { result } = renderHook(() => useDataQueryPanel(runQuery, new MemoryStorage()));

    await act(async () => {
      await result.current.run();
    });

    expect(result.current.visibleRows).toHaveLength(DATA_QUERY_ROWS_PER_PAGE);
    expect(result.current.totalPages).toBe(2);
    expect(result.current.truncationMessage).toContain("Showing 100 of 102");

    act(() => {
      result.current.setPage(2);
    });

    expect(result.current.visibleRows).toHaveLength(2);
  });

  it("copies query results as CSV when clipboard is available", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal("navigator", { clipboard: { writeText } });
    const runQuery = vi.fn().mockResolvedValue(successResult({ rows: [["AAPL", "quoted \"value\""]] }));
    const { result } = renderHook(() => useDataQueryPanel(runQuery, new MemoryStorage()));

    await act(async () => {
      await result.current.run();
    });
    await act(async () => {
      await result.current.copyResults();
    });

    expect(writeText).toHaveBeenCalledWith('symbol,event_count\nAAPL,"quoted ""value"""');
    expect(result.current.copyStatus).toBe("Query results copied.");

    vi.unstubAllGlobals();
  });

  it("surfaces guard rejections from success=false payloads", async () => {
    const runQuery = vi.fn().mockResolvedValue(
      successResult({ success: false, error: "Only read-only queries are allowed.", rows: [], rowCount: 0 }),
    );
    const { result } = renderHook(() => useDataQueryPanel(runQuery));

    await act(async () => {
      await result.current.run();
    });

    expect(result.current.result).toBeNull();
    expect(result.current.error).toContain("read-only");
  });

  it("surfaces transport failures as errors", async () => {
    const runQuery = vi.fn().mockRejectedValue(new Error("network down"));
    const { result } = renderHook(() => useDataQueryPanel(runQuery));

    await act(async () => {
      await result.current.run();
    });

    expect(result.current.result).toBeNull();
    expect(result.current.error).toBe("network down");
  });
});
