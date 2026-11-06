import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import {
  applyCrossScreenCounts,
  countAccountingOpenBreaks,
  countOpenBreaks,
  countOpenOrders,
  countProviderHealth,
  formatProviderHealth,
  selectCrossScreenCounts
} from "@/lib/cross-screen-counts";
import { buildAccountingReconciliationContinuityStatus } from "@/screens/accounting-screen.workflow-continuity";
import type { AccountingWorkspaceResponse } from "@/types";
import type { WorkstationCrossScreenCounts } from "@/types/cross-screen-counts";

// The same fixture the server's CrossScreenCountDefinitionsTests reads, so both count the same way.
interface CountFixture {
  providerHealth: { name: string; statuses: string[]; healthy: number; total: number }[];
  openBreaks: { name: string; statuses: string[]; open: number }[];
  openOrders: { name: string; statuses: string[]; open: number }[];
}
const fixture = JSON.parse(
  readFileSync(resolve(process.cwd(), "../../../tests/fixtures/cross-screen-counts/cross-screen-counts.json"), "utf8")
) as CountFixture;

const shared: WorkstationCrossScreenCounts = {
  asOf: "2026-03-21T14:30:00Z",
  openOrders: { count: 7, definition: "orders" },
  providers: { healthy: 3, total: 5, definition: "providers" },
  openBreaks: { count: 4, definition: "breaks" }
};

describe("cross-screen count definitions (shared with the server)", () => {
  it.each(fixture.providerHealth)("provider health: $name", ({ statuses, healthy, total }) => {
    expect(countProviderHealth(statuses.map((status) => ({ status })))).toEqual({ healthy, total });
  });

  it.each(fixture.openBreaks)("open breaks: $name", ({ statuses, open }) => {
    expect(countOpenBreaks(statuses.map((status) => ({ status })))).toBe(open);
  });

  it.each(fixture.openOrders)("open orders: $name", ({ statuses, open }) => {
    expect(countOpenOrders(statuses.map((status) => ({ status })))).toBe(open);
  });
});

describe("selectCrossScreenCounts", () => {
  it("counts a screen's own source list before the server's counts", () => {
    const values = selectCrossScreenCounts({
      shared,
      openOrders: [{}, {}],
      providers: [{ status: "Healthy" }, { status: "Warning" }],
      breakQueue: [{ status: "Open" }, { status: "Resolved" }]
    });
    expect(values).toEqual({ openOrders: 2, providers: { healthy: 1, total: 2 }, openBreaks: 1 });
  });

  it("falls back to the server's counts where the screen has no list", () => {
    expect(selectCrossScreenCounts({ shared })).toEqual({ openOrders: 7, providers: { healthy: 3, total: 5 }, openBreaks: 4 });
  });

  it("keeps the server's open-break total when the accounting payload withholds the queue", () => {
    // A caller who may see the count but not the cases gets an empty breakQueue and the real total.
    const withheld = { workspace: { openBreaks: 3 }, breakQueue: [] };
    expect(countAccountingOpenBreaks(withheld)).toBe(3);
    expect(selectCrossScreenCounts({ shared: null, breakQueue: [], openBreakCount: 3 }).openBreaks).toBe(3);
    expect(countAccountingOpenBreaks({ breakQueue: [{ status: "Open" }, { status: "Resolved" }] })).toBe(1);

    const continuity = buildAccountingReconciliationContinuityStatus({
      metrics: [],
      reconciliationQueue: [],
      breakQueue: [],
      workspace: { totalRuns: 0, reconciledRuns: 0, ledgerReadyRuns: 0, openBreaks: 3, securityIssues: 0 }
    } as unknown as AccountingWorkspaceResponse);
    expect(continuity.label).toContain("3 open breaks");
  });

  it("reports an unreadable count as null, never a substitute number", () => {
    const values = selectCrossScreenCounts({ shared: { ...shared, openOrders: null, providers: null, openBreaks: null } });
    expect(values).toEqual({ openOrders: null, providers: null, openBreaks: null });
    expect(formatProviderHealth(values.providers)).toBe("—");
  });
});

describe("applyCrossScreenCounts", () => {
  it("replaces cross-screen headline cards with the selector's value and drops their unexplained delta", () => {
    const metrics = [
      { id: "orders", label: "Open Orders", value: "4", delta: "+1", tone: "default" },
      { id: "providers-healthy", label: "Providers Healthy", value: "4", delta: "0", tone: "success" },
      { id: "fills", label: "Fills", value: "13", delta: "+3", tone: "success" }
    ];
    const applied = applyCrossScreenCounts(metrics, { openOrders: 2, providers: { healthy: 1, total: 4 }, openBreaks: null });
    expect(applied[0]).toMatchObject({ value: "2", delta: "" });
    expect(applied[1]).toMatchObject({ value: "1/4", delta: "", tone: "warning" });
    // Cards that are not cross-screen counts are left alone.
    expect(applied[2]).toEqual(metrics[2]);
  });
});
