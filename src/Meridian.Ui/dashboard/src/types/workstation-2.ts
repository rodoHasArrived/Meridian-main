import type {
  AccountingConfigurationValidationIssue,
  OperationsActionOrigin,
  OperationsBreakCase,
  OperationsContinuityWorkflow,
  OperationsEvidenceLink,
  OperationsGateKey,
  OperationsNextAction,
  OperationsReconciliationLaneSummary,
  OperationsWorkflowBlocker,
  OperationsWorkflowStatus,
  OperatorWorkItem,
  OperatorWorkItemTone,
  PrivateCapitalCloseCockpit,
  TradingAcceptanceGateStatus,
  WorkflowAction,
} from "../types";

export interface OperationsLedgerValidationRequest {
  expectedVersion: number;
  actor: string;
  isBalanced: boolean;
  periodOpen: boolean;
  hasDuplicatePostingCandidate?: boolean;
  rationale?: string | null;
  correlationId?: string | null;
  evidenceLinks?: OperationsEvidenceLink[] | null;
}

export interface OperationsLedgerPostRequest {
  expectedVersion: number;
  actor: string;
  ledgerBatchId: string;
  postingKind: string;
  periodOpen: boolean;
  hasValidatedJournal?: boolean;
  hasDuplicatePostingCandidate?: boolean;
  rationale?: string | null;
  correlationId?: string | null;
  evidenceLinks?: OperationsEvidenceLink[] | null;
  journalCandidate?: Record<string, unknown> | null;
  actionOrigin?: OperationsActionOrigin | null;
}

export interface OperationsReconciliationRunRequest {
  expectedVersion: number;
  actor: string;
  rationale?: string | null;
  correlationId?: string | null;
  breakCases?: OperationsBreakCase[] | null;
  evidenceLinks?: OperationsEvidenceLink[] | null;
  securityCoverageIssueCount?: number | null;
  securityAccountingIssueCount?: number | null;
  expectedAccountingEventCount?: number | null;
  expectedJournalPreviewCount?: number | null;
  sourceRunId?: string | null;
  reconciliationRunId?: string | null;
  bankEntityId?: string | null;
  amountTolerance?: number | null;
  maxAsOfDriftMinutes?: number | null;
  reconciliationLanes?: OperationsReconciliationLaneSummary[] | null;
}

export interface OperationsSubmitApprovalRequest {
  expectedVersion: number;
  actor: string;
  reviewer: string;
  rationale: string;
  reportPackId: string;
  correlationId?: string | null;
  evidenceLinks?: OperationsEvidenceLink[] | null;
  checklistControlApprovals?: OperationsChecklistControlApproval[] | null;
  actionOrigin?: OperationsActionOrigin | null;
}

export interface OperationsApprovalDecisionRequest {
  expectedVersion: number;
  actor: string;
  reviewer: string;
  rationale: string;
  reportPackId: string;
  correlationId?: string | null;
  evidenceLinks?: OperationsEvidenceLink[] | null;
  checklistControlApprovals?: OperationsChecklistControlApproval[] | null;
  actionOrigin?: OperationsActionOrigin | null;
}

export interface OperationsRejectWorkflowRequest {
  expectedVersion: number;
  actor: string;
  reviewer: string;
  rationale: string;
  reasonCode: string;
  correlationId?: string | null;
  evidenceLinks?: OperationsEvidenceLink[] | null;
  actionOrigin?: OperationsActionOrigin | null;
}

export interface OperationsAssignBreakCaseRequest {
  expectedVersion: number;
  actor: string;
  owner: string;
  rationale: string;
  escalationLevel?: string | null;
  escalationReason?: string | null;
  dueDate?: string | null;
  correlationId?: string | null;
  evidenceLinks?: OperationsEvidenceLink[] | null;
  actionOrigin?: OperationsActionOrigin | null;
}

export interface OperationsResolveBreakCaseRequest {
  expectedVersion: number;
  actor: string;
  resolutionStatus: string;
  rationale: string;
  correlationId?: string | null;
  evidenceLinks?: OperationsEvidenceLink[] | null;
  actionOrigin?: OperationsActionOrigin | null;
}

export interface OperationsCloseWorkflowRequest {
  expectedVersion: number;
  actor: string;
  rationale: string;
  reportPackId: string;
  checklistControlApprovals?: OperationsChecklistControlApproval[] | null;
  correlationId?: string | null;
  evidenceLinks?: OperationsEvidenceLink[] | null;
  closePackageId?: string | null;
  closePackageManifestId?: string | null;
  closePackageEvidenceHash?: string | null;
  closePackageRetainedManifestRoute?: string | null;
  actionOrigin?: OperationsActionOrigin | null;
  documentSnapshots?: EvidenceDocument[] | null;
}

export interface OperationsReopenWorkflowRequest {
  expectedVersion: number;
  actor: string;
  rationale: string;
  incidentId: string;
  isGovernedAdmin: boolean;
  justification?: string | null;
  approvalReference?: string | null;
  impactSummary?: string | null;
  correlationId?: string | null;
  evidenceLinks?: OperationsEvidenceLink[] | null;
  actionOrigin?: OperationsActionOrigin | null;
}

export interface OperationsChecklistAcknowledgeRequest {
  expectedVersion: number;
  actor: string;
  rationale: string;
  correlationId?: string | null;
}

export interface OperationsTransitionResult {
  success: boolean;
  workflow: OperationsContinuityWorkflow;
  blockers: OperationsWorkflowBlocker[];
  message: string | null;
}

