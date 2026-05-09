import { describe, expect, it, vi } from "vitest";
import { buildMegaMenuViewModel, resolveMegaMenuKeyCommand } from "@/components/meridian/mega-menu.view-model";

describe("mega menu view model", () => {
  it("derives active section and route labels from the current path", () => {
    const model = buildMegaMenuViewModel({
      pathname: "/data/backfill/queued",
      open: true,
      openMenu: vi.fn(),
      closeMenu: vi.fn(),
      toggleMenu: vi.fn()
    });

    const dataSection = model.sections.find((section) => section.key === "data");
    const backfillLink = dataSection?.links.find((link) => link.route === "/data/backfill");

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

  it("resolves close and focus-loop keyboard commands", () => {
    expect(resolveMegaMenuKeyCommand({ key: "Escape", shiftKey: false, focusBoundary: "middle" })).toBe("close");
    expect(resolveMegaMenuKeyCommand({ key: "Tab", shiftKey: false, focusBoundary: "last" })).toBe("focus-first");
    expect(resolveMegaMenuKeyCommand({ key: "Tab", shiftKey: true, focusBoundary: "first" })).toBe("focus-last");
    expect(resolveMegaMenuKeyCommand({ key: "Tab", shiftKey: false, focusBoundary: "middle" })).toBeNull();
    expect(resolveMegaMenuKeyCommand({ key: "ArrowDown", shiftKey: false, focusBoundary: "middle" })).toBeNull();
  });
});
