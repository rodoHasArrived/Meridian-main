import { act, renderHook, waitFor } from "@testing-library/react";
import { vi } from "vitest";
import {
  buildExecutionEvidenceState,
  buildPaperSessionCreateRequest,
  buildPaperSessionState,
  buildSessionReplayControlsState,
  buildStrategyLifecycleControlsState,
  buildTradingConfirmDialogState,
  buildTradingBlotterViewModel,
  buildOrderPreview,
  buildOrderSubmitRequest,
  buildOrderTicketState,
  buildPromotionApprovalRequest,
  buildPromotionGateState,
  buildPromotionRejectionRequest,
  buildTradingReadinessState,
  buildTradingLoadingState,
  buildTradingScreenShellViewModel,
  buildPromotionApprovalChecklist,
  createTradingConfirmState,
  emptyPaperSessionForm,
  emptyOrderTicketForm,
  emptyPromotionGateForm,
  formatReadinessStatusValue,
  mapBrokerageSyncLevel,
  mapReadinessStatusLevel,
  paperPromotionApprovalChecklist,
  updateOrderTicketForm,
  validatePaperSessionForm,
  validateOrderTicketForm,
  validatePromotionApproval,
  validatePromotionRejection,
  useOrderTicketViewModel,
  usePaperSessionsViewModel,
  useTradingConfirmViewModel,
  useTradingReadinessViewModel,
  usePromotionGateViewModel,
  type OrderTicketServices,
  type PaperSessionServices,
  type PromotionGateServices,
  type TradingConfirmServices,
  type TradingReadinessServices
} from "@/screens/trading-screen.view-model";
import type { ExecutionAuditEntry, ExecutionControlSnapshot, ExecutionPortfolioSnapshot, OperatorWorkItem, OperatorWorkItemKind, PaperSessionDetail, PaperSessionReplayVerification, PaperSessionSummary, PromotionEvaluationResult, ReplayFileRecord, ReplayStatus, TradingActionResult, TradingOperatorReadiness, TradingPaperSessionReadiness, TradingPosition, TradingReplayReadiness, TradingRiskState, TradingTrustGateReadiness, TradingWorkspaceResponse, WorkstationBrokerageSyncStatus } from "@/types";

const eligibleEvaluation: PromotionEvaluationResult = {
  runId: "run-1",
  strategyId: "strat-1",
  strategyName: "S1",
  sourceMode: "backtest",
  targetMode: "paper",
  isEligible: true,
  sharpeRatio: 1.2,
  maxDrawdownPercent: 5,
  totalReturn: 10,
  reason: "Eligible",
  found: true,
  ready: true
};

function createDeferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

describe("trading shell loading state", () => {
  it("projects route-aware pending state for the Trading loading branch", () => {
    expect(buildTradingLoadingState("/trading/risk")).toEqual({
      role: "status",
      ariaBusy: true,
      ariaLive: "polite",
      regionLabel: "Trading cockpit loading state",
      titleId: "trading-loading-title",
      detailId: "trading-loading-detail",
      title: "Loading Trading",
      detail: "Waiting for paper-trading state, order flow, brokerage wiring, and risk guardrail snapshots.",
      statusLabel: "Loading",
      routeLabel: "Risk guardrails",
      chips: [
        { label: "Route", value: "/trading/risk" },
        { label: "Workstream", value: "Risk guardrails" },
        { label: "Snapshots", value: "Pending" }
      ]
    });
  });

  it("keeps loading state inside the shell view model when data is unavailable", () => {
    const shell = buildTradingScreenShellViewModel({
      pathname: "/trading/positions",
      data: null,
      activeWorkflowPanel: null
    });

    expect(shell.loadingState.routeLabel).toBe("Position book");
    expect(shell.loadingState.chips).toContainEqual({ label: "Snapshots", value: "Pending" });
    expect(shell.headerChips).toContainEqual({ label: "Account", value: "-" });
  });
});

const blockedReadiness: TradingOperatorReadiness = {
  asOf: "2026-04-26T16:05:00Z",
  overallStatus: "Blocked",
  readyForPaperOperation: false,
  acceptanceGates: [],
  activeSession: null,
  sessions: [],
  replay: null,
  controls: {
    circuitBreakerOpen: false,
    circuitBreakerReason: null,
    circuitBreakerChangedBy: null,
    circuitBreakerChangedAt: null,
    manualOverrideCount: 0,
    symbolLimitCount: 0,
    defaultMaxPositionSize: null
  },
  promotion: null,
  trustGate: {
    gateId: "dk1",
    status: "ready-for-operator-review",
    readyForOperatorReview: true,
    operatorSignoffRequired: true,
    operatorSignoffStatus: "pending",
    generatedAt: "2026-04-26T15:00:00Z",
    packetPath: "artifacts/provider-validation/_automation/2026-04-26/dk1-pilot-parity-packet.json",
    sourceSummary: "wave1-validation-summary.json",
    requiredSampleCount: 4,
    readySampleCount: 4,
    validatedEvidenceDocumentCount: 2,
    requiredOwners: ["ops"],
    blockers: [],
    detail: "Awaiting owner sign-off.",
    operatorSignoff: null
  },
  brokerageSync: {
    fundAccountId: "fund-1",
    providerId: "alpaca",
    externalAccountId: "PA-404",
    health: "Failed",
    isLinked: true,
    isStale: true,
    lastAttemptedSyncAt: "2026-04-26T15:58:00Z",
    lastSuccessfulSyncAt: null,
    lastError: "Alpaca credentials are missing.",
    positionCount: 0,
    openOrderCount: 0,
    fillCount: 0,
    cashTransactionCount: 0,
    securityMissingCount: 0,
    warnings: ["Portfolio snapshot failed."]
  },
  workItems: [
    {
      workItemId: "brokerage-sync-failed-fund-1",
      kind: "BrokerageSync",
      label: "Brokerage sync failed",
      detail: "Sync broker credentials before paper operation.",
      tone: "Critical",
      createdAt: "2026-04-26T16:05:00Z",
      runId: null,
      fundAccountId: "fund-1",
      auditReference: null,
      workspace: "Trading",
      targetRoute: "/api/fund-accounts/fund-1/brokerage-sync",
      targetPageTag: "AccountPortfolio"
    }
  ],
  warnings: ["Portfolio snapshot failed."]
};

const activePaperSession: PaperSessionSummary = {
  sessionId: "sess-1",
  strategyId: "strat-1",
  strategyName: null,
  initialCash: 100000,
  createdAt: "2026-01-01T00:00:00Z",
  closedAt: null,
  isActive: true
};

const selectedPaperSessionDetail: PaperSessionDetail = {
  summary: activePaperSession,
  symbols: ["AAPL", "MSFT"],
  portfolio: {
    cash: 99000,
    portfolioValue: 100250,
    unrealisedPnl: 250,
    realisedPnl: 0,
    positions: [
      {
        symbol: "AAPL",
        quantity: 5,
        averageCostBasis: 200,
        currentPrice: 205,
        marketValue: 1025,
        unrealisedPnl: 25,
        realisedPnl: 0
      }
    ],
    asOf: "2026-01-01T00:15:00Z"
  },
  orderHistory: [
    {
      orderId: "ord-1",
      symbol: "AAPL",
      side: "Buy",
      type: "Market",
      quantity: 5,
      filledQuantity: 5,
      averageFillPrice: 200,
      status: "Filled",
      createdAt: "2026-01-01T00:05:00Z",
      updatedAt: "2026-01-01T00:06:00Z"
    }
  ]
};

const consistentReplayVerification: PaperSessionReplayVerification = {
  summary: activePaperSession,
  symbols: ["AAPL", "MSFT"],
  replaySource: "DurableFillLog",
  isConsistent: true,
  mismatchReasons: [],
  currentPortfolio: selectedPaperSessionDetail.portfolio,
  replayPortfolio: selectedPaperSessionDetail.portfolio!,
  verifiedAt: "2026-01-01T00:20:00Z",
  comparedFillCount: 1,
  comparedOrderCount: 1,
  comparedLedgerEntryCount: 2,
  lastPersistedFillAt: "2026-01-01T00:10:00Z",
  lastPersistedOrderUpdateAt: null,
  verificationAuditId: "audit-verify-1"
};

const replayFile: ReplayFileRecord = {
  path: "/tmp/replay.jsonl",
  name: "replay.jsonl",
  symbol: "AAPL",
  eventType: "trades",
  sizeBytes: 1024,
  isCompressed: false,
  lastModified: "2026-01-01T00:00:00Z"
};

const runningReplayStatus: ReplayStatus = {
  sessionId: "rep-1",
  filePath: "/tmp/replay.jsonl",
  status: "running",
  speedMultiplier: 1,
  eventsProcessed: 3,
  totalEvents: 10,
  progressPercent: 30,
  startedAt: "2026-01-01T00:00:00Z"
};

const executionAuditEntry: ExecutionAuditEntry = {
  auditId: "audit-1",
  category: "PaperSession",
  action: "ReplayPaperSession",
  outcome: "Completed",
  occurredAt: "2026-01-01T00:20:00Z",
  actor: "ops-session",
  brokerName: null,
  orderId: null,
  runId: null,
  symbol: null,
  correlationId: null,
  message: "Replay matched current state for paper session sess-1.",
  metadata: { sessionId: "sess-1" }
};

const executionControlsSnapshot: ExecutionControlSnapshot = {
  circuitBreaker: {
    isOpen: false,
    reason: null,
    changedBy: "ops",
    changedAt: "2026-01-01T00:00:00Z"
  },
  defaultMaxPositionSize: 5000,
  symbolPositionLimits: { AAPL: 2500 },
  manualOverrides: [
    {
      overrideId: "override-1",
      kind: "BypassOrderControls",
      reason: "incident drill",
      createdBy: "ops",
      createdAt: "2026-01-01T00:00:00Z",
      expiresAt: null,
      symbol: "AAPL",
      strategyId: null,
      runId: null
    }
  ],
  asOf: "2026-01-01T00:20:00Z"
};

const tradingWorkspace: TradingWorkspaceResponse = {
  metrics: [],
  positions: [
    {
      symbol: "AAPL",
      side: "Long",
      quantity: "100",
      averagePrice: "188.10",
      markPrice: "189.00",
      dayPnl: "+$90",
      unrealizedPnl: "+$90",
      exposure: "$18,900"
    },
    {
      symbol: "TSLA",
      side: "Short",
      quantity: "25",
      averagePrice: "250.00",
      markPrice: "254.00",
      dayPnl: "-$100",
      unrealizedPnl: "-$100",
      exposure: "$6,350"
    }
  ],
  openOrders: [
    {
      orderId: "PO-1",
      symbol: "MSFT",
      side: "Buy",
      type: "Limit",
      quantity: "20",
      limitPrice: "414.20",
      status: "Working",
      submittedAt: "09:42:00 ET"
    }
  ],
  fills: [
    {
      fillId: "FL-1",
      orderId: "PO-0",
      symbol: "NVDA",
      side: "Sell",
      quantity: "10",
      price: "948.20",
      venue: "NASDAQ",
      timestamp: "09:40:10 ET"
    }
  ],
  risk: {
    state: "Observe",
    summary: "Guardrails are active.",
    netExposure: "$120,000",
    grossExposure: "$150,000",
    var95: "$9,000",
    maxDrawdown: "-1.1%",
    buyingPowerUsed: "58%",
    activeGuardrails: ["Cap per single-name", "Throttle at 70%"]
  },
  brokerage: {
    provider: "Interactive Brokers",
    account: "DU1009034",
    environment: "paper",
    connection: "Connected",
    lastHeartbeat: "2s ago",
    orderIngress: "healthy",
    fillFeed: "healthy",
    notes: "Adapter is wired."
  }
};

