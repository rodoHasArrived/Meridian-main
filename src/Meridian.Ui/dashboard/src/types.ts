export type WorkspaceKey =
  | "trading"
  | "portfolio"
  | "accounting"
  | "reporting"
  | "strategy"
  | "data"
  | "settings";

export type LegacyWorkspaceKey = "overview" | "research" | "data-operations" | "governance";

export interface SessionInfo {
  displayName: string;
  role: string;
  environment: "paper" | "live" | "research";
  activeWorkspace: WorkspaceKey;
  commandCount: number;
}

export interface FeatureCapabilitySettingsResponse {
  capabilities: FeatureCapabilityToggle[];
}

export interface FeatureCapabilityToggle {
  capabilityKey: string;
  displayName: string;
  description: string;
  isEnabled: boolean;
  defaultEnabled: boolean;
  isPermanent: boolean;
  isOverridden: boolean;
  canToggle: boolean;
  disabledReason: string | null;
}

export interface FeatureCapabilityToggleRequest {
  isEnabled: boolean;
}

export interface WorkspaceSummary {
  key: WorkspaceKey;
  label: string;
  description: string;
  status: string;
}

export interface MetricSnapshot {
  id: string;
  label: string;
  value: string;
  delta: string;
  tone: "default" | "success" | "warning" | "danger";
}



export interface StrategyRunSummaryApiRecord {
  runId: string;
  strategyId: string;
  strategyName: string;
  mode: number;
  engine: number;
  status: number;
  startedAt: string;
  completedAt: string | null;
  datasetReference: string | null;
  feedReference: string | null;
  portfolioId: string | null;
  ledgerReference: string | null;
  netPnl: number | null;
  totalReturn: number | null;
  finalEquity: number | null;
  fillCount: number;
  lastUpdatedAt: string;
  auditReference?: string | null;
}
export interface ResearchRunRecord {
  id: string;
  strategyName: string;
  engine: string;
  mode: "backtest" | "paper" | "live";
  status: "Running" | "Queued" | "Needs Review" | "Completed";
  dataset: string;
  window: string;
  pnl: string;
  sharpe: string;
  lastUpdated: string;
  notes: string;
  promotionState?: string | null;
  netPnl?: number | null;
  totalReturn?: number | null;
  finalEquity?: number | null;
}

// --- Promotion workflow types ---

export interface PromotionEvaluationResult {
  runId: string;
  strategyId: string | null;
  strategyName: string | null;
  sourceMode: string | null;
  targetMode: string | null;
  isEligible: boolean;
  sharpeRatio: number;
  maxDrawdownPercent: number;
  totalReturn: number;
  reason: string;
  found: boolean;
  ready: boolean;
  requiresHumanApproval?: boolean;
  requiresManualOverride?: boolean;
  requiredManualOverrideKind?: string | null;
  blockingReasons?: string[] | null;
}

export interface PromotionDecisionResult {
  success: boolean;
  promotionId: string | null;
  newRunId: string | null;
  reason: string;
  auditReference?: string | null;
  approvedBy?: string | null;
}

export interface PromotionRecord {
  promotionId: string;
  strategyId: string;
  strategyName: string;
  sourceRunType: string;
  targetRunType: string;
  runId?: string;
  sourceRunId?: string | null;
  targetRunId?: string | null;
  decision?: string | null;
  approvedBy?: string | null;
  approvalReason?: string | null;
  reviewNotes?: string | null;
  auditReference?: string | null;
  manualOverrideId?: string | null;
  qualifyingSharpe: number;
  qualifyingMaxDrawdownPercent: number;
  qualifyingTotalReturn: number;
  promotedAt: string;
}

// --- Execution / paper session types ---

export interface PaperSessionSummary {
  sessionId: string;
  strategyId: string;
  strategyName: string | null;
  initialCash: number;
  createdAt: string;
  closedAt: string | null;
  isActive: boolean;
}

export interface ExecutionPortfolioSnapshot {
  cash: number;
  portfolioValue: number;
  unrealisedPnl: number;
  realisedPnl: number;
  positions: ExecutionPositionSnapshot[];
  asOf: string;
}

export interface ExecutionPositionSnapshot {
  symbol: string;
  quantity: number;
  averageCostBasis: number;
  currentPrice: number;
  marketValue: number;
  unrealisedPnl: number;
  realisedPnl: number;
}

export interface SessionOrderHistoryEntry {
  orderId: string;
  symbol: string;
  side: string;
  type: string;
  quantity: number;
  filledQuantity: number;
  averageFillPrice: number | null;
  status: string;
  createdAt: string;
  updatedAt: string;
}

export interface PaperSessionDetail {
  summary: PaperSessionSummary;
  symbols: string[];
  portfolio: ExecutionPortfolioSnapshot | null;
  orderHistory: SessionOrderHistoryEntry[] | null;
}

export interface PaperSessionReplayVerification {
  summary: PaperSessionSummary;
  symbols: string[];
  replaySource: string;
  isConsistent: boolean;
  mismatchReasons: string[];
  currentPortfolio: ExecutionPortfolioSnapshot | null;
  replayPortfolio: ExecutionPortfolioSnapshot;
  verifiedAt: string;
  comparedFillCount: number;
  comparedOrderCount: number;
  comparedLedgerEntryCount: number;
  lastPersistedFillAt: string | null;
  lastPersistedOrderUpdateAt: string | null;
  verificationAuditId: string | null;
}

export interface ExecutionAuditEntry {
  auditId: string;
  category: string;
  action: string;
  outcome: string;
  occurredAt: string;
  actor: string | null;
  brokerName: string | null;
  orderId: string | null;
  runId: string | null;
  symbol: string | null;
  correlationId: string | null;
  message: string | null;
  metadata: Record<string, string> | null;
}

export interface ExecutionCircuitBreakerState {
  isOpen: boolean;
  reason: string | null;
  changedBy: string | null;
  changedAt: string | null;
}

export interface ExecutionManualOverride {
  overrideId: string;
  kind: string;
  reason: string;
  createdBy: string;
  createdAt: string;
  expiresAt: string | null;
  symbol: string | null;
  strategyId: string | null;
  runId: string | null;
}

export interface ExecutionControlSnapshot {
  circuitBreaker: ExecutionCircuitBreakerState;
  defaultMaxPositionSize: number | null;
  symbolPositionLimits: Record<string, number>;
  manualOverrides: ExecutionManualOverride[];
  asOf: string;
}

export interface RiskRuleStatus {
  ruleName: string;
  state: "Healthy" | "Observe" | "Constrained";
  summary: string;
  isBreached: boolean;
  threshold: string;
  currentValue: string;
  asOf: string;
  recentViolations: string[];
}

export interface RiskRuleConfig {
  ruleName: string;
  defaultMaxPositionSize: number | null;
  symbolPositionLimits: Record<string, number> | null;
  maxDrawdownPercent: number | null;
  maxOrdersPerMinute: number | null;
}

export interface RiskRuleConfigUpdateRequest {
  defaultMaxPositionSize?: number | null;
  symbolPositionLimits?: Record<string, number | null> | null;
  maxDrawdownPercent?: number | null;
  maxOrdersPerMinute?: number | null;
  reason?: string | null;
}

export type OperatorWorkItemKind =
  | "PaperReplay"
  | "PromotionReview"
  | "BrokerageSync"
  | "SecurityMasterCoverage"
  | "ReconciliationBreak"
  | "ReportPackApproval"
  | "ProviderTrustGate"
  | "ExecutionControl"
  | "LedgerPeriodClose";

export type OperatorWorkItemTone = "Info" | "Success" | "Warning" | "Critical";
export type TradingAcceptanceGateStatus = "Ready" | "ReviewRequired" | "Blocked" | "Unknown";

export interface OperatorWorkItem {
  workItemId: string;
  kind: OperatorWorkItemKind;
  label: string;
  detail: string;
  tone: OperatorWorkItemTone;
  createdAt: string;
  runId: string | null;
  fundAccountId: string | null;
  auditReference: string | null;
  workspace?: string | null;
  targetRoute?: string | null;
  targetPageTag?: string | null;
  scope?: string | null;
  requiredSignoffRole?: string | null;
  toleranceProfileId?: string | null;
  signoffStatus?: string | null;
}

export interface OperatorInbox {
  asOf: string;
  items: OperatorWorkItem[];
  criticalCount: number;
  warningCount: number;
  reviewCount: number;
  summary: string;
}

export interface WorkflowAction {
  actionId: string;
  label: string;
  detail: string;
  targetPageTag: string;
  tone: string;
  workItemKind: OperatorWorkItemKind | null;
  routePrefixes: string[];
  routeContains: string[];
  aliases: string[];
}