export interface OperationsCloseChecklistTask {
  taskId: string;
  gate: OperationsGateKey;
  label: string;
  owner: string;
  requiredEvidence: string;
  dueDate: string | null;
  requiredApprovalCount: number;
  expiresOn: string | null;
  status: string;
  blockingReason: string | null;
  evidencePointer: string | null;
  remediationRoute: string | null;
  canAcknowledge: boolean;
  acknowledgedAtUtc: string | null;
  acknowledgedBy: string | null;
}

export interface OperationsClosePackagePublication {
  closePackageId: string;
  reportPackId: string;
  retainedManifestId: string;
  retainedManifestRoute: string;
  evidenceHash: string;
  publishedAtUtc: string;
  publishedBy: string;
  signOffRationale: string;
  evidenceLinks: OperationsEvidenceLink[];
  checklistControlApprovals: OperationsChecklistControlApproval[];
  documentSnapshots?: EvidenceDocument[];
}

export interface OperationsChecklistControlApproval {
  taskId: string;
  approvedBy: string;
  approvedAtUtc: string;
}

export interface OperationsCloseReadiness {
  isReadyToClose: boolean;
  severity: string;
  score: number;
  components: OperationsCloseReadinessComponent[];
  blockers: OperationsCloseReadinessBlocker[];
  nextActions: OperationsNextAction[];
}

export interface OperationsCloseReadinessComponent {
  key: string;
  label: string;
  score: number;
  weight: number;
  isReady: boolean;
  severity: string;
  blockingReason: string | null;
  gate: OperationsGateKey | null;
  routeHint: string | null;
}

export interface OperationsCloseReadinessBlocker {
  code: string;
  category: string;
  severity: string;
  message: string;
  gate: OperationsGateKey | null;
  routeHint: string | null;
}

export interface OperationsApprovalPolicyMatrix {
  policyId: string;
  version: string;
  generatedAtUtc: string;
  rows: OperationsApprovalPolicyMatrixRow[];
}

export interface OperationsApprovalPolicyMatrixRow {
  policyKey: string;
  workflowArea: string;
  action: string;
  gate: OperationsGateKey;
  trigger: string;
  requiredPermission: string;
  submitterRole: string;
  reviewerRole: string;
  requiredDistinctApprovals: number;
  requiresIndependentReviewer: boolean;
  requiresReportPack: boolean;
  requiresChecklistControlApprovals: boolean;
  evidenceRequirement: string;
  auditEventType: string;
  route: string;
  severity: string;
}

export interface OperationsApprovalPolicyRuleUpsertRequest extends OperationsApprovalPolicyMatrixRow {
  requestedBy: string;
  rationale: string;
  correlationId?: string | null;
}

export interface OperationsApprovalPolicyRuleAuditEvent {
  auditId: string;
  eventType: string;
  occurredAtUtc: string;
  actor: string;
  rationale: string;
  correlationId: string;
  policyKey: string;
  action: string;
  gate: OperationsGateKey;
  requiredDistinctApprovals: number;
  requiresIndependentReviewer: boolean;
  requiresReportPack: boolean;
  requiresChecklistControlApprovals: boolean;
}

export interface OperationsApprovalPolicyRuleUpsertResult {
  rule: OperationsApprovalPolicyMatrixRow;
  matrix: OperationsApprovalPolicyMatrix;
  auditEvent: OperationsApprovalPolicyRuleAuditEvent;
}

export interface OperationsCloseCalendar {
  generatedAtUtc: string;
  items: OperationsCloseCalendarItem[];
}

export interface OperationsCloseCalendarItem {
  workflowId: string;
  fundAccountId: string;
  periodId: string;
  status: OperationsWorkflowStatus;
  version: number;
  nextDueDate: string | null;
  nextDueTaskId: string | null;
  nextDueLabel: string | null;
  nextDueOwner: string | null;
  readinessSeverity: string | null;
  readinessScore: number | null;
  isReadyToClose: boolean;
  blockerCount: number;
  openChecklistCount: number;
  requiredApprovalCount: number;
  completedApprovalCount: number;
  route: string;
  readinessComponents?: Record<string, unknown>[] | null;
  readinessBlockers?: OperationsCloseReadinessBlocker[] | null;
  readinessNextActions?: OperationsNextAction[] | null;
}

export interface OperationsCloseCalendarItemUpsertRequest {
  workflowId: string;
  taskId: string;
  dueDate: string;
  owner: string;
  requestedBy: string;
  rationale: string;
  correlationId?: string | null;
}

export interface OperationsCloseCalendarItemAuditEvent {
  auditId: string;
  eventType: string;
  occurredAtUtc: string;
  actor: string;
  rationale: string;
  correlationId: string;
  workflowId: string;
  fundAccountId: string;
  periodId: string;
  taskId: string;
  dueDate: string;
  owner: string;
}

export interface OperationsCloseCalendarItemUpsertResult {
  item: OperationsCloseCalendarItem;
  calendar: OperationsCloseCalendar;
  auditEvent: OperationsCloseCalendarItemAuditEvent;
}

export interface FinancialOperationsCommandCenterMetric {
  metricId: string;
  label: string;
  value: string;
  detail: string;
  status: string;
  routeHint: string | null;
}

export interface FinancialOperationsQueueRow {
  queueId: string;
  sourceKind: string;
  kindLabel: string;
  title: string;
  statusLabel: string;
  detail: string;
  ownerLabel: string;
  dueLabel: string;
  evidenceLabel: string;
  actionLabel: string;
  routeHint: string | null;
  isBlocked: boolean;
  sortOrder: number;
  workflowId?: string | null;
  evidenceLinks: OperationsEvidenceLink[];
  severityLabel: string;
  slaLabel: string;
  blockerType: string;
  closeReportImpact: string;
}

