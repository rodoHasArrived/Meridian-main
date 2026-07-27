import { axe } from "jest-axe";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as governanceApi from "@/lib/reporting-governance-api";
import { ReportRunGovernanceScreen } from "@/screens/report-run-governance-screen";
import { renderWithRouter } from "@/test/render";
import type {
  GovernedReportingRun,
  ReportingGovernanceSeriesHistory,
  SecureReportingDistributionCapabilityCatalog,
  SecureReportingTransportCapability
} from "@/types/reporting-governance";

vi.mock("@/lib/reporting-governance-api", () => ({
  approveGovernedReportingRestatement: vi.fn(),
  approveGovernedReportingRun: vi.fn(),
  getGovernedReportingRun: vi.fn(),
  getGovernedReportingSeriesHistory: vi.fn(),
  getSecureReportingAccessGrantHistory: vi.fn(),
  getSecureReportingDeliveryHistory: vi.fn(),
  getSecureReportingTransportCapabilities: vi.fn(),
  issueSecureReportingAccessGrant: vi.fn(),
  queueSecureReportingDelivery: vi.fn(),
  releaseGovernedReportingRun: vi.fn(),
  requestGovernedReportingRestatement: vi.fn(),
  revokeSecureReportingAccessGrant: vi.fn(),
  submitGovernedReportingRun: vi.fn(),
  validateGovernedReportingRun: vi.fn()
}));

const draftRun = buildRun();
const seriesHistory: ReportingGovernanceSeriesHistory = {
  seriesId: draftRun.seriesId,
  runs: [draftRun],
  restatementRequests: []
};
const transports: SecureReportingTransportCapability[] = [
  {
    transportId: "secure-portal",
    displayName: "Secure portal",
    deliveryMode: "SecurePortal",
    isExternal: false,
    requiresDestination: false,
    usesGovernedRecipientScope: true,
    issuesAccessGrant: false,
    supportsProviderReceipts: false,
    isConfigured: true,
    isInfrastructureReady: true,
    infrastructureDisabledReasonCode: null,
    isReady: true,
    disabledReasonCode: null
  }
];
const distributionCapabilities: SecureReportingDistributionCapabilityCatalog = {
  canQueueDelivery: true,
  canIssueAccessGrant: true,
  canRevokeAccessGrant: true,
  actionDisabledReasonCode: null,
  transports
};

