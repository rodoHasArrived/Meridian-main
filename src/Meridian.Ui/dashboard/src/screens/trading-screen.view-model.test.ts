import {
  buildExecutionEvidenceState,
  buildPaperSessionCreateRequest,
  buildPaperSessionState,
  buildSessionReplayControlsState,
  buildTradingConfirmDialogState,
  buildOrderSubmitRequest,
  buildOrderTicketState,
  buildPromotionApprovalRequest,
  buildPromotionGateState,
  buildPromotionRejectionRequest,
  buildTradingReadinessState,
  createTradingConfirmState,
  emptyPaperSessionForm,
  emptyOrderTicketForm,
  emptyPromotionGateForm,
  formatReadinessStatusValue,
  mapBrokerageSyncLevel,
  mapReadinessStatusLevel,
  updateOrderTicketForm,
  validatePaperSessionForm,
  validateOrderTicketForm,
  validatePromotionApproval,
  validatePromotionRejection
} from "@/screens/trading-screen.view-model";
import type { ExecutionAuditEntry, ExecutionControlSnapshot, PaperSessionDetail, PaperSessionReplayVerification, PaperSessionSummary, PromotionEvaluationResult, ReplayFileRecord, ReplayStatus, TradingOperatorReadiness } from "@/types";

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

    const failed = buildExecutionEvidenceState({
      auditEntries: [],
      controlsSnapshot: null,
      loading: false,
      errorText: "Controls API unavailable."
    });

    expect(failed.auditEmptyText).toBe("No execution audit entries available.");
    expect(failed.controlsEmptyText).toBe("Snapshot unavailable.");
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
        verifyButtonLabel: "Verifying...",
        ariaLabel: "sess-1, strat-1, Active, $100,000.00 initial cash"
      })
    ]);
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
    expect(invalidState.formRequirementText).toBe("Enter initial cash of at least $1,000.");
    expect(invalidState.statusAnnouncement).toBe("Paper session workflow failed: Create failed");
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
    expect(invalid.canStart).toBe(false);
    expect(invalid.canSeek).toBe(false);
    expect(invalid.canApplySpeed).toBe(false);
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
      expect.objectContaining({ id: "as-of", label: "As of", value: "2026-04-26T16:05:00Z", level: "review" })
    ]);
    expect(state.workItems).toHaveLength(1);
    expect(state.warnings).toEqual(["Portfolio snapshot failed."]);
    expect(state.statusAnnouncement).toBe("Trading readiness blocked as of 2026-04-26T16:05:00Z.");
  });

  it("derives refresh and error copy for readiness commands", () => {
    const refreshing = buildTradingReadinessState({
      readiness: blockedReadiness,
      refreshing: true,
      errorText: null
    });

    expect(refreshing.refreshButtonLabel).toBe("Refreshing...");
    expect(refreshing.refreshAriaLabel).toBe("Refreshing trading readiness");
    expect(refreshing.statusAnnouncement).toBe("Refreshing trading readiness.");

    const failed = buildTradingReadinessState({
      readiness: null,
      refreshing: false,
      errorText: "Network failed."
    });

    expect(failed.summaryRows).toEqual([]);
    expect(failed.statusAnnouncement).toBe("Trading readiness refresh failed: Network failed.");
  });

  it("normalizes readiness and brokerage status levels", () => {
    expect(formatReadinessStatusValue("ReviewRequired")).toBe("Review required");
    expect(mapReadinessStatusLevel("Ready")).toBe("ready");
    expect(mapReadinessStatusLevel("Blocked")).toBe("atRisk");
    expect(mapBrokerageSyncLevel({ ...blockedReadiness.brokerageSync!, health: "Healthy", isStale: false })).toBe("ready");
    expect(mapBrokerageSyncLevel(blockedReadiness.brokerageSync!)).toBe("atRisk");
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
    expect(state.canConfirm).toBe(true);
    expect(state.statusAnnouncement).toBe("Cancel order PO-1 confirmation open.");
  });

  it("derives busy and completed states for assistive feedback", () => {
    const action = { kind: "close-position" as const, symbol: "AAPL" };
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

    const submitting = buildOrderTicketState({
      form: { ...emptyOrderTicketForm, symbol: "MSFT", quantity: 2 },
      open: true,
      phase: "submitting",
      orderId: null,
      errorText: null
    });

    expect(submitting.submitButtonLabel).toBe("Submitting...");
    expect(submitting.statusAnnouncement).toBe("Submitting order request.");

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
        approvalReason: " Meets risk constraints "
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
});
