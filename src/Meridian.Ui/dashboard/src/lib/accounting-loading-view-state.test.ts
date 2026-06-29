import { describe, expect, it } from "vitest";
import { buildAccountingLoadingViewState } from "@/lib/accounting-loading-view-state";

describe("accounting loading view state", () => {
  it("derives canonical Accounting loading state from the active route", () => {
    const state = buildAccountingLoadingViewState("/accounting/reconciliation");

    expect(state).toMatchObject({
      role: "status",
      ariaBusy: true,
      ariaLive: "polite",
      titleId: "accounting-workspace-loading-title",
      detailId: "accounting-workspace-loading-detail",
      title: "Loading Accounting",
      detail: "Waiting for ledger, reconciliation, cash-flow, and Security Master summaries from workspace data.",
      routeLabel: "/accounting/reconciliation",
      workstreamLabel: "Reconciliation",
      statusItemsLabel: "Accounting workspace data loading"
    });
    expect(state.statusItems.map((item) => item.id)).toEqual([
      "ledger-reconciliation",
      "approvals-exceptions",
      "security-reporting"
    ]);
    expect(state.actions.map((action) => action.href)).toEqual([
      "/accounting/operations-continuity",
      "/accounting/entity-setup",
      "/data/providers",
      "/reporting/evidence"
    ]);
  });

  it("derives canonical Reporting loading state from the reporting route", () => {
    expect(buildAccountingLoadingViewState("/reporting")).toMatchObject({
      titleId: "reporting-workspace-loading-title",
      detailId: "reporting-workspace-loading-detail",
      title: "Loading Reporting",
      detail: "Waiting for report-pack, governed export, and approval summaries from workspace data.",
      workstreamLabel: "Reporting"
    });
  });
});
