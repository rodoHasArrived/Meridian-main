import { describe, expect, it } from "vitest";
import { buildSettingsScreenViewModel } from "@/screens/settings-screen.view-model";
import type { SessionInfo, SystemOverviewResponse } from "@/types";

const session: SessionInfo = {
  displayName: "Andrew Rowden",
  role: "Fund Manager",
  environment: "paper",
  activeWorkspace: "settings",
  commandCount: 42
};

const overview: SystemOverviewResponse = {
  systemStatus: "Healthy",
  providersOnline: 3,
  providersTotal: 3,
  activeRuns: 2,
  openPositions: 5,
  activeBackfills: 0,
  symbolsMonitored: 120,
  storageHealth: "Healthy",
  lastHeartbeatUtc: "2026-05-01T00:00:00Z",
  metrics: [],
  recentEvents: [
    { id: "e1", type: "info", message: "Backfill completed.", source: "DataPipeline", timestamp: "2026-05-01T00:00:00Z" }
  ]
};

describe("buildSettingsScreenViewModel", () => {
  it("builds session items from session data", () => {
    const vm = buildSettingsScreenViewModel(session, null);
    expect(vm.hasSession).toBe(true);
    expect(vm.sessionItems.some((i) => i.label === "Display name" && i.value === "Andrew Rowden")).toBe(true);
    expect(vm.sessionItems.some((i) => i.label === "Environment" && i.value === "paper")).toBe(true);
    expect(vm.sessionItems.some((i) => i.label === "Commands issued" && i.value === "42")).toBe(true);
  });

  it("marks live environment with warning tone", () => {
    const liveSession: SessionInfo = { ...session, environment: "live" };
    const vm = buildSettingsScreenViewModel(liveSession, null);
    const envItem = vm.sessionItems.find((i) => i.label === "Environment");
    expect(envItem?.tone).toBe("warning");
  });

  it("returns empty session items when session is null", () => {
    const vm = buildSettingsScreenViewModel(null, null);
    expect(vm.hasSession).toBe(false);
    expect(vm.sessionItems).toHaveLength(0);
  });

  it("builds system items from overview data", () => {
    const vm = buildSettingsScreenViewModel(null, overview);
    expect(vm.hasOverview).toBe(true);
    expect(vm.systemItems.some((i) => i.label === "Status" && i.value === "Healthy")).toBe(true);
    expect(vm.systemItems.some((i) => i.label === "Active runs" && i.value === "2")).toBe(true);
    expect(vm.systemItems.some((i) => i.label === "Symbols monitored" && i.value === "120")).toBe(true);
  });

  it("returns success tone for healthy system", () => {
    const vm = buildSettingsScreenViewModel(null, overview);
    expect(vm.systemTone).toBe("success");
  });

  it("returns warning tone for degraded system", () => {
    const degraded: SystemOverviewResponse = { ...overview, systemStatus: "Degraded" };
    const vm = buildSettingsScreenViewModel(null, degraded);
    expect(vm.systemTone).toBe("warning");
  });

  it("returns danger tone for offline system", () => {
    const offline: SystemOverviewResponse = { ...overview, systemStatus: "Offline" };
    const vm = buildSettingsScreenViewModel(null, offline);
    expect(vm.systemTone).toBe("danger");
  });

  it("surfaces recent events from overview", () => {
    const vm = buildSettingsScreenViewModel(null, overview);
    expect(vm.recentEventsSection.state).toBe("ready");
    expect(vm.recentEventsSection.listLabel).toBe("1 recent system event");
    expect(vm.recentEventsSection.rows).toHaveLength(1);
    expect(vm.recentEventsSection.rows[0]).toMatchObject({
      message: "Backfill completed.",
      statusCode: "INFO",
      badgeVariant: "default",
      tone: "default"
    });
  });

  it("returns empty events when overview has none", () => {
    const noEvents: SystemOverviewResponse = { ...overview, recentEvents: [] };
    const vm = buildSettingsScreenViewModel(null, noEvents);
    expect(vm.recentEventsSection.state).toBe("empty");
    expect(vm.recentEventsSection.statusLabel).toBe("No recent events");
    expect(vm.recentEventsSection.rows).toHaveLength(0);
  });

  it("returns unavailable recent-event state when overview is null", () => {
    const vm = buildSettingsScreenViewModel(null, null);
    expect(vm.recentEventsSection.state).toBe("unavailable");
    expect(vm.recentEventsSection.statusLabel).toBe("Event stream unavailable");
    expect(vm.recentEventsSection.statusDetail).toContain("Reconnect");
  });

  it("derives warning and error event tones with fallback evidence", () => {
    const eventOverview: SystemOverviewResponse = {
      ...overview,
      recentEvents: [
        { id: "w1", type: "warning", message: "Brokerage sync delayed.", source: "", timestamp: "" },
        { id: "e1", type: "error", message: "", source: "ConfigService", timestamp: "2026-05-01T00:03:00Z" }
      ]
    };

    const vm = buildSettingsScreenViewModel(null, eventOverview);

    expect(vm.recentEventsSection.listLabel).toBe("2 recent system events");
    expect(vm.recentEventsSection.rows[0]).toMatchObject({
      statusCode: "OBS",
      badgeVariant: "warning",
      source: "Unknown source",
      timestamp: "Timestamp unavailable"
    });
    expect(vm.recentEventsSection.rows[1]).toMatchObject({
      statusCode: "CRIT",
      badgeVariant: "danger",
      message: "Event detail unavailable."
    });
  });

  it("always includes diagnostic links", () => {
    const vm = buildSettingsScreenViewModel(null, null);
    expect(vm.diagnosticLinks.length).toBeGreaterThan(0);
    expect(vm.diagnosticLinks.every((l) => l.href.startsWith("/api/"))).toBe(true);
    expect(vm.diagnosticLinks.every((l) => l.ariaLabel.includes("diagnostic endpoint"))).toBe(true);
  });

  it("derives diagnostic endpoint posture from loaded workspace payloads", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      research: { metrics: [], runs: [] },
      trading: {} as never,
      dataOperations: { metrics: [], providers: [], backfills: [], exports: [] },
      governance: {} as never,
      reporting: {} as never,
      loading: false,
      error: null,
      workspaceErrors: {}
    });

    expect(vm.diagnosticStatusLabel).toBe("All reachable");
    expect(vm.diagnosticStatusVariant).toBe("success");
    expect(vm.diagnosticLinks.every((link) => link.statusLabel === "Loaded")).toBe(true);
  });

  it("surfaces workspace diagnostic failures without hiding endpoint links", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      research: null,
      trading: null,
      dataOperations: null,
      governance: null,
      reporting: null,
      loading: false,
      error: "Workstation request failed.",
      workspaceErrors: {
        trading: "Trading API returned 503.",
        reporting: "Reporting API returned 500."
      }
    });

    const tradingLink = vm.diagnosticLinks.find((link) => link.label === "Trading workspace");
    const reportingLink = vm.diagnosticLinks.find((link) => link.label === "Reporting workspace");

    expect(vm.diagnosticStatusVariant).toBe("danger");
    expect(tradingLink).toMatchObject({
      href: "/api/workstation/trading",
      statusLabel: "Failed",
      statusDetail: "Trading API returned 503."
    });
    expect(reportingLink).toMatchObject({
      href: "/api/workstation/reporting",
      statusLabel: "Failed",
      statusDetail: "Reporting API returned 500."
    });
  });

  it("builds system summary label", () => {
    const vm = buildSettingsScreenViewModel(null, overview);
    expect(vm.systemSummary).toContain("Healthy");
    expect(vm.systemSummary).toContain("3/3");
  });

  it("points system overview diagnostics at the mapped status endpoint", () => {
    const vm = buildSettingsScreenViewModel(session, overview);
    expect(vm.diagnosticLinks.find((link) => link.label === "System overview")?.href).toBe("/api/status");
  });

  it("handles null overview gracefully", () => {
    const vm = buildSettingsScreenViewModel(null, null);
    expect(vm.hasOverview).toBe(false);
    expect(vm.systemItems).toHaveLength(0);
    expect(vm.systemSummary).toContain("unavailable");
  });
});
