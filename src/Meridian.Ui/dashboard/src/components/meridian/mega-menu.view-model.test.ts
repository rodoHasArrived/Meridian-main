import { describe, expect, it, vi } from "vitest";
import { buildMegaMenuViewModel, resolveMegaMenuKeyCommand } from "@/components/meridian/mega-menu.view-model";

describe("mega menu view model", () => {
  it("derives active section and route labels from the current path", () => {
    const model = buildMegaMenuViewModel({
      pathname: "/data/backfills/queued",
      open: true,
      openMenu: vi.fn(),
      closeMenu: vi.fn(),
      toggleMenu: vi.fn()
    });

    const dataSection = model.sections.find((section) => section.key === "data");
    const backfillLink = dataSection?.links.find((link) => link.route === "/data/backfills");

    expect(model.triggerAriaLabel).toBe("Close workspace navigation menu");
    expect(model.triggerExpanded).toBe(true);
    expect(model.triggerControlsId).toBe(model.panelId);
    expect(dataSection).toMatchObject({
      active: true,
      ariaCurrent: "page"
    });
    expect(backfillLink).toMatchObject({
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Backfill queues, current route, Data workspace"
    });
    expect(dataSection?.links.find((link) => link.route === "/data")).toMatchObject({
      active: false,
      ariaCurrent: undefined,
      ariaLabel: "Open Provider posture, Data workspace"
    });
  });

  it("keeps top-level routes exact so child routes own current-route state", () => {
    const model = buildMegaMenuViewModel({
      pathname: "/trading/readiness",
      open: false,
      openMenu: vi.fn(),
      closeMenu: vi.fn(),
      toggleMenu: vi.fn()
    });

    const tradingSection = model.sections.find((section) => section.key === "trading");

    expect(model.triggerAriaLabel).toBe("Open workspace navigation menu");
    expect(model.triggerExpanded).toBe(false);
    expect(tradingSection).toMatchObject({ active: true, ariaCurrent: "page" });
    expect(tradingSection?.links.find((link) => link.route === "/trading")).toMatchObject({
      active: false,
      ariaCurrent: undefined
    });
    expect(tradingSection?.links.find((link) => link.route === "/trading/readiness")).toMatchObject({
      active: true,
      ariaCurrent: "page"
    });
  });

  it("surfaces implemented high-frequency workflow routes", () => {
    const model = buildMegaMenuViewModel({
      pathname: "/reporting/evidence",
      open: true,
      openMenu: vi.fn(),
      closeMenu: vi.fn(),
      toggleMenu: vi.fn()
    });

    const portfolioSection = model.sections.find((section) => section.key === "portfolio");
    const accountingSection = model.sections.find((section) => section.key === "accounting");
    const reportingSection = model.sections.find((section) => section.key === "reporting");
    const strategySection = model.sections.find((section) => section.key === "strategy");
    const dataSection = model.sections.find((section) => section.key === "data");

    expect(portfolioSection?.links.map((link) => link.route)).toContain("/portfolio/brokerage-sync");
    expect(accountingSection?.links.map((link) => link.route)).toContain("/accounting/security-master");
    expect(reportingSection?.links.map((link) => link.route)).toEqual([
      "/reporting",
      "/reporting/report-packs",
      "/reporting/evidence",
      "/reporting/exports"
    ]);
    expect(strategySection?.links.map((link) => link.route)).toEqual([
      "/strategy",
      "/strategy/designer",
      "/strategy/covered-call",
      "/strategy/promotions",
      "/strategy/research",
      "/strategy/quant-lab"
    ]);
    expect(dataSection?.links.map((link) => link.route)).toEqual([
      "/data",
      "/data/watchlist",
      "/data/quotes",
      "/data/alerts",
      "/data/backfills"
    ]);
    expect(reportingSection?.links.find((link) => link.route === "/reporting/evidence")).toMatchObject({
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Evidence, current route, Reporting workspace"
    });
    expect(reportingSection?.links.find((link) => link.route === "/reporting/report-packs")).toMatchObject({
      active: false,
      ariaCurrent: undefined,
      ariaLabel: "Open Report packs, Reporting workspace"
    });
    expect(strategySection?.links.find((link) => link.route === "/strategy/covered-call")).toMatchObject({
      active: false,
      ariaCurrent: undefined,
      ariaLabel: "Open Covered call, Strategy workspace"
    });
    expect(dataSection?.links.find((link) => link.route === "/data/alerts")).toMatchObject({
      active: false,
      ariaCurrent: undefined,
      ariaLabel: "Open Price alerts, Data workspace"
    });
  });

  it("marks covered call and price alerts as current route links", () => {
    const strategyModel = buildMegaMenuViewModel({
      pathname: "/strategy/covered-call",
      open: true,
      openMenu: vi.fn(),
      closeMenu: vi.fn(),
      toggleMenu: vi.fn()
    });
    const dataModel = buildMegaMenuViewModel({
      pathname: "/data/alerts",
      open: true,
      openMenu: vi.fn(),
      closeMenu: vi.fn(),
      toggleMenu: vi.fn()
    });

    expect(strategyModel.sections.find((section) => section.key === "strategy")?.links.find((link) => link.route === "/strategy/covered-call")).toMatchObject({
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Covered call, current route, Strategy workspace"
    });
    expect(dataModel.sections.find((section) => section.key === "data")?.links.find((link) => link.route === "/data/alerts")).toMatchObject({
      active: true,
      ariaCurrent: "page",
      ariaLabel: "Price alerts, current route, Data workspace"
    });
  });

  it("preserves operating scope in menu routes by workspace capability", () => {
    const model = buildMegaMenuViewModel({
      pathname: "/data/quotes",
      search: "?symbol=aapl&provider=alpaca&from=2026-05-01&to=2026-05-15",
      open: true,
      openMenu: vi.fn(),
      closeMenu: vi.fn(),
      toggleMenu: vi.fn()
    });

    const tradingReadiness = model.sections
      .find((section) => section.key === "trading")
      ?.links.find((link) => link.label === "Readiness");
    const dataQuotes = model.sections
      .find((section) => section.key === "data")
      ?.links.find((link) => link.label === "Live quotes");
    const settingsIntegrations = model.sections
      .find((section) => section.key === "settings")
      ?.links.find((link) => link.label === "Integrations");

    expect(model.operatingScopeLabel).toBe("Subject: AAPL / Provider: alpaca / Window: 2026-05-01 to 2026-05-15");
    expect(tradingReadiness).toMatchObject({
      route: "/trading/readiness?symbol=AAPL&provider=alpaca&from=2026-05-01&to=2026-05-15",
      ariaLabel: "Open Readiness, Trading workspace, preserving Subject: AAPL / Provider: alpaca / Window: 2026-05-01 to 2026-05-15"
    });
    expect(dataQuotes).toMatchObject({
      route: "/data/quotes?symbol=AAPL&provider=alpaca&from=2026-05-01&to=2026-05-15",
      ariaLabel: "Live quotes, current route, Data workspace, preserving Subject: AAPL / Provider: alpaca / Window: 2026-05-01 to 2026-05-15"
    });
    expect(settingsIntegrations).toMatchObject({
      route: "/settings/integrations?provider=alpaca",
      ariaLabel: "Open Integrations, Settings workspace, preserving Provider: alpaca"
    });
  });

  it("resolves close and focus-loop keyboard commands", () => {
    expect(resolveMegaMenuKeyCommand({ key: "Escape", shiftKey: false, focusBoundary: "middle" })).toBe("close");
    expect(resolveMegaMenuKeyCommand({ key: "Tab", shiftKey: false, focusBoundary: "last" })).toBe("focus-first");
    expect(resolveMegaMenuKeyCommand({ key: "Tab", shiftKey: true, focusBoundary: "first" })).toBe("focus-last");
    expect(resolveMegaMenuKeyCommand({ key: "Tab", shiftKey: false, focusBoundary: "middle" })).toBeNull();
    expect(resolveMegaMenuKeyCommand({ key: "ArrowDown", shiftKey: false, focusBoundary: "middle" })).toBeNull();
  });
});
