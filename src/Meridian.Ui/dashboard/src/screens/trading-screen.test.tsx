import { screen, fireEvent, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TradingScreen } from "@/screens/trading-screen";
import * as api from "@/lib/api";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type { TradingWorkspaceResponse } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    cancelOrder: vi.fn().mockResolvedValue({ actionId: "a1", status: "Completed", message: "ok", occurredAt: new Date().toISOString() }),
    cancelAllOrders: vi.fn().mockResolvedValue({ actionId: "a2", status: "Completed", message: "ok", occurredAt: new Date().toISOString() }),
    closePosition: vi.fn().mockResolvedValue({ actionId: "a3", status: "Accepted", message: "ok", occurredAt: new Date().toISOString() }),
    submitOrder: vi.fn().mockResolvedValue({ success: true, orderId: "O-1", reason: null }),
    getExecutionSessions: vi.fn().mockResolvedValue([{ sessionId: "sess-1", strategyId: "strat-1", strategyName: null, initialCash: 100000, createdAt: "2026-01-01", closedAt: null, isActive: true }]),
    createPaperSession: vi.fn(),
    closePaperSession: vi.fn().mockResolvedValue({ actionId: "a4", status: "Completed", message: "closed", occurredAt: "2026-01-01T01:00:00Z", auditId: "audit-close-1" }),
    getPaperSessionDetail: vi.fn().mockResolvedValue({
      summary: { sessionId: "sess-1", strategyId: "strat-1", strategyName: null, initialCash: 100000, createdAt: "2026-01-01", closedAt: null, isActive: true },
      symbols: ["AAPL", "MSFT"],
      portfolio: {
        cash: 99000,
        portfolioValue: 100250,
        unrealisedPnl: 250,
        realisedPnl: 0,
        positions: [{ symbol: "AAPL", quantity: 5, averageCostBasis: 200, currentPrice: 205, marketValue: 1025, unrealisedPnl: 25, realisedPnl: 0 }],
        asOf: "2026-01-01T00:15:00Z"
      },
      orderHistory: []
    }),
    getPaperSessionReplayVerification: vi.fn().mockResolvedValue({
      summary: { sessionId: "sess-1", strategyId: "strat-1", strategyName: null, initialCash: 100000, createdAt: "2026-01-01", closedAt: null, isActive: true },
      symbols: ["AAPL", "MSFT"],
      replaySource: "DurableFillLog",
      isConsistent: true,
      mismatchReasons: [],
      currentPortfolio: {
        cash: 99000,
        portfolioValue: 100250,
        unrealisedPnl: 250,
        realisedPnl: 0,
        positions: [{ symbol: "AAPL", quantity: 5, averageCostBasis: 200, currentPrice: 205, marketValue: 1025, unrealisedPnl: 25, realisedPnl: 0 }],
        asOf: "2026-01-01T00:15:00Z"
      },
      replayPortfolio: {
        cash: 99000,
        portfolioValue: 100250,
        unrealisedPnl: 250,
        realisedPnl: 0,
        positions: [{ symbol: "AAPL", quantity: 5, averageCostBasis: 200, currentPrice: 205, marketValue: 1025, unrealisedPnl: 25, realisedPnl: 0 }],
        asOf: "2026-01-01T00:15:00Z"
      },
      verifiedAt: "2026-01-01T00:20:00Z",
      comparedFillCount: 1,
      comparedOrderCount: 0,
      comparedLedgerEntryCount: 1,
      lastPersistedFillAt: "2026-01-01T00:10:00Z",
      lastPersistedOrderUpdateAt: null,
      verificationAuditId: "audit-verify-1"
    }),
    getExecutionAudit: vi.fn().mockResolvedValue([
      {
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
      }
    ]),
    getExecutionControls: vi.fn().mockResolvedValue({
      circuitBreaker: { isOpen: false, reason: null, changedBy: "ops", changedAt: "2026-01-01T00:00:00Z" },
      defaultMaxPositionSize: 5000,
      symbolPositionLimits: { AAPL: 2500 },
      manualOverrides: [
        {
          overrideId: "ovr-1",
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
    }),
    getReplayFiles: vi.fn().mockResolvedValue({ files: [{ path: "/tmp/replay.jsonl", name: "replay.jsonl", symbol: "AAPL", eventType: "trades", sizeBytes: 1, isCompressed: false, lastModified: "2026-01-01" }], total: 1, timestamp: "2026-01-01" }),
    startReplay: vi.fn().mockResolvedValue({ sessionId: "rep-1", filePath: "/tmp/replay.jsonl", status: "started", speedMultiplier: 1 }),
    getReplayStatus: vi.fn().mockResolvedValue({ sessionId: "rep-1", filePath: "/tmp/replay.jsonl", status: "running", speedMultiplier: 1, eventsProcessed: 3, totalEvents: 10, progressPercent: 30, startedAt: "2026-01-01" }),
    getTradingReadiness: vi.fn().mockResolvedValue(null),
    pauseReplay: vi.fn().mockResolvedValue({}),
    resumeReplay: vi.fn().mockResolvedValue({}),
    stopReplay: vi.fn().mockResolvedValue({}),
    seekReplay: vi.fn().mockResolvedValue({}),
    setReplaySpeed: vi.fn().mockResolvedValue({}),
    evaluatePromotion: vi.fn().mockResolvedValue({ runId: "run-1", strategyId: "strat-1", strategyName: "S1", sourceMode: "backtest", targetMode: "paper", isEligible: true, sharpeRatio: 1.2, maxDrawdownPercent: 5, totalReturn: 10, reason: "Eligible", found: true, ready: true }),
    approvePromotion: vi.fn().mockResolvedValue({ success: true, promotionId: "promo-1", newRunId: "paper-1", reason: "approved", auditReference: "audit-promo-1", approvedBy: "operator-7" }),
    rejectPromotion: vi.fn().mockResolvedValue({ success: true, promotionId: "promo-reject-1", newRunId: null, reason: "Promotion rejected: Run breached risk gate.", auditReference: "audit-reject-1", approvedBy: "operator-7" }),
    getPromotionHistory: vi.fn().mockResolvedValue([{
      promotionId: "promo-1",
      strategyId: "strat-1",
      strategyName: "S1",
      sourceRunType: "backtest",
      targetRunType: "paper",
      runId: "run-1",
      sourceRunId: "run-1",
      targetRunId: "paper-1",
      decision: "Approved",
      approvedBy: "operator-7",
      approvalReason: "Meets risk constraints",
      reviewNotes: "Checked replay consistency",
      auditReference: "audit-promo-1",
      manualOverrideId: "override-9",
      qualifyingSharpe: 1.2,
      qualifyingMaxDrawdownPercent: 5,
      qualifyingTotalReturn: 10,
      promotedAt: "2026-01-01"
    }]),
    pauseStrategy: vi.fn().mockResolvedValue({ strategyId: "s", action: "pause", success: true, reason: null }),
    stopStrategy: vi.fn().mockResolvedValue({ strategyId: "s", action: "stop", success: true, reason: null })
  };
});

