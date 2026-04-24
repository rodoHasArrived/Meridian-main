import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { TradingScreen } from "@/screens/trading-screen";
import * as api from "@/lib/api";
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
    pauseReplay: vi.fn().mockResolvedValue({}),
    resumeReplay: vi.fn().mockResolvedValue({}),
    stopReplay: vi.fn().mockResolvedValue({}),
    seekReplay: vi.fn().mockResolvedValue({}),
    setReplaySpeed: vi.fn().mockResolvedValue({}),
    evaluatePromotion: vi.fn().mockResolvedValue({ runId: "run-1", strategyId: "strat-1", strategyName: "S1", sourceMode: "backtest", targetMode: "paper", isEligible: true, sharpeRatio: 1.2, maxDrawdownPercent: 5, totalReturn: 10, reason: "Eligible", found: true, ready: true }),
    approvePromotion: vi.fn().mockResolvedValue({ success: true, promotionId: "promo-1", newRunId: "paper-1", reason: "approved" }),
    rejectPromotion: vi.fn().mockResolvedValue({ success: true, promotionId: null, newRunId: null, reason: "Run breached risk gate." }),
    getPromotionHistory: vi.fn().mockResolvedValue([{
      promotionId: "promo-1",
      strategyId: "strat-1",
      strategyName: "S1",
      sourceRunType: "backtest",
      targetRunType: "paper",
      runId: "run-1",
      approvedBy: "operator-7",
      approvalReason: "Meets risk constraints",
      reviewNotes: "Checked replay consistency",
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

describe("TradingScreen", () => {
  it("renders cockpit tables and wiring state", () => {
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);
    expect(screen.getByText("Live positions")).toBeInTheDocument();
    expect(screen.getByText("Session replay controls")).toBeInTheDocument();
    expect(screen.getByText("Backtest → Paper promotion gate")).toBeInTheDocument();
  });

  it("fetches and renders execution controls snapshot", async () => {
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);
    await waitFor(() => expect(api.getExecutionControls).toHaveBeenCalled());
    expect(screen.getByText(/Execution controls snapshot/i)).toBeInTheDocument();
    expect(screen.getByText(/Breaker Closed/i)).toBeInTheDocument();
    expect(screen.getByText(/Default limit:/i)).toHaveTextContent("5000");
    expect(screen.getByText(/Active overrides:/i)).toHaveTextContent("BypassOrderControls (AAPL)");
  });

  it("handles promotion happy path", async () => {
    const user = userEvent.setup();
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);
    await user.type(screen.getByLabelText("Run id"), "run-1");
    await user.type(screen.getByLabelText("Approved by"), "operator-7");
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
    await screen.findByText(/by operator-7/i);
    await screen.findByText(/reason: Meets risk constraints/i);
    await screen.findByText(/override: override-9/i);
    await screen.findByText(/notes: Checked replay consistency/i);
  });

  it("refreshes execution controls after control-affecting actions", async () => {
    const user = userEvent.setup();
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);
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
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);
    await user.type(screen.getByLabelText("Run id"), "run-bad");
    await user.click(screen.getByRole("button", { name: /evaluate gate checks/i }));
    await screen.findByText("eval failed");
  });

  it("supports rejecting a promotion with a required rationale", async () => {
    const user = userEvent.setup();
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);

    await user.type(screen.getByLabelText("Run id"), "run-1");
    await user.type(screen.getByLabelText("Rejection reason"), "Risk review failed on drawdown stability.");
    await user.click(screen.getByRole("button", { name: /reject promotion/i }));

    await screen.findByText(/Promotion rejected: Run breached risk gate\./i);
    expect(api.rejectPromotion).toHaveBeenCalledWith("run-1", "Risk review failed on drawdown stability.");
  });

  it("supports replay start and restore session for reconnect/resume workflows", async () => {
    const user = userEvent.setup();
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);

    await user.click(screen.getByRole("button", { name: "Start" }));
    await screen.findByText(/Replay running/i);

    await user.click(screen.getByRole("button", { name: "Restore" }));
    await waitFor(() => expect(api.getPaperSessionDetail).toHaveBeenCalledWith("sess-1"));
    expect(screen.getByText(/Selected session: sess-1/i)).toBeInTheDocument();
    expect(screen.getByText("AAPL, MSFT")).toBeInTheDocument();
  });

  it("shows replay verification and execution audit for the selected session", async () => {
    const user = userEvent.setup();
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);

    await user.click(screen.getByRole("button", { name: /verify replay/i }));

    await waitFor(() => expect(api.getPaperSessionReplayVerification).toHaveBeenCalledWith("sess-1"));
    expect(screen.getByText(/Matched current state/i)).toBeInTheDocument();
    expect(screen.getByText(/Compared fills: 1/i)).toBeInTheDocument();
    expect(screen.getByText(/Verification audit: audit-verify-1/i)).toBeInTheDocument();
    expect(screen.getByText(/ReplayPaperSession/i)).toBeInTheDocument();
  });

  it("opens confirmation dialog when Cancel order button is clicked", () => {
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);
    fireEvent.click(screen.getByTitle("Cancel order"));
    expect(screen.getByText(/cancel order PO-1/i)).toBeInTheDocument();
  });
});
