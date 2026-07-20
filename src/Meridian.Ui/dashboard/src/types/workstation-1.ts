import type {
  EvidenceStatus,
  MeridianAssuranceScore,
  OperationsCloseChecklistTask,
  OperationsClosePackagePublication,
  OperationsCloseReadiness,
  ReconciliationBreakDisposition,
  ReconciliationBreakMeasure,
} from "../types";

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

export type CoreFinancialObjectKind =
  | "Tenant"
  | "Entity"
  | "Relationship"
  | "Account"
  | "Instrument"
  | "Contract"
  | "Obligation"
  | "ExpectedCashFlow"
  | "Transaction"
  | "Position"
  | "Valuation"
  | "Reconciliation"
  | "Exception"
  | "CapitalAccount"
  | "LedgerAccount"
  | "JournalEntry"
  | "FundEvent"
  | "Document"
  | "Task"
  | "ReportPackage"
  | "AuditEvent";

export type ExtensibilityConfigurationArea =
  | "Workflow"
  | "Rule"
  | "Integration"
  | "DataMapping"
  | "Report"
  | "Permission"
  | "Classification"
  | "CustomField"
  | "SourcePriority"
  | "LedgerControl"
  | "Notification"
  | "DomainExtension"
  | "TenantTemplate";

export type ExtensibilityConfigurationStatus =
  | "Draft"
  | "Tested"
  | "Reviewed"
  | "Approved"
  | "Active"
  | "Superseded"
  | "Retired";

export type ExtensibilityScopeKind =
  | "Global"
  | "Tenant"
  | "EntityGroup"
  | "Entity"
  | "Account"
  | "WorkflowInstance"
  | "User";

export type GovernedFoundationKind =
  | "AuditTrail"
  | "SecurityModelFoundation"
  | "CoreObjectIdentity"
  | "FinancialCalculationIntegrity"
  | "DataLineageModel"
  | "ApprovalEvidenceModel"
  | "ImmutableRecordPreservation";

export type ExtensibilityValidationSeverity = "Info" | "Warning" | "Critical";

export interface StableCoreObjectContract {
  kind: CoreFinancialObjectKind;
  displayName: string;
  description: string;
  ownerContext: string;
  identityRule: string;
  allowsTenantCustomFields: boolean;
}

export interface ExtensibilityLayerContract {
  area: ExtensibilityConfigurationArea;
  displayName: string;
  description: string;
  examples: string[];
  allowedScopes: ExtensibilityScopeKind[];
  guardrails: string[];
}

export interface GovernedFoundationContract {
  kind: GovernedFoundationKind;
  displayName: string;
  description: string;
  guardrails: string[];
}

export interface ExtensibilityScope {
  scopeKind: ExtensibilityScopeKind;
  scopeId: string | null;
  displayName: string | null;
}

export interface ExtensibilityValidationIssue {
  code: string;
  severity: ExtensibilityValidationSeverity;
  message: string;
  blockedFoundation: GovernedFoundationKind | null;
  evidenceRoute: string | null;
}

export interface ExtensibilityConfigurationEnvelope {
  configurationId: string;
  area: ExtensibilityConfigurationArea;
  configurationType: string;
  owningContext: string;
  scope: ExtensibilityScope;
  status: ExtensibilityConfigurationStatus;
  version: number;
  effectiveAt: string;
  expiresAt: string | null;
  createdBy: string;
  createdAt: string;
  reviewedBy: string | null;
  approvedBy: string | null;
  approvedAt: string | null;
  changeReason: string;
  linkedAuditEventId: string | null;
  rollbackVersion: number | null;
  validationIssues: ExtensibilityValidationIssue[];
}

export interface DomainExtensionDescriptor {
  extensionId: string;
  displayName: string;
  owningContext: string;
  appliesToCoreObjects: CoreFinancialObjectKind[];
  customFieldKeys: string[];
  classificationKeys: string[];
  ruleIds: string[];
  canIntroduceCoreObjectIdentity: boolean;
  canBypassAuditTrail: boolean;
  canOverrideFinancialCalculations: boolean;
}