const data: TradingWorkspaceResponse = {
  metrics: [
    { id: "m1", label: "Net P&L", value: "+$3,100", delta: "+2.1%", tone: "success" },
    { id: "m2", label: "Open Orders", value: "4", delta: "+1", tone: "default" },
    { id: "m3", label: "Fills", value: "13", delta: "+3", tone: "success" },
    { id: "m4", label: "Risk", value: "Observe", delta: "0%", tone: "warning" }
  ],
  positions: [{ symbol: "AAPL", side: "Long", quantity: "100", averagePrice: "188.10", markPrice: "189.00", dayPnl: "+$90", unrealizedPnl: "+$90", exposure: "$18,900" }],
  openOrders: [{ orderId: "PO-1", symbol: "MSFT", side: "Buy", type: "Limit", quantity: "20", limitPrice: "414.20", status: "Working", submittedAt: "09:42:00 ET" }],
  fills: [{ fillId: "FL-1", orderId: "PO-0", symbol: "NVDA", side: "Sell", quantity: "10", price: "948.20", venue: "NASDAQ", timestamp: "09:40:10 ET" }],
  risk: { state: "Observe", summary: "Guardrails are active.", netExposure: "$120,000", grossExposure: "$150,000", var95: "$9,000", maxDrawdown: "-1.1%", buyingPowerUsed: "58%", activeGuardrails: ["Cap per single-name", "Throttle at 70%"] },
  brokerage: { provider: "Interactive Brokers", account: "DU1009034", environment: "paper", connection: "Connected", lastHeartbeat: "2s ago", orderIngress: "healthy", fillFeed: "healthy", notes: "Adapter is wired." }
};