describe("ReportRunGovernanceScreen", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(governanceApi.getGovernedReportingRun).mockResolvedValue(draftRun);
    vi.mocked(governanceApi.getGovernedReportingSeriesHistory).mockResolvedValue(seriesHistory);
    vi.mocked(governanceApi.getSecureReportingDeliveryHistory).mockResolvedValue([]);
    vi.mocked(governanceApi.getSecureReportingAccessGrantHistory).mockResolvedValue([]);
    vi.mocked(governanceApi.getSecureReportingTransportCapabilities).mockResolvedValue(distributionCapabilities);
    vi.mocked(governanceApi.validateGovernedReportingRun).mockResolvedValue(draftRun);
  });

  it("loads the exact query run and enables only server-authorized lifecycle actions", async () => {
    renderGovernance();

    expect((await screen.findAllByText("run-1")).length).toBeGreaterThan(0);
    expect(governanceApi.getGovernedReportingRun).toHaveBeenCalledWith(
      "run-1",
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(governanceApi.getGovernedReportingSeriesHistory).toHaveBeenCalledWith(
      "series-1",
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(screen.getByText("private-credit")).toBeInTheDocument();
    expect(screen.getByText("Private Credit")).toBeInTheDocument();
    expect(screen.getByText("Enabled by retained policy")).toBeInTheDocument();
    expect(screen.getByText("User: maker-1")).toBeInTheDocument();
    expect(screen.getByText("Group: checker-group")).toBeInTheDocument();
    expect(screen.getByText("Company: company-1")).toBeInTheDocument();

    const validate = screen.getByRole("button", { name: "Validate" });
    const submit = screen.getByRole("button", { name: "Submit for review" });
    expect(validate).toBeEnabled();
    expect(submit).toBeDisabled();

    fireEvent.click(validate);
    await waitFor(() => expect(governanceApi.validateGovernedReportingRun).toHaveBeenCalledWith(
      "run-1",
      7,
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    ));
  });

  it("does not fall back to a recent or fixture run when runId is missing", async () => {
    renderWithRouter(<ReportRunGovernanceScreen />, { initialEntries: ["/reporting/runs/detail"] });

    expect(screen.getByRole("alert")).toHaveTextContent("Select a governed report run");
    expect(governanceApi.getGovernedReportingRun).not.toHaveBeenCalled();
  });

  it("renders disabled owner access as retained policy evidence without inferring authorization", async () => {
    const ownerDisabled = buildRun({
      access: {
        ...draftRun.access,
        allowOwnerAccess: false,
        principals: [{ kind: "User", principalId: "named-reviewer-1" }]
      }
    });
    vi.mocked(governanceApi.getGovernedReportingRun).mockResolvedValue(ownerDisabled);
    vi.mocked(governanceApi.getGovernedReportingSeriesHistory).mockResolvedValue({
      ...seriesHistory,
      runs: [ownerDisabled]
    });

    renderGovernance();

    expect(await screen.findByText("Disabled by retained policy")).toBeInTheDocument();
    expect(screen.getByText("User: named-reviewer-1")).toBeInTheDocument();
  });

  it("aborts authoritative reads when the route unmounts", async () => {
    let requestSignal: AbortSignal | undefined;
    vi.mocked(governanceApi.getGovernedReportingRun).mockImplementation((_runId, options) => {
      requestSignal = options.signal;
      return new Promise<GovernedReportingRun>(() => undefined);
    });

    const view = renderGovernance();
    await waitFor(() => expect(requestSignal).toBeDefined());
    expect(requestSignal?.aborted).toBe(false);

    view.unmount();
    expect(requestSignal?.aborted).toBe(true);
  });

  it("suppresses legacy query-token recipient links", async () => {
    const released = buildRun({
      governanceState: "Released",
      actionAvailability: [],
      release: {
        authority: draftRun.creationAuthority,
        releasedAtUtc: "2026-07-15T12:30:00Z",
        manifestId: "manifest-1",
        manifestHash: "manifest-hash",
        artifacts: [{ artifactId: "artifact-1", artifactHash: "artifact-hash", byteLength: 42 }],
        evidenceIds: ["release-evidence-1"]
      }
    });
    vi.mocked(governanceApi.getGovernedReportingRun).mockResolvedValue(released);
    vi.mocked(governanceApi.getGovernedReportingSeriesHistory).mockResolvedValue({
      ...seriesHistory,
      runs: [released]
    });
    vi.mocked(governanceApi.issueSecureReportingAccessGrant).mockResolvedValue({
      grantId: "grant-1",
      runId: "run-1",
      recipientAccessUri: "/portal/reporting/access-grants/grant-1/exchange?token=legacy-secret",
      expiresAtUtc: "2026-07-15T13:00:00Z",
      audience: "investor-1",
      packageId: "package-1",
      artifactIds: ["artifact-1"]
    });

    const { container } = renderGovernance();
    const issue = await screen.findByRole("button", { name: "Issue scoped recipient access" });
    expect(issue).toBeEnabled();
    fireEvent.click(issue);

    await screen.findByText(/unsafe query or unsupported URL/i);
    expect(screen.queryByRole("link", { name: "Open recipient access" })).not.toBeInTheDocument();
    expect(container.innerHTML).not.toContain("legacy-secret");
    expect(container.querySelector('a[href*="?token="]')).toBeNull();
  });

  it("fails distribution controls closed when the server denies caller actions", async () => {
    const released = buildRun({
      governanceState: "Released",
      actionAvailability: [],
      release: {
        authority: draftRun.creationAuthority,
        releasedAtUtc: "2026-07-15T12:30:00Z",
        manifestId: "manifest-1",
        manifestHash: "manifest-hash",
        artifacts: [{ artifactId: "artifact-1", artifactHash: "artifact-hash", byteLength: 42 }],
        evidenceIds: ["release-evidence-1"]
      }
    });
    vi.mocked(governanceApi.getGovernedReportingRun).mockResolvedValue(released);
    vi.mocked(governanceApi.getGovernedReportingSeriesHistory).mockResolvedValue({
      ...seriesHistory,
      runs: [released]
    });
    vi.mocked(governanceApi.getSecureReportingTransportCapabilities).mockResolvedValue({
      canQueueDelivery: false,
      canIssueAccessGrant: false,
      canRevokeAccessGrant: false,
      actionDisabledReasonCode: "DELIVER_PERMISSION_REQUIRED",
      transports: transports.map((transport) => ({
        ...transport,
        isReady: false,
        disabledReasonCode: "DELIVER_PERMISSION_REQUIRED"
      }))
    });

    renderGovernance();

    const queue = await screen.findByRole("button", { name: "Queue secure delivery" });
    const issue = screen.getByRole("button", { name: "Issue scoped recipient access" });
    expect(queue).toBeDisabled();
    expect(issue).toBeDisabled();
    expect(screen.getAllByText(/DELIVER_PERMISSION_REQUIRED/).length).toBeGreaterThan(0);
  });

  it("queues resolver-backed external delivery with a blank optional destination assertion", async () => {
    const released = buildRun({
      governanceState: "Released",
      actionAvailability: [],
      release: {
        authority: draftRun.creationAuthority,
        releasedAtUtc: "2026-07-15T12:30:00Z",
        manifestId: "manifest-1",
        manifestHash: "manifest-hash",
        artifacts: [{ artifactId: "artifact-1", artifactHash: "artifact-hash", byteLength: 42 }],
        evidenceIds: ["release-evidence-1"]
      }
    });
    const relay: SecureReportingTransportCapability = {
      ...transports[0],
      transportId: "http-relay",
      displayName: "HTTP notification relay",
      deliveryMode: "ExternalNotification",
      isExternal: true,
      requiresDestination: false,
      issuesAccessGrant: true,
      supportsProviderReceipts: true
    };
    vi.mocked(governanceApi.getGovernedReportingRun).mockResolvedValue(released);
    vi.mocked(governanceApi.getGovernedReportingSeriesHistory).mockResolvedValue({
      ...seriesHistory,
      runs: [released]
    });
    vi.mocked(governanceApi.getSecureReportingTransportCapabilities).mockResolvedValue({
      ...distributionCapabilities,
      transports: [relay]
    });
    vi.mocked(governanceApi.queueSecureReportingDelivery).mockResolvedValue({
      jobId: "delivery-1",
      runId: released.runId,
      packageId: "package-1",
      releaseVersion: "1",
      artifactManifestHashSha256: "manifest-hash",
      distributionId: "distribution-1",
      transportId: relay.transportId,
      recipient: "maker-1",
      destination: "server-resolved@example.test",
      subject: "Released report",
      state: "Queued",
      attemptCount: 0,
      maxAttempts: 3,
      createdAtUtc: "2026-07-15T12:31:00Z",
      updatedAtUtc: "2026-07-15T12:31:00Z",
      nextAttemptAtUtc: "2026-07-15T12:31:00Z",
      lastErrorCode: null,
      lastError: null,
      providerMessageId: null,
      accessGrantId: null,
      receipts: []
    });

    renderGovernance();

    const queue = await screen.findByRole("button", { name: "Queue secure delivery" });
    fireEvent.change(screen.getByLabelText("Distribution ID"), { target: { value: "distribution-1" } });
    fireEvent.change(screen.getByLabelText("Subject"), { target: { value: "Released report" } });
    fireEvent.change(screen.getByLabelText("Body"), { target: { value: "Use the secure link." } });
    expect(screen.getByLabelText("Destination")).toHaveValue("");
    expect(screen.getByText(/Optional equality assertion/i)).toBeInTheDocument();
    expect(queue).toBeEnabled();

    fireEvent.click(queue);

    await waitFor(() => expect(governanceApi.queueSecureReportingDelivery).toHaveBeenCalledWith(
      expect.objectContaining({
        runId: released.runId,
        transportId: relay.transportId,
        destination: ""
      }),
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    ));
  });

  it("locks both released ClientPackage primaries and queues them atomically", async () => {
    const released = buildRun({
      governanceState: "Released",
      actionAvailability: [],
      normalizedParameters: {
        ...draftRun.normalizedParameters,
        outputFormat: "ClientPackage"
      },
      release: {
        authority: draftRun.creationAuthority,
        releasedAtUtc: "2026-07-15T12:30:00Z",
        manifestId: "manifest-client-package",
        manifestHash: "manifest-client-package-hash",
        artifacts: [
          { artifactId: "run-1.pdf", artifactHash: "pdf-hash", byteLength: 42 },
          { artifactId: "run-1.xlsx", artifactHash: "xlsx-hash", byteLength: 84 },
          { artifactId: "run-1.evidence.json", artifactHash: "evidence-hash", byteLength: 21 }
        ],
        evidenceIds: ["release-evidence-1"]
      }
    });
    vi.mocked(governanceApi.getGovernedReportingRun).mockResolvedValue(released);
    vi.mocked(governanceApi.getGovernedReportingSeriesHistory).mockResolvedValue({
      ...seriesHistory,
      runs: [released]
    });
    vi.mocked(governanceApi.queueSecureReportingDelivery).mockResolvedValue({
      jobId: "delivery-client-package",
      runId: released.runId,
      packageId: "package-1",
      releaseVersion: "1",
      artifactManifestHashSha256: "manifest-client-package-hash",
      distributionId: "distribution-1",
      transportId: "secure-portal",
      recipient: "maker-1",
      destination: "",
      subject: "Released client package",
      state: "Queued",
      attemptCount: 0,
      maxAttempts: 3,
      createdAtUtc: "2026-07-15T12:31:00Z",
      updatedAtUtc: "2026-07-15T12:31:00Z",
      nextAttemptAtUtc: "2026-07-15T12:31:00Z",
      lastErrorCode: null,
      lastError: null,
      providerMessageId: null,
      accessGrantId: null,
      receipts: []
    });

    renderGovernance();

    expect(await screen.findByText("Client package primaries locked")).toBeInTheDocument();
    const pdf = screen.getByLabelText("Include run-1.pdf in distribution");
    const xlsx = screen.getByLabelText("Include run-1.xlsx in distribution");
    const evidence = screen.getByLabelText("Include run-1.evidence.json in distribution");
    expect(pdf).toBeChecked();
    expect(pdf).toBeDisabled();
    expect(xlsx).toBeChecked();
    expect(xlsx).toBeDisabled();
    expect(evidence).toBeChecked();
    expect(evidence).toBeEnabled();
    fireEvent.click(evidence);

    fireEvent.change(screen.getByLabelText("Distribution ID"), { target: { value: "distribution-1" } });
    fireEvent.change(screen.getByLabelText("Subject"), { target: { value: "Released client package" } });
    fireEvent.change(screen.getByLabelText("Body"), { target: { value: "Use the secure link." } });
    const queue = screen.getByRole("button", { name: "Queue secure delivery" });
    expect(queue).toBeEnabled();
    fireEvent.click(queue);

    await waitFor(() => expect(governanceApi.queueSecureReportingDelivery).toHaveBeenCalledWith(
      expect.objectContaining({
        artifactIds: ["run-1.pdf", "run-1.xlsx"]
      }),
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    ));
  });

  it("blocks ClientPackage delivery and grants when either released primary is missing", async () => {
    const incomplete = buildRun({
      governanceState: "Released",
      actionAvailability: [],
      normalizedParameters: {
        ...draftRun.normalizedParameters,
        outputFormat: "ClientPackage"
      },
      release: {
        authority: draftRun.creationAuthority,
        releasedAtUtc: "2026-07-15T12:30:00Z",
        manifestId: "manifest-incomplete-client-package",
        manifestHash: "manifest-incomplete-hash",
        artifacts: [
          { artifactId: "run-1.pdf", artifactHash: "pdf-hash", byteLength: 42 }
        ],
        evidenceIds: ["release-evidence-1"]
      }
    });
    vi.mocked(governanceApi.getGovernedReportingRun).mockResolvedValue(incomplete);
    vi.mocked(governanceApi.getGovernedReportingSeriesHistory).mockResolvedValue({
      ...seriesHistory,
      runs: [incomplete]
    });

    renderGovernance();

    expect(await screen.findByText("Client package release is incomplete")).toBeInTheDocument();
    expect(screen.getByText(/missing run-1.xlsx/i)).toBeInTheDocument();
    const queue = screen.getByRole("button", { name: "Queue secure delivery" });
    const issueGrant = screen.getByRole("button", { name: "Issue scoped recipient access" });
    expect(queue).toBeDisabled();
    expect(issueGrant).toBeDisabled();
    fireEvent.click(queue);
    fireEvent.click(issueGrant);
    expect(governanceApi.queueSecureReportingDelivery).not.toHaveBeenCalled();
    expect(governanceApi.issueSecureReportingAccessGrant).not.toHaveBeenCalled();
  });

  it("treats a stale array-only transport response as unavailable", async () => {
    vi.mocked(governanceApi.getSecureReportingTransportCapabilities).mockResolvedValue(
      [] as unknown as SecureReportingDistributionCapabilityCatalog
    );

    renderGovernance();

    expect(await screen.findByText("Transport catalog unavailable")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Queue secure delivery" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Issue scoped recipient access" })).toBeDisabled();
  });

  it("has no basic accessibility violations with retained governance evidence", async () => {
    const { container } = renderGovernance();
    await screen.findByText("Certified point-in-time snapshot");

    const results = await axe(container);
    expect(results.violations.map((violation) => ({
      id: violation.id,
      targets: violation.nodes.map((node) => node.target)
    }))).toEqual([]);
  });
});

