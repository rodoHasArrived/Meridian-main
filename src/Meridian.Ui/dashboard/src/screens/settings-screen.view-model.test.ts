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
    expect(vm.hasEvents).toBe(true);
    expect(vm.recentEvents).toHaveLength(1);
    expect(vm.recentEvents[0].message).toBe("Backfill completed.");
  });

  it("returns empty events when overview has none", () => {
    const noEvents: SystemOverviewResponse = { ...overview, recentEvents: [] };
    const vm = buildSettingsScreenViewModel(null, noEvents);
    expect(vm.hasEvents).toBe(false);
  });

  it("always includes diagnostic links", () => {
    const vm = buildSettingsScreenViewModel(null, null);
    expect(vm.diagnosticLinks.length).toBeGreaterThan(0);
    expect(vm.diagnosticLinks.every((l) => l.href.startsWith("/api/"))).toBe(true);
  });

  it("builds system summary label", () => {
    const vm = buildSettingsScreenViewModel(null, overview);
    expect(vm.systemSummary).toContain("Healthy");
    expect(vm.systemSummary).toContain("3/3");
  });

  it("handles null overview gracefully", () => {
    const vm = buildSettingsScreenViewModel(null, null);
    expect(vm.hasOverview).toBe(false);
    expect(vm.systemItems).toHaveLength(0);
    expect(vm.systemSummary).toContain("unavailable");
  });
});
