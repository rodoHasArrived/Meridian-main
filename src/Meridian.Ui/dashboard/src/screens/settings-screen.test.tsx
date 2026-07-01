import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import { SettingsScreen } from "@/screens/settings-screen";
import { renderWithRouter } from "@/test/render";
import type {
  BrokerageConnectionStatus,
  LedgerMappingWorkbench,
  OperationsApprovalPolicyMatrix,
  OperationsCloseCalendar,
  PortfolioWorkspaceResponse,
  ProviderConnectionRow,
  ProviderIntegrationConnectionMonitor,
  ProviderIntegrationPromotionReadinessPreview,
  ProviderIntegrationQuarantineReview,
  ProviderIntegrationReconciliationHandoffHistory,
  ProviderIntegrationStagingIdentityResolutionPreview,
  ProviderIntegrationStagingReview,
  ProviderIntegrationSyncPlan,
  ProviderIntegrationSyncRunHistory,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot,
  RolePermissionCatalog,
  SecurityAssetProfileDefinition,
  SessionInfo,
  SystemOverviewResponse,
  UserAccessAssignment
} from "@/types";

const apiMocks = vi.hoisted(() => ({
  approveSecurityAssetProfile: vi.fn(),
  assignLedgerMapping: vi.fn(),
  createSecurityMasterEntry: vi.fn(),
  createRolePermissionProfile: vi.fn(),
  createScopedAccessAssignment: vi.fn(),
  draftSecurityAssetProfile: vi.fn(),
  getSecurityAssetProfileLineage: vi.fn(),
  listScopedAccessAssignments: vi.fn(),
  revokeScopedAccessAssignment: vi.fn(),
  rollbackSecurityAssetProfile: vi.fn(),
  upsertOperationsApprovalPolicyRule: vi.fn(),
  upsertOperationsCloseCalendarItem: vi.fn(),
  connectAlpacaConnection: vi.fn(),
  revokeAlpacaConnection: vi.fn(),
  testProviderConnection: vi.fn(),
  putProviderCredentials: vi.fn(),
  activateProviderIntegration: vi.fn(),
  checkProviderIntegrationSchemaDrift: vi.fn(),
  createProviderIntegrationReconciliationHandoff: vi.fn(),
  getProviderIntegrationReadiness: vi.fn(),
  getProviderIntegrationTemplate: vi.fn(),
  getProviderIntegrationTemplates: vi.fn(),
  importProviderIntegrationOpenApi: vi.fn(),
  replayProviderIntegrationQuarantineRecords: vi.fn(),
  resolveProviderIntegrationQuarantineRecord: vi.fn(),
  runDueProviderIntegrationSync: vi.fn(),
  runManualCsvProviderIntegrationDryRun: vi.fn(),
  runRestProviderIntegrationDryRun: vi.fn(),
  saveProviderIntegrationSetup: vi.fn(),
  verifyProviderConnection: vi.fn(),
  deleteProviderCredentials: vi.fn(),
  getProviderIntegrationConnectionMonitor: vi.fn(),
  getProviderIntegrationConnectionSyncPlan: vi.fn(),
  getProviderIntegrationConnectionSyncRuns: vi.fn(),
  getProviderIntegrationIdentityResolution: vi.fn(),
  getProviderIntegrationPromotionReadiness: vi.fn(),
  getProviderIntegrationQuarantineReview: vi.fn(),
  getProviderIntegrationReconciliationHandoffHistory: vi.fn(),
  getProviderIntegrationStagingReview: vi.fn()
}));

vi.mock("@/lib/api", async (importActual) => ({
  ...(await importActual<typeof import("@/lib/api")>()),
  approveSecurityAssetProfile: apiMocks.approveSecurityAssetProfile,
  assignLedgerMapping: apiMocks.assignLedgerMapping,
  createSecurityMasterEntry: apiMocks.createSecurityMasterEntry,
  createRolePermissionProfile: apiMocks.createRolePermissionProfile,
  createScopedAccessAssignment: apiMocks.createScopedAccessAssignment,
  draftSecurityAssetProfile: apiMocks.draftSecurityAssetProfile,
  getSecurityAssetProfileLineage: apiMocks.getSecurityAssetProfileLineage,
  listScopedAccessAssignments: apiMocks.listScopedAccessAssignments,
  revokeScopedAccessAssignment: apiMocks.revokeScopedAccessAssignment,
  rollbackSecurityAssetProfile: apiMocks.rollbackSecurityAssetProfile,
  upsertOperationsApprovalPolicyRule: apiMocks.upsertOperationsApprovalPolicyRule,
  upsertOperationsCloseCalendarItem: apiMocks.upsertOperationsCloseCalendarItem,
  connectAlpacaConnection: apiMocks.connectAlpacaConnection,
  revokeAlpacaConnection: apiMocks.revokeAlpacaConnection,
  testProviderConnection: apiMocks.testProviderConnection,
  putProviderCredentials: apiMocks.putProviderCredentials,
  activateProviderIntegration: apiMocks.activateProviderIntegration,
  checkProviderIntegrationSchemaDrift: apiMocks.checkProviderIntegrationSchemaDrift,
  createProviderIntegrationReconciliationHandoff: apiMocks.createProviderIntegrationReconciliationHandoff,
  getProviderIntegrationReadiness: apiMocks.getProviderIntegrationReadiness,
  getProviderIntegrationTemplate: apiMocks.getProviderIntegrationTemplate,
  getProviderIntegrationTemplates: apiMocks.getProviderIntegrationTemplates,
  importProviderIntegrationOpenApi: apiMocks.importProviderIntegrationOpenApi,
  replayProviderIntegrationQuarantineRecords: apiMocks.replayProviderIntegrationQuarantineRecords,
  resolveProviderIntegrationQuarantineRecord: apiMocks.resolveProviderIntegrationQuarantineRecord,
  runDueProviderIntegrationSync: apiMocks.runDueProviderIntegrationSync,
  runManualCsvProviderIntegrationDryRun: apiMocks.runManualCsvProviderIntegrationDryRun,
  runRestProviderIntegrationDryRun: apiMocks.runRestProviderIntegrationDryRun,
  saveProviderIntegrationSetup: apiMocks.saveProviderIntegrationSetup,
  verifyProviderConnection: apiMocks.verifyProviderConnection,
  deleteProviderCredentials: apiMocks.deleteProviderCredentials,
  getProviderIntegrationConnectionMonitor: apiMocks.getProviderIntegrationConnectionMonitor,
  getProviderIntegrationConnectionSyncPlan: apiMocks.getProviderIntegrationConnectionSyncPlan,
  getProviderIntegrationConnectionSyncRuns: apiMocks.getProviderIntegrationConnectionSyncRuns,
  getProviderIntegrationIdentityResolution: apiMocks.getProviderIntegrationIdentityResolution,
  getProviderIntegrationPromotionReadiness: apiMocks.getProviderIntegrationPromotionReadiness,
  getProviderIntegrationQuarantineReview: apiMocks.getProviderIntegrationQuarantineReview,
  getProviderIntegrationReconciliationHandoffHistory: apiMocks.getProviderIntegrationReconciliationHandoffHistory,
  getProviderIntegrationStagingReview: apiMocks.getProviderIntegrationStagingReview
}));

const session: SessionInfo = {
  displayName: "Andrew Rowden",
  role: "Fund Manager",
  environment: "paper",
  activeWorkspace: "settings",
  commandCount: 42
};

const overview: SystemOverviewResponse = {
  systemStatus: "Degraded",
  providersOnline: 2,
  providersTotal: 3,
  activeRuns: 1,
  openPositions: 5,
  activeBackfills: 0,
  symbolsMonitored: 120,
  storageHealth: "Warning",
  lastHeartbeatUtc: "2026-05-01T00:00:00Z",
  metrics: [],
  recentEvents: [
    {
      id: "evt-1",
      type: "warning",
      message: "Brokerage sync delayed.",
      source: "Provider health",
      timestamp: "2026-05-01T00:00:00Z"
    }
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
    actionHref: "/settings#alpaca-provider-setup",
    credentialFields: [
      {
        name: "KeyId",
        label: "Key ID",
        required: true,
        inputKind: "Password",
        placeholder: "ALPACA_API_KEY_ID",
        helpText: "Stored in Meridian's encrypted local provider store for Alpaca account verification."
      },
      {
        name: "SecretKey",
        label: "Secret key",
        required: true,
        inputKind: "Password",
        placeholder: "ALPACA_API_SECRET_KEY",
        helpText: "Stored in Meridian's encrypted local provider store for Alpaca account verification."
      }
    ],
    environmentOptions: [
      { value: "paper", label: "Paper", isDefault: true },
      { value: "live", label: "Live", isDefault: false }
    ]
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
    actionHref: "/settings#provider-polygon-connection",
    credentialFields: [
      {
        name: "ApiKey",
        label: "API key",
        required: true,
        inputKind: "Password",
        placeholder: "POLYGON_API_KEY",
        helpText: "Stored in Meridian's encrypted local provider store and masked after save."
      }
    ],
    environmentOptions: []
  }
];

const providerRoutingConnections: ProviderRoutingConnection[] = [
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
    bindingId: "provider-reference-ReferenceData",
    capability: "ReferenceData",
    connectionId: "provider-reference",
    target: null,
    priority: 100,
    enabled: true,
    failoverConnectionIds: [],
    safetyModeOverride: null,
    notes: null
  }
];