export interface WorkflowDefinition {
  workflowId: string;
  title: string;
  summary: string;
  workspaceId: string;
  workspaceTitle: string;
  entryPageTag: string;
  tone: string;
  actions: WorkflowAction[];
  evidenceTags: string[];
  marketPatternTags: string[];
}

export interface WorkflowLibrary {
  generatedAt: string;
  workflows: WorkflowDefinition[];
  actions: WorkflowAction[];
}

export interface WorkflowPreset {
  presetId: string;
  name: string;
  description: string | null;
  workflowId: string;
  workflowTitle: string;
  actionId: string | null;
  actionLabel: string;
  workspaceId: string;
  workspaceTitle: string;
  targetPageTag: string;
  tags: string[];
  isPinned: boolean;
  createdAt: string;
  updatedAt: string;
  lastUsedAt: string | null;
}

export interface WorkflowPresetLibrary {
  generatedAt: string;
  presets: WorkflowPreset[];
}

export interface WorkflowPresetSaveRequest {
  presetId?: string | null;
  name: string;
  description?: string | null;
  workflowId: string;
  actionId?: string | null;
  tags?: string[] | null;
  isPinned: boolean;
}

export type OperationsWorkflowStatus =
  | "NotStarted"
  | "CollectingBrokerData"
  | "SecurityMasterValidation"
  | "LedgerPostingDraft"
  | "ReconciliationActive"
  | "ApprovalPending"
  | "ReadyForClose"
  | "Closed"
  | "Blocked";

export type OperationsGateStatus = "NotStarted" | "InProgress" | "Passed" | "ReviewRequired" | "Blocked";
export type OperationsGateKey = "BrokerIngest" | "SecurityMaster" | "LedgerPosting" | "Reconciliation" | "Approval";
export type OperationsBrokerIntakeState = "Pending" | "Imported" | "Normalized" | "MatchedToInternalRun" | "Complete";
export type OperationsSecurityMasterState = "Pending" | "ResolvedAllInstruments" | "OverridesRequested" | "OverridesApproved" | "Complete";
export type OperationsLedgerPostingState = "Pending" | "Drafted" | "Validated" | "Posted" | "Complete";
export type OperationsReconciliationState = "Pending" | "AutoMatched" | "ExceptionsOpen" | "InReview" | "Cleared" | "Complete";
export type OperationsApprovalState = "Pending" | "Submitted" | "ReviewerAssigned" | "Approved" | "Rejected";

export interface OperationsEvidenceLink {
  evidenceId: string;
  label: string;
  route: string | null;
  source: string | null;
  capturedAtUtc: string | null;
}

export interface OperationsWorkflowBlocker {
  code: string;
  message: string;
  gate: OperationsGateKey | null;
  severity: string;
  evidenceLinks: OperationsEvidenceLink[];
}

export interface OperationsNextAction {
  code: string;
  label: string;
  route: string | null;
  gate: OperationsGateKey | null;
}

export interface OperationsGate {
  gateKey: OperationsGateKey;
  displayName: string;
  status: OperationsGateStatus;
  isRequired: boolean;
  description: string;
  blockers: OperationsWorkflowBlocker[];
  nextActions: OperationsNextAction[];
  completedAtUtc: string | null;
  completedBy: string | null;
}

export interface OperationsContinuityWorkflowSummary {
  workflowId: string;
  fundAccountId: string;
  periodId: string;
  securityMasterSnapshotId: string | null;
  brokerSource: string;
  status: OperationsWorkflowStatus;
  version: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  gates: OperationsGate[];
  nextActions: OperationsNextAction[];
}

export interface OperationsTimelineEntry {
  auditId: string;
  occurredAtUtc: string;
  workflowId: string;
  fundAccountId: string;
  periodId: string;
  eventType: string;
  fromState: OperationsWorkflowStatus;
  toState: OperationsWorkflowStatus;
  gate: OperationsGateKey | null;
  fromGateStatus: OperationsGateStatus | null;
  toGateStatus: OperationsGateStatus | null;
  actor: string;
  rationale: string | null;
  correlationId: string | null;
  references: OperationsEvidenceLink[];
  previousHash: string | null;
  currentHash: string;
}

export interface OperationsBreakCase {
  breakId: string;
  checkId: string;
  category: string;
  severity: string;
  status: string;
  owner: string | null;
  dueDate: string | null;
  expectedSource: string | null;
  actualSource: string | null;
  expectedAmount: number | null;
  actualAmount: number | null;
  variance: number | null;
  securityId: string | null;
  symbol: string | null;
  suggestedAction: string | null;
  evidenceLinks: OperationsEvidenceLink[];
}

export interface OperationsApproval {
  approvalId: string;
  status: OperationsApprovalState;
  operator: string | null;
  reviewer: string | null;
  rationale: string | null;
  submittedAtUtc: string | null;
  decidedAtUtc: string | null;
  evidenceLinks: OperationsEvidenceLink[];
}

export interface OperationsLedgerPreview {
  previewId: string;
  status: string;
  ledgerBatchId: string | null;
  generatedAtUtc: string | null;
  evidenceLinks: OperationsEvidenceLink[];
}

export interface OperationsReportPackReadiness {
  isReady: boolean;
  reportPackId: string | null;
  blockingReason: string | null;
  evidenceLinks: OperationsEvidenceLink[];
}

export interface OperationsContinuityWorkflow extends OperationsContinuityWorkflowSummary {
  brokerIntakeState: OperationsBrokerIntakeState;
  securityMasterState: OperationsSecurityMasterState;
  ledgerPostingState: OperationsLedgerPostingState;
  reconciliationState: OperationsReconciliationState;
  approvalState: OperationsApprovalState;
  timeline: OperationsTimelineEntry[];
  breakCases: OperationsBreakCase[];
  ledgerPreview: OperationsLedgerPreview | null;
  approvals: OperationsApproval[];
  reportPackReadiness: OperationsReportPackReadiness;
  closeChecklist: OperationsCloseChecklistTask[];
  closeReadiness: OperationsCloseReadiness | null;
  evidenceLinks: OperationsEvidenceLink[];
  blockers: OperationsWorkflowBlocker[];
}

