import type { ReportingRunParameters } from "./workstation-4";

export type ReportingGovernanceExecutionState = "Queued" | "Running" | "Succeeded" | "Failed" | string;

export type ReportingGovernanceState =
  | "Draft"
  | "Validated"
  | "InReview"
  | "Approved"
  | "Released"
  | string;

export type ReportingRestatementState = "Requested" | "Approved" | "Rejected" | string;

export interface ReportingGovernanceOperationalScope {
  tenantId: string;
  organizationId: string;
  companyId: string | null;
  fundId: string | null;
  bookId: string;
  periodId: string;
}

export type ReportingGovernanceAccessPrincipalKind = "User" | "Group" | "Company";

export interface ReportingGovernanceAccessPrincipal {
  kind: ReportingGovernanceAccessPrincipalKind;
  principalId: string;
}

export interface ReportingGovernanceAccessScope {
  policyId: string;
  policyVersion: string;
  mode: string;
  ownerPrincipalId: string | null;
  allowOwnerAccess: boolean;
  principals: ReportingGovernanceAccessPrincipal[];
  policyHash: string;
}

export interface ReportingGovernanceCertifiedSnapshot {
  snapshotId: string;
  snapshotHash: string;
  reconciliationCheckpointId: string;
  capturedAtUtc: string;
  sourceCheckpointId?: string | null;
  sourceCheckpointHash?: string | null;
  reconciliationCheckpointHash?: string | null;
  parametersCanonicalJson?: string | null;
  parametersHash?: string | null;
}

export interface ReportingGovernanceAuthority {
  actorId: string;
  tenantId: string;
  organizationId: string;
  companyId: string | null;
  permissions: string[];
  origin: string;
  correlationId: string;
  principalIds: string[];
}

export interface ReportingGovernanceReadinessCheck {
  checkId: string;
  passed: boolean;
  evidenceIds: string[];
  failureReason: string | null;
}

export interface ReportingGovernanceReadiness {
  receiptId: string;
  receiptHash: string;
  evaluatedAtUtc: string;
  isReady: boolean;
  checks: ReportingGovernanceReadinessCheck[];
}

export interface ReportingGovernanceApproval {
  authority: ReportingGovernanceAuthority;
  approvedAtUtc: string;
  decisionNote: string;
}

export interface ReportingGovernanceArtifact {
  artifactId: string;
  artifactHash: string;
  byteLength: number;
  fileName?: string | null;
  contentType?: string | null;
}

export interface ReportingGovernanceRelease {
  authority: ReportingGovernanceAuthority;
  releasedAtUtc: string;
  manifestId: string;
  manifestHash: string;
  artifacts: ReportingGovernanceArtifact[];
  evidenceIds: string[];
}

export interface ReportingGovernanceActionAvailability {
  action: string;
  isAllowed: boolean;
  blockedReason: string | null;
  expectedVersion: number;
}

export interface ReportingGovernanceAuditEntry {
  eventId: string;
  aggregateKind: string;
  aggregateId: string;
  aggregateVersion: number;
  occurredAtUtc: string;
  action: string;
  authority: ReportingGovernanceAuthority;
  permissionUsed: string;
  fromExecutionState: string | null;
  toExecutionState: string | null;
  fromGovernanceState: string | null;
  toGovernanceState: string | null;
  fromRestatementState: string | null;
  toRestatementState: string | null;
  note: string | null;
  previousHash: string | null;
  hash: string;
}

/**
 * Browser mirror of the canonical, immutable governed-run projection. Legacy response adaptation
 * belongs at the API boundary; screen code consumes this required canonical shape.
 */
export interface GovernedReportingRun {
  runId: string;
  seriesId: string;
  revision: number;
  templateId: string;
  templateVersion: string;
  scope: ReportingGovernanceOperationalScope;
  access: ReportingGovernanceAccessScope;
  snapshot: ReportingGovernanceCertifiedSnapshot;
  creationAuthority: ReportingGovernanceAuthority;
  createdAtUtc: string;
  restatementOfRunId: string | null;
  executionState: ReportingGovernanceExecutionState;
  governanceState: ReportingGovernanceState;
  version: number;
  readiness: ReportingGovernanceReadiness | null;
  approval: ReportingGovernanceApproval | null;
  release: ReportingGovernanceRelease | null;
  auditTrail: ReportingGovernanceAuditEntry[];
  normalizedParameters: ReportingRunParameters;
  /**
   * Additive server-owned command authorization. The browser intentionally treats an absent
   * action as denied instead of deriving authority from lifecycle state or permission names.
   */
  actionAvailability: ReportingGovernanceActionAvailability[];
}

