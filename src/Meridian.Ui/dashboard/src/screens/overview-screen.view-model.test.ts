import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  buildOverviewActivityRows,
  buildOverviewBriefingItems,
  buildOverviewPriorityRoutes,
  buildOverviewStatusBanner,
  buildOverviewStatusState,
  buildOverviewWorkspaceLinks,
  useOverviewStatusViewModel,
  type OverviewRefreshFetcher
} from "@/screens/overview-screen.view-model";
import type { SessionInfo, SystemOverviewResponse } from "@/types";

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

const session: SessionInfo = {
  displayName: "Meridian Ops",
  role: "Operator",
  environment: "paper",
  activeWorkspace: "trading",
  commandCount: 9
};

describe("overview-screen view model", () => {
  it("derives status, fallback stats, and empty activity copy", () => {
    const state = buildOverviewStatusState({
      current: overview,
      session: null,
      refreshing: false,
      refreshError: null,
      refreshedAt: null
    });

    expect(state.statusLabel).toBe("System Degraded");
    expect(state.statusBanner.role).toBe("alert");
    expect(state.statusBanner.ariaLive).toBe("assertive");
    expect(state.statusBanner.titleId).toBe("overview-status-title");
    expect(state.statusBanner.detailId).toBe("overview-status-detail");
    expect(state.statusBanner.ariaLabel).toContain("System Degraded");
    expect(state.statusBanner.detailText).toContain("2 of 4 providers online");
    expect(state.providerSummary).toBe("2 of 4 providers online");
    expect(state.storageLabel).toBe("Warning");
    expect(state.hasMetrics).toBe(false);
    expect(state.hasEvents).toBe(false);
    expect(state.activityEmptyText).toBe("No recent events.");
    expect(state.activityRows).toEqual([]);
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
      session: null,
      refreshing: false,
      refreshError: "Provider offline",
      refreshedAt: null
    });

    expect(state.current).toBe(overview);
    expect(state.refreshErrorText).toBe("Refresh failed: Provider offline. Showing the last known status.");
    expect(state.refreshAnnouncement).toBe(state.refreshErrorText);
    expect(state.statusBanner.role).toBe("alert");
    expect(state.statusBanner.ariaLabel).toContain("Refresh failed: Provider offline");
    expect(state.refreshButtonLabel).toBe("Refresh");
  });

  it("announces active refresh state", () => {
    const state = buildOverviewStatusState({
      current: null,
      session: null,
      refreshing: true,
      refreshError: null,
      refreshedAt: null
    });

    expect(state.statusLabel).toBe("Connecting to system...");
    expect(state.statusBanner.role).toBe("status");
    expect(state.statusBanner.ariaLive).toBe("polite");
    expect(state.statusBanner.detailText).toBe("Waiting for the workstation status payload.");
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

  it("derives operator briefing tiles from session and status state", () => {
    const briefingItems = buildOverviewBriefingItems({
      session,
      current: overview,
      providerSummary: "2 of 4 providers online",
      storageLabel: "Warning",
      lastHeartbeatLabel: "11:15 AM",
      refreshErrorText: null
    });

    expect(briefingItems).toContainEqual({
      id: "session",
      label: "Session",
      value: "Meridian Ops",
      detail: "Operator - 9 commands ready",
      tone: "default",
      badgeVariant: null,
      ariaLabel: "Session: Meridian Ops. Operator - 9 commands ready"
    });
    expect(briefingItems).toContainEqual({
      id: "environment",
      label: "Operating mode",
      value: "paper",
      detail: "Current route trading",
      tone: "success",
      badgeVariant: "paper",
      ariaLabel: "Operating mode: paper. Current route trading"
    });
    expect(briefingItems.find((item) => item.id === "providers")?.tone).toBe("warning");
  });

  it("derives priority route presentation copy from canonical workspace links", () => {
    const routes = buildOverviewPriorityRoutes(buildOverviewWorkspaceLinks());

    expect(routes.map((route) => route.id)).toEqual(["trading", "accounting", "reporting"]);
    expect(routes[0]).toMatchObject({
      eyebrow: "Execution posture",
      title: "Keep the active session ready",
      buttonLabel: "Open trading cockpit",
      href: "/trading"
    });
    expect(routes[1]).toMatchObject({
      buttonLabel: "Open accounting lane",
      href: "/accounting"
    });
    expect(routes[2]).toMatchObject({
      buttonLabel: "Open reporting lane",
      href: "/reporting"
    });
  });

  it("derives activity row status, fallback timestamps, and accessible summaries", () => {
    const rows = buildOverviewActivityRows([
      {
        id: "evt-1",
        type: "warning",
        message: "Brokerage sync delayed.",
        source: "Provider health",
        timestamp: "not-a-date"
      },
      {
        id: "evt-2",
        type: "error",
        message: "Storage verification failed.",
        source: " ",
        timestamp: "2026-04-28T18:15:00Z"
      }
    ]);

    expect(rows[0]).toMatchObject({
      id: "evt-1",
      typeLabel: "Warning",
      statusCode: "OBS",
      badgeVariant: "warning",
      tone: "warning",
      source: "Provider health",
      timestampLabel: "Unavailable"
    });
    expect(rows[0].ariaLabel).toBe("Warning event from Provider health at Unavailable: Brokerage sync delayed.");
    expect(rows[1]).toMatchObject({
      typeLabel: "Error",
      statusCode: "ERR",
      badgeVariant: "danger",
      tone: "danger",
      source: "Unknown source"
    });
  });

  it("derives healthy status banner semantics as a polite status region", () => {
    const healthy = buildOverviewStatusBanner({
      current: { ...overview, systemStatus: "Healthy", storageHealth: "Healthy", providersOnline: 4 },
      statusLabel: "All Systems Healthy",
      providerSummary: "4 of 4 providers online",
      storageLabel: "Healthy",
      lastHeartbeatLabel: "10:15 AM",
      refreshErrorText: null
    });

    expect(healthy.role).toBe("status");
    expect(healthy.ariaLive).toBe("polite");
    expect(healthy.detailText).toBe("4 of 4 providers online. Storage Healthy. Last heartbeat 10:15 AM.");
    expect(healthy.ariaLabel).toContain("All Systems Healthy");
  });

  it("ignores an older manual refresh that resolves after a newer refresh", async () => {
    const olderRefresh = createDeferred<SystemOverviewResponse>();
    const newerRefresh = createDeferred<SystemOverviewResponse>();
    const fetchSystemStatus = vi.fn<OverviewRefreshFetcher>()
      .mockReturnValueOnce(olderRefresh.promise)
      .mockReturnValueOnce(newerRefresh.promise);

    const { result } = renderHook(() => useOverviewStatusViewModel(overview, session, fetchSystemStatus));

    let olderCommand!: Promise<void>;
    let newerCommand!: Promise<void>;
    act(() => {
      olderCommand = result.current.refresh();
      newerCommand = result.current.refresh();
    });
    await waitFor(() => expect(fetchSystemStatus).toHaveBeenCalledTimes(2));

    await act(async () => {
      newerRefresh.resolve({ ...overview, systemStatus: "Healthy", providersOnline: 4, providersTotal: 4 });
      await newerCommand;
    });

    expect(result.current.statusLabel).toBe("All Systems Healthy");
    expect(result.current.refreshing).toBe(false);

    await act(async () => {
      olderRefresh.resolve({ ...overview, systemStatus: "Offline", providersOnline: 0 });
      await olderCommand;
      await flushAsync();
    });

    expect(result.current.statusLabel).toBe("All Systems Healthy");
    expect(result.current.providerSummary).toBe("4 of 4 providers online");
  });

  it("does not publish a manual refresh after the overview unmounts", async () => {
    const pendingRefresh = createDeferred<SystemOverviewResponse>();
    const fetchSystemStatus = vi.fn<OverviewRefreshFetcher>().mockReturnValueOnce(pendingRefresh.promise);
    const { result, unmount } = renderHook(() => useOverviewStatusViewModel(overview, session, fetchSystemStatus));

    let refreshCommand!: Promise<void>;
    act(() => {
      refreshCommand = result.current.refresh();
    });
    await waitFor(() => expect(fetchSystemStatus).toHaveBeenCalledTimes(1));

    unmount();
    await act(async () => {
      pendingRefresh.resolve({ ...overview, systemStatus: "Offline", providersOnline: 0 });
      await refreshCommand;
      await flushAsync();
    });

    expect(result.current.statusLabel).toBe("System Degraded");
    expect(result.current.providerSummary).toBe("2 of 4 providers online");
  });
});

interface Deferred<T> {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (reason?: unknown) => void;
}

function createDeferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

async function flushAsync() {
  await Promise.resolve();
  await Promise.resolve();
}
