import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Blob as NodeBlob } from "node:buffer";
import { useTableState } from "./use-table-state";

interface Row extends Record<string, unknown> {
  symbol: string;
  sector: string;
  qty: number;
}

const data: Row[] = [
  { symbol: "AAPL", sector: "Tech", qty: 100 },
  { symbol: "XOM", sector: "Energy", qty: 40 },
  { symbol: "MSFT", sector: "Tech", qty: 75 },
];

describe("useTableState", () => {
  const originalCreateObjectURL = URL.createObjectURL;
  const originalRevokeObjectURL = URL.revokeObjectURL;

  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    Object.defineProperty(URL, "createObjectURL", {
      configurable: true,
      value: originalCreateObjectURL,
    });
    Object.defineProperty(URL, "revokeObjectURL", {
      configurable: true,
      value: originalRevokeObjectURL,
    });
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it("searches across all fields", () => {
    const { result } = renderHook(() => useTableState<Row>(data));
    act(() => result.current.setQuery("energy"));
    expect(result.current.data.map((r) => r.symbol)).toEqual(["XOM"]);
    expect(result.current.resultCount).toBe(1);
  });

  it("toggles sort direction on repeated column toggles", () => {
    const { result } = renderHook(() => useTableState<Row>(data));
    act(() => result.current.toggleSort("qty"));
    expect(result.current.data.map((r) => r.qty)).toEqual([40, 75, 100]);
    act(() => result.current.toggleSort("qty"));
    expect(result.current.data.map((r) => r.qty)).toEqual([100, 75, 40]);
  });

  it("applies multi-select column filters and counts them", () => {
    const { result } = renderHook(() => useTableState<Row>(data));
    act(() => result.current.toggleFilter("sector", "Tech"));
    expect(result.current.data.map((r) => r.symbol)).toEqual(["AAPL", "MSFT"]);
    expect(result.current.filterCount).toBe(1);
    act(() => result.current.toggleFilter("sector", "Tech"));
    expect(result.current.resultCount).toBe(3);
    expect(result.current.filterCount).toBe(0);
  });

  it("clears query, sort, and filters together", () => {
    const { result } = renderHook(() => useTableState<Row>(data));
    act(() => {
      result.current.setQuery("tech");
      result.current.toggleSort("qty");
      result.current.toggleFilter("sector", "Tech");
    });
    act(() => result.current.clearAllFilters());
    expect(result.current.query).toBe("");
    expect(result.current.sortBy).toBeNull();
    expect(result.current.filterCount).toBe(0);
    expect(result.current.resultCount).toBe(3);
  });

  it("syncs rows when initialData changes", () => {
    const { result, rerender } = renderHook(({ rows }) => useTableState<Row>(rows), {
      initialProps: { rows: [] as Row[] },
    });
    expect(result.current.rawData).toHaveLength(0);
    rerender({ rows: data });
    expect(result.current.rawData).toHaveLength(3);
    expect(result.current.data.map((r) => r.symbol)).toEqual(["AAPL", "XOM", "MSFT"]);
  });

  it("restores persisted state during initialization", () => {
    localStorage.setItem("positions", JSON.stringify({ query: "energy", sortBy: null, filters: {} }));
    const { result } = renderHook(() => useTableState<Row>(data, "positions"));
    expect(result.current.query).toBe("energy");
    expect(result.current.data.map((r) => r.symbol)).toEqual(["XOM"]);
  });

  it("sorts nullish values consistently after concrete values", () => {
    const rows = [
      { symbol: "MISSING", sector: "Tech", qty: undefined as unknown as number },
      { symbol: "XOM", sector: "Energy", qty: 40 },
      { symbol: "AAPL", sector: "Tech", qty: 100 },
    ];
    const { result } = renderHook(() => useTableState<Row>(rows));
    act(() => result.current.toggleSort("qty"));
    expect(result.current.data.map((r) => r.symbol)).toEqual(["XOM", "AAPL", "MISSING"]);
  });

  it("escapes quotes and newlines in CSV exports", async () => {
    const createObjectURL = vi.fn((_blob: Blob) => "blob:csv");
    const revokeObjectURL = vi.fn();
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: createObjectURL });
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: revokeObjectURL });
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
    // jsdom's Blob lacks .text(); use Node's Blob for this export so we can read it back.
    const OriginalBlob = globalThis.Blob;
    globalThis.Blob = NodeBlob as unknown as typeof Blob;
    const csvRows = [{ symbol: "AAPL", sector: "Tech\nGrowth", qty: '10"0' }];
    const { result } = renderHook(() => useTableState(csvRows));

    try {
      act(() => result.current.exportCSV("positions.csv"));
      const blob = createObjectURL.mock.calls[0][0] as Blob;
      await expect(blob.text()).resolves.toContain('"Tech\nGrowth","10""0"');
    } finally {
      globalThis.Blob = OriginalBlob;
    }
    expect(click).toHaveBeenCalledOnce();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(revokeObjectURL).toHaveBeenCalledWith("blob:csv");
  });
});
