import { act, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
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
});
