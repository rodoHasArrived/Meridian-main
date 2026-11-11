import { describe, expect, it } from "vitest";
import { buildAppShellViewState, type AppShellWorkspacePayload } from "@/app-shell.view-model";
import {
  buildDailyControlTowerModel,
  buildWorkspaceAttentionCounts,
  dailyControlTowerProofPassportLabels,
  selectDailyControlTowerQueueItems
} from "@/lib/daily-control-tower";
import { normalizeWorkspacePath } from "@/lib/workspace";
import type { DataWorkspaceResponse, TradingWorkspaceResponse } from "@/types";

const payload: AppShellWorkspacePayload = {
  session: {
    displayName: "Ops Desk",
    role: "Operator",
    environment: "paper",
    activeWorkspace: "trading",
    commandCount: 7
  },
  overview: null,
  strategy: null,
  trading: {
    readiness: {
      asOf: "2026-05-14T21:30:00Z",
      overallStatus: "Blocked",
      readyForPaperOperation: false,
      acceptanceGates: [],
      activeSession: null,
      sessions: [],
      replay: null,
      controls: {
        circuitBreakerOpen: false
      },
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
  } as unknown as TradingWorkspaceResponse,
  portfolio: null,
  data: {
    providers: [
      {
        provider: "Alpaca",
        status: "Warning",
        capability: "paper",
        latency: "120ms",
        note: "Paper endpoint returned intermittent quote gaps.",
        recommendedAction: "Review paper provider posture."
      }
    ],
    backfills: [],
    exports: []
  } as unknown as DataWorkspaceResponse,
  accounting: null,
  reporting: null,
  workflowSummary: null
};

describe("daily control tower model", () => {
  it("leads with the worst queued decision while listing the queue finance-first", () => {
    const shell = buildAppShellViewState({
      pathname: "/",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload
    });

    const model = buildDailyControlTowerModel(shell.workflowContinuity, shell.trustStrip);

    expect(shell.workflowContinuity.title).toBe("Daily Control Tower");
    // The blocked Portfolio item leads even though the finance-first list puts a Reporting
    // review item on top: the headline must name the same decision as the masthead pill.
    expect(model.decision.title).toBe("Brokerage sync failed");
    expect(model.statusLabel).toBe("Blocked");
    expect(model.leadingRow?.item.label).toBe("Brokerage sync failed");
    expect(model.ownerLabel).toBe(model.leadingRow?.item.workspaceLabel);
    expect(model.nextActionHref).toBe(model.leadingRow?.item.route);
    expect(model.driverItems).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "continuity",
        label: "Continuity source",
        value: "Daily Control Tower",
        detail: expect.stringContaining("Source route: /."),
        badgeVariant: "danger"
      }),
      expect.objectContaining({
        id: "queue",
        label: "Finance queue",
        value: "1 blocked / 2 review",
        badgeVariant: "danger"
      }),
      expect.objectContaining({
        id: "trust",
        label: "Trust posture",
        value: "2 need attention",
        badgeVariant: "warning"
      }),
      expect.objectContaining({
        id: "proof",
        label: "Evidence events",
        value: "3 retained",
        badgeVariant: "success"
      })
    ]));
    expect(model.queueRows.map((row) => row.item.label)).toEqual([
      "Report pack approval waiting",
      "Alpaca provider warning",
      "Brokerage sync failed"
    ]);
  });

  it("builds the required evidence summary facts from linked evidence", () => {
    const shell = buildAppShellViewState({
      pathname: "/",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload
    });

    const model = buildDailyControlTowerModel(shell.workflowContinuity, shell.trustStrip);
    const row = model.queueRows[0];

    expect(row?.proof?.label).toBe("Report pack approval waiting");
    // The hero names the event correlated with the leading row, so it agrees with that row's
    // Freshness fact rather than quoting whichever event is newest in the workstation.
    const leadingFreshness = model.leadingRow?.proofPassportItems.find((item) => item.label === "Freshness")?.value;
    expect(model.leadingRow?.proof?.label).toBe("Brokerage sync failed");
    expect(model.evidenceLabel).toBe(`Linked evidence: ${model.leadingRow?.proof?.workspaceLabel} ${leadingFreshness}`);
    expect(row?.proofPassportItems.map((item) => item.label)).toEqual(dailyControlTowerProofPassportLabels());
    expect(row?.proofPassportItems.find((item) => item.label === "Source")?.value).toBe("Reporting");
    expect(row?.proofPassportItems.find((item) => item.label === "Freshness")?.value).toBe("2026-05-14 21:00 UTC");
    expect(row?.proofPassportItems.find((item) => item.label === "Report Usage")?.value).toBe("Reporting package or evidence");
    expect(row?.proofPassportItems.find((item) => item.label === "Audit Trail")?.value).toBe("2026-05-14T21:00:00.000Z");
  });

  it("fails closed when an item has no exactly correlated evidence event", () => {
    const shell = buildAppShellViewState({
      pathname: "/",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload
    });
    const leadingItemId = shell.workflowContinuity.leadingOperatorFocusItemId;
    const viewModel = {
      ...shell.workflowContinuity,
      evidenceTimelineItems: shell.workflowContinuity.evidenceTimelineItems.filter(
        (item) => item.id !== leadingItemId
      )
    };

    const model = buildDailyControlTowerModel(viewModel, shell.trustStrip);
    const row = model.leadingRow;

    expect(row?.item.id).toBe(leadingItemId);
    expect(row?.proof).toBeNull();
    // The newest remaining event belongs to another item. The hero may mention it as workstation
    // context but must not present it as this decision's evidence.
    expect(viewModel.evidenceTimelineItems.length).toBeGreaterThan(0);
    expect(model.evidenceLabel).toMatch(/^No evidence linked to this item\. Latest workstation evidence: /);
    expect(buildDailyControlTowerModel({ ...viewModel, evidenceTimelineItems: [] }, shell.trustStrip).evidenceLabel)
      .toBe("No evidence linked to this item");
    expect(row?.proofPassportItems.find((item) => item.label === "Freshness")?.value)
      .toBe("No timestamped evidence");
    expect(row?.proofPassportItems.find((item) => item.label === "Evidence Packet")?.value)
      .toBe("No evidence packet linked");
  });

  it("derives sidebar attention counts from the exact queue the Control Tower lists", () => {
    const shell = buildAppShellViewState({
      pathname: "/",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload
    });

    const model = buildDailyControlTowerModel(shell.workflowContinuity, shell.trustStrip);
    const attention = buildWorkspaceAttentionCounts(shell.workflowContinuity);

    expect(model.queueRows.length).toBeGreaterThan(0);
    expect(attention.total).toBe(model.queueRows.length);
    const expected: Record<string, number> = {};
    for (const row of model.queueRows) {
      const key = normalizeWorkspacePath(row.item.route.split(/[?#]/, 1)[0]);
      expected[key] = (expected[key] ?? 0) + 1;
    }
    expect(attention.byWorkspace).toEqual(expected);
    expect(selectDailyControlTowerQueueItems(shell.workflowContinuity).map((item) => item.id))
      .toEqual(model.queueRows.map((row) => row.item.id));
  });

  it("falls back to the item's workspace label when its route has no workspace root", () => {
    const shell = buildAppShellViewState({ pathname: "/", loading: false, error: null, workspaceErrors: {}, payload });
    const item = { ...shell.workflowContinuity.operatorFocusCommandItems[0]!, route: "/?focus=1", workspaceLabel: "Accounting" };
    const attention = buildWorkspaceAttentionCounts({
      ...shell.workflowContinuity,
      operatorFocusCommandItems: [item],
      operatorFocusItems: []
    });

    expect(attention).toEqual({ total: 1, byWorkspace: { accounting: 1 } });
  });
});
