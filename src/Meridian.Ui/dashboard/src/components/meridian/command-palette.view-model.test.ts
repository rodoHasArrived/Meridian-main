import { describe, expect, it } from "vitest";
import { buildCommandPaletteViewModel } from "@/components/meridian/command-palette.view-model";

describe("command palette view model", () => {
  it("marks the current workspace from the active route", () => {
    const model = buildCommandPaletteViewModel("/settings/integrations");

    expect(model.itemCountLabel).toBe("7 workspaces");
    expect(model.commandListLabel).toBe("7 workspace commands");
    expect(model.activeWorkspaceLabel).toBe("Current: Settings");
    expect(model.routeSummary).toBe("Route to a canonical operator workspace. Current: Settings.");
    expect(model.initialFocusItemId).toBe("settings");
    expect(model.items.find((item) => item.id === "settings")).toMatchObject({
      route: "/settings",
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
});