export interface ReportingGovernanceChangedLine {
  lineKey: string;
  previousValue: string;
  currentValue: string;
  evidenceIds: string[];
}

export interface ReportingGovernanceRestatement {
  requestId: string;
  predecessorRunId: string;
  seriesId: string;
  predecessorRevision: number;
  predecessorVersion: number;
  reason: string;
  changedLines: ReportingGovernanceChangedLine[];
  requestedBy: ReportingGovernanceAuthority;
  requestedAtUtc: string;
  state: ReportingRestatementState;
  version: number;
  approvedBy: ReportingGovernanceAuthority | null;
  approvedAtUtc: string | null;
  draftRunId: string | null;
  auditTrail: ReportingGovernanceAuditEntry[];
  actionAvailability: ReportingGovernanceActionAvailability[];
}

export interface ReportingGovernanceRestatementApproval {
  request: ReportingGovernanceRestatement;
  draftRun: GovernedReportingRun;
}

export interface ReportingGovernanceSeriesHistory {
  seriesId: string;
  runs: GovernedReportingRun[];
  restatementRequests: ReportingGovernanceRestatement[];
}

export interface SecureReportingDeliveryReceipt {
  receiptId: string;
  kind: string;
  occurredAtUtc: string;
  providerReference: string | null;
  evidenceReference: string | null;
  detail: string | null;
}

export interface SecureReportingDelivery {
  jobId: string;
  runId: string;
  packageId: string;
  releaseVersion: string;
  artifactManifestHashSha256: string;
  distributionId: string;
  transportId: string;
  recipient: string;
  recipientKind?: "User" | "Group" | "Company";
  destination: string;
  subject: string;
  state: string;
  attemptCount: number;
  maxAttempts: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  nextAttemptAtUtc: string | null;
  lastErrorCode: string | null;
  lastError: string | null;
  providerMessageId: string | null;
  accessGrantId: string | null;
  receipts: SecureReportingDeliveryReceipt[];
}

export interface SecureReportingDeliveryQueueRequest {
  runId: string;
  distributionId: string;
  transportId: string;
  recipientPrincipalId: string | null;
  recipientPrincipalKind?: "User" | "Group" | "Company" | null;
  destination: string;
  subject: string;
  body: string;
  artifactIds: string[];
  grantLifetimeSeconds: number | null;
  grantMaxUses: number | null;
  maxAttempts: number;
}

export interface SecureReportingAccessGrantIssueRequest {
  runId: string;
  recipientPrincipalId: string | null;
  recipientPrincipalKind?: "User" | "Group" | "Company" | null;
  artifactIds: string[];
  lifetimeSeconds: number | null;
  maxUses: number | null;
}

/** One-time issuance response. RecipientAccessUri may contain an opaque fragment, never a query bearer. */
export interface SecureReportingIssuedAccessGrant {
  grantId: string;
  runId: string;
  recipientAccessUri: string;
  expiresAtUtc: string;
  audience: string;
  audienceKind?: "User" | "Group" | "Company";
  packageId: string;
  artifactIds: string[];
}

/** Credential-free durable grant projection returned by authenticated operator discovery. */
export interface SecureReportingAccessGrant {
  grantId: string;
  runId: string;
  packageId: string;
  audience: string;
  audienceKind?: "User" | "Group" | "Company";
  artifactIds: string[];
  state: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  maxUses: number;
  useCount: number;
  lastUsedAtUtc: string | null;
  revokedAtUtc: string | null;
  revokedBy: string | null;
  revocationReason: string | null;
  allowPackageRead: boolean;
}

export interface SecureReportingTransportCapability {
  transportId: string;
  displayName: string;
  deliveryMode: string;
  isExternal: boolean;
  requiresDestination: boolean;
  usesGovernedRecipientScope: boolean;
  issuesAccessGrant: boolean;
  supportsProviderReceipts: boolean;
  isConfigured: boolean;
  isInfrastructureReady: boolean;
  infrastructureDisabledReasonCode: string | null;
  isReady: boolean;
  disabledReasonCode: string | null;
}

/** Caller-specific authorization and credential-free transport readiness. */
export interface SecureReportingDistributionCapabilityCatalog {
  canQueueDelivery: boolean;
  canIssueAccessGrant: boolean;
  canRevokeAccessGrant: boolean;
  actionDisabledReasonCode: string | null;
  transports: SecureReportingTransportCapability[];
}