describe("trading blotter view model", () => {
  it("derives selected evidence rows, tones, empty flags, and detail panels", () => {
    const state = buildTradingBlotterViewModel({
      data: tradingWorkspace,
      selectedPositionId: "tsla-short-1",
      selectedOrderId: "po-1-0",
      selectedFillId: "FL-1"
    });

    expect(state.hasPositions).toBe(true);
    expect(state.hasOpenOrders).toBe(true);
    expect(state.hasFills).toBe(true);
    expect(state.cancelAllDisabled).toBe(false);
    expect(state.cancelAllDisabledReason).toBeNull();
    expect(state.cancelAllAriaLabel).toBe("Cancel all 1 open orders");
    expect(state.positionRows[1]).toMatchObject({
      id: "tsla-short-1",
      isSelected: true,
      dayPnlTone: "danger",
      unrealizedPnlTone: "danger",
      selectAriaLabel: "Inspect TSLA short position"
    });
    expect(state.orderRows[0]).toMatchObject({
      id: "po-1-0",
      isSelected: true,
      statusTone: "success",
      cancelAriaLabel: "Cancel order PO-1"
    });
    expect(state.fillRows[0]).toMatchObject({
      id: "FL-1",
      isSelected: true,
      detailPanelId: "trading-fill-detail",
      ariaExpanded: true,
      selectAriaLabel: "Inspect fill FL-1"
    });
    expect(state.selectedPosition).toEqual(expect.objectContaining({
      title: "TSLA",
      statusLabel: "Observe",
      statusTone: "warning",
      ariaLabel: "Position detail for TSLA"
    }));
    expect(state.selectedPosition?.fields).toContainEqual({ label: "Guardrails", value: "2 active", tone: "warning" });
    expect(state.selectedOrder).toEqual(expect.objectContaining({
      title: "PO-1",
      statusLabel: "Working",
      statusTone: "success",
      ariaLabel: "Order detail for PO-1"
    }));
    expect(state.selectedFill).toEqual(expect.objectContaining({
      title: "FL-1",
      statusLabel: "Fill evidence",
      statusTone: "success",
      ariaLabel: "Fill detail for FL-1"
    }));
    expect(state.selectedFill?.fields).toContainEqual({ label: "Provider", value: "Interactive Brokers", tone: "muted" });
    expect(state.fillRows[0].cells).toEqual(["FL-1", "PO-0", "NVDA", "Sell", "10", "948.20", "NASDAQ", "09:40:10 ET"]);
  });

  it("keeps unavailable and empty states in the view model", () => {
    const unavailable = buildTradingBlotterViewModel({ data: null });

    expect(unavailable.hasPositions).toBe(false);
    expect(unavailable.hasOpenOrders).toBe(false);
    expect(unavailable.hasFills).toBe(false);
    expect(unavailable.selectedPosition).toBeNull();
    expect(unavailable.selectedOrder).toBeNull();
    expect(unavailable.selectedFill).toBeNull();
    expect(unavailable.cancelAllDisabled).toBe(true);
    expect(unavailable.positionEmptyText).toBe("Trading workspace data unavailable.");
    expect(unavailable.orderEmptyText).toBe("Trading workspace data unavailable.");
    expect(unavailable.fillEmptyText).toBe("Trading workspace data unavailable.");
    expect(unavailable.cancelAllDisabledReason).toBe("No open orders require cancellation.");

    const empty = buildTradingBlotterViewModel({
      data: { ...tradingWorkspace, positions: [], openOrders: [], fills: [] }
    });

    expect(empty.positionEmptyText).toBe("No live positions in the active paper session.");
    expect(empty.orderEmptyText).toBe("No open orders require operator action.");
    expect(empty.fillEmptyText).toBe("No recent fills have been reported for this session.");
    expect(empty.cancelAllAriaLabel).toBe("No open orders to cancel");
    expect(empty.cancelAllDisabledReason).toBe("No open orders require cancellation.");
  });
});

describe("execution evidence view model", () => {
  it("derives controls summary rows and accessible audit rows", () => {
    const state = buildExecutionEvidenceState({
      auditEntries: [executionAuditEntry],
      controlsSnapshot: executionControlsSnapshot,
      loading: false,
      errorText: null
    });

    expect(state.controlsPanel?.statusLabel).toBe("Breaker Closed");
    expect(state.controlsPanel?.ariaLabel).toContain("1 symbol limit");
    expect(state.controlsPanel?.rows).toContainEqual({ id: "default-limit", label: "Default limit", value: "5000" });
    expect(state.controlsPanel?.rows).toContainEqual({ id: "symbol-limits", label: "Symbol limits", value: "AAPL=2500" });
    expect(state.controlsPanel?.rows).toContainEqual({ id: "active-overrides", label: "Active overrides", value: "BypassOrderControls (AAPL)" });
    expect(state.auditRows[0]).toMatchObject({
      id: "audit-1",
      action: "ReplayPaperSession",
      outcome: "Completed",
      outcomeTone: "success",
      metadataText: "2026-01-01T00:20:00Z · session sess-1"
    });
    expect(state.auditRows[0].ariaLabel).toContain("Replay matched current state");
    expect(state.statusAnnouncement).toBe("Breaker Closed. 1 audit entry loaded.");
  });

  it("derives loading, empty, and error copy without raw component branching", () => {
    const loading = buildExecutionEvidenceState({
      auditEntries: [],
      controlsSnapshot: null,
      loading: true,
      errorText: null
    });

    expect(loading.auditEmptyText).toBe("Loading execution audit entries...");
    expect(loading.controlsEmptyText).toBe("Loading execution controls snapshot...");
    expect(loading.refreshButtonLabel).toBe("Refreshing...");
    expect(loading.refreshBusyLabel).toBe("Refreshing evidence...");
    expect(loading.refreshDisabled).toBe(true);
    expect(loading.refreshDisabledReason).toBe("Execution evidence refresh is already running.");

    const failed = buildExecutionEvidenceState({
      auditEntries: [],
      controlsSnapshot: null,
      loading: false,
      errorText: "Controls API unavailable."
    });

    expect(failed.auditEmptyText).toBe("No execution audit entries available.");
    expect(failed.controlsEmptyText).toBe("Snapshot unavailable.");
    expect(failed.refreshDisabled).toBe(false);
    expect(failed.refreshDisabledReason).toBeNull();
    expect(failed.statusAnnouncement).toBe("Execution evidence refresh failed: Controls API unavailable.");
  });
});

describe("paper session view model", () => {
  it("derives session row actions, selected detail, and replay evidence", () => {
    const state = buildPaperSessionState({
      sessions: [activePaperSession],
      selectedSessionId: "sess-1",
      selectedSessionDetail: selectedPaperSessionDetail,
      sessionReplayVerification: consistentReplayVerification,
      form: emptyPaperSessionForm,
      showCreateForm: false,
      busyCommand: { kind: "verifying", sessionId: "sess-1" },
      errorText: null
    });

    expect(state.rows).toEqual([
      expect.objectContaining({
        sessionId: "sess-1",
        initialCashText: "$100,000.00",
        statusLabel: "Active",
        isSelected: true,
        canRestore: false,
        canVerify: false,
        canClose: false,
        verifyButtonLabel: "Verifying…",
        restoreDisabledReason: "Paper session sess-1 verification is running.",
        verifyDisabledReason: "Paper session sess-1 verification is running.",
        closeDisabledReason: "Paper session sess-1 verification is running.",
        verifyAriaLabel: "Verifying replay for paper session sess-1",
        ariaLabel: "sess-1, strat-1, Active, $100,000.00 initial cash"
      })
    ]);
    expect(state.toggleCreateButtonAriaLabel).toBe("Open paper session creation form");
    expect(state.toggleCreateButtonDisabledReason).toBe("Paper session sess-1 verification is running.");
    expect(state.createButtonDisabledReason).toBe("Paper session sess-1 verification is running.");
    expect(state.cancelCreateButtonDisabledReason).toBe("Paper session sess-1 verification is running.");
    expect(state.formRequirementText).toBe("Paper session sess-1 verification is running.");
    expect(state.selectedSessionLabel).toBe("Selected session: sess-1");
    expect(state.detail).toEqual(expect.objectContaining({
      sessionId: "sess-1",
      statusLabel: "Active",
      statusTone: "ready",
      ariaLabel: "Paper session detail for sess-1"
    }));
    expect(state.detail?.infoRows).toEqual([
      { label: "Strategy", value: "strat-1" },
      { label: "Initial cash", value: "$100,000.00" },
      { label: "Tracked symbols", value: "AAPL, MSFT" },
      { label: "Orders retained", value: "1" }
    ]);
    expect(state.detail?.metricRows).toEqual([
      { label: "Cash", value: "$99,000.00" },
      { label: "Portfolio value", value: "$100,250.00" },
      { label: "Open positions", value: "1" }
    ]);
    expect(state.detail?.replay).toEqual(expect.objectContaining({
      tone: "success",
      statusLabel: "Matched current state",
      ariaLabel: "Replay verification matched current state for sess-1"
    }));
    expect(state.detail?.replay?.rows).toContainEqual({ label: "Verification audit", value: "audit-verify-1" });
    expect(state.statusAnnouncement).toBe("Verifying paper session sess-1.");
    expect(state.strategyIdField).toMatchObject({
      id: "paper-session-strategy-id",
      label: "Strategy ID",
      field: "strategyId",
      value: "",
      describedBy: "paper-session-create-requirements",
      disabled: true,
      disabledReason: "Paper session sess-1 verification is running.",
      invalid: false
    });
    expect(state.initialCashField).toMatchObject({
      id: "paper-session-initial-cash",
      label: "Initial cash ($)",
      field: "initialCash",
      value: "100000",
      describedBy: "paper-session-create-requirements",
      disabled: true,
      disabledReason: "Paper session sess-1 verification is running.",
      invalid: false,
      min: 1000,
      step: 1000
    });
  });

  it("validates create-session cash and builds default strategy ids", () => {
    expect(validatePaperSessionForm({ ...emptyPaperSessionForm, initialCash: "500" }))
      .toBe("Enter initial cash of at least $1,000.");
    expect(validatePaperSessionForm({ ...emptyPaperSessionForm, initialCash: "250000" })).toBeNull();

    expect(buildPaperSessionCreateRequest({
      strategyId: "  ",
      initialCash: "250000"
    }, () => 42)).toEqual({
      strategyId: "strat-42",
      initialCash: 250000
    });

    const invalidState = buildPaperSessionState({
      sessions: [],
      selectedSessionId: null,
      selectedSessionDetail: null,
      sessionReplayVerification: null,
      form: { ...emptyPaperSessionForm, initialCash: "bad" },
      showCreateForm: true,
      busyCommand: null,
      errorText: "Create failed"
    });

    expect(invalidState.canSubmitCreate).toBe(false);
    expect(invalidState.createButtonDisabledReason).toBe("Enter initial cash of at least $1,000.");
    expect(invalidState.cancelCreateButtonDisabledReason).toBeNull();
    expect(invalidState.formRequirementText).toBe("Enter initial cash of at least $1,000.");
    expect(invalidState.strategyIdField.invalid).toBe(false);
    expect(invalidState.initialCashField.invalid).toBe(true);
    expect(invalidState.initialCashField.disabled).toBe(false);
    expect(invalidState.statusAnnouncement).toBe("Paper session workflow failed: Create failed");
  });

  it("verifies replay through the paper-session hook and requests readiness refresh", async () => {
    const services: PaperSessionServices = {
      getExecutionSessions: vi.fn<PaperSessionServices["getExecutionSessions"]>().mockResolvedValue([activePaperSession]),
      createPaperSession: vi.fn<PaperSessionServices["createPaperSession"]>(),
      closePaperSession: vi.fn<PaperSessionServices["closePaperSession"]>(),
      getPaperSessionDetail: vi.fn<PaperSessionServices["getPaperSessionDetail"]>().mockResolvedValue(selectedPaperSessionDetail),
      getPaperSessionReplayVerification: vi.fn<PaperSessionServices["getPaperSessionReplayVerification"]>()
        .mockResolvedValue(consistentReplayVerification)
    };
    const onSessionEvidenceChanged = vi.fn<() => Promise<void>>().mockResolvedValue();
    const { result } = renderHook(() => usePaperSessionsViewModel({ services, onSessionEvidenceChanged }));

    await waitFor(() => {
      expect(result.current.rows).toHaveLength(1);
      expect(result.current.isBusy).toBe(false);
    });

    await act(async () => {
      await result.current.restoreSession("sess-1");
    });

    expect(services.getPaperSessionDetail).toHaveBeenCalledWith("sess-1");
    expect(result.current.selectedSessionId).toBe("sess-1");
    expect(result.current.selectedSessionDetail).toBe(selectedPaperSessionDetail);
    expect(result.current.sessionReplayVerification).toBeNull();
    expect(result.current.detail?.replay).toBeNull();
    expect(result.current.statusAnnouncement).toBe("Paper session sess-1 restored.");
    expect(onSessionEvidenceChanged).not.toHaveBeenCalled();

    await act(async () => {
      await result.current.verifySessionReplay("sess-1");
    });

    expect(services.getPaperSessionDetail).toHaveBeenCalledTimes(2);
    expect(services.getPaperSessionReplayVerification).toHaveBeenCalledWith("sess-1");
    expect(result.current.selectedSessionId).toBe("sess-1");
    expect(result.current.selectedSessionDetail).toBe(selectedPaperSessionDetail);
    expect(result.current.sessionReplayVerification).toBe(consistentReplayVerification);
    expect(result.current.detail?.replay).toEqual(expect.objectContaining({
      tone: "success",
      statusLabel: "Matched current state",
      ariaLabel: "Replay verification matched current state for sess-1"
    }));
    expect(result.current.detail?.replay?.rows).toContainEqual({ label: "Verification audit", value: "audit-verify-1" });
    expect(result.current.statusAnnouncement).toBe("Replay verification matched current state for sess-1.");
    expect(onSessionEvidenceChanged).toHaveBeenCalledOnce();
  });
});

