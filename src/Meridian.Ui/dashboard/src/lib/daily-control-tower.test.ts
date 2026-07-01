import { describe, expect, it } from "vitest";
import { buildAppShellViewState, type AppShellWorkspacePayload } from "@/app-shell.view-model";
import {
  buildDailyControlTowerModel,
  dailyControlTowerProofPassportLabels
} from "@/lib/daily-control-tower";
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
  it("projects the highest-priority shell decision into a blocked output queue", () => {
    const shell = buildAppShellViewState({
      pathname: "/",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload
    });

    const model = buildDailyControlTowerModel(shell.workflowContinuity, shell.trustStrip);

    expect(shell.workflowContinuity.title).toBe("Daily Control Tower");
    expect(model.decision.title).toBe("Resolve Brokerage sync failed");
    expect(model.statusLabel).toBe("Blocked");
    expect(model.ownerLabel).toBe("Settings");
    expect(model.outputLabel).toBe("Provider setup or diagnostics");
    expect(model.nextActionHref).toBe("/settings#alpaca-provider-setup");
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
        value: "1 need attention",
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
      "Brokerage sync failed",
      "Report pack approval waiting",
      "Alpaca provider warning"
    ]);
  });

  it("builds the required proof passport facts from linked evidence", () => {
    const shell = buildAppShellViewState({
      pathname: "/",
      loading: false,
      error: null,
      workspaceErrors: {},
      payload
    });

    const model = buildDailyControlTowerModel(shell.workflowContinuity, shell.trustStrip);
    const row = model.queueRows[0];

    expect(row?.proof?.label).toBe("Brokerage sync failed");
    expect(row?.proofPassportItems.map((item) => item.label)).toEqual(dailyControlTowerProofPassportLabels());
    expect(row?.proofPassportItems.find((item) => item.label === "Source")?.value).toBe("Settings");
    expect(row?.proofPassportItems.find((item) => item.label === "Freshness")?.value).toBe("2026-05-14 20:00 UTC");
    expect(row?.proofPassportItems.find((item) => item.label === "Report Usage")?.value).toBe("Provider setup or diagnostics");
    expect(row?.proofPassportItems.find((item) => item.label === "Audit Trail")?.value).toBe("2026-05-14T20:00:00.000Z");
  });
});
