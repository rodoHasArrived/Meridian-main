import { describe, expect, it, vi } from "vitest";
import { axe } from "jest-axe";
import { TradingScreen } from "@/screens/trading-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type { TradingWorkspaceResponse } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    getExecutionSessions: vi.fn().mockResolvedValue([{ sessionId: "sess-1", strategyId: "strat-1", strategyName: null, initialCash: 100000, createdAt: "2026-01-01", closedAt: null, isActive: true }]),
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
    getRiskRules: vi.fn().mockResolvedValue([
      {
        ruleName: "PositionLimit",
        state: "Healthy",
        summary: "No position breaches.",
        isBreached: false,
        threshold: "5000",
        currentValue: "2500",
        asOf: "2026-01-01T00:20:00Z",
        recentViolations: []
      }
    ]),
    getRiskRuleConfig: vi.fn().mockResolvedValue({
      ruleName: "DrawdownCircuitBreaker",
      defaultMaxPositionSize: null,
      symbolPositionLimits: null,
      maxDrawdownPercent: 5,
      maxOrdersPerMinute: null
    }),
    getReplayFiles: vi.fn().mockResolvedValue({ files: [{ path: "/tmp/replay.jsonl", name: "replay.jsonl", symbol: "AAPL", eventType: "trades", sizeBytes: 1, isCompressed: false, lastModified: "2026-01-01" }], total: 1, timestamp: "2026-01-01" }),
    getReplayStatus: vi.fn().mockResolvedValue({ sessionId: "rep-1", filePath: "/tmp/replay.jsonl", status: "running", speedMultiplier: 1, eventsProcessed: 3, totalEvents: 10, progressPercent: 30, startedAt: "2026-01-01" }),
    getTradingReadiness: vi.fn().mockResolvedValue(null),
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
    }])
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

describe("TradingScreen accessibility", () => {
  it.each([
    ["overview", "/trading"],
    ["orders", "/trading/orders"],
    ["positions", "/trading/positions"],
    ["risk", "/trading/risk"]
  ])("has no basic accessibility violations in the %s view", async (_view, route) => {
    const { container } = renderWithRouter(<TradingScreen data={data} />, { initialEntries: [route] });
    await waitForAsyncEffects();

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });
});
