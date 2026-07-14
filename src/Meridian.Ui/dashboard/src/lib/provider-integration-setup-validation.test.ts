import { describe, expect, it } from "vitest";
import {
  formatProviderIntegrationSetupDraftIssues,
  validateProviderIntegrationSetupDraft
} from "@/lib/provider-integration-setup-validation";
import type { ProviderIntegrationConnection, ProviderIntegrationManifest } from "@/types";

function createManifest(overrides: Partial<ProviderIntegrationManifest> = {}): ProviderIntegrationManifest {
  return {
    manifestId: "manifest-polygon-v1",
    manifestVersion: 1,
    providerId: "polygon",
    displayName: "Polygon.io",
    integrationType: "Rest",
    environment: "paper",
    auth: { type: "OAuth2", tokenUrl: "https://api.example.com/oauth/token", scopes: [], metadata: {} },
    capabilities: [
      { capability: "Positions", enabled: true, requiresCertifiedAdapter: false, requiredCanonicalFields: [] }
    ],
    endpoints: [],
    fieldMappings: [],
    sync: {
      mode: "incremental",
      frequency: "daily",
      time: "06:00",
      timezone: "America/New_York",
      cursorType: "Timestamp",
      cursorField: "updated_at",
      fullRefreshFrequency: "monthly"
    },
    validationRules: [],
    activation: {
      requiresAuthenticationTest: true,
      requiresEndpointTest: true,
      requiresDryRun: true,
      requiresApproval: true,
      productionWriteCapabilitiesAllowed: false,
      requiredIssueCodes: []
    },
    state: "Draft",
    createdBy: "operator@example.com",
    createdAt: "2026-06-16T14:30:00Z",
    ...overrides
  };
}

function createConnection(overrides: Partial<ProviderIntegrationConnection> = {}): ProviderIntegrationConnection {
  return {
    connectionId: "connection-polygon",
    providerId: "polygon",
    manifestId: "manifest-polygon-v1",
    connectionName: "Polygon paper",
    environment: "paper",
    state: "Draft",
    credentialSecretRef: "provider-credential:polygon:local",
    enabledCapabilities: ["Positions"],
    ownerUserId: "operator@example.com",
    createdAt: "2026-06-16T14:30:00Z",
    updatedAt: "2026-06-16T14:30:00Z",
    ...overrides
  };
}

describe("validateProviderIntegrationSetupDraft", () => {
  it("returns no issues for a consistent draft", () => {
    expect(validateProviderIntegrationSetupDraft(createManifest(), createConnection())).toEqual([]);
  });

  it("reports every missing required field in one pass", () => {
    const manifest = createManifest({ manifestId: " ", providerId: "" });
    const connection = createConnection({
      connectionId: "",
      providerId: "",
      manifestId: "",
      credentialSecretRef: " "
    });

    const issues = validateProviderIntegrationSetupDraft(manifest, connection);

    expect(issues.map((issue) => issue.field)).toEqual([
      "manifest.manifestId",
      "manifest.providerId",
      "connection.connectionId",
      "connection.providerId",
      "connection.manifestId",
      "connection.credentialSecretRef"
    ]);
  });

  it("reports scope mismatches between connection and manifest", () => {
    const connection = createConnection({
      manifestId: "manifest-other",
      providerId: "alpaca",
      environment: "production"
    });

    const issues = validateProviderIntegrationSetupDraft(createManifest(), connection);

    expect(issues.map((issue) => issue.field)).toEqual([
      "connection.manifestId",
      "connection.providerId",
      "connection.environment"
    ]);
    expect(issues[0]?.message).toContain("must match the manifest being saved");
  });

  it("treats environment comparison as case-insensitive", () => {
    const connection = createConnection({ environment: "Paper" });

    expect(validateProviderIntegrationSetupDraft(createManifest(), connection)).toEqual([]);
  });

  it("reports capabilities the manifest does not declare", () => {
    const connection = createConnection({
      enabledCapabilities: ["Positions", "Transactions"]
    });

    const issues = validateProviderIntegrationSetupDraft(createManifest(), connection);

    expect(issues).toHaveLength(1);
    expect(issues[0]).toMatchObject({
      field: "connection.enabledCapabilities",
      message: "Provider connection enables Transactions, but the manifest does not declare it."
    });
  });

  it("reports missing capability collections from hand-edited JSON instead of crashing", () => {
    const manifest = {
      ...createManifest(),
      capabilities: undefined
    } as unknown as ProviderIntegrationManifest;
    const connection = {
      ...createConnection(),
      enabledCapabilities: undefined
    } as unknown as ProviderIntegrationConnection;

    const issues = validateProviderIntegrationSetupDraft(manifest, connection);

    expect(issues.map((issue) => issue.field)).toEqual([
      "manifest.capabilities",
      "connection.enabledCapabilities"
    ]);
  });
});

describe("formatProviderIntegrationSetupDraftIssues", () => {
  it("formats issues as label-prefixed detail lines", () => {
    const issues = validateProviderIntegrationSetupDraft(
      createManifest(),
      createConnection({ credentialSecretRef: "" })
    );

    expect(formatProviderIntegrationSetupDraftIssues(issues)).toEqual([
      "Connection credential secret reference: Connection credential secret reference is required."
    ]);
  });
});