export interface FinancialOperationsCloseSupportDecisionRow {
  decisionId: string;
  category: string;
  label: string;
  status: string;
  isBlocking: boolean;
  detail: string;
  requiredAction: string;
  routeHint: string | null;
  evidenceLinks: OperationsEvidenceLink[];
}

export interface FinancialOperationsCloseSupportDecision {
  decisionId: string;
  status: string;
  isReady: boolean;
  summary: string;
  periodState: string;
  lockReopenPosture: string;
  navReportDependencyPosture: string;
  unresolvedExceptionCount: number;
  pendingApprovalCount: number;
  retainedEvidenceGapCount: number;
  decisions: FinancialOperationsCloseSupportDecisionRow[];
}

export interface FinancialOperationsCommandCenter {
  generatedAtUtc: string;
  fundProfileId: string | null;
  ledgerBookId: string | null;
  fundAccountId: string | null;
  periodId: string | null;
  status: string;
  isReadyToComplete: boolean;
  summary: string;
  activeItemCount: number;
  blockedItemCount: number;
  reviewItemCount: number;
  metrics: FinancialOperationsCommandCenterMetric[];
  queueRows: FinancialOperationsQueueRow[];
  activeWorkflow?: OperationsContinuityWorkflow | null;
  closeCalendar?: OperationsCloseCalendar | null;
  privateCapitalCloseCockpit?: PrivateCapitalCloseCockpit | null;
  closeSupportDecision?: FinancialOperationsCloseSupportDecision | null;
}

export type EvidenceStatus = "Unknown" | "Ready" | "ReviewRequired" | "Blocked" | "Stale" | "Missing";

export interface EvidenceSubject {
  subjectId: string;
  subjectKind: string;
  label: string;
  workspace: string;
  route: string | null;
  pageTag: string;
}

export interface EvidenceFreshness {
  asOf: string | null;
  isStale: boolean;
  reason: string | null;
}

export interface EvidenceArtifactRef {
  artifactId: string;
  kind: string;
  path: string | null;
  route: string | null;
  generatedAt: string;
  hash: string | null;
  retained: boolean;
  canonicalSubjectKind?: string | null;
  canonicalSubjectId?: string | null;
  capture?: EvidenceArtifactCapture | null;
  extractedFields?: EvidenceArtifactExtractionField[];
}

export interface EvidenceArtifactCapture {
  captureChannel: string;
  sourceSystem: string | null;
  receivedAt: string | null;
  receivedBy: string | null;
  sourceReference: string | null;
  receiptHash: string | null;
}

export interface EvidenceArtifactExtractionField {
  fieldName: string;
  extractedValue: string | null;
  expectedValue: string | null;
  confidenceScore: number | null;
  reviewState: string;
  validationStatus: EvidenceStatus;
  validationMessage: string | null;
  linkedRecordKind: string | null;
  linkedRecordId: string | null;
}

export interface EvidenceNode {
  evidenceId: string;
  subject: EvidenceSubject;
  kind: string;
  status: EvidenceStatus;
  freshness: EvidenceFreshness;
  sourceSystem: string;
  summary: string;
  artifactRefs: EvidenceArtifactRef[];
  relatedWorkItemIds: string[];
  metadata?: Record<string, string> | null;
}

export interface EvidenceEdge {
  fromId: string;
  toId: string;
  relationship: string;
  reason: string;
}

export interface EvidenceCompleteness {
  score: number;
  status: EvidenceStatus;
  requiredIds: string[];
  readyIds: string[];
  missingIds: string[];
  staleIds: string[];
  blockingWorkItemIds: string[];
  validationIssues?: EvidenceValidationIssue[];
  blockingIssueCount?: number;
  warningIssueCount?: number;
  orphanEvidenceIds?: string[];
  slaPolicies?: EvidenceSlaPolicy[];
  slaAssessments?: EvidenceSlaAssessment[];
  assuranceScore?: MeridianAssuranceScore;
}

export type EvidenceValidationSeverity = "Info" | "Warning" | "Critical";

export interface EvidenceValidationIssue {
  code: string;
  severity: EvidenceValidationSeverity;
  message: string;
  evidenceId?: string | null;
  evidenceKind?: string | null;
  sourceSystem?: string | null;
  relatedWorkItemId?: string | null;
}

export interface EvidenceSlaPolicy {
  policyId: string;
  evidenceKind: string;
  workflowKind: string;
  freshnessMinutes: number;
  breachSeverity: "Info" | "Warning" | "Critical";
  requiredForAssurance: boolean;
  description: string;
}

export interface EvidenceSlaAssessment {
  policyId: string;
  evidenceId: string;
  evidenceKind: string;
  sourceSystem: string;
  ageMinutes: number | null;
  freshnessMinutes: number;
  isBreached: boolean;
  severity: "Info" | "Warning" | "Critical";
  message: string;
}

export interface EvidenceAssuranceComponent {
  componentId: string;
  label: string;
  score: number;
  status: EvidenceStatus;
  detail: string;
}

export interface MeridianAssuranceScore {
  score: number;
  status: EvidenceStatus;
  components: EvidenceAssuranceComponent[];
  slaAssessments: EvidenceSlaAssessment[];
}

export type EvidenceProofChainLayerKind =
  | "Unknown"
  | "Source"
  | "Normalization"
  | "Reconciliation"
  | "Ledger"
  | "CapitalAccounts"
  | "Close"
  | "Reporting"
  | "Delivery"
  | "Audit";

export interface EvidenceProofChainLayer {
  layer: EvidenceProofChainLayerKind;
  label: string;
  status: EvidenceStatus;
  coveragePercent: number;
  requiredEvidenceIds: string[];
  evidenceIds: string[];
  readyEvidenceIds: string[];
  reviewEvidenceIds: string[];
  missingEvidenceIds: string[];
  evidenceKinds: string[];
  summary: string;
}