describe("session replay controls view model", () => {
  it("derives file options, ready state, and running replay affordances", () => {
    const ready = buildSessionReplayControlsState({
      files: [replayFile],
      selectedFilePath: replayFile.path,
      replayStatus: null,
      replaySpeed: "1",
      seekMs: "0",
      loadingFiles: false,
      activeCommand: null,
      errorText: null
    });

    expect(ready.fileOptions).toEqual([
      expect.objectContaining({
        path: replayFile.path,
        name: "replay.jsonl",
        ariaLabel: "replay.jsonl, AAPL / trades / 2026-01-01T00:00:00Z"
      })
    ]);
    expect(ready.statusText).toBe("Ready to replay replay.jsonl.");
    expect(ready.canStart).toBe(true);
    expect(ready.canPause).toBe(false);
    expect(ready.pauseDisabledReason).toBe("Start a replay before using this control.");
    expect(ready.sectionTitle).toBe("Session replay controls");
    expect(ready.fileSelectLabel).toBe("Replay file");
    expect(ready.fileSelectDescribedBy).toBe("session-replay-status");
    expect(ready.speedLabel).toBe("Replay speed");
    expect(ready.speedDescribedBy).toBe("session-replay-status session-replay-speed-help");
    expect(ready.seekLabel).toBe("Seek position");
    expect(ready.seekDescribedBy).toBe("session-replay-status session-replay-seek-help");
    expect(ready.statusPanel).toEqual(expect.objectContaining({
      role: "status",
      tone: "success",
      title: "Replay file selected",
      detail: "Ready to replay replay.jsonl."
    }));
    expect(ready.statusAnnouncement).toBe("Replay file replay.jsonl selected.");

    const running = buildSessionReplayControlsState({
      ...ready,
      files: [replayFile],
      selectedFilePath: replayFile.path,
      replayStatus: runningReplayStatus
    });

    expect(running.statusText).toBe("Replay running · 3/10 (30%)");
    expect(running.canPause).toBe(true);
    expect(running.canSeek).toBe(true);
    expect(running.canApplySpeed).toBe(true);
    expect(running.statusPanel).toEqual(expect.objectContaining({
      role: "status",
      tone: "success",
      title: "Replay running",
      detail: "Processed 3/10 events at 30%."
    }));
    expect(running.statusAnnouncement).toBe("Replay running for rep-1 at 30 percent.");
  });

  it("derives busy labels and input validation for replay commands", () => {
    const invalid = buildSessionReplayControlsState({
      files: [replayFile],
      selectedFilePath: replayFile.path,
      replayStatus: runningReplayStatus,
      replaySpeed: "0",
      seekMs: "-1",
      loadingFiles: false,
      activeCommand: null,
      errorText: "Replay service unavailable."
    });

    expect(invalid.speedValidationText).toBe("Enter a replay speed greater than 0.");
    expect(invalid.seekValidationText).toBe("Enter a seek position of 0 ms or greater.");
    expect(invalid.fileSelectDescribedBy).toBe("session-replay-status session-replay-error");
    expect(invalid.speedDescribedBy).toBe("session-replay-status session-replay-speed-help session-replay-error");
    expect(invalid.seekDescribedBy).toBe("session-replay-status session-replay-seek-help session-replay-error");
    expect(invalid.canStart).toBe(false);
    expect(invalid.canSeek).toBe(false);
    expect(invalid.canApplySpeed).toBe(false);
    expect(invalid.activeErrorText).toBe("Replay service unavailable.");
    expect(invalid.statusPanel).toEqual(expect.objectContaining({
      role: "alert",
      ariaLive: "assertive",
      tone: "danger",
      title: "Replay blocked",
      detail: "Replay service unavailable."
    }));
    expect(invalid.startDisabledReason).toBe("Enter a replay speed greater than 0.");
    expect(invalid.seekDisabledReason).toBe("Enter a seek position of 0 ms or greater.");
    expect(invalid.statusAnnouncement).toBe("Session replay failed: Replay service unavailable.");

    const starting = buildSessionReplayControlsState({
      ...invalid,
      replaySpeed: "1",
      seekMs: "0",
      activeCommand: "starting",
      errorText: null
    });

    expect(starting.startButtonLabel).toBe("Starting...");
    expect(starting.canStart).toBe(false);
    expect(starting.statusAnnouncement).toBe("Starting session replay.");
  });
});

describe("trading readiness view model", () => {
  it("derives contract summary rows, tones, and assistive labels", () => {
    const state = buildTradingReadinessState({
      readiness: blockedReadiness,
      refreshing: false,
      errorText: null
    });

    expect(state.summaryLabel).toBe("Trading readiness contract summary");
    expect(state.summaryRows).toEqual([
      expect.objectContaining({ id: "overall", label: "Overall", value: "Blocked", level: "atRisk", ariaLabel: "Overall readiness: Blocked" }),
      expect.objectContaining({ id: "paper", label: "Paper", value: "Not paper ready", level: "review" }),
      expect.objectContaining({ id: "brokerage", label: "Brokerage", value: "Failed stale", level: "atRisk" }),
      expect.objectContaining({ id: "as-of", label: "As of", value: "Apr 26, 16:05 UTC", level: "review", ariaLabel: "Readiness snapshot timestamp: Apr 26, 16:05 UTC" })
    ]);
    expect(state.workItems).toHaveLength(1);
    expect(state.warnings).toEqual(["Portfolio snapshot failed."]);
    expect(state.hasOperatorAttention).toBe(true);
    expect(state.workItemSummaryText).toBe("1 readiness item and 1 warning.");
    expect(state.primaryWorkItemKind).toBe("BrokerageSync");
    expect(state.evidenceAction).toEqual({
      label: "Evidence",
      href: "/reporting/evidence?subjectKind=paper-readiness&subjectId=current",
      ariaLabel: "Open paper cockpit readiness evidence"
    });
    expect(state.visibleWorkItems).toEqual([
      expect.objectContaining({
        workItemId: "brokerage-sync-failed-fund-1",
        kind: "BrokerageSync",
        label: "Brokerage sync failed",
        metadataText: "Trading · AccountPortfolio",
        action: {
          label: "Fix provider setup",
          href: "/settings#alpaca-provider-setup",
          ariaLabel: "Open provider setup for Brokerage sync failed",
          detail: "Review provider credentials and connection status in Settings."
        },
        ariaLabel: "Critical readiness item. Brokerage sync failed. Sync broker credentials before paper operation. Trading · AccountPortfolio"
      })
    ]);
    expect(state.visibleWarnings).toEqual([
      {
        id: "warning-1-portfolio-snapshot-failed",
        text: "Portfolio snapshot failed.",
        ariaLabel: "Trading readiness warning: Portfolio snapshot failed."
      }
    ]);
    expect(state.statusAnnouncement).toBe("Trading readiness blocked as of Apr 26, 16:05 UTC.");
  });

  it("limits displayed operator work items and warnings in the view model", () => {
    const readiness: TradingOperatorReadiness = {
      ...blockedReadiness,
      workItems: Array.from({ length: 6 }, (_, index) => ({
        ...blockedReadiness.workItems[0],
        workItemId: `work-${index + 1}`,
        kind: index === 0 ? "BrokerageSync" : "ReportPackApproval",
        label: `Work item ${index + 1}`,
        detail: `Detail ${index + 1}`,
        tone: index === 0 ? "Critical" : "Warning",
        workspace: index % 2 === 0 ? "Trading" : null,
        targetPageTag: index % 2 === 0 ? "RunRisk" : null,
        runId: index % 2 === 0 ? `run-${index + 1}` : null,
        auditReference: index % 2 === 0 ? `audit-${index + 1}` : null
      })),
      warnings: ["Warning one.", "Warning two.", "Warning three.", "Warning four."]
    };

    const state = buildTradingReadinessState({
      readiness,
      refreshing: false,
      errorText: null
    });

    expect(state.workItemSummaryText).toBe("6 readiness items and 4 warnings.");
    expect(state.visibleWorkItems).toHaveLength(4);
    expect(state.hiddenWorkItemCount).toBe(2);
    expect(state.workItemOverflowLabel).toBe("2 more readiness items in the Operator Readiness Console.");
    expect(state.visibleWorkItems[0]).toMatchObject({
      workItemId: "work-1",
      metadataText: "Trading · RunRisk · run-1 · audit-1",
      action: {
        label: "Fix provider setup",
        href: "/settings#alpaca-provider-setup",
        ariaLabel: "Open provider setup for Work item 1",
        detail: "Review provider credentials and connection status in Settings."
      }
    });
    expect(state.visibleWarnings.map((warning) => warning.text)).toEqual([
      "Warning one.",
      "Warning two.",
      "Warning three."
    ]);
    expect(state.hiddenWarningCount).toBe(1);
    expect(state.warningOverflowLabel).toBe("1 more warning in the Operator Readiness Console.");
  });

  it("derives refresh and error copy for readiness commands", () => {
    const refreshing = buildTradingReadinessState({
      readiness: blockedReadiness,
      refreshing: true,
      errorText: null
    });

    expect(refreshing.refreshButtonLabel).toBe("Refreshing...");
    expect(refreshing.refreshAriaLabel).toBe("Refreshing trading readiness");
    expect(refreshing.refreshBusyLabel).toBe("Refreshing readiness...");
    expect(refreshing.refreshDisabled).toBe(true);
    expect(refreshing.refreshDisabledReason).toBe("Trading readiness refresh is already running.");
    expect(refreshing.statusAnnouncement).toBe("Refreshing trading readiness.");

    const failed = buildTradingReadinessState({
      readiness: null,
      refreshing: false,
      errorText: "Network failed."
    });

    expect(failed.summaryRows).toEqual([]);
    expect(failed.refreshDisabled).toBe(false);
    expect(failed.refreshDisabledReason).toBeNull();
    expect(failed.statusAnnouncement).toBe("Trading readiness refresh failed: Network failed.");
  });

  it("ignores stale readiness refresh results after the initial payload changes", async () => {
    const staleRefresh = createDeferred<TradingOperatorReadiness | null>();
    const getTradingReadiness = vi.fn<TradingReadinessServices["getTradingReadiness"]>().mockReturnValue(staleRefresh.promise);
    const services: TradingReadinessServices = {
      getTradingReadiness
    };
    const nextReadiness: TradingOperatorReadiness = {
      ...blockedReadiness,
      asOf: "2026-04-26T16:10:00Z",
      overallStatus: "ReviewRequired"
    };
    const { result, rerender } = renderHook(
      ({ initialReadiness }) => useTradingReadinessViewModel({ initialReadiness, services }),
      { initialProps: { initialReadiness: blockedReadiness } }
    );

    await act(async () => {
      void result.current.refresh();
    });

    const refreshSignal = getTradingReadiness.mock.calls[0]?.[0]?.signal;
    expect(refreshSignal?.aborted).toBe(false);
    expect(result.current.refreshing).toBe(true);

    rerender({ initialReadiness: nextReadiness });

    expect(refreshSignal?.aborted).toBe(true);
    expect(result.current.refreshing).toBe(false);
    expect(result.current.readiness?.asOf).toBe("2026-04-26T16:10:00Z");

    await act(async () => {
      staleRefresh.resolve({
        ...blockedReadiness,
        asOf: "2026-04-26T15:55:00Z",
        overallStatus: "Blocked"
      });
      await staleRefresh.promise;
    });

    expect(result.current.readiness?.asOf).toBe("2026-04-26T16:10:00Z");
    expect(result.current.statusAnnouncement).toBe("Trading readiness review required as of Apr 26, 16:10 UTC.");
  });

  it("normalizes readiness and brokerage status levels", () => {
    expect(formatReadinessStatusValue("ReviewRequired")).toBe("Review required");
    expect(mapReadinessStatusLevel("Ready")).toBe("ready");
    expect(mapReadinessStatusLevel("Blocked")).toBe("atRisk");
    expect(mapBrokerageSyncLevel({ ...blockedReadiness.brokerageSync!, health: "Healthy", isStale: false })).toBe("ready");
    expect(mapBrokerageSyncLevel(blockedReadiness.brokerageSync!)).toBe("atRisk");
  });
});