const serverReadinessData: TradingWorkspaceResponse = {
  ...data,
  readiness: {
    asOf: "2026-04-26T16:00:00Z",
    overallStatus: "Blocked",
    readyForPaperOperation: false,
    acceptanceGates: [
      {
        gateId: "session",
        label: "Session active",
        status: "Ready",
        detail: "Active paper session sess-1 retains 1 order and 0 positions.",
        sessionId: "sess-1",
        runId: null,
        auditReference: null
      },
      {
        gateId: "replay",
        label: "Replay verified",
        status: "Blocked",
        detail: "Replay mismatch in server gate.",
        sessionId: "sess-1",
        runId: null,
        auditReference: "audit-replay-1"
      },
      {
        gateId: "audit-controls",
        label: "Risk state explainable",
        status: "ReviewRequired",
        detail: "One manual override requires review.",
        sessionId: null,
        runId: null,
        auditReference: null
      },
      {
        gateId: "promotion",
        label: "Promotion trace complete",
        status: "ReviewRequired",
        detail: "Promotion evidence is missing checklist coverage.",
        sessionId: null,
        runId: "run-1",
        auditReference: null
      },
      {
        gateId: "dk1-trust",
        label: "DK1 trust gate",
        status: "Ready",
        detail: "DK1 operator sign-off is complete.",
        sessionId: null,
        runId: null,
        auditReference: "artifacts/provider-validation/dk1-pilot-parity-packet.json"
      }
    ],
    activeSession: {
      sessionId: "sess-1",
      strategyId: "strat-1",
      strategyName: null,
      isActive: true,
      initialCash: 100000,
      createdAt: "2026-01-01T00:00:00Z",
      closedAt: null,
      symbolCount: 2,
      orderCount: 1,
      positionCount: 0,
      portfolioValue: 100000
    },
    sessions: [],
    replay: null,
    controls: {
      circuitBreakerOpen: false,
      circuitBreakerReason: null,
      circuitBreakerChangedBy: null,
      circuitBreakerChangedAt: null,
      manualOverrideCount: 1,
      symbolLimitCount: 1,
      defaultMaxPositionSize: 5000
    },
    promotion: null,
    trustGate: {
      gateId: "DK1",
      status: "ready-for-operator-review",
      readyForOperatorReview: true,
      operatorSignoffRequired: true,
      operatorSignoffStatus: "signed",
      generatedAt: "2026-04-26T15:55:00Z",
      packetPath: "artifacts/provider-validation/dk1-pilot-parity-packet.json",
      sourceSummary: null,
      requiredSampleCount: 4,
      readySampleCount: 4,
      validatedEvidenceDocumentCount: 4,
      requiredOwners: ["Data Operations", "Provider Reliability", "Trading"],
      blockers: [],
      detail: "DK1 operator sign-off is complete.",
      operatorSignoff: {
        status: "signed",
        requiredBeforeDk1Exit: true,
        requiredOwners: ["Data Operations", "Provider Reliability", "Trading"],
        signedOwners: ["Data Operations", "Provider Reliability", "Trading"],
        missingOwners: [],
        completedAt: "2026-04-26T16:00:00Z",
        sourcePath: "artifacts/provider-validation/dk1-operator-signoff.json"
      }
    },
    brokerageSync: null,
    workItems: [
      {
        workItemId: "execution-evidence-incomplete",
        kind: "ExecutionControl",
        label: "Execution evidence incomplete",
        detail: "OrderRejected is missing actor, scope, and rationale.",
        tone: "Warning",
        createdAt: "2026-04-26T16:00:00Z",
        runId: null,
        fundAccountId: null,
        auditReference: "audit-risk-missing-context",
        workspace: "Trading",
        targetRoute: "/api/workstation/trading/readiness",
        targetPageTag: "TradingShell"
      }
    ],
    warnings: ["Replay evidence is stale for sess-1."]
  }
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getTradingReadiness).mockResolvedValue(serverReadinessData.readiness!);
});

