import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { TestMemoryRouter } from "@/test/render";
import { describe, expect, it, vi } from "vitest";
import { ScopePickerOpenerProvider } from "@/lib/operating-scope/scope-picker-context";
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

// `brokerageEnvironment` defaults to undefined, so every test that does not set it stays on the
// fail-closed path (an unknown environment keeps the recovery block).
function panel(data: TradingOperatorReadiness | null, extra: Partial<Parameters<typeof BrokerageRecoveryPanel>[0]> = {}) {
  return <BrokerageRecoveryPanel readiness={data} fundAccountId={accountId} busy={false} error={null} onRecover={vi.fn().mockResolvedValue(undefined)} {...extra} />;
}

describe("BrokerageRecoveryPanel", () => {
  it("shows scoped health, timestamp, amounts and currency without claiming overall live readiness", async () => {
    const { container } = render(panel(readiness()));
    expect(screen.getByRole("status", { name: "Brokerage recovery readiness" })).toHaveTextContent("Brokerage evidence reconciled");
    expect(screen.getByText("Connected · healthy")).toBeInTheDocument();
    expect(screen.getByText("$1,250.00 (USD)")).toBeInTheDocument();
    expect(screen.getByText("$2,500.00 (USD)")).toBeInTheDocument();
    expect(screen.getByText("2026-10-05 15:30:02 UTC")).toBeInTheDocument();
    expect(screen.getByText(/Live operation also requires all trading readiness gates/)).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });

  it.each(["isComplete", "isFresh", "isConsistent"] as const)("blocks %s evidence even with a Ready server label", (field) => {
    render(panel(readiness({ brokerageRecovery: { ...recovery, portfolio: { ...recovery.portfolio!, [field]: false } } })));
    expect(screen.getByRole("status", { name: "Brokerage recovery readiness" })).toHaveTextContent("Live trading blocked");
    expect(screen.queryByText("No unresolved order discrepancies.")).not.toBeInTheDocument();
  });

  it("retains local and broker order identities, partial-fill differences and impacted runs while disconnected", () => {
    const state = readiness();
    render(panel({ ...state,
      brokerageRecovery: { ...recovery, status: "Blocked", blockingReasons: ["Fill recovery required"], affectedRuns: [{ runId: "run-7", strategyId: "momentum", status: "Paused", detail: "Exposure changed after reconnect." }] },
      executionReconciliation: { ...state.executionReconciliation!, status: "Blocked", brokerConnected: false, breakCount: 1, breaks: [{ kind: "FillMismatch", description: "Partial fill changed after disconnect", localOrderId: "local-1", brokerOrderId: "broker-1", clientOrderId: "client-1", symbol: "AAPL", localValue: "Filled 2 of 10", brokerValue: "Filled 6 of 10" }] }
    }));
    expect(screen.getByRole("status", { name: "Brokerage recovery readiness" })).toHaveTextContent("Live trading blocked");
    expect(screen.getByText("Disconnected")).toBeInTheDocument();
    const table = screen.getByRole("table", { name: "Local and broker order discrepancies" });
    for (const value of ["local-1", "broker-1", "client-1", "Filled 2 of 10", "Filled 6 of 10"]) {
      expect(within(table).getByText(value)).toBeInTheDocument();
    }
    expect(within(screen.getByRole("table", { name: "Affected strategy runs" })).getByText("run-7")).toBeInTheDocument();
  });

  it("keeps failed or pending recovery blocked while retaining evidence for investigation", () => {
    const { rerender } = render(panel(readiness(), { error: "Connection lost" }));
    expect(screen.getByRole("status", { name: "Brokerage recovery readiness" })).toHaveTextContent("Synchronization unavailable: Connection lost");
    expect(screen.getByText("$1,250.00 (USD)")).toBeInTheDocument();
    rerender(panel(readiness(), { busy: true }));
    expect(screen.getByRole("status", { name: "Brokerage recovery readiness" })).toHaveTextContent("Live trading blocked");
    expect(screen.getByRole("button", { name: "Reconciling…" })).toBeDisabled();
  });

  it("hides prior-account evidence and blocks missing recovery evidence", () => {
    const { rerender } = render(panel(readiness(), { fundAccountId: "22222222-2222-2222-2222-222222222222" }));
    expect(screen.getByRole("status", { name: "Brokerage recovery readiness" })).toHaveTextContent("Current account recovery evidence is unavailable");
    expect(screen.queryByText("$1,250.00 (USD)")).not.toBeInTheDocument();
    expect(screen.queryByText(/PA-404/)).not.toBeInTheDocument();
    // No evidence for the selected account: no grid of "Unavailable" facts, one next step.
    expect(screen.queryByText("Unavailable")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Synchronize and reconcile" })).toBeEnabled();
    rerender(panel(null, { fundAccountId: undefined }));
    expect(screen.queryByRole("button", { name: "Synchronize and reconcile" })).not.toBeInTheDocument();
    expect(screen.getByText("Choose a fund account")).toBeInTheDocument();
    rerender(panel(readiness({ brokerageRecovery: { ...recovery, fundAccountId: null } })));
    expect(screen.getByRole("status", { name: "Brokerage recovery readiness" })).toHaveTextContent("Current account recovery evidence is unavailable");
  });

  it("offers choosing an account as the single next step when none is in scope", async () => {
    const openScopePicker = vi.fn();
    const { container } = render(
      <ScopePickerOpenerProvider open={openScopePicker}>{panel(null, { fundAccountId: undefined, brokerageEnvironment: "paper" })}</ScopePickerOpenerProvider>
    );
    // Setup, not a failure: one neutral status that says live trading stays off.
    const status = screen.getByRole("status", { name: "Brokerage recovery readiness" });
    expect(status).toHaveTextContent("Choose a fund account");
    expect(status).toHaveTextContent("Live trading stays off until then.");
    expect(screen.getAllByRole("status", { name: "Brokerage recovery readiness" })).toHaveLength(1);
    expect(screen.queryByText(/recovery required/i)).not.toBeInTheDocument();
    expect(screen.queryByText("Unavailable")).not.toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    expect(screen.getAllByRole("button")).toHaveLength(1);
    await userEvent.setup().click(screen.getByRole("button", { name: "Choose account" }));
    expect(openScopePicker).toHaveBeenCalledTimes(1);
    expect((await axe(container)).violations).toEqual([]);
  });

  it.each([
    ["live", "live"],
    ["unknown", undefined],
    ["blank", "  "]
  ] as const)("keeps live trading blocked with no account when the environment is %s", (_, brokerageEnvironment) => {
    render(panel(null, { fundAccountId: undefined, brokerageEnvironment }));
    const status = screen.getByRole("status", { name: "Brokerage recovery readiness" });
    expect(status).toHaveTextContent("Live trading blocked");
    expect(screen.getAllByRole("status", { name: "Brokerage recovery readiness" })).toHaveLength(1);
    // The account is still the next step; only the banner above it stays red.
    expect(screen.getByText("Choose a fund account")).toBeInTheDocument();
  });

  it("treats a live environment case-insensitively", () => {
    render(panel(null, { fundAccountId: undefined, brokerageEnvironment: " LIVE " }));
    expect(screen.getByRole("status", { name: "Brokerage recovery readiness" })).toHaveTextContent("Live trading blocked");
  });

  it("keeps a synchronization error blocked in a paper environment with no account", () => {
    render(panel(null, { fundAccountId: undefined, brokerageEnvironment: "paper", error: "Connection lost" }));
    const status = screen.getByRole("status", { name: "Brokerage recovery readiness" });
    expect(status).toHaveTextContent("Live trading blocked");
    expect(status).toHaveTextContent("Synchronization unavailable: Connection lost");
  });

  it("keeps an account in scope on the recovery path in a paper environment", () => {
    render(panel(readiness({ brokerageRecovery: { ...recovery, status: "Blocked" } }), { brokerageEnvironment: "paper" }));
    expect(screen.getByRole("status", { name: "Brokerage recovery readiness" })).toHaveTextContent("Live trading blocked");
  });

  it("collapses to a neutral setup row with the account picker as its action", async () => {
    const openScopePicker = vi.fn();
    const { container } = render(
      <TestMemoryRouter>
        <ScopePickerOpenerProvider open={openScopePicker}>
          {panel(null, { fundAccountId: undefined, brokerageEnvironment: "paper", variant: "row", reviewHref: "/trading#brokerage-recovery" })}
        </ScopePickerOpenerProvider>
      </TestMemoryRouter>
    );
    const row = screen.getByRole("region", { name: "Brokerage recovery" });
    expect(row).not.toHaveClass("border-l-danger");
    expect(row).not.toHaveTextContent("Live trading blocked");
    expect(within(row).getByRole("status", { name: "Brokerage recovery readiness" })).toHaveTextContent("No fund account in scope");
    expect(within(row).queryByRole("link", { name: "Review brokerage recovery" })).not.toBeInTheDocument();
    await userEvent.setup().click(within(row).getByRole("button", { name: "Choose account" }));
    expect(openScopePicker).toHaveBeenCalledTimes(1);
    expect((await axe(container)).violations).toEqual([]);
  });

  it("keeps the Review link on the neutral setup row when no account picker is available", () => {
    render(
      <TestMemoryRouter>
        {panel(null, { fundAccountId: undefined, brokerageEnvironment: "paper", variant: "row", reviewHref: "/trading#brokerage-recovery" })}
      </TestMemoryRouter>
    );
    const row = screen.getByRole("region", { name: "Brokerage recovery" });
    expect(within(row).getByRole("status", { name: "Brokerage recovery readiness" }))
      .toHaveTextContent("No fund account in scope · brokerage recovery is checked per account; live trading stays off until one is chosen.");
    expect(row).not.toHaveTextContent("Live trading blocked");
    expect(within(row).queryByRole("button")).not.toBeInTheDocument();
    expect(within(row).getByRole("link", { name: "Review brokerage recovery" })).toHaveAttribute("href", "/trading#brokerage-recovery");
  });

  it("keeps the blocked row red for a live environment with no account", () => {
    render(
      <TestMemoryRouter>
        {panel(null, { fundAccountId: undefined, brokerageEnvironment: "live", variant: "row", reviewHref: "/trading#brokerage-recovery" })}
      </TestMemoryRouter>
    );
    const row = screen.getByRole("region", { name: "Brokerage recovery" });
    expect(row).toHaveClass("border-l-danger");
    expect(within(row).getByRole("status", { name: "Brokerage recovery readiness" })).toHaveTextContent("Live trading blocked — recovery required");
    expect(within(row).getByRole("link", { name: "Review brokerage recovery" })).toBeInTheDocument();
  });

  it("requests recovery once with a keyboard-operable command and clears blockage only after reconciled evidence arrives", async () => {
    const onRecover = vi.fn().mockResolvedValue(undefined);
    const { rerender } = render(panel(readiness({ brokerageRecovery: { ...recovery, status: "Blocked" } }), { onRecover }));
    const user = userEvent.setup();
    await user.tab();
    expect(screen.getByRole("button", { name: "Synchronize and reconcile" })).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(onRecover).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("status", { name: "Brokerage recovery readiness" })).toHaveTextContent("Live trading blocked");
    rerender(panel(readiness(), { onRecover }));
    expect(screen.getByRole("status", { name: "Brokerage recovery readiness" })).toHaveTextContent("Brokerage evidence reconciled");
  });
});