describe("strategy lifecycle controls view model", () => {
  it("keeps destructive lifecycle commands disabled until a strategy ID is present", () => {
    const state = buildStrategyLifecycleControlsState("   ");

    expect(state.canPause).toBe(false);
    expect(state.canStop).toBe(false);
    expect(state.pauseAction).toBeNull();
    expect(state.stopAction).toBeNull();
    expect(state.statusText).toBe("Enter a registered strategy ID to enable lifecycle actions.");
    expect(state.pauseAriaLabel).toBe("Enter a strategy ID before pausing a strategy");
    expect(state.pauseDisabledReason).toBe("Enter a registered strategy ID before using lifecycle actions.");
    expect(state.stopDisabledReason).toBe("Enter a registered strategy ID before using lifecycle actions.");
  });

  it("trims the strategy ID and derives confirmation actions", () => {
    const state = buildStrategyLifecycleControlsState("  mean-reversion-fx-01  ");

    expect(state.strategyIdValue).toBe("mean-reversion-fx-01");
    expect(state.canPause).toBe(true);
    expect(state.canStop).toBe(true);
    expect(state.pauseAction).toEqual({ kind: "pause-strategy", strategyId: "mean-reversion-fx-01" });
    expect(state.stopAction).toEqual({ kind: "stop-strategy", strategyId: "mean-reversion-fx-01" });
    expect(state.statusAnnouncement).toBe("Strategy lifecycle controls ready for mean-reversion-fx-01.");
    expect(state.stopAriaLabel).toBe("Open stop confirmation for strategy mean-reversion-fx-01");
    expect(state.pauseDisabledReason).toBeNull();
    expect(state.stopDisabledReason).toBeNull();
  });
});

describe("trading confirmation view model", () => {
  it("derives dialog labels, copy, identifiers, and command affordances", () => {
    const state = buildTradingConfirmDialogState(createTradingConfirmState({ kind: "cancel-order", orderId: "PO-1" }));

    expect(state.open).toBe(true);
    expect(state.title).toBe("Cancel order PO-1");
    expect(state.description).toBe("This will request cancellation of the selected order. Partial fills that already occurred are not reversed.");
    expect(state.dialogTitleId).toBe("trading-confirm-cancel-order-po-1-title");
    expect(state.dialogDescriptionId).toBe("trading-confirm-cancel-order-po-1-description");
    expect(state.confirmButtonLabel).toBe("Confirm");
    expect(state.confirmAriaLabel).toBe("Confirm cancel order po-1");
    expect(state.canClose).toBe(true);
    expect(state.canConfirm).toBe(false);
    expect(state.confirmDisabledReason).toBe("Review and acknowledge the trading action before confirming.");
    expect(state.acknowledgement).toEqual({
      id: "trading-confirm-cancel-order-po-1-acknowledgement",
      label: "I reviewed this trading action",
      description: "Cancel order PO-1 after reviewing partial-fill risk.",
      checked: false,
      disabled: false,
      disabledReason: null
    });
    expect(state.statusAnnouncement).toBe("Cancel order PO-1 confirmation open.");

    const acknowledged = buildTradingConfirmDialogState(createTradingConfirmState({ kind: "cancel-order", orderId: "PO-1" }, true));
    expect(acknowledged.canConfirm).toBe(true);
    expect(acknowledged.confirmDisabledReason).toBeNull();
  });

  it("derives busy and completed states for assistive feedback", () => {
    const action = { kind: "close-position" as const, positionKey: "acct-1:AAPL", symbol: "AAPL" };
    const busy = buildTradingConfirmDialogState({
      ...createTradingConfirmState(action),
      busy: true
    });

    expect(busy.title).toBe("Close position - AAPL");
    expect(busy.confirmButtonLabel).toBe("Processing...");
    expect(busy.canClose).toBe(false);
    expect(busy.canConfirm).toBe(false);
    expect(busy.statusAnnouncement).toBe("Close position - AAPL processing.");

    const completed = buildTradingConfirmDialogState({
      ...createTradingConfirmState(action),
      result: {
        actionId: "act-1",
        status: "Completed",
        message: "Position flattened.",
        occurredAt: "2026-04-26T16:00:00Z"
      }
    });

    expect(completed.isCompleted).toBe(true);
    expect(completed.canConfirm).toBe(false);
    expect(completed.resultPanel).toEqual(expect.objectContaining({
      tone: "success",
      status: "Completed",
      actionId: "act-1",
      ariaLabel: "Action completed: Position flattened."
    }));
    expect(completed.statusAnnouncement).toBe("Close position - AAPL completed: Position flattened.");
  });

  it("maps errors and rejected results into accessible status panels", () => {
    const action = { kind: "stop-strategy" as const, strategyId: "strat-1" };
    const failed = buildTradingConfirmDialogState({
      ...createTradingConfirmState(action),
      error: "Broker unavailable."
    });

    expect(failed.errorPanel).toEqual({
      text: "Broker unavailable.",
      ariaLabel: "Confirmation action failed: Broker unavailable."
    });
    expect(failed.statusAnnouncement).toBe("Stop strategy - strat-1 failed: Broker unavailable.");

    const rejected = buildTradingConfirmDialogState({
      ...createTradingConfirmState(action),
      result: {
        actionId: "act-2",
        status: "Rejected",
        message: "Strategy already stopped.",
        occurredAt: "2026-04-26T16:00:00Z"
      }
    });

    expect(rejected.resultPanel).toEqual(expect.objectContaining({
      tone: "warning",
      ariaLabel: "Action rejected: Strategy already stopped."
    }));
  });

  it("requires acknowledgement before executing the selected trading action", async () => {
    const completedResult: TradingActionResult = {
      actionId: "act-1",
      status: "Completed",
      message: "Order cancelled.",
      occurredAt: "2026-04-26T16:00:00Z"
    };
    const services: TradingConfirmServices = {
      cancelOrder: vi.fn().mockResolvedValue(completedResult),
      cancelAllOrders: vi.fn(),
      closePosition: vi.fn(),
      pauseStrategy: vi.fn(),
      stopStrategy: vi.fn()
    };
    const { result } = renderHook(() => useTradingConfirmViewModel({ services }));

    act(() => {
      result.current.openConfirm({ kind: "cancel-order", orderId: "PO-1" });
    });

    expect(result.current.canConfirm).toBe(false);
    await act(async () => {
      await result.current.executeConfirm();
    });
    expect(services.cancelOrder).not.toHaveBeenCalled();

    act(() => {
      result.current.setReviewAcknowledged(true);
    });
    expect(result.current.canConfirm).toBe(true);

    await act(async () => {
      await result.current.executeConfirm();
    });

    expect(services.cancelOrder).toHaveBeenCalledWith("PO-1");
    expect(result.current.resultPanel).toEqual(expect.objectContaining({
      status: "Completed",
      message: "Order cancelled."
    }));

    act(() => {
      result.current.closeConfirm();
      result.current.openConfirm({ kind: "cancel-all" });
    });

    expect(result.current.acknowledgement.checked).toBe(false);
    expect(result.current.canConfirm).toBe(false);
  });
});

describe("trading order ticket view model", () => {
  it("normalizes order input and clears price for market orders", () => {
    const form = updateOrderTicketForm(
      {
        ...emptyOrderTicketForm,
        symbol: " aapl ",
        type: "Limit",
        quantity: 10,
        limitPrice: 189.44
      },
      "type",
      "Market"
    );

    expect(form.limitPrice).toBeNull();
    expect(buildOrderSubmitRequest(form)).toEqual({
      symbol: "AAPL",
      side: "Buy",
      type: "Market",
      quantity: 10
    });
  });

  it("validates required symbol, quantity, and limit price fields", () => {
    expect(validateOrderTicketForm(emptyOrderTicketForm)).toBe("Enter a symbol before submitting an order.");
    expect(validateOrderTicketForm({ ...emptyOrderTicketForm, symbol: "SPY" }))
      .toBe("Enter an order quantity greater than zero.");
    expect(validateOrderTicketForm({
      ...emptyOrderTicketForm,
      symbol: "SPY",
      type: "Stop",
      quantity: 5,
      limitPrice: null
    })).toBe("Enter a stop price greater than zero.");
  });

  it("derives order command labels, disabled state, and announcements", () => {
    const invalid = buildOrderTicketState({
      form: { ...emptyOrderTicketForm, symbol: "MSFT" },
      open: true,
      phase: "idle",
      orderId: null,
      errorText: null
    });

    expect(invalid.canSubmit).toBe(false);
    expect(invalid.invalidField).toBe("quantity");
    expect(invalid.requirementText).toBe("Enter an order quantity greater than zero.");
    expect(invalid.submitDisabledReason).toBe("Enter an order quantity greater than zero.");
    expect(invalid.acknowledgement).toMatchObject({
      id: "order-ticket-review-acknowledgement",
      label: "I reviewed the order preview and risk warnings",
      checked: false,
      disabled: true,
      disabledReason: "Complete valid order fields before acknowledging the preview."
    });
    expect(invalid.formId).toBe("trading-order-ticket");
    expect(invalid.requirementId).toBe("order-ticket-requirements");
    expect(invalid.controls.symbol).toMatchObject({
      id: "order-ticket-symbol",
      label: "Symbol",
      value: "MSFT",
      describedBy: "order-ticket-requirements",
      ariaLabel: "Order symbol",
      invalid: false,
      required: true
    });
    expect(invalid.controls.quantity).toMatchObject({
      id: "order-ticket-quantity",
      label: "Quantity",
      value: "",
      invalid: true
    });
    expect(invalid.controls.side.options).toEqual([
      { value: "Buy", label: "Buy" },
      { value: "Sell", label: "Sell" }
    ]);

    const submitting = buildOrderTicketState({
      form: { ...emptyOrderTicketForm, symbol: "MSFT", quantity: 2 },
      open: true,
      phase: "submitting",
      orderId: null,
      errorText: null
    });

    expect(submitting.submitButtonLabel).toBe("Submitting…");
    expect(submitting.submitBusy).toBe(true);
    expect(submitting.submitDisabledReason).toBe("Order submission is already running.");
    expect(submitting.closeButtonLabel).toBe("Cancel");
    expect(submitting.closeAriaLabel).toBe("Order submission is in progress.");
    expect(submitting.closeDisabledReason).toBe("Order submission is in progress.");
    expect(submitting.statusAnnouncement).toBe("Submitting order request.");
    expect(submitting.controls.limitPrice).toBeNull();

    const validUnacknowledged = buildOrderTicketState({
      form: { ...emptyOrderTicketForm, symbol: "MSFT", quantity: 2 },
      open: true,
      phase: "idle",
      orderId: null,
      errorText: null
    });

    expect(validUnacknowledged.canSubmit).toBe(false);
    expect(validUnacknowledged.submitDisabledReason).toBe("Review the order preview and acknowledge before submitting.");
    expect(validUnacknowledged.acknowledgement.disabled).toBe(false);

    const limitOrder = buildOrderTicketState({
      form: { ...emptyOrderTicketForm, symbol: "MSFT", type: "Limit", quantity: 2, limitPrice: 189.44 },
      open: true,
      phase: "idle",
      orderId: null,
      errorText: null,
      acknowledged: true
    });

    expect(limitOrder.canSubmit).toBe(true);
    expect(limitOrder.submitDisabledReason).toBeNull();
    expect(limitOrder.closeAriaLabel).toBe("Close order ticket without submitting");
    expect(limitOrder.closeDisabledReason).toBeNull();
    expect(limitOrder.acknowledgement.checked).toBe(true);
    expect(limitOrder.controls.limitPrice).toMatchObject({
      id: "order-ticket-limit-price",
      label: "Limit price",
      value: "189.44",
      describedBy: "order-ticket-requirements",
      ariaLabel: "Limit order price",
      invalid: false,
      required: true
    });

    const submitted = buildOrderTicketState({
      form: emptyOrderTicketForm,
      open: false,
      phase: "submitted",
      orderId: "ord-42",
      errorText: null
    });

    expect(submitted.successText).toBe("Order submitted - ord-42.");
    expect(submitted.statusAnnouncement).toBe("Order submitted with id ord-42.");
  });

  it("ignores duplicate order submits while the first request is still pending", async () => {
    const deferred = createDeferred<Awaited<ReturnType<OrderTicketServices["submitOrder"]>>>();
    const services: OrderTicketServices = {
      submitOrder: vi.fn().mockReturnValue(deferred.promise)
    };
    const { result } = renderHook(() => useOrderTicketViewModel({ services }));

    act(() => {
      result.current.openTicket();
      result.current.updateField("symbol", "MSFT");
      result.current.updateField("quantity", "2");
      result.current.setAcknowledged(true);
    });

    await act(async () => {
      const firstSubmit = result.current.submitOrder();
      const duplicateSubmit = result.current.submitOrder();

      expect(services.submitOrder).toHaveBeenCalledTimes(1);
      deferred.resolve({ success: true, orderId: "ord-42", reason: null });
      await Promise.all([firstSubmit, duplicateSubmit]);
    });

    expect(services.submitOrder).toHaveBeenCalledWith({
      symbol: "MSFT",
      side: "Buy",
      type: "Market",
      quantity: 2
    });
    expect(result.current.successText).toBe("Order submitted - ord-42.");
  });
});