export interface EvidenceProofChain {
  coveragePercent: number;
  status: EvidenceStatus;
  coveredLayerCount: number;
  totalLayerCount: number;
  layers: EvidenceProofChainLayer[];
  summary: string;
}

export interface EvidencePacket {
  subject: EvidenceSubject;
  generatedAt: string;
  nodes: EvidenceNode[];
  edges: EvidenceEdge[];
  completeness: EvidenceCompleteness;
  actions: WorkflowAction[];
  warnings: string[];
  proofChain?: EvidenceProofChain;
}

export interface EvidenceGraph {
  subject: EvidenceSubject;
  generatedAt: string;
  nodes: EvidenceNode[];
  edges: EvidenceEdge[];
  warnings: string[];
  proofChain?: EvidenceProofChain;
}

export interface EvidenceTemplateExportSettings {
  schemaVersion: number;
  manifestOnly: boolean;
  defaultFormat: string;
}

export interface EvidenceTemplate {
  workflowId: string;
  requiredEvidenceKinds: string[];
  optionalEvidenceKinds: string[];
  noOrphanRule: boolean;
  exportSettings: EvidenceTemplateExportSettings;
}

export interface EvidencePacketExportRequest {
  requestedBy?: string | null;
  reason?: string | null;
  includeWarnings?: boolean;
  lifecycle?: EvidenceLifecycleMetadata | null;
  linkage?: EvidenceSubjectLinkage | null;
}

export interface EvidencePacketExportResponse {
  subjectKind: string;
  subjectId: string;
  generatedAt: string;
  manifestPath: string;
  manifestRoute: string;
  evidenceCount: number;
  warningCount: number;
  retained: boolean;
  vaultIdentity?: EvidenceVaultIdentity | null;
}

export interface EvidenceVaultIdentity {
  vaultId: string;
  subjectKind: string;
  subjectId: string;
  manifestPath: string;
  manifestRoute: string;
  retainedAt: string;
  contentHashSha256: string;
  schemaVersion: number;
  storageKind: string;
  artifacts: EvidenceVaultArtifact[];
  requestLists?: EvidenceRequestList[];
  supportRequests: EvidenceSupportRequest[];
  documents?: EvidenceDocument[];
  manifestSnapshot?: EvidenceManifest | null;
}

export interface EvidenceVaultArtifact {
  artifactId: string;
  kind: string;
  relativePath: string;
  contentHashSha256: string;
  sizeBytes: number;
  retainedAt: string;
  sourcePath: string | null;
  sourceRoute: string | null;
  canonicalSubjectKind: string | null;
  canonicalSubjectId: string | null;
  capture?: EvidenceArtifactCapture | null;
  extractedFields?: EvidenceArtifactExtractionField[];
  document?: EvidenceDocument | null;
}

export type EvidenceDocumentClassification =
  | "Unknown"
  | "Statement"
  | "Invoice"
  | "CapitalNotice"
  | "CustodianFile"
  | "BankEvidence"
  | "ValuationSupport"
  | "Agreement"
  | "TaxSupport"
  | "AuditRequestSupport"
  | "BankStatement"
  | "AdminPackage"
  | "TaxAuditSupport";

export type EvidenceExtractionStatus = "NotExtracted" | "Extracted" | "NeedsReview" | "Accepted" | "Rejected" | "Pending";

export type EvidenceDocumentIntakeChannel =
  | "Unknown"
  | "Upload"
  | "Email"
  | "Sftp"
  | "Api"
  | "PortalDownload"
  | "LocalFile"
  | "ImportedFileReference";

export type EvidenceDocumentIntakeSourceKind =
  | "UploadedContent"
  | "LocalFile"
  | "ImportedFileReference"
  | "Email"
  | "Sftp"
  | "Api"
  | "PortalDownload";

export type EvidenceDocumentLinkKind =
  | "Unknown"
  | "Period"
  | "Portfolio"
  | "Account"
  | "Instrument"
  | "Journal"
  | "ReconciliationCase"
  | "ReportLine"
  | "CloseTask"
  | "Fund"
  | "StatementRun"
  | "StatementImport";

export type EvidenceDocumentReviewStatus = "Unreviewed" | "NeedsReview" | "Accepted" | "Rejected";

export interface EvidenceDocumentLink {
  linkKind: EvidenceDocumentLinkKind;
  objectId: string;
  label?: string | null;
  route?: string | null;
  relationship?: string | null;
}

export interface EvidenceDocumentReviewState {
  status: EvidenceDocumentReviewStatus;
  reviewer?: string | null;
  reviewedAt?: string | null;
  notes?: string | null;
  confirmedFields?: EvidenceDocumentConfirmedField[];
}

export interface EvidenceDocumentConfirmedField {
  fieldName: string;
  confirmedValue: string;
  confirmedBy: string;
  confirmedAt: string;
  sourceFieldName?: string | null;
  notes?: string | null;
}

export interface EvidenceDocumentAuditEvent {
  recordedAt: string;
  actor: string;
  action: string;
  summary: string;
  correlationId?: string | null;
}

export interface EvidenceDocumentAuthority {
  canSupport: boolean;
  canBlock: boolean;
  canSuggest: boolean;
  canLink: boolean;
  canApprove: boolean;
  canPost: boolean;
  canCertify: boolean;
  canRelease: boolean;
  boundary: string;
}

export interface EvidenceDocumentSourceRecord {
  sourceHashSha256: string;
  receivedAt: string;
  sourceChannel: string;
  channelKind?: EvidenceDocumentIntakeChannel | null;
  actor?: string | null;
  tenantId?: string | null;
  scope?: string | null;
  sourceSystem?: string | null;
  sourceReference?: string | null;
  receiptHash?: string | null;
}

