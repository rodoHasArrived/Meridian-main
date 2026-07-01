import { describe, expect, it } from "vitest";
import { buildWorkspaceNavViewModel } from "@/components/meridian/workspace-nav.view-model";
import type { WorkspaceSummary } from "@/types";

describe("workspace nav view model", () => {
  it("marks the active canonical workspace route", () => {
    const model = buildWorkspaceNavViewModel("/portfolio/positions");

    expect(model.brandTitle).toBe("Meridian");
    expect(model.items).toHaveLength(7);
    expect(model.items.find((item) => item.key === "portfolio")).toMatchObject({
      route: "/portfolio",
      active: true,
      ariaCurrent: undefined,
      statusLabel: "Preview · Current",
      statusTone: "preview",
      ariaLabel: "Portfolio workspace, active section, Preview"
    });
    expect(model.currentWorkspace).toMatchObject({
      label: "Portfolio",
      statusLabel: "Preview posture",
      statusTone: "preview",
      route: "/portfolio",
      routeAriaLabel: "Canonical route /portfolio",
      ariaLabel: "Current workspace: Portfolio, Preview posture"
    });
    expect(model.deliveryShortcutLabel).toBe("Ctrl K");
    expect(model.items.find((item) => item.key === "trading")).toMatchObject({
      route: "/trading",
      active: false,
      ariaCurrent: undefined,
      statusLabel: "Review",
      statusTone: "review",
      ariaLabel: "Open Trading workspace, Review"
    });
    expect(model.items.find((item) => item.key === "trading")?.subItems.map((item) => item.route)).toEqual([
      "/trading",
      "/trading/orders",
      "/trading/positions",
      "/trading/risk",
      "/trading/readiness"
    ]);
  });

  it("normalizes legacy workspace aliases for current-route state", () => {
    const model = buildWorkspaceNavViewModel("/data-operations/backfills");

    expect(model.items.find((item) => item.key === "data")).toMatchObject({
      active: true,
      ariaCurrent: undefined,
      statusLabel: "Live · Current",
      statusTone: "live"
    });
    expect(model.currentWorkspace).toMatchObject({
      label: "Data",
      statusLabel: "Live posture",
      statusTone: "live"
    });
  });

  it("canonicalizes caller-provided workspace metadata before rendering root navigation", () => {
    const staleWorkspaces: WorkspaceSummary[] = [
      {
        key: "strategy",
        label: "Research",
        description: "Legacy research root label.",
        status: "Preview"
      },
      {
        key: "accounting",
        label: "Governance",
        description: "Legacy governance root label.",
        status: "Live"
      },
      {
        key: "data",
        label: "Data Operations",
        description: "Legacy data-operations root label.",
        status: "Review"
      }
    ];

    const model = buildWorkspaceNavViewModel("/governance/reconciliation", staleWorkspaces);

    expect(model.items.map((item) => [item.key, item.label, item.statusLabel])).toEqual([
      ["accounting", "Accounting", "Live · Current"],
      ["strategy", "Strategy", "Preview"],
      ["data", "Data", "Review"]
    ]);
    expect(model.items.map((item) => item.label)).not.toEqual(
      expect.arrayContaining(["Research", "Governance", "Data Operations"])
    );
    expect(model.currentWorkspace).toMatchObject({
      label: "Accounting",
      description: "Ledger, cash-flow, reconciliation, Security Master coverage, and fund-account evidence.",
      statusLabel: "Live posture"
    });
  });

  it("surfaces the family-office route under Portfolio", () => {
    const model = buildWorkspaceNavViewModel("/portfolio/family-office");
    const portfolio = model.items.find((item) => item.key === "portfolio");

    expect(portfolio?.subItems.map((item) => item.route)).toEqual([
      "/portfolio",
      "/portfolio/attribution",
      "/portfolio/brokerage-sync",
      "/portfolio/family-office"
    ]);
    expect(portfolio?.subItems.find((item) => item.route === "/portfolio/family-office")).toMatchObject({
      label: "Family office",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Family office, current page"
    });
  });

  it("surfaces the accounting ledger lane as a first-class subroute", () => {
    const model = buildWorkspaceNavViewModel("/accounting/ledger");
    const accounting = model.items.find((item) => item.key === "accounting");

    expect(accounting?.subItems.map((item) => item.route)).toEqual([
      "/accounting",
      "/accounting/operations-continuity",
      "/accounting/entity-setup",
      "/accounting/ledger",
      "/accounting/trial-balance",
      "/accounting/journal-entries",
      "/accounting/reconciliation",
      "/accounting/exceptions",
      "/accounting/security-master",
      "/accounting/approvals",
      "/accounting/evidence",
      "/accounting/configure"
    ]);
    expect(accounting?.subItems[0]).toMatchObject({
      label: "Today",
      active: false,
      ariaCurrent: undefined,
      ariaLabel: "Open Today"
    });
    expect(accounting?.subItems[1]).toMatchObject({
      label: "Close",
      active: false,
      ariaCurrent: undefined,
      ariaLabel: "Open Close"
    });
    expect(accounting?.subItems[3]).toMatchObject({
      label: "Ledger",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Ledger, current page"
    });
  });

  it("surfaces the accounting evidence intake queue under Accounting", () => {
    const model = buildWorkspaceNavViewModel("/accounting/evidence");
    const accounting = model.items.find((item) => item.key === "accounting");

    expect(accounting?.subItems.find((item) => item.route === "/accounting/evidence")).toMatchObject({
      label: "Evidence",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Evidence, current page"
    });
  });

  it("surfaces the operations-record release route under Reporting", () => {
    const model = buildWorkspaceNavViewModel("/reporting/operations-record");
    const reporting = model.items.find((item) => item.key === "reporting");

    expect(model.items.map((item) => item.label)).toEqual([
      "Trading",
      "Portfolio",
      "Accounting",
      "Reporting",
      "Strategy",
      "Data",
      "Settings"
    ]);
    expect(reporting?.subItems.map((item) => item.route)).toEqual([
      "/reporting",
      "/reporting/library",
      "/reporting/operations-record",
      "/reporting/report-packs",
      "/reporting/evidence",
      "/reporting/exports"
    ]);
    expect(reporting?.subItems.find((item) => item.route === "/reporting/operations-record")).toMatchObject({
      label: "Operations record",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Operations record, current page"
    });
  });

  it("surfaces the implemented covered-call backtest route under Strategy", () => {
    const model = buildWorkspaceNavViewModel("/strategy/covered-call");
    const strategy = model.items.find((item) => item.key === "strategy");

    expect(strategy?.subItems.map((item) => item.route)).toEqual([
      "/strategy",
      "/strategy/designer",
      "/strategy/formula-workbench",
      "/strategy/covered-call",
      "/strategy/promotions",
      "/strategy/lab",
      "/strategy/quant-lab"
    ]);
    expect(strategy?.subItems.find((item) => item.route === "/strategy/covered-call")).toMatchObject({
      label: "Covered call",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Covered call, current page"
    });
    expect(strategy?.subItems.find((item) => item.route === "/strategy/lab")).toMatchObject({
      label: "Strategy Lab",
      active: false,
      ariaCurrent: undefined,
      ariaLabel: "Open Strategy Lab"
    });
    expect(strategy?.subItems.map((item) => item.label)).not.toContain("Research Lab");
  });

  it("surfaces the implemented price-alerts route under Data", () => {
    const model = buildWorkspaceNavViewModel("/data/alerts");
    const data = model.items.find((item) => item.key === "data");

    expect(data?.subItems.map((item) => item.route)).toEqual([
      "/data",
      "/data/providers",
      "/data/watchlist",
      "/data/quotes",
      "/data/alerts",
      "/data/evidence",
      "/data/backfills"
    ]);
    expect(data?.subItems.find((item) => item.route === "/data/alerts")).toMatchObject({
      label: "Price alerts",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Price alerts, current page"
    });
  });

  it("surfaces the document evidence intake queue under Data", () => {
    const model = buildWorkspaceNavViewModel("/data/evidence");
    const data = model.items.find((item) => item.key === "data");

    expect(data?.subItems.find((item) => item.route === "/data/evidence")).toMatchObject({
      label: "Evidence",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Evidence, current page"
    });
  });

  it("surfaces the provider catalog lane under Data", () => {
    const model = buildWorkspaceNavViewModel("/data/providers");
    const data = model.items.find((item) => item.key === "data");

    expect(data?.subItems[1]).toMatchObject({
      label: "Providers",
      route: "/data/providers",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Providers, current page"
    });
  });

  it("preserves operating scope across workspace and subroute navigation", () => {
    const model = buildWorkspaceNavViewModel("/data/quotes", undefined, "?symbol=aapl&provider=alpaca");
    const trading = model.items.find((item) => item.key === "trading");
    const data = model.items.find((item) => item.key === "data");
    const quotes = data?.subItems.find((item) => item.label === "Live quotes");

    expect(model.operatingScopeLabel).toBe("Subject: AAPL / Provider: alpaca");
    expect(trading).toMatchObject({
      route: "/trading?symbol=AAPL&provider=alpaca",
      ariaLabel: "Open Trading workspace, Review, preserving Subject: AAPL / Provider: alpaca"
    });
    expect(data).toMatchObject({
      route: "/data?symbol=AAPL&provider=alpaca",
      ariaLabel: "Data workspace, active section, Live, preserving Subject: AAPL / Provider: alpaca"
    });
    expect(quotes).toMatchObject({
      route: "/data/quotes?symbol=AAPL&provider=alpaca",
      active: true,
      ariaLabel: "Live quotes, current page, preserving Subject: AAPL / Provider: alpaca"
    });
  });

  it("uses stored operating scope when the current route has no scope query", () => {
    const model = buildWorkspaceNavViewModel("/portfolio", undefined, "", {
      fundAccountId: "fund-001",
      runId: "run-44"
    });

    expect(model.currentWorkspace).toMatchObject({
      route: "/portfolio?fundAccountId=fund-001&runId=run-44",
      routeAriaLabel: "Scoped route /portfolio?fundAccountId=fund-001&runId=run-44"
    });
    expect(model.items.find((item) => item.key === "accounting")).toMatchObject({
      route: "/accounting?fundAccountId=fund-001&runId=run-44",
      ariaLabel: "Open Accounting workspace, Review, preserving Account: fund-001 / Run: run-44"
    });
  });
});