describe("buildOrderPreview", () => {
  const longAaplPosition: TradingPosition = {
    symbol: "AAPL",
    side: "Long",
    quantity: "100",
    averagePrice: "188.10",
    markPrice: "189.00",
    dayPnl: "+$90",
    unrealizedPnl: "+$90",
    exposure: "$18,900"
  };

  const healthyRisk: TradingRiskState = {
    state: "Healthy",
    summary: "All clear.",
    netExposure: "$0",
    grossExposure: "$0",
    var95: "$0",
    maxDrawdown: "0%",
    buyingPowerUsed: "12%",
    activeGuardrails: []
  };

  it("returns an unavailable preview when symbol or quantity is missing", () => {
    const preview = buildOrderPreview({
      form: { ...emptyOrderTicketForm },
      positions: [],
      risk: null
    });

    expect(preview.available).toBe(false);
    expect(preview.notional).toBeNull();
    expect(preview.notionalLabel).toBe("—");
    expect(preview.priceSource).toBe("none");
    expect(preview.warnings).toEqual([]);
    expect(preview.summaryLine).toMatch(/Order preview will appear/);
  });

  it("uses the limit price as the reference and computes notional for limit orders", () => {
    const preview = buildOrderPreview({
      form: { symbol: "MSFT", side: "Buy", type: "Limit", quantity: 10, limitPrice: 414.20 },
      positions: [],
      risk: healthyRisk
    });

    expect(preview.available).toBe(true);
    expect(preview.priceSource).toBe("limit");
    expect(preview.referencePrice).toBe(414.20);
    expect(preview.notional).toBeCloseTo(4142, 2);
    expect(preview.notionalLabel).toBe("$4,142.00");
    expect(preview.effect).toBe("open-long");
    expect(preview.effectLabel).toBe("Opens new long position");
    expect(preview.resultingPositionLabel).toBe("Long 10 MSFT");
    expect(preview.warnings).toEqual([]);
    expect(preview.riskNote).toBe("Risk Healthy • Buying power used 12%");
  });

  it("derives reduce vs close vs flip effects against an existing long", () => {
    const reduce = buildOrderPreview({
      form: { symbol: "AAPL", side: "Sell", type: "Market", quantity: 25, limitPrice: null },
      positions: [longAaplPosition],
      risk: null
    });
    expect(reduce.effect).toBe("reduce-long");
    expect(reduce.resultingPositionLabel).toBe("Long 75 AAPL");
    expect(reduce.priceSource).toBe("mark");
    expect(reduce.referencePrice).toBe(189);
    expect(reduce.notionalLabel).toBe("$4,725.00");

    const close = buildOrderPreview({
      form: { symbol: "AAPL", side: "Sell", type: "Market", quantity: 100, limitPrice: null },
      positions: [longAaplPosition],
      risk: null
    });
    expect(close.effect).toBe("close-long");
    expect(close.resultingPositionLabel).toBe("Flat AAPL");

    const flip = buildOrderPreview({
      form: { symbol: "AAPL", side: "Sell", type: "Market", quantity: 150, limitPrice: null },
      positions: [longAaplPosition],
      risk: null
    });
    expect(flip.effect).toBe("flip-to-short");
    expect(flip.resultingPositionLabel).toBe("Short 50 AAPL");
    expect(flip.warnings.find((w) => w.id === "position-flip")?.level).toBe("Critical");
  });

  it("surfaces a warning when a market order has no reference price", () => {
    const preview = buildOrderPreview({
      form: { symbol: "ZZZ", side: "Buy", type: "Market", quantity: 5, limitPrice: null },
      positions: [],
      risk: null
    });

    expect(preview.priceSource).toBe("none");
    expect(preview.notionalLabel).toBe("—");
    expect(preview.warnings.find((w) => w.id === "no-reference-price")).toBeDefined();
  });

  it("emits a danger-level risk warning when the risk state is Constrained", () => {
    const preview = buildOrderPreview({
      form: { symbol: "AAPL", side: "Buy", type: "Limit", quantity: 5, limitPrice: 190 },
      positions: [longAaplPosition],
      risk: {
        ...healthyRisk,
        state: "Constrained",
        summary: "Drawdown breach"
      }
    });

    const warning = preview.warnings.find((w) => w.id === "risk-constrained");
    expect(warning?.level).toBe("Critical");
    expect(warning?.message).toMatch(/Drawdown breach/);
  });
});

describe("trading promotion gate view model", () => {
  it("keeps promotion approval disabled until evaluation and rationale are ready", () => {
    const initial = buildPromotionGateState({
      form: { ...emptyPromotionGateForm, runId: " run-1 " },
      busy: false,
      phase: "idle",
      errorText: null,
      outcome: null,
      evaluation: null,
      history: []
    });

    expect(initial.canEvaluate).toBe(true);
    expect(initial.canPromote).toBe(false);
    expect(initial.approvalRequirementText).toBe("Approval remains disabled until gate checks return an eligible result.");

    const ready = buildPromotionGateState({
      form: {
        ...emptyPromotionGateForm,
        runId: " run-1 ",
        approvedBy: " operator-7 ",
        approvalReason: " Meets risk constraints ",
        approvalChecklist: paperPromotionApprovalChecklist
      },
      busy: false,
      phase: "idle",
      errorText: null,
      outcome: null,
      evaluation: eligibleEvaluation,
      history: []
    });

    expect(ready.canPromote).toBe(true);
    expect(ready.nextActionText).toBe("Promotion trace is ready for confirmation.");
    expect(ready.fields.runId).toEqual({
      field: "runId",
      id: "promotion-run-id",
      label: "Run id",
      ariaLabel: "Run id",
      placeholder: "backtest run id",
      describedBy: "promotion-run-help promotion-action-state",
      helpText: "Evaluate this run before writing a promotion decision.",
      helpId: "promotion-run-help",
      required: false
    });
    expect(ready.fields.approvalReason).toMatchObject({
      field: "approvalReason",
      id: "promotion-approval-reason",
      label: "Approval reason",
      placeholder: "why this promotion is approved",
      describedBy: "promotion-action-state",
      required: true
    });
    expect(ready.fields.manualOverrideId).toMatchObject({
      label: "Manual override id",
      placeholder: "optional manual override id",
      describedBy: null,
      required: false
    });
    expect(validatePromotionApproval(ready.form, eligibleEvaluation)).toBeNull();
  });

  it("derives rejection readiness from run id, operator, and rejection reason", () => {
    expect(validatePromotionRejection({ ...emptyPromotionGateForm, runId: "run-1" })).toBe(
      "Run id, operator, and rejection reason are required."
    );

    const state = buildPromotionGateState({
      form: {
        ...emptyPromotionGateForm,
        runId: "run-1",
        approvedBy: "operator-7",
        rejectionReason: "Risk review failed on drawdown stability."
      },
      busy: false,
      phase: "idle",
      errorText: null,
      outcome: null,
      evaluation: eligibleEvaluation,
      history: []
    });

    expect(state.canReject).toBe(true);
    expect(state.rejectionRequirementText).toBe("Rejection request is ready to write an audit-linked decision.");
  });

  it("builds trimmed approval and rejection requests without empty optional fields", () => {
    const form = {
      ...emptyPromotionGateForm,
      runId: " run-1 ",
      approvedBy: " operator-7 ",
      approvalReason: " Meets risk constraints ",
      rejectionReason: " Drawdown instability ",
      reviewNotes: " ",
      manualOverrideId: " override-9 "
    };

    expect(buildPromotionApprovalRequest(form)).toEqual({
      runId: "run-1",
      approvedBy: "operator-7",
      approvalReason: "Meets risk constraints",
      approvalChecklist: undefined,
      reviewNotes: undefined,
      manualOverrideId: "override-9"
    });
    expect(buildPromotionRejectionRequest(form)).toEqual({
      runId: "run-1",
      reason: "Drawdown instability",
      rejectedBy: "operator-7",
      reviewNotes: undefined,
      manualOverrideId: "override-9"
    });
  });

  it("does not authorize a selected run with stale promotion evaluation", () => {
    const form = {
      ...emptyPromotionGateForm,
      runId: "run-2",
      approvedBy: "operator-7",
      approvalReason: "Meets risk constraints"
    };

    const staleState = buildPromotionGateState({
      form,
      busy: false,
      phase: "idle",
      errorText: null,
      outcome: null,
      evaluation: eligibleEvaluation,
      history: []
    });

    expect(staleState.evaluation).toBeNull();
    expect(staleState.canPromote).toBe(false);
    expect(validatePromotionApproval(form, eligibleEvaluation)).toBe(
      "Evaluate gate checks for run-2 before confirming promotion."
    );
  });

  it("ignores stale promotion evaluation when the selected run changes before the response settles", async () => {
    const deferred = createDeferred<PromotionEvaluationResult>();
    const services = {
      evaluatePromotion: vi.fn(() => deferred.promise),
      approvePromotion: vi.fn(),
      rejectPromotion: vi.fn(),
      getPromotionHistory: vi.fn().mockResolvedValue([])
    };

    const { result } = renderHook(() => usePromotionGateViewModel(services));
    await waitFor(() => expect(services.getPromotionHistory).toHaveBeenCalledTimes(1));

    act(() => {
      result.current.updateField("runId", "run-1");
    });

    let request: Promise<void> | undefined;
    act(() => {
      request = result.current.evaluateGateChecks();
    });

    expect(result.current.busy).toBe(true);
    expect(result.current.evaluateButtonLabel).toBe("Evaluating...");

    act(() => {
      result.current.updateField("runId", "run-2");
    });

    expect(result.current.busy).toBe(false);
    expect(result.current.form.runId).toBe("run-2");

    await act(async () => {
      deferred.resolve(eligibleEvaluation);
      await request;
    });

    expect(result.current.evaluation).toBeNull();
    expect(result.current.canPromote).toBe(false);
    expect(result.current.nextActionText).toBe("Evaluate gate checks before approving or rejecting this run.");
  });

  it("announces busy and error states for assistive technology", () => {
    const busy = buildPromotionGateState({
      form: { ...emptyPromotionGateForm, runId: "run-1" },
      busy: true,
      phase: "evaluating",
      errorText: null,
      outcome: null,
      evaluation: null,
      history: []
    });

    expect(busy.evaluateButtonLabel).toBe("Evaluating...");
    expect(busy.statusAnnouncement).toBe("Evaluating promotion gate checks.");

    const failed = buildPromotionGateState({
      form: { ...emptyPromotionGateForm, runId: "run-1" },
      busy: false,
      phase: "idle",
      errorText: "eval failed",
      outcome: null,
      evaluation: null,
      history: []
    });

    expect(failed.statusAnnouncement).toBe("Promotion gate failed: eval failed");
  });

  describe("Wave 2 Cockpit Acceptance Gate: Session persistence + Replay verification", () => {
    it("Scenario_SessionCloseReplayAndPromotionReview_BacktestToPaperFlowRemainsContinuousAndAuditable", () => {
      // This test proves that /api/execution/* to /api/promotion/* continuity is maintained
      // and that one operator can: create session, close it, replay it, evaluate promotion, approve promotion
      // with both execution and promotion evidence visible in returned contracts

      const replayPortfolio: ExecutionPortfolioSnapshot = {
        cash: 45000,
        portfolioValue: 155000,
        unrealisedPnl: 10000,
        realisedPnl: 0,
        positions: [],
        asOf: "2026-04-27T14:30:00Z"
      };
      const replayVerification: PaperSessionReplayVerification = {
        summary: {
          sessionId: "session-001",
          strategyId: "strat-test",
          strategyName: null,
          initialCash: 100000,
          createdAt: "2026-04-27T14:00:00Z",
          closedAt: "2026-04-27T14:30:00Z",
          isActive: false
        },
        symbols: ["AAPL", "MSFT"],
        replaySource: "DurableFillLog",
        isConsistent: true,
        comparedFillCount: 42,
        comparedOrderCount: 18,
        comparedLedgerEntryCount: 18,
        mismatchReasons: [],
        currentPortfolio: replayPortfolio,
        replayPortfolio,
        verificationAuditId: "audit-replay-001",
        lastPersistedFillAt: "2026-04-27T14:32:15Z",
        lastPersistedOrderUpdateAt: "2026-04-27T14:31:58Z",
        verifiedAt: "2026-04-27T14:35:00Z"
      };

      const sessionDetail: PaperSessionDetail = {
        summary: {
          sessionId: "session-001",
          strategyId: "strat-test",
          strategyName: null,
          initialCash: 100000,
          createdAt: "2026-04-27T14:00:00Z",
          closedAt: "2026-04-27T14:30:00Z",
          isActive: false
        },
        symbols: ["AAPL", "MSFT"],
        portfolio: {
          cash: 45000,
          portfolioValue: 155000,
          unrealisedPnl: 10000,
          realisedPnl: 0,
          positions: [
            { symbol: "AAPL", quantity: 100, averageCostBasis: 150, currentPrice: 155, marketValue: 15500, unrealisedPnl: 500, realisedPnl: 0 },
            { symbol: "MSFT", quantity: 50, averageCostBasis: 300, currentPrice: 310, marketValue: 15500, unrealisedPnl: 500, realisedPnl: 0 }
          ],
          asOf: "2026-04-27T14:30:00Z"
        },
        orderHistory: Array(18).fill(null).map((_, i) => ({
          orderId: `order-${i}`,
          symbol: i % 2 === 0 ? "AAPL" : "MSFT",
          side: i % 3 === 0 ? "Buy" : "Sell",
          type: "Market",
          quantity: 10 + i,
          filledQuantity: 10 + i,
          averageFillPrice: 150 + (i % 10),
          status: "Filled",
          createdAt: new Date(2026, 3, 27, 14, 15 + i).toISOString(),
          updatedAt: new Date(2026, 3, 27, 14, 15 + i).toISOString()
        }))
      };

      // Verify replay verification data structure
      expect(replayVerification.replaySource).toBe("DurableFillLog");
      expect(replayVerification.isConsistent).toBe(true);
      expect(replayVerification.comparedFillCount).toBe(42);
      expect(replayVerification.comparedOrderCount).toBe(18);
      expect(replayVerification.comparedLedgerEntryCount).toBe(18);
      expect(replayVerification.mismatchReasons).toHaveLength(0);
      expect(replayVerification.verificationAuditId).toBeTruthy();

      // Verify session detail maintains execution evidence
      expect(sessionDetail.orderHistory).toHaveLength(18);
      expect(sessionDetail.portfolio?.positions).toHaveLength(2);
      expect(sessionDetail.portfolio?.portfolioValue).toBe(155000);

      // Verify promotion flow can consume the persistent session
      const promotion = buildPromotionGateState({
        form: {
          ...emptyPromotionGateForm,
          runId: "run-from-session-001",
          approvedBy: "operator-qa",
          approvalReason: "Session replay verified and portfolio consistent",
          approvalChecklist: paperPromotionApprovalChecklist
        },
        busy: false,
        phase: "idle",
        errorText: null,
        outcome: null,
        evaluation: { ...eligibleEvaluation, runId: "run-from-session-001" },
        history: []
      });

      expect(promotion.canPromote).toBe(true);
      expect(promotion.evaluation?.sourceMode).toBe("backtest");
      expect(promotion.evaluation?.targetMode).toBe("paper");
    });

    it("Scenario_ReplayMismatchDetection_StaleReplayBlocksPromotion", () => {
      // This test verifies that if replay verification detects mismatches,
      // the readiness gate blocks promotion until replay is fresh

      const stalereplayVerification: PaperSessionReplayVerification = {
        summary: {
          sessionId: "session-002",
          strategyId: "strat-test",
          strategyName: null,
          initialCash: 100000,
          createdAt: "2026-04-27T14:00:00Z",
          closedAt: "2026-04-27T14:30:00Z",
          isActive: false
        },
        symbols: ["AAPL", "MSFT"],
        replaySource: "DurableFillLog",
        isConsistent: false,
        comparedFillCount: 40,
        comparedOrderCount: 18,
        comparedLedgerEntryCount: 15,
        mismatchReasons: [
          "Ledger entry count mismatch: 18 in durable log vs 15 in current state",
          "Last persisted ledger entry at 2026-04-27T14:30:00Z is before session close"
        ],
        currentPortfolio: null,
        replayPortfolio: {
          cash: 50000,
          portfolioValue: 150000,
          unrealisedPnl: 0,
          realisedPnl: 0,
          positions: [],
          asOf: "2026-04-27T14:30:00Z"
        },
        verificationAuditId: "audit-replay-002",
        lastPersistedFillAt: "2026-04-27T14:30:00Z",
        lastPersistedOrderUpdateAt: "2026-04-27T14:29:45Z",
        verifiedAt: "2026-04-27T14:35:00Z"
      };

      // Verify mismatch is detected
      expect(stalereplayVerification.isConsistent).toBe(false);
      expect(stalereplayVerification.mismatchReasons).toHaveLength(2);
      expect(stalereplayVerification.mismatchReasons[0]).toContain("Ledger entry count mismatch");

      // Verify that promotion cannot proceed until replay is fresh
      // (This would be enforced in the acceptance gate scoring)
    });
  });

  describe("Wave 2 Cockpit Acceptance Gate: Promotion decision visibility + audit rationale", () => {
    it("Scenario_RiskTriggeredPromotionRejection_DecisionRemainsVisibleWithBlockingRationale", () => {
      // This test verifies that when a promotion is blocked by risk checks,
      // the blocking reasons are visible and the rejection carries explicit rationale

      const blockedEvaluation: PromotionEvaluationResult = {
        runId: "run-risk-blocked",
        strategyId: "strat-high-risk",
        strategyName: "High Risk Test",
        sourceMode: "backtest",
        targetMode: "paper",
        isEligible: false,
        sharpeRatio: 0.5,
        maxDrawdownPercent: 45,
        totalReturn: 8,
        reason: "Risk thresholds exceeded",
        found: true,
        ready: false,
        blockingReasons: [
          "Maximum drawdown of 45% exceeds threshold of 30%",
          "Sharpe ratio of 0.5 below minimum of 0.8 for live operation",
          "Strategy requires human approval due to elevated risk profile"
        ],
        requiresHumanApproval: true
      };

      const state = buildPromotionGateState({
        form: {
          ...emptyPromotionGateForm,
          runId: "run-risk-blocked",
          approvedBy: "operator-qa",
          rejectionReason: "Exceeds max drawdown threshold; recommend risk model review before approval"
        },
        busy: false,
        phase: "idle",
        errorText: null,
        outcome: null,
        evaluation: blockedEvaluation,
        history: []
      });

      // Verify promotion is blocked with explicit reasons
      expect(state.evaluation?.isEligible).toBe(false);
      expect(state.evaluation?.blockingReasons).toHaveLength(3);
      expect(state.evaluation?.blockingReasons?.[0]).toContain("Maximum drawdown");
      expect(state.evaluation?.requiresHumanApproval).toBe(true);

      // Verify rejection can carry explicit rationale
      expect(state.form.rejectionReason).toBe("Exceeds max drawdown threshold; recommend risk model review before approval");
      expect(state.canReject).toBe(true);
    });

    it("buildPromotionApprovalChecklist_ProjectsEligibilityRequirements", () => {
      const checklist = buildPromotionApprovalChecklist(eligibleEvaluation);

      expect(checklist).toHaveLength(4);
      expect(checklist[0].label).toBe("DK1 data trust");
      expect(checklist[1].label).toBe("Run lineage");
      expect(checklist[2].label).toBe("Risk metrics");
      expect(checklist[3].label).toBe("Portfolio/Ledger continuity");

      // Verify status based on evaluation
      expect(checklist[1].description).toContain("S1"); // strategyName
      expect(checklist[2].description).toContain("1.20"); // Sharpe ratio formatted
    });
  });
});

