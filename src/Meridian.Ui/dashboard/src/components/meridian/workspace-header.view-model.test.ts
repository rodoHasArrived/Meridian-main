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
    expect(model.eyebrow).toBe("Meridian workspace");
    expect(model.badges).toContainEqual({
      id: "environment",
      label: "PAPER",
      variant: "paper",
      ariaLabel: "paper environment"
    });
    expect(model.badges).toContainEqual({
      id: "workspace-status",
      label: "Review",
      variant: "warning",
      ariaLabel: "Trading workspace status Review"
    });
    expect(model.sessionLabel).toBe("Ops Desk");
    expect(model.sessionRoleLabel).toBe("Operator");
    expect(model.refreshAction).toEqual({
      label: "Refreshing",
      ariaLabel: "Refreshing Trading workspace data",
      title: "Trading workspace data is refreshing",
      disabled: true
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

    expect(model.badges.map((badge) => badge.id)).toEqual(["workspace", "workspace-status"]);
    expect(model.badges).toContainEqual({
      id: "workspace-status",
      label: "Setup",
      variant: "outline",
      ariaLabel: "Settings workspace status Setup"
    });
    expect(model.sessionLabel).toBe("Loading session");
    expect(model.sessionRoleLabel).toBeNull();
    expect(model.sessionPillAriaLabel).toBe("Session context loading");
    expect(model.refreshAction).toBeNull();
  });

  it("maps live and paper workspace statuses to semantic badge tones", () => {
    expect(
      buildWorkspaceHeaderViewModel({
        workspace: workspaceForKey("data"),
        session,
        canRefresh: true
      }).badges.find((badge) => badge.id === "workspace-status")?.variant
    ).toBe("success");

    expect(
      buildWorkspaceHeaderViewModel({
        workspace: workspaceForKey("strategy"),
        session,
        canRefresh: true
      }).badges.find((badge) => badge.id === "workspace-status")?.variant
    ).toBe("paper");
  });
});
