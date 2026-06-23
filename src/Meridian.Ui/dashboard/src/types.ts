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
}

export interface EvidenceVaultRequestListQuery {
  requestListKind?: string | null;
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

export interface ExternalGlMappingProfile {
  profileId: string;
  providerId: string;
  displayName: string;
  updatedAtUtc: string;
  dimensionMappings: DimensionMappingProfile[];
  accountMappings: Record<string, string>;
  certificationState: AccountingCertificationState;
}

export interface AccountingSystemMappingProfileUpsertRequest {
  profile: ExternalGlMappingProfile;
  actor: string;
  providerId?: string | null;
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  correlationId?: string | null;
  evidenceLinks?: string[];
}

export interface AccountingSystemExportPackageRequest {
  actor: string;
  providerId?: string | null;
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  periodStart?: string | null;
  periodEnd?: string | null;
  mappingProfileId?: string | null;
  journalEntryIds?: string[];
  requireBalancedReconciliation?: boolean;
  evidenceLinks?: string[];
  correlationId?: string | null;
}

export interface CertifyAccountingSystemExportPackageRequest {
  exportPackageId: string;
  actor: string;
  notes: string;
  evidenceLinks?: string[];
  correlationId?: string | null;
}

  export interface ExternalGlExportCertification {
    certificationId: string;
    state: AccountingCertificationState;
    actor: string;
    recordedAtUtc: string;
    summary: string;
    evidenceLinks: string[];
  }

  export interface ExternalGlExportLine {
    exportLineId: string;
    reconciliationRowId: string;
    sourceStatus: AccountingSystemReconciliationStatus;
    meridianAccountCode: string;
    externalAccountId: string;
    accountName: string;
    currency: string;
    debit: number;
    credit: number;
    netAmount: number;
    meridianDimensions?: LedgerDimensionSet | null;
    externalDimensions?: LedgerDimensionSet | null;
    evidenceLinks: string[];
  }

  export interface ExternalGlExportPackage {
    exportPackageId: string;
    providerId: string;
    fundProfileId: string;
    ledgerBookId: string | null;
    periodStart: string;
    periodEnd: string;
  createdAtUtc: string;
  createdBy: string;
  postingEnabled: boolean;
  postingDisabledReason: string;
  journalEntryIds: string[];
    evidenceLinks: string[];
    certification?: ExternalGlExportCertification | null;
    validationIssues: AccountingConfigurationValidationIssue[];
    generatedLines?: ExternalGlExportLine[];
    mappingProfileId?: string | null;
    reconciliationId?: string | null;
    requireBalancedReconciliation?: boolean;
    reconciliationSafeguardState?: ExternalGlExportReconciliationSafeguardState;
    reconciliationSafeguardIssueCodes?: string[];
  }

  export interface ExternalGlExportPackageManifest {
    exportPackageId: string;
    providerId: string;
    fundProfileId: string;
    ledgerBookId: string | null;
    periodStart: string;
    periodEnd: string;
    certificationState: AccountingCertificationState;
    generatedAtUtc: string;
    contentHash: string;
    contentType: string;
    fileName: string;
    externalPostingAllowed: boolean;
    postingDisabledReason: string;
    payload: string;
    generatedLines: ExternalGlExportLine[];
    evidenceLinks: string[];
    validationIssues: AccountingConfigurationValidationIssue[];
    mappingProfileId?: string | null;
    reconciliationId?: string | null;
    requireBalancedReconciliation?: boolean;
    reconciliationSafeguardState?: ExternalGlExportReconciliationSafeguardState;
    reconciliationSafeguardIssueCodes?: string[];
  }

export type CloseTaskStatus = "NotStarted" | "WaitingOnDependency" | "InProgress" | "ReadyForSignOff" | "SignedOff" | "Blocked";

export interface CloseDependency {
  dependencyId: string;
  dependsOnTaskId: string;
  reason: string;
}

export interface CloseSignOff {
  signOffId: string;
  role: string;
  actor: string | null;
  approvalState: ManualJournalEntryStatus;
  signedAtUtc: string | null;
  evidenceLinks: string[];
  notes?: string | null;
}

export interface CloseSignOffRequirement {
  requirementId: string;
  role: string;
  requiredApprovalCount: number;
  approvedCount: number;
  isSatisfied: boolean;
  evidenceRequirement: string;
}

export interface MaterialityPolicy {
  policyId: string;
  amountThreshold: number;
  percentThreshold: number;
  currency: string;
  reviewRole: string;
  requiresLateAdjustmentApproval: boolean;
}

export interface CloseTask {
  taskId: string;
  displayName: string;
  status: CloseTaskStatus;
  owner: string;
  dueDate: string;
  dependencies: CloseDependency[];
  signOffs: CloseSignOff[];
  evidenceLinks: string[];
  blockerReason?: string | null;
  signOffRequirements?: CloseSignOffRequirement[] | null;
}

export interface CloseCalendarMilestone {
  milestoneId: string;
  taskId: string;
  displayName: string;
  owner: string;
  dueDate: string;
  status: CloseTaskStatus;
  isBlocked: boolean;
  isSatisfied: boolean;
  isPeriodLocked: boolean;
  dependencyCount: number;
  requiredSignOffCount: number;
  approvedSignOffCount: number;
  evidenceLinks: string[];
  blockerReason?: string | null;
}

export interface LateAdjustmentRequest {
  requestId: string;
  journalEntryId: string;
  requestedBy: string;
  requestedAtUtc: string;
  amount: number;
  currency: string;
  reason: string;
  approvalState: ManualJournalEntryStatus;
  materialityPolicy: MaterialityPolicy;
  evidenceLinks: string[];
  decidedBy?: string | null;
  decidedAtUtc?: string | null;
  decisionNotes?: string | null;
}

export interface CreateLateAdjustmentRequest {
  workflowId: string;
  journalEntryId: string;
  amount: number;
  currency: string;
  reason: string;
  requestedBy: string;
  evidenceLinks?: string[];
  correlationId?: string | null;
}

export interface ReviewLateAdjustmentRequest {
  workflowId: string;
  requestId: string;
  decision: ManualJournalEntryStatus;
  actor: string;
  notes: string;
  evidenceLinks?: string[];
  correlationId?: string | null;
}

export interface SignOffCloseTaskRequest {
  workflowId: string;
  taskId: string;
  role: string;
  decision: ManualJournalEntryStatus;
  actor: string;
  notes: string;
  evidenceLinks?: string[];
  correlationId?: string | null;
}

export interface CloseTaskConfiguration {
  taskId: string;
  displayName?: string | null;
  owner?: string | null;
  dueDate?: string | null;
  requiredApprovalCount?: number | null;
  requiredEvidence?: string | null;
  dependsOnTaskIds?: string[] | null;
}

export interface UpsertClosePeriodPlanConfigurationRequest {
  workflowId: string;
  materialityPolicy?: MaterialityPolicy | null;
  taskConfigurations?: CloseTaskConfiguration[] | null;
  actor?: string | null;
  evidenceLinks?: string[] | null;
  correlationId?: string | null;
  actionOrigin?: OperationsActionOrigin | null;
}

export interface ClosePeriodPlan {
  closePlanId: string;
  fundProfileId: string;
  ledgerBookId: string | null;
  periodId: string;
  periodStart: string;
  periodEnd: string;
  closeDueDate: string;
  isPeriodLocked: boolean;
  tasks: CloseTask[];
  lateAdjustments: LateAdjustmentRequest[];
  materialityPolicy: MaterialityPolicy;
  validationIssues: AccountingConfigurationValidationIssue[];
  closeCalendar?: CloseCalendarMilestone[] | null;
}

export interface ReportCertification {
  certificationId: string;
  state: AccountingCertificationState;
  actor: string;
  recordedAtUtc: string;
  summary: string;
  evidenceLinks: string[];
}

  export interface RestatementWorkflow {
    restatementId: string;
    priorPackageId: string;
    reasonCode: string;
  approvalState: ManualJournalEntryStatus;
  requestedBy: string;
  requestedAtUtc: string;
    evidenceLinks: string[];
  }

  export interface ReportLineProvenance {
    statementId: string;
    lineId: string;
    lineLabel: string;
    sourceKind: string;
    amount: number;
    currency: string;
    dimensions: LedgerDimensionSet;
    evidenceLinks: string[];
  }

  export interface ReportExportArtifact {
    artifactId: string;
    artifactKind: string;
    displayName: string;
    format: string;
    route: string;
    certificationState: AccountingCertificationState;
    generatedAtUtc: string;
    contentHash: string;
    evidenceLinks: string[];
    sourceStatementId?: string | null;
    dimensionScope?: ReportDimensionScope | null;
  }

  export interface ReportExportArtifactManifest {
    packageId: string;
    artifactId: string;
    artifactKind: string;
    displayName: string;
    format: string;
    route: string;
    certificationState: AccountingCertificationState;
    generatedAtUtc: string;
    contentHash: string;
    contentType: string;
    fileName: string;
    externalPostingAllowed: boolean;
    payload: string;
    evidenceLinks: string[];
    sourceStatementId?: string | null;
    dimensionScope?: ReportDimensionScope | null;
  }

  export interface ReportDimensionScope {
    ledgerBookId?: string | null;
    dimensions: LedgerDimensionSet;
    hasExplicitScope: boolean;
    scopeHash: string;
    certificationEvidenceToken: string;
    scopedDimensionKeys: string[];
  }

  export interface FinancialStatementPackage {
    packageId: string;
    fundProfileId: string;
    ledgerBookId: string | null;
  periodId: string;
  certificationState: AccountingCertificationState;
  statementIds: string[];
    evidenceLinks: string[];
    certification?: ReportCertification | null;
    restatement?: RestatementWorkflow | null;
    lineProvenance?: ReportLineProvenance[];
  }

export interface InvestorCapitalStatement {
  statementId: string;
  fundProfileId: string;
  capitalAccountId: string;
  investorId: string | null;
  periodId: string;
  beginningCapital: number;
  contributions: number;
  distributions: number;
  realizedGainLoss: number;
  endingCapital: number;
  currency: string;
  certificationState: AccountingCertificationState;
  evidenceLinks: string[];
}

export interface RealizedGainLossReport {
  reportId: string;
  fundProfileId: string;
  ledgerBookId: string | null;
  periodId: string;
  dimensions: LedgerDimensionSet;
  realizedGainLoss: number;
  currency: string;
  certificationState: AccountingCertificationState;
  evidenceLinks: string[];
}

export interface NavPackage {
  packageId: string;
  fundProfileId: string;
  ledgerBookId: string | null;
  periodId: string;
  nav: number;
  currency: string;
  certificationState: AccountingCertificationState;
  evidenceLinks: string[];
  certification?: ReportCertification | null;
  restatement?: RestatementWorkflow | null;
}

export interface AccountingReportPackageRequest {
  fundProfileId: string;
  periodId: string;
  actor: string;
  ledgerBookId?: string | null;
  closeWorkflowId?: string | null;
  capitalAccountId?: string | null;
  investorId?: string | null;
  beginningCapital?: number;
  contributions?: number;
  distributions?: number;
  realizedGainLoss?: number;
  nav?: number;
  currency?: string;
  restatementReasonCode?: string | null;
  priorPackageId?: string | null;
  evidenceLinks?: string[];
  correlationId?: string | null;
}

export interface CertifyAccountingReportPackageRequest {
  packageId: string;
  actor: string;
  notes: string;
  evidenceLinks?: string[];
  correlationId?: string | null;
}

export type AccountingReadinessState = "NotStarted" | "NeedsAttention" | "Blocked" | "ReadyForReview" | "Certified";

export interface AccountingCloseReadinessItem {
  itemId: string;
  category: string;
  label: string;
  state: AccountingReadinessState;
  summary: string;
  requiredAction: string;
  blockingIssueCount: number;
  evidenceLinks: string[];
  blockingIssues: AccountingConfigurationValidationIssue[];
  ledgerBookId?: string | null;
  dimensions?: LedgerDimensionSet | null;
}

