import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ServerOrderPreview } from "@/lib/api/execution-workflows.api";
import type { AuthorityDecisionSummary, OrderResult } from "@/types";
import { formatAuthorityRefusal, manualOrderEntryDisabledReason, orderOriginLabel } from "./trading-screen.order-authority";
import { buildTradingBlotterViewModel, useOrderTicketViewModel } from "./trading-screen.view-model";

const account = "8c6b0029-9422-4961-ad61-a0ab6cbb242d";
const liveReason = "Manual live orders are not available: they need a reviewed manual-intent authority contract.";

function preview(): ServerOrderPreview {
  return { fingerprint: "request-1", evaluatedAt: new Date().toISOString(), expiresAt: new Date(Date.now() + 30_000).toISOString(),
    canSubmit: true, requiresApproval: false, estimatedNotional: 100, estimatedFees: null,
    checks: [{ code: "TEST", status: "passed", message: "Current account evidence checked." }] };
}

function refusal(overrides: Partial<AuthorityDecisionSummary> = {}): AuthorityDecisionSummary {
  return { origin: "OperatorTicket", isAuthorized: false, reasonCode: "ACCOUNT_GRANT_STALE",
    reason: "The execution account grant is older than the configured maximum age.", checkpoint: "dispatch",
    readinessSnapshotVersion: null, promotionAuditReference: null, ...overrides };
}

describe("order authority presentation", () => {
  it("labels every order origin and marks orders without one", () => {
    expect(orderOriginLabel("OperatorTicket")).toBe("Ticket");
    expect(orderOriginLabel("PositionAction")).toBe("Position");
    expect(orderOriginLabel("EscalationRelease")).toBe("Release");
    expect(orderOriginLabel("StrategyRun")).toBe("Strategy");
    expect(orderOriginLabel("Unattributed")).toBe("Untyped");
    expect(orderOriginLabel(null)).toBe("—");
  });

  it("names the checkpoint, code and reason of an authority refusal", () => {
    expect(formatAuthorityRefusal(refusal())).toBe(
      "Order authority refused this order just before dispatch (ACCOUNT_GRANT_STALE): The execution account grant is older than the configured maximum age.");
    expect(formatAuthorityRefusal(refusal({ checkpoint: "preflight", reasonCode: null, reason: null })))
      .toBe("Order authority refused this order: The order's authority did not permit routing.");
  });

  it("leaves admitted orders and other gates' refusals to the plain error message", () => {
    expect(formatAuthorityRefusal(refusal({ isAuthorized: true }))).toBeNull();
    expect(formatAuthorityRefusal(null)).toBeNull();
  });

  it("only blocks manual entry when the server says it is unavailable", () => {
    expect(manualOrderEntryDisabledReason({ available: false, reasonCode: "MANUAL_LIVE_INTENT_CONTRACT_NOT_REVIEWED", reason: liveReason }))
      .toBe(liveReason);
    expect(manualOrderEntryDisabledReason({ available: true, reasonCode: null, reason: null })).toBeNull();
    expect(manualOrderEntryDisabledReason(null)).toBeNull();
  });
});

describe("order ticket under order authority", () => {
  it("disables submission with the server's reason on a live routing path", async () => {
    const services = { submitOrder: vi.fn(), previewOrder: vi.fn().mockResolvedValue(preview()) };
    const manualOrderEntry = { available: false, reasonCode: "MANUAL_LIVE_INTENT_CONTRACT_NOT_REVIEWED", reason: liveReason };
    const { result } = renderHook(() => useOrderTicketViewModel({ services, fundAccountId: account, manualOrderEntry }));
    act(() => { result.current.openTicket(); result.current.updateField("symbol", "SPY"); result.current.updateField("quantity", "1"); });
    await waitFor(() => expect(result.current.serverPreview?.canSubmit).toBe(true));
    act(() => result.current.setAcknowledged(true));

    expect(result.current.canSubmit).toBe(false);
    expect(result.current.submitDisabledReason).toBe(liveReason);
    expect(result.current.manualEntryDisabledReason).toBe(liveReason);
  });

  it("renders an authority refusal with its checkpoint and code", async () => {
    const refused: OrderResult = { success: false, orderId: "ticket-1", reason: null,
      errorMessage: "The execution account grant is older than the configured maximum age.", authorityDecision: refusal() };
    const services = { submitOrder: vi.fn().mockResolvedValue(refused), previewOrder: vi.fn().mockResolvedValue(preview()) };
    const { result } = renderHook(() => useOrderTicketViewModel({ services, fundAccountId: account }));
    act(() => { result.current.openTicket(); result.current.updateField("symbol", "SPY"); result.current.updateField("quantity", "1"); });
    await waitFor(() => expect(result.current.serverPreview?.canSubmit).toBe(true));
    act(() => result.current.setAcknowledged(true));
    await act(async () => { await result.current.submitOrder(); });

    expect(services.submitOrder).toHaveBeenCalledTimes(1);
    expect(result.current.errorText).toContain("Order authority refused this order just before dispatch (ACCOUNT_GRANT_STALE)");
  });
});

describe("order blotter origin", () => {
  it("shows who originated each open order", () => {
    const vm = buildTradingBlotterViewModel({
      data: {
        metrics: [], positions: [], fills: [],
        openOrders: [
          { orderId: "o-1", symbol: "SPY", side: "Buy", type: "Limit", quantity: "1", limitPrice: "500.00", status: "Working", submittedAt: "14:00:00 UTC", origin: "StrategyRun" },
          { orderId: "o-2", symbol: "QQQ", side: "Sell", type: "Market", quantity: "2", limitPrice: "—", status: "Working", submittedAt: "14:01:00 UTC" }
        ],
        risk: { state: "Healthy", summary: "", netExposure: "—", grossExposure: "—", var95: "—", maxDrawdown: "—", buyingPowerUsed: "—", activeGuardrails: [] },
        brokerage: { provider: "paper", account: "—", environment: "paper", connection: "Connected", lastHeartbeat: "—", orderIngress: "—", fillFeed: "—", notes: "" }
      }
    });

    expect(vm.orderRows.map((row) => row.origin)).toEqual(["Strategy", "—"]);
  });
});
