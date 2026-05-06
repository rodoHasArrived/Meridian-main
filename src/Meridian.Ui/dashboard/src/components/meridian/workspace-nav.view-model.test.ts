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
});
