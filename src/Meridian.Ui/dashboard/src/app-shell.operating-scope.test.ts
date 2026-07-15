import { describe, expect, it } from "vitest";
import {
  buildOperatingScopeFromSearch,
  operatingScopeDimensionsForRoute
} from "@/app-shell.operating-scope";

describe("operatingScopeDimensionsForRoute", () => {
  it("returns the dimensions each workspace actually filters on", () => {
    expect(operatingScopeDimensionsForRoute("/data/quotes")).toEqual(["symbol", "provider", "window"]);
    expect(operatingScopeDimensionsForRoute("/accounting/ledger")).toEqual([
      "symbol",
      "fundAccountId",
      "runId",
      "provider",
      "window"
    ]);
    expect(operatingScopeDimensionsForRoute("/settings")).toEqual(["fundAccountId", "provider"]);
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
