import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  COVERED_CALL_CHAIN_DETAIL_PANEL_ID,
  buildChainPreviewPanelViewModel,
  DEFAULT_COVERED_CALL_FORM,
  formToRequest,
  isTerminalPhase,
  useCoveredCallScreenViewModel,
  validateForm,
  type CoveredCallScreenServices
} from "@/screens/covered-call-screen.view-model";
import type {
  CoveredCallChainPreview,
  CoveredCallRunHandle,
  CoveredCallRunResult,
  CoveredCallRunStatus
} from "@/types/covered-call";

describe("validateForm", () => {
  it("returns empty when form is valid", () => {
    const errors = validateForm({ ...DEFAULT_COVERED_CALL_FORM, minStrike: "500" });
    expect(errors).toEqual({});
  });

  it("requires non-zero min strike", () => {
    const errors = validateForm({ ...DEFAULT_COVERED_CALL_FORM, minStrike: "0" });
    expect(errors.minStrike).toBeDefined();
  });

  it("requires overwriteRatio in (0,1]", () => {
    expect(validateForm({ ...DEFAULT_COVERED_CALL_FORM, minStrike: "500", overwriteRatio: "0" }).overwriteRatio).toBeDefined();
    expect(validateForm({ ...DEFAULT_COVERED_CALL_FORM, minStrike: "500", overwriteRatio: "1.5" }).overwriteRatio).toBeDefined();
  });

  it("requires from <= to", () => {
    const errors = validateForm({
      ...DEFAULT_COVERED_CALL_FORM,
      minStrike: "500",
      from: "2024-12-01",
      to: "2024-01-01"
    });
    expect(errors.to).toBeDefined();
  });
});

describe("formToRequest", () => {
  it("maps numeric fields and uppercases the symbol", () => {
    const req = formToRequest({ ...DEFAULT_COVERED_CALL_FORM, underlyingSymbol: "spy", minStrike: "500", label: "  q1  " });
    expect(req.underlyingSymbol).toBe("SPY");
    expect(req.minStrike).toBe(500);
    expect(req.label).toBe("q1");
    expect(typeof req.overwriteRatio).toBe("number");
  });

  it("converts empty maxDte to null", () => {
    const req = formToRequest({ ...DEFAULT_COVERED_CALL_FORM, minStrike: "500", maxDte: "" });
    expect(req.maxDte).toBeNull();
  });
});

describe("isTerminalPhase", () => {
  it.each(["Completed", "Failed", "Cancelled"] as const)("treats %s as terminal", (phase) => {
    expect(isTerminalPhase(phase)).toBe(true);
  });
  it.each(["Queued", "WarmingUp", "Running"] as const)("treats %s as non-terminal", (phase) => {
    expect(isTerminalPhase(phase)).toBe(false);
  });
});

