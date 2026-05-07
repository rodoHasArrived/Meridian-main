import { describe, expect, it } from "vitest";
import { buildCommandPaletteViewModel } from "@/components/meridian/command-palette.view-model";

describe("command palette view model", () => {
  it("marks the current workspace from the active route", () => {
    const model = buildCommandPaletteViewModel("/settings/integrations");

    expect(model.itemCountLabel).toBe("7 workspaces");
    expect(model.commandListLabel).toBe("7 workspace commands");
    expect(model.activeWorkspaceLabel).toBe("Current: Settings");
    expect(model.routeSummary).toBe("Route to a canonical operator workspace. Current: Settings.");
    expect(model.shortcutHint).toBe("Esc to close");
    expect(model.initialFocusItemId).toBe("settings");
    expect(model.items.find((item) => item.id === "settings")).toMatchObject({
      kind: "workspace",
      route: "/settings",
      routeLabel: "/settings",
      statusLabel: "Current",
      commandLabel: "Stay in Settings",
      active: true
    });
    expect(model.items.find((item) => item.id === "trading")).toMatchObject({
      route: "/trading",
      statusLabel: "Review",
      commandLabel: "Open Trading",
      active: false
    });
  });

  it("normalizes legacy routes before deriving active state", () => {
    const model = buildCommandPaletteViewModel("/data-operations/backfills");

    expect(model.activeWorkspaceLabel).toBe("Current: Data");
    expect(model.items.find((item) => item.id === "data")?.active).toBe(true);
  });

  it("exposes an empty state when workspace metadata is missing", () => {
    const model = buildCommandPaletteViewModel("/trading", []);

    expect(model.items).toEqual([]);
    expect(model.commandListLabel).toBe("0 workspace commands");
    expect(model.routeSummary).toBe("Route to a canonical operator workspace. No active workspace.");
    expect(model.initialFocusItemId).toBeNull();
    expect(model.emptyState).toMatchObject({
      title: "No workspace commands available"
    });
  });

  it("adds backend workflow actions and pinned presets to command routing", () => {
    const model = buildCommandPaletteViewModel("/trading", undefined, {
      workflowLibrary: {
        generatedAt: "2026-01-01T00:00:00Z",
        actions: [],
        workflows: [
          {
            workflowId: "accounting-reconciliation-review",
            title: "Accounting Reconciliation Review",
            summary: "Work breaks and audit evidence.",
            workspaceId: "accounting",
            workspaceTitle: "Accounting",
            entryPageTag: "AccountingShell",
            tone: "Warning",
            evidenceTags: ["break queue"],
            marketPatternTags: ["exception queue"],
            actions: [
              {
                actionId: "workflow.accounting.review-reconciliation",
                label: "Review Reconciliation Breaks",
                detail: "Open the reconciliation lane and work the break queue.",
                targetPageTag: "FundReconciliation",
                tone: "Warning",
                workItemKind: "ReconciliationBreak",
                routePrefixes: ["/api/workstation/reconciliation/break-queue"],
                routeContains: [],
                aliases: []
              }
            ]
          }
        ]
      },
      workflowPresets: {
        generatedAt: "2026-01-01T00:00:00Z",
        presets: [
          {
            presetId: "preset-1",
            name: "Morning breaks",
            description: "Pinned accounting triage",
            workflowId: "accounting-reconciliation-review",
            workflowTitle: "Accounting Reconciliation Review",
            actionId: "workflow.accounting.review-reconciliation",
            actionLabel: "Review Reconciliation Breaks",
            workspaceId: "accounting",
            workspaceTitle: "Accounting",
            targetPageTag: "FundReconciliation",
            tags: ["morning"],
            isPinned: true,
            createdAt: "2026-01-01T00:00:00Z",
            updatedAt: "2026-01-01T00:00:00Z",
            lastUsedAt: null
          }
        ]
      }
    });

    expect(model.itemCountLabel).toBe("7 workspaces - 1 preset - 1 workflow action");
    expect(model.commandListLabel).toBe("9 commands");
    expect(model.backendStatusLabel).toBe("1 workflow action - 1 preset");
    expect(model.items.find((item) => item.id === "workflow:accounting-reconciliation-review:workflow.accounting.review-reconciliation")).toMatchObject({
      kind: "workflow",
      route: "/accounting/reconciliation",
      statusLabel: "Workflow",
      presetId: null
    });
    expect(model.items.find((item) => item.id === "preset:preset-1")).toMatchObject({
      kind: "preset",
      route: "/accounting/reconciliation",
      statusLabel: "Pinned",
      presetId: "preset-1"
    });
  });

  it("routes report-pack approval workflows to the dedicated Reporting task panel", () => {
    const model = buildCommandPaletteViewModel("/reporting", undefined, {
      workflowLibrary: {
        generatedAt: "2026-01-01T00:00:00Z",
        actions: [],
        workflows: [
          {
            workflowId: "portfolio-reporting-output",
            title: "Portfolio Reporting Output",
            summary: "Review report packs and approvals.",
            workspaceId: "reporting",
            workspaceTitle: "Reporting",
            entryPageTag: "ReportingShell",
            tone: "Primary",
            evidenceTags: ["report pack"],
            marketPatternTags: ["approval queue"],
            actions: [
              {
                actionId: "workflow.reporting.approve-report-pack",
                label: "Approve Report Pack",
                detail: "Open report-pack review and approval output.",
                targetPageTag: "FundReportPack",
                tone: "Primary",
                workItemKind: "ReportPackApproval",
                routePrefixes: [],
                routeContains: [],
                aliases: []
              }
            ]
          }
        ]
      }
    });

    expect(model.items.find((item) => item.id === "workflow:portfolio-reporting-output:workflow.reporting.approve-report-pack")).toMatchObject({
      kind: "workflow",
      route: "/reporting/report-packs",
      routeLabel: "/reporting/report-packs"
    });
  });

  it("routes account portfolio workflows to the dedicated brokerage-sync task panel", () => {
    const model = buildCommandPaletteViewModel("/portfolio", undefined, {
      workflowLibrary: {
        generatedAt: "2026-01-01T00:00:00Z",
        actions: [],
        workflows: [
          {
            workflowId: "portfolio-brokerage-sync-review",
            title: "Portfolio Brokerage Sync Review",
            summary: "Review brokerage account sync posture.",
            workspaceId: "portfolio",
            workspaceTitle: "Portfolio",
            entryPageTag: "PortfolioShell",
            tone: "Warning",
            evidenceTags: ["brokerage sync"],
            marketPatternTags: ["account state"],
            actions: [
              {
                actionId: "workflow.portfolio.review-brokerage-sync",
                label: "Review Brokerage Sync",
                detail: "Open account portfolio sync posture.",
                targetPageTag: "AccountPortfolio",
                tone: "Warning",
                workItemKind: "BrokerageSync",
                routePrefixes: ["/api/fund-accounts/brokerage-sync/accounts"],
                routeContains: [],
                aliases: []
              }
            ]
          }
        ]
      }
    });

    expect(model.items.find((item) => item.id === "workflow:portfolio-brokerage-sync-review:workflow.portfolio.review-brokerage-sync")).toMatchObject({
      kind: "workflow",
      route: "/portfolio/brokerage-sync",
      routeLabel: "/portfolio/brokerage-sync"
    });
  });

  it("keeps workspace commands available when the workflow backend is unavailable", () => {
    const model = buildCommandPaletteViewModel("/settings", undefined, {
      workflowError: "Request failed for /api/workstation/workflows (503)"
    });

    expect(model.items).toHaveLength(7);
    expect(model.backendStatusLabel).toBe("Workflow library unavailable");
    expect(model.routeSummary).toBe(
      "Route through shared backend workflow commands. Current: Settings. Workflow library unavailable; workspace commands remain available."
    );
  });
});
