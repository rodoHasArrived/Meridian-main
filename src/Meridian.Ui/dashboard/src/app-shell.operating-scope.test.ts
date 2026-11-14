import fs from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import {
  appendOperatingScopeToRoute,
  buildOperatingScopeBarSegments,
  buildOperatingScopeFromSearch,
  describeOperatingScopeForRoute,
  OPERATING_SCOPE_ALL_AUTHORIZED,
  OPERATING_SCOPE_ALL_PERIODS,
  OPERATING_SCOPE_EMPTY_SUMMARY,
  operatingScopeDimensionsForRoute,
  readOperatingScopeFromSearch,
  removeOperatingScopeFromSearch
} from "@/app-shell.operating-scope";

describe("operatingScopeDimensionsForRoute", () => {
  it("returns the dimensions each workspace actually filters on", () => {
    expect(operatingScopeDimensionsForRoute("/data/quotes")).toEqual(["symbol", "provider", "window"]);
    // The ledger does not read the entity, so it is not a dimension the route applies.
    expect(operatingScopeDimensionsForRoute("/accounting/ledger")).toEqual([
      "symbol",
      "fundAccountId",
      "runId",
      "provider",
      "window"
    ]);
    expect(operatingScopeDimensionsForRoute("/settings")).toEqual(["fundAccountId", "provider"]);
  });

  it("applies the entity only on the Accounting routes whose screens read it", () => {
    for (const route of ["/accounting", "/accounting/configure", "/accounting/journal-entries", "/accounting/approvals", "/accounting/operations-continuity"]) {
      expect(operatingScopeDimensionsForRoute(route)).toContain("entityId");
    }
    for (const route of ["/accounting/ledger", "/accounting/trial-balance", "/accounting/close-calendar"]) {
      expect(operatingScopeDimensionsForRoute(route)).not.toContain("entityId");
    }
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
    expect(OPERATING_SCOPE_ALL_PERIODS).toBe("All periods");
    expect(OPERATING_SCOPE_EMPTY_SUMMARY).toBe("All authorized scopes");
    expect(scope.hasScope).toBe(false);
    expect(scope.summary).toBe("All authorized scopes");
    expect(buildOperatingScopeBarSegments(scope)).toEqual([
      { id: "entityId", label: "Entity", value: "All authorized", isSet: false, applied: true },
      { id: "fundId", label: "Fund", value: "All authorized", isSet: false, applied: true },
      { id: "fundAccountId", label: "Account", value: "All authorized", isSet: false, applied: true },
      { id: "window", label: "Period", value: "All periods", isSet: false, applied: true }
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
  it("orders Entity, Fund, Account, Subject, Run, Provider, Period and shows the optional ones only when set", () => {
    const scope = buildOperatingScopeFromSearch("?symbol=MSFT&fundAccountId=fund-1&runId=run-9&provider=Alpaca&asOf=2026-05-15&fundId=fund-x&entityId=entity-y");

    expect(buildOperatingScopeBarSegments(scope).map((segment) => [segment.label, segment.value])).toEqual([
      ["Entity", "entity-y"],
      ["Fund", "fund-x"],
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

describe("entity and fund dimensions", () => {
  it("parse from the URL, normalise, and round-trip through the scope state", () => {
    const search = "?entityId=%2022222222-2222-2222-2222-222222222222%20&fundId=fund%2F1&fundAccountId=acct-1";
    expect(readOperatingScopeFromSearch(search)).toMatchObject({
      entityId: "22222222-2222-2222-2222-222222222222",
      fundId: "fund1",
      fundAccountId: "acct-1"
    });

    const scope = buildOperatingScopeFromSearch(search);
    expect(scope).toMatchObject({ entityId: "22222222-2222-2222-2222-222222222222", fundId: "fund1", hasScope: true });
    expect(scope.queryParams.slice(0, 2)).toEqual([
      { key: "entityId", value: "22222222-2222-2222-2222-222222222222", scopeKey: "entityId" },
      { key: "fundId", value: "fund1", scopeKey: "fundId" }
    ]);
    const reparsed = buildOperatingScopeFromSearch(appendOperatingScopeToRoute("/trading", scope).split("?")[1]);
    expect(reparsed.queryParams).toEqual(scope.queryParams);
  });

  it("fall back to the stored scope and treat blank values as unset", () => {
    const scope = buildOperatingScopeFromSearch("?entityId=%20%20", { entityId: "entity-stored", fundId: "" });
    expect(scope.entityId).toBe("entity-stored");
    expect(scope.fundId).toBeNull();
    expect(buildOperatingScopeFromSearch("?fundId=").hasScope).toBe(false);
  });

  it("travel on every route but apply only where a screen reads them", () => {
    const scope = buildOperatingScopeFromSearch("?entityId=entity-1&fundId=fund-1&symbol=MSFT");

    expect(appendOperatingScopeToRoute("/data/quotes", scope)).toBe("/data/quotes?entityId=entity-1&fundId=fund-1&symbol=MSFT");
    expect(appendOperatingScopeToRoute("/settings", scope)).toBe("/settings?entityId=entity-1&fundId=fund-1");
    expect(appendOperatingScopeToRoute("/accounting?entityId=entity-own", scope)).toBe("/accounting?entityId=entity-own&fundId=fund-1&symbol=MSFT");

    const onData = buildOperatingScopeBarSegments(scope, operatingScopeDimensionsForRoute("/data/quotes"));
    expect(onData.find((segment) => segment.id === "entityId")).toMatchObject({ isSet: true, applied: false });
    expect(onData.find((segment) => segment.id === "fundId")).toMatchObject({ isSet: true, applied: false });
    const onLedger = buildOperatingScopeBarSegments(scope, operatingScopeDimensionsForRoute("/accounting/ledger"));
    expect(onLedger.find((segment) => segment.id === "entityId")).toMatchObject({ isSet: true, applied: false });
    const onAccounting = buildOperatingScopeBarSegments(scope, operatingScopeDimensionsForRoute("/accounting"));
    expect(onAccounting.find((segment) => segment.id === "entityId")).toMatchObject({ isSet: true, applied: true });
    expect(onAccounting.find((segment) => segment.id === "fundId")).toMatchObject({ isSet: true, applied: false });

    expect(describeOperatingScopeForRoute("/accounting", scope)).toBe("Entity entity-1 · Subject MSFT");
    expect(describeOperatingScopeForRoute("/accounting/ledger", scope)).toBe("Subject MSFT");
    expect(describeOperatingScopeForRoute("/trading", scope)).toBe("Subject MSFT");
  });

  it("are removed with the rest of the scope", () => {
    expect(removeOperatingScopeFromSearch("?entityId=e&fundId=f&tab=book")).toBe("?tab=book");
  });

  it("print a display name in the bar when one is known, and the id otherwise", () => {
    const scope = buildOperatingScopeFromSearch("?entityId=entity-1&fundId=fund-1&fundAccountId=acct-1");
    expect(buildOperatingScopeBarSegments(scope, undefined, { entityId: "Meridian Holdings LP", fundId: " " })
      .map((segment) => `${segment.label} ${segment.value}`)).toEqual([
      "Entity Meridian Holdings LP",
      "Fund fund-1",
      "Account acct-1",
      "Period All periods"
    ]);
    expect(buildOperatingScopeBarSegments(buildOperatingScopeFromSearch(""), undefined, { entityId: "Ignored" })[0]?.value)
      .toBe("All authorized");
  });
});

describe("the period window", () => {
  it("reads a same-day range as that day and an open range honestly", () => {
    const value = (search: string) => buildOperatingScopeBarSegments(buildOperatingScopeFromSearch(search)).at(-1)?.value;
    expect(value("?from=2026-11-14&to=2026-11-14")).toBe("2026-11-14");
    expect(value("?from=2026-11-01&to=2026-11-14")).toBe("2026-11-01 to 2026-11-14");
    expect(value("?from=2026-11-01")).toBe("2026-11-01 to Now");
    expect(value("?asOf=2026-11-14")).toBe("as of 2026-11-14");
    expect(value("")).toBe("All periods");
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
