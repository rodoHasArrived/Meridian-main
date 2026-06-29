import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import {
  buildAlpacaConnectionCommandState,
  buildSettingsRecentEventsSelectionViewModel,
  buildSettingsScreenViewModel,
  useAlpacaConnectionFormViewModel,
  useRobinhoodConnectionViewModel
} from "@/screens/settings-screen.view-model";
import type {
  BrokerageConnectionStatus,
  LedgerMappingWorkbench,
  OperationsApprovalPolicyMatrix,
  OperationsCloseCalendar,
  ProviderConnectionRow,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot,
  RolePermissionCatalog,
  SecurityAssetProfileDefinition,
  SessionInfo,
  SystemOverviewResponse
} from "@/types";

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

const robinhoodConnection: BrokerageConnectionStatus = {
  providerId: "robinhood",
  displayName: "Robinhood",
  state: "Connected",
  isConfigured: true,
  isConnected: true,
  authorizationUrl: null,
  connectedAt: "2026-05-07T11:50:00Z",
  expiresAt: "2026-06-07T11:50:00Z",
  lastError: null,
  warnings: [],
  scopes: ["positions:read", "balances:read"],
  environment: null,
  externalAccountId: "RH-987",
  verifiedAt: null,
  maskedKeyId: null
};

const providerConnections: ProviderConnectionRow[] = [
  {
    providerId: "alpaca",
    displayName: "Alpaca",
    capability: "DataAndBrokerage",
    credentialState: "Verified",
    credentialSource: "LocalEncryptedStore",
    verificationState: "Verified",
    health: "Healthy",
    fallbackActive: false,
    lastVerifiedAt: "2026-05-07T11:50:00Z",
    lastSuccessfulAt: "2026-05-07T11:50:00Z",
    lastFailureAt: null,
    lastError: null,
    maskedKeyPreview: "********1234",
    environment: "paper",
    externalAccountId: "PA123",
    affectedWorkflows: ["Trading readiness", "Portfolio brokerage sync"],
    recommendedAction: "No credential repair action required.",
    actionHref: "/settings#alpaca-provider-setup"
  },
  {
    providerId: "polygon",
    displayName: "Polygon.io",
    capability: "Data",
    credentialState: "Missing",
    credentialSource: "None",
    verificationState: "NotVerified",
    health: "Warning",
    fallbackActive: true,
    lastVerifiedAt: null,
    lastSuccessfulAt: null,
    lastFailureAt: "2026-05-07T11:45:00Z",
    lastError: "Provider credential missing.",
    maskedKeyPreview: null,
    environment: null,
    externalAccountId: null,
    affectedWorkflows: ["Historical backfill"],
    recommendedAction: "Add the Polygon API key before routing data repair through Polygon.",
    actionHref: "/settings#provider-polygon-connection"
  }
];

const providerRoutingConnections: ProviderRoutingConnection[] = [
  {
    connectionId: "provider-alpaca-paper",
    providerFamilyId: "alpaca",
    displayName: "Alpaca paper route",
    connectionType: "DataVendor",
    connectionMode: "ReadOnly",
    enabled: true,
    credentialReference: "vault:alpaca/paper",
    institutionId: null,
    externalAccountId: null,
    scope: null,
    tags: ["streaming"],
    description: null,
    productionReady: false
  },
  {
    connectionId: "provider-reference",
    providerFamilyId: "polygon",
    displayName: "Reference data route",
    connectionType: "DataVendor",
    connectionMode: "ReadOnly",
    enabled: true,
    credentialReference: "vault:polygon/default",
    institutionId: null,
    externalAccountId: null,
    scope: null,
    tags: ["reference"],
    description: null,
    productionReady: true
  }
];

const providerRoutingBindings: ProviderRoutingBinding[] = [
  {
    bindingId: "provider-alpaca-paper-RealtimeMarketData",
    capability: "RealtimeMarketData",
    connectionId: "provider-alpaca-paper",
    target: null,
    priority: 100,
    enabled: true,
    failoverConnectionIds: [],
    safetyModeOverride: null,
    notes: null
  },
  {
    bindingId: "provider-reference-ReferenceData",
    capability: "ReferenceData",
    connectionId: "provider-reference",
    target: null,
    priority: 100,
    enabled: true,
    failoverConnectionIds: ["provider-alpaca-paper"],
    safetyModeOverride: null,
    notes: null
  }
];

const providerRoutingTrustSnapshots: ProviderRoutingTrustSnapshot[] = [
  {
    connectionId: "provider-alpaca-paper",
    providerFamilyId: "alpaca",
    score: 84,
    isHealthy: true,
    healthStatus: "Healthy",
    isProductionReady: false,
    isCertificationFresh: false,
    signals: []
  },
  {
    connectionId: "provider-reference",
    providerFamilyId: "polygon",
    score: 97,
    isHealthy: true,
    healthStatus: "Healthy",
    isProductionReady: true,
    isCertificationFresh: true,
    signals: []
  }
];

const rolePermissionCatalog: RolePermissionCatalog = {
  roles: [
    {
      role: "Accounting",
      displayName: "Accounting",
      description: "Accounting and fund-operations access for trade records, exports, and direct-lending operations.",
      isBuiltIn: true,
      permissions: ["ViewTrades", "ExportData", "ManageDirectLending"],
      permissionMask: 0
    },
    {
      role: "Admin",
      displayName: "Admin",
      description: "Full platform administration including governed operations.",
      isBuiltIn: true,
      permissions: ["ManageUsers", "AdminMaintenance", "ModifyConfig"],
      permissionMask: 0
    }
  ],
  permissions: [
    { name: "ManageUsers", value: 0, group: "Administration", description: "Create users." },
    { name: "AdminMaintenance", value: 0, group: "Administration", description: "Run maintenance." },
    { name: "ManageDirectLending", value: 0, group: "Direct lending", description: "Manage direct lending." }
  ]
};