export interface OperationsCloseChecklistTask {
  taskId: string;
  gate: OperationsGateKey;
  label: string;
  owner: string;
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

export interface OperationsCloseReadiness {
  isReadyToClose: boolean;
  severity: string;
  blockers: OperationsCloseReadinessBlocker[];
  nextActions: OperationsNextAction[];
}

export interface OperationsCloseReadinessBlocker {
  code: string;
  category: string;
  severity: string;
  message: string;
  gate: OperationsGateKey | null;
  routeHint: string | null;
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
}

export interface EvidencePacket {
  subject: EvidenceSubject;
  generatedAt: string;
  nodes: EvidenceNode[];
  edges: EvidenceEdge[];
  completeness: EvidenceCompleteness;
  actions: WorkflowAction[];
  warnings: string[];
}

export interface EvidenceGraph {
  subject: EvidenceSubject;
  generatedAt: string;
  nodes: EvidenceNode[];
  edges: EvidenceEdge[];
  warnings: string[];
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

export type ProviderConnectionCapability = "Data" | "Brokerage" | "DataAndBrokerage";
export type ProviderCredentialState = "NotRequired" | "Missing" | "Partial" | "Configured" | "Verified" | "Invalid";
export type ProviderCredentialSource =
  | "None"
  | "LocalEncryptedStore"
  | "Environment"
  | "ExternalVaultReference"
  | "NotRequired";
export type ProviderVerificationState = "NotRequired" | "NotVerified" | "Verified" | "Failed" | "Stale";
export type ProviderContinuityHealth = "Unknown" | "Healthy" | "Warning" | "Degraded" | "Blocked";

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

export interface BrokerageHouseholdAccount {
  fundAccountId: string;
  providerId: string;
  externalAccountId: string;
  displayName: string;
  accountKind: BrokerageAccountKind;
  health: WorkstationBrokerageSyncStatus["health"];
  cash: number;
  equity: number;
  buyingPower: number;
  currency: string;
  syncedAt: string;
  positionCount: number;
  cashTransactionCount: number;
  warnings: string[];
}

export interface BrokerageHouseholdPosition {
  fundAccountId: string;
  providerId: string;
  externalAccountId: string;
  accountKind: BrokerageAccountKind;
  symbol: string;
  quantity: number;
  averageEntryPrice: number;
  marketPrice: number;
  marketValue: number;
  unrealizedPnl: number;
  assetClass: string;
  security: WorkstationSecurityReference | null;
  description: string | null;
  positionId: string | null;
  currency: string | null;
}

export interface BrokerageHouseholdPortfolio {
  providerId: string;
  asOf: string;
  totalCash: number;
  totalEquity: number;
  totalBuyingPower: number;
  currency: string;
  accounts: BrokerageHouseholdAccount[];
  positions: BrokerageHouseholdPosition[];
  warnings: string[];
}

export interface BrokerageCashFlowEntry {
  transactionId: string;
  transactionType: string;
  category: string;
  amount: number;
  currency: string;
  postedAt: string;
  symbol: string | null;
  description: string | null;
}

export interface BrokerageCashFlowSummary {
  fundAccountId: string;
  providerId: string | null;
  externalAccountId: string | null;
  accountKind: BrokerageAccountKind;
  from: string | null;
  to: string | null;
  totalInflows: number;
  totalOutflows: number;
  netCashFlow: number;
  currency: string;
  transactionCount: number;
  entries: BrokerageCashFlowEntry[];
  warnings: string[];
}

export interface TradingOperatorReadiness {
  asOf: string;
  overallStatus: TradingAcceptanceGateStatus;
  readyForPaperOperation: boolean;
  acceptanceGates: TradingAcceptanceGate[];
  activeSession: TradingPaperSessionReadiness | null;
  sessions: TradingPaperSessionReadiness[];
  replay: TradingReplayReadiness | null;
  controls: TradingControlReadiness;
  promotion: TradingPromotionReadiness | null;
  trustGate: TradingTrustGateReadiness;
  brokerageSync: WorkstationBrokerageSyncStatus | null;
  workItems: OperatorWorkItem[];
  warnings: string[];
}

export interface CreateExecutionManualOverrideRequest {
  kind: string;
  reason: string;
  createdBy?: string | null;
  symbol?: string | null;
  strategyId?: string | null;
  runId?: string | null;
  expiresAt?: string | null;
}

export interface ReplayFileRecord {
  path: string;
  name: string;
  symbol: string | null;
  eventType: string | null;
  sizeBytes: number;
  isCompressed: boolean;
  lastModified: string;
}

export interface ReplayStatus {
  sessionId: string;
  filePath: string;
  status: string;
  speedMultiplier: number;
  eventsProcessed: number;
  totalEvents: number;
  progressPercent: number;
  startedAt: string;
}

export interface OrderSubmitRequest {
  symbol: string;
  side: "Buy" | "Sell";
  type: "Market" | "Limit" | "Stop";
  quantity: number;
  limitPrice?: number | null;
}

export interface OrderResult {
  success: boolean;
  orderId: string | null;
  reason: string | null;
}

export interface ResearchWorkspaceResponse {
  metrics: MetricSnapshot[];
  runs: ResearchRunRecord[];
  plotTool?: ResearchPlotToolPayload | null;
}

export interface ResearchPlotToolTabPayload {
  id: string;
  label: string;
  tabId: string;
  panelId: string;
  selected: boolean;
  buttonVariant: "secondary" | "ghost";
  tabIndex: number;
  ariaLabel: string;
}

export interface ResearchPlotToolPayload {
  workspace: unknown;
  statistics: unknown;
  studies: unknown[];
  tabs: ResearchPlotToolTabPayload[];
  activeView?: "workspace" | "statistics";
}

export interface DataOperationsProviderRecord {
  providerId?: string;
  displayName?: string;
  provider: string;
  status: "Healthy" | "Warning" | "Degraded" | "Blocked";
  capability: string;
  latency: string;
  note: string;
  trustScore?: string;
  signalSource?: string;
  reasonCode?: string;
  recommendedAction?: string;
  gateImpact?: string;
  connectionSummary?: ProviderConnectionRow | null;
  routingSummary?: DataOperationsProviderRoutingSummary | null;
  diagnostics?: DataOperationsProviderDiagnosticSummary[] | null;
}

export interface DataOperationsProviderRoutingSummary {
  connectionId: string | null;
  providerFamilyId: string | null;
  productionReady: boolean | null;
  certificationFresh: boolean | null;
  bindingCount: number;
  fallbackRouteCount: number;
  healthStatus: string | null;
}

export interface DataOperationsProviderDiagnosticSummary {
  id: string;
  label: string;
  status: "pass" | "warning" | "fail" | "pending";
  statusLabel: string;
  detail: string;
}

export interface DataOperationsBackfillRecord {
  jobId: string;
  scope: string;
  provider: string;
  status: "Queued" | "Running" | "Review";
  progress: string;
  updatedAt: string;
}

export interface DataOperationsExportRecord {
  exportId: string;
  profile: string;
  target: string;
  status: "Ready" | "Running" | "Attention";
  rows: string;
  updatedAt: string;
}

export interface DataOperationsWorkspaceResponse {
  metrics: MetricSnapshot[];
  providers: DataOperationsProviderRecord[];
  backfills: DataOperationsBackfillRecord[];
  exports: DataOperationsExportRecord[];
}

export interface TradingPosition {
  positionKey?: string;
  symbol: string;
  side: "Long" | "Short";
  quantity: string;
  averagePrice: string;
  markPrice: string;
  dayPnl: string;
  unrealizedPnl: string;
  exposure: string;
}

export interface TradingOrder {
  orderId: string;
  symbol: string;
  side: "Buy" | "Sell";
  type: "Market" | "Limit" | "Stop";
  quantity: string;
  limitPrice: string;
  status: "Working" | "Partially Filled" | "Pending Routing";
  submittedAt: string;
}

export interface TradingFill {
  fillId: string;
  orderId: string;
  symbol: string;
  side: "Buy" | "Sell";
  quantity: string;
  price: string;
  venue: string;
  timestamp: string;
}

export interface TradingRiskState {
  state: "Healthy" | "Observe" | "Constrained";
  summary: string;
  netExposure: string;
  grossExposure: string;
  var95: string;
  maxDrawdown: string;
  buyingPowerUsed: string;
  activeGuardrails: string[];
}

export interface BrokerageWiringStatus {
  provider: string;
  account: string;
  environment: "paper" | "live";
  connection: "Connected" | "Degraded" | "Disconnected";
  lastHeartbeat: string;
  orderIngress: string;
  fillFeed: string;
  notes: string;
}

export interface TradingWorkspaceResponse {
  metrics: MetricSnapshot[];
  positions: TradingPosition[];
  openOrders: TradingOrder[];
  fills: TradingFill[];
  risk: TradingRiskState;
  brokerage: BrokerageWiringStatus;
  readiness?: TradingOperatorReadiness | null;
}

export interface PortfolioRunRow {
  runId: string;
  strategyName: string;
  engine: string;
  mode: string;
  status: string;
  pnl: string;
  sharpe: string;
  dataset: string;
  window: string;
  lastUpdated: string;
  notes: string;
  promotionState: string | null;
}

export interface PortfolioWorkspaceResponse {
  metrics: MetricSnapshot[];
  positions: TradingPosition[];
  risk: TradingRiskState;
  brokerage: BrokerageWiringStatus;
  runs: PortfolioRunRow[];
  cashFlow: GovernanceCashFlowSummary | null;
}


export interface StatementRunSummary {
  runId: string;
  importId: string;
  startedAtUtc: string;
  completedAtUtc: string;
  positionMatches: number;
  cashMatches: number;
  transactionMatches: number;
  openExceptionCount: number;
  brokerCustodian?: string | null;
  account?: string | null;
  period?: string | null;
  status?: string | null;
  validationIssueCount?: number | null;
  matchCount?: number | null;
  breakCount?: number | null;
  caseCount?: number | null;
  importedAtUtc?: string | null;
}

export interface StatementRunException {
  breakId: string;
  runId: string;
  importId: string;
  sourceReference: string;
  breakCode: string;
  category: string;
  delta: number;
  tolerance: number;
  toleranceBreached: boolean;
  createdAtUtc: string;
  status: string;
}

export interface GovernanceReconciliationRecord {
  runId: string;
  strategyName: string;
  mode: "paper" | "live" | "backtest";
  status: string;
  lastUpdated: string;
  breakCount: number;
  openBreakCount: number;
  reconciliationStatus: "NotStarted" | "BreaksOpen" | "SecurityCoverageOpen" | "Resolved" | "Balanced";
}

export interface GovernanceCashFlowSummary {
  totalCash: number;
  totalLedgerCash: number;
  netVariance: number;
  totalFinancing: number;
  runsWithCashSignals: number;
  runsWithCashVariance: number;
  tone: "default" | "success" | "warning" | "danger";
  summary: string;
}

export interface GovernanceReportingProfile {
  id: string;
  name: string;
  targetTool: string;
  format: string;
  description: string;
  loaderScript: boolean;
  dataDictionary: boolean;
}

export interface GovernanceReportingSummary {
  profileCount: number;
  recommendedProfiles: string[];
  profiles: GovernanceReportingProfile[];
  reportPackTargets: string[];
  summary: string;
}

export interface GovernanceWorkspaceResponse {
  metrics: MetricSnapshot[];
  reconciliationQueue: GovernanceReconciliationRecord[];
  breakQueue: ReconciliationBreakQueueItem[];
  cashFlow: GovernanceCashFlowSummary;
  reporting: GovernanceReportingSummary;
  controlCenter?: {
    closeReadiness: string;
    portfolioFilterOptions: string[];
    accountFilterOptions: string[];
    blockerSeverityDistribution: { severity: string; count: number }[];
    agingCurves: { bucket: string; count: number }[];
    ownerWorkload: { owner: string; openCount: number }[];
    slaBreachCount: number;
    trendSnapshots: { metric: string; value: number; trend: string }[];
    drillLinks: { label: string; href: string }[];
    alerts: { tone: "danger" | "warning" | "info"; message: string }[];
  };
}

export interface ExportAnalysisResult {
  jobId: string | null;
  success: boolean;
  status: string;
  profileId: string;
  symbols: string[] | null;
  filesGenerated: number;
  totalRecords: number;
  totalBytes: number;
  outputDirectory: string | null;
  durationSeconds: number;
  error: string | null;
  warnings: string[] | null;
  files: ExportAnalysisFile[];
  timestamp: string;
}

export interface ExportAnalysisFile {
  path: string;
  symbol: string | null;
  format: string | null;
  sizeBytes: number;
  recordCount: number;
}


export interface StatementRunSummary {
  runId: string;
  importId: string;
  startedAtUtc: string;
  completedAtUtc: string;
  positionMatches: number;
  cashMatches: number;
  transactionMatches: number;
  openExceptionCount: number;
}

export interface StatementRunException {
  breakId: string;
  runId: string;
  importId: string;
  sourceReference: string;
  breakCode: string;
  category: string;
  delta: number;
  tolerance: number;
  toleranceBreached: boolean;
  createdAtUtc: string;
  status: string;
}

export type ReconciliationBreakQueueStatus = "Open" | "InReview" | "Resolved" | "Dismissed" | "SignedOff";
export type ReconciliationCaseLifecycleState = "Open" | "InReview" | "AwaitingApproval" | "Approved" | "Posted" | "Reopened" | "Superseded" | "Investigating" | "AwaitingEvidence" | "Resolved" | "SignedOff";
export type ReconciliationCasePriority = "Low" | "Normal" | "High" | "Critical";
export type ReconciliationCaseSlaState = "NotStarted" | "OnTrack" | "Warning" | "Breached" | "Paused" | "Stopped";
export type ReconciliationCaseCommentVisibility = "Internal" | "CloseEvidence" | "ExternalSummary";
export type ReconciliationCaseworkAction = "Assign" | "ChangePriority" | "TransitionStatus" | "AddComment" | "EditComment" | "DeleteComment" | "SetRootCause" | "SetResolution" | "LinkEvidence" | "SignOff" | "Reopen" | "Resolve";

export interface ReconciliationBreakQueueItem {
  breakId: string;
  runId: string;
  strategyName: string;
  category: string;
  status: ReconciliationBreakQueueStatus;
  variance: number;
  reason: string;
  assignedTo: string | null;
  detectedAt: string;
  lastUpdatedAt: string;
  reviewedBy: string | null;
  reviewedAt: string | null;
  resolvedBy: string | null;
  resolvedAt: string | null;
  resolutionNote: string | null;
  exceptionRoute?: string | null;
  toleranceProfileId?: string | null;
  toleranceBand?: number | null;
  requiredSignoffRole?: string | null;
  signoffStatus?: string | null;
  fundAccountId?: string | null;
  explainabilitySummary?: string | null;
  routingTarget?: string | null;
  routingDetail?: string | null;
  recommendedAction?: string | null;
  assigneeId?: string | null;
  assigneeDisplayName?: string | null;
  priority?: ReconciliationCasePriority;
  slaPolicyId?: string | null;
  slaDueAt?: string | null;
  slaWarningAt?: string | null;
  slaBreachedAt?: string | null;
  slaState?: ReconciliationCaseSlaState;
  ageBand?: string | null;
  businessAgeHours?: number;
  rootCauseCode?: string | null;
  resolutionCode?: string | null;
  signedOffBy?: string | null;
  signedOffAt?: string | null;
  signOffNote?: string | null;
  reopenedBy?: string | null;
  reopenedAt?: string | null;
  reopenReason?: string | null;
  version?: number;
  comments?: ReconciliationCaseComment[] | null;
  evidenceLinks?: string[] | null;
  commentCount?: number;
  evidenceCount?: number;
  lastActivityAt?: string | null;
  sourceType?: string | null;
  sourceSystem?: string | null;
  sourceReference?: string | null;
  sourceImportId?: string | null;
  sourceBreakId?: string | null;
  sourceFingerprint?: string | null;
  lastCommentExcerpt?: string | null;
  relatedCaseCount?: number;
  slaBadgeLabel?: string | null;
  slaBadgeTone?: "info" | "warning" | "danger" | "neutral" | "success" | string | null;
}

export interface ReconciliationCaseComment {
  commentId: string;
  parentCommentId?: string | null;
  authorId: string;
  authorDisplayName: string;
  visibility: ReconciliationCaseCommentVisibility;
  body: string;
  evidenceLinks: string[];
  createdAt: string;
  editedAt?: string | null;
  deletedAt?: string | null;
  deletedBy?: string | null;
  mentions?: string[] | null;
  linkedEvidenceIds?: string[] | null;
  previousTextHash?: string | null;
  editReason?: string | null;
  deleteReason?: string | null;
  statusTransition?: ReconciliationCaseLifecycleState | null;
}

export interface ReconciliationCaseworkCommand {
  breakId: string;
  action: ReconciliationCaseworkAction;
  actor: string;
  commandId: string;
  correlationId: string;
  source: string;
  expectedVersion: number;
  reason?: string | null;
  assignee?: string | null;
  priority?: ReconciliationCasePriority | null;
  status?: ReconciliationCaseLifecycleState | null;
  note?: string | null;
  rootCauseCode?: string | null;
  resolutionCode?: string | null;
  causationId?: string | null;
  commentId?: string | null;
  parentCommentId?: string | null;
  visibility?: ReconciliationCaseCommentVisibility;
  evidenceLinks?: string[] | null;
  privileged?: boolean;
  statusTransition?: ReconciliationCaseLifecycleState | null;
  mentions?: string[] | null;
}

export interface ReconciliationBulkCaseworkRequest {
  breakIds: string[];
  action: ReconciliationCaseworkAction;
  actor: string;
  commandId: string;
  correlationId: string;
  source: string;
  idempotencyKey: string;
  dryRun: boolean;
  allowPartialSuccess: boolean;
  reason?: string | null;
  assignee?: string | null;
  priority?: ReconciliationCasePriority | null;
  status?: ReconciliationCaseLifecycleState | null;
  note?: string | null;
  rootCauseCode?: string | null;
  resolutionCode?: string | null;
  maxCaseCount?: number;
}

export interface ReconciliationBulkCaseworkResult {
  bulkActionId: string;
  idempotencyKey: string;
  dryRun: boolean;
  requestedCount: number;
  succeededCount: number;
  failedCount: number;
  results: Array<{ breakId: string; succeeded: boolean; wouldSucceed: boolean; error?: string | null; item?: ReconciliationBreakQueueItem | null }>;
}

export interface ReviewReconciliationBreakRequest {
  breakId: string;
  assignedTo: string;
  reviewedBy: string;
  reviewNote?: string;
}

export interface ResolveReconciliationBreakRequest {
  breakId: string;
  status: "Resolved" | "Dismissed";
  resolvedBy: string;
  resolutionNote: string;
  operatorRationale: string;
}

// --- Trading action result ---

export interface TradingActionResult {
  actionId: string;
  status: "Accepted" | "Completed" | "Rejected" | "Failed";
  message: string;
  occurredAt: string;
  auditId?: string | null;
}

// --- Multi-run comparison types ---

export interface RunComparisonRow {
  runId: string;
  strategyName: string;
  mode: string;
  engine: string;
  status: string;
  netPnl: number | null;
  totalReturn: number | null;
  finalEquity: number | null;
  maxDrawdown: number | null;
  sharpeRatio: number | null;
  fillCount: number;
  lastUpdatedAt: string;
  promotionState: string;
  hasLedger: boolean;
  hasAuditTrail: boolean;
}

// --- Run diff types ---

export interface PositionDiffEntry {
  symbol: string;
  baseQuantity: number;
  targetQuantity: number;
  basePnl: number;
  targetPnl: number;
  changeType: "Added" | "Removed" | "Modified";
}

export interface ParameterDiff {
  key: string;
  baseValue: string | null;
  targetValue: string | null;
}

export interface MetricsDiff {
  netPnlDelta: number;
  totalReturnDelta: number;
  fillCountDelta: number;
  baseNetPnl: number | null;
  targetNetPnl: number | null;
  baseTotalReturn: number | null;
  targetTotalReturn: number | null;
}

export interface RunDiff {
  baseRunId: string;
  targetRunId: string;
  baseStrategyName: string;
  targetStrategyName: string;
  addedPositions: PositionDiffEntry[];
  removedPositions: PositionDiffEntry[];
  modifiedPositions: PositionDiffEntry[];
  parameterChanges: ParameterDiff[];
  metrics: MetricsDiff;
}

// --- Security reference ---

export interface WorkstationSecurityReference {
  securityId: string;
  displayName: string;
  assetClass: string;
  currency: string;
  status: "Active" | "Inactive" | "Pending";
  primaryIdentifier: string | null;
  subType: string | null;
}

// --- Portfolio types ---

export interface NetSymbolPosition {
  symbol: string;
  netQuantity: number;
  grossQuantity: number;
}

export interface PortfolioPositionSummary {
  symbol: string;
  quantity: number;
  averageCostBasis: number;
  realizedPnl: number;
  unrealizedPnl: number;
  isShort: boolean;
  security: WorkstationSecurityReference | null;
}

export interface PortfolioSummary {
  portfolioId: string;
  runId: string;
  asOf: string;
  cash: number;
  longMarketValue: number;
  shortMarketValue: number;
  grossExposure: number;
  netExposure: number;
  totalEquity: number;
  realizedPnl: number;
  unrealizedPnl: number;
  commissions: number;
  financing: number;
  positions: PortfolioPositionSummary[];
  securityResolvedCount: number;
  securityMissingCount: number;
}

// --- Ledger types ---

export type AccountingBasisKind = "Primary" | "Gaap" | "Cash" | "Tax" | "Statutory";

export interface LedgerTrialBalanceLine {
  accountName: string;
  accountType: string;
  symbol: string | null;
  financialAccountId: string | null;
  balance: number;
  entryCount: number;
  security: WorkstationSecurityReference | null;
  accountingBasis?: AccountingBasisKind;
  accountingPolicyId?: string;
  accountingPolicyVersion?: string;
  ruleId?: string | null;
  ruleVersion?: string | null;
  sourceEventId?: string | null;
  sourceJournalEntryId?: string | null;
}

export interface LedgerJournalLine {
  journalEntryId: string;
  timestamp: string;
  description: string;
  totalDebits: number;
  totalCredits: number;
  lineCount: number;
}

export interface LedgerSummary {
  ledgerReference: string;
  runId: string;
  asOf: string;
  journalEntryCount: number;
  ledgerEntryCount: number;
  assetBalance: number;
  liabilityBalance: number;
  equityBalance: number;
  revenueBalance: number;
  expenseBalance: number;
  trialBalance: LedgerTrialBalanceLine[];
  journal: LedgerJournalLine[];
  securityResolvedCount: number;
  securityMissingCount: number;
}

// --- Equity curve types ---

export interface EquityCurvePoint {
  date: string;
  totalEquity: number;
  cash: number;
  dailyReturn: number;
  drawdownFromPeak: number;
  drawdownFromPeakPercent: number;
}

export interface EquityCurveSummary {
  runId: string;
  initialEquity: number;
  finalEquity: number;
  maxDrawdown: number;
  maxDrawdownPercent: number;
  maxDrawdownRecoveryDays: number;
  sharpeRatio: number;
  sortinoRatio: number;
  points: EquityCurvePoint[];
}

// --- Fill types ---

export interface RunFillEntry {
  fillId: string;
  orderId: string;
  symbol: string;
  filledQuantity: number;
  fillPrice: number;
  commission: number;
  filledAt: string;
  accountId: string | null;
}

export interface RunFillSummary {
  runId: string;
  totalFills: number;
  totalCommissions: number;
  fills: RunFillEntry[];
}

// --- Attribution types ---

export interface SymbolAttributionEntry {
  symbol: string;
  realizedPnl: number;
  unrealizedPnl: number;
  totalPnl: number;
  tradeCount: number;
  commissions: number;
  marginInterestAllocated: number;
}

export interface RunAttributionSummary {
  runId: string;
  totalRealizedPnl: number;
  totalUnrealizedPnl: number;
  totalCommissions: number;
  bySymbol: SymbolAttributionEntry[];
}

// --- Strategy run summary ---

export type StrategyRunMode = "Backtest" | "Paper" | "Live";
export type StrategyRunEngine = "Internal" | "QuantConnect" | "External";
export type StrategyRunStatus = "Running" | "Paused" | "Completed" | "Failed" | "Cancelled" | "Stopped";
export type StrategyRunPromotionState =
  | "None"
  | "RequiresCompletion"
  | "CandidateForPaper"
  | "CandidateForLive"
  | "LiveManaged";

export interface StrategyRunSummary {
  runId: string;
  strategyId: string;
  strategyName: string;
  mode: StrategyRunMode;
  engine: StrategyRunEngine;
  status: StrategyRunStatus;
  startedAt: string;
  completedAt: string | null;
  datasetReference: string | null;
  feedReference: string | null;
  portfolioId: string | null;
  ledgerReference: string | null;
  netPnl: number | null;
  totalReturn: number | null;
  finalEquity: number | null;
  fillCount: number;
  lastUpdatedAt: string;
  auditReference: string | null;
}

export interface StrategyRunDetail {
  summary: StrategyRunSummary;
  parameters: Record<string, string>;
  portfolio: PortfolioSummary | null;
  ledger: LedgerSummary | null;
  execution?: unknown | null;
  promotion?: unknown | null;
  governance?: unknown | null;
  governanceHooks?: unknown[] | null;
}

export interface StrategyRunContinuityLink {
  runId: string;
  strategyId: string;
  strategyName: string;
  mode: StrategyRunMode;
  status: StrategyRunStatus;
  startedAt: string;
  completedAt: string | null;
  promotionState: StrategyRunPromotionState;
  fundProfileId?: string | null;
  fundDisplayName?: string | null;
}

export interface StrategyRunContinuityLineage {
  parentRunId: string | null;
  parentRun: StrategyRunContinuityLink | null;
  childRuns: StrategyRunContinuityLink[];
}

export interface StrategyRunCashFlowDigest {
  asOf: string;
  currency: string;
  totalEntries: number;
  totalInflows: number;
  totalOutflows: number;
  netCashFlow: number;
  projectedNetPosition: number;
  bucketCount: number;
  nextBucketStart: string | null;
  nextBucketEnd: string | null;
  nextBucketNetFlow: number | null;
}

export interface ReconciliationRunSummary {
  reconciliationRunId: string;
  runId: string;
  createdAt: string;
  portfolioAsOf: string | null;
  ledgerAsOf: string | null;
  matchCount: number;
  breakCount: number;
  openBreakCount: number;
  hasTimingDrift: boolean;
  amountTolerance: number;
  maxAsOfDriftMinutes: number;
  securityIssueCount: number;
  hasSecurityCoverageIssues: boolean;
  bankTransactionCount: number;
  bankBreakCount: number;
  expectedAccountingEventCount: number;
  expectedJournalPreviewCount: number;
  securityMasterAccountingIssueCount: number;
  hasSecurityMasterAccountingIssues: boolean;
}

export type StrategyRunContinuityWarningSeverity = "Info" | "Warning" | "Critical";
export type StrategyRunContinuitySeamHealthStatus = "Healthy" | "Missing" | "Stale";

export interface StrategyRunContinuityWarning {
  code: string;
  severity: StrategyRunContinuityWarningSeverity;
  message: string;
  sourceSeam: string;
}

export interface StrategyRunContinuityStatus {
  hasRun: boolean;
  runHealth: StrategyRunContinuitySeamHealthStatus;
  hasFills: boolean;
  fillsHealth: StrategyRunContinuitySeamHealthStatus;
  hasPortfolio: boolean;
  portfolioHealth: StrategyRunContinuitySeamHealthStatus;
  hasLedger: boolean;
  ledgerHealth: StrategyRunContinuitySeamHealthStatus;
  hasCashFlow: boolean;
  cashFlowHealth: StrategyRunContinuitySeamHealthStatus;
  hasReconciliation: boolean;
  reconciliationHealth: StrategyRunContinuitySeamHealthStatus;
  asOfDriftMinutes: number;
  openReconciliationBreaks: number;
  securityCoverageIssueCount: number;
  hasWarnings: boolean;
  warnings: StrategyRunContinuityWarning[];
}

export interface StrategyRunContinuityDto {
  run: StrategyRunDetail;
  lineage: StrategyRunContinuityLineage;
  cashFlow: StrategyRunCashFlowDigest | null;
  reconciliation: ReconciliationRunSummary | null;
  continuityStatus: StrategyRunContinuityStatus;
}

// --- Security Master workstation types ---

export interface SecurityClassificationSummary {
  assetClass: string;
  subType: string | null;
  primaryIdentifierKind: string | null;
  primaryIdentifierValue: string | null;
  matchedIdentifierKind?: string | null;
  matchedIdentifierValue?: string | null;
  matchedProvider?: string | null;
}

export interface SecurityEconomicDefinitionSummary {
  currency: string;
  version: number;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  subType: string | null;
  assetFamily: string | null;
  issuerType: string | null;
}

export interface SecurityMasterEntry {
  securityId: string;
  displayName: string;
  status: "Active" | "Inactive" | "Pending" | "Deactivated";
  classification: SecurityClassificationSummary;
  economicDefinition: SecurityEconomicDefinitionSummary;
}

export interface SecurityIdentifierEntry {
  kind: string;
  value: string;
  isPrimary: boolean;
  validFrom: string;
  validTo: string | null;
  provider: string | null;
}

export interface SecurityAliasEntry {
  aliasId: string;
  securityId: string;
  aliasKind: string;
  aliasValue: string;
  provider: string | null;
  scope: "Operations" | "Collector" | "Execution" | "Migration";
  reason: string | null;
  createdBy: string;
  createdAt: string;
  validFrom: string;
  validTo: string | null;
  isEnabled: boolean;
}

export interface SecurityIdentityDrillIn {
  securityId: string;
  displayName: string;
  assetClass: string;
  status: string;
  version: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  identifiers: SecurityIdentifierEntry[];
  aliases: SecurityAliasEntry[];
}

export interface SecurityMasterScheduleSummary {
  supportsCashflowSchedule: boolean;
  supportsFactorHistory: boolean;
  hasEconomicScheduleTerms: boolean;
  currentFactor: number | null;
  currentFactorDate: string | null;
  nextLifecycleDate: string | null;
  sourceSummary: string;
  summary: string;
}

export interface SecurityMasterScheduleEvent {
  eventId: string;
  eventType: string;
  effectiveDate: string;
  payDate: string | null;
  accrualStartDate: string | null;
  accrualEndDate: string | null;
  expectedAmount: number | null;
  actualAmount: number | null;
  varianceAmount: number | null;
  factorStart: number | null;
  factorEnd: number | null;
  currency: string;
  postingStatus: string;
  sourceSystem: string;
  sourceRecordId: string | null;
  sourceAsOfUtc: string | null;
  sourceUpdatedBy: string | null;
  sourceReason: string | null;
  isDerivedFromEconomicTerms: boolean;
  isCurrentProjection: boolean;
}

export interface SecurityMasterFactorPoint {
  pointId: string;
  effectiveDate: string;
  factor: number;
  previousFactor: number | null;
  sourceSystem: string;
  sourceRecordId: string | null;
  sourceAsOfUtc: string | null;
  sourceUpdatedBy: string | null;
  sourceReason: string | null;
  isCurrentFactor: boolean;
}

export interface SecurityMasterScheduleProvenance {
  provenanceId: string;
  category: string;
  summary: string;
  effectiveDate: string | null;
  sourceSystem: string;
  sourceRecordId: string | null;
  sourceAsOfUtc: string | null;
  sourceUpdatedBy: string | null;
  sourceReason: string | null;
  streamVersion: number | null;
  eventType: string | null;
}

export interface SecurityMasterScheduleBook {
  supportsCashflowSchedule: boolean;
  supportsFactorHistory: boolean;
  hasEconomicScheduleTerms: boolean;
  currency: string;
  currentFactor: number | null;
  currentFactorDate: string | null;
  nextLifecycleDate: string | null;
  sourceSummary: string;
  summary: string;
  events: SecurityMasterScheduleEvent[];
  factorHistory: SecurityMasterFactorPoint[];
  provenanceHistory: SecurityMasterScheduleProvenance[];
}

export interface SecurityMasterLotModel {
  quantityModel: string;
  lotSize: number | null;
  contractMultiplier: number | null;
  usesFaceValue: boolean;
  supportsFactorAdjustedExposure: boolean;
  requiresResolvedSecurityId: boolean;
  summary: string;
}

export interface SecurityMasterOpenLot {
  securityId: string;
  portfolioId: string;
  runId: string;
  accountScopeId: string | null;
  accountScopeDisplayName: string | null;
  vehicleScopeId: string | null;
  vehicleScopeDisplayName: string | null;
  lotId: string;
  symbol: string;
  tradeDate: string;
  settleDate: string | null;
  originalQuantity: number;
  currentQuantity: number;
  originalFace: number | null;
  currentFace: number | null;
  factorAdjustedQuantity: number | null;
  factorAdjustedFace: number | null;
  costBasis: number;
  entryPrice: number;
  unrealizedPnl: number | null;
  currency: string;
  lotStatus: string;
  sourceSystem: string;
  sourceRecordId: string | null;
  asOfUtc: string;
  sourceUpdatedBy: string | null;
  sourceReason: string | null;
  isLongTerm: boolean;
  notes: string | null;
}

export interface SecurityMasterOpenLotProvenance {
  provenanceId: string;
  runId: string;
  portfolioId: string;
  accountScopeId: string | null;
  accountScopeDisplayName: string | null;
  sourceSystem: string;
  sourceRecordId: string | null;
  asOfUtc: string;
  summary: string;
}

export interface SecurityMasterOpenLotReadModel {
  quantityModel: string;
  lotSize: number | null;
  contractMultiplier: number | null;
  usesFaceValue: boolean;
  supportsFactorAdjustedExposure: boolean;
  requiresResolvedSecurityId: boolean;
  currentFactor: number | null;
  currentFactorDate: string | null;
  asOfUtc: string;
  summary: string;
  lots: SecurityMasterOpenLot[];
  provenanceHistory: SecurityMasterOpenLotProvenance[];
}

export interface SecurityMasterTrustSnapshot {
  securityId: string;
  retrievedAtUtc: string;
  scheduleSummary?: SecurityMasterScheduleSummary | null;
  lotModel?: SecurityMasterLotModel | null;
  scheduleBook?: SecurityMasterScheduleBook | null;
  openLotReadModel?: SecurityMasterOpenLotReadModel | null;
}

export interface OperatorOverridesDto {
  securityId: string;
  values: Record<string, string>;
  updatedBy: string;
  updatedAt: string;
}

export interface OperatorOverridesPatchRequest {
  setValues?: Record<string, string>;
  removeKeys?: string[];
}

export interface SecurityMasterConflict {
  conflictId: string;
  securityId: string;
  conflictKind: string;
  fieldPath: string;
  providerA: string;
  valueA: string;
  providerB: string;
  valueB: string;
  detectedAt: string;
  status: "Open" | "Resolved" | "Dismissed";
}

export interface ResolveConflictRequest {
  conflictId: string;
  resolution: "AcceptA" | "AcceptB" | "Dismiss";
  resolvedBy: string;
  reason?: string;
}

// --- Provider setup types ---

export type ProviderKind =
  | "polygon"
  | "databento"
  | "alpaca"
  | "interactivebrokers"
  | "yahoo"
  | "custom";

export interface ProviderKindMeta {
  kind: ProviderKind;
  label: string;
  description: string;
  needsApiKey: boolean;
  needsApiSecret: boolean;
  needsEndpoint: boolean;
  defaultCapabilities: string[];
}

export interface ProviderSetupRequest {
  kind: ProviderKind | string;
  displayName: string;
  apiKey: string | null;
  apiSecret: string | null;
  endpoint: string | null;
  capabilities: string[];
  environment?: string | null;
}

export interface ProviderSetupResult {
  success: boolean;
  providerId: string | null;
  providerName: string;
  message: string;
  error: string | null;
  connectionId?: string | null;
  bindingIds?: string[] | null;
  credentialState?: ProviderCredentialState | null;
  credentialSource?: ProviderCredentialSource | null;
  credentialReference?: string | null;
  environment?: string | null;
  warnings?: string[] | null;
}

export interface ProviderRouteScope {
  workspace?: string | null;
  fundProfileId?: string | null;
  entityId?: string | null;
  sleeveId?: string | null;
  vehicleId?: string | null;
  accountId?: string | null;
}

export interface ProviderRoutingConnection {
  connectionId: string;
  providerFamilyId: string;
  displayName: string;
  connectionType: string;
  connectionMode: string;
  enabled: boolean;
  credentialReference: string | null;
  institutionId: string | null;
  externalAccountId: string | null;
  scope: ProviderRouteScope | null;
  tags: string[];
  description: string | null;
  productionReady: boolean;
}

export interface ProviderRoutingBinding {
  bindingId: string;
  capability: string;
  connectionId: string;
  target: ProviderRouteScope | null;
  priority: number;
  enabled: boolean;
  failoverConnectionIds: string[];
  safetyModeOverride: string | null;
  notes: string | null;
}

export interface ProviderRoutingTrustSnapshot {
  connectionId: string;
  providerFamilyId: string;
  score: number;
  isHealthy: boolean;
  healthStatus: string;
  isProductionReady: boolean;
  isCertificationFresh: boolean;
  signals: string[];
  decision?: unknown;
}

export interface ProviderRoutePreviewRequest {
  capability: string;
  workspace?: string | null;
  fundProfileId?: string | null;
  entityId?: string | null;
  sleeveId?: string | null;
  vehicleId?: string | null;
  accountId?: string | null;
  securityId?: string | null;
  symbol?: string | null;
  market?: string | null;
  assetClass?: string | null;
  requireProductionReady?: boolean;
}

export interface ProviderRoutePreviewCandidate {
  connectionId: string;
  providerFamilyId: string;
  isHealthy: boolean;
  scopeRank: number;
  priority: number;
  reasonCodes: string[];
  fallbackConnectionIds: string[];
  policyGate: string | null;
  compositeScore: number;
  healthScore: number;
  latencyScore: number;
  dataQualityScore: number;
  coverageScore: number;
  policyGateScore: number;
}

export interface ProviderRoutePreviewResponse {
  capability: string;
  isRoutable: boolean;
  selectedConnectionId: string | null;
  selectedProviderFamilyId: string | null;
  safetyMode: string;
  requiresManualApproval: boolean;
  reasonCodes: string[];
  skippedCandidates: string[];
  fallbackConnectionIds: string[];
  policyGate: string | null;
  candidates: ProviderRoutePreviewCandidate[];
  rankedAlternatives?: ProviderRoutePreviewCandidate[] | null;
}

// --- Backfill mutation types ---

export interface BackfillTriggerRequest {
  provider: string | null;
  symbols: string[];
  from: string | null;
  to: string | null;
}

export interface BackfillTriggerResult {
  success: boolean;
  provider: string;
  symbols: string[];
  from: string | null;
  to: string | null;
  barsWritten: number;
  startedUtc: string;
  completedUtc: string;
  error: string | null;
}

export interface BackfillPreviewSymbol {
  symbol: string;
  estimatedBars: number;
  hasMarketHoursData: boolean;
  notes: string[];
}

export interface BackfillPreviewResult {
  provider: string;
  providerDisplayName: string;
  from: string;
  to: string;
  totalDays: number;
  estimatedTradingDays: number;
  symbols: BackfillPreviewSymbol[];
  estimatedDurationSeconds: number;
  notes: string[];
}

export interface BackfillProgressEntry {
  symbol: string;
  barsWritten: number;
  completed: boolean;
}

export interface BackfillProgressResponse {
  active?: boolean;
  provider?: string | null;
  symbols?: BackfillProgressEntry[];
  message?: string | null;
  lastRun?: BackfillTriggerResult | null;
  isActive?: boolean;
  timestamp?: string;
}

// --- System Overview types ---

export interface SystemEventRecord {
  id: string;
  type: "info" | "warning" | "error";
  message: string;
  source: string;
  timestamp: string;
}

export interface SystemOverviewResponse {
  systemStatus: "Healthy" | "Degraded" | "Offline";
  providersOnline: number;
  providersTotal: number;
  activeRuns: number;
  openPositions: number;
  activeBackfills: number;
  symbolsMonitored: number;
  storageHealth: "Healthy" | "Warning" | "Critical";
  lastHeartbeatUtc: string;
  metrics: MetricSnapshot[];
  recentEvents: SystemEventRecord[];
}

// --- Symbol management types ---

export interface SymbolRecord {
  symbol: string;
  status: "Active" | "Monitored" | "Archived" | "Error";
  provider: string | null;
  lastEventAt: string | null;
  eventCount: number;
  hasHistoricalData: boolean;
}

export interface SymbolStatistics {
  totalSymbols: number;
  monitoredSymbols: number;
  archivedSymbols: number;
  symbolsWithErrors: number;
  totalEventsLast24h: number;
}

// --- Quality monitoring types ---

export interface QualitySymbolScore {
  symbol: string;
  completenessScore: number;
  freshnessScore: number;
  gapCount: number;
  anomalyCount: number;
  health: "Healthy" | "Warning" | "Critical";
}

export interface QualityGapEntry {
  symbol: string;
  provider: string;
  from: string;
  to: string;
  estimatedBars: number;
  status: "Open" | "Resolved";
}

export interface QualityAnomalyEntry {
  anomalyId: string;
  symbol: string;
  anomalyType: string;
  message: string;
  detectedAt: string;
  acknowledged: boolean;
}

export interface QualityDashboardResponse {
  overallScore: number;
  completenessScore: number;
  freshnessScore: number;
  anomalyRate: number;
  symbols: QualitySymbolScore[];
  recentGaps: QualityGapEntry[];
  recentAnomalies: QualityAnomalyEntry[];
}

export interface ReconciliationCalibrationProfile {
  toleranceProfileId: string;
  exceptionRoute: string;
  highestSeverity: string;
  maxToleranceBand: number | null;
  totalBreakCount: number;
  openBreakCount: number;
  inReviewBreakCount: number;
  resolvedBreakCount: number;
  dismissedBreakCount: number;
  pendingSignoffCount: number;
  signedOffCount: number;
  lastUpdatedAt: string;
}

export type ReconciliationCalibrationStatus = "Ready" | "ReviewRequired" | "Blocked";

export interface ReconciliationCalibrationSummary {
  asOf: string;
  status: ReconciliationCalibrationStatus;
  summary: string;
  totalBreakCount: number;
  activeBreakCount: number;
  openBreakCount: number;
  inReviewBreakCount: number;
  resolvedBreakCount: number;
  dismissedBreakCount: number;
  criticalOpenBreakCount: number;
  pendingSignoffCount: number;
  signedOffCount: number;
  missingCalibrationMetadataCount: number;
  profiles: ReconciliationCalibrationProfile[];
}

export interface CorporateAction {
  corpActId: string;
  securityId: string;
  eventType: string;
  exDate: string;
  payDate: string | null;
  dividendPerShare: number | null;
  currency: string | null;
  splitRatio: number | null;
  newSecurityId: string | null;
  distributionRatio: number | null;
  acquirerSecurityId: string | null;
  exchangeRatio: number | null;
  subscriptionPricePerShare: number | null;
  rightsPerShare: number | null;
}

export interface TradingParameters {
  securityId: string;
  lotSize: number | null;
  tickSize: number | null;
  contractMultiplier: number | null;
  marginRequirementPct: number | null;
  tradingHoursUtc: string | null;
  circuitBreakerThresholdPct: number | null;
  asOf: string;
}

export interface SessionStatsDto {
  sessionDate: string;
  open: number;
  high: number;
  low: number;
  last: number;
  volume: number;
  vwap: number;
  tradeCount: number;
  change: number;
  changePercent: number | null;
  firstTradeAt: string;
  lastTradeAt: string;
}

export interface QuoteDataResponse {
  symbol: string;
  timestamp: string;
  bidPrice: number;
  bidSize: number;
  askPrice: number;
  askSize: number;
  midPrice: number | null;
  spread: number | null;
  sequenceNumber: number;
  streamId: string | null;
  venue: string | null;
  session: SessionStatsDto | null;
}

export interface QuotesResponse {
  symbol: string;
  quote: QuoteDataResponse | null;
  timestamp: string;
}

export interface QuotesSnapshotItem {
  symbol: string;
  timestamp: string;
  bidPrice: number;
  bidSize: number;
  askPrice: number;
  askSize: number;
  midPrice: number | null;
  spread: number | null;
  lastPrice: number | null;
  lastSize: number | null;
  lastTradeTimestamp: string | null;
  sequenceNumber: number;
  streamId: string | null;
  venue: string | null;
  session: SessionStatsDto | null;
}

export interface QuotesSnapshotResponse {
  timestamp: string;
  count: number;
  quotes: QuotesSnapshotItem[];
}

export interface TradeDataResponse {
  symbol: string;
  timestamp: string;
  price: number;
  size: number;
  aggressor: string;
  sequenceNumber: number;
  streamId: string | null;
  venue: string | null;
}

export interface TradesResponse {
  symbol: string;
  trades: TradeDataResponse[];
  count: number;
  timestamp: string;
}

export interface OrderBookLevelDto {
  side: string;
  level: number;
  price: number;
  size: number;
  marketMaker: string | null;
}

export interface OrderBookResponse {
  symbol: string;
  timestamp: string;
  bids: OrderBookLevelDto[];
  asks: OrderBookLevelDto[];
  midPrice: number | null;
  imbalance: number | null;
  marketState: string;
  sequenceNumber: number;
  isStale: boolean;
  streamId: string | null;
  venue: string | null;
}

export interface HistoricalBarPoint {
  start: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
  vwap: number;
  tradeCount: number;
}

export interface HistoricalBarsResponse {
  success: boolean;
  message: string | null;
  symbol: string;
  intervalMinutes: number;
  from: string | null;
  to: string | null;
  totalBars: number;
  filesProcessed: number;
  totalFiles: number;
  queryTimeMs: number;
  bars: HistoricalBarPoint[];
}

export type QuantPlotKind =
  | "Line"
  | "MultiLine"
  | "CumulativeReturn"
  | "Drawdown"
  | "Heatmap"
  | "Candlestick"
  | "Bar"
  | "Scatter"
  | "Histogram";

export interface QuantPlotPoint {
  date: string;
  value: number;
}

export interface QuantPlotSeries {
  label: string;
  values: QuantPlotPoint[];
}

export interface QuantPlotBar {
  date: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface QuantPlot {
  title: string;
  type: QuantPlotKind;
  series: QuantPlotPoint[] | null;
  multiSeries: QuantPlotSeries[] | null;
  candlestick: QuantPlotBar[] | null;
  heatmapData: number[][] | null;
  heatmapLabels: string[] | null;
}

export interface QuantDiagnostic {
  severity: string;
  message: string;
  line: number;
  column: number;
}

export interface QuantMetric {
  label: string;
  value: string;
}

export interface QuantTrade {
  timestamp: string;
  symbol: string;
  side: string;
  quantity: number;
  price: number;
  commission: number;
}

export interface QuantParameter {
  name: string;
  label: string;
  typeName: string;
  defaultValue: string | null;
  min: number | null;
  max: number | null;
  description: string | null;
}

export interface QuantRunResponse {
  success: boolean;
  elapsedMs: number;
  compileTimeMs: number;
  peakMemoryBytes: number;
  runtimeError: string | null;
  consoleOutput: string;
  compilationErrors: QuantDiagnostic[];
  runtimeDiagnostics: QuantDiagnostic[];
  metrics: QuantMetric[];
  plots: QuantPlot[];
  trades: QuantTrade[];
  runtimeParameters: QuantParameter[];
}

export interface QuantParametersResponse {
  parameters: QuantParameter[];
}

export interface QuantTemplate {
  id: string;
  title: string;
  description: string;
  source: string;
}

export interface QuantTemplatesResponse {
  templates: QuantTemplate[];
}

export interface QuantRunRequest {
  source: string;
  parameters: Record<string, string | number | boolean | null>;
}

// --- Strategy Designer ---

export interface StrategyDesignDocument {
  documentId: string;
  name: string;
  description: string;
  version: string;
  datasetReference: string;
  universe: string[];
  cells: StrategyDesignCell[];
  transitions: StrategyDesignTransition[];
  metadata?: Record<string, string> | null;
  createdAt: string;
  updatedAt: string;
}

export interface StrategyDesignCell {
  cellId: string;
  label: string;
  kind: string;
  purpose: string;
  source: string;
  fieldRefs: string[];
  parameters?: Record<string, string> | null;
  disabledReason?: string | null;
}

export interface StrategyDesignTransition {
  transitionId: string;
  fromCellId: string;
  toCellId: string;
  kind: string;
  condition: string;
  maxIterations?: number | null;
  rationale?: string | null;
}

export interface StrategyDesignFieldCatalogItem {
  fieldId: string;
  label: string;
  source: string;
  dataSet: string;
  typeName: string;
  description: string;
  isEnabled: boolean;
  disabledReason: string | null;
  synonyms: string[];
}

export interface StrategyDesignTemplate {
  templateId: string;
  name: string;
  description: string;
  category: string;
  sourcePrototype: string;
  tags: string[];
  document: StrategyDesignDocument;
}

export interface StrategyDesignValidationMessage {
  code: string;
  severity: string;
  targetId: string;
  message: string;
}

export interface StrategyDesignValidationResult {
  isValid: boolean;
  summary: string;
  messages: StrategyDesignValidationMessage[];
}

export interface StrategyDesignDraftSummary {
  documentId: string;
  name: string;
  version: string;
  datasetReference: string;
  cellCount: number;
  transitionCount: number;
  updatedAt: string;
  validationSummary: string;
}

export interface StrategyDesignDraftSaveRequest {
  document: StrategyDesignDocument;
}

export interface StrategyDesignDraftSaveResponse {
  document: StrategyDesignDocument;
  summary: StrategyDesignDraftSummary;
  validation: StrategyDesignValidationResult;
  trace: StrategyDesignRunTraceEntry[];
}

export interface StrategyDesignCompiledScript {
  source: string;
  datasetFingerprint: string;
  fieldRefs: string[];
  disabledFieldRefs: string[];
}

export interface StrategyDesignPreviewRow {
  rowId: string;
  cellId: string;
  label: string;
  purpose: string;
  status: string;
  fieldRefs: string[];
  detail: string;
}

export interface StrategyDesignRunTraceEntry {
  stepId: string;
  label: string;
  status: string;
  detail: string;
  cellId?: string | null;
  occurredAt?: string | null;
}

export interface StrategyDesignPreviewResult {
  validation: StrategyDesignValidationResult;
  compiled: StrategyDesignCompiledScript;
  rows: StrategyDesignPreviewRow[];
  trace: StrategyDesignRunTraceEntry[];
}

export interface StrategyDesignRunBacktestRequest {
  document: StrategyDesignDocument;
  parameters?: Record<string, string> | null;
}

export interface StrategyDesignRunBacktestResponse {
  success: boolean;
  runId: string | null;
  strategyId: string;
  strategyName: string;
  validation: StrategyDesignValidationResult;
  compiled: StrategyDesignCompiledScript;
  trace: StrategyDesignRunTraceEntry[];
  previewRows: StrategyDesignPreviewRow[];
  metrics: Record<string, string>;
  runtimeError: string | null;
  promotionCandidatePath: string | null;
  reviewPacketPath: string | null;
}

// --- Quant Notebook ---

export type CellKind = "code" | "markdown";
export type CellExecutionState = "idle" | "running" | "done" | "error" | "stale";

export interface CellExecutionContext {
  symbol?: string;
  from?: string;
  to?: string;
  interval?: DataFetchRequest["interval"];
}

export interface CellOutput {
  kind: "console" | "metric" | "signal" | "error";
  text: string;
  tone?: "default" | "success" | "warning" | "danger";
  timestamp?: string;
}

export interface NotebookCell {
  id: string;
  ordinal: number;
  kind: CellKind;
  source: string;
  state: CellExecutionState;
  statusText: string;
  collapsed: boolean;
  output: CellOutput[];
}

export interface CellSnippet {
  id: string;
  label: string;
  description: string;
  kind: CellKind;
  source: string;
}

export interface CellExecuteRequest {
  cellId: string;
  source: string;
  context: CellExecutionContext;
}

export interface CellExecuteResult {
  cellId: string;
  success: boolean;
  output: CellOutput[];
  elapsedMs: number;
  errorMessage: string | null;
}

export interface PriceBar {
  timestamp: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface DataFetchRequest {
  symbol: string;
  from: string;
  to: string;
  interval: "daily" | "hourly" | "minute";
}

export interface DataFetchResult {
  symbol: string;
  from: string;
  to: string;
  interval: DataFetchRequest["interval"];
  bars: PriceBar[];
  rowCount: number;
}