export interface TenantTemplateConfigurationBundle {
  tenantTemplateId: string;
  displayName: string;
  profile: string;
  configurations: ExtensibilityConfigurationEnvelope[];
  domainExtensions: DomainExtensionDescriptor[];
  allowsCoreObjectIdentityOverrides: boolean;
  allowsAuditTrailOverrides: boolean;
  allowsCalculationOverrides: boolean;
}

export interface ExtensibilityActivationReadiness {
  isReady: boolean;
  issues: ExtensibilityValidationIssue[];
  requiredFoundationChecks: GovernedFoundationKind[];
}

export interface TenantTemplateActivationRequest {
  changeReason: string;
  linkedAuditEventId?: string | null;
}

export interface TenantTemplateActivationResult {
  tenantTemplateId: string;
  isActivated: boolean;
  resultingStatus: ExtensibilityConfigurationStatus;
  evaluatedAt: string;
  evaluatedBy: string;
  changeReason: string;
  linkedAuditEventId: string | null;
  readiness: ExtensibilityActivationReadiness;
  tenantTemplate: TenantTemplateConfigurationBundle | null;
}

export interface ExtensibilityRegistration {
  registrationId: string;
  area: ExtensibilityConfigurationArea;
  status: ExtensibilityConfigurationStatus;
  displayName: string;
  summary: string;
  owningContext: string;
  scope: ExtensibilityScope;
  targetCoreObjects: CoreFinancialObjectKind[];
  governedFoundations: GovernedFoundationKind[];
  templateIds: string[];
  evidenceTags: string[];
  guardrails: string[];
}

export interface ExtensibilityCatalog {
  schemaVersion: string;
  generatedAt: string;
  stableCoreObjects: StableCoreObjectContract[];
  configurableLayers: ExtensibilityLayerContract[];
  governedFoundations: GovernedFoundationContract[];
  registrations: ExtensibilityRegistration[];
}

export type FinancialRecordExplorerId = "ledger" | "portfolio" | "security-instrument" | "report-line-provenance";

export type FinancialRecordExplorerTone = "Default" | "Success" | "Warning" | "Danger" | "Info";

export interface FinancialRecordExplorerDto {
  explorerId: FinancialRecordExplorerId | string;
  title: string;
  description: string;
  sourceState: string;
  isBlocked: boolean;
  blockedReason: string;
  scopeItems: FinancialRecordExplorerScopeItemDto[];
  savedViews: FinancialRecordExplorerSavedViewDto[];
  summaryItems: FinancialRecordExplorerSummaryItemDto[];
  filters: FinancialRecordExplorerFilterDto[];
  columns: FinancialRecordExplorerColumnDto[];
  rows: FinancialRecordExplorerRowDto[];
  selectedRecord: FinancialRecordExplorerSelectedRecordDto | null;
  proofActions: FinancialRecordExplorerProofActionDto[];
  recordGraph: FinancialRecordExplorerRecordGraphDto;
}

export interface FinancialRecordExplorerScopeItemDto {
  label: string;
  value: string;
  tone: FinancialRecordExplorerTone;
}

export interface FinancialRecordExplorerSavedViewDto {
  viewId: string;
  label: string;
  description: string;
  isSystem: boolean;
  isActive: boolean;
  filters: FinancialRecordExplorerFilterDto[];
  searchText: string;
  columnIds?: string[] | null;
}

export interface FinancialRecordExplorerSummaryItemDto {
  label: string;
  value: string;
  detail: string;
  tone: FinancialRecordExplorerTone;
}

export interface FinancialRecordExplorerFilterDto {
  filterId: string;
  label: string;
  value: string;
  operator: string;
  tone: FinancialRecordExplorerTone;
}

export interface FinancialRecordExplorerColumnDto {
  columnId: string;
  header: string;
  cellKind: string;
  width: number;
  isRightAligned: boolean;
}

export interface FinancialRecordExplorerCellDto {
  columnId: string;
  displayValue: string;
  rawValue: string;
  tone: FinancialRecordExplorerTone;
  linkHref: string;
}