const ledgerMappingWorkbench: LedgerMappingWorkbench = {
  asOf: "2026-05-28T00:00:00Z",
  accountCount: 2,
  mappedAccountCount: 1,
  unmappedAccountCount: 1,
  ledgerGroups: [
    {
      ledgerGroupId: "lg-1",
      displayName: "Direct lending",
      accountIds: ["account-1"],
      investmentPortfolioIds: [],
      clientIds: [],
      fundIds: [],
      sleeveIds: [],
      vehicleIds: []
    }
  ],
  accounts: [
    {
      accountId: "account-2",
      accountCode: "OPS-2",
      displayName: "Operations account",
      accountType: "ManagedAccount",
      operationalStatus: "Active",
      baseCurrency: "USD",
      institution: null,
      fundId: null,
      sleeveId: null,
      vehicleId: null,
      entityId: null,
      portfolioId: null,
      ledgerReference: null,
      mapping: {
        ledgerGroupId: "unassigned",
        source: "Unassigned",
        sourceNodeId: null,
        sourceNodeKind: null,
        sourceReference: null,
        requiresUserMapping: true,
        issueCodes: ["ledger-mapping.missing"]
      },
      recommendedAction: "Assign a ledger group before close."
    }
  ]
};

const approvalPolicyMatrix: OperationsApprovalPolicyMatrix = {
  policyId: "ops-close",
  version: "2026.05",
  generatedAtUtc: "2026-05-28T00:00:00Z",
  rows: [
    {
      policyKey: "ready-for-close",
      workflowArea: "Account close",
      action: "Approve close",
      gate: "Approval",
      trigger: "ReadyForClose",
      requiredPermission: "AdminMaintenance",
      submitterRole: "Accounting",
      reviewerRole: "Admin",
      requiredDistinctApprovals: 2,
      requiresIndependentReviewer: true,
      requiresReportPack: true,
      requiresChecklistControlApprovals: true,
      evidenceRequirement: "Report pack and checklist controls",
      auditEventType: "close-approved",
      route: "/accounting/operations-continuity",
      severity: "High"
    }
  ]
};

const closeCalendar: OperationsCloseCalendar = {
  generatedAtUtc: "2026-05-28T00:00:00Z",
  items: [
    {
      workflowId: "workflow-1",
      fundAccountId: "fund-1",
      periodId: "2026-05",
      status: "Blocked",
      version: 4,
      nextDueDate: "2026-05-31",
      nextDueTaskId: "task-1",
      nextDueLabel: "Resolve ledger mapping",
      nextDueOwner: "Accounting",
      readinessSeverity: "Warning",
      readinessScore: 70,
      isReadyToClose: false,
      blockerCount: 1,
      openChecklistCount: 2,
      requiredApprovalCount: 2,
      completedApprovalCount: 1,
      route: "/accounting/operations-continuity"
    }
  ]
};