export interface EvidenceDocumentIntakeSource {
  sourceKind: EvidenceDocumentIntakeSourceKind;
  path?: string | null;
  uri?: string | null;
  displayName?: string | null;
  expectedContentHashSha256?: string | null;
}

export interface EvidenceDocument {
  documentId: string;
  fileName: string;
  classification: EvidenceDocumentClassification;
  sourceHashSha256: string;
  receivedAt: string;
  sourceChannel: string;
  actor?: string | null;
  tenantId?: string | null;
  scope?: string | null;
  extractionStatus: EvidenceExtractionStatus;
  objectLinks: EvidenceDocumentLink[];
  reviewerState: EvidenceDocumentReviewState;
  auditTrail: EvidenceDocumentAuditEvent[];
  contentType?: string | null;
  sourceSystem?: string | null;
  sourceReference?: string | null;
  vaultId?: string | null;
  artifactId?: string | null;
  manifestRoute?: string | null;
  extractorId?: string | null;
  channelKind?: EvidenceDocumentIntakeChannel | null;
  sourceRecord?: EvidenceDocumentSourceRecord | null;
  extractedFields?: EvidenceArtifactExtractionField[];
  authority?: EvidenceDocumentAuthority | null;
}

export interface EvidenceVaultIntakeRequest {
  subjectKind: string;
  subjectId: string;
  intakeChannel: string;
  fileName: string;
  contentBase64?: string | null;
  contentType?: string | null;
  sourceSystem?: string | null;
  sourceReference?: string | null;
  receivedBy?: string | null;
  expectedContentHashSha256?: string | null;
  extractedFields?: EvidenceArtifactExtractionField[] | null;
  linkage?: EvidenceSubjectLinkage | null;
  lifecycle?: EvidenceLifecycleMetadata | null;
  classification?: EvidenceDocumentClassification;
  actor?: string | null;
  tenantId?: string | null;
  scope?: string | null;
  extractionStatus?: EvidenceExtractionStatus | null;
  intakeChannelKind?: EvidenceDocumentIntakeChannel | null;
  extractorId?: string | null;
  reviewerState?: EvidenceDocumentReviewState | null;
  objectLinks?: EvidenceDocumentLink[];
  intakeSource?: EvidenceDocumentIntakeSource | null;
}

export interface EvidenceVaultIntakeResponse {
  intakeId: string;
  subjectKind: string;
  subjectId: string;
  intakeChannel: string;
  fileName: string;
  relativePath: string;
  contentHashSha256: string;
  sizeBytes: number;
  capturedAt: string;
  capture: EvidenceArtifactCapture;
  extractedFields: EvidenceArtifactExtractionField[];
  vaultIdentity: EvidenceVaultIdentity;
  document?: EvidenceDocument | null;
}

export interface EvidenceSupportRequest {
  requestId: string;
  requestKind: string;
  evidenceId: string;
  evidenceKind: string | null;
  severity: EvidenceValidationSeverity;
  status: string;
  summary: string;
  sourceSystem: string | null;
  workItemId: string | null;
  blockedOutput: string | null;
}

export interface EvidenceRequest {
  requestId: string;
  requestKind: string;
  severity: EvidenceValidationSeverity;
  status: string;
  summary: string;
  targetKind?: string | null;
  targetId?: string | null;
  blockedOutput?: string | null;
}

export type EvidenceManifestPackageKindCode =
  | "Unknown"
  | "EvidencePacket"
  | "CloseBinder"
  | "AuditPacket"
  | "ReportSupportPackage"
  | "TaxSupportPackage"
  | "OperationalEventSupportPackage";

export interface EvidenceManifest {
  manifestId: string;
  frozenAt: string;
  packageKind: string;
  packageId: string;
  contentHashSha256: string;
  documents: EvidenceDocument[];
  requests: EvidenceRequest[];
  objectLinks: EvidenceDocumentLink[];
  packageKindCode?: EvidenceManifestPackageKindCode | null;
}

export type EvidenceRequestListKindCode =
  | "Unknown"
  | "Evidence"
  | "Close"
  | "Audit"
  | "Tax"
  | "ReportPackage"
  | "OperationalEvent";

export interface EvidenceRequestList {
  requestListId: string;
  requestListKind: string;
  targetKind: string;
  targetId: string;
  highestSeverity: EvidenceValidationSeverity;
  status: string;
  requestCount: number;
  requestIds: string[];
  evidenceKinds: string[];
  blockedOutputs: string[];
  summary: string;
  requestListKindCode?: EvidenceRequestListKindCode | null;
}

export interface EvidenceVaultRequestListQuery {
  requestListKind?: string | null;
  requestListKindCode?: EvidenceRequestListKindCode | null;
  targetKind?: string | null;
  targetId?: string | null;
  status?: string | null;
  subjectKind?: string | null;
  subjectId?: string | null;
  maxResults?: number | null;
}

export interface EvidenceVaultRequestListEntry extends EvidenceRequestList {
  openRequestCount: number;
  vaultId: string;
  subjectKind: string;
  subjectId: string;
  manifestRoute: string;
  retainedAt: string;
  supportRequests: EvidenceSupportRequest[];
}

export interface EvidenceVaultDocumentQuery {
  classification?: EvidenceDocumentClassification | null;
  channelKind?: EvidenceDocumentIntakeChannel | null;
  extractionStatus?: EvidenceExtractionStatus | null;
  reviewStatus?: EvidenceDocumentReviewStatus | null;
  linkKind?: EvidenceDocumentLinkKind | null;
  objectId?: string | null;
  subjectKind?: string | null;
  subjectId?: string | null;
  tenantId?: string | null;
  scope?: string | null;
  maxResults?: number | null;
}

