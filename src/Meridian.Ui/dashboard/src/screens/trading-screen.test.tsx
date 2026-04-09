import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { TradingScreen } from "@/screens/trading-screen";
import * as api from "@/lib/api";
import type { TradingRunDrillIn, TradingWorkspaceResponse } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    cancelOrder: vi.fn().mockResolvedValue({ actionId: "a1", status: "Completed", message: "ok", occurredAt: new Date().toISOString() }),
    cancelAllOrders: vi.fn().mockResolvedValue({ actionId: "a2", status: "Completed", message: "ok", occurredAt: new Date().toISOString() }),
    closePosition: vi.fn().mockResolvedValue({ actionId: "a3", status: "Accepted", message: "ok", occurredAt: new Date().toISOString() }),
    submitOrder: vi.fn().mockResolvedValue({ success: true, orderId: "O-1", reason: null }),
    getExecutionSessions: vi.fn().mockResolvedValue([{ sessionId: "sess-1", strategyId: "strat-1", strategyName: null, status: "Active", initialCash: 100000, createdAt: "2026-01-01" }]),
    createPaperSession: vi.fn(),
    closePaperSession: vi.fn().mockResolvedValue(undefined),
    getPaperSessionDetail: vi.fn().mockResolvedValue({ sessionId: "sess-1", strategyId: "strat-1", strategyName: null, status: "Active", initialCash: 100000, createdAt: "2026-01-01", closedAt: null }),
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
    getPromotionHistory: vi.fn().mockResolvedValue([{ promotionId: "promo-1", strategyId: "strat-1", strategyName: "S1", sourceRunType: "backtest", targetRunType: "paper", qualifyingSharpe: 1.2, qualifyingMaxDrawdownPercent: 5, qualifyingTotalReturn: 10, promotedAt: "2026-01-01" }]),
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
  positions: [{
    symbol: "AAPL",
    side: "Long",
    quantity: "100",
    averagePrice: "188.10",
    markPrice: "189.00",
    dayPnl: "+$90",
    unrealizedPnl: "+$90",
    exposure: "$18,900",
    security: {
      securityId: "security-aapl",
      displayName: "Apple Inc.",
      assetClass: "Equity",
      subType: "CommonShare",
      currency: "USD",
      status: "Active",
      primaryIdentifier: "AAPL",
      coverageStatus: "Partial",
      matchedIdentifierKind: "Ticker",
      matchedIdentifierValue: "AAPL",
      matchedProvider: "Polygon",
      resolutionReason: "Matched by ticker only; upstream provider identifier family is missing."
    },
    securityDetailUrl: "/workstation/governance/security-master?securityId=security-aapl"
  }],
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

  it("renders inline security master coverage for trading positions", () => {
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);

    expect(screen.getByText("Apple Inc.")).toBeInTheDocument();
    expect(screen.getByText("Partial")).toBeInTheDocument();
    expect(screen.getByText("Matched by ticker only; upstream provider identifier family is missing.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open security master/i })).toHaveAttribute(
      "href",
      "/workstation/governance/security-master?securityId=security-aapl"
    );
  });

  it("handles promotion happy path", async () => {
    const user = userEvent.setup();
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);
    await user.type(screen.getByLabelText("Run id"), "run-1");
    await user.click(screen.getByRole("button", { name: /evaluate gate checks/i }));
    await screen.findByText(/Eligible: Yes/i);
    await user.click(screen.getByRole("button", { name: /confirm promote/i }));
    await screen.findByText(/Promoted\. Promotion ID: promo-1/i);
  });

  it("shows error path when promotion evaluation fails", async () => {
    vi.mocked(api.evaluatePromotion).mockRejectedValueOnce(new Error("eval failed"));
    const user = userEvent.setup();
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);
    await user.type(screen.getByLabelText("Run id"), "run-bad");
    await user.click(screen.getByRole("button", { name: /evaluate gate checks/i }));
    await screen.findByText("eval failed");
  });

  it("supports replay start and restore session for reconnect/resume workflows", async () => {
    const user = userEvent.setup();
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);

    await user.click(screen.getByRole("button", { name: "Start" }));
    await screen.findByText(/Replay running/i);

    await user.click(screen.getByRole("button", { name: "Restore" }));
    await waitFor(() => expect(api.getPaperSessionDetail).toHaveBeenCalledWith("sess-1"));
    expect(screen.getByText(/Active session: sess-1/i)).toBeInTheDocument();
  });

  it("opens confirmation dialog when Cancel order button is clicked", () => {
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);
    fireEvent.click(screen.getByTitle("Cancel order"));
    expect(screen.getByText(/cancel order PO-1/i)).toBeInTheDocument();
  });

  it("renders run mode comparisons panel when comparisons are present", () => {
    const drillIn: TradingRunDrillIn = {
      equityCurve: "/api/workstation/runs/bt-1/equity-curve",
      fills: "/api/workstation/runs/bt-1/fills",
      attribution: "/api/workstation/runs/bt-1/attribution",
      ledger: null,
      cashFlows: "/api/portfolio/bt-1/cash-flows",
      comparison: "/api/workstation/runs/compare"
    };
    const dataWithComparisons: TradingWorkspaceResponse = {
      ...data,
      comparisons: [
        {
          strategyName: "mean-reversion-fx",
          modes: [
            { runId: "bt-1", mode: "backtest", status: "Completed", netPnl: 14500, totalReturn: 14.5, drillIn },
            { runId: "pp-1", mode: "paper", status: "Running", netPnl: 3200, totalReturn: 3.2, drillIn: { ...drillIn, equityCurve: "/api/workstation/runs/pp-1/equity-curve" } }
          ]
        }
      ]
    };

    render(
      <MemoryRouter initialEntries={["/trading"]}>
        <TradingScreen data={dataWithComparisons} />
      </MemoryRouter>
    );

    expect(screen.getByText("Strategy run comparisons")).toBeInTheDocument();
    expect(screen.getByText("mean-reversion-fx")).toBeInTheDocument();
    expect(screen.getByText("bt-1")).toBeInTheDocument();
    expect(screen.getByText("pp-1")).toBeInTheDocument();
    expect(screen.getByText("+$14,500")).toBeInTheDocument();
    expect(screen.getByText("+$3,200")).toBeInTheDocument();
  });

  it("does not render run mode comparisons panel when comparisons are absent", () => {
    render(<MemoryRouter initialEntries={["/trading"]}><TradingScreen data={data} /></MemoryRouter>);
    expect(screen.queryByText("Strategy run comparisons")).not.toBeInTheDocument();
  });
});
