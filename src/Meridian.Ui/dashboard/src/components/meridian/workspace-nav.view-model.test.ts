import { describe, expect, it } from "vitest";
import { buildWorkspaceNavViewModel } from "@/components/meridian/workspace-nav.view-model";

describe("workspace nav view model", () => {
  it("marks the active canonical workspace route", () => {
    const model = buildWorkspaceNavViewModel("/portfolio/positions");

    expect(model.brandTitle).toBe("Meridian");
    expect(model.items).toHaveLength(7);
    expect(model.items.find((item) => item.key === "portfolio")).toMatchObject({
      route: "/portfolio",
      active: true,
      ariaCurrent: "page",
      statusLabel: "Preview · Current",
      statusTone: "preview",
      ariaLabel: "Portfolio workspace, current route, Preview"
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
  });

  it("normalizes legacy workspace aliases for current-route state", () => {
    const model = buildWorkspaceNavViewModel("/data-operations/backfills");

    expect(model.items.find((item) => item.key === "data")).toMatchObject({
      active: true,
      ariaCurrent: "page",
      statusLabel: "Live · Current",
      statusTone: "live"
    });
    expect(model.currentWorkspace).toMatchObject({
      label: "Data",
      statusLabel: "Live posture",
      statusTone: "live"
    });
  });

  it("surfaces the accounting ledger lane as a first-class subroute", () => {
    const model = buildWorkspaceNavViewModel("/accounting");
    const accounting = model.items.find((item) => item.key === "accounting");

    expect(accounting?.subItems.map((item) => item.route)).toEqual([
      "/accounting",
      "/accounting/reconciliation",
      "/accounting/security-master",
      "/accounting/approvals"
    ]);
    expect(accounting?.subItems[0]).toMatchObject({
      label: "Ledger",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Ledger, current page"
    });
  });

  it("surfaces the implemented covered-call backtest route under Strategy", () => {
    const model = buildWorkspaceNavViewModel("/strategy/covered-call");
    const strategy = model.items.find((item) => item.key === "strategy");

    expect(strategy?.subItems.map((item) => item.route)).toEqual([
      "/strategy/designer",
      "/strategy/covered-call",
      "/strategy/promotions",
      "/strategy/research",
      "/strategy/quant-lab"
    ]);
    expect(strategy?.subItems.find((item) => item.route === "/strategy/covered-call")).toMatchObject({
      label: "Covered call",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Covered call, current page"
    });
  });

  it("surfaces the implemented price-alerts route under Data", () => {
    const model = buildWorkspaceNavViewModel("/data/alerts");
    const data = model.items.find((item) => item.key === "data");

    expect(data?.subItems.map((item) => item.route)).toEqual([
      "/data/watchlist",
      "/data/quotes",
      "/data/alerts",
      "/data/backfills"
    ]);
    expect(data?.subItems.find((item) => item.route === "/data/alerts")).toMatchObject({
      label: "Price alerts",
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Price alerts, current page"
    });
  });
});
