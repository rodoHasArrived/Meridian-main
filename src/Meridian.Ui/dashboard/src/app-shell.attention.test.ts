import { describe, expect, it } from "vitest";
import { buildAppShellViewState, type AppShellWorkspacePayload } from "@/app-shell.view-model";
import { buildWorkstationStatusItems } from "@/components/meridian/workstation-status-bar";
import { buildWorkspaceAttentionCounts } from "@/lib/daily-control-tower";
import type { DataWorkspaceResponse, SessionInfo, SystemOverviewResponse, TradingWorkspaceResponse } from "@/types";

const session = (environment: SessionInfo["environment"]): SessionInfo => ({
  displayName: "Ops Desk",
  role: "Operator",
  environment,
  activeWorkspace: "trading",
  commandCount: 7
});

const emptyPayload: AppShellWorkspacePayload = {
  session: session("paper"),
  overview: null,
  strategy: null,
  trading: null,
  portfolio: null,
  data: null,
  accounting: null,
  reporting: null,
  workflowSummary: null
};

const providers = (status: string): DataWorkspaceResponse => ({
  metrics: [],
  providers: [{ provider: "Alpaca", status, capability: "paper", latency: "120ms", note: "Heartbeat" }],
  backfills: [],
  exports: []
}) as unknown as DataWorkspaceResponse;

const tradingWithWorkItems = {
  readiness: {
    asOf: "2026-05-14T21:30:00Z",
    overallStatus: "Blocked",
    readyForPaperOperation: false,
    acceptanceGates: [],
    activeSession: null,
    sessions: [],
    replay: null,
    controls: { circuitBreakerOpen: false },
    promotion: null,
    trustGate: null,
    brokerageSync: null,
    workItems: [
      {
        workItemId: "brokerage-sync",
        kind: "BrokerageSync",
        label: "Brokerage sync failed",
        detail: "Account sync failed after the last provider heartbeat.",
        tone: "Critical",
        createdAt: "2026-05-14T20:00:00Z",
        runId: null,
        fundAccountId: "fund-1",
        auditReference: "audit-1",
        workspace: "portfolio",
        targetRoute: "/portfolio/brokerage-sync",
        targetPageTag: "BrokerageSync"
      },
      {
        workItemId: "report-pack",
        kind: "ReportPackApproval",
        label: "Report pack approval waiting",
        detail: "Monthly board pack still needs an operator sign-off.",
        tone: "Warning",
        createdAt: "2026-05-14T21:00:00Z",
        runId: "run-1",
        fundAccountId: null,
        auditReference: "audit-2",
        workspace: "reporting",
        targetRoute: "/reporting/report-packs",
        targetPageTag: "ReportPackApproval"
      }
    ],
    warnings: []
  }
} as unknown as TradingWorkspaceResponse;

function build(overrides: Partial<Parameters<typeof buildAppShellViewState>[0]> = {}) {
  return buildAppShellViewState({
    pathname: "/trading",
    loading: false,
    error: null,
    workspaceErrors: {},
    dataProvenance: "real",
    payload: { ...emptyPayload, data: providers("Healthy") },
    ...overrides
  });
}

