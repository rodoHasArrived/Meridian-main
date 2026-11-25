import { describe, expect, it } from "vitest";
import { routeForOperatorWorkItem } from "@/app-shell.workflow-routing";
import { appendOperatingScopeToRoute, buildOperatingScopeFromSearch, carryOperatingScopeIntoSearch } from "@/app-shell.operating-scope";
import type { OperatorWorkItem } from "@/types";

function workItem(overrides: Partial<OperatorWorkItem> = {}): OperatorWorkItem {
  return {
    workItemId: "work-1", kind: "ReconciliationBreak", label: "Review cash variance", detail: "Review retained evidence.",
    tone: "Warning", createdAt: "2026-11-23T12:00:00Z", runId: null, fundAccountId: null, auditReference: null,
    ...overrides
  };
}

describe("operator attention routes", () => {
  it("retains record context and uses the source account instead of conflicting target or remembered scope", () => {
    const route = routeForOperatorWorkItem(workItem({
      fundAccountId: "account-b",
      targetRoute: "/accounting/reconciliation?fundAccountId=account-a&runId=run-b&breakId=break-b&periodId=2026-11#case-evidence"
    }));
    const target = new URL(route, "http://workstation.test");
    expect(target.pathname).toBe("/accounting/reconciliation");
    expect(target.searchParams.get("fundAccountId")).toBe("account-b");
    expect(target.searchParams.get("recordScope")).toBe("1");
    expect(target.searchParams.get("runId")).toBe("run-b");
    expect(target.searchParams.get("breakId")).toBe("break-b");
    expect(target.searchParams.get("periodId")).toBe("2026-11");
    expect(target.hash).toBe("#case-evidence");

    const remembered = { entityId: "entity-a", fundId: "fund-a", fundAccountId: "account-a", runId: "run-a" };
    expect(appendOperatingScopeToRoute(route, buildOperatingScopeFromSearch("", remembered))).toBe(route);
    expect(carryOperatingScopeIntoSearch(target.pathname, target.search, remembered)).toBe(target.search);
  });

  it.each([
    ["PaperReplay", "/trading#session-replay-panel"],
    ["PromotionReview", "/trading#promotion-gate-panel"],
    ["ExecutionControl", "/trading/risk"],
    ["BrokerExecutionReconciliation", "/trading#brokerage-recovery"]
  ] as const)("opens the actual %s task when no target is supplied", (kind, expected) => {
    expect(routeForOperatorWorkItem(workItem({ kind }))).toBe(expected);
  });

  it.each(["/trading", "/trading/", "/trading/readiness"])("repairs a generic or retired target %s without dropping record parameters", (target) => {
    const route = routeForOperatorWorkItem(workItem({
      kind: "PromotionReview", fundAccountId: "account-b", targetRoute: `${target}?runId=run-b&provider=Alpaca`
    }));
    const resolved = new URL(route, "http://workstation.test");
    expect(resolved.pathname).toBe("/trading");
    expect(resolved.hash).toBe("#promotion-gate-panel");
    expect(resolved.searchParams.get("runId")).toBe("run-b");
    expect(resolved.searchParams.get("provider")).toBe("Alpaca");
    expect(resolved.searchParams.get("fundAccountId")).toBe("account-b");
  });

  it("routes shared TradingShell metadata to the task's own view", () => {
    expect(routeForOperatorWorkItem(workItem({ kind: "ExecutionControl", targetPageTag: "TradingShell", workspace: "trading" })))
      .toBe("/trading/risk");
    expect(routeForOperatorWorkItem(workItem({ kind: "PaperReplay", targetPageTag: "TradingReadiness" })))
      .toBe("/trading#session-replay-panel");
  });

  it("preserves specific local targets and evidence anchors", () => {
    const targetRoute = "/trading/orders?orderId=order-b#order-evidence";
    expect(routeForOperatorWorkItem(workItem({ kind: "BrokerExecutionReconciliation", targetRoute }))).toBe(targetRoute);
    expect(routeForOperatorWorkItem(workItem({ kind: "PromotionReview", targetRoute: "/trading/readiness?runId=run-b#promotion-gate-panel" })))
      .toBe("/trading?runId=run-b#promotion-gate-panel");
    expect(routeForOperatorWorkItem(workItem({ kind: "PaperReplay", targetRoute: "/trading/readiness?runId=run-b#provider" })))
      .toBe("/trading?runId=run-b#session-replay-panel");
  });

  it("scopes provider setup links and URL-encodes source account values", () => {
    const route = routeForOperatorWorkItem(workItem({ kind: "BrokerageSync", fundAccountId: "account&view=other" }));
    const target = new URL(route, "http://workstation.test");
    expect(target.pathname).toBe("/settings");
    expect(target.hash).toBe("#alpaca-provider-setup");
    expect(target.searchParams.get("fundAccountId")).toBe("account&view=other");
    expect(target.searchParams.has("view")).toBe(false);
  });

  it("keeps absent source scope unset and rejects external or API destinations", () => {
    for (const targetRoute of ["https://outside.example/trading", "//outside.example/trading", "/api/workstation/inbox"]) {
      expect(routeForOperatorWorkItem(workItem({ targetRoute }))).toBe("/accounting/reconciliation");
    }
    expect(routeForOperatorWorkItem(workItem({ fundAccountId: "  " }))).toBe("/accounting/reconciliation");
  });
});
