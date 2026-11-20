import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  buildCommandPaletteEntityItems,
  useCommandPaletteEntitySearch,
  type CommandPaletteEntitySearchServices,
  type WorkstationRecordSearchHit,
  type WorkstationRecordSearchResult
} from "@/components/meridian/command-palette.entity-search";
import { ApiError } from "@/lib/api-errors";
import type { SymbolRecord } from "@/types";

const symbol: SymbolRecord = {
  symbol: "aapl",
  status: "Active",
  provider: "Polygon",
  lastEventAt: "2026-01-01T00:00:00Z",
  eventCount: 42,
  hasHistoricalData: true
};

const instrument: WorkstationRecordSearchHit = {
  recordId: "sec-aapl",
  recordType: "Instrument",
  label: "Apple Inc.",
  status: "Active",
  scope: "Security Master catalog",
  asOfPosture: "Current reference",
  href: "/accounting/security-master/detail?securityId=sec-aapl"
};

const response = { items: [instrument], isPartial: false };

async function advance(ms: number) {
  await act(() => vi.advanceTimersByTimeAsync(ms));
}

afterEach(() => {
  vi.useRealTimers();
});

describe("command palette entity search", () => {
  it("keeps authorized records in server order, followed by matching symbol prefixes", () => {
    expect(buildCommandPaletteEntityItems([instrument], [{ ...symbol, symbol: "BAAP" }, symbol], "aa")).toEqual([
      expect.objectContaining({
        recordId: "sec-aapl",
        label: "Apple Inc.",
        route: instrument.href,
        sourceLabel: "Instrument",
        description: "Security Master catalog · Current reference",
        statusLabel: "Active"
      }),
      expect.objectContaining({
        id: "symbol:AAPL",
        label: "AAPL",
        route: "/data/quotes?symbol=AAPL",
        sourceLabel: "Symbol",
        commandLabel: "Open AAPL quotes"
      })
    ]);
  });

  it("debounces both sources and disables development fallbacks", async () => {
    vi.useFakeTimers();
    const services: CommandPaletteEntitySearchServices = {
      searchSymbols: vi.fn().mockResolvedValue([symbol]),
      searchRecords: vi.fn().mockResolvedValue(response)
    };

    const { result } = renderHook(() =>
      useCommandPaletteEntitySearch({ open: true, query: " aa ", services })
    );

    expect(result.current.status).toBe("searching");
    await advance(249);
    expect(services.searchRecords).not.toHaveBeenCalled();
    expect(services.searchSymbols).not.toHaveBeenCalled();
    await advance(1);

    const options = { signal: expect.any(AbortSignal), allowDevelopmentFallback: false };
    expect(services.searchRecords).toHaveBeenCalledWith("aa", options);
    expect(services.searchSymbols).toHaveBeenCalledWith("aa", options);
    expect(result.current.status).toBe("ready");
    expect(result.current.items).toHaveLength(2);
  });

  it("drops pending and visible results when the query, scope or palette changes", async () => {
    vi.useFakeTimers();
    let resolveFirst: (answer: WorkstationRecordSearchResult) => void = () => {};
    const searchRecords = vi.fn<CommandPaletteEntitySearchServices["searchRecords"]>()
      .mockReturnValueOnce(new Promise((resolve) => { resolveFirst = resolve; }))
      .mockResolvedValueOnce(response);
    const services = { searchRecords, searchSymbols: vi.fn().mockResolvedValue([]) };
    const { result, rerender } = renderHook(
      (props) => useCommandPaletteEntitySearch({ ...props, services }),
      { initialProps: { open: true, query: "aa", scopeKey: "account-a" } }
    );

    await advance(250);
    rerender({ open: true, query: "ms", scopeKey: "account-a" });
    expect(searchRecords.mock.calls[0][1]?.signal?.aborted).toBe(true);
    await act(async () => resolveFirst(response));
    expect(result.current.items).toEqual([]);

    await advance(250);
    expect(result.current.items).toHaveLength(1);
    rerender({ open: true, query: "ms", scopeKey: "account-b" });
    expect(result.current.items).toEqual([]);
    rerender({ open: false, query: "ms", scopeKey: "account-b" });
    expect(result.current).toMatchObject({ items: [], status: "idle" });
    await advance(250);
    expect(searchRecords).toHaveBeenCalledTimes(2);
  });

  it("keeps authorized hits when a provider degrades and hides refusal details", async () => {
    vi.useFakeTimers();
    const services: CommandPaletteEntitySearchServices = {
      searchSymbols: vi.fn().mockRejectedValue(new ApiError({ path: "/search", status: 403, detail: "Private scope" })),
      searchRecords: vi.fn().mockResolvedValue({ ...response, isPartial: true })
    };
    const { result } = renderHook(() =>
      useCommandPaletteEntitySearch({ open: true, query: "aa", services })
    );
    await advance(250);

    expect(result.current.status).toBe("degraded");
    expect(result.current.items.map((item) => item.recordId)).toEqual(["sec-aapl"]);
    expect(result.current.error).toBe("Some record sources are temporarily unavailable");
  });

  it("treats an access refusal as an empty source", async () => {
    vi.useFakeTimers();
    const services: CommandPaletteEntitySearchServices = {
      searchSymbols: vi.fn().mockResolvedValue([]),
      searchRecords: vi.fn().mockRejectedValue(new ApiError({ path: "/search", status: 403, detail: "Private scope" }))
    };
    const { result } = renderHook(() =>
      useCommandPaletteEntitySearch({ open: true, query: "aa", services })
    );
    await advance(250);
    expect(result.current).toEqual({ items: [], status: "ready", error: null });
  });
});