export interface FinancialRecordExplorerRowDto {
  recordId: string;
  recordType: string;
  label: string;
  source: string;
  status: string;
  tone: FinancialRecordExplorerTone;
  cells: FinancialRecordExplorerCellDto[];
  detail: FinancialRecordExplorerSelectedRecordDto;
}

export interface FinancialRecordExplorerSelectedRecordDto {
  recordId: string;
  recordType: string;
  title: string;
  subtitle: string;
  description: string;
  tone: FinancialRecordExplorerTone;
  fields: FinancialRecordExplorerSummaryItemDto[];
  proofActions: FinancialRecordExplorerProofActionDto[];
  usedIn: FinancialRecordExplorerRelationshipDto[];
  impacts: FinancialRecordExplorerRelationshipDto[];
  fullRecordHref: string;
}

export interface FinancialRecordExplorerProofActionDto {
  actionId: string;
  label: string;
  description: string;
  href: string;
  isEnabled: boolean;
  disabledReason: string;
  tone: FinancialRecordExplorerTone;
}

export interface FinancialRecordExplorerRecordGraphDto {
  nodes: FinancialRecordExplorerGraphNodeDto[];
  edges: FinancialRecordExplorerGraphEdgeDto[];
}

export interface FinancialRecordExplorerGraphNodeDto {
  nodeId: string;
  label: string;
  nodeType: string;
  tone: FinancialRecordExplorerTone;
  href: string;
}

export interface FinancialRecordExplorerGraphEdgeDto {
  sourceNodeId: string;
  targetNodeId: string;
  label: string;
  tone: FinancialRecordExplorerTone;
}

export interface FinancialRecordExplorerRelationshipDto {
  relationshipId: string;
  label: string;
  description: string;
  href: string;
  tone: FinancialRecordExplorerTone;
}

export interface FinancialRecordExplorerSavedViewSaveRequestDto {
  label: string;
  description: string;
  searchText: string;
  filters: FinancialRecordExplorerFilterDto[];
  columnIds?: string[] | null;
}

export type LedgerMappingSource =
  | "AccountAssignment"
  | "InvestmentPortfolioAssignment"
  | "SleeveAssignment"
  | "VehicleAssignment"
  | "FundAssignment"
  | "EntityAssignment"
  | "AccountLedgerReference"
  | "Unassigned";

export interface LedgerGroupSummary {
  ledgerGroupId: string;
  displayName: string;
  accountIds: string[];
  investmentPortfolioIds: string[];
  clientIds: string[];
  fundIds: string[];
  sleeveIds: string[];
  vehicleIds: string[];
  sharedDataAccess?: Record<string, unknown> | null;
}

export interface LedgerMappingResolution {
  ledgerGroupId: string;
  source: LedgerMappingSource;
  sourceNodeId: string | null;
  sourceNodeKind: string | null;
  sourceReference: string | null;
  requiresUserMapping: boolean;
  issueCodes: string[];
}

export interface LedgerMappingAccount {
  accountId: string;
  accountCode: string;
  displayName: string;
  accountType: string;
  operationalStatus: string;
  baseCurrency: string;
  institution: string | null;
  fundId: string | null;
  sleeveId: string | null;
  vehicleId: string | null;
  entityId: string | null;
  portfolioId: string | null;
  ledgerReference: string | null;
  mapping: LedgerMappingResolution;
  recommendedAction: string;
}

export interface LedgerMappingWorkbench {
  asOf: string;
  organization?: Record<string, unknown> | null;
  business?: Record<string, unknown> | null;
  accountCount: number;
  mappedAccountCount: number;
  unmappedAccountCount: number;
  ledgerGroups: LedgerGroupSummary[];
  accounts: LedgerMappingAccount[];
  sharedDataAccess?: Record<string, unknown> | null;
}

export interface LedgerMappingAssignmentRequest {
  accountId: string;
  ledgerGroupId: string;
  requestedBy: string;
  rationale: string;
  effectiveFrom?: string | null;
  correlationId?: string | null;
  assignmentId?: string | null;
}