export interface EvidenceVaultDocumentEntry {
  document: EvidenceDocument;
  vaultId: string;
  subjectKind: string;
  subjectId: string;
  manifestRoute: string;
  retainedAt: string;
  storageKind: string;
  openRequestCount: number;
  supportRequests: EvidenceSupportRequest[];
}

export interface EvidenceVaultDocumentReviewRequest {
  status: EvidenceDocumentReviewStatus;
  reviewer: string;
  notes?: string | null;
  extractionStatus?: EvidenceExtractionStatus | null;
  correlationId?: string | null;
  confirmedFields?: EvidenceDocumentConfirmedField[];
}

export interface EvidenceVaultDocumentReviewResponse {
  entry: EvidenceVaultDocumentEntry;
  auditEvent: EvidenceDocumentAuditEvent;
}

export interface EvidenceLifecycleMetadata {
  retainUntil?: string | null;
  legalHold: boolean;
  expiresAt?: string | null;
  accessPolicyTags: string[];
}

export interface EvidenceSubjectLinkage {
  evidenceSubject?: string | null;
  runId?: string | null;
  periodId?: string | null;
  reportPackId?: string | null;
  reconciliationCaseId?: string | null;
  accountingRecordId?: string | null;
  reportPackDeliveryAttemptId?: string | null;
  reportPackDeliveryPackageId?: string | null;
}

export interface EvidenceVaultLookupRequest {
  evidenceSubject?: string | null;
  runId?: string | null;
  periodId?: string | null;
  reportPackId?: string | null;
  reconciliationCaseId?: string | null;
  accountingRecordId?: string | null;
  reportPackDeliveryAttemptId?: string | null;
  reportPackDeliveryPackageId?: string | null;
}

export type ChiefOfStaffIntentKind =
  | "Unknown"
  | "AccountingReconciliationReview"
  | "TradingReadinessReview"
  | "ReportPackApproval"
  | "GovernanceAuditTrailReview"
  | "GeneralOperatorAssistance";

export type ChiefOfStaffSessionStatus =
  | "Created"
  | "ReviewRequired"
  | "Blocked"
  | "AwaitingOperatorDecision"
  | "Approved"
  | "Rejected"
  | "Deferred";

export type ChiefOfStaffDecisionKind = "Approve" | "Reject" | "Defer";

export type ChiefOfStaffRuntimeHealthStatus = "Healthy" | "Degraded" | "Unavailable";

export interface ChiefOfStaffActionCandidate {
  actionId: string;
  label: string;
  targetWorkflow: string;
  targetRoute: string | null;
  targetPageTag: string | null;
  requiredSignoffRole: string | null;
  approvalRequired: boolean;
  impactSummary: string;
  evidencePrerequisites: string[];
}

export interface ChiefOfStaffRecommendation {
  recommendationId: string;
  title: string;
  detail: string;
  tone: OperatorWorkItemTone;
  actions: ChiefOfStaffActionCandidate[];
}

export interface ChiefOfStaffTraceSummary {
  traceId: string;
  runtimeName: string;
  runtimeVersion: string | null;
  capturedAt: string;
  warningCodes: string[];
}

export interface ChiefOfStaffEvidenceBundle {
  subjects: EvidenceSubject[];
  packets: EvidencePacket[];
  workItems: OperatorWorkItem[];
  relatedReconciliationRunIds: string[];
  relatedWorkflowIds: string[];
  traceArtifacts: EvidenceArtifactRef[];
  completenessStatus: EvidenceStatus;
  warnings: string[];
}

export interface ChiefOfStaffSession {
  sessionId: string;
  intentKind: ChiefOfStaffIntentKind;
  operatorRequest: string;
  markdownSummary: string;
  structuredPayload: unknown;
  evidenceBundle: ChiefOfStaffEvidenceBundle;
  recommendations: ChiefOfStaffRecommendation[];
  actions: ChiefOfStaffActionCandidate[];
  traceSummary: ChiefOfStaffTraceSummary;
  freshnessAsOf: string;
  status: ChiefOfStaffSessionStatus;
  pendingApproval: boolean;
  routedWorkflowReferences: string[];
  warnings: string[];
}

export interface ChiefOfStaffSessionSummary {
  sessionId: string;
  intentKind: ChiefOfStaffIntentKind;
  operatorRequest: string;
  status: ChiefOfStaffSessionStatus;
  freshnessAsOf: string;
  pendingApproval: boolean;
  warnings: string[];
}

export interface ChiefOfStaffSessionQuery {
  workspace?: string;
  fundProfileId?: string;
  fundAccountId?: string;
  status?: ChiefOfStaffSessionStatus;
  limit?: number;
}

export interface ChiefOfStaffDecisionRequest {
  decision: ChiefOfStaffDecisionKind;
  actor: string;
  selectedActionId?: string | null;
  rationale?: string | null;
  correlationId?: string | null;
}

export interface ChiefOfStaffRuntimeHealth {
  status: ChiefOfStaffRuntimeHealthStatus;
  detail: string;
  checkedAt: string;
}

export interface ChiefOfStaffTraceExportRequest {
  requestedBy: string;
  reason?: string | null;
  includeWarnings?: boolean;
}

export interface ChiefOfStaffEvidenceExport {
  sessionId: string;
  manifest: EvidencePacketExportResponse;
  traceSummary: ChiefOfStaffTraceSummary;
}

