import { describe, expect, it } from "vitest";
import { buildWorkspaceHeaderViewModel } from "@/components/meridian/workspace-header.view-model";
import { workspaceForKey } from "@/lib/workspace";
import type { SessionInfo } from "@/types";

const session: SessionInfo = {
  displayName: "Ops Desk",
  role: "Operator",
  environment: "paper",
  activeWorkspace: "trading",
  commandCount: 8
};

describe("workspace header view model", () => {
  it("builds global workspace context and a disabled refresh action while loading", () => {
    const model = buildWorkspaceHeaderViewModel({
      workspace: workspaceForKey("trading"),
      session,
      canRefresh: true,
      refreshing: true
    });

    expect(model.title).toBe("Trading Workstation");
    expect(model.eyebrow).toBe("Workspace");
    expect(model.badges.map((badge) => badge.id)).toEqual(["workspace-maturity"]);
    expect(model.badges).toContainEqual({
      id: "workspace-maturity",
      label: "Available",
      variant: "default",
      ariaLabel: "Trading product maturity Available"
    });
    expect(model.metaItems).toEqual([]);
    expect(model.sessionLabel).toBe("Ops Desk");
    expect(model.sessionRoleLabel).toBe("Operator");
    expect(model.refreshAction).toEqual({
      label: "Refreshing",
      ariaLabel: "Refreshing Trading workspace data",
      title: "Trading workspace data is refreshing",
      disabled: true,
      disabledReason: "Trading workspace data is refreshing.",
      busy: true
    });
    expect(model.liveAnnouncement).toBe("Refreshing Trading workspace data.");
    expect(model.ariaBusy).toBe(true);
  });

  it("keeps pending session and setup status explicit", () => {
    const model = buildWorkspaceHeaderViewModel({
      workspace: workspaceForKey("settings"),
      session: null,
      canRefresh: false
    });

    expect(model.badges.map((badge) => badge.id)).toEqual(["workspace-maturity"]);
    expect(model.metaItems).toEqual([]);
    expect(model.badges).toContainEqual({
      id: "workspace-maturity",
      label: "Setup",
      variant: "outline",
      ariaLabel: "Settings product maturity Setup"
    });
    expect(model.sessionLabel).toBe("Loading session");
    expect(model.sessionRoleLabel).toBeNull();
    expect(model.sessionPillAriaLabel).toBe("Session context loading");
    expect(model.refreshAction).toBeNull();
  });

  it("maps the product maturity taxonomy to semantic badge tones", () => {
    expect(
      buildWorkspaceHeaderViewModel({
        workspace: workspaceForKey("portfolio"),
        session,
        canRefresh: true
      }).badges.find((badge) => badge.id === "workspace-maturity")?.variant
    ).toBe("warning");

    expect(
      buildWorkspaceHeaderViewModel({
        workspace: workspaceForKey("settings"),
        session,
        canRefresh: true
      }).badges.find((badge) => badge.id === "workspace-maturity")?.variant
    ).toBe("outline");

    expect(
      buildWorkspaceHeaderViewModel({
        workspace: workspaceForKey("data"),
        session,
        canRefresh: true
      }).badges.find((badge) => badge.id === "workspace-maturity")?.variant
    ).toBe("default");
  });

  it("keeps refresh enabled without a disabled reason when data is idle", () => {
    const model = buildWorkspaceHeaderViewModel({
      workspace: workspaceForKey("portfolio"),
      session,
      canRefresh: true,
      refreshing: false
    });

    expect(model.refreshAction).toMatchObject({
      label: "Refresh",
      ariaLabel: "Refresh Portfolio workspace data",
      disabled: false,
      disabledReason: null,
      busy: false
    });
    expect(model.ariaBusy).toBe(false);
  });
});
