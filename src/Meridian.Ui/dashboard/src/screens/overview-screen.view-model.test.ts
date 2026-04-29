import { describe, expect, it } from "vitest";
import {
  buildOverviewStatusState,
  buildOverviewWorkspaceLinks
} from "@/screens/overview-screen.view-model";
import type { SystemOverviewResponse } from "@/types";

const overview: SystemOverviewResponse = {
  systemStatus: "Degraded",
  providersOnline: 2,
  providersTotal: 4,
  activeRuns: 3,
  openPositions: 5,
  activeBackfills: 1,
  symbolsMonitored: 42,
  storageHealth: "Warning",
  lastHeartbeatUtc: "2026-04-28T18:15:00Z",
  metrics: [],
  recentEvents: []
};

describe("overview-screen view model", () => {
  it("derives status, fallback stats, and empty activity copy", () => {
    const state = buildOverviewStatusState({
      current: overview,
      refreshing: false,
      refreshError: null,
      refreshedAt: null
    });

    expect(state.statusLabel).toBe("System Degraded");
    expect(state.providerSummary).toBe("2 of 4 providers online");
    expect(state.storageLabel).toBe("Warning");
    expect(state.hasMetrics).toBe(false);
    expect(state.hasEvents).toBe(false);
    expect(state.activityEmptyText).toBe("No recent events.");
    expect(state.fallbackStats).toContainEqual({
      id: "providers",
      label: "Providers Online",
      value: "2 / 4",
      tone: "warning"
    });
    expect(state.fallbackStats).toContainEqual({
      id: "backfills",
      label: "Active Backfills",
      value: "1",
      tone: "warning"
    });
    expect(state.workspaceSummary).toBe("7 canonical operator routes. Legacy routes redirect to their canonical workspaces.");
    expect(state.workspaceLinks.map((workspace) => workspace.label)).toEqual([
      "Trading",
      "Portfolio",
      "Accounting",
      "Reporting",
      "Strategy",
      "Data",
      "Settings"
    ]);
  });

  it("surfaces refresh failures while keeping stale data available", () => {
    const state = buildOverviewStatusState({
      current: overview,
      refreshing: false,
      refreshError: "Provider offline",
      refreshedAt: null
    });

    expect(state.current).toBe(overview);
    expect(state.refreshErrorText).toBe("Refresh failed: Provider offline. Showing the last known status.");
    expect(state.refreshAnnouncement).toBe(state.refreshErrorText);
    expect(state.refreshButtonLabel).toBe("Refresh");
  });

  it("announces active refresh state", () => {
    const state = buildOverviewStatusState({
      current: null,
      refreshing: true,
      refreshError: null,
      refreshedAt: null
    });

    expect(state.statusLabel).toBe("Connecting to system...");
    expect(state.refreshButtonLabel).toBe("Refreshing...");
    expect(state.refreshAriaLabel).toBe("Refreshing system status");
    expect(state.refreshAnnouncement).toBe("Refreshing system status.");
    expect(state.activityEmptyText).toBe("Loading activity feed...");
  });

  it("builds canonical workspace links instead of legacy overview cards", () => {
    const links = buildOverviewWorkspaceLinks();

    expect(links).toHaveLength(7);
    expect(links.map((link) => link.href)).toEqual([
      "/trading",
      "/portfolio",
      "/accounting",
      "/reporting",
      "/strategy",
      "/data",
      "/settings"
    ]);
    expect(links.some((link) => link.label === "Research")).toBe(false);
    expect(links.some((link) => link.href === "/data-operations")).toBe(false);
    expect(links.find((link) => link.id === "trading")?.badgeVariant).toBe("warning");
    expect(links.find((link) => link.id === "strategy")?.badgeVariant).toBe("paper");
    expect(links.find((link) => link.id === "data")?.badgeVariant).toBe("live");
    expect(links[0].ariaLabel).toContain("Open Trading workspace");
  });
});