export interface TradingAcceptanceGate {
  gateId: string;
  label: string;
  status: TradingAcceptanceGateStatus;
  detail: string;
  sessionId: string | null;
  runId: string | null;
  auditReference: string | null;
  reason?: string | null;
  lastEvidenceAt?: string | null;
  requiredNextAction?: string | null;
}

export interface TradingExecutionReconciliationBreak {
  kind: string;
  description: string;
  localOrderId: string | null;
  brokerOrderId: string | null;
  clientOrderId: string | null;
  symbol: string | null;
  localValue: string | null;
  brokerValue: string | null;
}

export interface TradingExecutionReconciliationReadiness {
  status: TradingAcceptanceGateStatus;
  gatewayId: string;
  brokerDisplayName: string;
  brokerHealthy: boolean;
  brokerConnected: boolean;
  matchedOpenOrderCount: number;
  breakCount: number;
  reconciledAt: string;
  detail: string;
  breaks: TradingExecutionReconciliationBreak[];
}

export interface TradingLiveOperationRequirement {
  requirementId: string;
  label: string;
  status: TradingAcceptanceGateStatus;
  detail: string;
  checklistItem: string;
  evidenceReference: string | null;
  checklistSatisfied: boolean;
  evidenceSatisfied: boolean;
  blockerCode?: string | null;
}

export interface TradingPaperSessionReadiness {
  sessionId: string;
  strategyId: string;
  strategyName: string | null;
  isActive: boolean;
  initialCash: number;
  createdAt: string;
  closedAt: string | null;
  symbolCount: number;
  orderCount: number;
  positionCount: number;
  portfolioValue: number | null;
}

export interface TradingReplayReadiness {
  sessionId: string;
  replaySource: string;
  isConsistent: boolean;
  comparedFillCount: number;
  comparedOrderCount: number;
  comparedLedgerEntryCount: number;
  verifiedAt: string;
  lastPersistedFillAt: string | null;
  lastPersistedOrderUpdateAt: string | null;
  verificationAuditId: string | null;
  mismatchReasons: string[];
}

export interface TradingControlReadiness {
  circuitBreakerOpen: boolean;
  circuitBreakerReason: string | null;
  circuitBreakerChangedBy: string | null;
  circuitBreakerChangedAt: string | null;
  manualOverrideCount: number;
  symbolLimitCount: number;
  defaultMaxPositionSize: number | null;
}

export interface TradingPromotionReadiness {
  state: string;
  reason: string;
  requiresReview: boolean;
  sourceRunId: string | null;
  targetRunId: string | null;
  suggestedNextMode: string | null;
  auditReference: string | null;
  approvalStatus: string | null;
  manualOverrideId: string | null;
  approvedBy: string | null;
  approvalChecklist?: string[] | null;
  evidenceReferences?: string[] | null;
}

export interface TradingOperatorSignoffReadiness {
  status: string;
  requiredBeforeDk1Exit: boolean;
  requiredOwners: string[];
  signedOwners: string[];
  missingOwners: string[];
  completedAt: string | null;
  sourcePath: string | null;
}

export interface TradingTrustGateReadiness {
  gateId: string;
  status: string;
  readyForOperatorReview: boolean;
  operatorSignoffRequired: boolean;
  operatorSignoffStatus: string;
  generatedAt: string | null;
  packetPath: string | null;
  sourceSummary: string | null;
  requiredSampleCount: number;
  readySampleCount: number;
  validatedEvidenceDocumentCount: number;
  requiredOwners: string[];
  blockers: string[];
  detail: string;
  operatorSignoff: TradingOperatorSignoffReadiness | null;
}

export interface WorkstationBrokerageSyncStatus {
  fundAccountId: string;
  providerId: string | null;
  externalAccountId: string | null;
  health: "Unlinked" | "Healthy" | "Stale" | "Degraded" | "Failed";
  isLinked: boolean;
  isStale: boolean;
  lastAttemptedSyncAt: string | null;
  lastSuccessfulSyncAt: string | null;
  lastError: string | null;
  positionCount: number;
  openOrderCount: number;
  fillCount: number;
  cashTransactionCount: number;
  securityMissingCount: number;
  warnings: string[];
  accountKind?: BrokerageAccountKind;
}

export type BrokerageAccountKind = "Unknown" | "TaxableBrokerage" | "RothIra" | "TraditionalIra";

export type BrokerageConnectionState =
  | "NotConfigured"
  | "Disconnected"
  | "AuthorizationPending"
  | "Connected"
  | "ReauthorizationRequired"
  | "Degraded";

export interface BrokerageConnectionStatus {
  providerId: string;
  displayName: string;
  state: BrokerageConnectionState;
  isConfigured: boolean;
  isConnected: boolean;
  authorizationUrl: string | null;
  connectedAt: string | null;
  expiresAt: string | null;
  lastError: string | null;
  warnings: string[];
  scopes: string[];
  environment?: "paper" | "live" | string | null;
  externalAccountId?: string | null;
  verifiedAt?: string | null;
  maskedKeyId?: string | null;
}

export interface AlpacaBrokerageConnectionRequest {
  keyId: string;
  secretKey: string;
  environment: "paper" | "live";
}

export type ProviderConnectionCapability = "Data" | "Brokerage" | "DataAndBrokerage" | "AccountingSystem";
export type ProviderCredentialState = "NotRequired" | "Missing" | "Partial" | "Configured" | "Verified" | "Invalid";
export type ProviderCredentialSource =
  | "None"
  | "LocalEncryptedStore"
  | "Environment"
  | "ExternalVaultReference"
  | "NotRequired";