export interface LedgerMappingAssignmentAuditEvent {
  auditId: string;
  eventType: string;
  occurredAtUtc: string;
  actor: string;
  rationale: string;
  correlationId: string;
  accountId: string;
  accountCode: string;
  fromLedgerGroupId: string | null;
  toLedgerGroupId: string;
  assignmentId: string;
}

export interface LedgerMappingAssignmentResult {
  assignment: Record<string, unknown>;
  account: LedgerMappingAccount;
  auditEvent: LedgerMappingAssignmentAuditEvent;
  workbench: LedgerMappingWorkbench;
}

export interface PermissionCatalogItem {
  name: string;
  value: number;
  group: string;
  description: string;
}

export interface RolePermissionProfile {
  role: string;
  displayName: string;
  description: string;
  isBuiltIn: boolean;
  permissions: string[];
  permissionMask: number;
  baseRole?: string | null;
  createdBy?: string | null;
  createdAtUtc?: string | null;
  updatedBy?: string | null;
  updatedAtUtc?: string | null;
  lastRationale?: string | null;
  lastAuditId?: string | null;
}

export interface RolePermissionCatalog {
  roles: RolePermissionProfile[];
  permissions: PermissionCatalogItem[];
}

export interface RolePermissionProfileUpsertRequest {
  profileName: string;
  displayName: string;
  description?: string | null;
  baseRole: string;
  permissionNames: string[];
  requestedBy: string;
  rationale: string;
  correlationId?: string | null;
}

export interface RolePermissionProfileAuditEvent {
  auditId: string;
  eventType: string;
  occurredAtUtc: string;
  actor: string;
  rationale: string;
  correlationId: string;
  profileName: string;
  baseRole: string;
  permissionNames: string[];
  permissionMask: number;
}

export interface RolePermissionProfileUpsertResult {
  profile: RolePermissionProfile;
  catalog: RolePermissionCatalog;
  auditEvent: RolePermissionProfileAuditEvent;
}

export interface UserAccount {
  username: string;
  role: string;
  roleProfileName?: string | null;
  permissionNames: string[];
  permissionMask: number;
  isDisabled: boolean;
  passwordResetRequired: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  createdBy: string;
  updatedBy: string;
  lastPasswordResetAtUtc?: string | null;
  disabledAtUtc?: string | null;
  disabledBy?: string | null;
  lastAuditId?: string | null;
}

export interface UserAccountUpsertRequest {
  username: string;
  role: string;
  roleProfileName?: string | null;
  permissionNames?: string[] | null;
  newPassword?: string | null;
  passwordHash?: string | null;
  isDisabled?: boolean | null;
  passwordResetRequired: boolean;
  requestedBy: string;
  rationale: string;
  correlationId?: string | null;
}

export interface UserPasswordResetRequest {
  username: string;
  newPassword?: string | null;
  passwordHash?: string | null;
  passwordResetRequired: boolean;
  revokeSessions: boolean;
  requestedBy: string;
  rationale: string;
  correlationId?: string | null;
}

export interface UserAccountDisableRequest {
  username: string;
  isDisabled: boolean;
  revokeSessions: boolean;
  requestedBy: string;
  rationale: string;
  correlationId?: string | null;
}

export interface UserAccountAuditEvent {
  auditId: string;
  eventType: string;
  occurredAtUtc: string;
  actor: string;
  username: string;
  rationale: string;
  correlationId: string;
  role: string;
  permissionNames: string[];
  permissionMask: number;
  isDisabled: boolean;
  passwordResetRequired: boolean;
  revokedSessionCount: number;
}

export interface UserAccountMutationResult {
  account: UserAccount;
  auditEvent: UserAccountAuditEvent;
  revokedSessionCount: number;
}

export interface UserSessionRevokeRequest {
  username?: string | null;
  revokeAll: boolean;
  requestedBy: string;
  rationale: string;
  correlationId?: string | null;
}

export interface UserSessionRevokeResult {
  auditId: string;
  occurredAtUtc: string;
  actor: string;
  username?: string | null;
  revokedAll: boolean;
  revokedSessionCount: number;
  rationale: string;
  correlationId: string;
}