const providerRoutingTrustSnapshots: ProviderRoutingTrustSnapshot[] = [
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

const portfolio: PortfolioWorkspaceResponse = {
  metrics: [],
  positions: [],
  risk: {
    state: "Healthy",
    summary: "No portfolio risk flags.",
    netExposure: "$0",
    grossExposure: "$0",
    var95: "$0",
    maxDrawdown: "0%",
    activeGuardrails: [],
    buyingPowerUsed: "0%"
  },
  brokerage: {
    provider: "Alpaca",
    account: "PA-DEMO",
    environment: "paper",
    connection: "Connected",
    orderIngress: "healthy",
    fillFeed: "healthy",
    lastHeartbeat: "2026-05-07T12:00:00Z",
    notes: "Paper brokerage fixture is healthy."
  },
  runs: [],
  cashFlow: null
};

const rolePermissionCatalog: RolePermissionCatalog = {
  roles: [
    {
      role: "Accounting",
      displayName: "Accounting",
      description: "Accounting and fund-operations access.",
      isBuiltIn: true,
      permissions: ["ViewTrades", "ManageDirectLending"],
      permissionMask: 0
    }
  ],
  permissions: [
    { name: "ViewTrades", value: 0, group: "Trading", description: "View trade records." },
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
      ledgerGroupId: "direct-lending",
      displayName: "Direct lending",
      accountIds: [],
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
    profileId: "private-fund-interest",
    version: 1,
    name: "Private Fund Interest",
    category: "AlternativeAsset",
    subType: "PrivateFundInterest",
    status: "Approved",
    fields: [
      {
        key: "sponsor",
        label: "Sponsor",
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
        key: "navDate",
        label: "NAV date",
        fieldType: "Date",
        isRequired: true,
        allowedValues: [],
        description: null,
        minValue: null,
        maxValue: null,
        isProjected: true,
        isSearchable: true
      },
      {
        key: "commitment",
        label: "Commitment",
        fieldType: "Decimal",
        isRequired: false,
        allowedValues: [],
        description: null,
        minValue: 0,
        maxValue: null,
        isProjected: true,
        isSearchable: false
      }
    ],
    identifierPreferences: [
      { kind: "InternalCode", isRequiredForClose: true, reason: "Private funds require an internal code." }
    ],
    lifecycleStates: ["Committed", "Funded", "Harvesting"],
    accountingImpactHints: ["CommitmentAccounting", "NavBasedValuation"],
    dateOrderRules: [],
    effectiveFrom: "2026-05-01",
    effectiveTo: null,
    approvedBy: "Security Master Council",
    approvedAtUtc: "2026-05-01T00:00:00Z",
    changeReason: "Seed template"
  }
];

describe("SettingsScreen", () => {
  beforeEach(() => {
    apiMocks.approveSecurityAssetProfile.mockReset();
    apiMocks.connectAlpacaConnection.mockReset();
    apiMocks.createSecurityMasterEntry.mockReset();
    apiMocks.draftSecurityAssetProfile.mockReset();
    apiMocks.getSecurityAssetProfileLineage.mockReset();
    apiMocks.assignLedgerMapping.mockReset();
    apiMocks.createRolePermissionProfile.mockReset();
    apiMocks.createScopedAccessAssignment.mockReset();
    apiMocks.listScopedAccessAssignments.mockReset();
    apiMocks.revokeScopedAccessAssignment.mockReset();
    apiMocks.listScopedAccessAssignments.mockImplementation(() => new Promise(() => undefined));
    apiMocks.rollbackSecurityAssetProfile.mockReset();
    apiMocks.upsertOperationsApprovalPolicyRule.mockReset();
    apiMocks.upsertOperationsCloseCalendarItem.mockReset();
    apiMocks.revokeAlpacaConnection.mockReset();
    apiMocks.testProviderConnection.mockReset();
    apiMocks.putProviderCredentials.mockReset();
    apiMocks.createProviderIntegrationReconciliationHandoff.mockReset();
    apiMocks.importProviderIntegrationOpenApi.mockReset();
    apiMocks.replayProviderIntegrationQuarantineRecords.mockReset();
    apiMocks.resolveProviderIntegrationQuarantineRecord.mockReset();
    apiMocks.runDueProviderIntegrationSync.mockReset();
    apiMocks.verifyProviderConnection.mockReset();
    apiMocks.deleteProviderCredentials.mockReset();
    apiMocks.getProviderIntegrationConnectionMonitor.mockReset();
    apiMocks.getProviderIntegrationConnectionSyncPlan.mockReset();
    apiMocks.getProviderIntegrationConnectionSyncRuns.mockReset();
    apiMocks.getProviderIntegrationIdentityResolution.mockReset();
    apiMocks.getProviderIntegrationPromotionReadiness.mockReset();
    apiMocks.getProviderIntegrationQuarantineReview.mockReset();
    apiMocks.getProviderIntegrationReconciliationHandoffHistory.mockReset();
    apiMocks.getProviderIntegrationStagingReview.mockReset();
  });

  it("renders recent events as accessible status evidence rows", () => {
    renderWithRouter(<SettingsScreen session={session} overview={overview} />, {
      initialEntries: ["/settings#diagnostic-endpoints"]
    });

    expect(screen.getByRole("region", { name: "Settings workbench context" })).toHaveTextContent(
      "Operator control posture"
    );
    const eventTable = screen.getByRole("treegrid", { name: "1 recent system event" });
    const eventRow = within(eventTable).getByRole("row", {
      name: /Select event evt-1\. OBS event from Provider health at May 1, 00:00 UTC\. Brokerage sync delayed\./i
    });
    const eventDetail = screen.getByRole("complementary", { name: "Selected recent event detail" });

    expect(eventRow).toHaveAttribute("aria-selected", "true");
    expect(eventRow).toHaveAttribute("aria-controls", "settings-recent-event-detail");
    expect(eventRow).toHaveAttribute("aria-expanded", "true");
    expect(within(eventRow).getByText("OBS")).toBeInTheDocument();
    expect(within(eventRow).getByText("Brokerage sync delayed.")).toBeInTheDocument();
    expect(within(eventRow).getByText("Provider health")).toBeInTheDocument();
    expect(within(eventDetail).getByText("Brokerage sync delayed.")).toBeInTheDocument();
    expect(within(eventDetail).getByText("Provider health / evt-1")).toBeInTheDocument();
  });

  it("renders profile authentication posture with authority handoffs", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    const profileRegion = screen.getByRole("region", { name: "Profile and authentication posture" });
    expect(profileRegion).toHaveTextContent("Profile and access posture");
    expect(profileRegion).toHaveTextContent("Access ready");
    expect(profileRegion).toHaveTextContent("Andrew Rowden");
    expect(profileRegion).toHaveTextContent("Fund Manager");
    expect(profileRegion).toHaveTextContent("42 commands issued");
    expect(profileRegion).toHaveTextContent("Brokerage verified");
    expect(within(profileRegion).getByRole("list", {
      name: "Profile authentication and authorization readiness steps"
    })).toBeInTheDocument();
    expect(within(profileRegion).getByRole("link", {
      name: "Open Trading readiness from verified profile authentication posture"
    })).toHaveAttribute("href", "/trading/readiness");
    expect(within(profileRegion).getByRole("link", {
      name: "Open Settings diagnostic services from profile authentication posture"
    })).toHaveAttribute("href", "/settings#diagnostic-endpoints");
    expect(document.querySelector("#diagnostic-endpoints")).not.toBeInTheDocument();
  });

  it("renders fund operations controls for mappings, roles, approvals, and close calendar", () => {
    renderWithRouter(
      <SettingsScreen
        session={{ ...session, role: "Accounting" }}
        overview={overview}
        rolePermissionCatalog={rolePermissionCatalog}
        ledgerMappingWorkbench={ledgerMappingWorkbench}
        operationsApprovalPolicyMatrix={approvalPolicyMatrix}
        operationsCloseCalendar={closeCalendar}
      />
    );

    const controlCenter = screen.getByRole("list", { name: "Fund operations configuration surfaces" });
    expect(within(controlCenter).getByText("Ledger Mapping Workbench")).toBeInTheDocument();
    expect(within(controlCenter).getByText("Role and Permission Studio")).toBeInTheDocument();
    expect(within(controlCenter).getByText("Approval Policy Matrix")).toBeInTheDocument();
    expect(within(controlCenter).getByText("Account Close Calendar")).toBeInTheDocument();
    expect(within(controlCenter).getByText("1 unmapped")).toBeInTheDocument();
    expect(within(controlCenter).getByText("Accounting active")).toBeInTheDocument();
    expect(within(controlCenter).getByText("1 rules")).toBeInTheDocument();
    expect(within(controlCenter).getByText("1 blocked")).toBeInTheDocument();
    expect(within(controlCenter).getByRole("link", { name: "Open role and permission catalog service" })).toHaveAttribute(
      "href",
      "/api/auth/roles"
    );
    expect(within(controlCenter).getByRole("link", { name: "Open service details for Account Close Calendar" })).toHaveAttribute(
      "href",
      "/api/workstation/operations/continuity/close-calendar"
    );
  });

  it("renders asset profile accounting and profile-backed creation fields", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        securityAssetProfiles={securityAssetProfiles}
      />
    );

    const profileRows = screen.getByRole("list", { name: "1 asset profile" });
    expect(within(profileRows).getByText("Private Fund Interest")).toBeInTheDocument();
    expect(within(profileRows).getByText("InternalCode")).toBeInTheDocument();
    expect(screen.getByRole("form", { name: "Draft asset profile" })).toBeInTheDocument();

    const createForm = screen.getByRole("form", { name: "Create profile-backed security" });
    expect(within(createForm).getByLabelText("Profile-backed security display name")).toBeInTheDocument();
    expect(within(createForm).getByLabelText("Profile field Sponsor")).toBeInTheDocument();
    expect(within(createForm).getByLabelText("Profile field NAV date")).toBeInTheDocument();
  });

  it("creates profile-backed securities pinned to the approved profile version", async () => {
    const user = userEvent.setup();
    const onRefresh = vi.fn();
    apiMocks.createSecurityMasterEntry.mockResolvedValue({
      securityId: "sec-private-fund",
      displayName: "Meridian Private Fund I",
      status: "Active",
      classification: {
        assetClass: "CustomAsset",
        subType: "PrivateFundInterest",
        primaryIdentifierKind: "InternalCode",
        primaryIdentifierValue: "PF-I",
        matchedIdentifierKind: null,
        matchedIdentifierValue: null,
        matchedProvider: null
      },
      economicDefinition: {
        currency: "USD",
        version: 1,
        effectiveFrom: "2026-05-29T00:00:00Z",
        effectiveTo: null,
        subType: "PrivateFundInterest",
        assetFamily: "AlternativeAsset",
        issuerType: null
      }
    });

    renderWithRouter(
      <SettingsScreen
        session={{ ...session, displayName: "Ops Lead" }}
        overview={overview}
        securityAssetProfiles={securityAssetProfiles}
        onRefresh={onRefresh}
      />
    );

    const form = screen.getByRole("form", { name: "Create profile-backed security" });
    await user.type(within(form).getByLabelText("Profile-backed security display name"), "Meridian Private Fund I");
    await user.type(within(form).getByLabelText("Profile-backed security internal code"), "PF-I");
    await user.type(within(form).getByLabelText("Profile field Sponsor"), "Meridian GP");
    await user.type(within(form).getByLabelText("Profile field NAV date"), "2026-04-30");
    await user.click(within(form).getByRole("button", { name: /Create security/i }));

    expect(apiMocks.createSecurityMasterEntry).toHaveBeenCalledWith(expect.objectContaining({
      assetClass: "CustomAsset",
      updatedBy: "Ops Lead",
      sourceRecordId: "asset-profile:private-fund-interest:v1",
      assetSpecificTerms: expect.objectContaining({
        schemaVersion: 3,
        customProfileId: "private-fund-interest",
        profileVersion: 1,
        category: "AlternativeAsset",
        profileFields: expect.objectContaining({
          sponsor: "Meridian GP",
          navDate: "2026-04-30"
        }),
        profileApproval: expect.objectContaining({
          approvalReference: "profile:private-fund-interest:v1"
        })
      }),
      identifiers: [
        expect.objectContaining({
          kind: "InternalCode",
          value: "PF-I",
          isPrimary: true
        })
      ]
    }));
    expect(onRefresh).toHaveBeenCalledOnce();
    expect(await within(form).findByText("Security created for Meridian Private Fund I.")).toBeInTheDocument();
  });

  it("submits ledger mapping assignments with audit rationale", async () => {
    const user = userEvent.setup();
    apiMocks.assignLedgerMapping.mockResolvedValue({
      assignment: {
        assignmentId: "assignment-1",
        nodeId: "account-2",
        assignmentType: "LedgerGroup",
        assignmentReference: "direct-lending",
        effectiveFrom: "2026-05-28T00:00:00Z",
        effectiveTo: null,
        isPrimary: true
      },
      account: {
        ...ledgerMappingWorkbench.accounts[0],
        mapping: {
          ...ledgerMappingWorkbench.accounts[0].mapping,
          ledgerGroupId: "direct-lending",
          requiresUserMapping: false
        }
      },
      auditEvent: {
        auditId: "audit-1",
        eventType: "ledger-mapping-assigned",
        occurredAtUtc: "2026-05-28T00:00:00Z",
        actor: "Accounting",
        rationale: "Reviewed by fund operations",
        correlationId: "settings-ledger-map-1",
        accountId: "account-2",
        accountCode: "OPS-2",
        fromLedgerGroupId: null,
        toLedgerGroupId: "direct-lending",
        assignmentId: "assignment-1"
      },
      workbench: ledgerMappingWorkbench
    });
    const onRefresh = vi.fn();

    renderWithRouter(
      <SettingsScreen
        session={{ ...session, role: "Accounting", displayName: "Ops Lead" }}
        overview={overview}
        ledgerMappingWorkbench={ledgerMappingWorkbench}
        onRefresh={onRefresh}
      />
    );

    const form = screen.getByRole("form", { name: "Assign ledger mapping" });
    await user.clear(within(form).getByLabelText("Ledger mapping rationale"));
    await user.type(within(form).getByLabelText("Ledger mapping rationale"), "Reviewed by fund operations");
    await user.click(within(form).getByRole("button", { name: /Save mapping/i }));

    expect(apiMocks.assignLedgerMapping).toHaveBeenCalledWith(expect.objectContaining({
      accountId: "account-2",
      ledgerGroupId: "direct-lending",
      requestedBy: "Ops Lead",
      rationale: "Reviewed by fund operations"
    }));
    expect(onRefresh).toHaveBeenCalledOnce();
    expect(await within(form).findByText("Ledger mapping saved for OPS-2.")).toBeInTheDocument();
    expect(within(form).getByText("Audit audit-1")).toBeInTheDocument();
  });

  it("creates role profiles with explicit permissions and audit rationale", async () => {
    const user = userEvent.setup();
    apiMocks.createRolePermissionProfile.mockResolvedValue({
      profile: {
        role: "Close Reviewer",
        displayName: "Close Reviewer",
        description: "Close review authority.",
        isBuiltIn: false,
        permissions: ["ViewTrades", "ManageDirectLending"],
        permissionMask: 3,
        baseRole: "Accounting",
        createdBy: "Ops Lead",
        createdAtUtc: "2026-05-28T00:00:00Z",
        updatedBy: "Ops Lead",
        updatedAtUtc: "2026-05-28T00:00:00Z",
        lastRationale: "Scoped close reviewer",
        lastAuditId: "role-audit-1"
      },
      catalog: rolePermissionCatalog,
      auditEvent: {
        auditId: "role-audit-1",
        eventType: "role-permission-profile-upserted",
        occurredAtUtc: "2026-05-28T00:00:00Z",
        actor: "Ops Lead",
        rationale: "Scoped close reviewer",
        correlationId: "settings-role-profile-1",
        profileName: "Close Reviewer",
        baseRole: "Accounting",
        permissionNames: ["ViewTrades", "ManageDirectLending"],
        permissionMask: 3
      }
    });
    const onRefresh = vi.fn();

    renderWithRouter(
      <SettingsScreen
        session={{ ...session, role: "Accounting", displayName: "Ops Lead" }}
        overview={overview}
        rolePermissionCatalog={rolePermissionCatalog}
        onRefresh={onRefresh}
      />
    );

    const form = screen.getByRole("form", { name: "Create role profile" });
    await user.type(within(form).getByLabelText("Role profile name"), "Close Reviewer");
    await user.clear(within(form).getByLabelText("Role profile rationale"));
    await user.type(within(form).getByLabelText("Role profile rationale"), "Scoped close reviewer");
    await user.click(within(form).getByRole("button", { name: /Save profile/i }));

    expect(apiMocks.createRolePermissionProfile).toHaveBeenCalledWith(expect.objectContaining({
      profileName: "Close Reviewer",
      displayName: "Close Reviewer",
      baseRole: "Accounting",
      permissionNames: ["ViewTrades", "ManageDirectLending"],
      requestedBy: "Ops Lead",
      rationale: "Scoped close reviewer"
    }));
    expect(onRefresh).toHaveBeenCalledOnce();
    expect(await within(form).findByText("Role profile saved for Close Reviewer.")).toBeInTheDocument();
    expect(within(form).getByText("Audit role-audit-1")).toBeInTheDocument();
  });

  it("grants and revokes scoped access assignments with audit evidence", async () => {
    const user = userEvent.setup();
    const assignment: UserAccessAssignment = {
      assignmentId: "access-assignment-1",
      principalId: "fund-controller",
      principalKind: "User",
      scopeKind: "Fund",
      scopeId: "fund-2026",
      role: "Accounting",
      roleProfileName: "Close Reviewer",
      permissionNames: ["ManageDirectLending"],
      permissionMask: 2,
      effectiveFrom: "2026-06-01T00:00:00Z",
      effectiveTo: null,
      grantedBy: "Ops Lead",
      rationale: "Month-end close control",
      correlationId: "access-correlation-1",
      version: 3,
      createdAtUtc: "2026-06-01T00:00:00Z",
      updatedAtUtc: "2026-06-01T00:00:00Z",
      revokedBy: null,
      revokedAtUtc: null,
      revocationReason: null,
      lastAuditId: "access-audit-1",
      approvalLimitAmount: 100000,
      approvalLimitCurrency: "USD",
      segregationOfDutiesRule: "Requester cannot approve own payment request."
    };
    const createdAssignment: UserAccessAssignment = {
      ...assignment,
      assignmentId: "access-assignment-2",
      principalId: "fund-reviewer",
      scopeId: "fund-review",
      roleProfileName: null,
      permissionNames: ["ViewTrades", "ManageDirectLending"],
      permissionMask: 3,
      rationale: "Grant fund close authority",
      correlationId: "access-correlation-2",
      version: 1,
      lastAuditId: "access-audit-2",
      approvalLimitAmount: 250000,
      approvalLimitCurrency: "USD",
      segregationOfDutiesRule: "Requester cannot approve own payment request."
    };
    const revokedAssignment: UserAccessAssignment = {
      ...assignment,
      version: 4,
      revokedBy: "Ops Lead",
      revokedAtUtc: "2026-06-02T00:00:00Z",
      revocationReason: "Revoke scoped authority for fund-controller.",
      lastAuditId: "access-audit-3"
    };
    apiMocks.listScopedAccessAssignments.mockResolvedValue([assignment]);
    apiMocks.createScopedAccessAssignment.mockResolvedValue({
      assignment: createdAssignment,
      auditEvent: {
        auditId: "access-audit-2",
        eventType: "scoped-access-granted",
        occurredAtUtc: "2026-06-02T00:00:00Z",
        actor: "Ops Lead",
        rationale: "Grant fund close authority",
        correlationId: "access-correlation-2",
        assignmentId: createdAssignment.assignmentId,
        principalId: createdAssignment.principalId,
        scopeKind: createdAssignment.scopeKind,
        scopeId: createdAssignment.scopeId,
        permissionNames: createdAssignment.permissionNames,
        permissionMask: createdAssignment.permissionMask,
        version: createdAssignment.version,
        approvalLimitAmount: createdAssignment.approvalLimitAmount,
        approvalLimitCurrency: createdAssignment.approvalLimitCurrency,
        segregationOfDutiesRule: createdAssignment.segregationOfDutiesRule
      }
    });
    apiMocks.revokeScopedAccessAssignment.mockResolvedValue({
      assignment: revokedAssignment,
      auditEvent: {
        auditId: "access-audit-3",
        eventType: "scoped-access-revoked",
        occurredAtUtc: "2026-06-02T00:00:00Z",
        actor: "Ops Lead",
        rationale: "Revoke scoped authority for fund-controller.",
        correlationId: "access-correlation-3",
        assignmentId: revokedAssignment.assignmentId,
        principalId: revokedAssignment.principalId,
        scopeKind: revokedAssignment.scopeKind,
        scopeId: revokedAssignment.scopeId,
        permissionNames: revokedAssignment.permissionNames,
        permissionMask: revokedAssignment.permissionMask,
        version: revokedAssignment.version,
        approvalLimitAmount: revokedAssignment.approvalLimitAmount,
        approvalLimitCurrency: revokedAssignment.approvalLimitCurrency,
        segregationOfDutiesRule: revokedAssignment.segregationOfDutiesRule
      }
    });
    const onRefresh = vi.fn();

    renderWithRouter(
      <SettingsScreen
        session={{ ...session, role: "Accounting", displayName: "Ops Lead" }}
        overview={overview}
        rolePermissionCatalog={rolePermissionCatalog}
        onRefresh={onRefresh}
      />,
      {
        initialEntries: ["/settings#settings-overview"]
      }
    );

    const consoleRegion = await screen.findByRole("region", { name: "Scoped access assignment console" });
    expect(within(consoleRegion).getByText("fund-controller")).toBeInTheDocument();
    expect(within(consoleRegion).getByText("access-audit-1")).toBeInTheDocument();
    expect(within(consoleRegion).getByText("USD 100,000")).toBeInTheDocument();
    expect(within(consoleRegion).getByText("Requester cannot approve own payment request.")).toBeInTheDocument();

    const form = screen.getByRole("form", { name: "Grant scoped access assignment" });
    await waitFor(() => expect(within(form).getByLabelText("Role")).toHaveValue("Accounting"));
    fireEvent.change(within(form).getByLabelText("Scoped access principal id"), { target: { value: "fund-reviewer" } });
    fireEvent.change(within(form).getByLabelText("Scoped access scope id"), { target: { value: "fund-review" } });
    fireEvent.change(within(form).getByLabelText("Scoped access approval limit amount"), { target: { value: "250000" } });
    fireEvent.change(within(form).getByLabelText("Scoped access approval limit currency"), { target: { value: "usd" } });
    fireEvent.change(within(form).getByLabelText("Scoped access segregation of duties rule"), {
      target: { value: "Requester cannot approve own payment request." }
    });
    fireEvent.change(within(form).getByLabelText("Scoped access rationale"), { target: { value: "Grant fund close authority" } });
    await user.click(within(form).getByRole("button", { name: /Grant access/i }));

    expect(apiMocks.createScopedAccessAssignment).toHaveBeenCalledWith(expect.objectContaining({
      principalId: "fund-reviewer",
      principalKind: "User",
      scopeKind: "Fund",
      scopeId: "fund-review",
      role: "Accounting",
      roleProfileName: null,
      permissionNames: ["ViewTrades", "ManageDirectLending"],
      requestedBy: "Ops Lead",
      rationale: "Grant fund close authority",
      approvalLimitAmount: 250000,
      approvalLimitCurrency: "USD",
      segregationOfDutiesRule: "Requester cannot approve own payment request."
    }));
    expect(await within(form).findByText("Scoped access granted for fund-reviewer.")).toBeInTheDocument();
    expect(within(form).getByText("Audit access-audit-2")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Revoke scoped access for fund-controller" }));

    expect(apiMocks.revokeScopedAccessAssignment).toHaveBeenCalledWith(expect.objectContaining({
      assignmentId: "access-assignment-1",
      expectedVersion: 3,
      requestedBy: "Ops Lead",
      rationale: "Revoke scoped authority for fund-controller."
    }));
    expect(await within(form).findByText("Scoped access revoked for fund-controller.")).toBeInTheDocument();
    expect(onRefresh).toHaveBeenCalledTimes(2);
    expect(screen.queryByText("fund-controller")).toBeNull();
  });

  it("updates approval policy rules with reviewer and evidence controls", async () => {
    const user = userEvent.setup();
    apiMocks.upsertOperationsApprovalPolicyRule.mockResolvedValue({
      rule: {
        ...approvalPolicyMatrix.rows[0],
        reviewerRole: "Controller",
        requiredDistinctApprovals: 3,
        evidenceRequirement: "Controller packet and checklist control evidence."
      },
      matrix: approvalPolicyMatrix,
      auditEvent: {
        auditId: "approval-policy-audit-1",
        eventType: "operations-approval-policy-rule-upserted",
        occurredAtUtc: "2026-05-28T00:00:00Z",
        actor: "Ops Lead",
        rationale: "Tighten close approval accounting",
        correlationId: "settings-approval-policy-1",
        policyKey: approvalPolicyMatrix.rows[0].policyKey,
        action: approvalPolicyMatrix.rows[0].action,
        gate: approvalPolicyMatrix.rows[0].gate,
        requiredDistinctApprovals: 3,
        requiresIndependentReviewer: true,
        requiresReportPack: true,
        requiresChecklistControlApprovals: true
      }
    });
    const onRefresh = vi.fn();

    renderWithRouter(
      <SettingsScreen
        session={{ ...session, role: "Accounting", displayName: "Ops Lead" }}
        overview={overview}
        operationsApprovalPolicyMatrix={approvalPolicyMatrix}
        onRefresh={onRefresh}
      />
    );

    const form = screen.getByRole("form", { name: "Configure approval policy rule" });
    fireEvent.change(within(form).getByLabelText("Approval policy reviewer role"), { target: { value: "Controller" } });
    fireEvent.change(within(form).getByLabelText("Approval policy required distinct approvals"), { target: { value: "3" } });
    fireEvent.change(within(form).getByLabelText("Approval policy evidence requirement"), {
      target: { value: "Controller packet and checklist control evidence." }
    });
    fireEvent.change(within(form).getByLabelText("Approval policy rationale"), { target: { value: "Tighten close approval accounting" } });
    await user.click(within(form).getByRole("button", { name: /Save policy/i }));

    expect(apiMocks.upsertOperationsApprovalPolicyRule).toHaveBeenCalledWith(expect.objectContaining({
      policyKey: "ready-for-close",
      reviewerRole: "Controller",
      requiredDistinctApprovals: 3,
      evidenceRequirement: "Controller packet and checklist control evidence.",
      requestedBy: "Ops Lead",
      rationale: "Tighten close approval accounting"
    }));
    expect(onRefresh).toHaveBeenCalledOnce();
    expect(await within(form).findByText("Approval policy saved for Approve close.")).toBeInTheDocument();
    expect(within(form).getByText("Audit approval-policy-audit-1")).toBeInTheDocument();
  });

  it("configures account close calendar ownership with audit rationale", async () => {
    const user = userEvent.setup();
    apiMocks.upsertOperationsCloseCalendarItem.mockResolvedValue({
      item: {
        ...closeCalendar.items[0],
        nextDueDate: "2026-06-03",
        nextDueOwner: "Controller"
      },
      calendar: closeCalendar,
      auditEvent: {
        auditId: "close-calendar-audit-1",
        eventType: "operations-close-calendar-item-upserted",
        occurredAtUtc: "2026-05-28T00:00:00Z",
        actor: "Ops Lead",
        rationale: "Move close task to controller queue",
        correlationId: "settings-close-calendar-1",
        workflowId: "workflow-1",
        fundAccountId: "fund-1",
        periodId: "2026-05",
        taskId: "task-1",
        dueDate: "2026-06-03",
        owner: "Controller"
      }
    });
    const onRefresh = vi.fn();

    renderWithRouter(
      <SettingsScreen
        session={{ ...session, role: "Accounting", displayName: "Ops Lead" }}
        overview={overview}
        operationsCloseCalendar={closeCalendar}
        onRefresh={onRefresh}
      />
    );

    const form = screen.getByRole("form", { name: "Configure account close calendar" });
    await user.clear(within(form).getByLabelText("Close calendar due date"));
    await user.type(within(form).getByLabelText("Close calendar due date"), "2026-06-03");
    await user.clear(within(form).getByLabelText("Close calendar owner"));
    await user.type(within(form).getByLabelText("Close calendar owner"), "Controller");
    await user.clear(within(form).getByLabelText("Close calendar rationale"));
    await user.type(within(form).getByLabelText("Close calendar rationale"), "Move close task to controller queue");
    await user.click(within(form).getByRole("button", { name: /Save calendar/i }));

    expect(apiMocks.upsertOperationsCloseCalendarItem).toHaveBeenCalledWith(expect.objectContaining({
      workflowId: "workflow-1",
      taskId: "task-1",
      dueDate: "2026-06-03",
      owner: "Controller",
      requestedBy: "Ops Lead",
      rationale: "Move close task to controller queue"
    }));
    expect(onRefresh).toHaveBeenCalledOnce();
    expect(await within(form).findByText("Close calendar saved for 2026-05.")).toBeInTheDocument();
    expect(within(form).getByText("Audit close-calendar-audit-1")).toBeInTheDocument();
  });

  it("renders provider connection center with continuity repair links", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
        providerConnections={providerConnections}
        providerRoutingConnections={providerRoutingConnections}
        providerRoutingBindings={providerRoutingBindings}
        providerRoutingTrustSnapshots={providerRoutingTrustSnapshots}
        onProviderRoutingRefresh={vi.fn()}
      />
    );

    const center = screen.getByText("Provider Connection Center").closest("div");
    expect(screen.getByText("Brokerage capable")).toBeInTheDocument();
    expect(screen.getByText("Data providers")).toBeInTheDocument();
    expect(screen.getByText("Alpaca")).toBeInTheDocument();
    expect(screen.getByText("Polygon.io")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh Provider Connection Center routing data" })).toBeInTheDocument();
    expect(screen.getByText("Reference data")).toBeInTheDocument();
    expect(screen.getByText("97% · Healthy")).toBeInTheDocument();
    expect(screen.getByText("Production ready")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Alpaca provider connection row" })).toHaveAttribute(
      "href",
      "/settings#alpaca-provider-setup"
    );
    expect(screen.getByRole("link", { name: "Open Polygon.io provider connection row" })).toHaveAttribute(
      "href",
      "/settings#provider-polygon-connection"
    );
    expect(center).not.toHaveTextContent("endpoint-secret");
    expect(center).not.toHaveTextContent("vault:polygon/default");
  });

  it("loads provider integration runtime evidence for a provider routing connection", async () => {
    const user = userEvent.setup();
    const syncRuns: ProviderIntegrationSyncRunHistory = {
      connectionId: "provider-reference",
      totalSyncRuns: 4,
      returnedSyncRuns: 2,
      latestStartedAt: "2026-06-16T12:30:00Z",
      syncRuns: [
        {
          syncRunId: "sync-positions-2",
          capability: "Positions",
          endpointKey: "positions",
          startedAt: "2026-06-16T12:30:00Z",
          completedAt: "2026-06-16T12:31:00Z",
          status: "Quarantined",
          recordsReceived: 17,
          recordsAccepted: 14,
          recordsQuarantined: 3,
          durableStagingRecordCount: 14,
          durableQuarantinedRecordCount: 3,
          criticalIssueCount: 1,
          warningIssueCount: 1,
          rawPayloadId: "raw-polygon-2",
          issues: []
        },
        {
          syncRunId: "sync-positions-1",
          capability: "Positions",
          endpointKey: "positions",
          startedAt: "2026-06-16T11:00:00Z",
          completedAt: "2026-06-16T11:01:00Z",
          status: "Loaded",
          recordsReceived: 12,
          recordsAccepted: 12,
          recordsQuarantined: 0,
          durableStagingRecordCount: 12,
          durableQuarantinedRecordCount: 0,
          criticalIssueCount: 0,
          warningIssueCount: 0,
          rawPayloadId: "raw-polygon-1",
          issues: []
        }
      ]
    };
    const monitor: ProviderIntegrationConnectionMonitor = {
      connectionId: "provider-reference",
      manifestId: "manifest-polygon",
      providerId: "polygon",
      displayName: "Polygon.io",
      connectionName: "Reference data route",
      environment: "paper",
      state: "Active",
      enabledCapabilities: ["Positions"],
      lastSyncRun: syncRuns.syncRuns[0],
      recentSyncRuns: syncRuns.syncRuns,
      recentRecordsReceived: 17,
      recentRecordsAccepted: 14,
      recentRecordsQuarantined: 3,
      durableStagingRecordCount: 14,
      durableQuarantinedRecordCount: 3,
      hasCriticalIssues: true
    };
    const quarantine: ProviderIntegrationQuarantineReview = {
      connectionId: "provider-reference",
      syncRunIds: ["sync-positions-2"],
      records: [
        {
          quarantineRecordId: "quarantine-position-1",
          syncRunId: "sync-positions-2",
          connectionId: "provider-reference",
          capability: "Positions",
          rawRecord: { accountNumber: "acct-1", cusip: null },
          mappedRecord: { providerAccountId: "acct-1" },
          validationErrors: [
            {
              code: "SCHEMA_REQUIRED",
              severity: "Critical",
              message: "CUSIP is required before staging promotion.",
              targetField: "cusip",
              suggestedFix: "Add provider mapping."
            }
          ],
          status: "Quarantined",
          createdAt: "2026-06-16T12:31:00Z"
        },
        {
          quarantineRecordId: "quarantine-position-2",
          syncRunId: "sync-positions-2",
          connectionId: "provider-reference",
          capability: "Positions",
          rawRecord: { accountNumber: "acct-1", cusip: null },
          mappedRecord: { providerAccountId: "acct-1" },
          validationErrors: [
            {
              code: "SCHEMA_REQUIRED",
              severity: "Critical",
              message: "CUSIP is required before staging promotion.",
              targetField: "cusip",
              suggestedFix: "Add provider mapping."
            }
          ],
          status: "Quarantined",
          createdAt: "2026-06-16T12:31:05Z"
        },
        {
          quarantineRecordId: "quarantine-position-3",
          syncRunId: "sync-positions-2",
          connectionId: "provider-reference",
          capability: "Positions",
          rawRecord: { accountNumber: "acct-1", cusip: null },
          mappedRecord: { providerAccountId: "acct-1" },
          validationErrors: [
            {
              code: "SCHEMA_REQUIRED",
              severity: "Critical",
              message: "CUSIP is required before staging promotion.",
              targetField: "cusip",
              suggestedFix: "Add provider mapping."
            }
          ],
          status: "Quarantined",
          createdAt: "2026-06-16T12:31:10Z"
        }
      ],
      decisions: [],
      totalQuarantinedRecords: 3,
      criticalIssueCount: 1,
      warningIssueCount: 1,
      pendingReviewRecordCount: 3,
      decisionedRecordCount: 0,
      replayRequestedRecordCount: 0,
      ignoredRecordCount: 0,
      cashPositionCandidateCount: 0,
      issueGroups: [
        {
          issueCode: "SCHEMA_REQUIRED",
          severity: "Critical",
          targetField: "cusip",
          message: "CUSIP is required before staging promotion.",
          suggestedFix: "Add provider mapping.",
          recordCount: 3
        }
      ]
    };
    const reviewOnlyDecision = {
      decisionId: "quarantine-decision-review-1",
      syncRunId: "sync-positions-2",
      quarantineRecordId: "quarantine-position-1",
      connectionId: "provider-reference",
      action: "ReviewOnly" as const,
      reviewedBy: "Andrew Rowden",
      reviewedAt: "2026-06-16T12:42:00Z",
      note: "Reviewed from the Settings Provider Connection Center runtime evidence panel."
    };
    const replayDecision = {
      ...reviewOnlyDecision,
      decisionId: "quarantine-decision-replay-1",
      quarantineRecordId: "quarantine-position-2",
      action: "ReplayAfterMappingChange" as const,
      reviewedAt: "2026-06-16T12:43:00Z",
      note: "Marked from the Settings Provider Connection Center for replay after mapping changes."
    };
    const ignoreDecision = {
      decisionId: "quarantine-decision-ignore-1",
      syncRunId: "sync-positions-2",
      quarantineRecordId: "quarantine-position-3",
      connectionId: "provider-reference",
      action: "IgnoreProviderRecord" as const,
      reviewedBy: "Andrew Rowden",
      reviewedAt: "2026-06-16T12:44:00Z",
      note: "Ignored from the Settings Provider Connection Center after operator review."
    };
    const reviewedQuarantine = {
      ...quarantine,
      decisions: [reviewOnlyDecision],
      decisionedRecordCount: 1,
      pendingReviewRecordCount: 2
    };
    const replayQuarantine = {
      ...quarantine,
      decisions: [reviewOnlyDecision, replayDecision],
      decisionedRecordCount: 2,
      pendingReviewRecordCount: 1,
      replayRequestedRecordCount: 1
    };
    const ignoredQuarantine = {
      ...quarantine,
      decisions: [reviewOnlyDecision, replayDecision, ignoreDecision],
      decisionedRecordCount: 3,
      pendingReviewRecordCount: 0,
      replayRequestedRecordCount: 1,
      ignoredRecordCount: 1
    };
    const syncPlan: ProviderIntegrationSyncPlan = {
      connectionId: "provider-reference",
      manifestId: "manifest-polygon",
      providerId: "polygon",
      connectionName: "Reference data route",
      connectionState: "Active",
      evaluatedAt: "2026-06-16T12:35:00Z",
      dueCount: 1,
      blockedCount: 0,
      items: [
        {
          capability: "Positions",
          endpointKey: "positions",
          scheduleMode: "incremental",
          frequency: "daily",
          timezone: "America/New_York",
          lastSuccessfulSyncAt: "2026-06-16T12:30:00Z",
          nextEligibleSyncAt: "2026-06-17T12:30:00Z",
          isDue: true,
          isBlocked: false,
          reason: "Daily provider position sync is due.",
          issues: []
        }
      ]
    };
    const staging: ProviderIntegrationStagingReview = {
      connectionId: "provider-reference",
      syncRunIds: ["sync-positions-2"],
      records: [
        {
          stagingRecordId: "stage-position-1",
          syncRunId: "sync-positions-2",
          connectionId: "provider-reference",
          capability: "Positions",
          rawPayloadId: "raw-polygon-2",
          sourceRecordId: "provider-position-1",
          dedupeKey: "Positions:provider-position-1",
          mappedRecord: { providerAccountId: "acct-1", quantity: 10 },
          validationWarnings: [],
          status: "Validated",
          createdAt: "2026-06-16T12:31:00Z"
        }
      ],
      capabilitySummaries: [{ capability: "Positions", recordCount: 1, warningCount: 0 }],
      warningGroups: [],
      totalStagedRecords: 1,
      readyForReconciliationCount: 1,
      warningRecordCount: 0
    };
    const identity: ProviderIntegrationStagingIdentityResolutionPreview = {
      connectionId: "provider-reference",
      syncRunIds: ["sync-positions-2"],
      rows: [],
      totalRows: 1,
      accountReviewRequiredCount: 0,
      missingAccountIdentifierCount: 0,
      securityResolvedCount: 1,
      securityReviewRequiredCount: 1,
      missingSecurityIdentifierCount: 0
    };
    const promotion: ProviderIntegrationPromotionReadinessPreview = {
      connectionId: "provider-reference",
      syncRunIds: ["sync-positions-2"],
      totalRows: 1,
      readyForReconciliationCount: 1,
      reviewRequiredCount: 0,
      blockedCount: 0,
      rows: [
        {
          stagingRecordId: "stage-position-1",
          syncRunId: "sync-positions-2",
          capability: "Positions",
          promotionTarget: "reconciliation-staging",
          status: "ReadyForReconciliation",
          providerAccountId: "acct-1",
          internalAccountId: "internal-account-1",
          internalSecurityId: "security-1",
          securityDisplayName: "US Treasury 2031",
          securityRoute: "/data/security-master/security-1",
          issues: []
        }
      ]
    };
    const handoff: ProviderIntegrationReconciliationHandoffHistory = {
      connectionId: "provider-reference",
      totalRecords: 1,
      handoffCount: 1,
      lastRequestedAt: "2026-06-16T12:40:00Z",
      records: [
        {
          handoffId: "handoff-1",
          connectionId: "provider-reference",
          syncRunId: "sync-positions-2",
          stagingRecordId: "stage-position-1",
          capability: "Positions",
          promotionTarget: "reconciliation-staging",
          requestedBy: "operations",
          requestedAt: "2026-06-16T12:40:00Z",
          approvalEvidenceId: "approval-1",
          note: "Approved after identity review.",
          providerAccountId: "acct-1",
          internalAccountId: "internal-account-1",
          internalSecurityId: "security-1",
          securityRoute: "/data/security-master/security-1",
          issues: []
        }
      ]
    };
    const emptyHandoff: ProviderIntegrationReconciliationHandoffHistory = {
      connectionId: "provider-reference",
      totalRecords: 0,
      handoffCount: 0,
      lastRequestedAt: null,
      records: []
    };
    apiMocks.getProviderIntegrationConnectionMonitor.mockResolvedValue(monitor);
    apiMocks.getProviderIntegrationConnectionSyncPlan.mockResolvedValue(syncPlan);
    apiMocks.getProviderIntegrationConnectionSyncRuns.mockResolvedValue(syncRuns);
    apiMocks.getProviderIntegrationIdentityResolution.mockResolvedValue(identity);
    apiMocks.getProviderIntegrationPromotionReadiness.mockResolvedValue(promotion);
    apiMocks.getProviderIntegrationQuarantineReview
      .mockResolvedValue(quarantine)
      .mockResolvedValueOnce(quarantine)
      .mockResolvedValueOnce(quarantine)
      .mockResolvedValueOnce(reviewedQuarantine)
      .mockResolvedValueOnce(reviewedQuarantine)
      .mockResolvedValueOnce(replayQuarantine)
      .mockResolvedValueOnce(ignoredQuarantine);
    apiMocks.getProviderIntegrationReconciliationHandoffHistory
      .mockResolvedValue(handoff)
      .mockResolvedValueOnce(emptyHandoff);
    apiMocks.getProviderIntegrationStagingReview.mockResolvedValue(staging);
    apiMocks.importProviderIntegrationOpenApi.mockResolvedValue({
      imported: true,
      manifest: {
        manifestId: "draft-polygon-openapi-v1",
        integrationType: "OpenApiRest",
        endpoints: [{ endpointKey: "positions" }, { endpointKey: "accounts" }],
        state: "Draft"
      },
      readiness: {
        isReady: false,
        requiredEvidence: ["endpoint-test-required"],
        issues: []
      },
      issues: [],
      message: "OpenAPI import draft manifest saved."
    });
    apiMocks.runDueProviderIntegrationSync.mockResolvedValue({
      connectionId: "provider-reference",
      requestedAt: "2026-06-16T12:45:00Z",
      startedCount: 1,
      skippedCount: 0,
      items: [
        {
          capability: "Positions",
          endpointKey: "positions",
          started: true,
          skipped: false,
          reason: "due",
          syncRunId: "run-due-positions-1",
          dryRunResult: null,
          issues: []
        }
      ]
    });
    apiMocks.createProviderIntegrationReconciliationHandoff.mockResolvedValue({
      accepted: true,
      handoffId: "handoff-settings-1",
      connectionId: "provider-reference",
      promotionTarget: "reconciliation-staging",
      records: handoff.records,
      acceptedRecordCount: 1,
      rejectedRecordCount: 0,
      duplicateRecordCount: 0,
      issues: [],
      message: "Reconciliation handoff created."
    });
    apiMocks.replayProviderIntegrationQuarantineRecords.mockResolvedValue({
      replaySyncRunId: "provider-replay-provider-reference-20260616",
      rawPayloadId: "raw-replay-1",
      capability: "Positions",
      recordsReplayed: 2,
      recordsAccepted: 2,
      recordsRequarantined: 0,
      status: "Validated",
      issues: []
    });
    apiMocks.resolveProviderIntegrationQuarantineRecord.mockResolvedValue({
      resolved: true,
      record: quarantine.records[0],
      decision: {
        decisionId: "quarantine-decision-1",
        syncRunId: "sync-positions-2",
        quarantineRecordId: "quarantine-position-1",
        connectionId: "provider-reference",
        action: "ReviewOnly",
        reviewedBy: "Andrew Rowden",
        reviewedAt: "2026-06-16T12:42:00Z",
        note: "Reviewed from the Settings Provider Connection Center runtime evidence panel."
      },
      message: "Provider integration quarantine review decision recorded."
    });

    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
        providerConnections={providerConnections}
        providerRoutingConnections={providerRoutingConnections}
        providerRoutingBindings={providerRoutingBindings}
        providerRoutingTrustSnapshots={providerRoutingTrustSnapshots}
      />
    );

    const panel = screen.getByRole("region", { name: "Polygon.io provider integration runtime evidence" });
    expect(within(panel).getByText("No runtime evidence loaded.")).toBeInTheDocument();
    await user.click(within(panel).getByRole("button", { name: "Load provider integration runtime evidence for Polygon.io" }));

    await waitFor(() => {
      expect(apiMocks.getProviderIntegrationConnectionMonitor).toHaveBeenCalledWith("provider-reference", 5);
      expect(apiMocks.getProviderIntegrationConnectionSyncPlan).toHaveBeenCalledWith("provider-reference", expect.any(String));
      expect(apiMocks.getProviderIntegrationConnectionSyncRuns).toHaveBeenCalledWith("provider-reference", 5);
      expect(apiMocks.getProviderIntegrationStagingReview).toHaveBeenCalledWith("provider-reference", 5);
      expect(apiMocks.getProviderIntegrationIdentityResolution).toHaveBeenCalledWith("provider-reference", 5);
      expect(apiMocks.getProviderIntegrationPromotionReadiness).toHaveBeenCalledWith("provider-reference", 5);
      expect(apiMocks.getProviderIntegrationReconciliationHandoffHistory).toHaveBeenCalledWith("provider-reference");
      expect(apiMocks.getProviderIntegrationQuarantineReview).toHaveBeenCalledWith("provider-reference", 5);
    });
    expect(await within(panel).findByText("Provider integration runtime evidence loaded.")).toBeInTheDocument();
    expect(within(panel).getByText("provider-reference")).toBeInTheDocument();
    expect(within(panel).getByText("Jun 16, 12:30 UTC")).toBeInTheDocument();
    expect(within(panel).getByText("2 / 4")).toBeInTheDocument();
    expect(within(panel).getByText("14 / 3")).toBeInTheDocument();
    expect(within(panel).getByText("1 critical / 1 warning")).toBeInTheDocument();
    expect(within(panel).getByText("3 pending / 0 replay / 0 ignored / 0 cash")).toBeInTheDocument();
    expect(within(panel).getByText("1 due / 0 blocked")).toBeInTheDocument();
    expect(within(panel).getByText("1 rows")).toBeInTheDocument();
    expect(within(panel).getByText("1 review required")).toBeInTheDocument();
    expect(within(panel).getByText("1 ready / 0 blocked")).toBeInTheDocument();
    expect(within(panel).getByText("0 handoffs")).toBeInTheDocument();
    expect(within(panel).getByText("sync-positions-2")).toBeInTheDocument();
    expect(within(panel).getByText("quarantine-position-1")).toBeInTheDocument();
    expect(within(panel).getByText("quarantine-position-3")).toBeInTheDocument();
    expect(within(panel).getByText("SCHEMA_REQUIRED")).toBeInTheDocument();
    expect(within(panel).getAllByText("CUSIP is required before staging promotion.").length).toBeGreaterThanOrEqual(1);
    expect(within(panel).getByText("Daily provider position sync is due.")).toBeInTheDocument();
    expect(within(panel).getByText("stage-position-1")).toBeInTheDocument();
    expect(within(panel).getByText(/US Treasury 2031/)).toBeInTheDocument();

    const openApiForm = within(panel).getByRole("form", { name: "Polygon.io OpenAPI import draft" });
    await user.click(within(openApiForm).getByRole("button", { name: "Import OpenAPI draft manifest for Polygon.io" }));

    await waitFor(() => {
      expect(apiMocks.importProviderIntegrationOpenApi).toHaveBeenCalledWith(expect.objectContaining({
        manifestId: "draft-polygon-openapi-v1",
        providerId: "polygon",
        displayName: "Polygon.io OpenAPI",
        environment: "paper",
        authType: "OAuth2",
        capabilities: ["Positions"],
        importedBy: "Andrew Rowden",
        importedAt: expect.any(String),
        changeReason: "Imported from the Settings Provider Connection Center."
      }));
    });
    expect(await within(openApiForm).findByText("OpenAPI import draft manifest saved.")).toBeInTheDocument();
    expect(within(openApiForm).getByText("2 endpoints seeded.")).toBeInTheDocument();

    await user.click(within(panel).getByRole("button", {
      name: "Create reconciliation handoff for 1 provider integration staging rows for Polygon.io"
    }));

    await waitFor(() => {
      expect(apiMocks.createProviderIntegrationReconciliationHandoff).toHaveBeenCalledWith(expect.objectContaining({
        connectionId: "provider-reference",
        stagingRecordIds: ["stage-position-1"],
        requestedBy: "Andrew Rowden",
        requestedAt: expect.any(String),
        approvalEvidenceId: expect.stringMatching(/^settings-provider-handoff-provider-reference-/),
        note: "Approved from the Settings Provider Connection Center promotion readiness panel.",
        recentRunLimit: 5
      }));
    });

    await user.click(within(panel).getByRole("button", {
      name: "Review quarantine record quarantine-position-1 for Polygon.io"
    }));

    await waitFor(() => {
      expect(apiMocks.resolveProviderIntegrationQuarantineRecord).toHaveBeenCalledWith(expect.objectContaining({
        connectionId: "provider-reference",
        syncRunId: "sync-positions-2",
        quarantineRecordId: "quarantine-position-1",
        action: "ReviewOnly",
        reviewedBy: "Andrew Rowden",
        reviewedAt: expect.any(String),
        note: "Reviewed from the Settings Provider Connection Center runtime evidence panel."
      }));
    });
    expect(await within(panel).findByText("Decision: Review by Andrew Rowden · Jun 16, 12:42 UTC")).toBeInTheDocument();
    expect(within(panel).getByText("Decision recorded")).toBeInTheDocument();
    expect(within(panel).queryByRole("button", {
      name: "Review quarantine record quarantine-position-1 for Polygon.io"
    })).not.toBeInTheDocument();
    expect(within(panel).queryByRole("button", {
      name: "Mark quarantine record quarantine-position-1 for replay after mapping change for Polygon.io"
    })).not.toBeInTheDocument();
    expect(within(panel).queryByRole("button", {
      name: "Ignore quarantine record quarantine-position-1 for Polygon.io"
    })).not.toBeInTheDocument();
    expect(within(panel).queryByRole("button", {
      name: "Mark quarantine record quarantine-position-1 as cash position for Polygon.io"
    })).not.toBeInTheDocument();

    await user.click(within(panel).getByRole("button", {
      name: "Replay 2 quarantined provider integration records for Polygon.io"
    }));

    await waitFor(() => {
      expect(apiMocks.replayProviderIntegrationQuarantineRecords).toHaveBeenCalledWith(expect.objectContaining({
        sourceSyncRunId: "sync-positions-2",
        manifestId: "manifest-polygon",
        connectionId: "provider-reference",
        capability: "Positions",
        quarantineRecordIds: ["quarantine-position-2", "quarantine-position-3"],
        requestedBy: "Andrew Rowden",
        requestedAt: expect.any(String),
        replaySyncRunId: expect.stringMatching(/^provider-replay-provider-reference-/)
      }));
    });

    await user.click(within(panel).getByRole("button", {
      name: "Mark quarantine record quarantine-position-2 for replay after mapping change for Polygon.io"
    }));

    await waitFor(() => {
      expect(apiMocks.resolveProviderIntegrationQuarantineRecord).toHaveBeenCalledWith(expect.objectContaining({
        connectionId: "provider-reference",
        syncRunId: "sync-positions-2",
        quarantineRecordId: "quarantine-position-2",
        action: "ReplayAfterMappingChange",
        reviewedBy: "Andrew Rowden",
        reviewedAt: expect.any(String),
        note: "Marked from the Settings Provider Connection Center for replay after mapping changes."
      }));
    });
    expect(await within(panel).findByText("Decision: Replay after mapping change by Andrew Rowden · Jun 16, 12:43 UTC")).toBeInTheDocument();
    expect(within(panel).queryByRole("button", {
      name: "Mark quarantine record quarantine-position-2 as cash position for Polygon.io"
    })).not.toBeInTheDocument();

    await user.click(within(panel).getByRole("button", {
      name: "Ignore quarantine record quarantine-position-3 for Polygon.io"
    }));

    await waitFor(() => {
      expect(apiMocks.resolveProviderIntegrationQuarantineRecord).toHaveBeenCalledWith(expect.objectContaining({
        connectionId: "provider-reference",
        syncRunId: "sync-positions-2",
        quarantineRecordId: "quarantine-position-3",
        action: "IgnoreProviderRecord",
        reviewedBy: "Andrew Rowden",
        reviewedAt: expect.any(String),
        note: "Ignored from the Settings Provider Connection Center after operator review."
      }));
    });
    expect(await within(panel).findByText("Decision: Ignore provider record by Andrew Rowden · Jun 16, 12:44 UTC")).toBeInTheDocument();

    await user.click(within(panel).getByRole("button", { name: "Run due provider integration sync for Polygon.io" }));

    await waitFor(() => {
      expect(apiMocks.runDueProviderIntegrationSync).toHaveBeenCalledWith("provider-reference", expect.objectContaining({
        connectionId: "provider-reference",
        requestedBy: "Andrew Rowden",
        requestedAt: expect.any(String),
        maxPages: 2,
        pathParametersByCapability: {},
        queryParametersByCapability: {}
      }));
    });
  });

  it("runs the guided provider integration workbench over shared setup and dry-run endpoints", async () => {
    const user = userEvent.setup();
    const manifest = {
      manifestId: "template-polygon-data-v1",
      manifestVersion: 1,
      providerId: "polygon",
      displayName: "Polygon positions REST",
      integrationType: "OpenApiRest" as const,
      environment: "paper",
      auth: { type: "ApiKey" as const, tokenUrl: null, scopes: [], metadata: {} },
      capabilities: [
        {
          capability: "Positions" as const,
          enabled: true,
          requiresCertifiedAdapter: false,
          requiredCanonicalFields: ["accountId", "symbol", "quantity"]
        }
      ],
      endpoints: [
        {
          endpointKey: "positions",
          capability: "Positions" as const,
          method: "Get" as const,
          path: "/v3/reference/positions",
          headers: {},
          query: { limit: "100" },
          requestBodyTemplate: null,
          dependsOn: null,
          pagination: { type: "None" as const, cursorPath: null, cursorParam: null, nextUrlPath: null, pageSize: null },
          response: { recordsPath: "$.results", schemaFingerprint: "positions-v1", requiredPaths: ["accountId", "symbol", "quantity"] }
        }
      ],
      fieldMappings: [
        { capability: "Positions" as const, sourcePath: "$.accountId", targetField: "providerAccountId", transform: null, required: true, confidence: "High" as const, defaultValue: null, constantValue: null },
        { capability: "Positions" as const, sourcePath: "$.symbol", targetField: "symbol", transform: null, required: true, confidence: "High" as const, defaultValue: null, constantValue: null }
      ],
      sync: { mode: "incremental", frequency: "daily", time: null, timezone: "America/New_York", cursorType: "Timestamp" as const, cursorField: "updatedAt", fullRefreshFrequency: null },
      validationRules: [],
      activation: {
        requiresAuthenticationTest: true,
        requiresEndpointTest: true,
        requiresDryRun: true,
        requiresApproval: true,
        productionWriteCapabilitiesAllowed: false,
        requiredIssueCodes: []
      },
      state: "Draft" as const,
      createdBy: "operations",
      createdAt: "2026-06-16T12:00:00Z",
      approvedBy: null,
      approvedAt: null,
      changeReason: "Seed Polygon provider integration."
    };
    const reviewReadiness = {
      isReady: false,
      requiredEvidence: ["dry-run-result"],
      issues: [
        { code: "DRY_RUN_REQUIRED", severity: "Warning" as const, message: "Run dry-run before activation.", capability: "Positions" as const, suggestedFix: "Run a read-only dry-run." }
      ]
    };
    const readyReadiness = { isReady: true, requiredEvidence: ["dry-run-result"], issues: [] };
    apiMocks.getProviderIntegrationTemplates.mockResolvedValue([
      {
        manifestId: manifest.manifestId,
        providerId: "polygon",
        displayName: "Polygon positions REST",
        integrationType: "OpenApiRest",
        capabilities: ["Positions"],
        summary: "Read-only position import for reconciliation staging.",
        requiresCredentials: true
      }
    ]);
    apiMocks.getProviderIntegrationTemplate.mockResolvedValue(manifest);
    apiMocks.saveProviderIntegrationSetup.mockResolvedValue({
      saved: true,
      manifestId: manifest.manifestId,
      connectionId: "provider-reference",
      manifestState: "Draft",
      connectionState: "Draft",
      readiness: reviewReadiness,
      approvalEvidenceId: null,
      message: "Provider integration setup draft saved."
    });
    apiMocks.getProviderIntegrationReadiness.mockResolvedValue(readyReadiness);
    apiMocks.runManualCsvProviderIntegrationDryRun.mockResolvedValue({
      syncRunId: "settings-csv-provider-reference-20260616",
      rawPayloadId: "raw-csv-1",
      capability: "Positions",
      recordsReceived: 1,
      recordsAccepted: 1,
      recordsQuarantined: 0,
      status: "Validated",
      issues: []
    });
    apiMocks.runRestProviderIntegrationDryRun.mockResolvedValue({
      syncRunId: "settings-rest-provider-reference-20260616",
      rawPayloadId: "raw-rest-1",
      capability: "Positions",
      recordsReceived: 2,
      recordsAccepted: 2,
      recordsQuarantined: 0,
      status: "Validated",
      issues: []
    });
    apiMocks.checkProviderIntegrationSchemaDrift.mockResolvedValue({
      manifestId: manifest.manifestId,
      connectionId: "provider-reference",
      capability: "Positions",
      endpointKey: "positions",
      syncRunId: "settings-rest-provider-reference-20260616",
      rawPayloadId: "raw-rest-1",
      driftDetected: false,
      shouldPauseCapability: false,
      recordsInspected: 2,
      issues: []
    });
    apiMocks.activateProviderIntegration.mockResolvedValue({
      activated: true,
      manifestId: manifest.manifestId,
      connectionId: "provider-reference",
      manifestState: "Active",
      connectionState: "Active",
      readiness: readyReadiness,
      message: "Provider integration activated."
    });

    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        providerConnections={providerConnections}
        providerRoutingConnections={providerRoutingConnections}
        providerRoutingBindings={providerRoutingBindings}
        providerRoutingTrustSnapshots={providerRoutingTrustSnapshots}
      />
    );

    const workbench = screen.getByRole("region", { name: "Polygon.io guided provider integration workbench" });
    await user.click(within(workbench).getByRole("button", { name: "Load provider integration templates for Polygon.io" }));
    expect(await within(workbench).findByText("1 provider integration templates loaded.")).toBeInTheDocument();
    await user.click(within(workbench).getByRole("button", { name: "Use selected provider integration template for Polygon.io" }));
    expect(await within(workbench).findByText("Template template-polygon-data-v1 loaded into draft setup editor.")).toBeInTheDocument();
    expect(within(workbench).getAllByText(/\$\.accountId/).length).toBeGreaterThan(0);

    await user.click(within(workbench).getByRole("button", { name: "Save provider integration setup draft for Polygon.io" }));
    await waitFor(() => {
      expect(apiMocks.saveProviderIntegrationSetup).toHaveBeenCalledWith(expect.objectContaining({
        manifest: expect.objectContaining({ manifestId: manifest.manifestId }),
        connection: expect.objectContaining({ connectionId: "provider-reference", credentialSecretRef: expect.stringContaining("provider-credential:polygon") }),
        savedBy: "Andrew Rowden",
        savedAt: expect.any(String)
      }));
    });
    expect(await within(workbench).findByText("Provider integration setup draft saved.")).toBeInTheDocument();

    await user.click(within(workbench).getByRole("button", { name: "Check provider integration activation readiness for Polygon.io" }));
    await waitFor(() => {
      expect(apiMocks.getProviderIntegrationReadiness).toHaveBeenCalledWith(manifest.manifestId, "provider-reference");
    });
    expect(await within(workbench).findByText("Activation readiness passed.")).toBeInTheDocument();

    await user.click(within(workbench).getByRole("button", { name: "Run provider integration CSV dry-run for Polygon.io" }));
    await waitFor(() => {
      expect(apiMocks.runManualCsvProviderIntegrationDryRun).toHaveBeenCalledWith(expect.objectContaining({
        manifestId: manifest.manifestId,
        connectionId: "provider-reference",
        capability: "Positions",
        requestedBy: "Andrew Rowden"
      }));
    });
    expect(await within(workbench).findByText("CSV dry-run completed: 1 accepted / 0 quarantined.")).toBeInTheDocument();

    await user.click(within(workbench).getByRole("button", { name: "Run provider integration REST dry-run for Polygon.io" }));
    await waitFor(() => {
      expect(apiMocks.runRestProviderIntegrationDryRun).toHaveBeenCalledWith(expect.objectContaining({
        manifestId: manifest.manifestId,
        connectionId: "provider-reference",
        endpointKey: "positions",
        maxPages: 2
      }));
    });
    expect(await within(workbench).findByText("REST dry-run completed: 2 accepted / 0 quarantined.")).toBeInTheDocument();

    await user.click(within(workbench).getByRole("button", { name: "Check provider integration schema drift for Polygon.io" }));
    await waitFor(() => {
      expect(apiMocks.checkProviderIntegrationSchemaDrift).toHaveBeenCalledWith(expect.objectContaining({
        manifestId: manifest.manifestId,
        connectionId: "provider-reference",
        endpointKey: "positions",
        rawPayloadId: "raw-rest-1",
        checkedBy: "Andrew Rowden"
      }));
    });
    expect(await within(workbench).findByText("Schema drift check passed.")).toBeInTheDocument();

    await user.click(within(workbench).getByRole("button", { name: "Activate provider integration setup for Polygon.io" }));
    await waitFor(() => {
      expect(apiMocks.activateProviderIntegration).toHaveBeenCalledWith(expect.objectContaining({
        manifestId: manifest.manifestId,
        connectionId: "provider-reference",
        approvedBy: "Andrew Rowden",
        approvalEvidenceId: expect.stringMatching(/^settings-provider-activation-provider-reference-/)
      }));
    });
    expect(await within(workbench).findByText("Provider integration activated.")).toBeInTheDocument();
  });
  it("supports inline provider edit, test, save, verify, and clear actions", async () => {
    const user = userEvent.setup();
    const onRefresh = vi.fn();
    const onProviderRoutingRefresh = vi.fn();
    apiMocks.testProviderConnection.mockResolvedValue({ success: true, latency: "82ms", message: "Provider reachable." });
    apiMocks.putProviderCredentials.mockResolvedValue({ credentialState: "Configured", warnings: [] });
    apiMocks.verifyProviderConnection.mockResolvedValue({ success: true, lastError: null, warnings: [] });
    apiMocks.deleteProviderCredentials.mockResolvedValue({ verificationState: "NotVerified", warnings: [] });

    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        providerConnections={providerConnections}
        onRefresh={onRefresh}
        onProviderRoutingRefresh={onProviderRoutingRefresh}
      />
    );

    await user.click(screen.getByRole("button", { name: "Edit Alpaca credentials" }));
    await user.type(screen.getByLabelText("Alpaca Key ID"), "alpaca-key");
    await user.type(screen.getByLabelText("Alpaca Secret key"), "alpaca-secret");
    await user.click(screen.getByRole("button", { name: "Test Alpaca connection" }));
    await user.click(screen.getByRole("button", { name: "Save Alpaca credentials" }));
    await user.click(screen.getByRole("button", { name: "Re-verify Alpaca connection" }));
    await user.click(screen.getByRole("button", { name: "Clear Alpaca credentials" }));

    expect(apiMocks.testProviderConnection).toHaveBeenCalledWith("alpaca");
    expect(apiMocks.putProviderCredentials).toHaveBeenCalledWith(
      "alpaca",
      expect.objectContaining({
        credentials: expect.objectContaining({
          KeyId: "alpaca-key",
          SecretKey: "alpaca-secret"
        })
      })
    );
    expect(apiMocks.verifyProviderConnection).toHaveBeenCalledWith("alpaca");
    expect(apiMocks.deleteProviderCredentials).toHaveBeenCalledWith("alpaca");
    expect(onRefresh).toHaveBeenCalled();
    expect(onProviderRoutingRefresh).toHaveBeenCalled();
    expect(screen.getAllByText(/Impact summary:/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Readiness checks/).length).toBeGreaterThan(0);
  });

  it("saves QuickBooks Online OAuth company fields through inline setup", async () => {
    const user = userEvent.setup();
    apiMocks.putProviderCredentials.mockResolvedValue({ credentialState: "Configured", warnings: [] });
    const quickBooksConnections: ProviderConnectionRow[] = [
      ...providerConnections,
      {
        providerId: "quickbooks",
        displayName: "QuickBooks Online",
        capability: "AccountingSystem",
        credentialState: "Missing",
        credentialSource: "None",
        verificationState: "NotVerified",
        health: "Warning",
        fallbackActive: false,
        lastVerifiedAt: null,
        lastSuccessfulAt: null,
        lastFailureAt: null,
        lastError: null,
        maskedKeyPreview: null,
        environment: "sandbox",
        externalAccountId: null,
        affectedWorkflows: ["External GL reconciliation"],
        recommendedAction: "Add QuickBooks Online OAuth client ID, client secret, refresh token, and company realm ID before importing read-only GL evidence.",
        actionHref: "/settings#provider-quickbooks-connection",
        credentialFields: [
          {
            name: "ClientId",
            label: "Client ID",
            required: true,
            inputKind: "Password",
            placeholder: "QUICKBOOKS_CLIENT_ID",
            helpText: "Stored in Meridian's encrypted local provider store for OAuth token refresh."
          },
          {
            name: "ClientSecret",
            label: "Client secret",
            required: true,
            inputKind: "Password",
            placeholder: "QUICKBOOKS_CLIENT_SECRET",
            helpText: "Used only server-side for OAuth token refresh."
          },
          {
            name: "RefreshToken",
            label: "Refresh token",
            required: true,
            inputKind: "Password",
            placeholder: "QUICKBOOKS_REFRESH_TOKEN",
            helpText: "Token exchange refreshes read-only API access and stores rotated tokens locally."
          },
          {
            name: "RealmId",
            label: "Company realm ID",
            required: true,
            inputKind: "Text",
            placeholder: "QUICKBOOKS_REALM_ID",
            helpText: "Selects the QuickBooks Online company to read."
          },
          {
            name: "CompanyName",
            label: "Company name",
            required: false,
            inputKind: "Text",
            placeholder: "QUICKBOOKS_COMPANY_NAME",
            helpText: "Optional display label for the selected QuickBooks company."
          }
        ],
        environmentOptions: [
          { value: "sandbox", label: "Sandbox", isDefault: true },
          { value: "production", label: "Production", isDefault: false }
        ]
      }
    ];

    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        providerConnections={quickBooksConnections}
      />
    );

    await user.click(screen.getByRole("button", { name: "Edit QuickBooks Online credentials" }));
    fireEvent.change(screen.getByLabelText("QuickBooks Online Client ID"), { target: { value: "qbo-client-id" } });
    fireEvent.change(screen.getByLabelText("QuickBooks Online Client secret"), { target: { value: "qbo-client-secret" } });
    fireEvent.change(screen.getByLabelText("QuickBooks Online Refresh token"), { target: { value: "qbo-refresh-token" } });
    fireEvent.change(screen.getByLabelText("QuickBooks Online Company realm ID"), { target: { value: "9130359087654321" } });
    fireEvent.change(screen.getByLabelText("QuickBooks Online Company name"), { target: { value: "Meridian-Dev" } });
    await user.click(screen.getByRole("button", { name: "Save QuickBooks Online credentials" }));

    expect(apiMocks.putProviderCredentials).toHaveBeenCalledWith(
      "quickbooks",
      expect.objectContaining({
        credentials: expect.objectContaining({
          ClientId: "qbo-client-id",
          ClientSecret: "qbo-client-secret",
          RefreshToken: "qbo-refresh-token",
          RealmId: "9130359087654321",
          CompanyName: "Meridian-Dev"
        }),
        environment: "sandbox"
      })
    );
  });

  it("renders QuickBooks Fixture inline setup without fake credential fields", async () => {
    const user = userEvent.setup();
    const fixtureConnections: ProviderConnectionRow[] = [
      ...providerConnections,
      {
        providerId: "quickbooks-fixture",
        displayName: "QuickBooks Fixture",
        capability: "AccountingSystem",
        credentialState: "NotRequired",
        credentialSource: "NotRequired",
        verificationState: "NotRequired",
        health: "Healthy",
        fallbackActive: false,
        lastVerifiedAt: null,
        lastSuccessfulAt: null,
        lastFailureAt: null,
        lastError: null,
        maskedKeyPreview: null,
        environment: null,
        externalAccountId: null,
        affectedWorkflows: ["External GL reconciliation"],
        recommendedAction: "No credential action required; use the fixture to preview external GL reconciliation.",
        actionHref: "/settings#provider-quickbooks-fixture-connection",
        credentialFields: [],
        environmentOptions: []
      }
    ];

    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        providerConnections={fixtureConnections}
      />
    );

    await user.click(screen.getByRole("button", { name: "Edit QuickBooks Fixture credentials" }));

    expect(screen.getByText("No credential fields are required for this provider.")).toBeInTheDocument();
    expect(screen.queryByLabelText("QuickBooks Fixture Fixture mode")).not.toBeInTheDocument();
  });

  it("filters provider rows by search and risk filters", async () => {
    const user = userEvent.setup();

    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        providerConnections={providerConnections}
      />
    );

    fireEvent.change(screen.getByLabelText("Search providers in connection center"), { target: { value: "polygon" } });
    expect(screen.getByText("Polygon.io")).toBeInTheDocument();
    expect(screen.queryByText("Alpaca")).toBeNull();

    await user.selectOptions(screen.getByLabelText("Health"), "warning");
    expect(screen.getByText("Polygon.io")).toBeInTheDocument();
  });

  it("updates recent-event detail with keyboard row selection", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={{
          ...overview,
          recentEvents: [
            ...overview.recentEvents,
            {
              id: "evt-2",
              type: "error",
              message: "Storage heartbeat missed.",
              source: "Storage",
              timestamp: "2026-05-01T00:04:00Z"
            }
          ]
        }}
      />,
      {
        initialEntries: ["/settings#diagnostic-endpoints"]
      }
    );

    const storageRow = screen.getByRole("row", {
      name: /Select event evt-2\. CRIT event from Storage at May 1, 00:04 UTC\. Storage heartbeat missed\./i
    });

    storageRow.focus();
    await user.keyboard("{Enter}");

    const eventDetail = screen.getByRole("complementary", { name: "Selected recent event detail" });
    expect(storageRow).toHaveAttribute("aria-selected", "true");
    expect(within(eventDetail).getByRole("region", { name: "CRIT event detail for evt-2" })).toHaveTextContent(
      "Storage heartbeat missed."
    );
    expect(within(eventDetail).getByText("Critical")).toBeInTheDocument();
  });

  it("keeps the recent-events panel visible when there are no events", () => {
    renderWithRouter(<SettingsScreen session={session} overview={{ ...overview, recentEvents: [] }} />, {
      initialEntries: ["/settings#diagnostic-endpoints"]
    });

    expect(screen.getAllByText("No recent events")).toHaveLength(2);
    expect(screen.getByText("No system events reported for the active session. Diagnostic services remain available below.")).toBeInTheDocument();
  });

  it("renders an alert state when overview data is unavailable", () => {
    renderWithRouter(<SettingsScreen session={session} overview={null} />, {
      initialEntries: ["/settings#diagnostic-endpoints"]
    });

    expect(screen.getAllByText("Event stream unavailable")).toHaveLength(2);
    expect(screen.getByRole("alert")).toHaveTextContent("Reconnect to the Meridian API");
  });

  it("labels diagnostic service links", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        strategy={{ metrics: [], runs: [] }}
        trading={{} as never}
        portfolio={portfolio}
        data={{ metrics: [], providers: [], backfills: [], exports: [] }}
        accounting={{} as never}
        reporting={{} as never}
      />,
      { initialEntries: ["/settings#diagnostic-endpoints"] }
    );

    expect(screen.getByRole("link", { name: "Open System overview diagnostic service" })).toHaveAttribute(
      "href",
      "/api/status"
    );
    expect(screen.getByRole("link", { name: "Open Data workspace diagnostic service" })).toHaveAttribute(
      "href",
      "/api/workstation/data"
    );
    expect(screen.getByRole("link", { name: "Open Strategy workspace diagnostic service" })).toHaveAttribute(
      "href",
      "/api/workstation/strategy"
    );
    expect(screen.getByRole("list", { name: "Diagnostic service availability" })).toBeInTheDocument();
    expect(screen.getAllByText("All reachable").length).toBeGreaterThan(0);
  });

  it("renders the Alpaca paper connection panel", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    expect(screen.getByText("Alpaca paper API keys").closest("#alpaca-provider-setup")).toBeInTheDocument();
    expect(screen.getByRole("radiogroup", { name: "Alpaca trading environment" })).toBeInTheDocument();
    const paperEndpoint = screen.getByRole("radio", { name: "Use Alpaca paper endpoint for workstation validation" });
    const liveEndpoint = screen.getByRole("radio", { name: "Use Alpaca live endpoint for production brokerage verification" });
    expect(paperEndpoint).toBeChecked();
    expect(liveEndpoint).not.toBeChecked();
    expect(paperEndpoint).toHaveAccessibleDescription(/Paper endpoint for workstation validation.*Paper endpoint selected/s);
    expect(liveEndpoint).toHaveAccessibleDescription(/Live endpoint for production brokerage verification.*Paper endpoint selected/s);
    expect(screen.getByText("Enter Alpaca credentials")).toBeInTheDocument();
    expect(screen.getAllByText("Key ID").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Needed").length).toBeGreaterThan(0);
    expect(screen.getByText("********1234")).toBeInTheDocument();
    expect(screen.getByText("PA123")).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Alpaca provider setup checklist" })).toBeInTheDocument();
    expect(screen.getByText("Move from demo data to a verified paper connection before relying on readiness evidence.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Trading readiness after Alpaca account verification" })).toHaveAttribute(
      "href",
      "/trading/readiness"
    );
    expect(screen.getByRole("button", { name: /connect and test/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /clear/i })).toBeEnabled();
    expect(screen.getByLabelText(/Key ID/)).toHaveAccessibleDescription(/Stored values remain masked after refresh\..*Enter Alpaca credentials/s);
    expect(screen.getByLabelText(/Secret key/)).toHaveAccessibleDescription(/Secret key is never displayed after submit\..*Enter Alpaca credentials/s);
    expect(screen.getByRole("button", { name: /connect and test/i })).toHaveAttribute(
      "title",
      "Enter an Alpaca key ID before testing the connection."
    );
  });

  it("renders the Robinhood read-only connection card with a connect button", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
        robinhoodConnection={robinhoodConnection}
      />
    );

    expect(screen.getByText("Robinhood (read-only)").closest("#robinhood-provider-setup")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Connect Robinhood" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Disconnect" })).toBeInTheDocument();
    expect(screen.getByText("RH-987")).toBeInTheDocument();
    expect(screen.getByText("positions:read, balances:read")).toBeInTheDocument();
  });

  it("shows the OAuth environment hint and blocks connect when Robinhood is not configured", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
        robinhoodConnection={{
          ...robinhoodConnection,
          state: "NotConfigured",
          isConfigured: false,
          isConnected: false,
          connectedAt: null,
          expiresAt: null,
          externalAccountId: null,
          authorizationUrl: null,
          scopes: []
        }}
      />
    );

    expect(screen.getByText(/Read-only Robinhood requires an authorized aggregation provider/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Connect Robinhood" })).toBeDisabled();
  });

  it("renders the Robinhood authorization link when authorization is pending", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
        robinhoodConnection={{
          ...robinhoodConnection,
          state: "AuthorizationPending",
          isConnected: false,
          authorizationUrl: "https://aggregator.example/oauth/authorize?token=abc"
        }}
      />
    );

    const authorizationLink = screen.getByRole("link", { name: /Open authorization page/ });
    expect(authorizationLink).toHaveAttribute("href", "https://aggregator.example/oauth/authorize?token=abc");
    expect(authorizationLink).toHaveAttribute("target", "_blank");
    expect(authorizationLink).toHaveAttribute("rel", "noopener noreferrer");
  });

  it("mounts the Robinhood card when deep-linked to the robinhood-provider-setup hash", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        robinhoodConnection={robinhoodConnection}
      />,
      { initialEntries: ["/settings#robinhood-provider-setup"] }
    );

    expect(document.querySelector("#robinhood-provider-setup")).not.toBeNull();
    expect(screen.getByRole("button", { name: "Connect Robinhood" })).toBeInTheDocument();
  });

  it("blocks live Alpaca credential testing until the live endpoint is acknowledged", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    await user.click(screen.getByRole("radio", { name: "Use Alpaca live endpoint for production brokerage verification" }));
    await user.type(screen.getByPlaceholderText("ALPACA_KEY_ID"), "AK-LIVE");
    await user.type(screen.getByPlaceholderText("ALPACA_SECRET_KEY"), "secret");

    const submit = screen.getByRole("button", { name: /connect and test/i });
    expect(screen.getByRole("checkbox", { name: "Acknowledge live Alpaca endpoint before testing credentials" })).not.toBeChecked();
    expect(screen.getByText("Live endpoint review required")).toBeInTheDocument();
    expect(submit).toBeDisabled();
    expect(submit).toHaveAttribute(
      "title",
      "Acknowledge the live Alpaca endpoint before testing live credentials."
    );

    await user.click(screen.getByRole("checkbox", { name: "Acknowledge live Alpaca endpoint before testing credentials" }));

    expect(submit).toBeEnabled();
    expect(screen.getByText("Credentials ready for test")).toBeInTheDocument();
  });

  it("explains disabled Alpaca form controls while a credential request is running", async () => {
    const user = userEvent.setup();
    const busyReason = "Alpaca credential request is already running.";
    let resolveConnect: (status: BrokerageConnectionStatus) => void = () => undefined;
    apiMocks.connectAlpacaConnection.mockImplementationOnce(() => new Promise<BrokerageConnectionStatus>((resolve) => {
      resolveConnect = resolve;
    }));
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    await user.click(screen.getByRole("radio", { name: "Use Alpaca live endpoint for production brokerage verification" }));
    await user.type(screen.getByPlaceholderText("ALPACA_KEY_ID"), "AK-LIVE");
    await user.type(screen.getByPlaceholderText("ALPACA_SECRET_KEY"), "secret");
    await user.click(screen.getByRole("checkbox", { name: "Acknowledge live Alpaca endpoint before testing credentials" }));
    await user.click(screen.getByRole("button", { name: /connect and test/i }));

    expect(screen.getByLabelText(/Key ID/)).toBeDisabled();
    expect(screen.getByLabelText(/Key ID/)).toHaveAccessibleDescription(new RegExp(`${busyReason}.*Testing Alpaca credentials`, "s"));
    expect(screen.getByLabelText(/Secret key/)).toBeDisabled();
    expect(screen.getByLabelText(/Secret key/)).toHaveAccessibleDescription(new RegExp(`${busyReason}.*Testing Alpaca credentials`, "s"));
    expect(screen.getByRole("radio", { name: "Use Alpaca paper endpoint for workstation validation" })).toHaveAttribute(
      "aria-describedby",
      expect.stringContaining("alpaca-environment-disabled-reason")
    );
    expect(screen.getByRole("radio", { name: "Use Alpaca live endpoint for production brokerage verification" })).toBeDisabled();
    expect(screen.getAllByText(busyReason).length).toBeGreaterThanOrEqual(4);
    expect(screen.getByRole("checkbox", { name: "Acknowledge live Alpaca endpoint before testing credentials" })).toHaveAccessibleDescription(
      new RegExp(`${busyReason}.*Testing Alpaca credentials`, "s")
    );

    resolveConnect({ ...alpacaConnection, environment: "live" });
    expect(await screen.findAllByText("Alpaca account verified.")).toHaveLength(2);
  });

  it("requires confirmation before clearing stored Alpaca credentials", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    await user.click(screen.getByRole("button", { name: /clear/i }));

    expect(apiMocks.revokeAlpacaConnection).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: /confirm clear/i })).toBeEnabled();
    expect(screen.getByText("Confirm Alpaca credential clear")).toBeInTheDocument();
    expect(screen.getByText(/remove the stored Alpaca key reference/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /confirm clear/i }));

    expect(apiMocks.revokeAlpacaConnection).toHaveBeenCalledTimes(1);
  });

  it("renders structured Alpaca clear failure details", async () => {
    const user = userEvent.setup();
    apiMocks.revokeAlpacaConnection.mockRejectedValueOnce(
      new ApiError({
        path: "/api/brokerage-connections/alpaca",
        status: 409,
        detail: "Credential revocation is blocked.",
        validationIssues: [
          {
            field: "providerState",
            label: "providerState",
            messages: ["Provider still has an active verification job."]
          }
        ]
      })
    );

    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    await user.click(screen.getByRole("button", { name: /clear/i }));
    await user.click(screen.getByRole("button", { name: /confirm clear/i }));

    const setupPanel = document.querySelector("#alpaca-provider-setup");
    expect(setupPanel).not.toBeNull();
    expect(await within(setupPanel as HTMLElement).findByText("Meridian service returned 409. Open diagnostics for technical details.")).toBeInTheDocument();
    expect(within(setupPanel as HTMLElement).getAllByText("Credential revocation is blocked.").length).toBeGreaterThan(0);
    expect(within(setupPanel as HTMLElement).getByText("providerState: Provider still has an active verification job.")).toBeInTheDocument();
  });

  it("renders structured Alpaca validation details when credential verification fails", async () => {
    const user = userEvent.setup();
    apiMocks.connectAlpacaConnection.mockRejectedValueOnce(
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

    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        brokerageConnection={alpacaConnection}
      />
    );

    await user.type(screen.getByPlaceholderText("ALPACA_KEY_ID"), "AK-PAPER");
    await user.type(screen.getByPlaceholderText("ALPACA_SECRET_KEY"), "secret");
    await user.click(screen.getByRole("button", { name: /connect and test/i }));

    const setupPanel = document.querySelector("#alpaca-provider-setup");
    expect(setupPanel).not.toBeNull();
    expect(await within(setupPanel as HTMLElement).findByText("Meridian service returned 422. Open diagnostics for technical details.")).toBeInTheDocument();
    expect(within(setupPanel as HTMLElement).getAllByText("One or more validation errors occurred.").length).toBeGreaterThan(0);
    expect(within(setupPanel as HTMLElement).getByText("secretKey: Secret key must include the paper account scope.")).toBeInTheDocument();
  });

  it("renders service coverage groups with mapped service links", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        strategy={{ metrics: [], runs: [] }}
        trading={{} as never}
        portfolio={portfolio}
        data={{ metrics: [], providers: [], backfills: [], exports: [] }}
        accounting={{} as never}
        reporting={{} as never}
      />,
      { initialEntries: ["/settings#backend-capability-coverage"] }
    );

    expect(screen.getByRole("list", { name: "Service coverage by workstation route" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "GET /api/workstation/workflows for Settings Workflow library" })).toHaveAttribute(
      "href",
      "/api/workstation/workflows"
    );
    expect(screen.getByRole("link", { name: "GET /api/workstation/runs/history for Strategy Run history" })).toHaveAttribute(
      "href",
      "/api/workstation/runs/history"
    );
    expect(screen.getByRole("link", { name: "GET /api/ledger/private-capital/activity for Accounting Private-capital activity" })).toHaveAttribute(
      "href",
      "/api/ledger/private-capital/activity"
    );
    expect(screen.getByRole("link", { name: "GET /api/ledger/private-capital/report-output for Accounting Report output" })).toHaveAttribute(
      "href",
      "/api/ledger/private-capital/report-output"
    );
    expect(screen.queryByRole("link", { name: "POST /api/workstation/reconciliation/runs for Accounting Run reconciliation" })).toBeNull();
    expect(screen.getByRole("group", {
      name: "Reference-only POST /api/workstation/reconciliation/runs for Accounting Run reconciliation"
    })).toHaveTextContent(
      "Reference"
    );
  });

  it("renders runtime capability toggles and sends toggle requests", async () => {
    const onFeatureCapabilityToggle = vi.fn();
    const user = userEvent.setup();

    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        featureCapabilities={{
          capabilities: [
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
            },
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
            }
          ]
        }}
        onFeatureCapabilityToggle={onFeatureCapabilityToggle}
      />
    );

    expect(screen.getByRole("heading", { name: "Runtime feature capabilities" })).toBeInTheDocument();
    const securityMasterToggle = screen.getByRole("switch", { name: "Enable Security master" });
    expect(securityMasterToggle).toHaveAttribute("aria-checked", "false");

    await user.click(securityMasterToggle);

    expect(onFeatureCapabilityToggle).toHaveBeenCalledWith("desktop.data.security-master", true);
    expect(screen.getByRole("switch", { name: "Disable Settings workspace" })).toBeDisabled();
  });

  it("renders diagnostic service failures as accessible service cards", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        strategy={null}
        trading={null}
        data={null}
        accounting={null}
        error="Workstation request failed."
        workspaceErrors={{ trading: "<!DOCTYPE HTML><html><body><h1>404</h1><p>File not found</p></body></html>" }}
      />
    );

    const tradingLink = screen.getByRole("link", { name: "Open Trading workspace diagnostic service" });

    expect(tradingLink).toHaveAttribute("href", "/api/workstation/trading");
    expect(within(tradingLink).getByText("Failed")).toBeInTheDocument();
    expect(within(tradingLink).getByText("Workspace data unavailable. Try again or open diagnostics.")).toBeInTheDocument();
  });
});
