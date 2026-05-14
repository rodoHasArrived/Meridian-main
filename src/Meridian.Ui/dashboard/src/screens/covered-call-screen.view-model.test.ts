import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  COVERED_CALL_CHAIN_DETAIL_PANEL_ID,
  buildCoveredCallStageSteps,
  buildHistoryPanelViewModel,
  buildChainPreviewPanelViewModel,
  DEFAULT_COVERED_CALL_FORM,
  formatUtcMinute,
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
  CoveredCallRunSummary,
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

describe("buildCoveredCallStageSteps", () => {
  it("keeps unavailable run and results stages disabled until evidence exists", () => {
    const steps = buildCoveredCallStageSteps("configure", {
      runId: null,
      status: null,
      result: null,
      selectedPositionIndex: 0
    });

    expect(steps.map((step) => step.buttonLabel)).toEqual(["1. Configure", "2. Run", "3. Results"]);
    expect(steps[0]).toMatchObject({
      id: "configure",
      isCurrent: true,
      disabled: false,
      ariaLabel: "Current step: Configure"
    });
    expect(steps[1]).toMatchObject({
      id: "run",
      disabled: true,
      disabledReason: "Start a covered-call backtest before opening run status.",
      ariaLabel: "Run step unavailable: Start a covered-call backtest before opening run status."
    });
    expect(steps[2]).toMatchObject({
      id: "results",
      disabled: true,
      disabledReason: "Complete or open a covered-call run before opening results.",
      ariaLabel: "Results step unavailable: Complete or open a covered-call run before opening results."
    });
  });

  it("enables run after submission and results after a completed result loads", () => {
    const queuedSteps = buildCoveredCallStageSteps("configure", {
      runId: "run-1",
      status: null,
      result: null,
      selectedPositionIndex: 0
    });
    expect(queuedSteps[1]).toMatchObject({
      id: "run",
      disabled: false,
      ariaLabel: "Open Run step"
    });
    expect(queuedSteps[2].disabled).toBe(true);

    const completedSteps = buildCoveredCallStageSteps("results", {
      runId: "run-1",
      status: {
        runId: "run-1",
        phase: "Completed",
        percentComplete: 1,
        currentBacktestDate: null,
        failureMessage: null
      },
      result: completedRunResult("run-1"),
      selectedPositionIndex: 0
    });

    expect(completedSteps[1]).toMatchObject({
      id: "run",
      disabled: false,
      ariaLabel: "Open Run step"
    });
    expect(completedSteps[2]).toMatchObject({
      id: "results",
      isCurrent: true,
      disabled: false,
      ariaLabel: "Current step: Results"
    });
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

describe("buildHistoryPanelViewModel", () => {
  const history: CoveredCallRunSummary[] = [
    {
      runId: "run/dev-1",
      underlyingSymbol: "SPY",
      from: "2024-01-01",
      to: "2024-06-30",
      label: "Income sleeve",
      status: "Completed",
      startedAt: "2024-06-30T14:05:30Z",
      endedAt: "2024-06-30T14:08:00Z",
      cagr: 0.126,
      sharpeRatio: 1.234,
      winRate: 0.71
    },
    {
      runId: "run-failed",
      underlyingSymbol: "QQQ",
      from: "2024-01-01",
      to: "2024-06-30",
      label: null,
      status: "Failed",
      startedAt: "not-a-date",
      endedAt: null,
      cagr: null,
      sharpeRatio: null,
      winRate: null
    }
  ];

  it("builds UTC-labelled selectable rows with outcome metrics", () => {
    const panel = buildHistoryPanelViewModel({ history, historyError: null, selectedRunId: "run/dev-1" });

    expect(panel.tableLabel).toBe("Previous covered-call backtest runs");
    expect(panel.selectedRowId).toBe("covered-call-history-run-dev-1");
    expect(panel.rows[0]).toMatchObject({
      isOpening: false,
      startedAtLabel: "Jun 30, 2024 14:05 UTC",
      rangeLabel: "2024-01-01 to 2024-06-30",
      statusBadgeVariant: "success",
      cagrLabel: "12.60%",
      sharpeLabel: "1.23",
      winRateLabel: "71.00%",
      rowSelectAriaLabel: "Open covered-call run run/dev-1. SPY. Completed. Started Jun 30, 2024 14:05 UTC."
    });
    expect(panel.rows[1]).toMatchObject({
      startedAtLabel: "Invalid timestamp",
      statusBadgeVariant: "danger",
      cagrLabel: "—",
      labelText: "Unlabeled run"
    });
  });

  it("marks the opening run with view-model owned selection and copy", () => {
    const panel = buildHistoryPanelViewModel({
      history,
      historyError: null,
      selectedRunId: "run-failed",
      openingRunId: "run/dev-1"
    });

    expect(panel.description).toBe("Opening saved covered-call evidence. Late results from earlier selections are ignored.");
    expect(panel.selectedRowId).toBe("covered-call-history-run-dev-1");
    expect(panel.rows[0]).toMatchObject({
      isOpening: true,
      statusLabel: "Opening...",
      statusBadgeVariant: "warning",
      rowSelectAriaLabel: "Opening covered-call run run/dev-1. SPY. Started Jun 30, 2024 14:05 UTC."
    });
  });

  it("keeps history load failures and retry labels in the view model", () => {
    const panel = buildHistoryPanelViewModel({ history: [], historyError: "HTTP 503", selectedRunId: null });

    expect(panel.description).toContain("Retry");
    expect(panel.errorTitle).toBe("Run history failed to load");
    expect(panel.errorDescription).toBe("HTTP 503");
    expect(panel.retryAriaLabel).toBe("Retry covered-call run history");
    expect(panel.emptyText).toBe("Run history failed to load.");
  });

  it("keeps history loading copy and disabled retry state in the view model", () => {
    const panel = buildHistoryPanelViewModel({
      history: [],
      historyError: "HTTP 503",
      historyLoading: true,
      selectedRunId: null
    });

    expect(panel.description).toBe("Loading saved covered-call evidence...");
    expect(panel.errorTitle).toBeNull();
    expect(panel.errorDescription).toBeNull();
    expect(panel.retryLabel).toBe("Loading history...");
    expect(panel.retryAriaLabel).toBe("Loading covered-call run history");
    expect(panel.retryDisabled).toBe(true);
    expect(panel.emptyText).toBe("Loading run history...");
  });

  it("formats UTC minute labels defensively", () => {
    expect(formatUtcMinute("2024-01-02T03:04:05Z")).toBe("Jan 02, 2024 03:04 UTC");
    expect(formatUtcMinute(null)).toBe("Not recorded");
    expect(formatUtcMinute("bad")).toBe("Invalid timestamp");
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

  it("goToStage blocks unavailable stages through the view model", () => {
    const { result } = renderHook(() => useCoveredCallScreenViewModel({ services: makeServices(), pollIntervalMs: 10, chainPreviewDebounceMs: 100000 }));

    act(() => result.current.goToStage("results"));

    expect(result.current.stage).toBe("configure");
    expect(result.current.errorBanner).toBe("Complete or open a covered-call run before opening results.");
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
    expect(result.current.historyLoading).toBe(false);
  });

  it("tracks history loading and ignores stale history results", async () => {
    const first = deferred<CoveredCallRunSummary[]>();
    const second = deferred<CoveredCallRunSummary[]>();
    const services = makeServices({
      listRuns: vi.fn()
        .mockReturnValueOnce(first.promise)
        .mockReturnValueOnce(second.promise)
    });
    const { result } = renderHook(() => useCoveredCallScreenViewModel({ services, pollIntervalMs: 1000000, chainPreviewDebounceMs: 100000 }));

    void act(() => {
      void result.current.loadHistory();
    });
    expect(result.current.historyLoading).toBe(true);
    expect(result.current.historyPanel.retryDisabled).toBe(true);

    void act(() => {
      void result.current.loadHistory();
    });

    await act(async () => {
      first.resolve([
        { runId: "stale", underlyingSymbol: "SPY", from: "2024-01-01", to: "2024-06-30", label: null, status: "Completed", startedAt: "2024-07-01T00:00:00Z", endedAt: null, cagr: 0.1, sharpeRatio: 0.5, winRate: 0.7 }
      ]);
      await first.promise;
    });

    expect(result.current.history).toHaveLength(0);
    expect(result.current.historyLoading).toBe(true);

    await act(async () => {
      second.resolve([
        { runId: "fresh", underlyingSymbol: "QQQ", from: "2024-01-01", to: "2024-06-30", label: null, status: "Completed", startedAt: "2024-07-01T00:00:00Z", endedAt: null, cagr: 0.2, sharpeRatio: 0.8, winRate: 0.6 }
      ]);
      await second.promise;
    });

    expect(result.current.history.map((run) => run.runId)).toEqual(["fresh"]);
    expect(result.current.historyLoading).toBe(false);
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

  it("ignores stale openRun results when a newer history row is selected", async () => {
    const first = deferred<CoveredCallRunResult>();
    const second = deferred<CoveredCallRunResult>();
    const services = makeServices({
      getResult: vi.fn((runId: string) => {
        if (runId === "first-run") {
          return first.promise;
        }
        if (runId === "second-run") {
          return second.promise;
        }
        throw new Error(`Unexpected run ${runId}`);
      })
    });
    const { result } = renderHook(() => useCoveredCallScreenViewModel({ services, pollIntervalMs: 1000000, chainPreviewDebounceMs: 100000 }));

    void act(() => {
      void result.current.openRun("first-run");
    });
    expect(result.current.historyOpeningRunId).toBe("first-run");

    void act(() => {
      void result.current.openRun("second-run");
    });
    expect(result.current.historyOpeningRunId).toBe("second-run");

    await act(async () => {
      second.resolve({ ...completedRunResult("second-run"), underlyingSymbol: "QQQ" });
      await second.promise;
    });

    expect(result.current.run.runId).toBe("second-run");
    expect(result.current.run.result?.underlyingSymbol).toBe("QQQ");
    expect(result.current.historyOpeningRunId).toBeNull();

    await act(async () => {
      first.resolve({ ...completedRunResult("first-run"), underlyingSymbol: "SPY" });
      await first.promise;
    });

    expect(result.current.run.runId).toBe("second-run");
    expect(result.current.run.result?.underlyingSymbol).toBe("QQQ");
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

function completedRunResult(runId: string): CoveredCallRunResult {
  return {
    runId,
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
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}