export type AccessPrincipalKind = "User" | "Group";

export type AccessScopeKind =
  | "Global"
  | "Organization"
  | "Business"
  | "Client"
  | "Fund"
  | "Sleeve"
  | "Vehicle"
  | "InvestmentPortfolio"
  | "LegalEntity"
  | "Account";

export interface UserAccessAssignment {
  assignmentId: string;
  principalId: string;
  principalKind: AccessPrincipalKind;
  scopeKind: AccessScopeKind;
  scopeId?: string | null;
  role: string;
  roleProfileName?: string | null;
  permissionNames: string[];
  permissionMask: number;
  effectiveFrom: string;
  effectiveTo?: string | null;
  grantedBy: string;
  rationale: string;
  correlationId: string;
  version: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  revokedBy?: string | null;
  revokedAtUtc?: string | null;
  revocationReason?: string | null;
  lastAuditId?: string | null;
  approvalLimitAmount?: number | null;
  approvalLimitCurrency?: string | null;
  segregationOfDutiesRule?: string | null;
}

export interface UserAccessAssignmentCreateRequest {
  principalId: string;
  principalKind: AccessPrincipalKind;
  scopeKind: AccessScopeKind;
  scopeId?: string | null;
  role: string;
  roleProfileName?: string | null;
  permissionNames: string[];
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  requestedBy: string;
  rationale: string;
  approvalLimitAmount?: number | null;
  approvalLimitCurrency?: string | null;
  segregationOfDutiesRule?: string | null;
  correlationId?: string | null;
}

export interface UserAccessAssignmentRevokeRequest {
  assignmentId: string;
  expectedVersion: number;
  requestedBy: string;
  rationale: string;
  correlationId?: string | null;
}

export interface UserAccessAssignmentQuery {
  principalId?: string | null;
  scopeKind?: AccessScopeKind | null;
  scopeId?: string | null;
  includeRevoked?: boolean;
}

export interface UserAccessAssignmentAuditEvent {
  auditId: string;
  eventType: string;
  occurredAtUtc: string;
  actor: string;
  rationale: string;
  correlationId: string;
  assignmentId: string;
  principalId: string;
  scopeKind: AccessScopeKind;
  scopeId?: string | null;
  permissionNames: string[];
  permissionMask: number;
  version: number;
  approvalLimitAmount?: number | null;
  approvalLimitCurrency?: string | null;
  segregationOfDutiesRule?: string | null;
}