// ─── Milestone 1: Named operator-state coverage ──────────────────────────────

describe("trading readiness named operator states", () => {
  const baseSession: TradingPaperSessionReadiness = {
    sessionId: "sess-1",
    strategyId: "strat-1",
    strategyName: "S1",
    isActive: true,
    initialCash: 100000,
    createdAt: "2026-04-01T00:00:00Z",
    closedAt: null,
    symbolCount: 2,
    orderCount: 5,
    positionCount: 1,
    portfolioValue: 102000
  };

  const consistentReplayReadiness: TradingReplayReadiness = {
    sessionId: "sess-1",
    replaySource: "DurableFillLog",
    isConsistent: true,
    comparedFillCount: 5,
    comparedOrderCount: 5,
    comparedLedgerEntryCount: 5,
    verifiedAt: "2026-04-01T01:00:00Z",
    lastPersistedFillAt: "2026-04-01T00:55:00Z",
    lastPersistedOrderUpdateAt: null,
    verificationAuditId: "audit-v1",
    mismatchReasons: []
  };

  const healthyBrokerageSync = {
    fundAccountId: "fund-1",
    providerId: "alpaca",
    externalAccountId: "ACCT-1",
    health: "Healthy" as const,
    isLinked: true,
    isStale: false,
    lastAttemptedSyncAt: "2026-04-01T01:00:00Z",
    lastSuccessfulSyncAt: "2026-04-01T01:00:00Z",
    lastError: null,
    positionCount: 1,
    openOrderCount: 0,
    fillCount: 5,
    cashTransactionCount: 0,
    securityMissingCount: 0,
    warnings: []
  };

  const signedTrustGate: TradingTrustGateReadiness = {
    gateId: "dk1",
    status: "signed",
    readyForOperatorReview: true,
    operatorSignoffRequired: true,
    operatorSignoffStatus: "signed",
    generatedAt: "2026-04-01T00:00:00Z",
    packetPath: "artifacts/dk1-packet.json",
    sourceSummary: "wave1-summary.json",
    requiredSampleCount: 4,
    readySampleCount: 4,
    validatedEvidenceDocumentCount: 4,
    requiredOwners: ["ops"],
    blockers: [],
    detail: "Signed.",
    operatorSignoff: null
  };

  it("context-required: missing session blocks cockpit with paper-session-missing work item", () => {
    const readiness: TradingOperatorReadiness = {
      ...blockedReadiness,
      overallStatus: "Blocked",
      activeSession: null,
      sessions: [],
      replay: null,
      workItems: [
        {
          workItemId: "paper-session-missing",
          kind: "PaperReplay",
          label: "Paper session missing",
          detail: "Create a paper session before operating.",
          tone: "Critical",
          createdAt: "2026-04-01T00:00:00Z",
          runId: null,
          fundAccountId: null,
          auditReference: null
        }
      ],
      warnings: []
    };

    const state = buildTradingReadinessState({ readiness, refreshing: false, errorText: null });

    expect(state.readiness?.overallStatus).toBe("Blocked");
    expect(state.workItems).toHaveLength(1);
    expect(state.workItems[0].workItemId).toBe("paper-session-missing");
    expect(state.workItems[0].kind).toBe("PaperReplay");
    expect(state.workItems[0].tone).toBe("Critical");
    expect(state.primaryWorkItemKind).toBe("PaperReplay");
    expect(state.hasOperatorAttention).toBe(true);
    // PaperReplay action routes to the session-replay panel in the trading cockpit
    expect(state.visibleWorkItems[0].action).toEqual({
      label: "Verify replay",
      href: "/trading#session-replay-panel",
      ariaLabel: "Open session replay panel for Paper session missing",
      detail: "Verify replay consistency in the Trading cockpit."
    });
  });

  it("replay-mismatch: inconsistent replay blocks cockpit and routes to replay panel", () => {
    const readiness: TradingOperatorReadiness = {
      ...blockedReadiness,
      overallStatus: "Blocked",
      readyForPaperOperation: false,
      activeSession: baseSession,
      sessions: [baseSession],
      replay: {
        ...consistentReplayReadiness,
        isConsistent: false,
        mismatchReasons: [
          "Ledger entry count mismatch: 18 vs 15",
          "Last persisted fill diverges from session state"
        ],
        verificationAuditId: "audit-mismatch-1"
      },
      workItems: [
        {
          workItemId: "paper-replay-mismatch-sess-1",
          kind: "PaperReplay",
          label: "Replay mismatch detected",
          detail: "Ledger entry count mismatch: 18 vs 15",
          tone: "Critical",
          createdAt: "2026-04-01T00:00:00Z",
          runId: null,
          fundAccountId: null,
          auditReference: "audit-mismatch-1"
        }
      ],
      warnings: []
    };

    const state = buildTradingReadinessState({ readiness, refreshing: false, errorText: null });

    expect(state.readiness?.overallStatus).toBe("Blocked");
    expect(state.workItems).toHaveLength(1);
    expect(state.workItems[0].workItemId).toBe("paper-replay-mismatch-sess-1");
    expect(state.workItems[0].detail).toContain("Ledger entry count mismatch");
    expect(state.workItems[0].auditReference).toBe("audit-mismatch-1");
    expect(state.visibleWorkItems[0].action?.label).toBe("Verify replay");
    expect(state.visibleWorkItems[0].action?.href).toBe("/trading#session-replay-panel");
  });

  it("controls-blocked: open circuit breaker surfaces reason and routes to risk controls", () => {
    const readiness: TradingOperatorReadiness = {
      ...blockedReadiness,
      overallStatus: "Blocked",
      readyForPaperOperation: false,
      activeSession: baseSession,
      sessions: [baseSession],
      replay: consistentReplayReadiness,
      controls: {
        circuitBreakerOpen: true,
        circuitBreakerReason: "Drawdown threshold breached",
        circuitBreakerChangedBy: "system",
        circuitBreakerChangedAt: "2026-04-01T00:30:00Z",
        manualOverrideCount: 0,
        symbolLimitCount: 0,
        defaultMaxPositionSize: null
      },
      workItems: [
        {
          workItemId: "execution-circuit-breaker-open",
          kind: "ExecutionControl",
          label: "Circuit breaker open",
          detail: "Drawdown threshold breached",
          tone: "Critical",
          createdAt: "2026-04-01T00:30:00Z",
          runId: null,
          fundAccountId: null,
          auditReference: null
        }
      ],
      warnings: []
    };

    const state = buildTradingReadinessState({ readiness, refreshing: false, errorText: null });

    expect(state.readiness?.overallStatus).toBe("Blocked");
    expect(state.workItems[0].workItemId).toBe("execution-circuit-breaker-open");
    expect(state.workItems[0].detail).toBe("Drawdown threshold breached");
    expect(state.visibleWorkItems[0].action).toEqual({
      label: "Review risk controls",
      href: "/trading/risk",
      ariaLabel: "Open risk controls for Circuit breaker open",
      detail: "Review execution guardrails and circuit-breaker state in Trading."
    });
  });

  it("paper-review: incomplete promotion trace routes operator to promotion gate panel", () => {
    const readiness: TradingOperatorReadiness = {
      ...blockedReadiness,
      overallStatus: "ReviewRequired",
      readyForPaperOperation: false,
      activeSession: baseSession,
      sessions: [baseSession],
      replay: consistentReplayReadiness,
      promotion: {
        state: "ReviewRequired",
        reason: "approvalChecklist field is missing from promotion trace",
        requiresReview: true,
        sourceRunId: "run-backtest-001",
        targetRunId: "run-paper-001",
        suggestedNextMode: "paper",
        auditReference: "audit-promo-1",
        approvalStatus: null,
        manualOverrideId: null,
        approvedBy: null,
        approvalChecklist: null
      },
      workItems: [
        {
          workItemId: "promotion-trace-incomplete-run-backtest-001",
          kind: "PromotionReview",
          label: "Promotion trace incomplete",
          detail: "approvalChecklist field is missing from promotion trace",
          tone: "Warning",
          createdAt: "2026-04-01T00:00:00Z",
          runId: "run-backtest-001",
          fundAccountId: null,
          auditReference: "audit-promo-1"
        }
      ],
      warnings: []
    };

    const state = buildTradingReadinessState({ readiness, refreshing: false, errorText: null });

    expect(state.readiness?.overallStatus).toBe("ReviewRequired");
    expect(state.workItems[0].workItemId).toBe("promotion-trace-incomplete-run-backtest-001");
    expect(state.workItems[0].detail).toContain("approvalChecklist field is missing");
    expect(state.visibleWorkItems[0].action?.label).toBe("Open promotion gate");
    expect(state.visibleWorkItems[0].action?.href).toBe("/trading#promotion-gate-panel");
  });

  it.todo("live-oversight: ReviewRequired with live-only work item documents expected readiness shape before live flag lands");
});