const securityAssetProfiles: SecurityAssetProfileDefinition[] = [
  {
    profileId: "structured-credit-io-po",
    version: 1,
    name: "Structured Credit IO/PO",
    category: "StructuredCredit",
    subType: "InterestOnly",
    status: "Approved",
    fields: [
      {
        key: "poolId",
        label: "Pool ID",
        fieldType: "Text",
        isRequired: true,
        allowedValues: [],
        description: null,
        minValue: null,
        maxValue: null,
        isProjected: true,
        isSearchable: true
      },
      {
        key: "currentFactor",
        label: "Current factor",
        fieldType: "Decimal",
        isRequired: true,
        allowedValues: [],
        description: null,
        minValue: 0,
        maxValue: 1,
        isProjected: true,
        isSearchable: false
      }
    ],
    identifierPreferences: [
      { kind: "Cusip", isRequiredForClose: true, reason: "CUSIP coverage is required when available." },
      { kind: "InternalCode", isRequiredForClose: true, reason: "Internal code is required for modeled strips." }
    ],
    lifecycleStates: ["Modeled", "Validated"],
    accountingImpactHints: ["FactorSchedule", "IncomeAccrual"],
    dateOrderRules: [],
    effectiveFrom: "2026-05-01",
    effectiveTo: null,
    approvedBy: "Security Master Council",
    approvedAtUtc: "2026-05-01T00:00:00Z",
    changeReason: "Seed template"
  }
];

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
    expect(vm.systemItems.some((i) => i.label === "Last heartbeat" && i.value === "May 1, 00:00 UTC")).toBe(true);
  });

  it("builds fund operations control cards from configuration payloads", () => {
    const vm = buildSettingsScreenViewModel({
      session: { ...session, role: "Accounting" },
      overview,
      rolePermissionCatalog,
      ledgerMappingWorkbench,
      operationsApprovalPolicyMatrix: approvalPolicyMatrix,
      operationsCloseCalendar: closeCalendar
    });

    expect(vm.operationsControlCenter.statusLabel).toBe("2 review");
    expect(vm.operationsControlCenter.loadedCountLabel).toBe("4 / 4");
    expect(vm.operationsControlCenter.cards.map((card) => card.title)).toEqual([
      "Ledger Mapping Workbench",
      "Role and Permission Studio",
      "Approval Policy Matrix",
      "Account Close Calendar"
    ]);
    expect(vm.operationsControlCenter.cards.find((card) => card.id === "ledger-mapping")).toMatchObject({
      statusLabel: "1 unmapped",
      endpointHref: "/api/fund-structure/ledger-mapping-view"
    });
    expect(vm.operationsControlCenter.cards.find((card) => card.id === "role-permissions")).toMatchObject({
      statusLabel: "Accounting active",
      endpointHref: "/api/auth/roles"
    });
    expect(vm.operationsControlCenter.cards.find((card) => card.id === "approval-policy")).toMatchObject({
      statusLabel: "1 rules",
      endpointHref: "/api/workstation/operations/continuity/approval-policy-matrix"
    });
    expect(vm.operationsControlCenter.cards.find((card) => card.id === "close-calendar")).toMatchObject({
      statusLabel: "1 blocked",
      endpointHref: "/api/workstation/operations/continuity/close-calendar"
    });
  });

  it("summarizes approved asset profiles for governed Security Master creation", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      securityAssetProfiles
    });

    expect(vm.assetProfileGovernancePanel).toMatchObject({
      statusLabel: "1 approved",
      statusVariant: "success",
      approvedCountLabel: "1",
      projectedFieldCountLabel: "2",
      closeIdentifierCountLabel: "2",
      canCreateSecurity: true,
      createDisabledReason: null
    });
    expect(vm.assetProfileGovernancePanel.rows[0]).toMatchObject({
      profileId: "structured-credit-io-po",
      versionLabel: "v1",
      name: "Structured Credit IO/PO",
      categoryLabel: "StructuredCredit / InterestOnly",
      requiredCloseIdentifierLabel: "Cusip, InternalCode",
      accountingImpactLabel: "FactorSchedule, IncomeAccrual"
    });
  });

  it("builds provider connection center rows with exact repair anchors", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      providerConnections,
      brokerageConnection: alpacaConnection
    });

    expect(vm.providerConnectionCenter.statusLabel).toBe("1 need review");
    expect(vm.providerConnectionCenter.statusVariant).toBe("warning");
    const brokerageRows = vm.providerConnectionCenter.groups.find((group) => group.id === "brokerage")?.rows ?? [];
    const dataRows = vm.providerConnectionCenter.groups.find((group) => group.id === "data")?.rows ?? [];

    expect(brokerageRows).toHaveLength(1);
    expect(brokerageRows[0]).toMatchObject({
      providerId: "alpaca",
      integrationConnectionId: "alpaca",
      capabilityLabel: "Data + Brokerage",
      credentialLabel: "Verified",
      sourceLabel: "Encrypted local store",
      actionHref: "/settings#alpaca-provider-setup"
    });
    expect(dataRows).toHaveLength(1);
    expect(dataRows[0]).toMatchObject({
      providerId: "polygon",
      integrationConnectionId: "polygon",
      credentialLabel: "Missing",
      fallbackLabel: "Fallback active",
      actionHref: "/settings#provider-polygon-connection"
    });
  });

  it("merges provider-routing connections, bindings, and trust snapshots into the connection center", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      providerConnections: [],
      providerRoutingConnections,
      providerRoutingBindings,
      providerRoutingTrustSnapshots,
      providerRoutingRefreshing: true
    });

    expect(vm.providerConnectionCenter.statusLabel).toBe("Refreshing");
    expect(vm.providerConnectionCenter.refreshAction).toMatchObject({
      label: "Refreshing...",
      busy: true,
      disabled: true
    });
    expect(vm.providerConnectionCenter.routingSummaryLabel).toBe("2 routing connections · 2 bindings · 2 trust snapshots");

    const dataRows = vm.providerConnectionCenter.groups.find((group) => group.id === "data")?.rows ?? [];
    expect(dataRows).toHaveLength(2);
    expect(dataRows[0]).toMatchObject({
      providerId: "provider-alpaca-paper",
      integrationConnectionId: "provider-alpaca-paper",
      displayName: "Alpaca paper route",
      credentialLabel: "Configured",
      sourceLabel: "Vault reference",
      environmentLabel: "PAPER",
      maskedKeyPreviewLabel: "Hidden by routing API",
      routingBindingsLabel: "Realtime",
      trustScoreLabel: "84% · Healthy",
      productionStateLabel: "Certification needed",
      recommendedAction: "Run provider certification before production routing."
    });
    expect(dataRows[1]).toMatchObject({
      providerId: "provider-reference",
      integrationConnectionId: "provider-reference",
      routingBindingsLabel: "Reference data",
      fallbackLabel: "1 failover route",
      trustScoreLabel: "97% · Healthy",
      productionStateLabel: "Production ready"
    });
    expect(dataRows.map((row) => row.sourceLabel).join(" ")).not.toContain("vault:polygon/default");
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
      tone: "default",
      timestamp: "May 1, 00:00 UTC",
      ariaLabel: "INFO event from DataPipeline at May 1, 00:00 UTC. Backfill completed."
    });
  });

  it("builds selectable recent-event rows and detail state", () => {
    const eventOverview: SystemOverviewResponse = {
      ...overview,
      recentEvents: [
        { id: "w1", type: "warning", message: "Brokerage sync delayed.", source: "Provider health", timestamp: "2026-05-01T00:03:00Z" },
        { id: "e1", type: "error", message: "Storage heartbeat missed.", source: "Storage", timestamp: "2026-05-01T00:05:00Z" }
      ]
    };
    const baseVm = buildSettingsScreenViewModel(null, eventOverview);

    const eventsVm = buildSettingsRecentEventsSelectionViewModel(baseVm.recentEventsSection, "e1");

    expect(eventsVm.selectedRowId).toBe("e1");
    expect(eventsVm.rows[1]).toMatchObject({
      detailPanelId: "settings-recent-event-detail",
      expanded: true,
      selectAriaLabel: "Select event e1. CRIT event from Storage at May 1, 00:05 UTC. Storage heartbeat missed."
    });
    expect(eventsVm.selectedDetail).toMatchObject({
      title: "Storage heartbeat missed.",
      statusLabel: "Critical",
      statusVariant: "danger",
      ariaLabel: "CRIT event detail for e1"
    });
    expect(eventsVm.selectedDetail?.fields).toContainEqual({ label: "Source", value: "Storage", tone: "default" });
    expect(eventsVm.selectedDetail?.fields).toContainEqual({ label: "Status code", value: "CRIT", tone: "danger" });
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
    expect(vm.diagnosticLinks.every((l) => l.ariaLabel.includes("diagnostic service"))).toBe(true);
  });

  it("derives diagnostic service posture from loaded workspace data", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      strategy: { metrics: [], runs: [] },
      trading: {} as never,
      data: { metrics: [], providers: [], backfills: [], exports: [] },
      accounting: {} as never,
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
      { label: "Heartbeat", value: "May 1, 00:00 UTC" }
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

  it("derives a connected Robinhood OAuth panel", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      robinhoodConnection
    });

    expect(vm.robinhoodConnectionPanel).toMatchObject({
      providerLabel: "Robinhood",
      stateLabel: "Connected",
      statusTone: "success",
      accountLabel: "RH-987",
      scopesLabel: "positions:read, balances:read",
      authorizationUrl: null,
      isConfigured: true,
      canConnect: false,
      canDisconnect: true
    });
    expect(vm.robinhoodConnectionPanel.connectedAtLabel).toBe("2026-05-07T11:50:00Z");
    expect(vm.robinhoodConnectionPanel.expiresAtLabel).toBe("2026-06-07T11:50:00Z");
    expect(vm.robinhoodConnectionPanel.statusDetail).toContain("Robinhood account RH-987");
    expect(vm.robinhoodConnectionPanel.statusDetail).not.toContain("Alpaca");
    expect(vm.robinhoodConnectionPanel.statusDetail).not.toContain("/v2/account");
  });

  it("flags a degraded Robinhood panel with a danger tone", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      robinhoodConnection: {
        ...robinhoodConnection,
        state: "Degraded",
        isConnected: false,
        lastError: "Aggregation provider rejected the refresh token."
      }
    });

    expect(vm.robinhoodConnectionPanel.statusTone).toBe("danger");
    expect(vm.robinhoodConnectionPanel.badgeVariant).toBe("danger");
    expect(vm.robinhoodConnectionPanel.statusDetail).toBe("Aggregation provider rejected the refresh token.");
  });

  it("derives an unconfigured Robinhood panel that blocks connect and disconnect", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      robinhoodConnection: {
        ...robinhoodConnection,
        state: "NotConfigured",
        isConfigured: false,
        isConnected: false,
        connectedAt: null,
        expiresAt: null,
        externalAccountId: null,
        scopes: []
      }
    });

    expect(vm.robinhoodConnectionPanel).toMatchObject({
      stateLabel: "Not configured",
      statusTone: "default",
      accountLabel: "Not linked",
      connectedAtLabel: "Not connected",
      expiresAtLabel: "No expiry recorded",
      scopesLabel: "No scopes granted",
      isConfigured: false,
      canConnect: true,
      canDisconnect: false
    });
    expect(vm.robinhoodConnectionPanel.statusDetail).toContain("ROBINHOOD_BROKERAGE_");
    expect(vm.robinhoodConnectionPanel.statusDetail).not.toContain("Alpaca");
  });

  it("surfaces a pending Robinhood authorization URL", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      robinhoodConnection: {
        ...robinhoodConnection,
        state: "AuthorizationPending",
        isConnected: false,
        authorizationUrl: "https://aggregator.example/oauth/authorize?token=abc"
      }
    });

    expect(vm.robinhoodConnectionPanel.authorizationUrl).toBe(
      "https://aggregator.example/oauth/authorize?token=abc"
    );
    expect(vm.robinhoodConnectionPanel.statusTone).toBe("warning");
    expect(vm.robinhoodConnectionPanel.canConnect).toBe(true);
  });

  it("builds profile authentication posture from session and brokerage authority", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      brokerageConnection: alpacaConnection
    });

    expect(vm.profileAuthenticationPanel).toMatchObject({
      regionLabel: "Profile and authentication posture",
      title: "Profile and access posture",
      statusLabel: "Access ready",
      statusTone: "success",
      avatarInitials: "AR",
      operatorName: "Andrew Rowden",
      roleLabel: "Fund Manager",
      environmentLabel: "PAPER",
      workspaceLabel: "Settings",
      commandCountLabel: "42 commands issued",
      authorityLabel: "Brokerage verified",
      authorityDetail: "Alpaca account PA123 is verified for readiness handoffs.",
      notice: null
    });
    expect(vm.profileAuthenticationPanel.facts).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "operator", value: "Andrew Rowden", tone: "default" }),
      expect.objectContaining({ id: "environment", value: "PAPER", tone: "success" }),
      expect.objectContaining({ id: "brokerage", value: "Brokerage verified", tone: "success" })
    ]));
    expect(vm.profileAuthenticationPanel.steps).toEqual(expect.arrayContaining([
      expect.objectContaining({
        id: "brokerage-authority",
        statusLabel: "Verified",
        actionHref: "/trading/readiness",
        actionAriaLabel: "Open Trading readiness from verified profile authentication posture"
      }),
      expect.objectContaining({
        id: "audit-diagnostics",
        statusLabel: "Review",
        actionHref: "/settings#diagnostic-endpoints"
      })
    ]));
  });

  it("warns when profile authentication posture is operating in live mode", () => {
    const liveSession: SessionInfo = { ...session, environment: "live" };
    const vm = buildSettingsScreenViewModel({
      session: liveSession,
      overview,
      brokerageConnection: { ...alpacaConnection, environment: "live" }
    });

    expect(vm.profileAuthenticationPanel).toMatchObject({
      statusLabel: "Live authority active",
      statusTone: "warning",
      environmentLabel: "LIVE",
      notice: {
        title: "Live environment controls active",
        tone: "warning",
        role: "status"
      }
    });
    expect(vm.profileAuthenticationPanel.summary).toContain("LIVE mode");
    expect(vm.profileAuthenticationPanel.steps.find((step) => step.id === "environment-authority")).toMatchObject({
      statusLabel: "LIVE",
      tone: "warning",
      detail: "Live mode requires explicit brokerage and readiness evidence before sensitive actions."
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

  it("keeps pristine Alpaca credential readiness neutral before submit", () => {
    const pristineState = buildAlpacaConnectionCommandState({
      canClear: false,
      form: {
        keyId: "",
        secretKey: "",
        environment: "paper",
        liveAcknowledged: false,
        busyAction: null,
        submitted: false,
        actionMessage: null,
        actionDetails: [],
        actionTone: "default"
      }
    });

    expect(pristineState).toMatchObject({
      canSubmit: false,
      canEdit: true,
      keyIdError: false,
      secretKeyError: false,
      formPanelTitle: "Enter Alpaca credentials",
      formPanelTone: "default",
      formPanelRole: "status",
      submitLabel: "Connect and test"
    });
    expect(pristineState.keyIdField).toMatchObject({
      id: "alpaca-key-id",
      label: "Key ID",
      placeholder: "ALPACA_KEY_ID",
      describedBy: "alpaca-key-id-help alpaca-credential-readiness",
      disabled: false,
      disabledReason: null
    });
    expect(pristineState.secretKeyField).toMatchObject({
      id: "alpaca-secret-key",
      label: "Secret key",
      type: "password",
      describedBy: "alpaca-secret-key-help alpaca-credential-readiness",
      disabled: false,
      disabledReason: null
    });
    expect(pristineState.requirements).toEqual([
      expect.objectContaining({ id: "alpaca-key-id-requirement", value: "Needed", met: false, tone: "muted" }),
      expect.objectContaining({ id: "alpaca-secret-key-requirement", value: "Needed", met: false, tone: "muted" }),
      expect.objectContaining({ id: "alpaca-environment-requirement", value: "PAPER", met: true, tone: "success" }),
      expect.objectContaining({ id: "alpaca-live-acknowledgement-requirement", value: "Not required", met: true, tone: "muted" })
    ]);
    expect(pristineState.liveAcknowledgement).toMatchObject({
      visible: false,
      checked: false,
      required: false
    });
  });

  it("derives Alpaca credential command disabled and validation state", () => {
    const emptyState = buildAlpacaConnectionCommandState({
      canClear: false,
      form: {
        keyId: "",
        secretKey: "",
        environment: "paper",
        liveAcknowledged: false,
        busyAction: null,
        submitted: true,
        actionMessage: null,
        actionDetails: [],
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
    expect(emptyState.environmentLegend).toBe("Alpaca trading environment");
    expect(emptyState.environmentOptions).toEqual([
      expect.objectContaining({
        id: "alpaca-environment-paper",
        value: "paper",
        badgeLabel: "Default",
        endpointLabel: "https://paper-api.alpaca.markets/v2",
        isSelected: true,
        disabled: false,
        tone: "paper"
      }),
      expect.objectContaining({
        id: "alpaca-environment-live",
        value: "live",
        badgeLabel: "Real money",
        endpointLabel: "https://api.alpaca.markets/v2",
        isSelected: false,
        disabled: false,
        tone: "live"
      })
    ]);
    expect(emptyState.requirements).toEqual([
      expect.objectContaining({ id: "alpaca-key-id-requirement", value: "Required", met: false, tone: "warning" }),
      expect.objectContaining({ id: "alpaca-secret-key-requirement", value: "Required", met: false, tone: "warning" }),
      expect.objectContaining({ id: "alpaca-environment-requirement", value: "PAPER", met: true, tone: "success" }),
      expect.objectContaining({ id: "alpaca-live-acknowledgement-requirement", value: "Not required", met: true, tone: "muted" })
    ]);

    const busyState = buildAlpacaConnectionCommandState({
      canClear: true,
      form: {
        keyId: "AK123",
        secretKey: "secret",
        environment: "live",
        liveAcknowledged: true,
        busyAction: "connect",
        submitted: true,
        actionMessage: null,
        actionDetails: [],
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
    expect(busyState.keyIdField).toMatchObject({
      disabled: true,
      helpText: "Alpaca credential request is already running.",
      disabledReason: "Alpaca credential request is already running."
    });
    expect(busyState.secretKeyField).toMatchObject({
      disabled: true,
      helpText: "Alpaca credential request is already running.",
      disabledReason: "Alpaca credential request is already running."
    });
    expect(busyState.liveAcknowledgement.disabledReason).toBe("Alpaca credential request is already running.");
    expect(busyState.liveAcknowledgement.disabledReasonId).toBe("alpaca-live-acknowledgement-disabled-reason");
    expect(busyState.environmentOptions).toEqual([
      expect.objectContaining({
        value: "paper",
        isSelected: false,
        disabled: true,
        disabledReason: "Alpaca credential request is already running.",
        disabledReasonId: "alpaca-environment-disabled-reason"
      }),
      expect.objectContaining({
        value: "live",
        isSelected: true,
        disabled: true,
        disabledReason: "Alpaca credential request is already running.",
        disabledReasonId: "alpaca-environment-disabled-reason"
      })
    ]);

    const clearPendingState = buildAlpacaConnectionCommandState({
      canClear: true,
      clearConfirmationPending: true,
      form: {
        keyId: "",
        secretKey: "",
        environment: "paper",
        liveAcknowledged: false,
        busyAction: null,
        submitted: false,
        actionMessage: null,
        actionDetails: [],
        actionTone: "default"
      }
    });

    expect(clearPendingState).toMatchObject({
      clearLabel: "Confirm clear",
      formPanelTitle: "Confirm Alpaca credential clear",
      formPanelTone: "warning",
      clearDisabledReason: null
    });
    expect(clearPendingState.formPanelDetail).toContain("remove the stored Alpaca key reference");
  });

  it("requires explicit acknowledgement before testing live Alpaca credentials", () => {
    const pendingLiveState = buildAlpacaConnectionCommandState({
      canClear: true,
      form: {
        keyId: "AK123",
        secretKey: "secret",
        environment: "live",
        liveAcknowledged: false,
        busyAction: null,
        submitted: false,
        actionMessage: null,
        actionDetails: [],
        actionTone: "default"
      }
    });

    expect(pendingLiveState).toMatchObject({
      canSubmit: false,
      formPanelTitle: "Live endpoint review required",
      formPanelTone: "warning",
      submitDisabledReason: "Acknowledge the live Alpaca endpoint before testing live credentials."
    });
    expect(pendingLiveState.liveAcknowledgement).toMatchObject({
      visible: true,
      checked: false,
      required: true,
      disabled: false
    });
    expect(pendingLiveState.requirements).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "alpaca-live-acknowledgement-requirement", value: "Required", met: false, tone: "warning" })
    ]));

    const acknowledgedLiveState = buildAlpacaConnectionCommandState({
      canClear: true,
      form: {
        keyId: "AK123",
        secretKey: "secret",
        environment: "live",
        liveAcknowledged: true,
        busyAction: null,
        submitted: false,
        actionMessage: null,
        actionDetails: [],
        actionTone: "default"
      }
    });

    expect(acknowledgedLiveState.canSubmit).toBe(true);
    expect(acknowledgedLiveState.submitDisabledReason).toBeNull();
    expect(acknowledgedLiveState.liveAcknowledgement.checked).toBe(true);
    expect(acknowledgedLiveState.requirements).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "alpaca-live-acknowledgement-requirement", value: "Accepted", met: true, tone: "success" })
    ]));
  });

  it("surfaces canonical service coverage groups with browser routes and mapped services", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      strategy: { metrics: [], runs: [] },
      trading: {} as never,
      portfolio: {} as never,
      data: { metrics: [], providers: [], backfills: [], exports: [] },
      accounting: {} as never,
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
        expect.objectContaining({ href: "/api/workstation/runs/history", method: "GET", isBrowserNavigable: true }),
        expect.objectContaining({
          href: "/api/workstation/runs/compare",
          method: "POST",
          isBrowserNavigable: false,
          interactionLabel: "Reference"
        })
      ])
    );
    expect(vm.backendCapabilityGroups.find((group) => group.id === "portfolio")?.endpoints).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ href: "/api/workstation/portfolio", method: "GET", isBrowserNavigable: true }),
        expect.objectContaining({
          href: "/api/workstation/runs/{runId}/review-packet",
          method: "GET",
          isBrowserNavigable: false,
          interactionLabel: "Reference"
        })
      ])
    );
    expect(vm.backendCapabilityGroups.find((group) => group.id === "accounting")?.statusDetail).toContain(
      "templates and change actions stay reference-only"
    );
    expect(vm.backendCapabilityGroups.find((group) => group.id === "accounting")?.endpoints).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          href: "/api/workstation/security-master/securities",
          method: "GET",
          isBrowserNavigable: true
        }),
        expect.objectContaining({
          href: "/api/ledger/private-capital/activity",
          method: "GET",
          isBrowserNavigable: true
        }),
        expect.objectContaining({
          href: "/api/ledger/private-capital/report-output",
          method: "GET",
          isBrowserNavigable: true
        })
      ])
    );
    expect(vm.backendCapabilityGroups.find((group) => group.id === "data")?.endpoints).not.toEqual(
      expect.arrayContaining([
        expect.objectContaining({ href: "/api/workstation/security-master/securities" })
      ])
    );
    expect(vm.backendCapabilityGroups.find((group) => group.id === "data")?.endpoints).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ href: "/api/backfill/checkpoints", method: "GET", isBrowserNavigable: true }),
        expect.objectContaining({
          href: "/api/backfill/checkpoints/{jobId}/pending",
          method: "GET",
          isBrowserNavigable: false,
          interactionLabel: "Reference"
        }),
        expect.objectContaining({
          href: "/api/backfill/checkpoints/{jobId}/resume",
          method: "POST",
          isBrowserNavigable: false,
          interactionLabel: "Reference"
        })
      ])
    );
    expect(vm.backendCapabilityGroups.find((group) => group.id === "settings")?.endpoints).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ href: "/api/workstation/workflows", isBrowserNavigable: true }),
        expect.objectContaining({ href: "/api/workstation/workflows/presets", isBrowserNavigable: true })
      ])
    );
  });

  it("does not mark Portfolio capability as surfaced from Trading alone", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      strategy: { metrics: [], runs: [] },
      trading: {} as never,
      portfolio: null,
      data: { metrics: [], providers: [], backfills: [], exports: [] },
      accounting: {} as never,
      reporting: {} as never,
      loading: false,
      error: null,
      workspaceErrors: {}
    });

    expect(vm.backendCapabilityGroups.find((group) => group.id === "portfolio")).toMatchObject({
      statusLabel: "Unavailable",
      statusVariant: "danger",
      statusDetail: "Portfolio workspace data has not loaded."
    });
  });

  it("surfaces workspace diagnostic failures without hiding endpoint links", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      strategy: null,
      trading: null,
      data: null,
      accounting: null,
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
    expect(vm.diagnosticSummary).toContain("Checking 7 diagnostic services");
  });

  it("handles null overview gracefully", () => {
    const vm = buildSettingsScreenViewModel(null, null);
    expect(vm.hasOverview).toBe(false);
    expect(vm.systemItems).toHaveLength(0);
    expect(vm.systemSummary).toContain("unavailable");
  });

  it("does not submit live Alpaca credentials before acknowledgement", async () => {
    const connectConnection = vi.fn();
    const { result } = renderHook(() => useAlpacaConnectionFormViewModel({
      canClear: false,
      connectConnection
    }));

    act(() => {
      result.current.setEnvironment("live");
      result.current.setKeyId("live-key");
      result.current.setSecretKey("live-secret");
    });

    await act(async () => {
      await result.current.connect({ preventDefault: vi.fn() } as never);
    });

    expect(connectConnection).not.toHaveBeenCalled();
    expect(result.current.submitDisabledReason).toBe(
      "Acknowledge the live Alpaca endpoint before testing live credentials."
    );
    expect(result.current.liveAcknowledgement.required).toBe(true);
  });

  it("does not refresh or update Alpaca credential state after unmount", async () => {
    const connectResult = deferred<BrokerageConnectionStatus>();
    const connectConnection = vi.fn().mockReturnValue(connectResult.promise);
    const onRefresh = vi.fn();
    const { result, unmount } = renderHook(() => useAlpacaConnectionFormViewModel({
      canClear: false,
      connectConnection,
      onRefresh
    }));

    act(() => {
      result.current.setKeyId("paper-key");
      result.current.setSecretKey("paper-secret");
    });
    await act(async () => {
      void result.current.connect({ preventDefault: vi.fn() } as never);
    });
    await waitFor(() => expect(connectConnection).toHaveBeenCalledTimes(1));

    unmount();
    await act(async () => {
      connectResult.resolve(alpacaConnection);
      await connectResult.promise;
    });

    expect(onRefresh).not.toHaveBeenCalled();
  });

  it("passes an abort signal through Alpaca credential connect and aborts it on unmount", async () => {
    const connectResult = deferred<BrokerageConnectionStatus>();
    const connectConnection = vi.fn().mockReturnValue(connectResult.promise);
    const { result, unmount } = renderHook(() => useAlpacaConnectionFormViewModel({
      canClear: false,
      connectConnection
    }));

    act(() => {
      result.current.setKeyId("paper-key");
      result.current.setSecretKey("paper-secret");
    });
    await act(async () => {
      void result.current.connect({ preventDefault: vi.fn() } as never);
    });
    await waitFor(() => expect(connectConnection).toHaveBeenCalledTimes(1));

    const options = connectConnection.mock.calls[0]?.[1] as { signal?: AbortSignal };
    expect(options.signal).toBeInstanceOf(AbortSignal);
    expect(options.signal?.aborted).toBe(false);

    unmount();

    expect(options.signal?.aborted).toBe(true);
  });

  it("requires confirmation before clearing Alpaca credentials", async () => {
    const revokeConnection = vi.fn().mockResolvedValue(alpacaConnection);
    const { result } = renderHook(() => useAlpacaConnectionFormViewModel({
      canClear: true,
      revokeConnection
    }));

    await act(async () => {
      await result.current.clear();
    });

    expect(revokeConnection).not.toHaveBeenCalled();
    expect(result.current.clearLabel).toBe("Confirm clear");
    expect(result.current.formPanelTitle).toBe("Confirm Alpaca credential clear");

    act(() => {
      result.current.setKeyId("paper-key");
    });

    expect(result.current.clearLabel).toBe("Clear");

    await act(async () => {
      await result.current.clear();
    });
    await act(async () => {
      await result.current.clear();
    });

    expect(revokeConnection).toHaveBeenCalledTimes(1);
  });

  it("passes an abort signal through Alpaca credential clear and aborts it on unmount", async () => {
    const clearResult = deferred<BrokerageConnectionStatus>();
    const revokeConnection = vi.fn().mockReturnValue(clearResult.promise);
    const { result, unmount } = renderHook(() => useAlpacaConnectionFormViewModel({
      canClear: true,
      revokeConnection
    }));

    await act(async () => {
      await result.current.clear();
    });
    await act(async () => {
      void result.current.clear();
    });
    await waitFor(() => expect(revokeConnection).toHaveBeenCalledTimes(1));

    const options = revokeConnection.mock.calls[0]?.[0] as { signal?: AbortSignal };
    expect(options.signal).toBeInstanceOf(AbortSignal);
    expect(options.signal?.aborted).toBe(false);

    unmount();

    expect(options.signal?.aborted).toBe(true);
  });

  it("builds runtime feature capability toggles from shared API response", () => {
    const vm = buildSettingsScreenViewModel({
      session,
      overview,
      featureCapabilities: {
        capabilities: [
          {
            capabilityKey: "desktop.settings.workspace",
            displayName: "Settings workspace",
            description: "Preferences and diagnostics.",
            isEnabled: true,
            defaultEnabled: true,
            isPermanent: true,
            isOverridden: false,
            canToggle: false,
            disabledReason: "Required for workstation navigation."
          },
          {
            capabilityKey: "desktop.data.security-master",
            displayName: "Security master",
            description: "Reference-data review.",
            isEnabled: false,
            defaultEnabled: true,
            isPermanent: false,
            isOverridden: true,
            canToggle: true,
            disabledReason: null
          }
        ]
      }
    });

    expect(vm.runtimeCapabilitySection.statusLabel).toBe("1 disabled");
    expect(vm.runtimeCapabilitySection.toggles).toEqual([
      expect.objectContaining({
        capabilityKey: "desktop.settings.workspace",
        statusLabel: "Enabled",
        defaultLabel: "Default on",
        overrideLabel: "Using default",
        canToggle: false
      }),
      expect.objectContaining({
        capabilityKey: "desktop.data.security-master",
        statusLabel: "Disabled",
        defaultLabel: "Default on",
        overrideLabel: "Configured override",
        canToggle: true
      })
    ]);
  });

  it("surfaces structured Alpaca connection errors", async () => {
    const connectConnection = vi.fn().mockRejectedValue(
      new ApiError({
        path: "/api/brokerage-connections/alpaca/connect",
        status: 422,
        detail: "One or more validation errors occurred.",
        validationIssues: [
          {
            field: "secretKey",
            label: "secretKey",
            messages: ["Secret key must include the paper account scope."]
          }
        ]
      })
    );
    const { result } = renderHook(() => useAlpacaConnectionFormViewModel({
      canClear: false,
      connectConnection
    }));

    act(() => {
      result.current.setKeyId("paper-key");
      result.current.setSecretKey("paper-secret");
    });

    await act(async () => {
      await result.current.connect({ preventDefault: vi.fn() } as never);
    });

    expect(result.current.actionMessage).toBe("One or more validation errors occurred.");
    expect(result.current.statusDetails).toEqual([
      "Meridian service returned 422. Open diagnostics for technical details.",
      "secretKey: Secret key must include the paper account scope."
    ]);
    expect(result.current.statusRole).toBe("alert");
  });

  it("opens the Robinhood authorization URL and refreshes on connect", async () => {
    const startConnection = vi.fn().mockResolvedValue({
      ...robinhoodConnection,
      state: "AuthorizationPending",
      isConnected: false,
      authorizationUrl: "https://aggregator.example/oauth/authorize?token=abc"
    });
    const openAuthorizationUrl = vi.fn();
    const onRefresh = vi.fn();
    const { result } = renderHook(() => useRobinhoodConnectionViewModel({
      canConnect: true,
      canDisconnect: false,
      startConnection,
      openAuthorizationUrl,
      onRefresh
    }));

    await act(async () => {
      await result.current.connect();
    });

    expect(startConnection).toHaveBeenCalledTimes(1);
    expect(openAuthorizationUrl).toHaveBeenCalledWith("https://aggregator.example/oauth/authorize?token=abc");
    expect(onRefresh).toHaveBeenCalledTimes(1);
    expect(result.current.busy).toBe(false);
    expect(result.current.actionTone).toBe("success");
    expect(result.current.actionMessage).toContain("opened tab");
  });

  it("ignores Robinhood connect when connect is not permitted", async () => {
    const startConnection = vi.fn();
    const { result } = renderHook(() => useRobinhoodConnectionViewModel({
      canConnect: false,
      canDisconnect: true,
      startConnection
    }));

    await act(async () => {
      await result.current.connect();
    });

    expect(startConnection).not.toHaveBeenCalled();
  });

  it("revokes the Robinhood connection and refreshes on disconnect", async () => {
    const revokeConnection = vi.fn().mockResolvedValue({
      ...robinhoodConnection,
      state: "Disconnected",
      isConnected: false
    });
    const onRefresh = vi.fn();
    const { result } = renderHook(() => useRobinhoodConnectionViewModel({
      canConnect: false,
      canDisconnect: true,
      revokeConnection,
      onRefresh
    }));

    await act(async () => {
      await result.current.disconnect();
    });

    expect(revokeConnection).toHaveBeenCalledTimes(1);
    expect(onRefresh).toHaveBeenCalledTimes(1);
    expect(result.current.actionTone).toBe("success");
    expect(result.current.actionMessage).toBe("Robinhood connection revoked.");
  });

  it("ignores Robinhood disconnect when disconnect is not permitted", async () => {
    const revokeConnection = vi.fn();
    const onRefresh = vi.fn();
    const { result } = renderHook(() => useRobinhoodConnectionViewModel({
      canConnect: true,
      canDisconnect: false,
      revokeConnection,
      onRefresh
    }));

    await act(async () => {
      await result.current.disconnect();
    });

    expect(revokeConnection).not.toHaveBeenCalled();
    expect(onRefresh).not.toHaveBeenCalled();
    expect(result.current.busy).toBe(false);
  });

  it("reports Robinhood connect failures without throwing", async () => {
    const startConnection = vi.fn().mockRejectedValue(
      new ApiError({
        path: "/api/brokerage-connections/robinhood/connect",
        status: 502,
        detail: "Robinhood connect failed.",
        validationIssues: []
      })
    );
    const { result } = renderHook(() => useRobinhoodConnectionViewModel({
      canConnect: true,
      canDisconnect: false,
      startConnection
    }));

    await act(async () => {
      await result.current.connect();
    });

    expect(result.current.actionTone).toBe("danger");
    expect(result.current.statusRole).toBe("alert");
    expect(result.current.busy).toBe(false);
  });
});

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}
