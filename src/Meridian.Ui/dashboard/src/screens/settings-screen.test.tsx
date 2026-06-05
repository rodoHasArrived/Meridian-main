import { screen, within } from "@testing-library/react";
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
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot,
  RolePermissionCatalog,
  SecurityAssetProfileDefinition,
  SessionInfo,
  SystemOverviewResponse
} from "@/types";

const apiMocks = vi.hoisted(() => ({
  approveSecurityAssetProfile: vi.fn(),
  assignLedgerMapping: vi.fn(),
  createSecurityMasterEntry: vi.fn(),
  createRolePermissionProfile: vi.fn(),
  draftSecurityAssetProfile: vi.fn(),
  getSecurityAssetProfileLineage: vi.fn(),
  rollbackSecurityAssetProfile: vi.fn(),
  upsertOperationsApprovalPolicyRule: vi.fn(),
  upsertOperationsCloseCalendarItem: vi.fn(),
  connectAlpacaConnection: vi.fn(),
  revokeAlpacaConnection: vi.fn(),
  testProviderConnection: vi.fn(),
  putProviderCredentials: vi.fn(),
  verifyProviderConnection: vi.fn(),
  deleteProviderCredentials: vi.fn()
}));

vi.mock("@/lib/api", async (importActual) => ({
  ...(await importActual<typeof import("@/lib/api")>()),
  approveSecurityAssetProfile: apiMocks.approveSecurityAssetProfile,
  assignLedgerMapping: apiMocks.assignLedgerMapping,
  createSecurityMasterEntry: apiMocks.createSecurityMasterEntry,
  createRolePermissionProfile: apiMocks.createRolePermissionProfile,
  draftSecurityAssetProfile: apiMocks.draftSecurityAssetProfile,
  getSecurityAssetProfileLineage: apiMocks.getSecurityAssetProfileLineage,
  rollbackSecurityAssetProfile: apiMocks.rollbackSecurityAssetProfile,
  upsertOperationsApprovalPolicyRule: apiMocks.upsertOperationsApprovalPolicyRule,
  upsertOperationsCloseCalendarItem: apiMocks.upsertOperationsCloseCalendarItem,
  connectAlpacaConnection: apiMocks.connectAlpacaConnection,
  revokeAlpacaConnection: apiMocks.revokeAlpacaConnection,
  testProviderConnection: apiMocks.testProviderConnection,
  putProviderCredentials: apiMocks.putProviderCredentials,
  verifyProviderConnection: apiMocks.verifyProviderConnection,
  deleteProviderCredentials: apiMocks.deleteProviderCredentials
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
    apiMocks.rollbackSecurityAssetProfile.mockReset();
    apiMocks.upsertOperationsApprovalPolicyRule.mockReset();
    apiMocks.upsertOperationsCloseCalendarItem.mockReset();
    apiMocks.revokeAlpacaConnection.mockReset();
    apiMocks.testProviderConnection.mockReset();
    apiMocks.putProviderCredentials.mockReset();
    apiMocks.verifyProviderConnection.mockReset();
    apiMocks.deleteProviderCredentials.mockReset();
  });

  it("renders recent events as accessible status evidence rows", () => {
    renderWithRouter(<SettingsScreen session={session} overview={overview} />);

    expect(screen.getByRole("region", { name: "Settings workbench context" })).toHaveTextContent(
      "Operator control posture"
    );
    const eventTable = screen.getByRole("table", { name: "1 recent system event" });
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
      name: "Open Settings diagnostic endpoints from profile authentication posture"
    })).toHaveAttribute("href", "/settings#diagnostic-endpoints");
    expect(document.querySelector("#diagnostic-endpoints")).toBeInTheDocument();
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
    expect(within(controlCenter).getByRole("link", { name: "Open role and permission catalog endpoint" })).toHaveAttribute(
      "href",
      "/api/auth/roles"
    );
    expect(within(controlCenter).getByRole("link", { name: "Open raw endpoint for Account Close Calendar" })).toHaveAttribute(
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
    await user.clear(within(form).getByLabelText("Approval policy reviewer role"));
    await user.type(within(form).getByLabelText("Approval policy reviewer role"), "Controller");
    await user.clear(within(form).getByLabelText("Approval policy required distinct approvals"));
    await user.type(within(form).getByLabelText("Approval policy required distinct approvals"), "3");
    await user.clear(within(form).getByLabelText("Approval policy evidence requirement"));
    await user.type(
      within(form).getByLabelText("Approval policy evidence requirement"),
      "Controller packet and checklist control evidence."
    );
    await user.clear(within(form).getByLabelText("Approval policy rationale"));
    await user.type(within(form).getByLabelText("Approval policy rationale"), "Tighten close approval accounting");
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
    await user.type(screen.getByLabelText("Alpaca API key"), "alpaca-key");
    await user.type(screen.getByLabelText("Alpaca API secret"), "alpaca-secret");
    await user.click(screen.getByRole("button", { name: "Test Alpaca connection" }));
    await user.click(screen.getByRole("button", { name: "Save Alpaca credentials" }));
    await user.click(screen.getByRole("button", { name: "Re-verify Alpaca connection" }));
    await user.click(screen.getByRole("button", { name: "Clear Alpaca credentials" }));

    expect(apiMocks.testProviderConnection).toHaveBeenCalledWith("alpaca");
    expect(apiMocks.putProviderCredentials).toHaveBeenCalledWith(
      "alpaca",
      expect.objectContaining({
        credentials: expect.objectContaining({
          apiKey: "alpaca-key",
          apiSecret: "alpaca-secret"
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
        actionHref: "/settings#provider-quickbooks-connection"
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
    await user.type(screen.getByLabelText("QuickBooks Online Client ID"), "qbo-client-id");
    await user.type(screen.getByLabelText("QuickBooks Online Client secret"), "qbo-client-secret");
    await user.type(screen.getByLabelText("QuickBooks Online Refresh token"), "qbo-refresh-token");
    await user.type(screen.getByLabelText("QuickBooks Online Company realm ID"), "9130359087654321");
    await user.type(screen.getByLabelText("QuickBooks Online Company name"), "Meridian-Dev");
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

  it("filters provider rows by search and risk filters", async () => {
    const user = userEvent.setup();

    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        providerConnections={providerConnections}
      />
    );

    await user.type(screen.getByLabelText("Search providers in connection center"), "polygon");
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
      />
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
    renderWithRouter(<SettingsScreen session={session} overview={{ ...overview, recentEvents: [] }} />);

    expect(screen.getAllByText("No recent events")).toHaveLength(2);
    expect(screen.getByText("No system events reported for the active session. Diagnostic endpoints remain available below.")).toBeInTheDocument();
  });

  it("renders an alert state when overview data is unavailable", () => {
    renderWithRouter(<SettingsScreen session={session} overview={null} />);

    expect(screen.getAllByText("Event stream unavailable")).toHaveLength(2);
    expect(screen.getByRole("alert")).toHaveTextContent("Reconnect to the Meridian API");
  });

  it("labels diagnostic endpoint links", () => {
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
      />
    );

    expect(screen.getByRole("link", { name: "Open System overview diagnostic endpoint" })).toHaveAttribute(
      "href",
      "/api/status"
    );
    expect(screen.getByRole("link", { name: "Open Data workspace diagnostic endpoint" })).toHaveAttribute(
      "href",
      "/api/workstation/data"
    );
    expect(screen.getByRole("link", { name: "Open Strategy workspace diagnostic endpoint" })).toHaveAttribute(
      "href",
      "/api/workstation/strategy"
    );
    expect(screen.getByRole("list", { name: "Diagnostic endpoint availability" })).toBeInTheDocument();
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
    expect(await within(setupPanel as HTMLElement).findByText("Endpoint returned 409 for /api/brokerage-connections/alpaca.")).toBeInTheDocument();
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
    expect(await within(setupPanel as HTMLElement).findByText("Endpoint returned 422 for /api/brokerage-connections/alpaca/connect.")).toBeInTheDocument();
    expect(within(setupPanel as HTMLElement).getAllByText("One or more validation errors occurred.").length).toBeGreaterThan(0);
    expect(within(setupPanel as HTMLElement).getByText("secretKey: Secret key must include the paper account scope.")).toBeInTheDocument();
  });

  it("renders backend capability groups with mapped API links", () => {
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
      />
    );

    expect(screen.getByRole("list", { name: "Backend capability coverage by workstation route" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "GET /api/workstation/workflows for Settings Workflow library" })).toHaveAttribute(
      "href",
      "/api/workstation/workflows"
    );
    expect(screen.getByRole("link", { name: "GET /api/workstation/runs/history for Strategy Run history" })).toHaveAttribute(
      "href",
      "/api/workstation/runs/history"
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
    const securityMasterToggle = screen.getByRole("checkbox", { name: "Enable Security master" });
    expect(securityMasterToggle).not.toBeChecked();

    await user.click(securityMasterToggle);

    expect(onFeatureCapabilityToggle).toHaveBeenCalledWith("desktop.data.security-master", true);
    expect(screen.getByRole("checkbox", { name: "Disable Settings workspace" })).toBeDisabled();
  });

  it("renders diagnostic endpoint failures as accessible endpoint cards", () => {
    renderWithRouter(
      <SettingsScreen
        session={session}
        overview={overview}
        strategy={null}
        trading={null}
        data={null}
        accounting={null}
        error="Workstation request failed."
        workspaceErrors={{ trading: "Trading API returned 503." }}
      />
    );

    const tradingLink = screen.getByRole("link", { name: "Open Trading workspace diagnostic endpoint" });

    expect(tradingLink).toHaveAttribute("href", "/api/workstation/trading");
    expect(within(tradingLink).getByText("Failed")).toBeInTheDocument();
    expect(within(tradingLink).getByText("Trading API returned 503.")).toBeInTheDocument();
  });
});