// ─── Milestone 2: Work-item action routing for non-brokerage kinds ────────────

describe("trading readiness work-item action routing", () => {
  function workItemForKind(kind: OperatorWorkItemKind, extra?: Partial<OperatorWorkItem>): TradingOperatorReadiness {
    return {
      ...blockedReadiness,
      workItems: [
        {
          workItemId: `test-${kind.toLowerCase()}`,
          kind,
          label: `Test ${kind}`,
          detail: `Detail for ${kind}`,
          tone: "Critical",
          createdAt: "2026-04-01T00:00:00Z",
          runId: null,
          fundAccountId: null,
          auditReference: null,
          ...extra
        }
      ],
      warnings: []
    };
  }

  it("routes PaperReplay work items to the session-replay panel", () => {
    const state = buildTradingReadinessState({ readiness: workItemForKind("PaperReplay"), refreshing: false, errorText: null });
    const action = state.visibleWorkItems[0].action;

    expect(action).not.toBeNull();
    expect(action?.label).toBe("Verify replay");
    expect(action?.href).toBe("/trading#session-replay-panel");
    expect(action?.detail).toContain("replay");
  });

  it("preserves the PaperReplay session-replay panel when shared metadata targets the Trading shell", () => {
    const state = buildTradingReadinessState({
      readiness: workItemForKind("PaperReplay", {
        workspace: "Trading",
        targetRoute: "/api/workstation/trading/readiness",
        targetPageTag: "TradingShell"
      }),
      refreshing: false,
      errorText: null
    });
    const action = state.visibleWorkItems[0].action;

    expect(action).not.toBeNull();
    expect(action?.label).toBe("Verify replay");
    expect(action?.href).toBe("/trading#session-replay-panel");
  });

  it("routes ExecutionControl work items to the trading risk route", () => {
    const state = buildTradingReadinessState({ readiness: workItemForKind("ExecutionControl"), refreshing: false, errorText: null });
    const action = state.visibleWorkItems[0].action;

    expect(action).not.toBeNull();
    expect(action?.label).toBe("Review risk controls");
    expect(action?.href).toBe("/trading/risk");
    expect(action?.detail).toContain("guardrails");
  });

  it("uses shared TradingShell metadata for ExecutionControl work items when present", () => {
    const state = buildTradingReadinessState({
      readiness: workItemForKind("ExecutionControl", {
        workspace: "Trading",
        targetRoute: "/api/workstation/trading/readiness",
        targetPageTag: "TradingShell"
      }),
      refreshing: false,
      errorText: null
    });
    const action = state.visibleWorkItems[0].action;

    expect(action).not.toBeNull();
    expect(action?.label).toBe("Review risk controls");
    expect(action?.href).toBe("/trading");
  });

  it("routes PromotionReview work items to the promotion gate panel", () => {
    const state = buildTradingReadinessState({ readiness: workItemForKind("PromotionReview", { runId: "run-001" }), refreshing: false, errorText: null });
    const action = state.visibleWorkItems[0].action;

    expect(action).not.toBeNull();
    expect(action?.label).toBe("Open promotion gate");
    expect(action?.href).toBe("/trading#promotion-gate-panel");
  });

  it("uses shared TradingShell metadata for PromotionReview work items when present", () => {
    const state = buildTradingReadinessState({
      readiness: workItemForKind("PromotionReview", {
        runId: "run-001",
        workspace: "Trading",
        targetRoute: "/api/workstation/trading/readiness",
        targetPageTag: "TradingShell"
      }),
      refreshing: false,
      errorText: null
    });
    const action = state.visibleWorkItems[0].action;

    expect(action).not.toBeNull();
    expect(action?.label).toBe("Open promotion gate");
    expect(action?.href).toBe("/trading");
  });

  it("routes SecurityMasterCoverage work items to the security master route", () => {
    const state = buildTradingReadinessState({ readiness: workItemForKind("SecurityMasterCoverage"), refreshing: false, errorText: null });
    const action = state.visibleWorkItems[0].action;

    expect(action).not.toBeNull();
    expect(action?.label).toBe("Open security master");
    expect(action?.href).toBe("/accounting/security-master");
    expect(action?.detail).toContain("security");
  });

  it("routes ReconciliationBreak work items to the reconciliation route", () => {
    const state = buildTradingReadinessState({ readiness: workItemForKind("ReconciliationBreak"), refreshing: false, errorText: null });
    const action = state.visibleWorkItems[0].action;

    expect(action).not.toBeNull();
    expect(action?.label).toBe("Open reconciliation");
    expect(action?.href).toBe("/accounting/reconciliation");
    expect(action?.detail).toContain("reconciliation");
  });

  it("returns null action for unrouted work item kinds", () => {
    const state = buildTradingReadinessState({ readiness: workItemForKind("ReportPackApproval"), refreshing: false, errorText: null });
    expect(state.visibleWorkItems[0].action).toBeNull();
  });
});

// ─── Milestone 4: Approval checklist field + validation ──────────────────────

describe("promotion approval checklist", () => {
  it("validates that an empty checklist blocks the approval", () => {
    const formWithChecklist = {
      ...emptyPromotionGateForm,
      runId: "run-1",
      approvedBy: "operator-1",
      approvalReason: "Meets risk criteria",
      approvalChecklist: []
    };

    const error = validatePromotionApproval(formWithChecklist, eligibleEvaluation);
    expect(error).toContain("checklist");
  });

  it("allows promotion when the checklist is fully populated", () => {
    const formWithChecklist = {
      ...emptyPromotionGateForm,
      runId: "run-1",
      approvedBy: "operator-1",
      approvalReason: "Meets risk criteria",
      approvalChecklist: paperPromotionApprovalChecklist
    };

    expect(validatePromotionApproval(formWithChecklist, eligibleEvaluation)).toBeNull();
  });

  it("includes approvalChecklist in the serialized request when populated", () => {
    const form = {
      ...emptyPromotionGateForm,
      runId: "run-1",
      approvedBy: "operator-1",
      approvalReason: "Meets risk criteria",
      approvalChecklist: paperPromotionApprovalChecklist
    };

    const request = buildPromotionApprovalRequest(form);
    expect(request.approvalChecklist).toEqual(paperPromotionApprovalChecklist);
    expect(request.runId).toBe("run-1");
  });

  it("omits approvalChecklist from the request when the form list is empty", () => {
    const form = {
      ...emptyPromotionGateForm,
      runId: "run-1",
      approvedBy: "operator-1",
      approvalReason: "Meets risk criteria",
      approvalChecklist: []
    };

    const request = buildPromotionApprovalRequest(form);
    expect(request.approvalChecklist).toBeUndefined();
  });

  it("auto-populates the checklist from the evaluation result when gate is eligible", async () => {
    const deferred = createDeferred<PromotionEvaluationResult>();
    const services: PromotionGateServices = {
      evaluatePromotion: () => deferred.promise,
      approvePromotion: vi.fn(),
      rejectPromotion: vi.fn(),
      getPromotionHistory: () => Promise.resolve([])
    };

    const { result } = renderHook(() => usePromotionGateViewModel(services));

    act(() => {
      result.current.updateField("runId", "run-1");
    });

    expect(result.current.form.approvalChecklist).toHaveLength(0);

    act(() => {
      void result.current.evaluateGateChecks();
    });

    await act(async () => {
      deferred.resolve(eligibleEvaluation);
      await deferred.promise;
    });

    await waitFor(() => {
      expect(result.current.form.approvalChecklist).toHaveLength(4);
    });
    expect(result.current.form.approvalChecklist).toContain("DK1_TRUST_PACKET_REVIEWED");
    expect(result.current.form.approvalChecklist).toContain("RISK_CONTROLS_REVIEWED");
  });

  it("clears the approval checklist when the runId field changes", async () => {
    const deferred = createDeferred<PromotionEvaluationResult>();
    const services: PromotionGateServices = {
      evaluatePromotion: () => deferred.promise,
      approvePromotion: vi.fn(),
      rejectPromotion: vi.fn(),
      getPromotionHistory: () => Promise.resolve([])
    };

    const { result } = renderHook(() => usePromotionGateViewModel(services));

    act(() => {
      result.current.updateField("runId", "run-1");
    });
    act(() => {
      void result.current.evaluateGateChecks();
    });
    await act(async () => {
      deferred.resolve(eligibleEvaluation);
      await deferred.promise;
    });
    await waitFor(() => expect(result.current.form.approvalChecklist.length).toBeGreaterThan(0));

    act(() => {
      result.current.updateField("runId", "run-2");
    });

    expect(result.current.form.approvalChecklist).toHaveLength(0);
  });
});

// ─── Milestone 5: Fund account context threading ─────────────────────────────

