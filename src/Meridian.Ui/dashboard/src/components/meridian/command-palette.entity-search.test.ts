import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  buildCommandPaletteEntityItems,
  useCommandPaletteEntitySearch,
  type CommandPaletteEntitySearchServices
} from "@/components/meridian/command-palette.entity-search";
import { requirePresent } from "@/test/fixtures";
import type { SecurityMasterEntry, SymbolRecord } from "@/types";
import { ApiError } from "@/lib/api-errors";
import { parseCommandPaletteRecordQuery } from "./command-palette.entity-query";

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
  const report = {
    runId: "report-1", templateId: "Trial balance", executionState: "Succeeded", governanceState: "InReview",
    scope: { tenantId: "tenant", organizationId: "org", companyId: "company", fundId: null, bookId: "book", periodId: "period" }
  };
  const statement = {
    runId: "statement-run-1", importId: "different-import-identity", startedAtUtc: "2026-11-11T12:00:00Z", completedAtUtc: "2026-11-11T12:00:01Z",
    positionMatches: 1, cashMatches: 1, transactionMatches: 1, openExceptionCount: 2, account: "Account A"
  };
  const recordServices = (): CommandPaletteEntitySearchServices => ({
    searchSymbols: vi.fn().mockResolvedValue([]), searchSecurities: vi.fn().mockResolvedValue([]),
    getReportRun: vi.fn().mockResolvedValue(report), getStatementRun: vi.fn().mockResolvedValue(statement)
  });

  it("looks up exactly one governed report and disables fallback without broad search calls", async () => {
    vi.useFakeTimers();
    const services = recordServices();
    const { result } = renderHook(() => useCommandPaletteEntitySearch({ open: true, query: "report: report-1", services }));
    await act(() => vi.advanceTimersByTimeAsync(250));
    expect(services.getReportRun).toHaveBeenCalledWith("report-1", { signal: expect.any(AbortSignal), allowDevelopmentFallback: false });
    expect(services.searchSymbols).not.toHaveBeenCalled();
    expect(services.searchSecurities).not.toHaveBeenCalled();
    expect(services.getStatementRun).not.toHaveBeenCalled();
    expect(result.current.items).toEqual([expect.objectContaining({ id: "report:report-1", route: "/reporting/runs/detail?runId=report-1", statusLabel: "InReview" })]);
  });

  it("keeps statement run and import identities distinct in the selected-run route", async () => {
    vi.useFakeTimers();
    const services = recordServices();
    const { result } = renderHook(() => useCommandPaletteEntitySearch({ open: true, query: "statement: statement-run-1", services }));
    await act(() => vi.advanceTimersByTimeAsync(250));
    expect(result.current.items[0]).toMatchObject({
      route: "/accounting/reconciliation?reconciliationRun=statement-run-1#statement-runs",
      description: expect.stringContaining("different-import-identity")
    });
    expect(services.getStatementRun).toHaveBeenCalledWith("statement-run-1", { signal: expect.any(AbortSignal), allowDevelopmentFallback: false });
  });

  it.each([
    [401, "Sign in again"], [403, "Permission denied"], [404, "No accessible report run matches this exact ID"], [503, "lookup service unavailable"]
  ])("distinguishes HTTP %s lookup failures from an empty search", async (status, message) => {
    vi.useFakeTimers();
    const services = recordServices();
    vi.mocked(services.getReportRun!).mockRejectedValue(new ApiError({ status: Number(status), path: "/test" }));
    const { result } = renderHook(() => useCommandPaletteEntitySearch({ open: true, query: "report: report-1", services }));
    await act(() => vi.advanceTimersByTimeAsync(250));
    expect(result.current.status).toBe("error");
    expect(result.current.error).toContain(message);
    expect(result.current.items).toEqual([]);
  });

  it("clears prior records immediately and ignores a delayed result after query changes", async () => {
    vi.useFakeTimers();
    const services = recordServices();
    let complete!: (value: typeof report) => void;
    vi.mocked(services.getReportRun!).mockResolvedValueOnce(report).mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
    const { result, rerender } = renderHook(({ query, open }) => useCommandPaletteEntitySearch({ open, query, services }), { initialProps: { query: "report: report-1", open: true } });
    await act(() => vi.advanceTimersByTimeAsync(250));
    expect(result.current.items).toHaveLength(1);
    rerender({ query: "report: report-2", open: true });
    expect(result.current.items).toEqual([]);
    expect(result.current.status).toBe("searching");
    await act(() => vi.advanceTimersByTimeAsync(250));
    rerender({ query: "report: report-2", open: false });
    await act(async () => complete({ ...report, runId: "report-2" }));
    expect(result.current.items).toEqual([]);
    expect(result.current.status).toBe("idle");
  });

  it("rejects a different retained record instead of silently substituting it", async () => {
    vi.useFakeTimers();
    const services = recordServices();
    const { result } = renderHook(() => useCommandPaletteEntitySearch({ open: true, query: "report: not-report-1", services }));
    await act(() => vi.advanceTimersByTimeAsync(250));
    expect(result.current.status).toBe("error");
    expect(result.current.error).toContain("No alternative record");
    expect(result.current.items).toEqual([]);
  });

  it.each(["account: a", "journal: j", "import: imported-1", "report:"])("explains unsupported or incomplete %s without querying unrelated sources", async query => {
    vi.useFakeTimers();
    const services = recordServices();
    const { result } = renderHook(() => useCommandPaletteEntitySearch({ open: true, query, services }));
    await act(() => vi.advanceTimersByTimeAsync(250));
    expect(result.current.error).toBeTruthy();
    expect(result.current.items).toEqual([]);
    expect(services.searchSymbols).not.toHaveBeenCalled();
    expect(services.searchSecurities).not.toHaveBeenCalled();
    expect(services.getReportRun).not.toHaveBeenCalled();
    expect(services.getStatementRun).not.toHaveBeenCalled();
  });

  it("parses typed record prefixes without changing opaque IDs", () => {
    expect(parseCommandPaletteRecordQuery(" REPORT-RUN: AbC/1 ")).toEqual({ kind: "report", value: "AbC/1", guidance: null });
    expect(parseCommandPaletteRecordQuery("aa").kind).toBe("general");
    expect(parseCommandPaletteRecordQuery("import: a").guidance).toContain("exact statement reconciliation run ID");
  });

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

    // The only assignment to `firstSignal` happens inside the fetch mock, which control-flow
    // analysis cannot see, so here it is still narrowed to the `null` it was initialised with -
    // `firstSignal?.aborted` would read `.aborted` off `never`. The explicit type argument restores
    // the intended type, and asserting presence turns "the mock never captured a signal" into a
    // named failure instead of a silently-passing optional chain.
    expect(requirePresent<AbortSignal>(firstSignal, "captured abort signal").aborted).toBe(true);
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