describe("buildChainPreviewPanelViewModel", () => {
  const chainPreview: CoveredCallChainPreview = {
    underlyingSymbol: "SPY",
    asOf: "2024-01-01",
    underlyingPrice: 500,
    totalContractsScanned: 2,
    filtersPassed: 1,
    candidates: [
      {
        strike: 505,
        expiration: "2024-02-16",
        daysToExpiration: 32,
        bid: 2.41,
        ask: 2.58,
        delta: 0.31,
        impliedVolatility: 0.22,
        openInterest: 1040,
        volume: 122,
        meetsAllFilters: true,
        rejectReason: null
      },
      {
        strike: 510,
        expiration: "2024-02-16",
        daysToExpiration: 32,
        bid: 1.71,
        ask: 1.95,
        delta: 0.42,
        impliedVolatility: null,
        openInterest: 84,
        volume: 12,
        meetsAllFilters: false,
        rejectReason: "Open interest below minimum"
      }
    ]
  };

  it("derives selectable chain rows and selected detail from preview data", () => {
    const panel = buildChainPreviewPanelViewModel({
      status: "ready",
      data: chainPreview,
      error: null,
      selectedIndex: 1
    });

    expect(panel.description).toBe("1 of 2 candidates pass filters.");
    expect(panel.detailPanelId).toBe(COVERED_CALL_CHAIN_DETAIL_PANEL_ID);
    expect(panel.selectedRowId).toBe(panel.rows[1].id);
    expect(panel.rows[1]).toMatchObject({
      statusLabel: "Open interest below minimum",
      statusBadgeVariant: "outline",
      detailPanelId: COVERED_CALL_CHAIN_DETAIL_PANEL_ID,
      ariaExpanded: true,
      rowSelectAriaLabel: "Inspect SPY 510.00 call expiring 2024-02-16. Status Open interest below minimum."
    });
    expect(panel.selectedDetail).toMatchObject({
      title: "SPY 510.00 call",
      statusLabel: "Open interest below minimum",
      ariaLabel: "Selected covered-call candidate: SPY 510.00 call expiring 2024-02-16"
    });
    expect(panel.selectedDetail?.fields).toContainEqual({ label: "Implied volatility", value: "—" });
  });

  it("keeps loading, empty, and error copy in the view model", () => {
    expect(buildChainPreviewPanelViewModel({
      status: "loading",
      data: null,
      error: null,
      selectedIndex: 0
    })).toMatchObject({
      description: "Loading chain preview...",
      emptyText: "Loading chain preview...",
      selectedDetail: null
    });

    expect(buildChainPreviewPanelViewModel({
      status: "ready",
      data: { ...chainPreview, candidates: [], filtersPassed: 0 },
      error: null,
      selectedIndex: 0
    })).toMatchObject({
      description: "No option candidates matched the current filters.",
      emptyText: "No candidates match the current filters.",
      detailEmptyText: "Adjust strike, delta, DTE, liquidity, or spread filters to find covered-call candidates."
    });

    expect(buildChainPreviewPanelViewModel({
      status: "error",
      data: null,
      error: "HTTP 503",
      selectedIndex: 0
    })).toMatchObject({
      description: "Error: HTTP 503",
      emptyText: "Chain preview failed: HTTP 503",
      detailEmptyTitle: "Chain preview failed"
    });
  });
});

