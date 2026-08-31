import { describe, expect, it } from "vitest";
import {
  buildExecutionBlotterMetrics,
  buildExecutionBlotterRow,
  buildExecutionProvenance,
  executionBlotterEmptyMessage
} from "@/screens/trading-screen.execution-blotter.view-model";
import type {
  ExecutionBlotterPosition,
  ExecutionBlotterSnapshot
} from "@/types/execution-blotter.types";

function position(overrides: Partial<ExecutionBlotterPosition> = {}): ExecutionBlotterPosition {
  return {
    positionKey: "pos-1",
    symbol: "SPY",
    underlyingSymbol: "SPY",
    productDescription: "SPDR S&P 500 ETF",
    tradeId: null,
    quantity: 120,
    averageCostBasis: 480.5,
    marketPrice: 502.25,
    marketValue: 60_270,
    unrealisedPnl: 2_610,
    realisedPnl: 0,
    assetClass: "Equity",
    side: "Long",
    supportsClose: true,
    supportsUpsize: true,
    ...overrides
  };
}

function snapshot(overrides: Partial<ExecutionBlotterSnapshot> = {}): ExecutionBlotterSnapshot {
  return {
    positions: [position()],
    isBrokerBacked: true,
    isLive: true,
    source: "Alpaca live account",
    statusMessage: "Book reconciled 12 seconds ago.",
    asOf: "2026-05-29T14:00:00Z",
    ...overrides
  };
}

describe("buildExecutionProvenance", () => {
  it("names a live broker book as such", () => {
    expect(buildExecutionProvenance(snapshot())).toMatchObject({
      label: "Broker book · live",
      tone: "success"
    });
  });

  it("marks a simulated book so paper rows are never read as the broker's", () => {
    const vm = buildExecutionProvenance(snapshot({ isBrokerBacked: false, isLive: false, source: "Paper simulator" }));
    expect(vm.label).toBe("Simulated book · not live");
    expect(vm.tone).toBe("warning");
    expect(vm.detail).toContain("Paper simulator");
  });

  it("refuses to attribute rows at all when the snapshot has not loaded", () => {
    expect(buildExecutionProvenance(null)).toMatchObject({ label: "Provenance unknown", tone: "warning" });
  });

  it("flags a broker book that is not live", () => {
    expect(buildExecutionProvenance(snapshot({ isLive: false })).tone).toBe("warning");
  });
});

describe("buildExecutionBlotterRow", () => {
  it("renders a position with a signed unrealised figure and its tone", () => {
    expect(buildExecutionBlotterRow(position())).toMatchObject({
      symbol: "SPY",
      side: "Long",
      quantity: "120",
      unrealisedTone: "success",
      canUpsize: true,
      contractDetail: null
    });
    expect(buildExecutionBlotterRow(position()).unrealisedPnl.startsWith("+")).toBe(true);
  });

  it("tones a losing position as danger", () => {
    expect(buildExecutionBlotterRow(position({ unrealisedPnl: -410 })).unrealisedTone).toBe("danger");
  });

  it("summarizes option contract terms when the position carries them", () => {
    const row = buildExecutionBlotterRow(position({ expiration: "2026-06-19", strike: 505, right: "Call" }));
    expect(row.contractDetail).toBe("exp 2026-06-19 · strike 505 · Call");
  });

  it("offers upsize only where the server said it is supported", () => {
    expect(buildExecutionBlotterRow(position({ supportsUpsize: false })).canUpsize).toBe(false);
    // Absent flag means not offered, rather than assumed allowed.
    expect(buildExecutionBlotterRow(position({ supportsUpsize: undefined })).canUpsize).toBe(false);
  });
});

describe("buildExecutionBlotterMetrics", () => {
  it("reports gateway availability and account figures", () => {
    const metrics = buildExecutionBlotterMetrics(
      { brokerName: "Alpaca", mode: "Live", isAvailable: true, asOf: "2026-05-29T14:00:00Z", selectedGatewayId: "alpaca-1" },
      { cash: 25_000, portfolioValue: 85_270, unrealisedPnl: 2_610, realisedPnl: -140, positionCount: 3, asOf: "2026-05-29T14:00:00Z" }
    );

    expect(metrics.find((metric) => metric.id === "gateway")).toMatchObject({ value: "Alpaca", tone: "success" });
    expect(metrics.find((metric) => metric.id === "gateway")?.detail).toContain("alpaca-1");
    expect(metrics.find((metric) => metric.id === "unrealised")?.tone).toBe("success");
    expect(metrics.find((metric) => metric.id === "positions")?.value).toBe("3");
  });

  it("escalates an unavailable gateway", () => {
    const metrics = buildExecutionBlotterMetrics(
      { brokerName: "Alpaca", mode: "Live", isAvailable: false, asOf: "2026-05-29T14:00:00Z" },
      null
    );
    expect(metrics.find((metric) => metric.id === "gateway")?.tone).toBe("danger");
  });

  it("shows an unloaded read as unknown rather than as zero", () => {
    const metrics = buildExecutionBlotterMetrics(null, null);
    expect(metrics.every((metric) => metric.value === "—")).toBe(true);
  });
});

describe("executionBlotterEmptyMessage", () => {
  it("separates an inactive host, a failed read, and an genuinely empty book", () => {
    expect(executionBlotterEmptyMessage("inactive")).toContain("not active");
    expect(executionBlotterEmptyMessage("error")).toContain("could not be read");
    expect(executionBlotterEmptyMessage("loading")).toContain("Loading");
    expect(executionBlotterEmptyMessage("ready")).toBe("No open positions in the execution book.");
  });
});
