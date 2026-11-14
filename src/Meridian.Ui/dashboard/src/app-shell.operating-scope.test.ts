import fs from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import {
  appendOperatingScopeToRoute,
  buildOperatingScopeBarSegments,
  buildOperatingScopeFromSearch,
  describeOperatingScopeForRoute,
  OPERATING_SCOPE_ALL_AUTHORIZED,
  OPERATING_SCOPE_EMPTY_SUMMARY,
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

describe("the empty operating scope", () => {
  it("reads All authorized, never None or No scope selected", () => {
    const scope = buildOperatingScopeFromSearch("");

    expect(OPERATING_SCOPE_ALL_AUTHORIZED).toBe("All authorized");
    expect(OPERATING_SCOPE_EMPTY_SUMMARY).toBe("All authorized scopes");
    expect(scope.hasScope).toBe(false);
    expect(scope.summary).toBe("All authorized scopes");
    expect(buildOperatingScopeBarSegments(scope)).toEqual([
      { id: "fundAccountId", label: "Account", value: "All authorized", isSet: false, applied: true },
      { id: "window", label: "Period", value: "All authorized", isSet: false, applied: true }
    ]);
    expect(describeOperatingScopeForRoute("/trading", scope)).toBe("All authorized scopes");
  });

  it("is never worded as an absent scope anywhere in the dashboard source", () => {
    const root = path.resolve(__dirname);
    const offenders: string[] = [];
    const walk = (directory: string) => {
      for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        const full = path.join(directory, entry.name);
        if (entry.isDirectory()) {
          walk(full);
        } else if (/\.(ts|tsx)$/.test(entry.name) && !/\.test\.tsx?$/.test(entry.name)) {
          const text = fs.readFileSync(full, "utf8");
          if (/No scope selected|No operating scope selected/.test(text)) offenders.push(path.relative(root, full));
        }
      }
    };
    walk(root);

    expect(offenders).toEqual([]);
  });
});

describe("buildOperatingScopeBarSegments", () => {
  it("orders Account, Subject, Run, Provider, Period and shows the optional ones only when set", () => {
    const scope = buildOperatingScopeFromSearch("?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&asOf=2026-05-15");

    expect(buildOperatingScopeBarSegments(scope).map((segment) => [segment.label, segment.value])).toEqual([
      ["Account", "fund-1"],
      ["Subject", "MSFT"],
      ["Run", "Selected run"],
      ["Provider", "Alpaca"],
      ["Period", "as of 2026-05-15"]
    ]);
  });

  it("marks set dimensions the route does not filter by as not applied", () => {
    const scope = buildOperatingScopeFromSearch("?symbol=MSFT&fundAccountId=fund-1");
    const segments = buildOperatingScopeBarSegments(scope, operatingScopeDimensionsForRoute("/data/quotes"));

    expect(segments.find((segment) => segment.id === "fundAccountId")).toMatchObject({ isSet: true, applied: false });
    expect(segments.find((segment) => segment.id === "symbol")).toMatchObject({ isSet: true, applied: true });
    expect(segments.find((segment) => segment.id === "window")).toMatchObject({ isSet: false, applied: true });
  });
});

describe("describeOperatingScopeForRoute", () => {
  it("names only the dimensions the route applies", () => {
    const scope = buildOperatingScopeFromSearch("?symbol=MSFT&fundAccountId=fund-1");

    expect(describeOperatingScopeForRoute("/trading/orders", scope)).toBe("Subject MSFT · Account fund-1");
    expect(describeOperatingScopeForRoute("/data/quotes", scope)).toBe("Subject MSFT");
    expect(describeOperatingScopeForRoute("/settings", buildOperatingScopeFromSearch("?symbol=MSFT"))).toBe("All authorized scopes");
  });
});

describe("appendOperatingScopeToRoute", () => {
  it("carries the applied scope on links, so a copied or followed link keeps it", () => {
    const scope = buildOperatingScopeFromSearch("?fundAccountId=fund-1&symbol=MSFT&provider=Alpaca");

    expect(appendOperatingScopeToRoute("/trading/orders", scope)).toBe("/trading/orders?symbol=MSFT&fundAccountId=fund-1&provider=Alpaca");
    expect(appendOperatingScopeToRoute("/data/quotes?tab=book", scope)).toBe("/data/quotes?tab=book&symbol=MSFT&provider=Alpaca");
    expect(appendOperatingScopeToRoute("/trading", buildOperatingScopeFromSearch(""))).toBe("/trading");
  });
});