async function renderTradingScreen(
  screenData: TradingWorkspaceResponse = data,
  initialEntry = "/trading"
) {
  const result = renderWithRouter(<TradingScreen data={screenData} />, { initialEntries: [initialEntry] });
  await waitForAsyncEffects();
  return result;
}

describe("TradingScreen", () => {
  it("renders cockpit tables and wiring state", async () => {
    await renderTradingScreen();
    expect(screen.getByText("Live positions")).toBeInTheDocument();
    expect(screen.getByText("Session replay controls")).toBeInTheDocument();
    expect(screen.getByText("Backtest → Paper promotion gate")).toBeInTheDocument();
  });

  it("fetches and renders execution controls snapshot", async () => {
    await renderTradingScreen();
    await waitFor(() => expect(api.getExecutionControls).toHaveBeenCalled());
    expect(screen.getByText(/Execution controls snapshot/i)).toBeInTheDocument();
    expect(screen.getAllByText(/Breaker Closed/i).length).toBeGreaterThan(0);
    const controls = screen.getByLabelText(/Execution controls snapshot: breaker closed/i);
    expect(within(controls).getByText("5000")).toBeInTheDocument();
    expect(within(controls).getByText("BypassOrderControls (AAPL)")).toBeInTheDocument();
  });

  it("surfaces cockpit readiness against operator acceptance gates", async () => {
    await renderTradingScreen();

    expect(screen.getByText("Paper cockpit readiness")).toBeInTheDocument();
    await screen.findByText("2/4 ready");
    expect(screen.getByText("Restore required")).toBeInTheDocument();
    expect(screen.getByText("Verify required")).toBeInTheDocument();
    expect(screen.getByText(/recent execution audit entry is visible/i)).toBeInTheDocument();
    expect(screen.getByText(/Approved by operator-7: Meets risk constraints\. Audit audit-promo-1\./i)).toBeInTheDocument();
  });

  it("uses server acceptance gates when the readiness contract provides them", async () => {
    await renderTradingScreen(serverReadinessData);

    expect(screen.getByText("2/5 ready")).toBeInTheDocument();
    expect(screen.getByText("Overall: Blocked")).toBeInTheDocument();
    expect(screen.getByText("Paper: Not paper ready")).toBeInTheDocument();
    expect(screen.getByText("Brokerage: No account sync")).toBeInTheDocument();
    expect(screen.getByText("As of: 2026-04-26T16:00:00Z")).toBeInTheDocument();
    expect(screen.getByText("Replay verified")).toBeInTheDocument();
    expect(screen.getByText("Blocked")).toBeInTheDocument();
    expect(screen.getByText("Replay mismatch in server gate.")).toBeInTheDocument();
    expect(screen.getByText("DK1 trust gate")).toBeInTheDocument();
    expect(screen.getAllByText("Review required")).toHaveLength(2);
    expect(screen.getByText("Operator work items")).toBeInTheDocument();
    expect(screen.getByText("Execution evidence incomplete")).toBeInTheDocument();
    expect(screen.getByText("OrderRejected is missing actor, scope, and rationale.")).toBeInTheDocument();
    expect(screen.getByText("Replay evidence is stale for sess-1.")).toBeInTheDocument();
  });

  it("refreshes shared readiness and surfaces account brokerage posture", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getTradingReadiness).mockResolvedValueOnce({
      ...serverReadinessData.readiness!,
      asOf: "2026-04-26T16:05:00Z",
      overallStatus: "ReviewRequired",
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
    });

    await renderTradingScreen(serverReadinessData);
    await user.click(screen.getByRole("button", { name: /refresh trading readiness/i }));

    await waitFor(() => expect(api.getTradingReadiness).toHaveBeenCalledTimes(1));
    expect(screen.getByText("Overall: Review required")).toBeInTheDocument();
    expect(screen.getByText("Brokerage: Failed stale")).toBeInTheDocument();
    expect(screen.getByText("As of: 2026-04-26T16:05:00Z")).toBeInTheDocument();
    expect(screen.getByText("Brokerage sync failed")).toBeInTheDocument();
    expect(screen.getByText("Sync broker credentials before paper operation.")).toBeInTheDocument();
    expect(screen.getByText("Portfolio snapshot failed.")).toBeInTheDocument();
  });

  it("handles promotion happy path", async () => {
    const user = userEvent.setup();
    await renderTradingScreen();
    await waitFor(() => expect(api.getExecutionControls).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(api.getPromotionHistory).toHaveBeenCalledTimes(1));
    vi.mocked(api.getPromotionHistory).mockClear();
    await user.type(screen.getByLabelText("Run id"), "run-1");
    await user.type(screen.getByLabelText("Operator id"), "operator-7");
    await user.type(screen.getByLabelText("Approval reason"), "Meets risk constraints");
    await user.type(screen.getByLabelText("Review notes"), "Checked replay consistency");
    await user.type(screen.getByLabelText("Manual override id"), "override-9");
    await user.click(screen.getByRole("button", { name: /evaluate gate checks/i }));
    await screen.findByText(/Eligible: Yes/i);
    await user.click(screen.getByRole("button", { name: /confirm promote/i }));
    await screen.findByText(/Promoted\. Promotion ID: promo-1/i);
    expect(api.approvePromotion).toHaveBeenCalledWith({
      runId: "run-1",
      approvedBy: "operator-7",
      approvalReason: "Meets risk constraints",
      reviewNotes: "Checked replay consistency",
      manualOverrideId: "override-9"
    });
    await waitFor(() => expect(api.getPromotionHistory).toHaveBeenCalledTimes(1));
    await screen.findByText(/by operator-7/i);
    await screen.findByText(/reason: Meets risk constraints/i);
    await screen.findByText(/audit: audit-promo-1/i);
    await screen.findByText(/override: override-9/i);
    await screen.findByText(/notes: Checked replay consistency/i);
  });

  it("refreshes execution controls after control-affecting actions", async () => {
    const user = userEvent.setup();
    await renderTradingScreen();
    await waitFor(() => expect(api.getExecutionControls).toHaveBeenCalledTimes(1));

    await user.click(screen.getByRole("button", { name: /new order/i }));
    await user.type(screen.getByPlaceholderText("AAPL"), "AAPL");
    const quantityInput = screen.getAllByRole("spinbutton")[0];
    await user.clear(quantityInput);
    await user.type(quantityInput, "10");
    await user.click(screen.getByRole("button", { name: /submit order/i }));

    await waitFor(() => expect(api.getExecutionControls).toHaveBeenCalledTimes(2));
  });

  it("shows error path when promotion evaluation fails", async () => {
    vi.mocked(api.evaluatePromotion).mockRejectedValueOnce(new Error("eval failed"));
    const user = userEvent.setup();
    await renderTradingScreen();
    await user.type(screen.getByLabelText("Run id"), "run-bad");
    await user.click(screen.getByRole("button", { name: /evaluate gate checks/i }));
    await screen.findByText("eval failed");
  });

  it("supports rejecting a promotion with a required rationale", async () => {
    const user = userEvent.setup();
    await renderTradingScreen();
    await screen.findByText(/reason: Meets risk constraints/i);
    vi.mocked(api.getPromotionHistory).mockClear();
    vi.mocked(api.getPromotionHistory).mockResolvedValueOnce([{
      promotionId: "promo-reject-1",
      strategyId: "strat-1",
      strategyName: "S1",
      sourceRunType: "backtest",
      targetRunType: "paper",
      runId: "run-1",
      sourceRunId: "run-1",
      targetRunId: null,
      decision: "Rejected",
      approvedBy: "operator-7",
      approvalReason: "Risk review failed on drawdown stability.",
      reviewNotes: null,
      auditReference: "audit-reject-1",
      manualOverrideId: null,
      qualifyingSharpe: 1.2,
      qualifyingMaxDrawdownPercent: 5,
      qualifyingTotalReturn: 10,
      promotedAt: "2026-01-01T01:00:00Z"
    }]);

    await user.type(screen.getByLabelText("Run id"), "run-1");
    await user.type(screen.getByLabelText("Operator id"), "operator-7");
    await user.type(screen.getByLabelText("Rejection reason"), "Risk review failed on drawdown stability.");
    await user.click(screen.getByRole("button", { name: /reject promotion/i }));

    const rejectionStatus = await screen.findByText(/Promotion rejected: Run breached risk gate\./i);
    expect(rejectionStatus).toHaveClass("text-warning");
    expect(rejectionStatus).not.toHaveClass("text-success");
    expect(api.rejectPromotion).toHaveBeenCalledWith({
      runId: "run-1",
      reason: "Risk review failed on drawdown stability.",
      rejectedBy: "operator-7",
      reviewNotes: undefined,
      manualOverrideId: undefined
    });
    await waitFor(() => expect(api.getPromotionHistory).toHaveBeenCalledTimes(1));
    await screen.findByText(/reason: Risk review failed on drawdown stability\./i);
    await screen.findByText(/audit: audit-reject-1/i);
    expect(screen.getByLabelText("Rejection reason")).toHaveValue("");
  });

  it("renders unsuccessful rejection responses as errors", async () => {
    vi.mocked(api.rejectPromotion).mockResolvedValueOnce({
      success: false,
      promotionId: null,
      newRunId: null,
      reason: "Run not found or has no metrics available for rejection trace."
    });
    const user = userEvent.setup();
    await renderTradingScreen();
    await screen.findByText(/reason: Meets risk constraints/i);
    vi.mocked(api.getPromotionHistory).mockClear();

    await user.type(screen.getByLabelText("Run id"), "missing-run");
    await user.type(screen.getByLabelText("Operator id"), "operator-7");
    await user.type(screen.getByLabelText("Rejection reason"), "Risk review failed on drawdown stability.");
    await user.click(screen.getByRole("button", { name: /reject promotion/i }));

    const rejectionStatus = await screen.findByText(/Run not found or has no metrics available for rejection trace\./i);
    expect(rejectionStatus).toHaveClass("text-destructive");
    expect(rejectionStatus).not.toHaveClass("text-success");
    await waitFor(() => expect(api.getPromotionHistory).toHaveBeenCalledTimes(1));
    expect(screen.getByLabelText("Rejection reason")).toHaveValue("Risk review failed on drawdown stability.");
  });

  it("supports replay start and restore session for reconnect/resume workflows", async () => {
    const user = userEvent.setup();
    await renderTradingScreen();

    const startButton = await screen.findByRole("button", { name: "Start" });
    await waitFor(() => expect(startButton).toBeEnabled());
    await user.click(startButton);
    await screen.findByText("Replay running · 3/10 (30%)");

    await user.click(screen.getByRole("button", { name: /restore paper session sess-1/i }));
    await waitFor(() => expect(api.getPaperSessionDetail).toHaveBeenCalledWith("sess-1"));
    expect(screen.getByText(/Selected session: sess-1/i)).toBeInTheDocument();
    expect(screen.getByText("AAPL, MSFT")).toBeInTheDocument();
  });

  it("shows replay verification and execution audit for the selected session", async () => {
    const user = userEvent.setup();
    vi.mocked(api.getTradingReadiness).mockResolvedValueOnce(null);
    await renderTradingScreen();

    await user.click(await screen.findByRole("button", { name: /verify replay/i }));

    await waitFor(() => expect(api.getPaperSessionReplayVerification).toHaveBeenCalledWith("sess-1"));
    expect(screen.getByRole("status", { name: /replay verification matched current state for sess-1/i })).toHaveTextContent(/Matched current state/i);
    expect(screen.getByText(/Compared fills: 1/i)).toBeInTheDocument();
    expect(screen.getByText(/Verification audit: audit-verify-1/i)).toBeInTheDocument();
    expect(screen.getByText(/ReplayPaperSession/i)).toBeInTheDocument();
    await screen.findByText("4/4 ready");
  });

  it("opens confirmation dialog when Cancel order button is clicked", async () => {
    await renderTradingScreen();
    fireEvent.click(screen.getByTitle("Cancel order"));
    const dialog = screen.getByRole("dialog", { name: /cancel order PO-1/i });
    expect(dialog).toHaveAccessibleDescription("This will request cancellation of the selected order. Partial fills that already occurred are not reversed.");
    expect(screen.getByRole("button", { name: /confirm cancel order po-1/i })).toBeEnabled();
  });
});
