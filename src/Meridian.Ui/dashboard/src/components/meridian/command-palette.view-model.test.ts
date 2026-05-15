import { describe, expect, it } from "vitest";
import {
  buildCommandPaletteViewModel,
  resolveCommandPaletteKeyCommand
} from "@/components/meridian/command-palette.view-model";

describe("command palette view model", () => {
  it("marks the current workspace and setup anchor from the active route", () => {
    const model = buildCommandPaletteViewModel("/settings#alpaca-provider-setup");

    expect(model.itemCountLabel).toBe("7 workspaces - 13 quick routes");
    expect(model.commandListLabel).toBe("20 workstation commands");
    expect(model.filteredItemCountLabel).toBe("20 commands available");
    expect(model.commandGroups.map((group) => `${group.label}:${group.countLabel}`)).toEqual([
      "Workspaces:7 workspaces",
      "Quick routes:13 quick routes"
    ]);
    expect(model.activeWorkspaceLabel).toBe("Current: Settings");
    expect(model.routeSummary).toBe("Route to common operator workflows and canonical workspaces. Current: Settings.");
    expect(model.shortcutHint).toBe("Esc to close");
    expect(model.initialFocusItemId).toBe("route:settings-integrations");
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
    expect(model.items.find((item) => item.id === "route:settings-integrations")).toMatchObject({
      kind: "route",
      route: "/settings#alpaca-provider-setup",
      statusLabel: "Current",
      commandLabel: "Stay on Alpaca provider setup",
      active: true
    });
    expect(model.filteredItems).toHaveLength(20);
  });

  it("filters commands by workspace, route, status, and description text", () => {
    const model = buildCommandPaletteViewModel("/settings", undefined, {}, "preview portfolio");

    expect(model.filteredItemCountLabel).toBe("1 of 20 commands match");
    expect(model.filteredItems.map((item) => item.id)).toEqual(["portfolio"]);
    expect(model.commandGroups).toEqual([
      expect.objectContaining({
        kind: "workspace",
        label: "Workspaces",
        countLabel: "1 workspace",
        ariaLabel: "Workspaces: 1 workspace"
      })
    ]);
    expect(model.initialFocusItemId).toBe("portfolio");
  });

  it("does not mark hash-targeted setup commands active from other Settings panels", () => {
    const model = buildCommandPaletteViewModel("/settings");

    expect(model.initialFocusItemId).toBe("settings");
    expect(model.items.find((item) => item.id === "route:settings-integrations")).toMatchObject({
      route: "/settings#alpaca-provider-setup",
      routeLabel: "/settings#alpaca-provider-setup",
      statusLabel: "Route",
      commandLabel: "Open Alpaca provider setup",
      active: false
    });

    const capabilityModel = buildCommandPaletteViewModel("/settings#backend-capability-coverage");
    expect(capabilityModel.initialFocusItemId).toBe("settings");
    expect(capabilityModel.items.find((item) => item.id === "route:settings-integrations")).toMatchObject({
      route: "/settings#alpaca-provider-setup",
      statusLabel: "Route",
      commandLabel: "Open Alpaca provider setup",
      active: false
    });
  });

  it("exposes a searchable empty state when commands do not match", () => {
    const model = buildCommandPaletteViewModel("/settings", undefined, {}, "not-a-command");

    expect(model.items).toHaveLength(20);
    expect(model.filteredItems).toEqual([]);
    expect(model.commandGroups).toEqual([]);
    expect(model.filteredItemCountLabel).toBe("0 of 20 commands match");
    expect(model.emptyState).toEqual({
      title: "No matching commands",
      detail: "Try a workspace name, route, workflow title, or status label."
    });
  });

  it("normalizes legacy routes before deriving active state", () => {
    const model = buildCommandPaletteViewModel("/data-operations/backfills");

    expect(model.activeWorkspaceLabel).toBe("Current: Data");
    expect(model.items.find((item) => item.id === "data")?.active).toBe(true);
  });

  it("keeps quick routes available when workspace metadata is missing", () => {
    const model = buildCommandPaletteViewModel("/trading", []);

    expect(model.items).toHaveLength(13);
    expect(model.commandListLabel).toBe("13 workstation commands");
    expect(model.itemCountLabel).toBe("0 workspaces - 13 quick routes");
    expect(model.routeSummary).toBe("Route to common operator workflows and canonical workspaces. No active workspace.");
    expect(model.initialFocusItemId).toBe("route:trading-readiness");
    expect(model.emptyState).toBeNull();
  });

  it("exposes covered-call and price-alert route commands", () => {
    const coveredCallModel = buildCommandPaletteViewModel("/strategy/covered-call", undefined, {}, "covered call");
    const priceAlertsModel = buildCommandPaletteViewModel("/data/alerts", undefined, {}, "price alerts");

    expect(coveredCallModel.filteredItems.map((item) => item.id)).toContain("route:strategy-covered-call");
    expect(coveredCallModel.items.find((item) => item.id === "route:strategy-covered-call")).toMatchObject({
      kind: "route",
      label: "Covered call backtest",
      route: "/strategy/covered-call",
      statusLabel: "Current",
      commandLabel: "Stay on Covered call backtest",
      active: true
    });
    expect(priceAlertsModel.filteredItems.map((item) => item.id)).toContain("route:data-alerts");
    expect(priceAlertsModel.items.find((item) => item.id === "route:data-alerts")).toMatchObject({
      kind: "route",
      label: "Price alerts",
      route: "/data/alerts",
      statusLabel: "Current",
      commandLabel: "Stay on Price alerts",
      active: true
    });
  });

  it("applies the active operating symbol to symbol-aware command routes", () => {
    const model = buildCommandPaletteViewModel("/portfolio", undefined, {}, "msft depth", "msft");

    expect(model.operatingContextLabel).toBe("Subject: MSFT");
    expect(model.filteredItems.map((item) => item.id)).toEqual(["route:data-quotes"]);
    expect(model.items.find((item) => item.id === "route:data-quotes")).toMatchObject({
      route: "/data/quotes?symbol=MSFT",
      routeLabel: "/data/quotes?symbol=MSFT",
      description: "Inspect quotes, trades, depth, charts, and staged tickets. Subject: MSFT."
    });
    expect(model.items.find((item) => item.id === "route:data-alerts")).toMatchObject({
      route: "/data/alerts?symbol=MSFT",
      routeLabel: "/data/alerts?symbol=MSFT",
      description: "Create local quote-threshold alerts and review alert trigger state. Subject: MSFT."
    });
  });

  it("prefers the route symbol over the stored operating symbol", () => {
    const model = buildCommandPaletteViewModel("/data/quotes?symbol=AAPL", undefined, {}, "", "MSFT");

    expect(model.operatingContextLabel).toBe("Subject: AAPL");
    expect(model.initialFocusItemId).toBe("route:data-quotes");
    expect(model.items.find((item) => item.id === "route:data-quotes")).toMatchObject({
      route: "/data/quotes?symbol=AAPL",
      statusLabel: "Current",
      active: true
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

    expect(model.itemCountLabel).toBe("7 workspaces - 13 quick routes - 1 preset - 1 workflow action");
    expect(model.commandListLabel).toBe("22 commands");
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

  it("routes evidence workflow actions to the Evidence Workbench", () => {
    const model = buildCommandPaletteViewModel("/strategy", undefined, {
      workflowLibrary: {
        generatedAt: "2026-01-01T00:00:00Z",
        actions: [],
        workflows: [
          {
            workflowId: "strategy-to-paper-review",
            title: "Strategy to Paper Review",
            summary: "Review strategy evidence before promotion.",
            workspaceId: "strategy",
            workspaceTitle: "Strategy",
            entryPageTag: "StrategyShell",
            tone: "Primary",
            evidenceTags: ["run evidence"],
            marketPatternTags: ["promotion review"],
            actions: [
              {
                actionId: "workflow.evidence.open-packet",
                label: "Open Evidence Packet",
                detail: "Open the reusable evidence packet.",
                targetPageTag: "EvidenceWorkbench",
                tone: "Primary",
                workItemKind: null,
                routePrefixes: [],
                routeContains: [],
                aliases: []
              }
            ]
          }
        ]
      }
    });

    expect(model.items.find((item) => item.id === "workflow:strategy-to-paper-review:workflow.evidence.open-packet")).toMatchObject({
      kind: "workflow",
      route: "/reporting/evidence",
      routeLabel: "/reporting/evidence"
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

  it("keeps local route commands available when the workflow backend is unavailable", () => {
    const model = buildCommandPaletteViewModel("/settings", undefined, {
      workflowError: "Request failed for /api/workstation/workflows (503)"
    });

    expect(model.items).toHaveLength(20);
    expect(model.backendStatusLabel).toBe("Workflow library unavailable");
    expect(model.routeSummary).toBe(
      "Route through shared backend workflow commands. Current: Settings. Workflow library unavailable; local route commands remain available."
    );
  });

  it("keeps command palette keyboard commands in the view model", () => {
    expect(resolveCommandPaletteKeyCommand({ key: "Escape", focusBoundary: "middle" })).toBe("close");
    expect(resolveCommandPaletteKeyCommand({ key: "Tab", focusBoundary: "last" })).toBe("focus-first");
    expect(resolveCommandPaletteKeyCommand({ key: "Tab", shiftKey: true, focusBoundary: "first" })).toBe("focus-last");
    expect(resolveCommandPaletteKeyCommand({ key: "Tab", focusBoundary: "outside" })).toBe("focus-first");
    expect(resolveCommandPaletteKeyCommand({ key: "Tab", shiftKey: true, focusBoundary: "outside" })).toBe("focus-last");
    expect(resolveCommandPaletteKeyCommand({ key: "Enter", focusBoundary: "middle", focusTarget: "search" })).toBe(
      "activate-first-command"
    );
    expect(resolveCommandPaletteKeyCommand({ key: "Enter", focusBoundary: "middle", focusTarget: "command" })).toBeNull();
    expect(resolveCommandPaletteKeyCommand({ key: "ArrowDown", focusBoundary: "middle" })).toBe("focus-next-command");
    expect(resolveCommandPaletteKeyCommand({ key: "ArrowUp", focusBoundary: "middle" })).toBe("focus-previous-command");
    expect(resolveCommandPaletteKeyCommand({ key: "Home", focusBoundary: "middle" })).toBeNull();
    expect(resolveCommandPaletteKeyCommand({ key: "Tab", focusBoundary: "middle" })).toBeNull();
    expect(resolveCommandPaletteKeyCommand({ key: "Tab", focusBoundary: "none" })).toBeNull();
  });
});
