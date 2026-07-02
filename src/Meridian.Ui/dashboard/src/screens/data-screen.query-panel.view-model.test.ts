import { act, renderHook } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  DEFAULT_DATA_QUERY_SQL,
  useDataQueryPanel,
  type DataQueryRunner,
} from "@/screens/data-screen.query-panel.view-model";
import type { DataQueryResult } from "@/types";

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
