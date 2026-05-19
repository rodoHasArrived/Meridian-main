import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import {
  COVERED_CALL_CHAIN_DETAIL_PANEL_ID,
  buildCoveredCallFormFieldGroups,
  buildCoveredCallFormFields,
  buildChainPreviewPanelViewModel,
  buildCoveredCallCancelCommandState,
  buildCoveredCallPayoffPanel,
  buildCoveredCallRunCommandState,
  buildCoveredCallRunProgressPanel,
  buildCoveredCallResultsActionPanel,
  buildCoveredCallStageNavigationState,
  buildCoveredCallTradeTimelinePanel,
  DEFAULT_COVERED_CALL_FORM,
  formToRequest,
  isTerminalPhase,
  useCoveredCallScreenViewModel,
  validateForm,
  type CoveredCallRunState,
  type CoveredCallScreenServices
} from "@/screens/covered-call-screen.view-model";
import type {
  CoveredCallChainPreview,
  CoveredCallRunHandle,
  CoveredCallRunResult,
  CoveredCallRunStatus,
  CoveredCallTrade
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

describe("covered-call form field view models", () => {
  it("keeps field labels, helper copy, stable ids, and scoring controls in the view model", () => {
    const fields = buildCoveredCallFormFields({ minStrike: "Minimum strike must be greater than zero." });
    const groups = buildCoveredCallFormFieldGroups(fields);

    expect(fields.minStrike).toMatchObject({
      id: "cc-minStrike",
      label: "Min strike",
      type: "number",
      step: "0.01",
      required: true,
      helperText: "Lowest call strike the strategy may sell.",
      errorId: "cc-minStrike-error",
      describedBy: "cc-minStrike-help cc-minStrike-error",
      error: "Minimum strike must be greater than zero.",
      invalid: true
    });
    expect(fields.scoringMode).toMatchObject({
      id: "cc-scoringMode",
      label: "Scoring mode",
      type: "select",
      describedBy: "cc-scoringMode-help",
      invalid: false,
      options: [
        { value: "Relative", label: "Relative", description: "Rank by relative candidate quality." },
        { value: "Basic", label: "Basic", description: "Use the baseline filter score." }
      ]
    });
    expect(fields.depthBonusWeight).toMatchObject({
      id: "cc-depthBonusWeight",
      label: "Depth bonus weight",
      helperText: "Extra score weight for deeper option-chain liquidity when Relative scoring is selected."
    });
    expect(groups.map((group) => group.id)).toContain("scoring");
    expect(groups.find((group) => group.id === "scoring")?.fields.map((field) => field.key)).toEqual([
      "scoringMode",
      "depthBonusWeight"
    ]);
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

describe("covered-call run command view models", () => {
  const idleRun: CoveredCallRunState = {
    runId: null,
    status: null,
    result: null,
    selectedPositionIndex: 0,
    selectedTradeIndex: 0,
    isStarting: false,
    isCancelling: false
  };

  it("disables the run command until the required form fields are valid", () => {
    expect(buildCoveredCallRunCommandState(DEFAULT_COVERED_CALL_FORM, false)).toMatchObject({
      label: "Run backtest",
      disabled: true,
      disabledReason: "Minimum strike must be greater than zero.",
      feedbackId: "covered-call-run-command-feedback",
      feedbackText: "Cannot run yet: Minimum strike must be greater than zero.",
      busy: false
    });

    expect(buildCoveredCallRunCommandState({ ...DEFAULT_COVERED_CALL_FORM, minStrike: "500" }, false)).toMatchObject({
      label: "Run backtest",
      ariaLabel: "Run covered-call backtest",
      disabled: false,
      disabledReason: null,
      feedbackText: null,
      busy: false
    });
  });

  it("exposes submission and cancellation busy states for the shared button primitive", () => {
    expect(buildCoveredCallRunCommandState({ ...DEFAULT_COVERED_CALL_FORM, minStrike: "500" }, true)).toMatchObject({
      label: "Submitting...",
      ariaLabel: "Submitting covered-call backtest",
      disabled: false,
      feedbackText: "Submitting covered-call run request.",
      busy: true,
      busyLabel: "Submitting..."
    });

    expect(buildCoveredCallCancelCommandState({ ...idleRun, runId: "run-1", isCancelling: true })).toMatchObject({
      label: "Cancelling run",
      ariaLabel: "Cancelling covered-call backtest run",
      disabled: false,
      feedbackText: "Cancelling covered-call backtest run.",
      busy: true,
      busyLabel: "Cancelling..."
    });
  });

  it("keeps cancel disabled reasons and progress copy in the view model", () => {
    expect(buildCoveredCallCancelCommandState(idleRun)).toMatchObject({
      disabled: true,
      disabledReason: "Run ID is not available until the engine accepts the request.",
      feedbackId: "covered-call-cancel-command-feedback",
      feedbackText: "Run ID is not available until the engine accepts the request."
    });

    expect(buildCoveredCallCancelCommandState({
      ...idleRun,
      runId: "run-1",
      status: { runId: "run-1", phase: "Completed", percentComplete: 1, currentBacktestDate: null, failureMessage: null }
    })).toMatchObject({
      disabled: true,
      disabledReason: "Run is already completed.",
      feedbackText: "Run is already completed."
    });

    expect(buildCoveredCallCancelCommandState({
      ...idleRun,
      runId: "run-1",
      status: { runId: "run-1", phase: "Running", percentComplete: 0.42, currentBacktestDate: "2024-03-01", failureMessage: null }
    }, true)).toMatchObject({
      label: "Confirm cancel",
      ariaLabel: "Confirm cancel covered-call backtest run run-1. This stops the active backtest request.",
      disabled: false,
      disabledReason: null,
      feedbackText: "Cancel confirmation pending. Confirm cancel stops this covered-call backtest run."
    });

    expect(buildCoveredCallRunProgressPanel({ ...idleRun, isStarting: true })).toMatchObject({
      title: "Submitting backtest",
      description: "Submitting covered-call run request to the strategy engine.",
      percentComplete: 0,
      ariaValueText: "Submitting covered-call run request.",
      ariaBusy: true
    });

    expect(buildCoveredCallRunProgressPanel({
      ...idleRun,
      runId: "run-1",
      status: { runId: "run-1", phase: "Running", percentComplete: 0.42, currentBacktestDate: "2024-03-01", failureMessage: null }
    })).toMatchObject({
      title: "Running backtest",
      description: "Phase: Running - 2024-03-01",
      percentComplete: 42,
      ariaValueText: "Running 42% complete.",
      ariaBusy: true
    });
  });

  it("locks stage navigation while submit or cancel actions are unresolved", () => {
    const submitting = buildCoveredCallStageNavigationState({ ...idleRun, isStarting: true }, "run");
    expect(submitting).toMatchObject({
      feedbackId: "covered-call-stage-navigation-feedback",
      feedbackText: "Wait until the strategy engine accepts the backtest request before leaving run progress.",
      configure: {
        disabled: true,
        disabledReason: "Wait until the strategy engine accepts the backtest request before leaving run progress."
      },
      run: {
        disabled: false,
        disabledReason: null
      },
      results: {
        disabled: true,
        disabledReason: "Wait until the strategy engine accepts the backtest request before leaving run progress."
      }
    });
    expect(submitting.steps).toMatchObject([
      {
        stage: "configure",
        buttonLabel: "1. Configure",
        ariaDescribedBy: "covered-call-stage-navigation-feedback",
        ariaCurrent: undefined,
        isCurrent: false,
        disabled: true
      },
      {
        stage: "run",
        buttonLabel: "2. Run",
        ariaLabel: "2. Run",
        ariaCurrent: "step",
        isCurrent: true,
        disabled: false
      },
      {
        stage: "results",
        buttonLabel: "3. Results",
        ariaDescribedBy: "covered-call-stage-navigation-feedback",
        ariaCurrent: undefined,
        isCurrent: false,
        disabled: true
      }
    ]);

    expect(buildCoveredCallStageNavigationState({ ...idleRun, runId: "run-1", isCancelling: true })).toMatchObject({
      feedbackId: "covered-call-stage-navigation-feedback",
      feedbackText: "Wait until cancellation completes before leaving run progress.",
      configure: {
        disabled: true,
        disabledReason: "Wait until cancellation completes before leaving run progress."
      },
      run: {
        disabled: false,
        disabledReason: null
      },
      results: {
        disabled: true,
        disabledReason: "Wait until cancellation completes before leaving run progress."
      }
    });
  });

  it("builds post-run workflow handoffs from completed result evidence", () => {
    const panel = buildCoveredCallResultsActionPanel({
      runId: "abc",
      underlyingSymbol: "spy",
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
    });

    expect(panel).toMatchObject({
      title: "Next workflow",
      description: "Use the SPY backtest evidence while the context is fresh."
    });
    expect(panel.actions).toEqual([
      expect.objectContaining({
        id: "live-quote",
        href: "/data/quotes?symbol=SPY",
        ariaLabel: "Validate live quote evidence for SPY"
      }),
      expect.objectContaining({
        id: "strategy-designer",
        href: "/strategy/designer"
      }),
      expect.objectContaining({
        id: "report-pack",
        href: "/reporting/report-packs"
      })
    ]);
  });
});

describe("buildCoveredCallTradeTimelinePanel", () => {
  const trade: CoveredCallTrade = {
    strike: 505,
    expiration: "2024-02-16",
    contracts: 2,
    multiplier: 100,
    entryDate: "2024-01-10",
    entryCredit: 2.35,
    exitDate: "2024-01-24",
    exitDebit: 0.75,
    exitReason: "TakeProfit",
    entryImpliedVolatility: 0.22,
    netPnlPerContract: 160,
    totalNetPnl: 320,
    holdingDays: 14,
    isWin: true,
    wasAssigned: false
  };

  function runResult(trades: CoveredCallTrade[]): CoveredCallRunResult {
    return {
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
        totalOptionTrades: trades.length,
        assignedTrades: trades.filter((item) => item.wasAssigned).length,
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
      trades,
      openPositionsAtEnd: []
    };
  }

  it("projects covered-call trades into selectable rows and selected detail evidence", () => {
    const loss = {
      ...trade,
      strike: 510,
      entryDate: "2024-02-01",
      exitDate: "2024-02-12",
      exitReason: "Assigned",
      totalNetPnl: -140,
      netPnlPerContract: -70,
      isWin: false,
      wasAssigned: true
    };

    const panel = buildCoveredCallTradeTimelinePanel(runResult([trade, loss]), 1);

    expect(panel).toMatchObject({
      title: "Trades (2)",
      tableLabel: "SPY covered-call trade timeline",
      selectedRowId: panel.rows[1].id
    });
    expect(panel.rows[1]).toMatchObject({
      entryDateLabel: "2024-02-01",
      exitDateLabel: "2024-02-12",
      strikeLabel: "510.00",
      pnlLabel: "-$140",
      pnlClassName: "text-danger",
      statusLabel: "Assigned",
      exitReasonLabel: "Assigned",
      statusBadgeVariant: "warning",
      detailPanelId: "covered-call-trade-detail",
      ariaExpanded: true,
      rowSelectAriaLabel: "Inspect SPY trade 2, entry 2024-02-01, exit 2024-02-12, strike 510.00, PnL -$140, status Assigned."
    });
    expect(panel.selectedDetail).toMatchObject({
      title: "SPY 510.00 call",
      statusLabel: "Assigned",
      ariaLabel: "Selected covered-call trade 2: SPY 510.00 call"
    });
    expect(panel.rows[0].exitReasonLabel).toBe("Take profit");
    expect(panel.selectedDetail?.description).toBe("Assigned; exit reason Assigned; -$140 total net PnL.");
    expect(panel.selectedDetail?.fields).toContainEqual({ label: "Exit reason", value: "Assigned" });
    expect(panel.selectedDetail?.fields).toContainEqual({ label: "Assignment", value: "Assigned" });
  });

  it("keeps empty trade timeline state in the view model", () => {
    expect(buildCoveredCallTradeTimelinePanel(runResult([]), 0)).toMatchObject({
      title: "Trades (0)",
      emptyText: "No trades recorded.",
      detailEmptyText: "This completed run did not record covered-call trade fills.",
      selectedRowId: null,
      selectedDetail: null
    });
  });
});

describe("buildCoveredCallPayoffPanel", () => {
  function runResult(openPositionsAtEnd: CoveredCallRunResult["openPositionsAtEnd"]): CoveredCallRunResult {
    return {
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
        totalOptionTrades: 0,
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
      openPositionsAtEnd
    };
  }

  it("projects selectable open positions and chart geometry into the view model", () => {
    const panel = buildCoveredCallPayoffPanel(runResult([
      {
        positionId: "pos-505",
        strike: 505,
        expiration: "2024-02-16",
        contracts: 2,
        multiplier: 100,
        entryDate: "2024-01-10",
        entryCredit: 2.35,
        markToClose: 0.75,
        currentDelta: 0.31,
        currentDte: 21,
        unrealisedPnl: 320,
        premiumCaptured: 0.68
      },
      {
        positionId: "pos-510",
        strike: 510,
        expiration: "2024-03-15",
        contracts: 1,
        multiplier: 100,
        entryDate: "2024-02-01",
        entryCredit: 1.9,
        markToClose: 3.3,
        currentDelta: 0.42,
        currentDte: 44,
        unrealisedPnl: -140,
        premiumCaptured: 0.42
      }
    ]), 1);

    expect(panel).toMatchObject({
      title: "Payoff diagram (short call leg)",
      selectorAriaLabel: "Covered-call open positions",
      description: "1 x 510.00 call expiring 2024-03-15 - short-call break-even about $511.90",
      emptyText: null
    });
    expect(panel.positionOptions).toHaveLength(2);
    expect(panel.positionOptions[1]).toMatchObject({
      id: "pos-510",
      label: "510.00 call",
      description: "2024-03-15 - 1 contract",
      selected: true,
      buttonVariant: "secondary",
      ariaLabel: "Selected SPY 510.00 call expiring 2024-03-15 payoff diagram"
    });
    expect(panel.chart).toMatchObject({
      viewBox: "0 0 320 180",
      ariaLabel: "SPY 510.00 short-call payoff diagram"
    });
    expect(panel.chart?.path).toMatch(/^M/);
  });

  it("keeps empty payoff state in the view model", () => {
    expect(buildCoveredCallPayoffPanel(runResult([]), 0)).toMatchObject({
      title: "Payoff diagram",
      description: "Short-call payoff diagram for any open position at end of run.",
      emptyText: "No open positions at end of run.",
      positionOptions: [],
      chart: null
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
    expect(result.current.errorBanner?.summary).toMatch(/minimum strike/i);
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

  it("startRun exposes a submitting command state and blocks duplicate submissions", async () => {
    let resolveStart: ((value: CoveredCallRunHandle) => void) | undefined;
    const services = makeServices({
      startRun: vi.fn(() => new Promise<CoveredCallRunHandle>((resolve) => {
        resolveStart = resolve;
      }))
    });
    const { result } = renderHook(() => useCoveredCallScreenViewModel({ services, pollIntervalMs: 1000000, chainPreviewDebounceMs: 100000 }));

    act(() => result.current.setField("minStrike", "500"));

    act(() => {
      void result.current.startRun();
    });

    expect(result.current.stage).toBe("run");
    expect(result.current.runCommand).toMatchObject({
      label: "Submitting...",
      busy: true
    });
    expect(result.current.cancelRunCommand).toMatchObject({
      disabled: true,
      disabledReason: "Run ID is not available until the engine accepts the request."
    });

    act(() => {
      void result.current.startRun();
    });
    expect(services.startRun).toHaveBeenCalledTimes(1);

    act(() => {
      result.current.goToStage("configure");
    });
    expect(result.current.stage).toBe("run");

    await act(async () => {
      resolveStart?.({ runId: "abc", queuedAt: new Date().toISOString() });
    });

    expect(result.current.runCommand.busy).toBe(false);
    expect(result.current.cancelRunCommand.disabled).toBe(false);
  });

  it("cancelRun requires a confirmation pass before calling the cancel endpoint", async () => {
    const services = makeServices();
    const { result } = renderHook(() => useCoveredCallScreenViewModel({ services, pollIntervalMs: 1000000, chainPreviewDebounceMs: 100000 }));

    act(() => result.current.setField("minStrike", "500"));
    await act(async () => {
      await result.current.startRun();
    });

    await act(async () => {
      await result.current.cancelRun();
    });

    expect(services.cancelRun).not.toHaveBeenCalled();
    expect(result.current.cancelRunCommand).toMatchObject({
      label: "Confirm cancel",
      feedbackText: "Cancel confirmation pending. Confirm cancel stops this covered-call backtest run."
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

  it("loadHistory keeps structured backend details when history fails", async () => {
    const services = makeServices({
      listRuns: vi.fn(async () => {
        throw new ApiError({
          path: "/api/covered-call/runs?limit=50",
          status: 503,
          title: "History store unavailable",
          detail: "Covered-call run history is temporarily offline."
        });
      })
    });
    const { result } = renderHook(() => useCoveredCallScreenViewModel({ services, pollIntervalMs: 1000000, chainPreviewDebounceMs: 100000 }));

    await act(async () => {
      await result.current.loadHistory();
    });

    expect(result.current.historyError).toEqual({
      summary: "Covered-call run history is temporarily offline.",
      details: [
        "Endpoint returned 503 for /api/covered-call/runs?limit=50.",
        "History store unavailable"
      ]
    });
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

    expect(result.current.errorBanner?.summary).toContain("expired");
    expect(result.current.stage).toBe("configure");
  });

  it("cancelRun keeps structured backend details when the cancel request fails", async () => {
    const services = makeServices({
      cancelRun: vi.fn(async () => {
        throw new ApiError({
          path: "/api/covered-call/runs/abc/cancel",
          status: 409,
          title: "Cancellation blocked",
          detail: "The run already completed before cancellation reached the engine."
        });
      })
    });
    const { result } = renderHook(() => useCoveredCallScreenViewModel({ services, pollIntervalMs: 1000000, chainPreviewDebounceMs: 100000 }));

    act(() => result.current.setField("minStrike", "500"));
    await act(async () => {
      await result.current.startRun();
    });

    await act(async () => {
      await result.current.cancelRun();
    });
    await act(async () => {
      await result.current.cancelRun();
    });

    expect(result.current.errorBanner).toEqual({
      summary: "The run already completed before cancellation reached the engine.",
      details: [
        "Endpoint returned 409 for /api/covered-call/runs/abc/cancel.",
        "Cancellation blocked"
      ]
    });
  });
});
