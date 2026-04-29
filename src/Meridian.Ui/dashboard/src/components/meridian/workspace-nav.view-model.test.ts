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
      ariaLabel: "Portfolio workspace, current route, Preview"
    });
    expect(model.items.find((item) => item.key === "trading")).toMatchObject({
      route: "/trading",
      active: false,
      ariaCurrent: undefined,
      statusLabel: "Review",
      ariaLabel: "Open Trading workspace, Review"
    });
  });

  it("normalizes legacy workspace aliases for current-route state", () => {
    const model = buildWorkspaceNavViewModel("/data-operations/backfills");

    expect(model.items.find((item) => item.key === "data")).toMatchObject({
      active: true,
      ariaCurrent: "page",
      statusLabel: "Live · Current"
    });
  });
});