export interface UserAccessAssignmentMutationResult {
  assignment: UserAccessAssignment;
  auditEvent: UserAccessAssignmentAuditEvent;
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
export interface StrategyRunRecord {
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
  approvalChecklist?: string[] | null;
  evidenceReferences?: string[] | null;
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

export interface UpdateExecutionPositionLimitRequest {
  maxPositionSize: number | null;
  reason?: string | null;
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
  | "LedgerPeriodClose"
  | "BrokerExecutionReconciliation";

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
  viewStateEnvelope?: string | null;
}

export interface WorkflowPresetLibrary {
  generatedAt: string;
  presets: WorkflowPreset[];
}

export interface OperatorWorkflowHomeSummary {
  generatedAt: string;
  hasOperatingContext: boolean;
  operatingContextLabel: string;
  fundDisplayName: string;
  workspaces: WorkspaceWorkflowSummary[];
  assuranceScore?: MeridianAssuranceScore | null;
}

export interface WorkspaceWorkflowSummary {
  workspaceId: string;
  workspaceTitle: string;
  statusLabel: string;
  statusDetail: string;
  statusTone: string;
  nextAction: WorkflowNextAction;
  primaryBlocker: WorkflowBlockerSummary;
  evidence: WorkflowEvidenceBadge[];
}

export interface WorkflowNextAction {
  label: string;
  detail: string;
  targetPageTag: string;
  tone: string;
}

export interface WorkflowBlockerSummary {
  code: string;
  label: string;
  detail: string;
  tone: string;
  isBlocking: boolean;
}

export interface WorkflowEvidenceBadge {
  label: string;
  value: string;
  tone: string;
}

export interface WorkflowPresetSaveRequest {
  presetId?: string | null;
  name: string;
  description?: string | null;
  workflowId: string;
  actionId?: string | null;
  tags?: string[] | null;
  isPinned: boolean;
  viewStateEnvelope?: string | null;
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
export type OperationsReconciliationLaneStatus = "Missing" | "Ready" | "ReviewRequired" | "Blocked";
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
  ledgerBookId?: string | null;
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
  escalationLevel?: string | null;
  escalationReason?: string | null;
  escalatedAtUtc?: string | null;
  slaState?: string | null;
  slaDueAtUtc?: string | null;
  materiality?: number | null;
  rootCauseCode?: string | null;
  approvalState?: string | null;
  blockedOutputs?: string[] | null;
  measures?: ReconciliationBreakMeasure[] | null;
  disposition?: ReconciliationBreakDisposition | null;
  dispositionReason?: string | null;
  supersedingBreakId?: string | null;
  dispositionApprovedBy?: string | null;
  dispositionApprovalReference?: string | null;
  dispositionEvidenceHash?: string | null;
  disposedAtUtc?: string | null;
}

export interface OperationsReconciliationLaneSummary {
  laneId: string;
  label: string;
  status: OperationsReconciliationLaneStatus;
  isReady: boolean;
  breakCount: number;
  summary: string;
  routeHint: string | null;
  evidenceLinks: OperationsEvidenceLink[];
  requiredActions?: string[] | null;
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

export type FundAuditEvidenceCategoryKey =
  | "SourceRecords"
  | "NormalizedActivity"
  | "ReconciliationCases"
  | "LedgerEvidence"
  | "Approvals"
  | "ReportPack"
  | "Exports"
  | "RestatementLineage";

export interface FundAuditEvidenceCategorySummary {
  key: FundAuditEvidenceCategoryKey;
  label: string;
  isComplete: boolean;
  status: string;
  evidenceCount: number;
  evidenceIds: string[];
  route: string | null;
}

export interface FundAuditPackReadiness {
  isComplete: boolean;
  generatedInSeconds: number;
  slaTargetSeconds: number;
  slaMet: boolean;
  missingEvidenceCategories: FundAuditEvidenceCategoryKey[];
  warnings: string[];
  evidenceCategorySummaries: FundAuditEvidenceCategorySummary[];
}

export interface OperationsAccountingRecordSummary {
  recordId: string;
  isAuditReady: boolean;
  completeCategoryCount: number;
  requiredCategoryCount: number;
  summary: string;
  evidenceCategories: OperationsAccountingRecordEvidenceCategory[];
  evidenceLinks: OperationsEvidenceLink[];
  auditPackReadiness?: FundAuditPackReadiness | null;
}

export interface OperationsAccountingRecordEvidenceCategory {
  key: string;
  label: string;
  isComplete: boolean;
  status: string;
  routeHint: string | null;
  evidenceLinks: OperationsEvidenceLink[];
  requiredEvidence?: string[] | null;
}

export interface OperationsDashboardMetric {
  metricId: string;
  label: string;
  value: string;
  status: EvidenceStatus;
  detail: string;
  routeHint: string | null;
  evidenceLinks: OperationsEvidenceLink[];
  requiredActions?: string[] | null;
}

export interface OperationsDashboardSummary {
  dashboardId: string;
  stage: string;
  status: EvidenceStatus;
  isReady: boolean;
  readyMetricCount: number;
  totalMetricCount: number;
  summary: string;
  metrics: OperationsDashboardMetric[];
  evidenceLinks: OperationsEvidenceLink[];
  requiredActions?: string[] | null;
}

export interface OperationsEvidencePackageSummary {
  packageId: string;
  label: string;
  status: EvidenceStatus;
  isReady: boolean;
  summary: string;
  routeHint: string | null;
  completeCategoryCount: number;
  requiredCategoryCount: number;
  evidenceLinkCount: number;
  evidenceLinks: OperationsEvidenceLink[];
  requiredActions?: string[] | null;
}

export interface OperationsReviewedAutomationSummary {
  summaryId: string;
  stage: string;
  status: EvidenceStatus;
  requiresHumanReview: boolean;
  summary: string;
  allowedUseCases: string[];
  prohibitedActions: string[];
  evidenceLinks: OperationsEvidenceLink[];
  requiredActions?: string[] | null;
  artifacts?: OperationsReviewedAutomationArtifact[] | null;
}

export interface OperationsReviewedAutomationArtifact {
  artifactId: string;
  artifactKind: string;
  title: string;
  status: EvidenceStatus;
  requiresHumanReview: boolean;
  confidencePercent?: number | null;
  sourceSummary: string;
  suggestedOperatorAction: string;
  blockedMaterialAction: string;
  evidenceLinks: OperationsEvidenceLink[];
  reviewChecklist?: string[] | null;
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
  closePackage: OperationsClosePackagePublication | null;
  dashboardSummary?: OperationsDashboardSummary | null;
  evidencePackages?: OperationsEvidencePackageSummary[] | null;
  reviewedAutomation?: OperationsReviewedAutomationSummary | null;
  accountingRecordSummary?: OperationsAccountingRecordSummary | null;
  reconciliationLanes?: OperationsReconciliationLaneSummary[] | null;
  evidenceLinks: OperationsEvidenceLink[];
  blockers: OperationsWorkflowBlocker[];
}

export type OperationsActionOrigin =
  | "HumanOperator"
  | "AutomationSuggestion"
  | "AssistantDraft"
  | "AutomationAssistant";

export interface OperationsStartWorkflowRequest {
  fundAccountId: string;
  periodId: string;
  securityMasterSnapshotId?: string | null;
  brokerSource?: string | null;
  actor: string;
  rationale?: string | null;
  correlationId?: string | null;
  evidenceLinks?: OperationsEvidenceLink[] | null;
}

export interface OperationsTransitionRequest {
  expectedVersion: number;
  actor: string;
  rationale?: string | null;
  correlationId?: string | null;
  evidenceLinks?: OperationsEvidenceLink[] | null;
  evidenceReferenceIds?: string[] | null;
}

export interface OperationsGatePostureRequest extends OperationsTransitionRequest {
  providerAccountLinked?: boolean | null;
  providerSyncStale?: boolean | null;
  securityCoverageIssueCount?: number | null;
  securityAccountingIssueCount?: number | null;
  ledgerPreviewAvailable?: boolean | null;
  ledgerDraftBalanced?: boolean | null;
  ledgerPostingValidated?: boolean | null;
  openCriticalBreakCount?: number | null;
  openNonCriticalBreakCount?: number | null;
  reportPackReady?: boolean | null;
  reportPackId?: string | null;
  providerRequiredCapabilityGaps?: string[] | null;
  providerDegradedCapabilityGaps?: string[] | null;
}

export interface OperationsSecurityMasterResolveRequest {
  expectedVersion: number;
  actor: string;
  rationale?: string | null;
  correlationId?: string | null;
  unresolvedInstrumentCount?: number;
  overrideRequestCount?: number;
  overridesApproved?: boolean;
  missingAccountingTermCount?: number;
  evidenceLinks?: OperationsEvidenceLink[] | null;
}

export interface OperationsSecurityMasterOverrideApprovalRequest {
  expectedVersion: number;
  actor: string;
  overrideId: string;
  rationale: string;
  policyReference: string;
  expiresOn?: string | null;
  correlationId?: string | null;
  evidenceLinks?: OperationsEvidenceLink[] | null;
  actionOrigin?: OperationsActionOrigin | null;
}

export interface OperationsLedgerDraftRequest {
  expectedVersion: number;
  actor: string;
  previewId: string;
  isBalanced: boolean;
  rationale?: string | null;
  correlationId?: string | null;
  hasSecurityMasterProvenance?: boolean;
  hasIdempotencyKey?: boolean;
  ledgerBatchId?: string | null;
  evidenceLinks?: OperationsEvidenceLink[] | null;
  hasSecurityMasterApproval?: boolean;
  hasLedgerMappings?: boolean;
}
