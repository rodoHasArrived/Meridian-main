import { describe, expect, it } from "vitest";
import { buildAlpacaConnectionCommandState, buildSettingsScreenViewModel } from "@/screens/settings-screen.view-model";
import type { BrokerageConnectionStatus, SessionInfo, SystemOverviewResponse } from "@/types";

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

const alpacaConnection: BrokerageConnectionStatus = {
  providerId: "alpaca",
  displayName: "Alpaca paper",
  state: "Connected",
  isConfigured: true,
  isConnected: true,
  authorizationUrl: null,
  connectedAt: "2026-05-07T11:50:00Z",
  expiresAt: null,
  lastError: null,
  warnings: [],
  scopes: ["trading:account", "brokerage-sync:read"],
  environment: "paper",
  externalAccountId: "PA123",
  verifiedAt: "2026-05-07T11:50:00Z",
  maskedKeyId: "********1234"
};

describe("buildSettingsScreenViewModel", () => {
  it("builds session items from session data", () => {
    const vm = buildSettingsScreenViewModel(session, null);
    expect(vm.hasSession).toBe(true);
    expect(vm.sessionItems.some((i) => i.label === "Display name" && i.value === "Andrew Rowden")).toBe(true);
    expect(vm.sessionItems.some((i) => i.label === "Environment" && i.value === "paper")).toBe(true);
    expect(vm.sessionItems.some((i) => i.label === "Commands issued" && i.value === "42")).toBe(true);
    expect(vm.headerChips).toEqual([
      { label: "Environment", value: "PAPER" },
      { label: "Workspace", value: "settings" },
      { label: "Diagnostics", value: "6 unavailable" },
      { label: "Heartbeat", value: "—" }
    ]);
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
    expect(vm.recentEventsSection.countLabel).toBe("1");
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
    expect(vm.recentEventsSection.countLabel).toBe("0");
    expect(vm.recentEventsSection.rows).toHaveLength(0);
  });

  it("treats missing recent events from runtime payloads as an empty event stream", () => {
    const partialOverview = { ...overview };
    delete (partialOverview as Partial<SystemOverviewResponse>).recentEvents;

    const vm = buildSettingsScreenViewModel(null, partialOverview as SystemOverviewResponse);

    expect(vm.recentEventsSection.state).toBe("empty");
    expect(vm.recentEventsSection.countLabel).toBe("0");
    expect(vm.recentEventsSection.statusLabel).toBe("No recent events");
  });

  it("returns unavailable recent-event state when overview is null", () => {
    const vm = buildSettingsScreenViewModel(null, null);
    expect(vm.recentEventsSection.state).toBe("unavailable");
    expect(vm.recentEventsSection.statusLabel).toBe("Event stream unavailable");
    expect(vm.recentEventsSection.countLabel).toBe("0");
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
    expect(vm.headerChips).toEqual([
      { label: "Environment", value: "PAPER" },
      { label: "Workspace", value: "settings" },
      { label: "Diagnostics", value: "All reachable" },
      { label: "Heartbeat", value: "2026-05-01T00:00:00Z" }
    ]);
    expect(vm.diagnosticCounts).toMatchObject({
      loaded: 7,
      failed: 0,
      checking: 0,
      loadedLabel: "7",
      failedLabel: "0",
      checkingLabel: "0"
    });
    expect(vm.diagnosticLinks.every((link) => link.statusLabel === "Loaded")).toBe(true);
  });

  it("derives Alpaca connection panel state without exposing secrets", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      brokerageConnection: alpacaConnection
    });

    expect(vm.alpacaConnectionPanel).toMatchObject({
      providerLabel: "Alpaca paper",
      stateLabel: "Connected",
      statusTone: "success",
      environmentLabel: "PAPER",
      accountLabel: "PA123",
      maskedKeyIdLabel: "********1234",
      canClear: true
    });
    expect(vm.alpacaConnectionPanel.statusDetail).toContain("PA123");
    expect(vm.alpacaConnectionPanel.statusDetail).not.toContain("secret");
    expect(vm.alpacaConnectionPanel.setupChecklist.map((step) => [step.id, step.statusLabel, step.tone])).toEqual([
      ["alpaca-paper-environment", "Ready", "success"],
      ["alpaca-api-keys", "Stored", "success"],
      ["alpaca-account-verification", "Verified", "success"],
      ["alpaca-readiness-handoff", "Ready", "success"]
    ]);
    expect(vm.alpacaConnectionPanel.setupChecklist[vm.alpacaConnectionPanel.setupChecklist.length - 1]).toMatchObject({
      actionLabel: "Open readiness",
      actionHref: "/trading/readiness",
      actionAriaLabel: "Open Trading readiness after Alpaca account verification"
    });
  });

  it("marks invalid Alpaca credentials as a degraded connection state", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      brokerageConnection: {
        ...alpacaConnection,
        state: "Degraded",
        isConnected: false,
        lastError: "Alpaca /v2/account verification failed: status 401",
        warnings: ["Alpaca /v2/account verification failed: status 401"]
      }
    });

    expect(vm.alpacaConnectionPanel.stateLabel).toBe("Verification failed");
    expect(vm.alpacaConnectionPanel.statusTone).toBe("danger");
    expect(vm.alpacaConnectionPanel.statusDetail).toContain("401");
    expect(vm.alpacaConnectionPanel.setupChecklist.find((step) => step.id === "alpaca-account-verification")).toMatchObject({
      statusLabel: "Failed",
      tone: "danger",
      detail: "Alpaca /v2/account verification failed: status 401"
    });
    expect(vm.alpacaConnectionPanel.setupChecklist.find((step) => step.id === "alpaca-readiness-handoff")).toMatchObject({
      statusLabel: "Blocked",
      actionHref: null
    });
  });

  it("derives Alpaca credential command disabled and validation state", () => {
    const emptyState = buildAlpacaConnectionCommandState({
      canClear: false,
      form: {
        keyId: "",
        secretKey: "",
        environment: "paper",
        busyAction: null,
        submitted: true,
        actionMessage: null,
        actionTone: "default"
      }
    });

    expect(emptyState).toMatchObject({
      canSubmit: false,
      canEdit: true,
      keyIdError: true,
      secretKeyError: true,
      formPanelTitle: "Credentials incomplete",
      formPanelTone: "warning",
      submitLabel: "Connect and test",
      clearDisabledReason: "No stored Alpaca credentials are available to clear."
    });
    expect(emptyState.submitDisabledReason).toContain("key ID");
    expect(emptyState.requirements).toEqual([
      expect.objectContaining({ id: "alpaca-key-id-requirement", value: "Required", met: false, tone: "warning" }),
      expect.objectContaining({ id: "alpaca-secret-key-requirement", value: "Required", met: false, tone: "warning" }),
      expect.objectContaining({ id: "alpaca-environment-requirement", value: "PAPER", met: true, tone: "success" })
    ]);

    const busyState = buildAlpacaConnectionCommandState({
      canClear: true,
      form: {
        keyId: "AK123",
        secretKey: "secret",
        environment: "live",
        busyAction: "connect",
        submitted: true,
        actionMessage: null,
        actionTone: "default"
      }
    });

    expect(busyState).toMatchObject({
      canSubmit: false,
      canEdit: false,
      submitBusy: true,
      clearBusy: false
    });
    expect(busyState.formPanelTitle).toBe("Testing Alpaca credentials");
    expect(busyState.submitDisabledReason).toContain("already running");
  });

  it("surfaces canonical backend capability groups with browser routes and mapped endpoints", () => {
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

    expect(vm.backendCapabilityStatusLabel).toBe("All surfaced");
    expect(vm.backendCapabilityStatusVariant).toBe("success");
    expect(vm.backendCapabilityGroups.map((group) => group.workspaceLabel)).toEqual([
      "Trading",
      "Portfolio",
      "Accounting",
      "Reporting",
      "Strategy",
      "Data",
      "Settings"
    ]);
    expect(vm.backendCapabilityGroups.find((group) => group.id === "strategy")?.endpoints).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ href: "/api/workstation/runs/history", method: "GET" }),
        expect.objectContaining({ href: "/api/workstation/runs/compare", method: "POST" })
      ])
    );
    expect(vm.backendCapabilityGroups.find((group) => group.id === "settings")?.endpoints).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ href: "/api/workstation/workflows" }),
        expect.objectContaining({ href: "/api/workstation/workflows/presets" })
      ])
    );
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
    expect(vm.diagnosticCounts.failed).toBeGreaterThan(0);
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

  it("derives diagnostic chip counts while loading", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      loading: true
    });

    expect(vm.diagnosticStatusLabel).toBe("7 checking");
    expect(vm.backendCapabilityStatusLabel).toBe("7 checking");
    expect(vm.diagnosticCounts).toMatchObject({
      loaded: 0,
      failed: 0,
      checking: 7,
      loadedLabel: "0",
      failedLabel: "0",
      checkingLabel: "7"
    });
    expect(vm.diagnosticSummary).toContain("Checking 7 diagnostic endpoints");
  });

  it("handles null overview gracefully", () => {
    const vm = buildSettingsScreenViewModel(null, null);
    expect(vm.hasOverview).toBe(false);
    expect(vm.systemItems).toHaveLength(0);
    expect(vm.systemSummary).toContain("unavailable");
  });
});
