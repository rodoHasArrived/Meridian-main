import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
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
});