describe("app shell attention state", () => {
  it("is all clear when nothing needs the operator", () => {
    const { attention } = build();

    expect(attention).toMatchObject({
      tone: "ready",
      clear: true,
      statusLabel: "All clear",
      title: "No open issues",
      moreCount: 0,
      href: "/",
      issues: []
    });
    expect(attention.signals.map((signal) => signal.id)).toEqual(["decisions", "providers", "source", "workspace-data"]);
  });

  it("does not raise declared demo data on a Paper session as a warning", () => {
    // SEEDED is named by the environment badge. A pill that warned about it on every demo
    // session would never clear, which is how warnings lose their meaning.
    const shell = build({ dataProvenance: "seeded" });

    expect(shell.trustStrip.environment).toEqual({ label: "PAPER", variant: "paper", dataLabel: "SEEDED" });
    expect(shell.attention.clear).toBe(true);
  });

  it("names an unverified data source and a provider warning as review issues", () => {
    const { attention } = build({
      dataProvenance: "unknown",
      payload: { ...emptyPayload, data: providers("Warning") }
    });

    expect(attention.tone).toBe("review");
    expect(attention.statusLabel).toBe("Review");
    // Data loads a provider warning into the decision queue too, so the decision leads.
    expect(attention.issues.map((issue) => issue.id)).toEqual(["decisions", "providers", "source"]);
    // The provider warning is one issue whether it is read from the queue or the signal.
    expect(attention.moreCount).toBe(1);
    expect(attention.sourceSignals.find((signal) => signal.id === "source")).toMatchObject({
      tone: "review",
      headline: "Data source unverified"
    });
    expect(attention.sourceSignals.find((signal) => signal.id === "providers")).toMatchObject({
      tone: "review",
      value: "1 warning",
      href: "/data/providers",
      actionLabel: "Open provider posture"
    });
  });

  it("blocks on non-real data inside a LIVE session", () => {
    const { attention } = build({
      dataProvenance: "simulated",
      payload: { ...emptyPayload, session: session("live"), data: providers("Healthy") }
    });

    expect(attention.tone).toBe("blocked");
    expect(attention.title).toBe("SIMULATED data in a LIVE session");
  });

  it("does not treat a LIVE session on real data as an issue", () => {
    const { attention } = build({ payload: { ...emptyPayload, session: session("live"), data: providers("Healthy") } });

    expect(attention.clear).toBe(true);
  });

  it("leads with the worst queued decision and counts the rest", () => {
    const shell = build({
      payload: { ...emptyPayload, trading: tradingWithWorkItems, data: providers("Warning") }
    });
    const { attention } = shell;

    expect(attention.tone).toBe("blocked");
    expect(attention.statusLabel).toBe("Blocked");
    expect(attention.title).toBe("Brokerage sync failed");
    // Three queued decisions, one of them the provider warning the Providers signal also
    // reports: one named, two more -- the same total as the queue count beside Today.
    expect(attention.moreCount).toBe(2);
    expect(attention.moreCount + 1).toBe(buildWorkspaceAttentionCounts(shell.workflowContinuity).total);
    expect(attention.ariaLabel).toBe("Workstation attention: Blocked. Brokerage sync failed. 2 more issues. Open Today.");
    expect(attention.signals[0]).toMatchObject({
      id: "decisions",
      kind: "workflow",
      value: "Brokerage sync failed · 2 more",
      tone: "blocked"
    });
  });

  it("names the same worst issue on Today as on every other route", () => {
    // Today lists its queue finance-first, which used to put a Reporting review item in the
    // headline there while every other route's pill named the blocked Portfolio item.
    const payload = { ...emptyPayload, trading: tradingWithWorkItems, data: providers("Warning") };
    const today = build({ pathname: "/", payload });
    const trading = build({ pathname: "/trading", payload });
    const accounting = build({ pathname: "/accounting", payload });

    expect(today.workflowContinuity.operatorFocusCommandItems[0]?.label).toBe("Report pack approval waiting");
    for (const shell of [today, trading, accounting]) {
      expect(shell.attention.tone).toBe("blocked");
      expect(shell.attention.title).toBe("Brokerage sync failed");
      expect(shell.attention.moreCount).toBe(2);
      expect(shell.workflowContinuity.decisionBrief).toMatchObject({
        title: "Brokerage sync failed",
        statusTone: "blocked"
      });
    }
  });

  it("reports workspace data that did not load, without counting a failed area twice", () => {
    const { attention } = build({ workspaceErrors: { accounting: "Ledger request timed out." } });

    expect(attention.tone).toBe("blocked");
    expect(attention.sourceSignals.find((signal) => signal.id === "workspace-data")).toMatchObject({
      tone: "blocked",
      value: "1 area did not load",
      actionLabel: "Review diagnostics"
    });
    // The failed area is queued as a decision and reported as a source signal: one issue.
    expect(attention.title).toBe("Accounting slice degraded");
    expect(attention.moreCount).toBe(0);
  });

  it("is pending, not clear, while workspace data loads", () => {
    const { attention } = build({ loading: true });

    expect(attention.tone).toBe("pending");
    expect(attention.statusLabel).toBe("Checking");
    expect(attention.clear).toBe(false);
  });

  it("cannot be all clear while the degraded-mode banner is up", () => {
    const overview = {
      degradedMode: {
        marketDataMode: "live",
        persistenceMode: "none",
        missingPersistenceDomains: []
      }
    } as unknown as SystemOverviewResponse;
    const { attention } = build({ payload: { ...emptyPayload, data: providers("Healthy"), overview } });

    expect(attention.tone).toBe("blocked");
    expect(attention.title).toBe("Host: Persistence: none");
    expect(attention.sourceSignals.map((signal) => signal.id)).toContain("host");
  });

  it("feeds the footer the same issue the masthead pill names", () => {
    const { attention } = build({
      payload: { ...emptyPayload, trading: tradingWithWorkItems, data: providers("Warning") }
    });
    const footer = buildWorkstationStatusItems({
      workspaceLabel: "Trading",
      refreshing: false,
      hasError: false,
      attention
    });

    expect(footer.find((item) => item.key === "attention")).toEqual({
      key: "attention",
      status: "err",
      label: "Attention",
      value: "Brokerage sync failed +2 more"
    });
  });
});
