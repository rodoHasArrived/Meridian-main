import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  buildOverviewActivityDetail,
  buildOverviewActivityRows,
  buildOverviewBriefingItems,
  buildOverviewPortfolioPanel,
  buildOverviewPriorityRoutes,
  buildOverviewRefreshCommand,
  buildOverviewStatusBanner,
  buildOverviewStatusState,
  buildOverviewValueBlockers,
  buildOverviewWorkspaceLinks,
  useOverviewActivitySelectionViewModel,
  useOverviewStatusViewModel,
  type OverviewRefreshFetcher
} from "@/screens/overview-screen.view-model";
import type { SessionInfo, SystemOverviewResponse, TradingWorkspaceResponse } from "@/types";

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

const tradingWorkspace: TradingWorkspaceResponse = {
  metrics: [],
  positions: [],
  openOrders: [],
  fills: [],
  risk: {
    state: "Healthy",
    summary: "Paper account is inside guardrails.",
    netExposure: "$0",
    grossExposure: "$0",
    var95: "$0",
    maxDrawdown: "0%",
    buyingPowerUsed: "0%",
    activeGuardrails: []
  },
  brokerage: {
    provider: "Alpaca",
    account: "Paper",
    environment: "paper",
    connection: "Connected",
    lastHeartbeat: "2026-04-28T18:15:00Z",
    orderIngress: "Ready",
    fillFeed: "Ready",
    notes: "Ready for paper orders."
  }
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
    expect(state.statusBanner.detailParts).toEqual({
      providerSummary: "2 of 4 providers online",
      storageLabel: "Warning",
      storageClassName: "text-warning",
      lastHeartbeatLabel: state.lastHeartbeatLabel
    });
    expect(state.statusBanner.icon).toBe("warning");
    expect(state.statusBanner.containerClassName).toBe("border-warning/30 bg-warning/10");
    expect(state.statusBanner.iconClassName).toBe("text-warning");
    expect(state.statusBanner.titleClassName).toBe("text-warning");
    expect(state.providerSummary).toBe("2 of 4 providers online");
    expect(state.storageLabel).toBe("Warning");
    expect(state.lastHeartbeatLabel).toBe("Apr 28, 18:15 UTC");
    expect(state.hasMetrics).toBe(false);
    expect(state.hasEvents).toBe(false);
    expect(state.hasValueBlockers).toBe(true);
    expect(state.valueBlockerRegionLabel).toBe("3 readiness blockers");
    expect(state.valueBlockerSummary).toBe("3 blockers need attention before a confident operator handoff.");
    expect(state.valueBlockers.map((blocker) => blocker.id)).toEqual([
      "providers-degraded",
      "storage-warning",
      "backfills-active"
    ]);
    expect(state.activityEmptyText).toBe("No recent events.");
    expect(state.activityRows).toEqual([]);
    expect(state.fallbackStats).toContainEqual({
      id: "providers",
      label: "Providers Online",
      value: "2 / 4",
      delta: "2 offline",
      tone: "warning"
    });
    expect(state.fallbackStats).toContainEqual({
      id: "backfills",
      label: "Active Backfills",
      value: "1",
      delta: "1 active backfill",
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
    expect(state.statusBanner.icon).toBe("offline");
    expect(state.statusBanner.containerClassName).toBe("border-danger/35 bg-danger/10");
    expect(state.refreshButtonLabel).toBe("Refresh");
    expect(state.refreshCommand).toMatchObject({
      label: "Refresh",
      ariaLabel: "Refresh system status",
      busy: false,
      disabled: false,
      disabledReason: null
    });
  });

  it("does not crash when the host status payload omits optional overview collections", () => {
    const state = buildOverviewStatusState({
      current: {
        ...overview,
        lastHeartbeatUtc: undefined as unknown as string,
        metrics: undefined as unknown as SystemOverviewResponse["metrics"],
        recentEvents: undefined as unknown as SystemOverviewResponse["recentEvents"]
      },
      session: null,
      refreshing: false,
      refreshError: null,
      refreshedAt: null
    });

    expect(state.lastHeartbeatLabel).toBe("Unavailable");
    expect(state.hasMetrics).toBe(false);
    expect(state.hasEvents).toBe(false);
    expect(state.activityRows).toEqual([]);
    expect(state.statusBanner.detailText).toContain("Last heartbeat Unavailable");
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
    expect(state.statusBanner.detailParts).toBeNull();
    expect(state.statusBanner.icon).toBe("pending");
    expect(state.statusBanner.containerClassName).toBe("border-border/70 bg-secondary/25");
    expect(state.refreshButtonLabel).toBe("Refreshing...");
    expect(state.refreshAriaLabel).toBe("Refreshing system status");
    expect(state.refreshCommand).toEqual({
      label: "Refreshing...",
      ariaLabel: "Refreshing system status",
      busyLabel: "Refreshing...",
      disabled: true,
      disabledReason: "System status refresh is already in progress.",
      busy: true
    });
    expect(state.refreshAnnouncement).toBe("Refreshing system status.");
    expect(state.activityEmptyText).toBe("Loading activity feed...");
  });

  it("derives refresh command presentation state for idle and busy states", () => {
    expect(buildOverviewRefreshCommand(false)).toEqual({
      label: "Refresh",
      ariaLabel: "Refresh system status",
      busyLabel: null,
      disabled: false,
      disabledReason: null,
      busy: false
    });

    expect(buildOverviewRefreshCommand(true)).toEqual({
      label: "Refreshing...",
      ariaLabel: "Refreshing system status",
      busyLabel: "Refreshing...",
      disabled: true,
      disabledReason: "System status refresh is already in progress.",
      busy: true
    });
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

  it("moves first-run symbol setup ahead of default priority routes", () => {
    const routes = buildOverviewPriorityRoutes(buildOverviewWorkspaceLinks(), {
      ...overview,
      symbolsMonitored: 0
    });

    expect(routes.map((route) => route.id)).toEqual(["data", "trading", "accounting"]);
    expect(routes[0]).toMatchObject({
      eyebrow: "First-run data setup",
      title: "Seed a working watchlist",
      buttonLabel: "Open watchlist",
      href: "/data/watchlist",
      ariaLabel: "Open Data watchlist starter packs"
    });
    expect(routes[0].detail).toContain("No monitored symbols are loaded yet");
  });

  it("moves provider setup ahead of data setup when no provider baseline is available", () => {
    const routes = buildOverviewPriorityRoutes(buildOverviewWorkspaceLinks(), {
      ...overview,
      providersOnline: 0,
      providersTotal: 0,
      symbolsMonitored: 0
    });

    expect(routes.map((route) => route.id)).toEqual(["settings", "data", "trading"]);
    expect(routes[0]).toMatchObject({
      eyebrow: "Integration setup",
      title: "Connect provider baseline",
      buttonLabel: "Open setup checks",
      href: "/settings#alpaca-provider-setup",
      ariaLabel: "Open Alpaca paper provider setup checklist"
    });
    expect(routes[0].detail).toContain("No providers are configured yet");
  });

  it("derives first-run blocker repair links from the live overview snapshot", () => {
    const blockers = buildOverviewValueBlockers(
      {
        ...overview,
        providersOnline: 0,
        providersTotal: 0,
        symbolsMonitored: 0,
        storageHealth: "Critical",
        activeBackfills: 0
      },
      null
    );

    expect(blockers.map((blocker) => blocker.id)).toEqual([
      "providers-missing",
      "symbols-empty",
      "storage-critical"
    ]);
    expect(blockers[0]).toMatchObject({
      href: "/settings#alpaca-provider-setup",
      actionLabel: "Connect provider",
      badgeVariant: "danger",
      tone: "danger"
    });
    expect(blockers[1]).toMatchObject({
      href: "/data/watchlist",
      actionLabel: "Seed watchlist"
    });
    expect(blockers[2].detail).toContain("not safe to trust");
  });

  it("keeps the blocker panel clear when readiness prerequisites are healthy", () => {
    const state = buildOverviewStatusState({
      current: {
        ...overview,
        systemStatus: "Healthy",
        providersOnline: 4,
        providersTotal: 4,
        activeBackfills: 0,
        storageHealth: "Healthy"
      },
      session,
      refreshing: false,
      refreshError: null,
      refreshedAt: null
    });

    expect(state.hasValueBlockers).toBe(false);
    expect(state.valueBlockers).toEqual([]);
    expect(state.valueBlockerRegionLabel).toBe("0 readiness blockers");
    expect(state.valueBlockerSummary).toBe("No immediate readiness blockers detected. Continue with the priority routes below.");
  });

  it("projects route-backed portfolio empty-state actions", () => {
    expect(buildOverviewPortfolioPanel(null, null).emptyAction).toEqual({
      href: "/settings#alpaca-provider-setup",
      label: "Connect provider",
      ariaLabel: "Open Alpaca paper provider setup checklist from the empty portfolio panel"
    });

    expect(buildOverviewPortfolioPanel(tradingWorkspace, null).emptyAction).toEqual({
      href: "/trading",
      label: "Open trading cockpit",
      ariaLabel: "Open Trading cockpit from the empty portfolio positions panel"
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
      source: "Unknown source",
      timestampLabel: "Apr 28, 18:15 UTC"
    });
    expect(rows[1].ariaLabel).toBe("Error event from Unknown source at Apr 28, 18:15 UTC: Storage verification failed.");
    expect(rows[1]).toMatchObject({
      selectAriaLabel: "Inspect Error event from Unknown source at Apr 28, 18:15 UTC: Storage verification failed.",
      detailPanelId: "overview-activity-selected-detail",
      expanded: false
    });
  });

  it("builds selected activity details from event rows", () => {
    const rows = buildOverviewActivityRows([
      {
        id: "evt-2",
        type: "error",
        message: "Storage verification failed.",
        source: " ",
        timestamp: "2026-04-28T18:15:00Z"
      }
    ]);

    expect(buildOverviewActivityDetail(rows[0])).toEqual({
      eyebrow: "Error event",
      title: "Storage verification failed.",
      subtitle: "Unknown source",
      description: "Error evidence needs triage before the related workflow can be trusted.",
      badgeLabel: "ERR",
      badgeVariant: "danger",
      ariaLabel: "Selected recent activity detail for Error event from Unknown source",
      fields: [
        { label: "Source", value: "Unknown source" },
        { label: "Timestamp", value: "Apr 28, 18:15 UTC" },
        { label: "Severity", value: "Error" },
        { label: "Event ID", value: "evt-2" }
      ]
    });
    expect(buildOverviewActivityDetail(null)).toBeNull();
  });

  it("keeps recent activity selection in the view model", () => {
    const rows = buildOverviewActivityRows([
      {
        id: "evt-1",
        type: "warning",
        message: "Brokerage sync delayed.",
        source: "Provider health",
        timestamp: "2026-04-28T18:15:00Z"
      },
      {
        id: "evt-2",
        type: "info",
        message: "Backfill completed.",
        source: "Data",
        timestamp: "2026-04-28T18:20:00Z"
      }
    ]);

    const { result } = renderHook(() => useOverviewActivitySelectionViewModel(rows));

    expect(result.current.selectedRowId).toBe("evt-1");
    expect(result.current.rows[0].expanded).toBe(true);
    expect(result.current.selectedDetail?.title).toBe("Brokerage sync delayed.");
    expect(result.current.tableLabel).toBe("2 recent system events");

    act(() => {
      result.current.selectActivity("evt-2");
    });

    expect(result.current.selectedRowId).toBe("evt-2");
    expect(result.current.rows[1].expanded).toBe(true);
    expect(result.current.selectedDetail?.title).toBe("Backfill completed.");
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
    expect(healthy.detailParts).toEqual({
      providerSummary: "4 of 4 providers online",
      storageLabel: "Healthy",
      storageClassName: "text-success",
      lastHeartbeatLabel: "10:15 AM"
    });
    expect(healthy.icon).toBe("healthy");
    expect(healthy.containerClassName).toBe("border-success/30 bg-success/10");
    expect(healthy.titleClassName).toBe("text-success");
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