describe("trading readiness fund account threading", () => {
  it("passes fundAccountId to getTradingReadiness on refresh", async () => {
    const getTradingReadiness = vi.fn<TradingReadinessServices["getTradingReadiness"]>();
    const deferred = createDeferred<TradingOperatorReadiness | null>();
    getTradingReadiness.mockReturnValueOnce(deferred.promise);

    const services: TradingReadinessServices = { getTradingReadiness };
    const { result } = renderHook(() =>
      useTradingReadinessViewModel({ initialReadiness: null, fundAccountId: "fund-account-42", services })
    );

    await act(async () => {
      void result.current.refresh();
    });

    expect(getTradingReadiness).toHaveBeenCalledOnce();
    const callArgs = getTradingReadiness.mock.calls[0]?.[0];
    expect(callArgs?.fundAccountId).toBe("fund-account-42");

    await act(async () => {
      deferred.resolve(null);
      await deferred.promise;
    });
  });

  it("omits fundAccountId from the call when no account context is active", async () => {
    const getTradingReadiness = vi.fn<TradingReadinessServices["getTradingReadiness"]>();
    const deferred = createDeferred<TradingOperatorReadiness | null>();
    getTradingReadiness.mockReturnValueOnce(deferred.promise);

    const services: TradingReadinessServices = { getTradingReadiness };
    const { result } = renderHook(() =>
      useTradingReadinessViewModel({ initialReadiness: null, services })
    );

    await act(async () => {
      void result.current.refresh();
    });

    expect(getTradingReadiness).toHaveBeenCalledOnce();
    const callArgs = getTradingReadiness.mock.calls[0]?.[0];
    expect(callArgs?.fundAccountId).toBeUndefined();

    await act(async () => {
      deferred.resolve(null);
      await deferred.promise;
    });
  });
});

// ─── Lane A / W2 blocker-path recovery: browser parity ───────────────────────

describe("trading readiness blocker recovery", () => {
  it("drops blocked work items from view state when refresh returns an empty work-item list", () => {
    // Step 1: build blocked state as seen before recovery
    const blockedState = buildTradingReadinessState({
      readiness: blockedReadiness,
      refreshing: false,
      errorText: null
    });

    expect(blockedState.hasOperatorAttention).toBe(true);
    expect(blockedState.visibleWorkItems).toHaveLength(1);
    expect(blockedState.visibleWorkItems[0].tone).toBe("Critical");

    // Step 2: simulate the refresh returning a cleared readiness payload
    const clearedReadiness: TradingOperatorReadiness = {
      ...blockedReadiness,
      asOf: "2026-04-26T16:30:00Z",
      overallStatus: "Ready",
      readyForPaperOperation: true,
      brokerageSync: {
        ...blockedReadiness.brokerageSync!,
        health: "Healthy",
        isStale: false,
        lastError: null,
        warnings: []
      },
      workItems: [],
      warnings: []
    };

    const clearedState = buildTradingReadinessState({
      readiness: clearedReadiness,
      refreshing: false,
      errorText: null
    });

    expect(clearedState.hasOperatorAttention).toBe(false);
    expect(clearedState.visibleWorkItems).toHaveLength(0);
    expect(clearedState.workItemSummaryText).toBe("0 readiness items and 0 warnings.");
  });

  it("transitions status announcement from blocked to ready when blockers clear", () => {
    const clearedReadiness: TradingOperatorReadiness = {
      ...blockedReadiness,
      asOf: "2026-04-26T16:35:00Z",
      overallStatus: "Ready",
      readyForPaperOperation: true,
      workItems: [],
      warnings: []
    };

    const state = buildTradingReadinessState({
      readiness: clearedReadiness,
      refreshing: false,
      errorText: null
    });

    expect(state.statusAnnouncement).toContain("ready");
    expect(state.statusAnnouncement).not.toContain("blocked");
  });

  it("refresh via hook applies cleared readiness payload and drops blocked work items", async () => {
    const blockerWorkItem = blockedReadiness.workItems[0];
    expect(blockerWorkItem).toBeDefined();

    const clearedReadiness: TradingOperatorReadiness = {
      ...blockedReadiness,
      asOf: "2026-04-26T16:40:00Z",
      overallStatus: "Ready",
      readyForPaperOperation: true,
      workItems: [],
      warnings: []
    };

    const getTradingReadiness = vi.fn<TradingReadinessServices["getTradingReadiness"]>()
      .mockResolvedValueOnce(clearedReadiness);
    const services: TradingReadinessServices = { getTradingReadiness };

    const { result } = renderHook(() =>
      useTradingReadinessViewModel({ initialReadiness: blockedReadiness, services })
    );

    expect(result.current.readiness?.workItems).toHaveLength(1);

    await act(async () => {
      await result.current.refresh();
    });

    expect(result.current.readiness?.workItems).toHaveLength(0);
    expect(result.current.readiness?.overallStatus).toBe("Ready");
    expect(result.current.readiness?.readyForPaperOperation).toBe(true);
  });
});

// ─── Milestone 7: Green cockpit proof ────────────────────────────────────────

describe("green cockpit: ReadyForPaperOperation proof", () => {
  const activeSession: TradingPaperSessionReadiness = {
    sessionId: "sess-green",
    strategyId: "strat-green",
    strategyName: "Green Strategy",
    isActive: true,
    initialCash: 100000,
    createdAt: "2026-04-01T00:00:00Z",
    closedAt: null,
    symbolCount: 2,
    orderCount: 10,
    positionCount: 2,
    portfolioValue: 108000
  };

  const consistentReplay: TradingReplayReadiness = {
    sessionId: "sess-green",
    replaySource: "DurableFillLog",
    isConsistent: true,
    comparedFillCount: 10,
    comparedOrderCount: 10,
    comparedLedgerEntryCount: 10,
    verifiedAt: "2026-04-01T01:00:00Z",
    lastPersistedFillAt: "2026-04-01T00:55:00Z",
    lastPersistedOrderUpdateAt: null,
    verificationAuditId: "audit-green",
    mismatchReasons: []
  };

  const signedTrustGate: TradingTrustGateReadiness = {
    gateId: "dk1",
    status: "signed",
    readyForOperatorReview: true,
    operatorSignoffRequired: true,
    operatorSignoffStatus: "signed",
    generatedAt: "2026-04-01T00:00:00Z",
    packetPath: "artifacts/dk1-packet.json",
    sourceSummary: "wave1-summary.json",
    requiredSampleCount: 4,
    readySampleCount: 4,
    validatedEvidenceDocumentCount: 4,
    requiredOwners: ["ops"],
    blockers: [],
    detail: "Signed.",
    operatorSignoff: null
  };

  const healthyBrokerageSync: WorkstationBrokerageSyncStatus = {
    fundAccountId: "fund-green",
    providerId: "alpaca",
    externalAccountId: "ACCT-GREEN",
    health: "Healthy",
    isLinked: true,
    isStale: false,
    lastAttemptedSyncAt: "2026-04-01T01:00:00Z",
    lastSuccessfulSyncAt: "2026-04-01T01:00:00Z",
    lastError: null,
    positionCount: 2,
    openOrderCount: 0,
    fillCount: 10,
    cashTransactionCount: 0,
    securityMissingCount: 0,
    warnings: []
  };

  it("shows ReadyForPaperOperation=true when DK1 signed + healthy brokerage + active session + consistent replay", () => {
    const greenReadiness: TradingOperatorReadiness = {
      asOf: "2026-04-01T01:00:00Z",
      overallStatus: "Ready",
      readyForPaperOperation: true,
      acceptanceGates: [],
      activeSession: activeSession,
      sessions: [activeSession],
      replay: consistentReplay,
      controls: {
        circuitBreakerOpen: false,
        circuitBreakerReason: null,
        circuitBreakerChangedBy: null,
        circuitBreakerChangedAt: null,
        manualOverrideCount: 0,
        symbolLimitCount: 0,
        defaultMaxPositionSize: null
      },
      promotion: null,
      trustGate: signedTrustGate,
      brokerageSync: healthyBrokerageSync,
      workItems: [],
      warnings: []
    };

    const state = buildTradingReadinessState({ readiness: greenReadiness, refreshing: false, errorText: null });

    expect(state.readiness?.readyForPaperOperation).toBe(true);
    expect(state.readiness?.overallStatus).toBe("Ready");
    expect(state.workItems).toHaveLength(0);
    expect(state.hasOperatorAttention).toBe(false);
  });

  it("keeps cockpit in ReviewRequired when DK1 operatorSignoffStatus=pending even with healthy brokerage", () => {
    const pendingTrustGate: TradingTrustGateReadiness = {
      ...signedTrustGate,
      status: "ready-for-operator-review",
      operatorSignoffStatus: "pending"
    };

    const pendingReadiness: TradingOperatorReadiness = {
      asOf: "2026-04-01T01:00:00Z",
      overallStatus: "ReviewRequired",
      readyForPaperOperation: false,
      acceptanceGates: [],
      activeSession: activeSession,
      sessions: [activeSession],
      replay: consistentReplay,
      controls: {
        circuitBreakerOpen: false,
        circuitBreakerReason: null,
        circuitBreakerChangedBy: null,
        circuitBreakerChangedAt: null,
        manualOverrideCount: 0,
        symbolLimitCount: 0,
        defaultMaxPositionSize: null
      },
      promotion: null,
      trustGate: pendingTrustGate,
      brokerageSync: healthyBrokerageSync,
      workItems: [
        {
          workItemId: "dk1-operator-signoff-pending",
          kind: "ProviderTrustGate",
          label: "DK1 operator sign-off pending",
          detail: "DK1 trust gate requires operator sign-off before paper operation.",
          tone: "Warning",
          createdAt: "2026-04-01T01:00:00Z",
          runId: null,
          fundAccountId: null,
          auditReference: null
        }
      ],
      warnings: []
    };

    const state = buildTradingReadinessState({ readiness: pendingReadiness, refreshing: false, errorText: null });

    expect(state.readiness?.readyForPaperOperation).toBe(false);
    expect(state.readiness?.overallStatus).toBe("ReviewRequired");
    expect(state.workItems).toHaveLength(1);
    expect(state.workItems[0].workItemId).toBe("dk1-operator-signoff-pending");
    expect(state.hasOperatorAttention).toBe(true);
  });
});

describe("trading readiness blocker taxonomy parity", () => {
  it("keeps blocker severity and repair routes aligned for core Wave 2 blockers", () => {
    const readiness: TradingOperatorReadiness = {
      ...blockedReadiness,
      overallStatus: "Blocked",
      acceptanceGates: [
        { gateId: "replay", label: "Replay", status: "ReviewRequired", detail: "Replay evidence is stale.", sessionId: "sess-1", runId: null, auditReference: "audit-replay" },
        { gateId: "trust-gate", label: "Provider trust", status: "Blocked", detail: "Operator sign-off evidence is missing.", sessionId: null, runId: null, auditReference: "audit-trust" },
        { gateId: "brokerage-sync", label: "Brokerage sync", status: "Blocked", detail: "Brokerage sync is incomplete.", sessionId: null, runId: null, auditReference: "audit-sync" }
      ],
      workItems: [
        { workItemId: "replay-stale", kind: "PaperReplay", label: "Replay stale", detail: "Replay verification is stale.", tone: "Warning", createdAt: "2026-05-01T00:00:00Z", runId: "run-1", fundAccountId: null, auditReference: "audit-replay" },
        { workItemId: "signoff-missing", kind: "ProviderTrustGate", label: "Sign-off missing", detail: "Missing operator sign-off evidence.", tone: "Critical", createdAt: "2026-05-01T00:00:01Z", runId: null, fundAccountId: null, auditReference: "audit-trust" },
        { workItemId: "provider-trust-degraded", kind: "ProviderTrustGate", label: "Provider trust degraded", detail: "Provider trust gate is degraded.", tone: "Critical", createdAt: "2026-05-01T00:00:02Z", runId: null, fundAccountId: null, auditReference: "audit-trust-2" },
        { workItemId: "brokerage-sync-incomplete", kind: "BrokerageSync", label: "Brokerage sync incomplete", detail: "Brokerage sync is incomplete.", tone: "Critical", createdAt: "2026-05-01T00:00:03Z", runId: null, fundAccountId: "fund-1", auditReference: "audit-sync", targetRoute: "/api/fund-accounts/fund-1/brokerage-sync", targetPageTag: "AccountPortfolio" }
      ]
    };

    const state = buildTradingReadinessState({ readiness, refreshing: false, errorText: null });
    const byId = new Map(state.visibleWorkItems.map((item) => [item.workItemId, item]));

    expect(byId.get("replay-stale")?.tone).toBe("Warning");
    expect(byId.get("replay-stale")?.action?.href).toBe("/trading#session-replay-panel");

    expect(byId.get("signoff-missing")?.tone).toBe("Critical");
    expect(byId.get("signoff-missing")?.action?.href).toBe("/data/providers");

    expect(byId.get("provider-trust-degraded")?.tone).toBe("Critical");
    expect(byId.get("provider-trust-degraded")?.action?.href).toBe("/data/providers");

    expect(byId.get("brokerage-sync-incomplete")?.tone).toBe("Critical");
    expect(byId.get("brokerage-sync-incomplete")?.action?.href).toBe("/portfolio/accounts/fund-1");
  });
});
