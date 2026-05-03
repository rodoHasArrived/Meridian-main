import { describe, expect, it } from "vitest";
import {
  buildWorkspacePlaceholderViewModel,
  formatHeartbeat
} from "@/screens/workspace-placeholder-screen.view-model";
import { workspaceForKey } from "@/lib/workspace";
import type { SessionInfo, SystemOverviewResponse } from "@/types";

const session: SessionInfo = {
  displayName: "Ops Desk",
  role: "Operator",
  environment: "paper",
  activeWorkspace: "trading",
  commandCount: 7
};

const overview: SystemOverviewResponse = {
  systemStatus: "Healthy",
  providersOnline: 3,
  providersTotal: 4,
  activeRuns: 2,
  openPositions: 5,
  activeBackfills: 1,
  symbolsMonitored: 128,
  storageHealth: "Healthy",
  lastHeartbeatUtc: "2026-01-02T03:04:05Z",
  metrics: [],
  recentEvents: []
};

describe("workspace placeholder view model", () => {
  it("builds portfolio-specific pending guidance and next actions", () => {
    const model = buildWorkspacePlaceholderViewModel({
      workspace: workspaceForKey("portfolio"),
      session,
      overview
    });

    expect(model.title).toBe("Portfolio route is available");
    expect(model.route).toBe("/portfolio");
    expect(model.routeStatus).toBe("Reserved pending surface");
    expect(model.statusCells).toContainEqual({
      id: "session",
      label: "Session",
      value: "Ops Desk - Operator",
      ariaLabel: "Session: Ops Desk - Operator"
    });
    expect(model.telemetryCells).toContainEqual({
      id: "last-heartbeat",
      label: "Last heartbeat",
      value: "Jan 02, 2026 03:04 UTC",
      ariaLabel: "Last heartbeat: Jan 02, 2026 03:04 UTC"
    });
    expect(model.routeRegionLabel).toBe("Portfolio route status");
    expect(model.pendingRegionLabel).toBe("Portfolio pending workspace guidance");
    expect(model.actionsLabel).toBe("Portfolio temporary workflow actions");
    expect(model.telemetryLabel).toBe("Portfolio route telemetry");
    expect(model.actions.map((action) => action.route)).toEqual(["/trading", "/accounting", "/strategy"]);
    expect(model.actions[0]).toMatchObject({
      detailId: "placeholder-action-trading-readiness-detail",
      routeLabel: "Route /trading",
      ariaLabel:
        "Review trading readiness. Check active sessions, orders, fills, replay evidence, and promotion blockers. Opens /trading."
    });
  });

  it("builds settings-specific setup and control recommendations", () => {
    const model = buildWorkspacePlaceholderViewModel({
      workspace: workspaceForKey("settings"),
      session: null,
      overview: null
    });

    expect(model.pendingTitle).toBe("Settings surface is pending");
    expect(model.statusCells).toContainEqual({
      id: "session",
      label: "Session",
      value: "Session loading",
      ariaLabel: "Session: Session loading"
    });
    expect(model.telemetryCells).toContainEqual({
      id: "last-heartbeat",
      label: "Last heartbeat",
      value: "No heartbeat loaded",
      ariaLabel: "Last heartbeat: No heartbeat loaded"
    });
    expect(model.actions.map((action) => action.label)).toEqual([
      "Review provider setup",
      "Check session readiness",
      "Review control evidence"
    ]);
  });

  it("formats invalid heartbeat values explicitly", () => {
    expect(formatHeartbeat("not-a-date")).toBe("Invalid heartbeat timestamp");
  });
});