export type ProviderVerificationState = "NotRequired" | "NotVerified" | "Verified" | "Failed" | "Stale";
export type ProviderContinuityHealth = "Unknown" | "Healthy" | "Warning" | "Degraded" | "Blocked";
export type ProviderReadinessStatus = "Ready" | "Review" | "Degraded" | "Blocked" | "Unknown";
export type ProviderReadinessEvidenceKind = "Credential" | "Connection" | "Validation" | "Degradation" | "Plaid" | "Routing";
export type ProviderCredentialInputKind = "Text" | "Password" | "Url";

export interface ProviderCredentialFieldMetadata {
  name: string;
  label: string;
  required: boolean;
  inputKind: ProviderCredentialInputKind;
  placeholder?: string | null;
  helpText?: string | null;
}

export interface ProviderEnvironmentOption {
  value: string;
  label: string;
  isDefault: boolean;
  helpText?: string | null;
}

export interface ProviderConnectionRow {
  providerId: string;
  displayName: string;
  capability: ProviderConnectionCapability;
  credentialState: ProviderCredentialState;
  credentialSource: ProviderCredentialSource;
  verificationState: ProviderVerificationState;
  health: ProviderContinuityHealth;
  fallbackActive: boolean;
  lastVerifiedAt: string | null;
  lastSuccessfulAt: string | null;
  lastFailureAt: string | null;
  lastError: string | null;
  maskedKeyPreview: string | null;
  environment: string | null;
  externalAccountId: string | null;
  affectedWorkflows: string[];
  recommendedAction: string;
  actionHref: string;
  credentialFields?: ProviderCredentialFieldMetadata[] | null;
  environmentOptions?: ProviderEnvironmentOption[] | null;
}

export interface ProviderCredentialUpsertRequest {
  credentials: Record<string, string | null | undefined>;
  environment?: string | null;
  requestedBy?: string | null;
}

export interface ProviderCredentialMutationResult {
  providerId: string;
  credentialState: ProviderCredentialState;
  credentialSource: ProviderCredentialSource;
  verificationState: ProviderVerificationState;
  health: ProviderContinuityHealth;
  maskedKeyPreview: string | null;
  environment: string | null;
  warnings: string[];
}

export interface ProviderCredentialVerificationResult {
  providerId: string;
  success: boolean;
  verificationState: ProviderVerificationState;
  health: ProviderContinuityHealth;
  lastVerifiedAt: string | null;
  lastError: string | null;
  externalAccountId: string | null;
  warnings: string[];
}

export interface ProviderReadinessSummary {
  asOf: string;
  status: ProviderReadinessStatus;
  totalProviders: number;
  readyProviders: number;
  reviewProviders: number;
  degradedProviders: number;
  blockedProviders: number;
  summary: string;
  recommendedAction: string;
  providers: ProviderReadinessRow[];
}

export interface ProviderReadinessRow {
  providerId: string;
  displayName: string;
  capability: ProviderConnectionCapability;
  status: ProviderReadinessStatus;
  credentialState: ProviderCredentialState;
  credentialSource: ProviderCredentialSource;
  verificationState: ProviderVerificationState;
  connectionHealth: ProviderContinuityHealth;
  isEnabled: boolean;
  isConnected: boolean;
  fallbackActive: boolean;
  degradationScore: number | null;
  lastVerifiedAt: string | null;
  lastSuccessfulAt: string | null;
  lastFailureAt: string | null;
  lastError: string | null;
  maskedKeyPreview: string | null;
  environment: string | null;
  externalAccountId: string | null;
  affectedWorkflows: string[];
  recommendedAction: string;
  actionHref: string;
  evidence: ProviderReadinessEvidence[];
  recoveryActions: ProviderRecoveryAction[];
  credentialFields?: ProviderCredentialFieldMetadata[] | null;
  environmentOptions?: ProviderEnvironmentOption[] | null;
}

export interface ProviderReadinessEvidence {
  kind: ProviderReadinessEvidenceKind;
  label: string;
  status: ProviderReadinessStatus;
  detail: string;
  observedAt?: string | null;
  route?: string | null;
}

export interface ProviderRecoveryAction {
  actionId: string;
  label: string;
  target: string;
  requiresMutation: boolean;
  disabledReason?: string | null;
}

export type AccountingSystemProviderState = "Available" | "Planned" | "Disabled";
export type AccountingSystemImportState = "NotStarted" | "Previewed" | "Imported" | "Failed";
export type AccountingSystemReconciliationStatus =
  | "Matched"
  | "Variance"
  | "MissingExternal"
  | "MissingMeridian"
  | "ReviewRequired";
export type AccountingSystemEvidencePackageStatus = "Ready" | "ReviewRequired" | "Missing";
export type AccountingCertificationState = "Draft" | "ReadyForReview" | "Certified" | "Rejected" | "Superseded";
export type ExternalGlExportReconciliationSafeguardState = "MissingEvidence" | "Blocked" | "Ready" | "Certified";

export interface LedgerDimensionSet {
  fundId?: string | null;
  entityId?: string | null;
  sleeveId?: string | null;
  strategyId?: string | null;
  investorId?: string | null;
  capitalAccountId?: string | null;
  instrumentId?: string | null;
  positionId?: string | null;
  taxLotId?: string | null;
  costCenterId?: string | null;
  counterpartyId?: string | null;
  externalGlDimensions?: Record<string, string> | null;
  organizationId?: string | null;
  portfolioId?: string | null;
  bookId?: string | null;
  accountId?: string | null;
  customerId?: string | null;
  vendorId?: string | null;
  projectId?: string | null;
}

export interface DimensionMappingProfile {
  profileId: string;
  displayName: string;
  providerId: string;
  meridianDimensions: LedgerDimensionSet;
  externalDimensions: LedgerDimensionSet;
  certificationState: AccountingCertificationState;
  validationIssues: AccountingConfigurationValidationIssue[];
}
