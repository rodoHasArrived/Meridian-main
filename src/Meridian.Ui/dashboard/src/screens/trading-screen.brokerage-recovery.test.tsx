import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it, vi } from "vitest";
import { BrokerageRecoveryPanel } from "./trading-screen.brokerage-recovery";
import type { TradingBrokerageRecovery, TradingOperatorReadiness } from "@/types";

const accountId = "11111111-1111-1111-1111-111111111111";
const recovery: TradingBrokerageRecovery = {
  fundAccountId: accountId, providerId: "alpaca", externalAccountId: "PA-404", status: "Ready",
  detail: "Portfolio and retained orders match broker evidence.", blockingReasons: [], affectedRuns: [],
  portfolio: {
    cash: 1250, buyingPower: 2500, portfolioValue: 6250, currency: "USD", positionCount: 2,
    observedAt: "2026-10-05T15:30:00Z", lastAttemptedAt: "2026-10-05T15:30:01Z", lastSuccessfulAt: "2026-10-05T15:30:02Z",
    isComplete: true, isFresh: true, isConsistent: true, warnings: []
  }
};

function readiness(overrides: Partial<TradingOperatorReadiness> = {}): TradingOperatorReadiness {
  return {
    asOf: "2026-10-05T15:30:03Z", overallStatus: "Blocked", readyForPaperOperation: true, acceptanceGates: [],
    activeSession: null, sessions: [], replay: null, promotion: null, brokerageSync: null, workItems: [], warnings: [],
    controls: { circuitBreakerOpen: false, circuitBreakerReason: null, circuitBreakerChangedBy: null, circuitBreakerChangedAt: null, manualOverrideCount: 0, symbolLimitCount: 0, defaultMaxPositionSize: null },
    trustGate: { gateId: "trust", status: "pending", readyForOperatorReview: false, operatorSignoffRequired: true, operatorSignoffStatus: "pending", operatorSignoff: null, generatedAt: null, packetPath: null, sourceSummary: null, requiredSampleCount: 0, readySampleCount: 0, validatedEvidenceDocumentCount: 0, requiredOwners: [], blockers: [], detail: "Pending" },
    brokerageRecovery: recovery,
    executionReconciliation: { status: "Ready", gatewayId: "alpaca", brokerDisplayName: "Alpaca", brokerHealthy: true, brokerConnected: true, matchedOpenOrderCount: 1, breakCount: 0, reconciledAt: "2026-10-05T15:30:03Z", detail: "Matched", breaks: [] },
    ...overrides
  };
}

function panel(data: TradingOperatorReadiness | null, extra = {}) {
  return <BrokerageRecoveryPanel readiness={data} fundAccountId={accountId} busy={false} error={null} onRecover={vi.fn().mockResolvedValue(undefined)} {...extra} />;
}

describe("BrokerageRecoveryPanel", () => {
  it("shows scoped health, timestamp, amounts and currency without claiming overall live readiness", async () => {
    const { container } = render(panel(readiness()));
    expect(screen.getByRole("status")).toHaveTextContent("Brokerage evidence reconciled");
    expect(screen.getByText("Connected · healthy")).toBeInTheDocument();
    expect(screen.getByText("$1,250.00 (USD)")).toBeInTheDocument();
    expect(screen.getByText("$2,500.00 (USD)")).toBeInTheDocument();
    expect(screen.getByText("2026-10-05 15:30:02 UTC")).toBeInTheDocument();
    expect(screen.getByText(/Live operation also requires all trading readiness gates/)).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });

  it.each(["isComplete", "isFresh", "isConsistent"] as const)("blocks %s evidence even with a Ready server label", (field) => {
    render(panel(readiness({ brokerageRecovery: { ...recovery, portfolio: { ...recovery.portfolio!, [field]: false } } })));
    expect(screen.getByRole("status")).toHaveTextContent("Live trading blocked");
    expect(screen.queryByText("No unresolved order discrepancies.")).not.toBeInTheDocument();
  });

  it("retains local and broker order identities, partial-fill differences and impacted runs while disconnected", () => {
    const state = readiness();
    render(panel({ ...state,
      brokerageRecovery: { ...recovery, status: "Blocked", blockingReasons: ["Fill recovery required"], affectedRuns: [{ runId: "run-7", strategyId: "momentum", status: "Paused", detail: "Exposure changed after reconnect." }] },
      executionReconciliation: { ...state.executionReconciliation!, status: "Blocked", brokerConnected: false, breakCount: 1, breaks: [{ kind: "FillMismatch", description: "Partial fill changed after disconnect", localOrderId: "local-1", brokerOrderId: "broker-1", clientOrderId: "client-1", symbol: "AAPL", localValue: "Filled 2 of 10", brokerValue: "Filled 6 of 10" }] }
    }));
    expect(screen.getByRole("status")).toHaveTextContent("Live trading blocked");
    expect(screen.getByText("Disconnected")).toBeInTheDocument();
    const table = screen.getByRole("table", { name: "Local and broker order discrepancies" });
    for (const value of ["local-1", "broker-1", "client-1", "Filled 2 of 10", "Filled 6 of 10"]) {
      expect(within(table).getByText(value)).toBeInTheDocument();
    }
    expect(within(screen.getByRole("table", { name: "Affected strategy runs" })).getByText("run-7")).toBeInTheDocument();
  });

  it("keeps failed or pending recovery blocked while retaining evidence for investigation", () => {
    const { rerender } = render(panel(readiness(), { error: "Connection lost" }));
    expect(screen.getByRole("status")).toHaveTextContent("Synchronization unavailable: Connection lost");
    expect(screen.getByText("$1,250.00 (USD)")).toBeInTheDocument();
    rerender(panel(readiness(), { busy: true }));
    expect(screen.getByRole("status")).toHaveTextContent("Live trading blocked");
    expect(screen.getByRole("button", { name: "Reconciling…" })).toBeDisabled();
  });

  it("hides prior-account evidence and blocks missing recovery evidence", () => {
    const { rerender } = render(panel(readiness(), { fundAccountId: "22222222-2222-2222-2222-222222222222" }));
    expect(screen.getByRole("status")).toHaveTextContent("Current account recovery evidence is unavailable");
    expect(screen.queryByText("$1,250.00 (USD)")).not.toBeInTheDocument();
    expect(screen.queryByText(/PA-404/)).not.toBeInTheDocument();
    rerender(panel(null, { fundAccountId: undefined }));
    expect(screen.getByRole("button", { name: "Synchronize and reconcile" })).toBeDisabled();
    rerender(panel(readiness({ brokerageRecovery: { ...recovery, fundAccountId: null } })));
    expect(screen.getByRole("status")).toHaveTextContent("Current account recovery evidence is unavailable");
  });

  it("requests recovery once with a keyboard-operable command and clears blockage only after reconciled evidence arrives", async () => {
    const onRecover = vi.fn().mockResolvedValue(undefined);
    const { rerender } = render(panel(readiness({ brokerageRecovery: { ...recovery, status: "Blocked" } }), { onRecover }));
    const user = userEvent.setup();
    await user.tab();
    expect(screen.getByRole("button", { name: "Synchronize and reconcile" })).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(onRecover).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("status")).toHaveTextContent("Live trading blocked");
    rerender(panel(readiness(), { onRecover }));
    expect(screen.getByRole("status")).toHaveTextContent("Brokerage evidence reconciled");
  });
});
