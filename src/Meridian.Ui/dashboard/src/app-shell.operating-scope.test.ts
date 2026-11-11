import { describe, expect, it } from "vitest";
import {
  buildOperatingScopeFromSearch,
  appendOperatingScopeToRoute,
  operatingScopeDimensionsForRoute
} from "@/app-shell.operating-scope";

describe("operatingScopeDimensionsForRoute", () => {
  it("claims only dimensions consumed by each rendered route", () => {
    expect(operatingScopeDimensionsForRoute("/data/quotes?symbol=AAPL")).toEqual(["symbol"]);
    expect(operatingScopeDimensionsForRoute("/data/quotes?view=watchlist&symbol=AAPL")).toEqual([]);
    expect(operatingScopeDimensionsForRoute("/portfolio/cash-ladder")).toEqual(["fundAccountId"]);
    expect(operatingScopeDimensionsForRoute("/accounting/ledger")).toEqual([]);
    expect(operatingScopeDimensionsForRoute("/settings")).toEqual([]);
    expect(operatingScopeDimensionsForRoute("/strategy/promotions?runId=r1")).toEqual(["runId"]);
    expect(operatingScopeDimensionsForRoute("/reporting/runs/detail?runId=r1")).toEqual(["runId"]);
    expect(operatingScopeDimensionsForRoute("/portfolio/asset-detail?securityId=s1&symbol=AAPL")).toEqual([]);
    expect(operatingScopeDimensionsForRoute("/accounting/journal-entries/detail?periodId=p1&runId=r1")).toEqual([]);
    expect(operatingScopeDimensionsForRoute("/accounting/journal-entries/detail?runId=r1")).toEqual([]);
    expect(operatingScopeDimensionsForRoute("/accounting/operations-continuity?fundAccountId=a1")).toEqual([]);
    expect(operatingScopeDimensionsForRoute("/trading/orders")).toEqual(["fundAccountId"]);
    expect(operatingScopeDimensionsForRoute("/unknown")).toEqual([]);
  });

  it("keeps stored-only scope retained on screens that read only URL query values", () => {
    for (const route of ["/strategy/promotions", "/strategy/run-ledger", "/reporting/preview", "/accounting/close-calendar", "/data/quotes"]) {
      expect(operatingScopeDimensionsForRoute(route, { runId: "stored-run", symbol: "AAPL", fundAccountId: "stored-account" })).toEqual([]);
    }
    expect(operatingScopeDimensionsForRoute("/strategy/promotions?runId=route-run", { runId: "stored-run" })).toEqual([]);
    expect(operatingScopeDimensionsForRoute("/strategy/promotions?runId=route-run", { runId: "route-run" })).toEqual(["runId"]);
    expect(operatingScopeDimensionsForRoute("/data/quotes?symbol=aapl", { symbol: "AAPL" })).toEqual(["symbol"]);
    expect(operatingScopeDimensionsForRoute("/data/quotes?view=alerts&symbol=AAPL", { symbol: "AAPL" })).toEqual([]);
    expect(operatingScopeDimensionsForRoute("/portfolio/cash-ladder", { fundAccountId: "stored-account" })).toEqual(["fundAccountId"]);
  });

  it("retains carried context in navigation without claiming it is applied", () => {
    const scope = buildOperatingScopeFromSearch("?symbol=AAPL&provider=alpaca&runId=r1&from=2026-01-01&fundAccountId=a1");
    const route = appendOperatingScopeToRoute("/portfolio/cash-ladder", scope);
    expect(route).toContain("symbol=AAPL");
    expect(route).toContain("provider=alpaca");
    expect(route).toContain("runId=r1");
    expect(operatingScopeDimensionsForRoute(route)).toEqual(["fundAccountId"]);
  });

  it("retains unsupported Trading account identifiers instead of claiming a filter that the API drops", () => {
    expect(operatingScopeDimensionsForRoute("/trading/orders", { fundAccountId: "account-label" })).toEqual([]);
    expect(operatingScopeDimensionsForRoute("/trading/orders", { fundAccountId: "53bf0251-17f6-4fb7-8dbe-6fb4966e2749" })).toEqual(["fundAccountId"]);
  });

  it("keeps report-run identity in deep-link state without exposing the raw run ID in visible scope", () => {
    const scope = buildOperatingScopeFromSearch(
      "?runId=report-run-board-202605",
      null,
      "/reporting/run-status"
    );

    expect(scope.runId).toBe("report-run-board-202605");
    expect(scope.queryParams).toContainEqual({
      key: "runId",
      value: "report-run-board-202605",
      scopeKey: "runId"
    });
    expect(scope.items).toContainEqual(expect.objectContaining({
      label: "Run",
      value: "Selected report run",
      ariaLabel: "Run: Selected report run"
    }));
    expect(scope.summary).not.toContain("report-run-board-202605");
  });

  it("uses ledger-run copy for Accounting while retaining the raw run query value", () => {
    const scope = buildOperatingScopeFromSearch("?runId=run-42", null, "/accounting/ledger");

    expect(scope.runId).toBe("run-42");
    expect(scope.queryParams).toContainEqual({
      key: "runId",
      value: "run-42",
      scopeKey: "runId"
    });
    expect(scope.items).toContainEqual(expect.objectContaining({
      label: "Run",
      value: "Selected ledger run",
      ariaLabel: "Run: Selected ledger run"
    }));
    expect(scope.summary).toBe("Run: Selected ledger run");
    expect(scope.clearAriaLabel).toBe("Clear operating scope: Run Selected ledger run");
  });

  it("uses neutral run copy when no workspace route is available", () => {
    const scope = buildOperatingScopeFromSearch("?runId=run-42");

    expect(scope.items).toContainEqual(expect.objectContaining({
      value: "Selected run",
      ariaLabel: "Run: Selected run"
    }));
  });
});