  export interface AccountingReportPackageBundle {
  financialStatements: FinancialStatementPackage;
  investorCapitalStatements: InvestorCapitalStatement[];
  realizedGainLoss: RealizedGainLossReport;
  navPackage: NavPackage;
  certification: ReportCertification;
  validationIssues: AccountingConfigurationValidationIssue[];
  exportArtifacts?: ReportExportArtifact[] | null;
  closeWorkflowId?: string | null;
  closeReadinessItems?: AccountingCloseReadinessItem[] | null;
  dimensionScope?: ReportDimensionScope | null;
}

export interface AccountingSystemProvider {
  providerId: string;
  displayName: string;
  state: AccountingSystemProviderState;
  requiresCredentials: boolean;
  supportsChartOfAccounts: boolean;
  supportsJournalEntries: boolean;
  supportsTrialBalance: boolean;
  supportsPosting: boolean;
  statusLabel: string;
  statusDetail: string;
  evidenceKinds: string[];
  connection?: AccountingSystemConnectionMetadata | null;
  mappingRequirements: AccountingSystemProviderMappingRequirement[];
}

export interface AccountingSystemProviderMappingRequirement {
  requirementId: string;
  label: string;
  requiredEvidenceKind: string;
  requiredAction: string;
  requiredForGuardedExport: boolean;
}

export interface AccountingSystemConnectionMetadata {
  providerId: string;
  environment: string | null;
  companyId: string | null;
  companyName: string | null;
  hasLocalConfig: boolean;
  hasRefreshToken: boolean;
  lastConnectedAtUtc: string | null;
  statusLabel: string;
  statusDetail: string;
  missingFields: string[];
}

export interface AccountingSystemImportRequest {
  providerId?: string | null;
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  periodStart?: string | null;
  periodEnd?: string | null;
  persistPreview?: boolean;
}

export interface AccountingSystemImportSummary {
  importId: string;
  providerId: string;
  providerDisplayName: string;
  fundProfileId: string;
  ledgerBookId: string | null;
  state: AccountingSystemImportState;
  periodStart: string;
  periodEnd: string;
  importedAtUtc: string;
  chartAccountCount: number;
  journalEntryCount: number;
  trialBalanceLineCount: number;
  evidenceReferences: string[];
  warnings: string[];
}

export interface AccountingSystemChartAccount {
  externalAccountId: string;
  accountCode: string;
  displayName: string;
  accountType: string;
  currency: string;
  isActive: boolean;
  parentExternalAccountId: string | null;
  evidenceRef: string | null;
}

export interface AccountingSystemJournalLine {
  externalLineId: string;
  externalAccountId: string;
  accountCode: string;
  description: string;
  debit: number;
  credit: number;
  currency: string;
  evidenceRef: string | null;
}

export interface AccountingSystemJournalEntry {
  externalJournalEntryId: string;
  accountingDate: string;
  description: string;
  currency: string;
  totalDebits: number;
  totalCredits: number;
  lines: AccountingSystemJournalLine[];
  evidenceRef: string | null;
}

export interface AccountingSystemTrialBalanceLine {
  externalAccountId: string;
  accountCode: string;
  accountName: string;
  accountType: string;
  debit: number;
  credit: number;
  currency: string;
  asOfDate: string;
  evidenceRef: string | null;
}

export interface AccountingSystemImportDetail {
  summary: AccountingSystemImportSummary;
  chartAccounts: AccountingSystemChartAccount[];
  journalEntries: AccountingSystemJournalEntry[];
  trialBalance: AccountingSystemTrialBalanceLine[];
}

export interface AccountingSystemReconciliationRow {
  rowId: string;
  accountCode: string;
  accountName: string;
  currency: string;
  status: AccountingSystemReconciliationStatus;
  externalDebit: number;
  externalCredit: number;
  meridianDebit: number;
  meridianCredit: number;
  variance: number;
  detail: string;
  evidenceRef: string | null;
  externalEvidenceReferences?: string[];
  meridianEvidenceReferences?: string[];
  evidenceReferences?: string[];
}

export interface AccountingSystemReconciliationEvidencePackage {
  packageId: string;
  label: string;
  status: AccountingSystemEvidencePackageStatus;
  evidenceReferenceCount: number;
  evidenceReferences: string[];
  requiredActions: string[];
}

export interface AccountingSystemReconciliationSummary {
  reconciliationId: string;
  importId: string;
  providerId: string;
  fundProfileId: string;
  periodStart: string;
  periodEnd: string;
  generatedAtUtc: string;
  matchedCount: number;
  breakCount: number;
  totalExternalDebits: number;
  totalExternalCredits: number;
  totalMeridianDebits: number;
  totalMeridianCredits: number;
  postingEnabled: boolean;
  postingDisabledReason: string;
  rows: AccountingSystemReconciliationRow[];
  evidenceReferences: string[];
  evidencePackages?: AccountingSystemReconciliationEvidencePackage[];
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

export interface StrategyWorkspaceResponse {
  metrics: MetricSnapshot[];
  runs: StrategyRunRecord[];
  plotTool?: StrategyPlotToolPayload | null;
}

export interface StrategyRunDrillInLinks {
  equityCurve: string;
  fills: string;
  attribution: string;
  ledger: string | null;
  cashFlows: string;
  continuity: string | null;
}

export interface StrategyBriefingRun {
  runId: string;
  strategyName: string;
  mode: number;
  status: number;
  dataset: string;
  windowLabel: string;
  returnLabel: string;
  sharpeLabel: string;
  lastUpdatedLabel: string;
  notes: string;
  promotionState: number | null;
  netPnl: number | null;
  totalReturn: number | null;
  finalEquity: number | null;
  drillIn: StrategyRunDrillInLinks;
}

export interface StrategySavedComparisonMode {
  runId: string;
  mode: number;
  status: number;
  netPnl: number | null;
  totalReturn: number | null;
  drillIn: StrategyRunDrillInLinks;
}

export interface StrategySavedComparison {
  comparisonId: string;
  strategyName: string;
  modeSummary: string;
  summary: string;
  anchorRunId: string | null;
  modes: StrategySavedComparisonMode[];
}

export interface StrategyBriefingAlert {
  alertId: string;
  title: string;
  summary: string;
  tone: string;
  runId: string | null;
  actionLabel: string | null;
}

export interface StrategyWhatChangedItem {
  changeId: string;
  title: string;
  summary: string;
  category: string;
  timestamp: string;
  relativeTime: string;
  runId: string | null;
}

export interface StrategyBriefingWorkspaceSummary {
  totalRuns: number;
  activeRuns: number;
  promotionCandidates: number;
  positivePnlRuns: number;
  latestRunId: string | null;
  latestStrategyName: string | null;
  hasLedgerCoverage: boolean;
  hasPortfolioCoverage: boolean;
  summary: string;
}

export interface StrategyBriefingResponse {
  workspace: StrategyBriefingWorkspaceSummary;
  insightFeed: {
    feedId: string;
    title: string;
    summary: string;
    generatedAt: string;
    widgets: Array<{
      widgetId: string;
      title: string;
      subtitle: string;
      headline: string;
      tone: string;
      summary: string;
      runId: string | null;
      drillInRoute: string | null;
    }>;
  };
  watchlists: Array<{
    watchlistId: string;
    name: string;
    symbols: string[];
    symbolCount: number;
    isPinned: boolean;
    sortOrder: number;
    accentColor: string | null;
    summary: string | null;
  }>;
  recentRuns: StrategyBriefingRun[];
  savedComparisons: StrategySavedComparison[];
  alerts: StrategyBriefingAlert[];
  whatChanged: StrategyWhatChangedItem[];
}

export interface StrategyPlotToolTabPayload {
  id: string;
  label: string;
  tabId: string;
  panelId: string;
  selected: boolean;
  buttonVariant: "secondary" | "ghost";
  tabIndex: number;
  ariaLabel: string;
}

export interface StrategyPlotToolPayload {
  workspace: unknown;
  statistics: unknown;
  studies: unknown[];
  tabs: StrategyPlotToolTabPayload[];
  activeView?: "workspace" | "statistics";
}

export interface DataProviderRecord {
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
  routingSummary?: DataProviderRoutingSummary | null;
  diagnostics?: DataProviderDiagnosticSummary[] | null;
}

export interface DataProviderRoutingSummary {
  connectionId: string | null;
  providerFamilyId: string | null;
  productionReady: boolean | null;
  certificationFresh: boolean | null;
  bindingCount: number;
  fallbackRouteCount: number;
  healthStatus: string | null;
}

export interface DataProviderDiagnosticSummary {
  id: string;
  label: string;
  status: "pass" | "warning" | "fail" | "pending";
  statusLabel: string;
  detail: string;
}

export interface DataBackfillRecord {
  jobId: string;
  scope: string;
  provider: string;
  status: "Queued" | "Running" | "Review";
  progress: string;
  updatedAt: string;
}

export interface DataExportRecord {
  exportId: string;
  profile: string;
  target: string;
  status: "Ready" | "Running" | "Attention";
  rows: string;
  updatedAt: string;
}

export interface DataUploadTemplateField {
  name: string;
  label: string;
  required: boolean;
  example: string;
  description: string;
}

export interface DataUploadTemplate {
  templateId: string;
  label: string;
  description: string;
  dataDomain: string;
  targetWorkflow: string;
  fileName: string;
  contentType: string;
  headerLine: string;
  fields: DataUploadTemplateField[];
  sampleRows: string[];
  validationNotes: string[];
  sourceKinds?: string[] | null;
  setupChecklist?: string[] | null;
  mappingGuidance?: string[] | null;
}

export interface DataUploadTemplateCatalog {
  templates: DataUploadTemplate[];
  acceptedFileExtensions: string[];
  maxPreviewRows: number;
  maxFileBytes: number;
}

export interface DataUploadValidationIssue {
  severity: "Error" | "Warning" | string;
  field: string;
  message: string;
  rowNumber: number | null;
}

export interface DataUploadPreviewResult {
  uploadId: string;
  templateId: string;
  templateLabel: string;
  fileName: string;
  fileSizeBytes: number;
  contentType: string;
  uploadedBy: string;
  uploadedAtUtc: string;
  retainedPath: string;
  parsedRowCount: number;
  previewRowCount: number;
  headers: string[];
  previewRows: Record<string, string>[];
  issues: DataUploadValidationIssue[];
  status: "ReadyForReview" | "NeedsSchemaRepair" | string;
  nextAction: string;
}

export interface DataWorkspaceResponse {
  metrics: MetricSnapshot[];
  providers: DataProviderRecord[];
  backfills: DataBackfillRecord[];
  exports: DataExportRecord[];
  uploadTemplates?: DataUploadTemplateCatalog | null;
}

export type DataOperationsProviderRecord = DataProviderRecord;
export type DataOperationsProviderRoutingSummary = DataProviderRoutingSummary;
export type DataOperationsProviderDiagnosticSummary = DataProviderDiagnosticSummary;
export type DataOperationsBackfillRecord = DataBackfillRecord;
export type DataOperationsExportRecord = DataExportRecord;
export type DataOperationsWorkspaceResponse = DataWorkspaceResponse;

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
  cashFlow: AccountingCashFlowSummary | null;
}

export interface MultiAssetEvidenceRequirement {
  requirementId: string;
  label: string;
  category: string;
  status: "Ready" | "ReviewRequired" | "Blocked" | string;
  evidenceRoute: string;
  required: boolean;
}

export interface MultiAssetReadinessBlocker {
  code: string;
  severity: "Review" | "Blocker" | string;
  message: string;
  source: string;
  evidenceRoute: string | null;
}

export interface MultiAssetDrillThroughTarget {
  targetId: string;
  targetType: string;
  label: string;
  route: string;
  evidenceLink: string | null;
  status: "Ready" | "ReviewRequired" | "Blocked" | string;
  source: string;
}

export interface MultiAssetClassCoverage {
  assetClass: string;
  displayName: string;
  status: "Ready" | "ReviewRequired" | "Blocked" | string;
  statusLabel: string;
  summary: string;
  evidenceRequirements: MultiAssetEvidenceRequirement[];
  blockers: MultiAssetReadinessBlocker[];
  drillThroughTargets?: MultiAssetDrillThroughTarget[];
  ledgerClassification: Record<string, string>;
  reconciliationSignals: Record<string, string>;
}

export interface MultiAssetCoverageSummary {
  fundAccountId: string;
  entity: string;
  asOfUtc: string;
  metrics: MetricSnapshot[];
  assetClasses: MultiAssetClassCoverage[];
  drillThroughRoutes: Record<string, string>;
}

export interface AssetOperationSubject {
  securityId: string;
  assetClass: string;
  displayName: string;
  primaryIdentifier: string | null;
  operationalProfile: string[];
}

export interface AssetTermsVersion {
  termsVersionId: string;
  securityId: string;
  versionNumber: number;
  termsHash: string;
  effectiveDate: string;
  recordedAt: string;
  sourceDomain: string;
  sourceEntityId: string;
  summary: string;
}

export interface AssetLifecycleEvent {
  lifecycleEventId: string;
  securityId: string;
  eventType: string;
  eventStatus: string;
  effectiveDate: string;
  recordedAt: string;
  sourceDomain: string;
  sourceEntityId: string;
  notes: string | null;
}

export interface AssetCashFlowProjectionRun {
  projectionRunId: string;
  securityId: string;
  projectionAsOf: string;
  engineVersion: string;
  status: string;
  generatedAt: string;
  sourceDomain: string;
  sourceEntityId: string;
}

export interface AssetProjectedCashFlow {
  projectedCashFlowId: string;
  projectionRunId: string;
  securityId: string;
  sequenceNumber: number;
  flowType: string;
  dueDate: string;
  amount: number;
  currency: string;
  status: string;
  sourceDomain: string;
  sourceEntityId: string;
}

export interface AssetActualActivity {
  activityId: string;
  securityId: string;
  activityType: string;
  effectiveDate: string;
  settlementDate: string | null;
  amount: number;
  currency: string;
  status: string;
  sourceDomain: string;
  sourceEntityId: string;
  evidenceReference: string | null;
}

export interface AssetReconciliationRun {
  reconciliationRunId: string;
  securityId: string;
  projectionRunId: string | null;
  status: string;
  requestedAt: string;
  completedAt: string | null;
  sourceDomain: string;
  sourceEntityId: string;
}

export interface AssetReconciliationResult {
  reconciliationResultId: string;
  reconciliationRunId: string;
  securityId: string;
  matchStatus: string;
  expectedAmount: number | null;
  actualAmount: number | null;
  varianceAmount: number | null;
  expectedDate: string | null;
  actualDate: string | null;
  notes: string[];
  sourceDomain: string;
  sourceEntityId: string;
}

export interface AssetLedgerProjection {
  ledgerProjectionId: string;
  securityId: string;
  projectionType: string;
  accountingDate: string;
  ledgerBasis: string;
  status: string;
  debitAmount: number;
  creditAmount: number;
  currency: string;
  sourceDomain: string;
  sourceEntityId: string;
  ledgerReference: string | null;
}

export interface AssetOperationsReadiness {
  securityId: string;
  status: string;
  capabilities: string[];
  readyCapabilities: string[];
  missingCapabilities: string[];
  blockers: string[];
  evaluatedAt: string;
  sourceDomain: string;
  sourceEntityId: string;
}

export interface AssetOperationsDetail {
  subject: AssetOperationSubject;
  termsHistory: AssetTermsVersion[];
  lifecycleEvents: AssetLifecycleEvent[];
  cashFlowProjectionRuns: AssetCashFlowProjectionRun[];
  projectedCashFlows: AssetProjectedCashFlow[];
  actualActivity: AssetActualActivity[];
  reconciliationRuns: AssetReconciliationRun[];
  reconciliationResults: AssetReconciliationResult[];
  ledgerProjections: AssetLedgerProjection[];
  readiness: AssetOperationsReadiness;
  workflowAudit: AssetLifecycleEvent[];
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

export interface AccountingReconciliationRecord {
  runId: string;
  strategyName: string;
  mode: "paper" | "live" | "backtest";
  status: string;
  lastUpdated: string;
  breakCount: number;
  openBreakCount: number;
  reconciliationStatus: "NotStarted" | "BreaksOpen" | "SecurityCoverageOpen" | "Resolved" | "Balanced";
}

export type GovernanceReconciliationRecord = AccountingReconciliationRecord;

export interface AccountingCashFlowSummary {
  totalCash: number;
  totalLedgerCash: number;
  netVariance: number;
  totalFinancing: number;
  runsWithCashSignals: number;
  runsWithCashVariance: number;
  tone: "default" | "success" | "warning" | "danger";
  summary: string;
}

export interface AccountingReportingProfile {
  id: string;
  name: string;
  targetTool: string;
  format: string;
  description: string;
  loaderScript: boolean;
  dataDictionary: boolean;
}

export interface ReportBrandingTheme {
  themeId: string;
  name: string;
  firmName: string;
  primaryColor: string;
  accentColor: string;
  textColor: string;
  backgroundColor: string;
  logoUri: string | null;
  footerText: string | null;
  disclaimer: string | null;
  isBuiltIn: boolean;
}

export type PortfolioReportingCutKind = "Fund" | "Strategy" | "UserTag";
export type PortfolioReportingLiveViewState = "LiveLinked" | "SourceBacked" | "Stale" | "Blocked";
export type PortfolioReportingPnlSlicePeriod = "Daily" | "Weekly" | "Monthly" | "Yearly";
export type PortfolioReportingAnalyticsKind = "TopWinner" | "TopLaggard" | "Contribution";
export type PortfolioReportingAnalyticsScope = "Security" | "Strategy" | "AssetClass";
export type CrossFundReportingConsolidationScope = "Company" | "Fund" | "Entity";

export interface PortfolioReportingCut {
  cutId: string;
  label: string;
  kind: PortfolioReportingCutKind;
  currency: string;
  grossExposure: number;
  netExposure: number;
  longMarketValue: number;
  shortMarketValue: number;
  totalCash: number;
  pendingSettlement: number;
  realizedPnl: number;
  unrealizedPnl: number;
  totalPnl: number;
  shadowNav: number;
  shadowNavVariance: number;
  sourceCount: number;
  tags: string[];
  asOf: string;
  evidenceRoute: string | null;
  shadowNavNote: string | null;
  versionStamp: string | null;
}

export interface PortfolioReportingLiveViewFreshnessPolicy {
  policyName: string;
  evaluatedAtUtc: string;
  sourceAgeSeconds: number | null;
  liveLinkWindowSeconds: number;
  staleWindowSeconds: number;
  isWithinLiveLinkWindow: boolean;
  isBeyondStaleWindow: boolean;
  reason: string;
}

export interface PortfolioReportingLiveView {
  viewId: string;
  label: string;
  kind: PortfolioReportingCutKind;
  state: PortfolioReportingLiveViewState;
  currency: string;
  grossExposure: number;
  netExposure: number;
  totalCash: number;
  pendingSettlement: number;
  totalPnl: number;
  shadowNav: number;
  asOf: string;
  sourceAsOfUtc: string | null;
  sourceCount: number;
  route: string;
  liquiditySummary: string;
  cashLadderSummary: string;
  telemetrySummary: string;
  tags: string[];
  cashLadderRoute: string | null;
  versionStamp: string | null;
  readinessBlockers?: string[] | null;
  marketTickAsOfUtc?: string | null;
  marketTickAgeSeconds?: number | null;
  marketTickSequence?: number | null;
  marketDataProvider?: string | null;
  tickFreshnessSummary?: string | null;
  isMarketTickLinked?: boolean;
  freshnessPolicy?: PortfolioReportingLiveViewFreshnessPolicy | null;
}

export interface PortfolioReportingPnlSlice {
  sliceId: string;
  period: PortfolioReportingPnlSlicePeriod;
  label: string;
  currency: string;
  startDate: string;
  endDate: string;
  realizedPnl: number;
  unrealizedPnl: number;
  totalPnl: number;
  priorTotalPnl: number;
  pnlChange: number;
  sourceCount: number;
  asOf: string;
  route: string;
  readinessSummary: string;
  tags: string[];
  versionStamp: string | null;
}

export interface PortfolioReportingAnalyticsRow {
  analyticsId: string;
  kind: PortfolioReportingAnalyticsKind;
  scope: PortfolioReportingAnalyticsScope;
  rank: number;
  label: string;
  symbol: string | null;
  classification: string | null;
  currency: string;
  realizedPnl: number;
  unrealizedPnl: number;
  totalPnl: number;
  contributionPercent: number;
  heatMapIntensity: number;
  sourceCount: number;
  asOf: string;
  route: string;
  readinessSummary: string;
  tags: string[];
  versionStamp: string | null;
}

export interface CrossFundReportingConsolidation {
  consolidationId: string;
  label: string;
  scope: CrossFundReportingConsolidationScope;
  currency: string;
  isReady: boolean;
  fundCount: number;
  entityCount: number;
  accountCount: number;
  runCount: number;
  grossExposure: number;
  netExposure: number;
  longMarketValue: number;
  shortMarketValue: number;
  totalCash: number;
  pendingSettlement: number;
  totalPnl: number;
  shadowNav: number;
  shadowNavVariance: number;
  sourceCount: number;
  asOf: string;
  route: string;
  readinessSummary: string;
  tags: string[];
  versionStamp: string | null;
}

export interface ReportingTemplateMetadata {
  templateId: string;
  family: string;
  name: string;
  version: string;
  sections: string[];
  reportWriterGrids?: ReportingTemplateGridMetadata[] | null;
  lifecycleStatus?: string;
  isBuiltIn?: boolean;
  isLatestApproved?: boolean;
  approvalSummary?: string;
  authoringRoute?: string;
  accessMode?: string;
  accessSummary?: string;
  isAccessible?: boolean;
  createdBy?: string | null;
  createdAt?: string | null;
  updatedBy?: string | null;
  updatedAt?: string | null;
  submittedBy?: string | null;
  submittedAt?: string | null;
  approvedBy?: string | null;
  approvedAt?: string | null;
  rejectedBy?: string | null;
  rejectedAt?: string | null;
  decisionRationale?: string | null;
  approvalReference?: string | null;
  basedOnTemplateId?: VersionedReportTemplateId | null;
  auditTrail?: ReportTemplateAuditEvent[] | null;
  validationIssues?: string[] | null;
}

export interface ReportingTemplateGridMetadata {
  gridId: string;
  title: string;
  kind: string;
  dimensionCount: number;
  metricCount: number;
  formulaCount: number;
  rowFields?: string[] | null;
  columnFields?: string[] | null;
  metrics?: ReportingTemplateGridMetricMetadata[] | null;
  formulas?: ReportingTemplateGridFormulaMetadata[] | null;
  topN?: number | null;
  sortBy?: string | null;
  sortDescending?: boolean;
  filters?: ReportingTemplateGridFilterMetadata[] | null;
  sourceFields?: ReportingTemplateGridFieldMetadata[] | null;
}

export interface ReportingTemplateGridFieldMetadata {
  name: string;
  label: string;
  role: string;
  dataType: string;
  dataset: string;
  description?: string | null;
}

export interface ReportWriterDatasetSource {
  sourceId: string;
  label: string;
  description: string;
  rowCount: number;
  fields: ReportingTemplateGridFieldMetadata[];
  rows: Record<string, string>[];
  tags?: string[] | null;
  certificationState?: string | null;
  validationState?: string | null;
  reconciliationState?: string | null;
  refreshCadence?: string | null;
  owner?: string | null;
  version?: string | null;
  releaseApproval?: string | null;
  lineageManifest?: string | null;
  sourceRunIds?: string[] | null;
  permittedConsumers?: string[] | null;
  rowLineageKeyField?: string | null;
  evidenceIndexField?: string | null;
}

export interface ReportingTemplateGridMetricMetadata {
  name: string;
  sourceField: string;
  function: string;
  label: string | null;
}

export interface ReportingTemplateGridFormulaMetadata {
  name: string;
  expression: string;
  label: string | null;
}

export interface ReportingTemplateGridFilterMetadata {
  field: string;
  operator: ReportWriterFilterOperator | string;
  value?: string | null;
  label?: string | null;
}

export type ReportWriterGridKind = "Detail" | "Pivot" | "TopN" | "Contribution";
export type ReportWriterAggregateFunction = "Sum" | "Count" | "Average" | "Min" | "Max";
export type ReportWriterFilterOperator =
  | "Equals"
  | "NotEquals"
  | "Contains"
  | "StartsWith"
  | "EndsWith"
  | "GreaterThan"
  | "GreaterThanOrEqual"
  | "LessThan"
  | "LessThanOrEqual"
  | "IsBlank"
  | "IsNotBlank";
export type ReportAccessMode = "Private" | "Restricted" | "CompanyWide";
export type ReportAccessPrincipalKind = "User" | "Group" | "Company";

export interface VersionedReportTemplateId {
  name: string;
  version: number;
}

export interface ReportTemplateParameterDefinition {
  name: string;
  required: boolean;
}

export interface ReportWriterMetricDefinition {
  name: string;
  sourceField: string;
  function?: ReportWriterAggregateFunction;
  label?: string | null;
}

export interface ReportWriterFormulaDefinition {
  name: string;
  expression: string;
  label?: string | null;
}

export interface ReportWriterFilterDefinition {
  field: string;
  operator?: ReportWriterFilterOperator;
  value?: string | null;
  label?: string | null;
}

export interface ReportWriterGridDefinition {
  gridId: string;
  title: string;
  kind: ReportWriterGridKind;
  rowFields?: string[] | null;
  columnFields?: string[] | null;
  metrics?: ReportWriterMetricDefinition[] | null;
  formulas?: ReportWriterFormulaDefinition[] | null;
  topN?: number | null;
  sortBy?: string | null;
  sortDescending?: boolean;
  filters?: ReportWriterFilterDefinition[] | null;
}

export interface ReportWriterGridColumn {
  key: string;
  label: string;
  role: string;
}

export interface ReportWriterGridRow {
  rowKey: string;
  values: Record<string, string>;
}

export interface ReportWriterMetricLineage {
  name: string;
  sourceField: string;
  function: ReportWriterAggregateFunction | string;
}

export interface ReportWriterFormulaLineage {
  name: string;
  expression: string;
  sourceFields: string[];
}

export interface ReportWriterFilterLineage {
  field: string;
  operator: ReportWriterFilterOperator | string;
  value?: string | null;
  label?: string | null;
}

export interface ReportWriterGridLineage {
  inputRowCount: number;
  outputRowCount: number;
  sourceFields: string[];
  metrics: ReportWriterMetricLineage[];
  formulas: ReportWriterFormulaLineage[];
  filteredInputRowCount?: number | null;
  filters?: ReportWriterFilterLineage[] | null;
}

export interface ReportWriterGridDataDictionaryField {
  key: string;
  label: string;
  role: string;
  sourceField: string;
  dataType: string;
  isGenerated: boolean;
  description: string;
}

export interface ReportWriterGridValidationCheck {
  checkId: string;
  status: string;
  detail: string;
}

export interface ReportWriterGridRender {
  gridId: string;
  title: string;
  kind: ReportWriterGridKind;
  columns: ReportWriterGridColumn[];
  rows: ReportWriterGridRow[];
  warnings: string[];
  lineage?: ReportWriterGridLineage | null;
  dataDictionary?: ReportWriterGridDataDictionaryField[] | null;
  validationChecks?: ReportWriterGridValidationCheck[] | null;
}

export interface ReportAccessPrincipal {
  kind: ReportAccessPrincipalKind;
  principalId: string;
  displayName?: string | null;
}

export interface ReportAccessPolicy {
  mode: ReportAccessMode;
  ownerPrincipalId?: string | null;
  principals?: ReportAccessPrincipal[] | null;
  companyId?: string | null;
  allowOwnerAccess?: boolean;
}

export interface ReportTemplateDraftRequest {
  name: string;
  displayName: string;
  sections: string[];
  parameters: ReportTemplateParameterDefinition[];
  family?: string | null;
  basedOnVersion?: number | null;
  rationale?: string | null;
  grids?: ReportWriterGridDefinition[] | null;
  accessPolicy?: ReportAccessPolicy | null;
}

export interface RenderReportTemplateRequest {
  templateId: VersionedReportTemplateId;
  parameters: Record<string, string>;
  datasetRows?: Record<string, string>[] | null;
  grids?: ReportWriterGridDefinition[] | null;
}

export interface RenderReportTemplateResponse {
  templateId: VersionedReportTemplateId;
  renderedContent: string;
  missingRequiredParameters: string[];
  grids?: ReportWriterGridRender[] | null;
  warnings?: string[] | null;
}

export interface ReportTemplateDecisionRequest {
  rationale: string;
  approvalReference?: string | null;
}

export interface ReportTemplateAuditEvent {
  at: string;
  actor: string;
  action: string;
  fromStatus: string;
  toStatus: string;
  note: string | null;
}

export interface ReportTemplateDefinition {
  templateId: VersionedReportTemplateId;
  displayName: string;
  parameters: ReportTemplateParameterDefinition[];
  sections: string[] | null;
  grids: ReportWriterGridDefinition[] | null;
  accessPolicy: ReportAccessPolicy | null;
}

export interface ReportTemplateGovernanceRecord {
  definition: ReportTemplateDefinition;
  status: string;
  family: string;
  isBuiltIn: boolean;
  isLatestApproved: boolean;
  createdBy: string;
  createdAt: string;
  updatedBy: string;
  updatedAt: string;
  validationIssues: string[];
  auditTrail: ReportTemplateAuditEvent[];
  submittedBy?: string | null;
  submittedAt?: string | null;
  approvedBy?: string | null;
  approvedAt?: string | null;
  rejectedBy?: string | null;
  rejectedAt?: string | null;
  decisionRationale?: string | null;
  approvalReference?: string | null;
  basedOnTemplateId?: VersionedReportTemplateId | null;
}

export interface ReportingRunStatusProjection {
  runId: string;
  templateId: string;
  family: string;
  status: string;
  trigger: string;
  asOfDate?: string | null;
  attemptCount: number;
  sectionCount: number;
  lineageLinkedSections: number;
  artifacts: string[];
  auditActions: string[];
  failureReason: string | null;
  drilldownLinks?: ReportingRunDrilldownLink[];
  nextActions?: ReportingRunNextAction[];
  generatedReportWriterGrids?: ReportingGeneratedReportWriterGrid[] | null;
  reportWriterDatasetSourceId?: string | null;
  reportWriterDatasetSourceLabel?: string | null;
  reportWriterDatasetRowCount?: number | null;
  brandingThemeId?: string | null;
  brandingTheme?: ReportBrandingTheme | null;
}

export interface ReportingGeneratedReportWriterGrid {
  gridId: string;
  title: string;
  kind: string;
  artifact: string;
  dimensionCount: number;
  metricCount: number;
  formulaCount: number;
  validationSummary?: string | null;
  validationPassedCount?: number | null;
  validationWarningCount?: number | null;
  validationFailedCount?: number | null;
}

export interface ReportingRunDrilldownLink {
  id: string;
  kind: string;
  label: string;
  href: string;
  method: string;
  isBrowserNavigable: boolean;
  source: string;
}

export interface ReportingRunNextAction {
  id: string;
  kind: string;
  label: string;
  href: string;
  method: string;
  isEnabled: boolean;
  disabledReason: string | null;
  isBrowserNavigable: boolean;
}

export interface ReportPackDistributionRecord {
  distributionId: string;
  recipient: string;
  recipientRole: string;
  channel: string;
  state: string;
  pendingItems: number;
  pendingSummary: string;
  owner: string;
  dueAtUtc: string | null;
  lastSentAtUtc: string | null;
  route: string;
}

export type GovernanceReportKind =
  | "TrialBalance"
  | "NavSummary"
  | "AssetAllocation"
  | "ReconciliationPack"
  | "PerformanceReport"
  | "HoldingsReport"
  | "CapitalAccountStatement"
  | "InvestorStatement"
  | "BoardPacket"
  | "AuditPackage"
  | "CertifiedDataset"
  | "CustomReport";
export type GovernanceReportArtifactFormat = "Json" | "Csv" | "Xlsx" | "Html" | "Pdf";
export type GovernanceReportPackStatus =
  | "Unknown"
  | "Draft"
  | "Generated"
  | "Validated"
  | "ReviewRequired"
  | "Approved"
  | "Rejected"
  | "Exported"
  | "Retained"
  | "Superseded"
  | "Restated"
  | "InReview"
  | "Published";
export type ReportPackDeliveryMode = "EmailLink" | "SecurePortal" | "EvidenceVault" | "InternalRoute";

export interface FundReportPackGenerateRequest {
  fundProfileId: string;
  auditActor: string;
  reportKind?: GovernanceReportKind;
  asOf?: string | null;
  currency?: string | null;
  correlationId?: string | null;
  decisionRationale?: string | null;
  formats?: GovernanceReportArtifactFormat[] | null;
  expectedSchemaVersion?: number | null;
  brandingThemeId?: string | null;
  brandingThemeOverride?: ReportBrandingTheme | null;
}

export interface FundReportPackPreviewRequest {
  fundProfileId: string;
  reportKind?: GovernanceReportKind;
  asOf?: string | null;
  currency?: string | null;
  brandingThemeId?: string | null;
  brandingThemeOverride?: ReportBrandingTheme | null;
}

export interface FundReportAssetClassSection {
  assetClass: string;
  total: number;
}

export interface FundReportPackPreview {
  reportId: string;
  fundProfileId: string;
  displayName: string;
  reportKind: GovernanceReportKind;
  currency: string;
  asOf: string;
  generatedAt: string;
  totalNetAssets: number;
  trialBalanceLineCount: number;
  assetClassSectionCount: number;
  assetClassSections: FundReportAssetClassSection[];
  brandingTheme?: ReportBrandingTheme | null;
}

export interface FundReportPackArtifact {
  artifactKind: string;
  format: GovernanceReportArtifactFormat;
  relativePath: string;
  sizeBytes: number;
  checksumSha256: string;
  schemaVersion: number;
}

export interface FundReportPackSnapshot {
  reportId: string;
  fundProfileId: string;
  displayName: string;
  reportKind: GovernanceReportKind;
  currency: string;
  asOf: string;
  generatedAt: string;
  totalNetAssets: number;
  auditActor: string;
  correlationId: string;
  decisionRationale: string | null;
  provenance: unknown;
  artifacts: FundReportPackArtifact[];
  warnings: string[];
  contractName: string;
  schemaVersion: number;
  brandingTheme: ReportBrandingTheme | null;
  status: GovernanceReportPackStatus;
  validationIssues: unknown[];
  lifecycleEvents: unknown[];
  auditPackReadiness: unknown | null;
}

export interface ReportPackDeliveryArtifact {
  format: GovernanceReportArtifactFormat;
  artifactName: string;
  contentType: string;
  retainedPath: string;
  byteSize: number;
  evidenceId: string;
  checksumSha256?: string | null;
  versionStamp?: string | null;
  downloadRoute?: string | null;
}

export interface ReportPackDeliveryAccessLink {
  kind: string;
  label: string;
  href: string;
  requiresToken: boolean;
  expiresAtUtc?: string | null;
  description?: string | null;
}

export interface ReportPackDeliveryNotification {
  notificationId: string;
  channel: string;
  recipient: string;
  recipientRole: string;
  deliveryMode: ReportPackDeliveryMode;
  subject: string;
  body: string;
  href: string;
  requiresToken: boolean;
  createdAtUtc: string;
  expiresAtUtc?: string | null;
  status: string;
}

export interface ReportPackDeliveryRecipient {
  distributionId: string;
  recipient: string;
  recipientRole: string;
  channel: string;
}

export interface ReportPackDeliveryApprovalStep {
  at: string;
  actor: string;
  action: string;
  fromState: string;
  toState: string;
  note?: string | null;
}

export interface ReportPackDeliveryEvidencePacket {
  packetId: string;
  packetKind: string;
  packageId: string;
  reportId: string;
  fundProfileId: string;
  fundAccountId: string;
  period: string;
  packageContents: string[];
  supportEvidenceIds: string[];
  recipientList: ReportPackDeliveryRecipient[];
  entitlementScope: string;
  approvalChain: ReportPackDeliveryApprovalStep[];
  datasetVersion: string;
  templateVersion: string;
  deliveryChannel: string;
  deliveredAtUtc: string;
  deliveryEvidence: ReportingWorkflowEvidenceLink[];
  requestHistory: string[];
  amendmentReason?: string | null;
  restatementLineage?: string | null;
  auditEventReferences?: string[] | null;
  blockedDownstreamOutputs?: string[] | null;
}

export interface ReportPackDeliveryPackage {
  packageId: string;
  reportId: string;
  distributionId: string;
  deliveryMode: ReportPackDeliveryMode;
  secureLink: string;
  portalRoute: string;
  formats: GovernanceReportArtifactFormat[];
  artifacts: ReportPackDeliveryArtifact[];
  createdAtUtc: string;
  retainedManifestPath: string;
  publicationEvidenceHash?: string | null;
  integritySummary?: string | null;
  reportingRunId?: string | null;
  reportingTemplateId?: string | null;
  reportingScheduleId?: string | null;
  reportingRunAsOfDate?: string | null;
  reportingRunStatus?: string | null;
  reportingRunTrigger?: string | null;
  reportingRunAttemptCount?: number | null;
  reportingRunSectionCount?: number | null;
  reportingRunLineageLinkedSections?: number | null;
  deliveryAccessSummary?: string | null;
  deliveryChannelSummary?: string | null;
  downloadSummary?: string | null;
  accessExpiresAtUtc?: string | null;
  accessLinks?: ReportPackDeliveryAccessLink[] | null;
  notifications?: ReportPackDeliveryNotification[] | null;
  sourceArtifacts?: string[] | null;
  generatedReportWriterGrids?: ReportingGeneratedReportWriterGrid[] | null;
  renderedReportWriterGrids?: ReportWriterGridRender[] | null;
  reportWriterDatasetSourceId?: string | null;
  reportWriterDatasetSourceLabel?: string | null;
  reportWriterDatasetRowCount?: number | null;
  lineProvenance?: ReportingWorkflowLineProvenance[] | null;
  deliveryEvidencePacket?: ReportPackDeliveryEvidencePacket | null;
  brandingTheme?: ReportBrandingTheme | null;
}

export interface ReportPackDeliveryAttempt {
  attemptId: string;
  reportId: string;
  distributionId: string;
  recipient: string;
  recipientRole: string;
  channel: string;
  state: string;
  attemptedAtUtc: string;
  actor: string;
  attemptNumber: number;
  deliveryReference: string;
  note: string | null;
  failureReason: string | null;
  evidenceLinks: ReportingWorkflowEvidenceLink[] | null;
  package: ReportPackDeliveryPackage | null;
}

export interface ReportPackDeliveryRequest {
  distributionId: string;
  actor?: string | null;
  deliveryReference?: string | null;
  note?: string | null;
  evidenceLinks?: ReportingWorkflowEvidenceLink[] | null;
  formats?: GovernanceReportArtifactFormat[] | null;
  deliveryMode?: ReportPackDeliveryMode | null;
  actionOrigin?: OperationsActionOrigin | null;
}

export interface ReportPackDeliveryFailureRequest extends ReportPackDeliveryRequest {
  failureReason: string;
}

export interface ReportPackDeliveryHistory {
  reportId: string;
  attempts: ReportPackDeliveryAttempt[];
}

export interface ReportingScheduleDeliveryTarget {
  distributionId: string;
  formats?: GovernanceReportArtifactFormat[] | null;
  deliveryMode?: ReportPackDeliveryMode | null;
  note?: string | null;
}

export interface ReportingScheduleDeliveryPlan {
  planId: string;
  scheduleId: string;
  templateId: string;
  distributionId: string;
  recipient: string;
  recipientRole: string;
  channel: string;
  deliveryMode: ReportPackDeliveryMode;
  formats: GovernanceReportArtifactFormat[];
  isReady: boolean;
  readinessSummary: string;
  route: string;
  dueAtUtc: string;
  nextAsOfDate: string;
  owner: string;
  note: string | null;
  lastDeliveryAttemptId: string | null;
  lastDeliveryState: string | null;
  lastDeliveryAtUtc: string | null;
  lastDeliveryPackageRoute: string | null;
  lastDeliverySecureLink: string | null;
  lastDeliveryAccessLinks?: ReportPackDeliveryAccessLink[] | null;
  lastDeliveryAccessExpiresAtUtc?: string | null;
  lastDeliveryAccessSummary?: string | null;
  lastDeliveryChannelSummary?: string | null;
  lastDeliveryDownloadSummary?: string | null;
  lastDeliveryNotificationCount?: number;
  lastDeliveryNotificationSummary?: string | null;
  lastDeliveryGeneratedReportWriterGridCount?: number;
  lastDeliveryRenderedReportWriterGridCount?: number;
  lastDeliveryReportWriterDatasetSummary?: string | null;
  lastDeliveryReportWriterGridSummary?: string | null;
  versionStamp: string | null;
  lastDeliveryArtifactCount?: number;
  lastDeliveryIntegritySummary?: string | null;
  readinessBlockers?: string[] | null;
  brandingThemeId?: string | null;
  brandingTheme?: ReportBrandingTheme | null;
  lastDeliveryEntitlementScope?: string | null;
}

export type StructuredReportingExportPurpose = "Regulatory" | "DataWarehouse" | "InvestmentDecision";

export interface StructuredReportingExport {
  exportId: string;
  label: string;
  purpose: StructuredReportingExportPurpose;
  format: GovernanceReportArtifactFormat;
  dataset: string;
  consumer: string;
  schemaVersion: number;
  rowCount: number;
  fieldCount: number;
  sourceCount: number;
  currency: string;
  asOf: string;
  isReady: boolean;
  retainedPath: string;
  route: string;
  dataDictionaryRoute: string | null;
  validationSummary: string | null;
  evidenceRoute: string | null;
  versionStamp: string | null;
  tags: string[] | null;
  readinessBlockers?: string[] | null;
  retainedManifestPath?: string | null;
  integrityHashSha256?: string | null;
  integritySummary?: string | null;
  rowLineageCount?: number | null;
}

export interface StructuredReportingExportColumn {
  name: string;
  dataType: string;
  description: string | null;
}

export interface StructuredReportingExportDataDictionaryField {
  name: string;
  dataType: string;
  description: string | null;
  ordinal: number;
  required: boolean;
}

export interface StructuredReportingExportValidationCheck {
  checkId: string;
  status: string;
  detail: string;
}

export interface StructuredReportingExportRowLineage {
  rowNumber: number;
  rowKey: string;
  rowHashSha256: string;
}

export interface StructuredReportingExportPayload {
  export: StructuredReportingExport;
  columns: StructuredReportingExportColumn[];
  rows: Record<string, string | null>[];
  warnings: string[];
  generatedAtUtc: string;
  dataDictionary?: StructuredReportingExportDataDictionaryField[] | null;
  validationChecks?: StructuredReportingExportValidationCheck[] | null;
  generatedByPrincipalId?: string | null;
  generatedForCompanyId?: string | null;
  generatedForGroupPrincipalIds?: string[] | null;
  rowLineage?: StructuredReportingExportRowLineage[] | null;
}

export interface ReportingScheduleRecord {
  scheduleId: string;
  templateId: string;
  cronExpression: string;
  nextAsOfDate: string;
  dueAtUtc: string;
  maxRetries: number;
  requestedBy: string;
  state: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  lastRunAtUtc: string | null;
  lastRunId: string | null;
  runCount: number;
  description: string | null;
  deliveryTargets?: ReportingScheduleDeliveryTarget[] | null;
  datasetRows?: Record<string, string>[] | null;
  datasetSourceId?: string | null;
  brandingThemeId?: string | null;
  brandingThemeOverride?: ReportBrandingTheme | null;
}

export interface ReportingScheduleUpsertRequest {
  scheduleId: string;
  templateId: string;
  cronExpression: string;
  nextAsOfDate: string;
  dueAtUtc: string;
  maxRetries: number;
  requestedBy: string;
  description?: string | null;
  state?: string;
  deliveryTargets?: ReportingScheduleDeliveryTarget[] | null;
  datasetRows?: Record<string, string>[] | null;
  datasetSourceId?: string | null;
  brandingThemeId?: string | null;
  brandingThemeOverride?: ReportBrandingTheme | null;
}

export interface ReportingScheduleRunResult {
  schedule: ReportingScheduleRecord;
  run: ReportingRunStatusProjection;
  deliveryAttempts?: ReportPackDeliveryAttempt[] | null;
  deliveryWarnings?: string[] | null;
}

export interface ReportingDueScheduleRunResult {
  evaluatedAtUtc: string;
  runs: ReportingScheduleRunResult[];
}

export interface ReportingRunRequest {
  templateId: string;
  asOfDate?: string | null;
  maxRetries?: number;
  jobId?: string | null;
  requestedBy?: string | null;
  datasetRows?: Record<string, string>[] | null;
  datasetSourceId?: string | null;
}

export interface ReportingRunResult {
  run: ReportingRunStatusProjection;
}

export interface ReportingRunAuditEntry {
  runId: string;
  timestampUtc: string;
  action: string;
  actor: string;
  notes: string;
}

export interface ReportingRunAuditTrail {
  runId: string;
  templateId: string;
  asOfDate: string;
  status: string;
  trigger: string;
  attemptCount: number;
  entries: ReportingRunAuditEntry[];
  reportWriterDatasetSourceId?: string | null;
  reportWriterDatasetSourceLabel?: string | null;
  reportWriterDatasetRowCount?: number | null;
}

export interface ReportingWorkflowEvidenceLink {
  evidenceId: string;
  label: string;
  route: string | null;
  source: string | null;
  capturedAtUtc: string | null;
}

export interface ReportingWorkflowAuditEntry {
  at: string;
  actor: string;
  action: string;
  fromState: string | null;
  toState: string | null;
  note: string | null;
}

export interface ReportingWorkflowChangedLine {
  lineKey: string;
  previousValue: string;
  currentValue: string;
  evidenceLinks: ReportingWorkflowEvidenceLink[] | null;
}

export interface ReportingWorkflowRestatement {
  reasonCode: string;
  approver: string | null;
  priorVersionReportId: string | null;
  changedLines: ReportingWorkflowChangedLine[];
  evidenceLinks: ReportingWorkflowEvidenceLink[] | null;
}

export interface ReportingWorkflowLineProvenance {
  lineKey: string;
  sourceKind: string;
  sourceId: string;
  evidenceId: string | null;
  runId: string | null;
  ledgerEntryId: string | null;
  reconciliationCaseId: string | null;
  reportValue: string | null;
  sourceSessionId: string | null;
  reconciliationRunId: string | null;
  providerEventId?: string | null;
  securityMasterId?: string | null;
  securityDefinitionId?: string | null;
  reconciliationOutcome?: string | null;
  approvalId?: string | null;
  financialRecordExplorerId?: string | null;
  financialRecordHref?: string | null;
}

export interface ReportingWorkflowPublication {
  manifestId: string;
  retainedManifestPath: string;
  evidenceHash: string;
  signedOffBy: string;
  signedOffAt: string;
  evidenceLinks: ReportingWorkflowEvidenceLink[] | null;
}

export interface ReportingWorkflowRecord {
  reportId: string;
  fundProfileId: string;
  fundAccountId: string;
  period: string;
  templateId: { name: string; version: number };
  state: string;
  version: number;
  createdAt: string;
  createdBy: string;
  updatedAt: string;
  auditTrail: ReportingWorkflowAuditEntry[];
  restatement: ReportingWorkflowRestatement | null;
  lineProvenance: ReportingWorkflowLineProvenance[];
  publication: ReportingWorkflowPublication | null;
}

export interface ReportingAccessAuditSummary {
  evaluationScope: string;
  summary: string;
  principalScopes: string[];
  visibleTemplateCount: number;
  hiddenTemplateCount: number;
  visibleReportPackCount: number;
  hiddenReportPackCount: number;
  visibleScheduleCount: number;
  hiddenScheduleCount: number;
  visibleDeliveryAttemptCount: number;
  hiddenDeliveryAttemptCount: number;
  visibleStructuredExportCount: number;
  hiddenStructuredExportCount: number;
  denialReasons: string[];
}

export interface AccountingReportingSummary {
  profileCount: number;
  fundProfileId?: string | null;
  selectedFundProfileId?: string | null;
  recommendedProfiles: string[];
  profiles: AccountingReportingProfile[];
  reportPackDistributions?: ReportPackDistributionRecord[];
  reportPackTargets?: string[];
  summary: string;
  templates?: ReportingTemplateMetadata[];
  recentRuns?: ReportingRunStatusProjection[];
  workflowRecords?: ReportingWorkflowRecord[];
  schedules?: ReportingScheduleRecord[];
  deliveryAttempts?: ReportPackDeliveryAttempt[];
  scheduleDeliveryPlans?: ReportingScheduleDeliveryPlan[];
  portfolioCuts?: PortfolioReportingCut[];
  structuredExports?: StructuredReportingExport[];
  brandingThemes?: ReportBrandingTheme[];
  reportWriterDatasetSources?: ReportWriterDatasetSource[];
  livePortfolioViews?: PortfolioReportingLiveView[];
  crossFundConsolidations?: CrossFundReportingConsolidation[];
  pnlSlices?: PortfolioReportingPnlSlice[];
  analyticsRows?: PortfolioReportingAnalyticsRow[];
  reportLineProvenanceExplorer?: FinancialRecordExplorerDto | null;
  accessAudit?: ReportingAccessAuditSummary | null;
}

export type GovernanceCashFlowSummary = AccountingCashFlowSummary;
export type GovernanceReportingProfile = AccountingReportingProfile;
export type GovernanceReportingSummary = AccountingReportingSummary;

export interface AccountingWorkspaceResponse {
  metrics: MetricSnapshot[];
  reconciliationQueue: AccountingReconciliationRecord[];
  breakQueue: ReconciliationBreakQueueItem[];
  cashFlow: AccountingCashFlowSummary;
  reporting: AccountingReportingSummary;
  manualJournalWorkbench?: ManualJournalEntryWorkbench | null;
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

export type GovernanceWorkspaceResponse = AccountingWorkspaceResponse;
export type ReportingWorkspaceResponse = AccountingWorkspaceResponse;

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

export interface ReconciliationBreakExplanation {
  summary: string;
  sourceSystems: string[];
  probableCause: string;
  ledgerImpact: string;
  suggestedNextAction: string;
  evidenceLinks: string[];
}

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
  breakExplanation?: ReconciliationBreakExplanation | null;
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
  continuityStatus?: string;
  reconciliationBreakCount?: number;
  reconciliationHighestSeverity?: string;
  hasLedgerEntryCoverage?: boolean;
  ledgerCoverageStatus?: string;
  cashFlowHealth?: string;
  compatibilityWarnings?: string[] | null;
  artifactCompleteness?: StrategyRunArtifactCompleteness | null;
}

export interface StrategyRunArtifactCompleteness {
  hasPortfolio: boolean;
  hasLedger: boolean;
  hasCashFlow: boolean;
  hasFills: boolean;
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
  finalEquityDelta?: number | null;
  maxDrawdownDelta?: number | null;
  sharpeRatioDelta?: number | null;
  baseFinalEquity?: number | null;
  targetFinalEquity?: number | null;
  baseMaxDrawdown?: number | null;
  targetMaxDrawdown?: number | null;
  baseSharpeRatio?: number | null;
  targetSharpeRatio?: number | null;
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
  compatibilityWarnings?: string[] | null;
  baseArtifactCompleteness?: StrategyRunArtifactCompleteness | null;
  targetArtifactCompleteness?: StrategyRunArtifactCompleteness | null;
  baseMode?: string | null;
  targetMode?: string | null;
  baseEngine?: string | null;
  targetEngine?: string | null;
  baseStrategyId?: string | null;
  targetStrategyId?: string | null;
  baseStrategyVersion?: string | null;
  targetStrategyVersion?: string | null;
  lineageRelation?: string;
  compatibilityLevel?: string;
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

export type AccountingConfigurationStatus = "Draft" | "Active" | "Archived";
export type AccountingConfigurationValidationSeverity = "Info" | "Warning" | "Critical";
export type AccountingProductionReadinessStatus = "Ready" | "ReviewRequired" | "Blocked" | "Unavailable";
export type AccountingProductionReadinessArea =
  | "LedgerBooks"
  | "RulesStudio"
  | "PostingRules"
  | "JournalLifecycle"
  | "DimensionalAccounting"
  | "ExternalGl"
  | "CloseReporting"
  | "TenantAdministration"
  | "MigrationRollout";
export type LedgerBookRolloutIssueSeverity = "Info" | "Warning" | "Critical";
export type AccountingTemplateLineSide = "Debit" | "Credit";
export type ManualJournalEntryStatus = "Draft" | "NeedsFix" | "Submitted" | "Approved" | "Rejected" | "Posted" | "Reversed" | "Rebooked" | "CloseLocked";
export type AccountingRuleConditionOperator = "Equals" | "NotEquals" | "Contains" | "AmountGreaterThanOrEqual" | "AmountLessThanOrEqual" | "AmountBetween" | "IsPresent";
export type AccountingRuleConditionGroupOperator = "All" | "Any";
export type AccountingRuleFormulaKind = "FixedAmount" | "SourceAmount" | "PercentageOfSourceAmount" | "AllocationResidual";
export type AllocationRuleBasis = "FixedPercent" | "InvestorCommitment" | "CapitalAccountBalance" | "StrategyWeight" | "CustomFormula";
export type JournalEntryLifecycleAction = "Validate" | "Submit" | "Approve" | "Reject" | "Post" | "Reverse" | "Rebook" | "LockAfterClose";
export type PrivateCapitalFundEventLedgerReadiness =
  | "Blocked"
  | "EvidenceMissing"
  | "ApprovalPending"
  | "PostingReview"
  | "ReportReview"
  | "Ready"
  | "Published";
export type PaymentIntentCashDirection = "Neutral" | "Inflow" | "Outflow";
export type PaymentIntentWorkflowStatus =
  | "EvidenceMissing"
  | "ApprovalPending"
  | "BankEvidencePending"
  | "BankReturned"
  | "ReconciliationPending"
  | "ExecutionDeferred"
  | "Blocked";
export type PrivateCapitalPaymentIntentEvidenceStatus =
  | "MissingIntent"
  | "CashEvidenceMissing"
  | "IntentCaptured"
  | "SettlementMatched";
export type ManualJournalEntryType =
  | "General"
  | "AccruedBalance"
  | "AccruedExpense"
  | "PrepaidExpense"
  | "Expense"
  | "Amortization"
  | "Deferral"
  | "Reclassification"
  | "Reversal"
  | "CapitalCall"
  | "Distribution"
  | "Subscription"
  | "Redemption"
  | "LpTransfer"
  | "ManagementFee";

export interface LedgerBook {
  ledgerBookId: string;
  fundProfileId: string;
  fundStructureNodeId: string;
  fundStructureNodeKind: string;
  displayName: string;
  baseCurrency: string;
  createdAt: string;
  updatedAt: string;
  description?: string | null;
  accountingBasis: AccountingBasisKind;
  accountingPolicyId: string;
  accountingPolicyVersion: string;
}

export interface CreateLedgerBookRequest {
  fundProfileId: string;
  fundStructureNodeId: string;
  fundStructureNodeKind: string;
  displayName: string;
  baseCurrency: string;
  description?: string | null;
  accountingBasis?: AccountingBasisKind;
  accountingPolicyId?: string;
  accountingPolicyVersion?: string;
}

export interface LedgerBookSetupCandidate {
  fundProfileId: string;
  fundStructureNodeId: string;
  fundStructureNodeKind: string;
  displayName: string;
  baseCurrency: string;
  accountingBasis: AccountingBasisKind;
  accountingPolicyId: string;
  accountingPolicyVersion: string;
  suggestedAction: string;
  description?: string | null;
  sourceLedgerBookId?: string | null;
  requestedLedgerBookId?: string | null;
}

export interface LedgerBookRequiredScope {
  fundStructureNodeId: string;
  fundStructureNodeKind: string;
  accountingBasis: AccountingBasisKind;
  displayName?: string | null;
}

export interface LedgerBookRolloutBookStatus {
  ledgerBookId: string;
  fundProfileId: string;
  fundStructureNodeId: string;
  fundStructureNodeKind: string;
  accountingBasis: AccountingBasisKind;
  accountingPolicyId: string;
  accountingPolicyVersion: string;
  periodCount: number;
  openPeriodCount: number;
  softClosedPeriodCount: number;
  hardClosedPeriodCount: number;
  firstPeriodStart?: string | null;
  lastPeriodEnd?: string | null;
}

export interface LedgerBookRolloutIssue {
  code: string;
  severity: LedgerBookRolloutIssueSeverity;
  message: string;
  scope?: string | null;
  ledgerBookId?: string | null;
  fundStructureNodeId?: string | null;
  accountingBasis?: AccountingBasisKind | null;
}

export interface LedgerBookRolloutAssessment {
  generatedAtUtc: string;
  fundProfileId?: string | null;
  fundStructureNodeId?: string | null;
  fundStructureNodeKind?: string | null;
  accountingBasis?: AccountingBasisKind | null;
  books: LedgerBookRolloutBookStatus[];
  issues: LedgerBookRolloutIssue[];
  isReady: boolean;
  criticalIssueCount: number;
  warningIssueCount: number;
  bookCount: number;
  openPeriodCount: number;
}

export interface AccountingProductionReadinessRequest {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  accountingBasis?: AccountingBasisKind | null;
  providerId?: string | null;
  tenantId?: string | null;
  companyId?: string | null;
  requiredLedgerBookScopes?: LedgerBookRequiredScope[] | null;
  tenantScopeConfigured?: boolean;
  adminRoleProfileConfigured?: boolean;
  scopedAccessPoliciesConfigured?: boolean;
  reportingGroupsConfigured?: boolean;
  accountingAdminSurfaceConfigured?: boolean;
  browserAccountingAdminSurfaceConfigured?: boolean;
  wpfAccountingAdminSurfaceConfigured?: boolean;
  chartAdministrationStudioConfigured?: boolean;
  ruleTestPromotionStudioConfigured?: boolean;
  closeSetupStudioConfigured?: boolean;
  providerMappingStudioConfigured?: boolean;
  tenantCompanyReportGroupSetupStudioConfigured?: boolean;
  auditReviewToolingConfigured?: boolean;
  bulkImportExportSafeguardsConfigured?: boolean;
  performanceValidationConfigured?: boolean;
  disasterRecoveryRunbookConfigured?: boolean;
  ledgerBookAdministrationStudioConfigured?: boolean;
  postingRuleAuthoringStudioConfigured?: boolean;
  approvalQueueStudioConfigured?: boolean;
  dimensionMappingStudioConfigured?: boolean;
  implementationSandboxConfigured?: boolean;
  tenantAdministrationEvidenceLinks?: string[] | null;
  postingRulesLedgerBookNativeCertified?: boolean;
  journalLifecycleLedgerBookNativeCertified?: boolean;
  closeReportingLedgerBookNativeCertified?: boolean;
  closePlanConfigurationLedgerBookNativeCertified?: boolean;
  externalGlLedgerBookNativeCertified?: boolean;
  reconciliationLedgerBookNativeCertified?: boolean;
  directLendingLedgerBookNativeCertified?: boolean;
  strategyLedgerReadLedgerBookNativeCertified?: boolean;
  ledgerBookWorkflowEvidenceLinks?: string[] | null;
  periodReportDimensionQueriesCertified?: boolean;
  crossPeriodReportDimensionQueriesCertified?: boolean;
  journalQueryDimensionFiltersCertified?: boolean;
  externalExportDimensionMappingCertified?: boolean;
  ledgerLineDimensionsPersistedCertified?: boolean;
  trialBalanceDimensionFiltersCertified?: boolean;
  reportPackageDimensionProvenanceCertified?: boolean;
  dimensionalReportingEvidenceLinks?: string[] | null;
  ledgerBookMigrationCertified?: boolean;
  historicalJournalBackfillCertified?: boolean;
  dimensionalBackfillCertified?: boolean;
  accountingConfigurationPromotionCertified?: boolean;
  closeReportingEvidenceMigrationCertified?: boolean;
  migrationEvidenceLinks?: string[] | null;
  migrationRunArtifacts?: AccountingMigrationRunArtifact[] | null;
}

export type AccountingMigrationRunKind =
  | "LedgerBookScope"
  | "HistoricalJournalBackfill"
  | "DimensionalBackfill"
  | "AccountingConfigurationPromotion"
  | "CloseReportingEvidence";

export type AccountingMigrationRunStatus = "Planned" | "Running" | "Completed" | "Failed" | "Certified";

export interface AccountingMigrationRunArtifact {
  runId: string;
  kind: AccountingMigrationRunKind;
  status: AccountingMigrationRunStatus;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  actor?: string | null;
  migratedRecordCount: number;
  issueCount: number;
  evidenceReferences: string[];
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  summary?: string | null;
  dimensions?: LedgerDimensionSet | null;
}

export interface AccountingMigrationRolloutPlanItem {
  kind: AccountingMigrationRunKind;
  code: string;
  label: string;
  certified: boolean;
  status: AccountingProductionReadinessStatus;
  scopeLabel: string;
  requiredAction: string;
  latestRunId?: string | null;
  latestRunStatus?: AccountingMigrationRunStatus | null;
  migratedRecordCount: number;
  issueCount: number;
  evidenceReferences: string[];
  blockingIssueCodes: string[];
}

export interface AccountingMigrationRunArtifactList {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  artifacts: AccountingMigrationRunArtifact[];
}

export interface AccountingProductionReadinessIssue {
  code: string;
  area: AccountingProductionReadinessArea;
  severity: AccountingConfigurationValidationSeverity;
  message: string;
  suggestedAction: string;
  evidenceReferences: string[];
}

export interface AccountingProductionReadinessComponent {
  area: AccountingProductionReadinessArea;
  label: string;
  status: AccountingProductionReadinessStatus;
  score: number;
  summary: string;
  issues: AccountingProductionReadinessIssue[];
  evidenceReferences: string[];
  route?: string | null;
}

export interface AccountingProductionGap {
  code: string;
  label: string;
  status: AccountingProductionReadinessStatus;
  highestSeverity: AccountingConfigurationValidationSeverity;
  summary: string;
  requiredAction: string;
  areas: AccountingProductionReadinessArea[];
  blockingIssueCodes: string[];
  routes: string[];
  issues?: AccountingProductionReadinessIssue[];
}

export interface AccountingTenantAdministrationReadiness {
  tenantId?: string | null;
  companyId?: string | null;
  tenantScopeConfigured: boolean;
  adminRoleProfileConfigured: boolean;
  scopedAccessPoliciesConfigured: boolean;
  reportingGroupsConfigured: boolean;
  accountingAdminSurfaceConfigured: boolean;
  browserAccountingAdminSurfaceConfigured: boolean;
  wpfAccountingAdminSurfaceConfigured: boolean;
  chartAdministrationStudioConfigured?: boolean;
  ruleTestPromotionStudioConfigured?: boolean;
  closeSetupStudioConfigured?: boolean;
  providerMappingStudioConfigured?: boolean;
  tenantCompanyReportGroupSetupStudioConfigured?: boolean;
  auditReviewToolingConfigured?: boolean;
  bulkImportExportSafeguardsConfigured?: boolean;
  performanceValidationConfigured?: boolean;
  disasterRecoveryRunbookConfigured?: boolean;
  ledgerBookAdministrationStudioConfigured?: boolean;
  postingRuleAuthoringStudioConfigured?: boolean;
  approvalQueueStudioConfigured?: boolean;
  dimensionMappingStudioConfigured?: boolean;
  implementationSandboxConfigured?: boolean;
  evidenceReferences: string[];
  completedControlCount: number;
  requiredControlCount: number;
  hasTenantScope: boolean;
  hasCompanyScope: boolean;
  hasRetainedEvidence: boolean;
}

export interface AccountingLedgerBookWorkflowReadiness {
  ledgerBookId?: string | null;
  postingRulesLedgerBookNativeCertified: boolean;
  journalLifecycleLedgerBookNativeCertified: boolean;
  closeReportingLedgerBookNativeCertified: boolean;
  closePlanConfigurationLedgerBookNativeCertified?: boolean;
  externalGlLedgerBookNativeCertified: boolean;
  reconciliationLedgerBookNativeCertified?: boolean;
  directLendingLedgerBookNativeCertified?: boolean;
  strategyLedgerReadLedgerBookNativeCertified?: boolean;
  evidenceReferences: string[];
  completedControlCount: number;
  requiredControlCount: number;
  hasLedgerBookScope: boolean;
  hasRetainedEvidence: boolean;
  hasLedgerBookScopedEvidence: boolean;
}

export interface AccountingDimensionalReportingReadiness {
  ledgerBookId?: string | null;
  periodReportDimensionQueriesCertified: boolean;
  crossPeriodReportDimensionQueriesCertified: boolean;
  journalQueryDimensionFiltersCertified: boolean;
  externalExportDimensionMappingCertified: boolean;
  ledgerLineDimensionsPersistedCertified?: boolean;
  trialBalanceDimensionFiltersCertified?: boolean;
  reportPackageDimensionProvenanceCertified?: boolean;
  evidenceReferences: string[];
  completedControlCount: number;
  requiredControlCount: number;
  hasLedgerBookScope: boolean;
  hasRetainedEvidence: boolean;
  hasLedgerBookScopedEvidence: boolean;
}

export interface AccountingTenantAdministrationProfile {
  tenantId: string;
  companyId: string;
  tenantScopeConfigured: boolean;
  adminRoleProfileConfigured: boolean;
  scopedAccessPoliciesConfigured: boolean;
  reportingGroupsConfigured: boolean;
  accountingAdminSurfaceConfigured: boolean;
  browserAccountingAdminSurfaceConfigured: boolean;
  wpfAccountingAdminSurfaceConfigured: boolean;
  chartAdministrationStudioConfigured?: boolean;
  ruleTestPromotionStudioConfigured?: boolean;
  closeSetupStudioConfigured?: boolean;
  providerMappingStudioConfigured?: boolean;
  tenantCompanyReportGroupSetupStudioConfigured?: boolean;
  auditReviewToolingConfigured?: boolean;
  bulkImportExportSafeguardsConfigured?: boolean;
  performanceValidationConfigured?: boolean;
  disasterRecoveryRunbookConfigured?: boolean;
  ledgerBookAdministrationStudioConfigured?: boolean;
  postingRuleAuthoringStudioConfigured?: boolean;
  approvalQueueStudioConfigured?: boolean;
  dimensionMappingStudioConfigured?: boolean;
  implementationSandboxConfigured?: boolean;
  updatedAtUtc: string;
  updatedBy: string;
  evidenceReferences: string[];
  correlationId?: string | null;
}

export interface AccountingTenantAdministrationProfileUpsertRequest {
  profile: AccountingTenantAdministrationProfile;
  actor: string;
  correlationId?: string | null;
  evidenceLinks?: string[] | null;
}

export interface AccountingProductionCertificationProfile {
  fundProfileId: string;
  ledgerBookId?: string | null;
  tenantId?: string | null;
  companyId?: string | null;
  postingRulesLedgerBookNativeCertified: boolean;
  journalLifecycleLedgerBookNativeCertified: boolean;
  closeReportingLedgerBookNativeCertified: boolean;
  closePlanConfigurationLedgerBookNativeCertified?: boolean;
  externalGlLedgerBookNativeCertified: boolean;
  reconciliationLedgerBookNativeCertified?: boolean;
  directLendingLedgerBookNativeCertified?: boolean;
  strategyLedgerReadLedgerBookNativeCertified?: boolean;
  periodReportDimensionQueriesCertified: boolean;
  crossPeriodReportDimensionQueriesCertified: boolean;
  journalQueryDimensionFiltersCertified: boolean;
  externalExportDimensionMappingCertified: boolean;
  ledgerLineDimensionsPersistedCertified?: boolean;
  trialBalanceDimensionFiltersCertified?: boolean;
  reportPackageDimensionProvenanceCertified?: boolean;
  updatedAtUtc: string;
  updatedBy: string;
  evidenceReferences: string[];
  correlationId?: string | null;
}

export interface AccountingProductionCertificationProfileUpsertRequest {
  profile: AccountingProductionCertificationProfile;
  actor: string;
  correlationId?: string | null;
  evidenceLinks?: string[] | null;
}

export interface AccountingProductionReadiness {
  generatedAtUtc: string;
  fundProfileId: string;
  ledgerBookId?: string | null;
  status: AccountingProductionReadinessStatus;
  score: number;
  components: AccountingProductionReadinessComponent[];
  issues: AccountingProductionReadinessIssue[];
  ledgerBookRollout?: LedgerBookRolloutAssessment | null;
  rulesStudioSummary?: AccountingRulesStudioSummary | null;
  ledgerBookWorkflows?: AccountingLedgerBookWorkflowReadiness | null;
  dimensionalReporting?: AccountingDimensionalReportingReadiness | null;
  externalGlProviderCount: number;
  certifiedExternalGlMappingProfileCount: number;
  externalGlLivePostingEnabled: boolean;
  migrationRunArtifacts?: AccountingMigrationRunArtifact[];
  migrationRolloutPlan?: AccountingMigrationRolloutPlanItem[];
  tenantAdministration?: AccountingTenantAdministrationReadiness | null;
  productionGaps?: AccountingProductionGap[];
  criticalIssueCount: number;
  warningIssueCount: number;
}

export interface ChartOfAccountsNode {
  nodeId: string;
  path: string;
  accountName: string;
  accountType: string;
  parentPath?: string | null;
  symbol?: string | null;
  financialAccountId?: string | null;
  isArchived: boolean;
}

export interface JournalEntryTemplateLine {
  lineId: string;
  accountPath: string;
  side: AccountingTemplateLineSide;
  amount: number;
  currency: string;
  description?: string | null;
}

export interface JournalEntryTemplate {
  templateId: string;
  displayName: string;
  description: string;
  lines: JournalEntryTemplateLine[];
  isArchived: boolean;
  version: string;
}

export interface AccountingRuleCondition {
  conditionId: string;
  field: string;
  operator: AccountingRuleConditionOperator;
  value?: string | null;
  secondValue?: string | null;
  isRequired: boolean;
  description?: string | null;
}

export interface AccountingRuleConditionGroup {
  groupId: string;
  operator: AccountingRuleConditionGroupOperator;
  conditions: AccountingRuleCondition[];
  isRequired: boolean;
  description?: string | null;
}

export interface AccountingRuleFormula {
  formulaId: string;
  kind: AccountingRuleFormulaKind;
  value: number;
  currency: string;
  description?: string | null;
}

export interface AllocationRule {
  allocationRuleId: string;
  basis: AllocationRuleBasis;
  weight: number;
  targetDimensions?: LedgerDimensionSet | null;
  formulaId?: string | null;
  description?: string | null;
}

export interface GeneratedPostingLine {
  lineId: string;
  accountPath: string;
  side: AccountingTemplateLineSide;
  amountFormulaId: string;
  amount: number;
  currency: string;
  dimensions?: LedgerDimensionSet | null;
  description?: string | null;
}

export interface RulePromotionApproval {
  approvalId: string;
  requestedBy: string;
  requestedAtUtc: string;
  approvalState: ManualJournalEntryStatus;
  approvedBy?: string | null;
  approvedAtUtc?: string | null;
  notes?: string | null;
  evidenceLinks: string[];
}

export interface AccountingRuleVersion {
  version: string;
  createdAtUtc: string;
  createdBy: string;
  changeSummary: string;
  promotionApproval?: RulePromotionApproval | null;
  evidenceLinks: string[];
}

export interface PostingRule {
  ruleId: string;
  displayName: string;
  sourceEventType: string;
  templateId: string;
  ruleVersion: string;
  isArchived: boolean;
  description?: string | null;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  priority?: number;
  scope?: LedgerDimensionSet | null;
  conditions?: AccountingRuleCondition[] | null;
  conditionGroups?: AccountingRuleConditionGroup[] | null;
  formulas?: AccountingRuleFormula[] | null;
  allocations?: AllocationRule[] | null;
  generatedPostings?: GeneratedPostingLine[] | null;
  versions?: AccountingRuleVersion[] | null;
  promotionApproval?: RulePromotionApproval | null;
  requiresPromotionApproval?: boolean;
}

export interface AccountingConfigurationValidationIssue {
  code: string;
  severity: AccountingConfigurationValidationSeverity;
  message: string;
  targetId?: string | null;
  suggestedAction?: string | null;
}

export interface AccountingActionAuditEvent {
  auditEventId: string;
  recordedAtUtc: string;
  actor: string;
  action: string;
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  correlationId?: string | null;
  beforeHash: string;
  afterHash: string;
  validationIssues: AccountingConfigurationValidationIssue[];
  evidenceLinks: string[];
  companyId?: string | null;
  reportGroupPrincipalIds?: string[] | null;
}

export interface AccountingRulesStudioSummary {
  totalRules: number;
  activeRules: number;
  archivedRules: number;
  effectiveDatedRules: number;
  generatedPostingRules: number;
  templateMappingRules: number;
  rulesWithConditions: number;
  rulesWithFormulas: number;
  rulesWithAllocations: number;
  rulesRequiringPromotionApproval: number;
  approvedPromotionRules: number;
  pendingPromotionApprovalRules: number;
  savedTestCaseCount: number;
  rulesWithSavedRegressionTests: number;
  rulesMissingCurrentVersionRegressionTests: number;
  criticalIssueCount: number;
  warningIssueCount: number;
}

export interface AccountingRulesStudioRuleRow {
  ruleId: string;
  displayName: string;
  sourceEventType: string;
  ruleVersion: string;
  priority: number;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  templateId: string;
  isArchived: boolean;
  usesGeneratedPostings: boolean;
  conditionCount: number;
  conditionGroupCount: number;
  formulaCount: number;
  allocationCount: number;
  generatedPostingLineCount: number;
  versionCount: number;
  savedTestCaseCount: number;
  savedTestEvidenceLinkCount: number;
  requiresPromotionApproval: boolean;
  isPromotionApproved: boolean;
  promotionApprovalState?: ManualJournalEntryStatus | null;
  promotionApprovalId?: string | null;
  criticalIssueCount: number;
  warningIssueCount: number;
  canDryRun: boolean;
  canRequestPromotion: boolean;
  canActivate: boolean;
}

export interface AccountingRulesStudioPromotionQueueItem {
  ruleId: string;
  displayName: string;
  ruleVersion: string;
  requestedBy: string;
  requestedAtUtc?: string | null;
  approvalState?: ManualJournalEntryStatus | null;
  approvalId?: string | null;
  regressionTestCaseCount: number;
  missingRegressionEvidenceCount: number;
  criticalIssueCount: number;
  suggestedAction: string;
}

export interface AccountingRulesStudio {
  summary: AccountingRulesStudioSummary;
  rules: AccountingRulesStudioRuleRow[];
  promotionQueue: AccountingRulesStudioPromotionQueueItem[];
}

export interface AccountingConfigurationWorkspace {
  fundProfileId: string;
  ledgerBookId?: string | null;
  status: AccountingConfigurationStatus;
  configurationVersion: string;
  updatedAtUtc: string;
  ledgerBooks: LedgerBook[];
  chartOfAccounts: ChartOfAccountsNode[];
  journalTemplates: JournalEntryTemplate[];
  postingRules: PostingRule[];
  validationIssues: AccountingConfigurationValidationIssue[];
  auditTrail: AccountingActionAuditEvent[];
  ruleTestCases?: AccountingRuleTestCase[] | null;
  rulesStudio?: AccountingRulesStudio | null;
  ledgerBookSetupCandidate?: LedgerBookSetupCandidate | null;
}

export interface AccountingJournalPreviewLine {
  accountPath: string;
  accountName: string;
  side: AccountingTemplateLineSide;
  amount: number;
  currency: string;
  description?: string | null;
}

export interface AccountingJournalTemplatePreview {
  templateId: string;
  displayName: string;
  isBalanced: boolean;
  totalDebits: number;
  totalCredits: number;
  lines: AccountingJournalPreviewLine[];
  validationIssues: AccountingConfigurationValidationIssue[];
}

export interface RuleDryRunRequest {
  fundProfileId: string;
  sourceEventType: string;
  eventAmount: number;
  currency: string;
  effectiveDate: string;
  actor: string;
  ledgerBookId?: string | null;
  dimensions?: LedgerDimensionSet | null;
  counterpartyId?: string | null;
  instrumentSymbol?: string | null;
  correlationId?: string | null;
}

export interface AccountingRuleDryRunMatch {
  ruleId: string;
  displayName: string;
  ruleVersion: string;
  priority: number;
  isMatched: boolean;
  explanations: string[];
  validationIssues: AccountingConfigurationValidationIssue[];
}

export interface RuleDryRunResult {
  fundProfileId: string;
  ledgerBookId?: string | null;
  sourceEventType: string;
  effectiveDate: string;
  eventAmount: number;
  currency: string;
  isPostingBalanced: boolean;
  selectedRuleId?: string | null;
  ruleMatches: AccountingRuleDryRunMatch[];
  generatedLines: AccountingJournalPreviewLine[];
  generatedPostingLines?: GeneratedPostingLine[] | null;
  validationIssues: AccountingConfigurationValidationIssue[];
}

export type LedgerPostingKind = "Originating" | "Adjustment";
export type AccountingTreatmentKind =
  | "General"
  | "Accrual"
  | "Expense"
  | "PrepaidExpense"
  | "Amortization"
  | "Deferral"
  | "Reclassification"
  | "Reversal"
  | "FxTranslation"
  | "TaxLotRelief"
  | "DirectLendingAccrual"
  | "EquityMethodInvestment"
  | "Intercompany"
  | "ConsolidationElimination";
export type AccountingPostingIntent = "Originating" | "Adjustment" | "Reversal" | "Rebook" | "Restatement" | "AutomatedDraft";
export type AccountingPostingApprovalState = "NotRequired" | "Pending" | "Approved" | "Rejected";
export type AccountingPostingEvidenceKind =
  | "Source"
  | "Approval"
  | "Reconciliation"
  | "Settlement"
  | "PeriodLock"
  | "OperatorRationale"
  | "Correction"
  | "ReportOutput"
  | "AuditSupport";

export interface AccountingPostingEvidenceReference {
  evidenceId: string;
  uri: string;
  kind: AccountingPostingEvidenceKind;
  sourceSystem: string;
  retainedAtUtc: string;
  retainedBy: string;
  subjectId?: string | null;
  contentHash?: string | null;
  description?: string | null;
}

export interface AccountingPostingCommand {
  commandId: string;
  aggregateId: string;
  periodId: string;
  effectiveDate: string;
  postingDate: string;
  idempotencyKey: string;
  intent: AccountingPostingIntent;
  sourceEventId?: string | null;
  correlationId?: string | null;
  causationId?: string | null;
  sourceJournalEntryId?: string | null;
  expectedVersion?: number | null;
  sourceEventType?: string | null;
  treasuryContext?: TreasuryLedgerContext | null;
  approvalState: AccountingPostingApprovalState;
  approvalId?: string | null;
  operatorRationale?: string | null;
  evidence: AccountingPostingEvidenceReference[];
  actionOrigin: OperationsActionOrigin;
}

export interface PostingRuleJournalCandidateRequest {
  fundProfileId: string;
  sourceEventType: string;
  eventAmount: number;
  currency: string;
  effectiveDate: string;
  actor: string;
  aggregateId: string;
  periodId: string;
  accountingTimestamp: string;
  description: string;
  accountingBasis?: AccountingBasisKind;
  ledgerBookId?: string | null;
  dimensions?: LedgerDimensionSet | null;
  counterpartyId?: string | null;
  instrumentSymbol?: string | null;
  correlationId?: string | null;
  sourceEventId?: string | null;
  sourceJournalEntryId?: string | null;
  policyId?: string | null;
  treatmentKind?: AccountingTreatmentKind | null;
  postingKind?: LedgerPostingKind;
  treasuryContext?: TreasuryLedgerContext | null;
  evidenceLinks?: string[] | null;
  tenantId?: string | null;
  companyId?: string | null;
}

export interface PostingRuleJournalCandidateIssue {
  code: string;
  severity: AccountingConfigurationValidationSeverity;
  message: string;
  blocksCandidate: boolean;
  targetId?: string | null;
  suggestedAction?: string | null;
}

export interface PostingRuleJournalCandidateResult {
  dryRunResult: RuleDryRunResult;
  selectedRuleId?: string | null;
  selectedRuleVersion?: string | null;
  generatedPostingLines: GeneratedPostingLine[];
  postingCommand?: AccountingPostingCommand | null;
  journalEntryId?: string | null;
  totalDebits: number;
  totalCredits: number;
  imbalance: number;
  isBalanced: boolean;
  hasBlockingIssues: boolean;
  canSubmitForApproval: boolean;
  canPostWithoutAdditionalApproval: boolean;
  evidenceLinks: string[];
  issues: PostingRuleJournalCandidateIssue[];
}

export interface AccountingRuleTestCase {
  testCaseId: string;
  displayName: string;
  request: RuleDryRunRequest;
  expectedRuleId?: string | null;
  expectedRuleVersion?: string | null;
  expectBalancedPosting: boolean;
  expectedIssueCodes: string[];
  expectedGeneratedPostingLines?: GeneratedPostingLine[] | null;
  evidenceLinks?: string[] | null;
}

export interface AccountingRuleTestCaseResult {
  testCaseId: string;
  displayName: string;
  passed: boolean;
  dryRunResult: RuleDryRunResult;
  assertionIssues: AccountingConfigurationValidationIssue[];
}

export interface AccountingRuleTestSuiteResult {
  fundProfileId: string;
  ledgerBookId?: string | null;
  executedAtUtc: string;
  actor: string;
  totalCount: number;
  passedCount: number;
  failedCount: number;
  results: AccountingRuleTestCaseResult[];
}

export interface ExecuteAccountingRuleTestCasesRequest {
  fundProfileId: string;
  actor: string;
  testCases?: AccountingRuleTestCase[] | null;
  ledgerBookId?: string | null;
  correlationId?: string | null;
}

export interface UpsertAccountingRuleTestCaseRequest {
  fundProfileId: string;
  testCase: AccountingRuleTestCase;
  actor: string;
  ledgerBookId?: string | null;
  correlationId?: string | null;
  evidenceLinks?: string[] | null;
  companyId?: string | null;
  reportGroupPrincipalIds?: string[] | null;
}

export interface ManualJournalEntryLine {
  lineId: string;
  side: AccountingTemplateLineSide;
  amount: number;
  currency: string;
  accountPath: string;
  entityId?: string | null;
  fundAllocationId?: string | null;
  securityId?: string | null;
  securityDisplayName?: string | null;
  taxLotId?: string | null;
  description?: string | null;
  evidenceLink?: string | null;
}

export interface ManualJournalEntryEvidenceAttachment {
  attachmentId: string;
  displayName: string;
  evidenceKind: string;
  uri: string;
  sourceSystem: string;
  addedAtUtc: string;
  addedBy: string;
  lineId?: string | null;
  description?: string | null;
}

export interface JournalEntryReversal {
  originalJournalEntryId: string;
  reversalJournalEntryId: string;
  reason: string;
  createdAtUtc: string;
  createdBy: string;
}

export interface JournalEntryRebook {
  originalJournalEntryId: string;
  rebookJournalEntryId: string;
  reason: string;
  createdAtUtc: string;
  createdBy: string;
}

export interface TreasuryLedgerContext {
  effectiveDate?: string | null;
  idempotencyKey?: string | null;
  fundEventId?: string | null;
  fundEventType?: string | null;
  capitalAccountId?: string | null;
  investorId?: string | null;
  paymentIntentId?: string | null;
  settlementReference?: string | null;
}

export interface ManualJournalEntryDraft {
  journalEntryId: string;
  status: ManualJournalEntryStatus;
  fundProfileId: string;
  ledgerBookId?: string | null;
  accountingBasis: AccountingBasisKind;
  accountingDate: string;
  periodId?: string | null;
  entityId?: string | null;
  fundNodeId?: string | null;
  currency: string;
  memo: string;
  preparedBy: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: number;
  lines: ManualJournalEntryLine[];
  evidenceLinks: string[];
  validationIssues: AccountingConfigurationValidationIssue[];
  evidenceAttachments?: ManualJournalEntryEvidenceAttachment[] | null;
  totalDebits: number;
  totalCredits: number;
  imbalance: number;
  approvalId?: string | null;
  submittedAtUtc?: string | null;
  submittedBy?: string | null;
  entryType: ManualJournalEntryType;
  treasuryContext?: TreasuryLedgerContext | null;
  dimensions?: LedgerDimensionSet | null;
  lifecycleTransitions?: JournalEntryLifecycleTransition[] | null;
  reversalOfJournalEntryId?: string | null;
  rebookedFromJournalEntryId?: string | null;
  approvedAtUtc?: string | null;
  approvedBy?: string | null;
  postedAtUtc?: string | null;
  postedBy?: string | null;
  closedLockedAtUtc?: string | null;
  closeLockedBy?: string | null;
  reversal?: JournalEntryReversal | null;
  rebook?: JournalEntryRebook | null;
  tenantId?: string | null;
  companyId?: string | null;
}

export interface JournalEntryLifecycleTransition {
  transitionId: string;
  fromStatus: ManualJournalEntryStatus;
  toStatus: ManualJournalEntryStatus;
  action: JournalEntryLifecycleAction;
  actor: string;
  recordedAtUtc: string;
  notes?: string | null;
  correlationId?: string | null;
  evidenceLinks: string[];
}

export interface JournalEntryLifecycleActionRequest {
  journalEntryId: string;
  fundProfileId: string;
  action: JournalEntryLifecycleAction;
  actor: string;
  version: number;
  notes?: string | null;
  correlationId?: string | null;
  evidenceLinks?: string[] | null;
  actionOrigin?: OperationsActionOrigin | null;
  periodIsLocked?: boolean;
  rebookLines?: ManualJournalEntryLine[] | null;
  ledgerBookId?: string | null;
  tenantId?: string | null;
  companyId?: string | null;
  reportGroupPrincipalIds?: string[] | null;
}

export interface AttachManualJournalEntryEvidenceRequest {
  journalEntryId: string;
  fundProfileId: string;
  actor: string;
  version: number;
  attachment: ManualJournalEntryEvidenceAttachment;
  correlationId?: string | null;
  evidenceLinks?: string[] | null;
  actionOrigin?: OperationsActionOrigin | null;
  periodIsLocked?: boolean;
  ledgerBookId?: string | null;
  tenantId?: string | null;
  companyId?: string | null;
  reportGroupPrincipalIds?: string[] | null;
}

export interface JournalEntryLifecycleActionResult {
  journalEntry: ManualJournalEntryDraft;
  transition: JournalEntryLifecycleTransition;
  generatedJournalEntries: ManualJournalEntryDraft[];
}

export interface PrivateCapitalFundEvent {
  fundEventId: string;
  fundEventType: string;
  entryType: ManualJournalEntryType;
  journalStatus: ManualJournalEntryStatus;
  journalEntryId: string;
  effectiveDate: string;
  capitalAccountId: string;
  investorId?: string | null;
  currency: string;
  grossAmount: number;
  netCapitalActivity: number;
  memo: string;
  paymentIntentId?: string | null;
  settlementReference?: string | null;
  evidenceLinks: string[];
  validationIssues: AccountingConfigurationValidationIssue[];
  updatedAtUtc: string;
  isPosted?: boolean;
  approvalId?: string | null;
}

export interface PrivateCapitalCapitalAccountActivity {
  capitalAccountId: string;
  investorId?: string | null;
  currency: string;
  contributions: number;
  distributions: number;
  subscriptions: number;
  redemptions: number;
  managementFees: number;
  netActivity: number;
  fundEventCount: number;
  lastEffectiveDate?: string | null;
  lastFundEventType?: string | null;
  fundEventIds: string[];
}

export interface PrivateCapitalCapitalAccountSubledgerEntry {
  subledgerEntryId: string;
  capitalAccountId: string;
  investorId?: string | null;
  currency: string;
  fundEventId: string;
  fundEventType: string;
  entryType: ManualJournalEntryType;
  approvalState: ManualJournalEntryStatus;
  journalEntryId: string;
  effectiveDate: string;
  grossAmount: number;
  netCapitalActivity: number;
  runningNetActivity: number;
  memo: string;
  evidenceLinks: string[];
  validationIssues: AccountingConfigurationValidationIssue[];
  updatedAtUtc: string;
  isPosted?: boolean;
}

export interface PrivateCapitalLedgerLineImpact {
  lineId: string;
  accountPath: string;
  side: AccountingTemplateLineSide;
  amount: number;
  currency: string;
  entityId?: string | null;
  securityId?: string | null;
  securityDisplayName?: string | null;
  evidenceLink?: string | null;
}

export interface PrivateCapitalLedgerImpact {
  ledgerImpactId: string;
  journalEntryId: string;
  fundEventId: string;
  fundEventType: string;
  capitalAccountId: string;
  investorId?: string | null;
  approvalState: ManualJournalEntryStatus;
  effectiveDate: string;
  currency: string;
  totalDebits: number;
  totalCredits: number;
  imbalance: number;
  lineCount: number;
  isBalanced: boolean;
  isPostingReady: boolean;
  evidenceLinks: string[];
  lines: PrivateCapitalLedgerLineImpact[];
  validationIssues: AccountingConfigurationValidationIssue[];
}

export interface PrivateCapitalEvidenceCategory {
  categoryId: string;
  label: string;
  isReady: boolean;
  summary: string;
  evidenceLinkCount: number;
  evidenceLinks: string[];
  requiredEvidence?: string[] | null;
}

export interface PrivateCapitalPaymentIntentEvidence {
  paymentIntentId?: string | null;
  settlementReference?: string | null;
  status: PrivateCapitalPaymentIntentEvidenceStatus | PaymentIntentWorkflowStatus;
  isReady: boolean;
  direction: PaymentIntentCashDirection;
  amount: number;
  currency: string;
  effectiveDate: string;
  summary: string;
  cashEvidenceLinkCount: number;
  cashEvidenceLinks: string[];
  requiredEvidence?: string[] | null;
  evidenceRoute?: string | null;
}

export interface PaymentIntentExpectedCashMovement {
  paymentIntentId: string;
  direction: PaymentIntentCashDirection;
  amount: number;
  currency: string;
  effectiveDate: string;
  settlementReference?: string | null;
  fundEventId?: string | null;
  fundEventType?: string | null;
  capitalAccountId?: string | null;
  investorId?: string | null;
  purpose: string;
  payee?: string | null;
  accountScope?: string | null;
  businessPurpose?: string | null;
  approvalPolicy?: string | null;
  sourceEvidenceLinks?: string[] | null;
}

export interface PaymentIntentApprovalStep {
  sequence: number;
  role: string;
  actor: string;
  status: string;
  decidedAtUtc?: string | null;
  evidenceRoute?: string | null;
}

export interface PaymentIntentBankEvidence {
  evidenceId: string;
  evidenceKind: string;
  status: string;
  summary: string;
  bankTransactionId?: string | null;
  transactionType?: string | null;
  amount?: number | null;
  currency?: string | null;
  effectiveDate?: string | null;
  recordedAtUtc?: string | null;
  externalRef?: string | null;
  evidenceRoute?: string | null;
  recordedBy?: string | null;
}

export interface PaymentIntentReconciliationLink {
  linkId: string;
  status: string;
  summary: string;
  evidenceRoute?: string | null;
  reconciliationCaseId?: string | null;
  reconciliationRunId?: string | null;
}

export interface PaymentIntentAuditEvent {
  auditEventId: string;
  recordedAtUtc: string;
  actor: string;
  action: string;
  summary: string;
  evidenceLinks: string[];
}

export interface PaymentIntentWorkflow {
  paymentIntentId: string;
  settlementReference?: string | null;
  fundProfileId: string;
  ledgerBookId?: string | null;
  fundEventId: string;
  journalEntryId: string;
  requester: string;
  requestedAtUtc: string;
  status: PaymentIntentWorkflowStatus;
  statusLabel: string;
  readinessReason: string;
  executionDeferredReason: string;
  expectedCashMovement: PaymentIntentExpectedCashMovement;
  evidenceRoute: string;
  workbenchRoute: string;
  approvalChain: PaymentIntentApprovalStep[];
  bankEvidence: PaymentIntentBankEvidence[];
  reconciliationLinks: PaymentIntentReconciliationLink[];
  auditHistory: PaymentIntentAuditEvent[];
}

export interface PrivateCapitalReportOutput {
  reportOutputId: string;
  reportOutputType: string;
  displayName: string;
  reportRoute: string;
  fundEventId: string;
  fundEventType: string;
  capitalAccountId: string;
  investorId?: string | null;
  approvalState: ManualJournalEntryStatus;
  effectiveDate: string;
  currency: string;
  netCapitalActivity: number;
  evidenceLinkCount: number;
  evidenceLinks: string[];
  isReportReady: boolean;
  validationIssues: AccountingConfigurationValidationIssue[];
  isPublished?: boolean;
  reportPackId?: string | null;
  reportWorkflowState?: string | null;
  publicationManifestId?: string | null;
  retainedManifestPath?: string | null;
  publicationEvidenceHash?: string | null;
  publishedAtUtc?: string | null;
  publishedBy?: string | null;
  reportLineProvenanceCount?: number;
  reportOutputRoute?: string | null;
  fundEventRecordRoute?: string | null;
  capitalAccountSubledgerRoute?: string | null;
  evidenceRoute?: string | null;
  approvalRoute?: string | null;
  readinessLabel?: string | null;
  readinessReason?: string | null;
  nextAction?: string | null;
  nextActionRoute?: string | null;
}

export interface PrivateCapitalFundEventLedgerRecord {
  fundEventRecordId: string;
  fundEventId: string;
  fundEventType: string;
  capitalAccountId: string;
  investorId?: string | null;
  approvalState: ManualJournalEntryStatus;
  journalEntryId: string;
  effectiveDate: string;
  currency: string;
  grossAmount: number;
  netCapitalActivity: number;
  capitalAccountOpeningNetActivity: number;
  capitalAccountEndingNetActivity: number;
  memo: string;
  paymentIntentId?: string | null;
  settlementReference?: string | null;
  activityRoute: string;
  evidenceRoute: string;
  approvalId?: string | null;
  approvalRoute?: string | null;
  isPosted: boolean;
  isPostingReady: boolean;
  isReportReady: boolean;
  isPublished: boolean;
  readiness: PrivateCapitalFundEventLedgerReadiness;
  readinessLabel: string;
  readinessReason: string;
  nextAction: string;
  nextActionRoute?: string | null;
  evidenceLinkCount: number;
  capitalAccountSubledgerEntryCount: number;
  ledgerImpactCount: number;
  reportOutputCount: number;
  validationIssueCount: number;
  primaryReportOutputId?: string | null;
  primaryReportOutputType?: string | null;
  primaryReportRoute?: string | null;
  reportWorkflowState?: string | null;
  publicationManifestId?: string | null;
  retainedManifestPath?: string | null;
  reportLineProvenanceCount: number;
  evidenceLinks: string[];
  evidenceCategories?: PrivateCapitalEvidenceCategory[] | null;
  fundEvent: PrivateCapitalFundEvent;
  capitalAccountSubledgerEntries: PrivateCapitalCapitalAccountSubledgerEntry[];
  ledgerImpacts: PrivateCapitalLedgerImpact[];
  reportOutputs: PrivateCapitalReportOutput[];
  validationIssues: AccountingConfigurationValidationIssue[];
  paymentIntentEvidence?: PrivateCapitalPaymentIntentEvidence | null;
}

export interface PrivateCapitalFundEventCommandCenterLane {
  laneId: string;
  label: string;
  status: string;
  isReady: boolean;
  summary: string;
  route?: string | null;
  evidenceLinkCount: number;
  evidenceLinks: string[];
  requiredActions: string[];
}

export interface PrivateCapitalFundEventCommandCenterSupportPackage {
  packageId: string;
  label: string;
  status: string;
  route?: string | null;
  evidenceLinkCount: number;
  evidenceLinks: string[];
  requiredActions: string[];
}

export interface PrivateCapitalFundEventCommandCenter {
  fundEventId: string;
  fundEventType: string;
  fundProfileId: string;
  ledgerBookId?: string | null;
  projectedAtUtc: string;
  commandCenterRoute: string;
  readiness: PrivateCapitalFundEventLedgerReadiness;
  readinessLabel: string;
  readinessReason: string;
  nextAction: string;
  nextActionRoute?: string | null;
  readyLaneCount: number;
  blockedLaneCount: number;
  fundEventRecord: PrivateCapitalFundEventLedgerRecord;
  lanes: PrivateCapitalFundEventCommandCenterLane[];
  supportPackages: PrivateCapitalFundEventCommandCenterSupportPackage[];
  liveCapabilities: string[];
  plannedCapabilities: string[];
}

export interface PrivateCapitalCloseCockpitWorkflow {
  workflowId: string;
  fundAccountId: string;
  periodId: string;
  status: OperationsWorkflowStatus;
  closeReadinessScore: number;
  isReadyToClose: boolean;
  workflowRoute: string;
  closePackageId?: string | null;
  closePackageRoute?: string | null;
  blockerCount: number;
  openChecklistCount: number;
  updatedAtUtc: string;
}

export interface PrivateCapitalCloseCockpitApproval {
  approvalId: string;
  workflowId: string;
  fundAccountId: string;
  periodId: string;
  status: OperationsApprovalState;
  operator?: string | null;
  reviewer?: string | null;
  rationale?: string | null;
  submittedAtUtc?: string | null;
  decidedAtUtc?: string | null;
  workflowRoute: string;
  evidenceLinkCount: number;
  evidenceLinks: OperationsEvidenceLink[];
}

export interface PrivateCapitalNavSupportComponent {
  componentId: string;
  label: string;
  status: EvidenceStatus;
  isReady: boolean;
  summary: string;
  route?: string | null;
  score: number;
}

export interface PrivateCapitalNavSupportPackage {
  packageId: string;
  label: string;
  status: EvidenceStatus;
  isReady: boolean;
  summary: string;
  route?: string | null;
  shadowNav?: number | null;
  currency?: string | null;
  evidenceLinkCount: number;
  evidenceLinks: OperationsEvidenceLink[];
  components: PrivateCapitalNavSupportComponent[];
  requiredActions: string[];
}

export interface PrivateCapitalCloseCockpitLane {
  laneId: string;
  label: string;
  status: EvidenceStatus;
  isReady: boolean;
  summary: string;
  route?: string | null;
  evidenceLinkCount: number;
  evidenceLinks: OperationsEvidenceLink[];
  requiredActions: string[];
}

export interface PrivateCapitalCloseCockpit {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  fundAccountId?: string | null;
  periodId?: string | null;
  entityId?: string | null;
  projectedAtUtc: string;
  cockpitRoute: string;
  overallStatus: EvidenceStatus;
  isReadyToClose: boolean;
  readinessScore: number;
  workflowCount: number;
  fundEventCount: number;
  capitalAccountCount: number;
  reportOutputCount: number;
  deliveredReportOutputCount: number;
  readyLaneCount: number;
  blockedLaneCount: number;
  lanes: PrivateCapitalCloseCockpitLane[];
  workflows: PrivateCapitalCloseCockpitWorkflow[];
  blockers: OperationsCloseReadinessBlocker[];
  nextActions: OperationsNextAction[];
  liveCapabilities: string[];
  plannedCapabilities: string[];
  approvalHistory?: PrivateCapitalCloseCockpitApproval[] | null;
  navSupportPackages?: PrivateCapitalNavSupportPackage[] | null;
  evidencePackages?: OperationsEvidencePackageSummary[] | null;
}

export interface PrivateCapitalCapitalAccountSubledger {
  subledgerId: string;
  fundProfileId: string;
  ledgerBookId?: string | null;
  projectedAtUtc: string;
  capitalAccountId: string;
  investorId?: string | null;
  currency: string;
  activityRoute: string;
  contributions: number;
  distributions: number;
  subscriptions: number;
  redemptions: number;
  managementFees: number;
  openingNetActivity: number;
  endingNetActivity: number;
  netCapitalActivity: number;
  fundEventCount: number;
  approvalQueueCount: number;
  postedFundEventCount: number;
  publishedReportOutputCount: number;
  evidenceLinkCount: number;
  validationIssueCount: number;
  firstEffectiveDate?: string | null;
  lastEffectiveDate?: string | null;
  lastFundEventType?: string | null;
  readiness?: PrivateCapitalFundEventLedgerReadiness;
  readinessLabel?: string | null;
  readinessReason?: string | null;
  nextAction?: string | null;
  nextActionRoute?: string | null;
  evidenceLinks: string[];
  evidenceCategories?: PrivateCapitalEvidenceCategory[] | null;
  paymentIntentEvidence?: PrivateCapitalPaymentIntentEvidence | null;
  capitalAccount?: PrivateCapitalCapitalAccountActivity | null;
  fundEventRecords: PrivateCapitalFundEventLedgerRecord[];
  subledgerEntries: PrivateCapitalCapitalAccountSubledgerEntry[];
  ledgerImpacts: PrivateCapitalLedgerImpact[];
  reportOutputs: PrivateCapitalReportOutput[];
  validationIssues: AccountingConfigurationValidationIssue[];
}

export interface PrivateCapitalActivityProjection {
  fundProfileId: string;
  ledgerBookId?: string | null;
  projectedAtUtc: string;
  fundEventCount: number;
  capitalAccountCount: number;
  submittedFundEventCount: number;
  approvalQueueCount: number;
  postedFundEventCount: number;
  publishedReportOutputCount: number;
  netCapitalActivity: number;
  currency: string;
  fundEvents: PrivateCapitalFundEvent[];
  capitalAccounts: PrivateCapitalCapitalAccountActivity[];
  capitalAccountSubledgerEntries: PrivateCapitalCapitalAccountSubledgerEntry[];
  ledgerImpacts: PrivateCapitalLedgerImpact[];
  reportOutputs: PrivateCapitalReportOutput[];
  fundEventRecords: PrivateCapitalFundEventLedgerRecord[];
  capitalAccountSubledgers?: PrivateCapitalCapitalAccountSubledger[] | null;
  paymentIntents?: PaymentIntentWorkflow[] | null;
  validationIssues: AccountingConfigurationValidationIssue[];
}

export interface CapitalAccountWorkbenchInvestorAccount {
  accountKey: string;
  capitalAccountId: string;
  investorId?: string | null;
  currency: string;
  activityRoute: string;
  readiness: PrivateCapitalFundEventLedgerReadiness;
  readinessLabel: string;
  readinessReason: string;
  nextAction: string;
  nextActionRoute?: string | null;
  openingNetActivity: number;
  endingNetActivity: number;
  netCapitalActivity: number;
  contributions: number;
  distributions: number;
  subscriptions: number;
  redemptions: number;
  managementFees: number;
  fundEventCount: number;
  postedFundEventCount: number;
  approvalQueueCount: number;
  publishedReportOutputCount: number;
  evidenceLinkCount: number;
  validationIssueCount: number;
  evidenceCategorySummary: string;
  evidenceLinks: string[];
  evidenceCategories: PrivateCapitalEvidenceCategory[];
  fundEventRecords: PrivateCapitalFundEventLedgerRecord[];
  subledgerEntries: PrivateCapitalCapitalAccountSubledgerEntry[];
  ledgerImpacts: PrivateCapitalLedgerImpact[];
  reportOutputs: PrivateCapitalReportOutput[];
  validationIssues: AccountingConfigurationValidationIssue[];
  paymentIntentEvidence?: PrivateCapitalPaymentIntentEvidence | null;
}

export interface CapitalAccountWorkbenchAllocationRule {
  ruleId: string;
  capitalAccountId: string;
  investorId?: string | null;
  categoryId: string;
  label: string;
  basis: string;
  isSatisfied: boolean;
  reason: string;
  route?: string | null;
  evidenceLinkCount: number;
  evidenceLinks: string[];
  requiredEvidence: string[];
  ruleVersion?: string | null;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  formula?: string | null;
  approvalState?: string | null;
  approvalReference?: string | null;
  replayTrace?: string | null;
  inputs?: CapitalAccountWorkbenchAllocationInput[] | null;
  relatedFundEventIds?: string[] | null;
}

export interface CapitalAccountWorkbenchAllocationInput {
  inputId: string;
  kind: string;
  sourceId: string;
  label: string;
  amount?: number | null;
  currency?: string | null;
  effectiveDate?: string | null;
  evidenceRoute?: string | null;
}

export interface CapitalAccountWorkbenchStatementLineage {
  lineageId: string;
  capitalAccountId: string;
  investorId?: string | null;
  reportOutputId: string;
  reportOutputType: string;
  displayName: string;
  reportRoute: string;
  reportPackId?: string | null;
  reportWorkflowState?: string | null;
  isPublished: boolean;
  isReportReady: boolean;
  publicationManifestId?: string | null;
  retainedManifestPath?: string | null;
  publicationEvidenceHash?: string | null;
  publishedAtUtc?: string | null;
  publishedBy?: string | null;
  reportLineProvenanceCount: number;
  hasRestatementLineage: boolean;
  restatementStatus: string;
  restatementReasonCode?: string | null;
  restatementPriorVersionReportId?: string | null;
  restatementApprover?: string | null;
  restatementChangedLineCount: number;
  restatementEvidenceLinkCount: number;
  reportOutputRoute?: string | null;
  evidenceRoute?: string | null;
  capitalAccountSubledgerRoute?: string | null;
  evidenceLinks: string[];
  restatementEvidenceLinks: string[];
  restatementChangedLines?: CapitalAccountWorkbenchRestatementChangedLine[] | null;
}

export interface CapitalAccountWorkbenchRestatementChangedLine {
  lineKey: string;
  previousValue: string;
  currentValue: string;
  evidenceLinkCount: number;
  evidenceLinks: string[];
}

export interface CapitalAccountWorkbenchAuditDrillThrough {
  drillThroughId: string;
  kind: string;
  label: string;
  summary: string;
  route?: string | null;
  isAvailable: boolean;
  evidenceLinkCount: number;
  evidenceLinks: string[];
  relatedIds: string[];
}

export interface CapitalAccountWorkbench {
  fundProfileId: string;
  ledgerBookId?: string | null;
  projectedAtUtc: string;
  capitalAccountId?: string | null;
  investorId?: string | null;
  currency: string;
  workbenchRoute: string;
  statusLabel: string;
  statusReason: string;
  investorAccountCount: number;
  fundEventCount: number;
  statementCount: number;
  restatementLineageCount: number;
  auditDrillThroughCount: number;
  netCapitalActivity: number;
  investorAccounts: CapitalAccountWorkbenchInvestorAccount[];
  allocationRules: CapitalAccountWorkbenchAllocationRule[];
  statementLineage: CapitalAccountWorkbenchStatementLineage[];
  auditDrillThroughs: CapitalAccountWorkbenchAuditDrillThrough[];
  validationIssues: AccountingConfigurationValidationIssue[];
  liveCapabilities: string[];
  plannedCapabilities: string[];
}

export interface ManualJournalEntryWorkbench {
  fundProfileId: string;
  ledgerBookId?: string | null;
  loadedAtUtc: string;
  ledgerBooks: LedgerBook[];
  chartOfAccounts: ChartOfAccountsNode[];
  drafts: ManualJournalEntryDraft[];
  auditTrail: AccountingActionAuditEvent[];
  privateCapitalActivity?: PrivateCapitalActivityProjection | null;
}

export interface SaveManualJournalEntryDraftRequest {
  draft: ManualJournalEntryDraft;
  actor: string;
  correlationId?: string | null;
  evidenceLinks?: string[] | null;
  tenantId?: string | null;
  companyId?: string | null;
  reportGroupPrincipalIds?: string[] | null;
  periodIsLocked?: boolean;
  ledgerBookId?: string | null;
}

export interface ValidateManualJournalEntryDraftRequest {
  draft: ManualJournalEntryDraft;
  actor: string;
  correlationId?: string | null;
  periodIsLocked?: boolean;
  ledgerBookId?: string | null;
  tenantId?: string | null;
  companyId?: string | null;
}

export interface SubmitManualJournalEntryApprovalRequest {
  journalEntryId: string;
  fundProfileId: string;
  actor: string;
  version: number;
  notes?: string | null;
  correlationId?: string | null;
  evidenceLinks?: string[] | null;
  actionOrigin?: OperationsActionOrigin | null;
  periodIsLocked?: boolean;
  ledgerBookId?: string | null;
  tenantId?: string | null;
  companyId?: string | null;
  reportGroupPrincipalIds?: string[] | null;
}

export interface PreviewJournalTemplateRequest {
  fundProfileId: string;
  templateId: string;
  actor: string;
  ledgerBookId?: string | null;
  correlationId?: string | null;
}

export interface UpsertChartOfAccountsNodeRequest {
  fundProfileId: string;
  node: ChartOfAccountsNode;
  actor: string;
  correlationId?: string | null;
  evidenceLinks?: string[] | null;
  companyId?: string | null;
  reportGroupPrincipalIds?: string[] | null;
}

export interface UpsertJournalEntryTemplateRequest {
  fundProfileId: string;
  template: JournalEntryTemplate;
  actor: string;
  correlationId?: string | null;
  evidenceLinks?: string[] | null;
  companyId?: string | null;
  reportGroupPrincipalIds?: string[] | null;
}

export interface UpsertPostingRuleRequest {
  fundProfileId: string;
  rule: PostingRule;
  actor: string;
  correlationId?: string | null;
  evidenceLinks?: string[] | null;
  ledgerBookId?: string | null;
}

export interface ApprovePostingRulePromotionRequest {
  fundProfileId: string;
  ruleId: string;
  ruleVersion: string;
  actor: string;
  approvalId: string;
  notes: string;
  evidenceLinks?: string[] | null;
  requestedBy?: string | null;
  requestedAtUtc?: string | null;
  correlationId?: string | null;
  companyId?: string | null;
  reportGroupPrincipalIds?: string[] | null;
  ledgerBookId?: string | null;
}

export interface ActivateAccountingConfigurationRequest {
  fundProfileId: string;
  actor: string;
  ledgerBookId?: string | null;
  correlationId?: string | null;
  evidenceLinks?: string[] | null;
  companyId?: string | null;
  reportGroupPrincipalIds?: string[] | null;
  actionOrigin?: OperationsActionOrigin;
}

export interface LedgerTrialBalanceLine {
  accountName: string;
  accountType: string;
  symbol: string | null;
  financialAccountId: string | null;
  balance: number;
  entryCount: number;
  security: WorkstationSecurityReference | null;
  accountScopeId?: string | null;
  accountScopeDisplayName?: string | null;
  entityScopeId?: string | null;
  entityScopeDisplayName?: string | null;
  sleeveScopeId?: string | null;
  sleeveScopeDisplayName?: string | null;
  vehicleScopeId?: string | null;
  vehicleScopeDisplayName?: string | null;
  dimensions?: LedgerDimensionSet | null;
  accountingBasis?: AccountingBasisKind;
  accountingPolicyId?: string;
  accountingPolicyVersion?: string;
  ruleId?: string | null;
  ruleVersion?: string | null;
  sourceEventId?: string | null;
  sourceEventIds?: string[];
  sourceJournalEntryId?: string | null;
  approvalIds?: string[];
}

export interface LedgerJournalLine {
  journalEntryId: string;
  timestamp: string;
  description: string;
  totalDebits: number;
  totalCredits: number;
  lineCount: number;
  accountScopeId?: string | null;
  accountScopeDisplayName?: string | null;
  entityScopeId?: string | null;
  entityScopeDisplayName?: string | null;
  sleeveScopeId?: string | null;
  sleeveScopeDisplayName?: string | null;
  vehicleScopeId?: string | null;
  vehicleScopeDisplayName?: string | null;
  dimensions?: LedgerDimensionSet | null;
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

export interface ExpectedJournalPreviewLine {
  accountName: string;
  accountType: string;
  symbol: string | null;
  debit: number;
  credit: number;
}

export interface ExpectedJournalPreview {
  journalPreviewId: string;
  expectedEventId: string;
  description: string;
  eventDate: string;
  isBalanced: boolean;
  requiresOperatorApproval: boolean;
  idempotencyKey: string;
  lines: ExpectedJournalPreviewLine[];
}

export interface LedgerImpactPreview {
  draftEntryCount: number;
  netDebitEffect: number;
  netCreditEffect: number;
  netBalanceDelta: number;
  hasValidationWarnings: boolean;
  validationFlags: string[];
}

export type InvestmentAccountingTransactionKind =
  | "Trade"
  | "Dividend"
  | "Fee"
  | "Accrual"
  | "CorporateAction"
  | "BrokerReconciliation";

export type InvestmentAccountingTradeSide = "Buy" | "Sell";
export type InvestmentAccountingPreviewMode = "TransactionLab" | "BooksBeforeBroker";

export interface InvestmentAccountingTransactionLabRequest {
  kind: InvestmentAccountingTransactionKind;
  fundAccountId: string;
  symbol: string;
  eventDate: string;
  currency: string;
  amount: number;
  quantity?: number;
  price?: number;
  feeAmount?: number;
  side?: InvestmentAccountingTradeSide | null;
  sourceRunId?: string | null;
  sourceSessionId?: string | null;
  brokerStatementId?: string | null;
  reconciliationCaseId?: string | null;
  evidenceIds?: string[] | null;
  previewMode?: InvestmentAccountingPreviewMode;
}

export interface InvestmentAccountingTrialBalanceImpact {
  accountName: string;
  accountType: string;
  symbol: string | null;
  balanceDelta: number;
  explanation: string;
}

export interface InvestmentAccountingReconciliationExpectation {
  expectedState: string;
  expectedBreakType: string;
  detail: string;
  evidenceIds: string[];
  brokerStatementId?: string | null;
  reconciliationCaseId?: string | null;
}

export interface BooksBeforeBrokerReadiness {
  isBooksBeforeBrokerMode: boolean;
  canStageBrokerAction: boolean;
  expectedBrokerAction: string;
  brokerInstructionSummary: string;
  requiredApprovals: string[];
  blockers: string[];
  evidenceIds: string[];
}

export interface InvestmentAccountingTransactionLabPreview {
  previewId: string;
  kind: InvestmentAccountingTransactionKind;
  fundAccountId: string;
  symbol: string;
  eventDate: string;
  currency: string;
  journalPreview: ExpectedJournalPreview;
  ledgerImpact: LedgerImpactPreview;
  trialBalanceImpact: InvestmentAccountingTrialBalanceImpact[];
  reconciliationExpectation: InvestmentAccountingReconciliationExpectation;
  evidenceIds: string[];
  sourceRunId?: string | null;
  sourceSessionId?: string | null;
  booksBeforeBroker?: BooksBeforeBrokerReadiness | null;
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

export interface CashFlowEntry {
  timestamp: string;
  amount: number;
  eventKind: string;
  symbol: string | null;
  currency: string;
  accountId: string | null;
  description: string | null;
}

export interface CashLadderBucket {
  bucketStart: string;
  bucketEnd: string;
  projectedInflows: number;
  projectedOutflows: number;
  netFlow: number;
  currency: string;
  eventCount: number;
}

export interface RunCashLadder {
  asOf: string;
  currency: string;
  bucketDays: number;
  totalProjectedInflows: number;
  totalProjectedOutflows: number;
  netPosition: number;
  buckets: CashLadderBucket[];
}

export interface RunCashFlowSummary {
  runId: string;
  asOf: string;
  currency: string;
  totalEntries: number;
  totalInflows: number;
  totalOutflows: number;
  netCashFlow: number;
  entries: CashFlowEntry[];
  ladder: RunCashLadder;
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

export type SecurityIdentifierKind =
  | "Ticker"
  | "Isin"
  | "Cusip"
  | "Sedol"
  | "Figi"
  | "OccOptionSymbol"
  | "ProviderSymbol"
  | "InternalCode"
  | "Lei"
  | "PermId"
  | "Bbgid"
  | "Wkn"
  | "Valoren"
  | "PermTicker"
  | "Ric"
  | "Cik";

export type SecurityAssetProfileStatus = "Draft" | "Approved" | "Superseded" | "Retired";

export type SecurityAssetProfileFieldType =
  | "Text"
  | "Decimal"
  | "Integer"
  | "Boolean"
  | "Date"
  | "Enum"
  | "CurrencyCode"
  | "SecurityLink";

export type SecurityAssetProfileAccountingImpactHint =
  | "Valuation"
  | "LedgerClassification"
  | "CommitmentAccounting"
  | "NavBasedValuation"
  | "FactorSchedule"
  | "OwnershipPercentage"
  | "IncomeAccrual";

export interface SecurityAssetProfileFieldDefinition {
  key: string;
  label: string;
  fieldType: SecurityAssetProfileFieldType;
  isRequired: boolean;
  allowedValues: string[];
  description: string | null;
  minValue: number | null;
  maxValue: number | null;
  isProjected: boolean;
  isSearchable: boolean;
}

export interface SecurityAssetProfileDateOrderRule {
  startFieldKey: string;
  endFieldKey: string;
  code: string;
  message: string;
}

export interface SecurityAssetProfileIdentifierPreference {
  kind: SecurityIdentifierKind;
  isRequiredForClose: boolean;
  reason: string;
}

export interface SecurityAssetProfileDefinition {
  profileId: string;
  version: number;
  name: string;
  category: string;
  subType: string | null;
  status: SecurityAssetProfileStatus;
  fields: SecurityAssetProfileFieldDefinition[];
  identifierPreferences: SecurityAssetProfileIdentifierPreference[];
  lifecycleStates: string[];
  accountingImpactHints: SecurityAssetProfileAccountingImpactHint[];
  dateOrderRules: SecurityAssetProfileDateOrderRule[];
  effectiveFrom: string;
  effectiveTo: string | null;
  approvedBy: string;
  approvedAtUtc: string;
  changeReason: string;
}

export interface SecurityAssetProfileDraftRequest {
  profileId: string;
  name: string;
  category: string;
  subType: string | null;
  fields: SecurityAssetProfileFieldDefinition[];
  identifierPreferences: SecurityAssetProfileIdentifierPreference[];
  lifecycleStates: string[];
  accountingImpactHints: SecurityAssetProfileAccountingImpactHint[];
  dateOrderRules: SecurityAssetProfileDateOrderRule[];
  requestedBy: string | null;
  rationale: string;
  correlationId?: string | null;
}

export interface SecurityAssetProfileApprovalRequest {
  profileId: string;
  version: number;
  effectiveFrom: string;
  approvalReference: string;
  requestedBy: string | null;
  rationale: string;
  correlationId?: string | null;
}

export interface SecurityAssetProfileRollbackRequest {
  profileId: string;
  targetVersion: number;
  effectiveFrom: string;
  approvalReference: string;
  requestedBy: string | null;
  rationale: string;
  correlationId?: string | null;
}

export interface SecurityAssetProfileGovernanceAuditEvent {
  auditId: string;
  eventType: string;
  occurredAtUtc: string;
  actor: string;
  rationale: string;
  correlationId: string;
  profileId: string;
  version: number;
  status: SecurityAssetProfileStatus;
  previousVersion: number | null;
  approvalReference: string | null;
}

export interface SecurityAssetProfileLineage {
  profileId: string;
  versions: SecurityAssetProfileDefinition[];
  auditEvents: SecurityAssetProfileGovernanceAuditEvent[];
}

export interface SecurityAssetProfileGovernanceResult {
  profile: SecurityAssetProfileDefinition;
  lineage: SecurityAssetProfileLineage;
  auditEvent: SecurityAssetProfileGovernanceAuditEvent;
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
  | "plaid"
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
  apiKeyLabel?: string;
  apiKeyAriaLabel?: string;
  apiSecretLabel?: string;
  apiSecretAriaLabel?: string;
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

export interface PlaidInstitution {
  institutionId: string;
  name: string;
  products: string[];
  countryCodes: string[];
  url?: string | null;
  primaryColor?: string | null;
  logo?: string | null;
}

export interface PlaidInstitutionSearchResult {
  query: string;
  institutions: PlaidInstitution[];
  requestId?: string | null;
}

export interface PlaidAccountLinkRequest {
  plaidAccountId: string;
  name: string;
  officialName?: string | null;
  mask?: string | null;
  type: string;
  subtype?: string | null;
  persistentAccountId?: string | null;
  meridianAccountId?: string | null;
  entityId?: string | null;
}

export interface PlaidLinkTokenRequest {
  userId: string;
  meridianAccountId?: string | null;
  products?: string[] | null;
  webhookUrl?: string | null;
  clientName?: string | null;
  language?: string | null;
  countryCodes?: string[] | null;
  institutionId?: string | null;
  institutionName?: string | null;
}

export interface PlaidLinkTokenResponse {
  linkToken: string;
  expiration?: string | null;
  requestId?: string | null;
  products: string[];
  institutionId?: string | null;
  institutionName?: string | null;
  environment?: string | null;
}

export interface PlaidPublicTokenExchangeRequest {
  publicToken: string;
  institutionId?: string | null;
  institutionName?: string | null;
  accounts: PlaidAccountLinkRequest[];
  requestedBy: string;
}

export interface PlaidPublicTokenExchangeResult {
  item: {
    itemId: string;
    institutionId: string;
    institutionName: string;
    status: string;
    linkedAt: string;
  };
  accounts: Array<{
    plaidAccountId: string;
    name: string;
    mask?: string | null;
    type: string;
    subtype?: string | null;
  }>;
  requestId?: string | null;
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

export type ProviderIntegrationType =
  | "Rest"
  | "OpenApiRest"
  | "GraphQl"
  | "Webhook"
  | "SftpFile"
  | "ManualUpload"
  | "Hybrid"
  | "StreamingTemplate"
  | "CertifiedTradingAdapter";

export type ProviderIntegrationCapabilityKind =
  | "Accounts"
  | "Balances"
  | "Positions"
  | "Holdings"
  | "Transactions"
  | "TaxLots"
  | "SecurityReferenceData"
  | "MarketPrices"
  | "CorporateActions"
  | "Documents"
  | "Alerts"
  | "Events"
  | "OrderPreview"
  | "OrderPlacement"
  | "OrderCancellation"
  | "OrderStatus"
  | "Executions";

export type ProviderIntegrationActivationState =
  | "Draft"
  | "Tested"
  | "DryRunPassed"
  | "PendingApproval"
  | "Active"
  | "Paused"
  | "Failed"
  | "Retired";

export type ProviderIntegrationProcessingStatus =
  | "Received"
  | "Parsed"
  | "Mapped"
  | "Validated"
  | "Quarantined"
  | "Loaded"
  | "Published"
  | "Blocked";

export type ProviderIntegrationAuthType =
  | "None"
  | "ApiKey"
  | "BearerToken"
  | "OAuth2"
  | "ClientCredentials"
  | "Basic"
  | "Certificate"
  | "CustomHeader";

export type ProviderIntegrationHttpMethod = "Get" | "Post" | "Put" | "Patch" | "Delete";
export type ProviderIntegrationPaginationType = "None" | "PageNumber" | "Offset" | "Cursor" | "NextUrl";

export type ProviderIntegrationCursorType =
  | "None"
  | "Timestamp"
  | "Date"
  | "CursorToken"
  | "PageNumber"
  | "Offset"
  | "Watermark"
  | "FullSnapshot";

export type ProviderMappingConfidence = "Low" | "Medium" | "High" | "Approved";
export type ProviderIntegrationIssueSeverity = "Info" | "Warning" | "Critical";

export type ProviderIntegrationQuarantineResolutionAction =
  | "ReviewOnly"
  | "ReplayAfterMappingChange"
  | "IgnoreProviderRecord"
  | "MarkAsCashPosition";

export type ProviderIntegrationIdentityResolutionStatus =
  | "Resolved"
  | "ReviewRequired"
  | "MissingIdentifier"
  | "NotFound"
  | "NotConfigured";

export type ProviderIntegrationPromotionReadinessStatus =
  | "ReadyForReconciliation"
  | "ReviewRequired"
  | "Blocked";

export interface ProviderIntegrationValidationIssue {
  code: string;
  severity: ProviderIntegrationIssueSeverity;
  message: string;
  targetField?: string | null;
  suggestedFix?: string | null;
}

export interface ProviderIntegrationAuthConfig {
  type: ProviderIntegrationAuthType;
  tokenUrl?: string | null;
  scopes: string[];
  metadata: Record<string, string>;
}

export interface ProviderIntegrationCapability {
  capability: ProviderIntegrationCapabilityKind;
  enabled: boolean;
  requiresCertifiedAdapter: boolean;
  requiredCanonicalFields: string[];
}

export interface ProviderIntegrationEndpointDependency {
  endpointKey: string;
  outputPath: string;
  parameterName: string;
}

export interface ProviderIntegrationEndpointPagination {
  type: ProviderIntegrationPaginationType;
  cursorPath?: string | null;
  cursorParam?: string | null;
  nextUrlPath?: string | null;
  pageSize?: number | null;
}

export interface ProviderIntegrationEndpointResponseShape {
  recordsPath: string;
  schemaFingerprint?: string | null;
  requiredPaths: string[];
}

export interface ProviderIntegrationEndpointDefinition {
  endpointKey: string;
  capability: ProviderIntegrationCapabilityKind;
  method: ProviderIntegrationHttpMethod;
  path: string;
  headers: Record<string, string>;
  query: Record<string, string>;
  requestBodyTemplate?: string | null;
  dependsOn?: ProviderIntegrationEndpointDependency | null;
  pagination: ProviderIntegrationEndpointPagination;
  response: ProviderIntegrationEndpointResponseShape;
}

export interface ProviderIntegrationTransformRule {
  type: string;
  parameters: Record<string, string>;
}

export interface ProviderIntegrationFieldMapping {
  capability: ProviderIntegrationCapabilityKind;
  sourcePath: string;
  targetField: string;
  transform?: ProviderIntegrationTransformRule | null;
  required: boolean;
  confidence: ProviderMappingConfidence;
  defaultValue?: string | null;
  constantValue?: string | null;
}

export interface ProviderIntegrationValidationRule {
  capability: ProviderIntegrationCapabilityKind;
  ruleCode: string;
  severity: ProviderIntegrationIssueSeverity;
  message: string;
  targetFields: string[];
}

export interface ProviderIntegrationSyncSchedule {
  mode: string;
  frequency: string;
  time?: string | null;
  timezone: string;
  cursorType: ProviderIntegrationCursorType;
  cursorField?: string | null;
  fullRefreshFrequency?: string | null;
}

export interface ProviderIntegrationActivationPolicy {
  requiresAuthenticationTest: boolean;
  requiresEndpointTest: boolean;
  requiresDryRun: boolean;
  requiresApproval: boolean;
  productionWriteCapabilitiesAllowed: boolean;
  requiredIssueCodes: string[];
}

export interface ProviderIntegrationActivationIssue {
  code: string;
  severity: ProviderIntegrationIssueSeverity;
  message: string;
  capability?: ProviderIntegrationCapabilityKind | null;
  suggestedFix?: string | null;
}

export interface ProviderIntegrationActivationReadiness {
  isReady: boolean;
  issues: ProviderIntegrationActivationIssue[];
  requiredEvidence: string[];
}

export interface ProviderIntegrationTemplateCatalogEntry {
  manifestId: string;
  providerId: string;
  displayName: string;
  integrationType: ProviderIntegrationType;
  capabilities: ProviderIntegrationCapabilityKind[];
  summary: string;
  requiresCredentials: boolean;
}

export interface ProviderIntegrationManifest {
  manifestId: string;
  manifestVersion: number;
  providerId: string;
  displayName: string;
  integrationType: ProviderIntegrationType;
  environment: string;
  auth: ProviderIntegrationAuthConfig;
  capabilities: ProviderIntegrationCapability[];
  endpoints: ProviderIntegrationEndpointDefinition[];
  fieldMappings: ProviderIntegrationFieldMapping[];
  sync: ProviderIntegrationSyncSchedule;
  validationRules: ProviderIntegrationValidationRule[];
  activation: ProviderIntegrationActivationPolicy;
  state: ProviderIntegrationActivationState;
  createdBy: string;
  createdAt: string;
  approvedBy?: string | null;
  approvedAt?: string | null;
  changeReason?: string | null;
}

export interface ProviderIntegrationConnection {
  connectionId: string;
  providerId: string;
  manifestId: string;
  connectionName: string;
  environment: string;
  state: ProviderIntegrationActivationState;
  credentialSecretRef: string;
  enabledCapabilities: ProviderIntegrationCapabilityKind[];
  ownerUserId: string;
  createdAt: string;
  updatedAt: string;
  approvalEvidenceId?: string | null;
}

export interface ProviderIntegrationQuarantinedRecord {
  quarantineRecordId: string;
  syncRunId: string;
  connectionId: string;
  capability: ProviderIntegrationCapabilityKind;
  rawRecord: unknown;
  mappedRecord?: unknown | null;
  validationErrors: ProviderIntegrationValidationIssue[];
  status: ProviderIntegrationProcessingStatus;
  createdAt: string;
}

export interface ProviderIntegrationQuarantineIssueGroup {
  issueCode: string;
  severity: ProviderIntegrationIssueSeverity;
  targetField?: string | null;
  message: string;
  suggestedFix?: string | null;
  recordCount: number;
}

export interface ProviderIntegrationQuarantineDecision {
  decisionId: string;
  syncRunId: string;
  quarantineRecordId: string;
  connectionId: string;
  action: ProviderIntegrationQuarantineResolutionAction;
  reviewedBy: string;
  reviewedAt: string;
  note?: string | null;
}

export interface ProviderIntegrationQuarantineReview {
  connectionId: string;
  syncRunIds: string[];
  records: ProviderIntegrationQuarantinedRecord[];
  issueGroups: ProviderIntegrationQuarantineIssueGroup[];
  decisions: ProviderIntegrationQuarantineDecision[];
  totalQuarantinedRecords: number;
  criticalIssueCount: number;
  warningIssueCount: number;
  pendingReviewRecordCount: number;
  decisionedRecordCount: number;
  replayRequestedRecordCount: number;
  ignoredRecordCount: number;
  cashPositionCandidateCount: number;
}

export interface ProviderIntegrationQuarantineResolutionRequest {
  connectionId: string;
  syncRunId: string;
  quarantineRecordId: string;
  action: ProviderIntegrationQuarantineResolutionAction;
  reviewedBy: string;
  reviewedAt: string;
  note?: string | null;
}

export interface ProviderIntegrationQuarantineResolutionResult {
  resolved: boolean;
  record: ProviderIntegrationQuarantinedRecord;
  decision: ProviderIntegrationQuarantineDecision;
  message?: string | null;
}

export interface ProviderIntegrationQuarantineReplayRequest {
  replaySyncRunId: string;
  sourceSyncRunId: string;
  manifestId: string;
  connectionId: string;
  capability: ProviderIntegrationCapabilityKind;
  quarantineRecordIds: string[];
  requestedBy: string;
  requestedAt: string;
}

export interface ProviderIntegrationQuarantineReplayResult {
  replaySyncRunId: string;
  rawPayloadId: string;
  capability: ProviderIntegrationCapabilityKind;
  recordsReplayed: number;
  recordsAccepted: number;
  recordsRequarantined: number;
  status: ProviderIntegrationProcessingStatus;
  issues: ProviderIntegrationValidationIssue[];
}

export interface ProviderIntegrationStagingRecord {
  stagingRecordId: string;
  syncRunId: string;
  connectionId: string;
  capability: ProviderIntegrationCapabilityKind;
  rawPayloadId: string;
  sourceRecordId?: string | null;
  dedupeKey: string;
  mappedRecord: unknown;
  validationWarnings: ProviderIntegrationValidationIssue[];
  status: ProviderIntegrationProcessingStatus;
  createdAt: string;
}

export interface ProviderIntegrationStagingCapabilitySummary {
  capability: ProviderIntegrationCapabilityKind;
  recordCount: number;
  warningCount: number;
}

export interface ProviderIntegrationStagingReview {
  connectionId: string;
  syncRunIds: string[];
  records: ProviderIntegrationStagingRecord[];
  capabilitySummaries: ProviderIntegrationStagingCapabilitySummary[];
  warningGroups: ProviderIntegrationQuarantineIssueGroup[];
  totalStagedRecords: number;
  readyForReconciliationCount: number;
  warningRecordCount: number;
}

export interface ProviderIntegrationIdentityCandidate {
  identifierKind: string;
  identifierValue: string;
  provider?: string | null;
  priority: number;
  status: ProviderIntegrationIdentityResolutionStatus;
  internalSecurityId?: string | null;
  displayName?: string | null;
  route?: string | null;
}

export interface ProviderIntegrationStagingIdentityResolutionRow {
  stagingRecordId: string;
  syncRunId: string;
  capability: ProviderIntegrationCapabilityKind;
  providerAccountId?: string | null;
  accountStatus: ProviderIntegrationIdentityResolutionStatus;
  internalAccountId?: string | null;
  accountResolutionNote?: string | null;
  securityStatus: ProviderIntegrationIdentityResolutionStatus;
  internalSecurityId?: string | null;
  securityDisplayName?: string | null;
  securityRoute?: string | null;
  securityCandidates: ProviderIntegrationIdentityCandidate[];
  issues: ProviderIntegrationValidationIssue[];
}

export interface ProviderIntegrationStagingIdentityResolutionPreview {
  connectionId: string;
  syncRunIds: string[];
  rows: ProviderIntegrationStagingIdentityResolutionRow[];
  totalRows: number;
  accountReviewRequiredCount: number;
  missingAccountIdentifierCount: number;
  securityResolvedCount: number;
  securityReviewRequiredCount: number;
  missingSecurityIdentifierCount: number;
}

export interface ProviderIntegrationPromotionReadinessRow {
  stagingRecordId: string;
  syncRunId: string;
  capability: ProviderIntegrationCapabilityKind;
  promotionTarget: string;
  status: ProviderIntegrationPromotionReadinessStatus;
  providerAccountId?: string | null;
  internalAccountId?: string | null;
  internalSecurityId?: string | null;
  securityDisplayName?: string | null;
  securityRoute?: string | null;
  issues: ProviderIntegrationValidationIssue[];
}

export interface ProviderIntegrationPromotionReadinessPreview {
  connectionId: string;
  syncRunIds: string[];
  rows: ProviderIntegrationPromotionReadinessRow[];
  totalRows: number;
  readyForReconciliationCount: number;
  reviewRequiredCount: number;
  blockedCount: number;
}

export interface ProviderIntegrationReconciliationHandoffRecord {
  handoffId: string;
  connectionId: string;
  syncRunId: string;
  stagingRecordId: string;
  capability: ProviderIntegrationCapabilityKind;
  promotionTarget: string;
  requestedBy: string;
  requestedAt: string;
  approvalEvidenceId: string;
  note?: string | null;
  providerAccountId?: string | null;
  internalAccountId?: string | null;
  internalSecurityId?: string | null;
  securityRoute?: string | null;
  issues: ProviderIntegrationValidationIssue[];
}

export interface ProviderIntegrationReconciliationHandoffRequest {
  connectionId: string;
  stagingRecordIds: string[];
  requestedBy: string;
  requestedAt: string;
  approvalEvidenceId: string;
  note?: string | null;
  recentRunLimit?: number | null;
}

export interface ProviderIntegrationReconciliationHandoffResult {
  accepted: boolean;
  handoffId?: string | null;
  connectionId: string;
  promotionTarget: string;
  records: ProviderIntegrationReconciliationHandoffRecord[];
  acceptedRecordCount: number;
  rejectedRecordCount: number;
  duplicateRecordCount: number;
  issues: ProviderIntegrationValidationIssue[];
  message?: string | null;
}

export interface ProviderIntegrationReconciliationHandoffHistory {
  connectionId: string;
  records: ProviderIntegrationReconciliationHandoffRecord[];
  totalRecords: number;
  handoffCount: number;
  lastRequestedAt?: string | null;
}

export interface ProviderIntegrationSyncRunEvidence {
  syncRunId: string;
  capability: ProviderIntegrationCapabilityKind;
  endpointKey: string;
  startedAt: string;
  completedAt?: string | null;
  status: ProviderIntegrationProcessingStatus;
  recordsReceived: number;
  recordsAccepted: number;
  recordsQuarantined: number;
  durableStagingRecordCount: number;
  durableQuarantinedRecordCount: number;
  criticalIssueCount: number;
  warningIssueCount: number;
  rawPayloadId?: string | null;
  issues: ProviderIntegrationValidationIssue[];
}

export interface ProviderIntegrationConnectionMonitor {
  connectionId: string;
  manifestId: string;
  providerId: string;
  displayName: string;
  connectionName: string;
  environment: string;
  state: ProviderIntegrationActivationState;
  enabledCapabilities: ProviderIntegrationCapabilityKind[];
  lastSyncRun?: ProviderIntegrationSyncRunEvidence | null;
  recentSyncRuns: ProviderIntegrationSyncRunEvidence[];
  recentRecordsReceived: number;
  recentRecordsAccepted: number;
  recentRecordsQuarantined: number;
  durableStagingRecordCount: number;
  durableQuarantinedRecordCount: number;
  hasCriticalIssues: boolean;
}

export interface ProviderIntegrationSyncRunHistory {
  connectionId: string;
  syncRuns: ProviderIntegrationSyncRunEvidence[];
  totalSyncRuns: number;
  returnedSyncRuns: number;
  latestStartedAt?: string | null;
}

export interface ProviderIntegrationSyncPlanItem {
  capability: ProviderIntegrationCapabilityKind;
  endpointKey?: string | null;
  scheduleMode: string;
  frequency: string;
  timezone: string;
  lastSuccessfulSyncAt?: string | null;
  nextEligibleSyncAt?: string | null;
  isDue: boolean;
  isBlocked: boolean;
  reason: string;
  issues: ProviderIntegrationValidationIssue[];
}

export interface ProviderIntegrationSyncPlan {
  connectionId: string;
  manifestId: string;
  providerId: string;
  connectionName: string;
  connectionState: ProviderIntegrationActivationState;
  evaluatedAt: string;
  items: ProviderIntegrationSyncPlanItem[];
  dueCount: number;
  blockedCount: number;
}

export interface ProviderIntegrationRunDueSyncRequest {
  connectionId: string;
  requestedAt: string;
  requestedBy: string;
  maxPages: number;
  pathParametersByCapability: Record<string, Record<string, string>>;
  queryParametersByCapability: Record<string, Record<string, string>>;
}

export interface ProviderIntegrationDryRunResult {
  syncRunId: string;
  rawPayloadId: string;
  capability: ProviderIntegrationCapabilityKind;
  recordsReceived: number;
  recordsAccepted: number;
  recordsQuarantined: number;
  status: ProviderIntegrationProcessingStatus;
  issues: ProviderIntegrationValidationIssue[];
}

export interface ProviderIntegrationRunDueSyncItemResult {
  capability: ProviderIntegrationCapabilityKind;
  endpointKey?: string | null;
  started: boolean;
  skipped: boolean;
  reason: string;
  syncRunId?: string | null;
  dryRunResult?: ProviderIntegrationDryRunResult | null;
  issues: ProviderIntegrationValidationIssue[];
}

export interface ProviderIntegrationRunDueSyncResult {
  connectionId: string;
  requestedAt: string;
  startedCount: number;
  skippedCount: number;
  items: ProviderIntegrationRunDueSyncItemResult[];
}

export interface ManualCsvProviderIntegrationDryRunRequest {
  syncRunId: string;
  manifestId: string;
  connectionId: string;
  capability: ProviderIntegrationCapabilityKind;
  fileName: string;
  csvContent: string;
  requestedBy: string;
  requestedAt: string;
}

export interface ProviderIntegrationRestDryRunRequest {
  syncRunId: string;
  manifestId: string;
  connectionId: string;
  capability: ProviderIntegrationCapabilityKind;
  endpointKey: string;
  pathParameters: Record<string, string>;
  queryParameters: Record<string, string>;
  requestedBy: string;
  requestedAt: string;
  maxPages: number;
}

export interface ProviderIntegrationOpenApiImportRequest {
  manifestId: string;
  providerId: string;
  displayName: string;
  environment: string;
  authType: ProviderIntegrationAuthType;
  tokenUrl?: string | null;
  scopes: string[];
  capabilities: ProviderIntegrationCapabilityKind[];
  openApiDocumentJson: string;
  importedBy: string;
  importedAt: string;
  changeReason?: string | null;
}

export interface ProviderIntegrationOpenApiImportResult {
  imported: boolean;
  manifest: ProviderIntegrationManifest;
  readiness: ProviderIntegrationActivationReadiness;
  issues: ProviderIntegrationValidationIssue[];
  message?: string | null;
}

export interface ProviderIntegrationSchemaDriftIssue {
  code: string;
  severity: ProviderIntegrationIssueSeverity;
  capability: ProviderIntegrationCapabilityKind;
  endpointKey: string;
  jsonPath: string;
  message: string;
  suggestedFix: string;
}

export interface ProviderIntegrationSchemaDriftCheckRequest {
  manifestId: string;
  connectionId: string;
  capability: ProviderIntegrationCapabilityKind;
  endpointKey: string;
  syncRunId: string;
  rawPayloadId: string;
  checkedBy: string;
  checkedAt: string;
}

export interface ProviderIntegrationSchemaDriftCheckResult {
  manifestId: string;
  connectionId: string;
  capability: ProviderIntegrationCapabilityKind;
  endpointKey: string;
  syncRunId: string;
  rawPayloadId: string;
  driftDetected: boolean;
  shouldPauseCapability: boolean;
  recordsInspected: number;
  issues: ProviderIntegrationSchemaDriftIssue[];
}

export interface ProviderIntegrationSetupSaveRequest {
  manifest: ProviderIntegrationManifest;
  connection: ProviderIntegrationConnection;
  savedBy: string;
  savedAt: string;
  changeReason?: string | null;
}

export interface ProviderIntegrationSetupSaveResult {
  saved: boolean;
  manifestId: string;
  connectionId: string;
  manifestState: ProviderIntegrationActivationState;
  connectionState: ProviderIntegrationActivationState;
  readiness: ProviderIntegrationActivationReadiness;
  approvalEvidenceId?: string | null;
  message?: string | null;
}

export interface ProviderIntegrationActivationRequest {
  manifestId: string;
  connectionId: string;
  approvedBy: string;
  approvedAt: string;
  approvalEvidenceId: string;
  changeReason?: string | null;
}

export interface ProviderIntegrationActivationResult {
  activated: boolean;
  manifestId: string;
  connectionId: string;
  manifestState: ProviderIntegrationActivationState;
  connectionState: ProviderIntegrationActivationState;
  readiness: ProviderIntegrationActivationReadiness;
  message?: string | null;
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
  breakCountTrend?: number | null;
  autoMatchRate?: number | null;
  t0ClosureRate?: number | null;
  breakCountAlertThreshold?: number | null;
  autoMatchRateAlertThreshold?: number | null;
  t0ClosureRateAlertThreshold?: number | null;
  breakCountAlertTriggered?: boolean;
  autoMatchRateAlertTriggered?: boolean;
  t0ClosureRateAlertTriggered?: boolean;
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

export interface InstrumentPassportEconomicDefinition extends Record<string, unknown> {
  assetClass?: string | null;
}

export interface InstrumentPassportIdentifierSummary {
  summary: string;
}

export interface InstrumentPassportUsage {
  summary: string;
}

export interface InstrumentPassportTrustPosture {
  tone: string;
  summary: string;
}
export interface InstrumentPassportProviderConfidence {
  provider: string;
  providerSource: string;
  mappingKind: string;
  symbol: string;
  normalizedSymbol: string;
  isPrimary: boolean;
  isActive: boolean;
  freshnessAsOf: string | null;
  freshnessMinutes: number | null;
  confidenceScore: number;
  confidenceReason: string;
  identifierConflictIds: string[];
  identifierConflictSummaries: string[];
  overrideHistory: Record<string, unknown>[];
}

export interface InstrumentPassportReferenceDataWorkbenchSection {
  sectionId: string;
  title: string;
  status: string;
  summary: string;
  evidenceCount: number;
  blockingIssueCount: number;
}

export interface InstrumentPassportOperationsHandoff {
  handoffId: string;
  target: string;
  title: string;
  detail: string;
  status: string;
  isEnabled: boolean;
}

export interface InstrumentPassportReferenceDataWorkbench {
  status: string;
  summary: string;
  sections: InstrumentPassportReferenceDataWorkbenchSection[];
  operationsHandoffs: InstrumentPassportOperationsHandoff[];
}

export interface InstrumentPassportPricing {
  status: string;
  summary: string;
  tradingParameters: TradingParameters | null;
  lotSize: number | null;
  tickSize: number | null;
  contractMultiplier: number | null;
  tradingHoursUtc: string | null;
  circuitBreakerThresholdPct: number | null;
}

export interface InstrumentPassport {
  securityId: string;
  identity: SecurityIdentityDrillIn;
  economicDefinition: InstrumentPassportEconomicDefinition;
  identifierSummary: InstrumentPassportIdentifierSummary;
  providerMappings: Record<string, unknown>[];
  lifecycleEvents: Record<string, unknown>[];
  corporateActions: CorporateAction[];
  pricing: InstrumentPassportPricing;
  usage: InstrumentPassportUsage;
  trustPosture: InstrumentPassportTrustPosture;
  retrievedAtUtc: string;
  providerConfidence: InstrumentPassportProviderConfidence[];
  referenceDataWorkbench?: InstrumentPassportReferenceDataWorkbench | null;
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

export type AdminMaintenanceTaskType =
  | "HealthCheck"
  | "Cleanup"
  | "Defragmentation"
  | "TierMigration"
  | "Compression"
  | "Repair"
  | "FullMaintenance"
  | "IntegrityCheck"
  | "Archival"
  | "RetentionEnforcement";

export interface ArchiveMaintenanceSchedule {
  scheduleId: string;
  name: string;
  description?: string | null;
  enabled: boolean;
  cronExpression: string;
  timeZoneId?: string | null;
  taskType: AdminMaintenanceTaskType | string;
  priority?: string | null;
  targetPaths?: string[] | null;
  lastExecutedAt?: string | null;
  nextExecutionAt?: string | null;
  lastExecutionId?: string | null;
  lastExecutionStatus?: string | null;
  executionCount?: number;
  successfulExecutions?: number;
  failedExecutions?: number;
  tags?: string[] | null;
}

export interface AdminMaintenanceScheduleResponse {
  schedules: ArchiveMaintenanceSchedule[];
  summary?: string | Record<string, unknown> | null;
  timestamp?: string;
  total?: number;
}

export interface MaintenanceExecution {
  executionId: string;
  scheduleId?: string | null;
  scheduleName?: string | null;
  taskType: AdminMaintenanceTaskType | string;
  status: string;
  manualTrigger?: boolean;
  startedAt: string;
  completedAt?: string | null;
  filesProcessed?: number;
  issuesFound?: number;
  issuesResolved?: number;
  bytesProcessed?: number;
  bytesSaved?: number;
  errorMessage?: string | null;
  logMessages?: string[] | null;
  result?: Record<string, unknown> | null;
}

export interface AdminMaintenanceHistoryResponse {
  executions: MaintenanceExecution[];
  total: number;
  statistics?: Record<string, unknown> | null;
  timestamp?: string;
}

export interface AdminMaintenanceRunRequest {
  taskType?: AdminMaintenanceTaskType | string;
  targetPaths?: string[] | null;
}

export interface AdminTierInfo {
  fileCount: number;
  totalBytes: number;
  oldestFile?: string | null;
  newestFile?: string | null;
}

export interface AdminStorageTiersResponse {
  tiers: Record<string, AdminTierInfo>;
  generatedAt?: string;
}

export interface AdminStorageUsageBreakdownEntry {
  fileCount: number;
  bytes: number;
}

export interface AdminStorageUsageResponse {
  rootPath: string;
  totalBytes: number;
  fileCount: number;
  breakdown: Record<string, AdminStorageUsageBreakdownEntry>;
  timestamp?: string;
}

export interface AdminRetentionResponse {
  retentionDays: number;
  maxStorageSizeGb: number;
  timestamp?: string;
}

export interface AdminCleanupCandidate {
  path: string;
  sizeBytes: number;
  lastModified: string;
}

export interface AdminCleanupPreviewResponse {
  candidateCount: number;
  reclaimableBytes: number;
  candidates: AdminCleanupCandidate[];
  timestamp?: string;
}

export interface AdminCleanupExecuteResponse {
  executed: boolean;
  message?: string | null;
  timestamp?: string;
}

export interface AdminStoragePermissionsResponse {
  rootPath: string;
  readable: boolean;
  writable: boolean;
  timestamp?: string;
}

export interface AdminQuickCheckResponse {
  configLoaded?: boolean;
  dataRoot?: string;
  dataRootExists?: boolean;
  symbolCount?: number;
  dataSource?: string;
  timestamp?: string;
}

export interface AdminShowConfigResponse {
  dataSource?: string;
  symbolCount?: number;
  symbols?: string[];
  dataRoot?: string;
  compress?: boolean;
  storage?: Record<string, unknown> | null;
  timestamp?: string;
}

export interface AdminErrorCodesResponse {
  errorCodes: Array<{ code: number; name: string }>;
  timestamp?: string;
}

export interface AdminSelfTestResponse {
  passed?: boolean;
  checks: Array<{ check: string; passed: boolean }>;
  timestamp?: string;
}

export interface MaintenanceSchedulesResponse extends AdminMaintenanceScheduleResponse {
  total?: number;
}

export interface MaintenanceScheduleHistoryResponse {
  scheduleId: string;
  executions: MaintenanceExecution[];
  total: number;
  summary?: Record<string, unknown> | null;
}

export interface DataPackageListItem {
  path: string;
  fileName: string;
  sizeBytes: number;
  createdAt: string;
  modifiedAt: string;
}

export interface DataPackageListResponse {
  packages: DataPackageListItem[];
}

export interface DataPackageCreateRequest {
  name?: string | null;
  description?: string | null;
  outputDirectory?: string | null;
  symbols?: string[] | null;
  eventTypes?: string[] | null;
  startDate?: string | null;
  endDate?: string | null;
  format?: string | null;
  compressionLevel?: string | null;
  includeQualityReport?: boolean | null;
  includeDataDictionary?: boolean | null;
  includeLoaderScripts?: boolean | null;
  verifyChecksums?: boolean | null;
  tags?: string[] | null;
  customMetadata?: Record<string, string> | null;
}

export interface DataPackageResult {
  success: boolean;
  jobId?: string;
  packageId?: string | null;
  packagePath?: string | null;
  packageFileName?: string | null;
  packageSizeBytes?: number;
  uncompressedSizeBytes?: number;
  filesIncluded?: number;
  totalEvents?: number;
  symbols?: string[];
  eventTypes?: string[];
  packageChecksum?: string | null;
  warnings?: string[];
  error?: string | null;
  completedAt?: string | null;
}

export interface DataPackageValidateRequest {
  packagePath: string;
}

export interface DataPackageValidationResponse {
  isValid: boolean;
  packagePath?: string;
  manifest?: Record<string, unknown> | null;
  issues?: string[] | null;
  missingFiles?: string[] | null;
  error?: string | null;
}

export interface DataPackageContentsResponse {
  packageId: string;
  name: string;
  description?: string | null;
  createdAt?: string;
  totalFiles: number;
  totalEvents: number;
  packageSizeBytes: number;
  uncompressedSizeBytes: number;
  symbols: string[];
  eventTypes: string[];
  files?: Array<{ relativePath?: string; path?: string; sizeBytes?: number; recordCount?: number }>;
  quality?: Record<string, unknown> | null;
}