describe("useCoveredCallScreenViewModel", () => {
  function makeServices(overrides: Partial<CoveredCallScreenServices> = {}): CoveredCallScreenServices {
    return {
      startRun: vi.fn(async (): Promise<CoveredCallRunHandle> => ({ runId: "abc", queuedAt: new Date().toISOString() })),
      getStatus: vi.fn(async (): Promise<CoveredCallRunStatus> => ({
        runId: "abc",
        phase: "Completed",
        percentComplete: 1,
        currentBacktestDate: null,
        failureMessage: null
      })),
      getResult: vi.fn(async (): Promise<CoveredCallRunResult> => ({
        runId: "abc",
        underlyingSymbol: "SPY",
        from: "2024-01-01",
        to: "2024-06-30",
        label: null,
        metrics: {
          cagr: 0.1,
          annualizedVolatility: 0.15,
          sharpeRatio: 0.7,
          sortinoRatio: 0.9,
          calmarRatio: 1.8,
          maxDrawdownPct: -0.05,
          winRate: 0.7,
          assignmentRate: 0.05,
          averageHoldingDays: 20,
          totalOptionTrades: 5,
          assignedTrades: 0,
          totalPremiumCollected: 1500,
          totalOptionPnl: 800,
          upCapture: 0.6,
          downCapture: 0.9,
          monthlyVar1Pct: -0.08,
          monthlyVar5Pct: -0.05,
          monthlyCVar5Pct: -0.06,
          returnSkewness: 0,
          returnKurtosis: 3,
          annualizedTurnover: 8
        },
        equityCurve: [],
        trades: [],
        openPositionsAtEnd: []
      })),
      cancelRun: vi.fn(async (): Promise<CoveredCallRunStatus> => ({
        runId: "abc", phase: "Cancelled", percentComplete: 0, currentBacktestDate: null, failureMessage: null
      })),
      previewChain: vi.fn(async (): Promise<CoveredCallChainPreview> => ({
        underlyingSymbol: "SPY",
        asOf: "2024-01-01",
        underlyingPrice: 500,
        candidates: [],
        totalContractsScanned: 0,
        filtersPassed: 0
      })),
      listRuns: vi.fn(async () => []),
      ...overrides
    };
  }

  it("setField updates form and clears the matching error", () => {
    const { result } = renderHook(() => useCoveredCallScreenViewModel({ services: makeServices(), pollIntervalMs: 10, chainPreviewDebounceMs: 100000 }));
    act(() => {
      // Trigger validation by attempting to start with invalid form
      void result.current.startRun();
    });
    expect(result.current.formErrors.minStrike).toBeDefined();

    act(() => result.current.setField("minStrike", "500"));
    expect(result.current.form.minStrike).toBe("500");
    expect(result.current.formErrors.minStrike).toBeUndefined();
  });

  it("startRun blocks when validation fails and surfaces a banner", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useCoveredCallScreenViewModel({ services, pollIntervalMs: 10, chainPreviewDebounceMs: 100000 }));

    await act(async () => {
      await result.current.startRun();
    });

    expect(services.startRun).not.toHaveBeenCalled();
    expect(result.current.errorBanner).toBeTruthy();
    expect(result.current.stage).toBe("configure");
  });

  it("startRun transitions to run stage and reaches results on Completed status", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useCoveredCallScreenViewModel({ services, pollIntervalMs: 5, chainPreviewDebounceMs: 100000 }));

    act(() => result.current.setField("minStrike", "500"));

    await act(async () => {
      await result.current.startRun();
    });

    // The run started: the run stage should have been entered (it may be replaced by results after polling).
    expect(services.startRun).toHaveBeenCalledTimes(1);

    await waitFor(() => {
      expect(result.current.stage).toBe("results");
      expect(result.current.run.result).not.toBeNull();
    }, { timeout: 1500 });

    expect(services.getStatus).toHaveBeenCalled();
    expect(services.getResult).toHaveBeenCalled();
  });

  it("cancelRun calls the cancel endpoint", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useCoveredCallScreenViewModel({ services, pollIntervalMs: 1000000, chainPreviewDebounceMs: 100000 }));

    act(() => result.current.setField("minStrike", "500"));
    await act(async () => {
      await result.current.startRun();
    });

    await act(async () => {
      await result.current.cancelRun();
    });

    expect(services.cancelRun).toHaveBeenCalledWith("abc");
  });

  it("loadHistory populates history", async () => {
    const services = makeServices({
      listRuns: vi.fn(async () => [
        { runId: "r1", underlyingSymbol: "SPY", from: "2024-01-01", to: "2024-06-30", label: null, status: "Completed", startedAt: "2024-07-01T00:00:00Z", endedAt: "2024-07-01T00:10:00Z", cagr: 0.1, sharpeRatio: 0.5, winRate: 0.7 }
      ])
    });
    const { result } = renderHook(() => useCoveredCallScreenViewModel({ services, pollIntervalMs: 1000000, chainPreviewDebounceMs: 100000 }));

    await act(async () => {
      await result.current.loadHistory();
    });

    expect(result.current.history).toHaveLength(1);
    expect(result.current.historyError).toBeNull();
  });

  it("openRun fetches result and switches to results stage", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useCoveredCallScreenViewModel({ services, pollIntervalMs: 1000000, chainPreviewDebounceMs: 100000 }));

    await act(async () => {
      await result.current.openRun("prior-run-id");
    });

    expect(services.getResult).toHaveBeenCalledWith("prior-run-id");
    expect(result.current.stage).toBe("results");
    expect(result.current.run.result).not.toBeNull();
    expect(result.current.run.runId).toBe("prior-run-id");
  });

  it("openRun surfaces an error banner when result is gone (e.g. 410)", async () => {
    const services = makeServices({
      getResult: vi.fn(async () => {
        throw new Error("Run completed but the cached result has expired.");
      })
    });
    const { result } = renderHook(() => useCoveredCallScreenViewModel({ services, pollIntervalMs: 1000000, chainPreviewDebounceMs: 100000 }));

    await act(async () => {
      await result.current.openRun("expired-run");
    });

    expect(result.current.errorBanner).toContain("expired");
    expect(result.current.stage).toBe("configure");
  });
});
