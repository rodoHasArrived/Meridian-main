import { describe, expect, it } from "vitest";
import { buildOperatingScopeFromSearch } from "@/lib/app-shell-operating-scope";
import {
  buildPrimaryOperatorWorkflowSteps,
  developmentFixtureDemoSteps,
  resolvePrimaryOperatorWorkflowStepId,
  workflowContinuityTrails
} from "@/lib/app-shell-workflow-catalog";

describe("app shell workflow catalog", () => {
  it("keeps the primary operator flow route-aware and scope-preserving", () => {
    const scope = buildOperatingScopeFromSearch("?symbol=AAPL&fundAccountId=fund-1");
    const steps = buildPrimaryOperatorWorkflowSteps("/accounting/reconciliation", scope);

    expect(steps.map((step) => step.id)).toEqual([
      "import",
      "validate",
      "reconcile",
      "investigate",
      "approve",
      "report"
    ]);
    expect(steps.find((step) => step.id === "reconcile")).toMatchObject({
      active: true,
      statusLabel: "Current",
      statusTone: "review",
      href: "/accounting/reconciliation?symbol=AAPL&fundAccountId=fund-1"
    });
    expect(steps.find((step) => step.id === "report")?.href).toBe("/reporting/report-builder?symbol=AAPL&fundAccountId=fund-1");
  });

  it("maps focused task routes to the expected primary workflow stage", () => {
    expect(resolvePrimaryOperatorWorkflowStepId("/data/providers")).toBe("import");
    expect(resolvePrimaryOperatorWorkflowStepId("/data/backfills")).toBe("validate");
    expect(resolvePrimaryOperatorWorkflowStepId("/accounting/ledger")).toBe("reconcile");
    expect(resolvePrimaryOperatorWorkflowStepId("/accounting/approvals")).toBe("approve");
    expect(resolvePrimaryOperatorWorkflowStepId("/reporting/exports")).toBe("report");
    expect(resolvePrimaryOperatorWorkflowStepId("/strategy")).toBe("investigate");
  });

  it("keeps continuity trails and fixture demo steps in the shared catalog", () => {
    expect(workflowContinuityTrails.map((trail) => trail.id)).toEqual([
      "market-data-to-paper",
      "strategy-to-paper",
      "trading-accounting",
      "accounting-closeout"
    ]);
    expect(workflowContinuityTrails.find((trail) => trail.id === "accounting-closeout")?.steps.map((step) => step.href)).toContain("/accounting/ledger");
    expect(workflowContinuityTrails.find((trail) => trail.id === "accounting-closeout")?.steps.map((step) => step.href)).toContain("/accounting/approvals");
    expect(developmentFixtureDemoSteps.map((step) => step.id)).toEqual(["watchlist", "quotes", "readiness", "connect"]);
    expect(developmentFixtureDemoSteps.find((step) => step.id === "connect")).toMatchObject({
      href: "/settings#alpaca-provider-setup",
      matchHash: "#alpaca-provider-setup"
    });
  });
});
