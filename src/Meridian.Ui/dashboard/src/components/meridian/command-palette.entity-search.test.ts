import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  buildCommandPaletteEntityItems,
  useCommandPaletteEntitySearch,
  type CommandPaletteEntitySearchServices
} from "@/components/meridian/command-palette.entity-search";
import type { SecurityMasterEntry, SymbolRecord } from "@/types";

const symbol: SymbolRecord = {
  symbol: "aapl",
  status: "Active",
  provider: "Polygon",
  lastEventAt: "2026-01-01T00:00:00Z",
  eventCount: 42,
  hasHistoricalData: true
};

const security: SecurityMasterEntry = {
  securityId: "sec-aapl",
  displayName: "Apple Inc.",
  status: "Active",
  classification: {
    assetClass: "Equity",
    subType: "Common",
    primaryIdentifierKind: "Ticker",
    primaryIdentifierValue: "AAPL"
  },
  economicDefinition: {
    currency: "USD",
    version: 1,
    effectiveFrom: null,
    effectiveTo: null,
    subType: "Common",
    assetFamily: "Equity",
    issuerType: "Corporate"
  }
};

afterEach(() => {
  vi.useRealTimers();
});

describe("command palette entity search", () => {
  it("normalizes symbol and security records into palette entity items", () => {
    expect(buildCommandPaletteEntityItems([symbol], [security])).toEqual([
      expect.objectContaining({
        id: "symbol:AAPL",
        label: "AAPL",
        route: "/data/quotes?symbol=AAPL",
        sourceLabel: "Symbol",
        commandLabel: "Open AAPL quotes"
      }),
      expect.objectContaining({
        id: "security:sec-aapl",
        label: "Apple Inc.",
        route: "/accounting/security-master/detail?securityId=sec-aapl",
        sourceLabel: "Security",
        commandLabel: "Open Apple Inc. security detail"
      })
    ]);
  });

  it("debounces searches and passes abort signals to both sources", async () => {
    vi.useFakeTimers();
    const services: CommandPaletteEntitySearchServices = {
      searchSymbols: vi.fn().mockResolvedValue([symbol]),
      searchSecurities: vi.fn().mockResolvedValue([security])
    };

    const { result } = renderHook(() =>
      useCommandPaletteEntitySearch({ open: true, query: "aa", services })
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(249);
    });
    expect(services.searchSymbols).not.toHaveBeenCalled();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1);
    });

    expect(result.current.status).toBe("ready");
    expect(services.searchSymbols).toHaveBeenCalledWith("aa", { signal: expect.any(AbortSignal) });
    expect(services.searchSecurities).toHaveBeenCalledWith("aa", 8, true, { signal: expect.any(AbortSignal) });
    expect(result.current.items).toHaveLength(2);
  });

  it("aborts stale requests when the query changes", async () => {
    vi.useFakeTimers();
    let firstSignal: AbortSignal | null = null;
    const services: CommandPaletteEntitySearchServices = {
      searchSymbols: vi.fn((_query, options) => {
        firstSignal ??= options?.signal ?? null;
        return Promise.resolve([symbol]);
      }),
      searchSecurities: vi.fn().mockResolvedValue([security])
    };

    const { rerender } = renderHook(
      ({ query }) => useCommandPaletteEntitySearch({ open: true, query, services }),
      { initialProps: { query: "aa" } }
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(250);
    });
    rerender({ query: "ms" });

    expect(firstSignal?.aborted).toBe(true);
  });

  it("keeps fulfilled results and reports degraded state when one source fails", async () => {
    vi.useFakeTimers();
    const services: CommandPaletteEntitySearchServices = {
      searchSymbols: vi.fn().mockResolvedValue([symbol]),
      searchSecurities: vi.fn().mockRejectedValue(new Error("security search down"))
    };

    const { result } = renderHook(() =>
      useCommandPaletteEntitySearch({ open: true, query: "aa", services })
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(250);
    });

    expect(result.current.status).toBe("degraded");
    expect(result.current.items.map((item) => item.id)).toEqual(["symbol:AAPL"]);
    expect(result.current.error).toBe("security search unavailable");
  });
});