function renderGovernance() {
  return renderWithRouter(<ReportRunGovernanceScreen />, {
    initialEntries: ["/reporting/runs/detail?runId=run-1"]
  });
}

function buildRun(overrides: Partial<GovernedReportingRun> = {}): GovernedReportingRun {
  const authority = {
    actorId: "maker-1",
    tenantId: "tenant-1",
    organizationId: "org-1",
    companyId: "company-1",
    permissions: ["CreateRun"],
    origin: "HumanOperator",
    correlationId: "correlation-1",
    principalIds: ["maker-1"]
  };

  return {
    runId: "run-1",
    seriesId: "series-1",
    revision: 1,
    templateId: "investor-statement",
    templateVersion: "4",
    scope: {
      tenantId: "tenant-1",
      organizationId: "org-1",
      companyId: "company-1",
      fundId: "fund-1",
      bookId: "book-1",
      periodId: "2026-06"
    },
    access: {
      policyId: "policy-1",
      policyVersion: "2",
      mode: "Restricted",
      ownerPrincipalId: "maker-1",
      allowOwnerAccess: true,
      principals: [
        { kind: "User", principalId: "maker-1" },
        { kind: "Group", principalId: "checker-group" },
        { kind: "Company", principalId: "company-1" }
      ],
      policyHash: "policy-hash"
    },
    snapshot: {
      snapshotId: "snapshot-1",
      snapshotHash: "snapshot-hash",
      reconciliationCheckpointId: "reconciliation-1",
      capturedAtUtc: "2026-07-15T12:00:00Z",
      sourceCheckpointId: "ledger-sequence-100",
      sourceCheckpointHash: "source-hash",
      reconciliationCheckpointHash: "reconciliation-hash",
      parametersCanonicalJson: JSON.stringify({
        scope: { fundProfileId: "fund-1" },
        periodId: "2026-06",
        finality: "Final",
        outputFormat: "Pdf"
      }),
      parametersHash: "parameters-hash"
    },
    creationAuthority: authority,
    createdAtUtc: "2026-07-15T12:00:00Z",
    restatementOfRunId: null,
    executionState: "Succeeded",
    governanceState: "Draft",
    version: 7,
    readiness: null,
    approval: null,
    release: null,
    auditTrail: [
      {
        eventId: "event-1",
        aggregateKind: "Run",
        aggregateId: "run-1",
        aggregateVersion: 7,
        occurredAtUtc: "2026-07-15T12:00:00Z",
        action: "RunCreated",
        authority,
        permissionUsed: "CreateRun",
        fromExecutionState: null,
        toExecutionState: "Succeeded",
        fromGovernanceState: null,
        toGovernanceState: "Draft",
        fromRestatementState: null,
        toRestatementState: null,
        note: "Certified run created.",
        previousHash: null,
        hash: "audit-hash"
      }
    ],
    normalizedParameters: {
      scope: {
        fundProfileId: "fund-1",
        entityScopeKind: "Portfolio",
        entityId: null,
        portfolioId: "portfolio-credit",
        investorId: null,
        dimensions: {
          strategyId: "private-credit",
          externalGlDimensions: { Department: "Private Credit" }
        }
      },
      periodId: "2026-06",
      asOfDate: "2026-06-30",
      ledgerBook: { ledgerBookId: null, ledgerBookCode: "Primary GL" },
      accountingBasis: "Gaap",
      presentationCurrency: "USD",
      consolidationLevel: "Portfolio",
      outputFormat: "Pdf",
      finality: "Final",
      includeSupportingSchedules: true,
      includeEvidenceAppendix: true,
      templateParameters: {}
    },
    actionAvailability: [
      { action: "validate", isAllowed: true, blockedReason: null, expectedVersion: 7 },
      { action: "submit", isAllowed: false, blockedReason: "Validate first.", expectedVersion: 7 }
    ],
    ...overrides
  };
}
