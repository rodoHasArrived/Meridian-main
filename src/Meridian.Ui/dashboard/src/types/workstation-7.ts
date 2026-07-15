import type {
  MetricSnapshot,
  ProviderCredentialSource,
  ProviderCredentialState,
  SecurityMasterFactorPoint,
  SecurityMasterScheduleEvent,
  SecurityMasterScheduleProvenance,
  SecurityMasterScheduleSummary,
} from "../types";

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

/**
 * Minimal projection of the economic-definition drill-in carried by the trust snapshot. The full DTO
 * has more fields; the dashboard surfaces only the passport version (the governed-write concurrency
 * token) and the asset class / currency the editor header needs.
 */
export interface SecurityMasterTrustSnapshotEconomicDefinition {
  version: number;
  assetClass: string;
  currency: string;
}

export interface SecurityMasterTrustSnapshot {
  securityId: string;
  retrievedAtUtc: string;
  economicDefinition?: SecurityMasterTrustSnapshotEconomicDefinition | null;
  scheduleSummary?: SecurityMasterScheduleSummary | null;
  lotModel?: SecurityMasterLotModel | null;
  scheduleBook?: SecurityMasterScheduleBook | null;
  openLotReadModel?: SecurityMasterOpenLotReadModel | null;
  corporateActionDescriptors?: CorporateActionDescriptor[] | null;
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

export interface BackfillProviderAttemptProgress {
  symbol: string;
  provider: string;
  rangeStart: string | null;
  rangeEnd: string | null;
  providerAttempt: number;
  retryRound: number;
  operation: string | null;
  status: string | null;
  barsDownloaded: number;
  startedAt: string;
  observedAt: string;
  error: string | null;
}

export interface BackfillProviderSymbolProgress {
  symbol: string;
  rangeStart: string | null;
  rangeEnd: string | null;
  totalDays: number;
  completedDays: number;
  percentComplete: number;
  isCompleted: boolean;
  isFailed: boolean;
  isSkipped: boolean;
  currentProvider: string | null;
  currentStatus: string | null;
  providerAttempt: number;
  retryRound: number;
  operation: string | null;
  attemptStartedAt: string | null;
  lastUpdatedAt: string | null;
  error: string | null;
}

export interface BackfillProviderProgressSnapshot {
  symbols: Record<string, BackfillProviderSymbolProgress>;
  recentProviderAttempts: BackfillProviderAttemptProgress[];
  overallPercentComplete: number;
  totalSymbols: number;
  completedSymbols: number;
  failedSymbols: number;
  droppedProviderNotifications: number;
  timestamp: string;
}

export interface BackfillProgressResponse {
  lastRun?: BackfillTriggerResult | null;
  isActive?: boolean;
  providerProgress?: BackfillProviderProgressSnapshot;
  timestamp?: string;
  // Compatibility fields returned by older hosts during rolling upgrades.
  active?: boolean;
  provider?: string | null;
  symbols?: BackfillProgressEntry[];
  message?: string | null;
}

export type BackfillRemediationSlaTier = "Standard" | "SameBusinessDay";

export type BackfillRemediationSlaStatus = "Open" | "DueSoon" | "Overdue" | "Failed" | "Completed";

export interface BackfillRemediationSla {
  tier: BackfillRemediationSlaTier;
  status: BackfillRemediationSlaStatus;
  dueAtUtc: string;
  requiresOwnerAssignment: boolean;
  downstreamWorkflow: string;
  reasonCode: string;
  provider: string;
  triggerSource: string | null;
  isCompatibilityDerived: boolean;
}

export interface BackfillExecutionHistoryRow {
  executionId: string;
  scheduleName: string;
  trigger: string;
  scheduleId: string;
  status: string;
  startedAt: string;
  completedAt: string | null;
  symbolsProcessed: number;
  barsDownloaded: number;
  errorMessage: string | null;
  fromDate: string;
  toDate: string;
  symbols: string[];
  autoRemediationTriggerReason: string | null;
  autoRemediationAttemptCount: number;
  autoRemediationLastOutcome: string | null;
  autoRemediationIdempotencyKey: string | null;
  autoRemediationSla: BackfillRemediationSla | null;
}

export interface BackfillExecutionHistoryResponse {
  executions: BackfillExecutionHistoryRow[];
  total: number;
  autoRemediation: {
    total: number;
    withReason: number;
    lastOutcome: string | null;
    defaultProvider: string;
  };
  timestamp: string;
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

// --- Quality monitoring types ---

export interface QualityGapEntry {
  symbol: string;
  eventType: string;
  gapStart: string;
  gapEnd: string;
  duration: string;
  missedSequenceStart: number;
  missedSequenceEnd: number;
  estimatedMissedEvents: number;
  severity: number;
  possibleCause: string | null;
}

export interface QualityAnomalyEntry {
  id: string;
  timestamp: string;
  symbol: string;
  type: number;
  severity: number;
  description: string;
  expectedValue: number;
  actualValue: number;
  deviationPercent: number;
  zScore: number;
  provider: string | null;
  isAcknowledged: boolean;
  detectedAt: string;
}

export interface QualityComponentResponse {
  kind: "StreamingFreshness" | "StoredCompleteness" | "AdapterGapIntegrity";
  label: string;
  weight: number;
  score: number | null;
  availability: "Measured" | "Partial" | "Stale" | "Unavailable";
  observedAt: string | null;
  issueCount: number;
  detail: string;
}

export interface QualityCompositeGapResponse {
  gapId: string;
  symbol: string;
  provider: string | null;
  eventType: string;
  from: string;
  to: string;
  estimatedMissingEvents: number;
  severity: string;
  status: "Open" | "Resolved";
  canBackfill: boolean;
  disabledReason: string | null;
}

export interface QualityProviderFreshnessResponse {
  provider: string;
  lastEventAt: string | null;
  ageMilliseconds: number | null;
  status: "Fresh" | "Stale" | "Unavailable";
  completenessScore: number | null;
  gapCount: number;
}

export interface QualityCompositeSymbolResponse {
  symbol: string;
  compositeScore: number | null;
  status: "Green" | "Amber" | "Red" | "Unavailable";
  isPartial: boolean;
  coverageWeight: number;
  expectedEvents: number | null;
  observedEvents: number | null;
  anomalyCount: number;
  components: QualityComponentResponse[];
  openGaps: QualityCompositeGapResponse[];
  providerFreshness: QualityProviderFreshnessResponse[];
  issues: string[];
}

export interface QualityCompositeDashboardResponse {
  version: string;
  observedAt: string;
  compositeScore: number | null;
  status: "Green" | "Amber" | "Red" | "Unavailable";
  isPartial: boolean;
  coverageWeight: number;
  components: QualityComponentResponse[];
  symbols: QualityCompositeSymbolResponse[];
  openGaps: QualityCompositeGapResponse[];
  anomalyCount: number;
}

export interface QualityGapRemediationRequest {
  gapId: string;
  dashboardVersion: string;
}

export interface QualityGapRemediationResponse {
  gapId: string;
  symbol: string;
  status: "None" | "Completed" | "FailedTransient" | "FailedPermanent" | "Skipped";
  provider: string;
  from: string;
  to: string;
  idempotencyKey: string;
  message: string;
}

export interface QualityDashboardResponse {
  timestamp: string;
  composite: QualityCompositeDashboardResponse | null;
  recentGaps: QualityGapEntry[];
  recentAnomalies: QualityAnomalyEntry[];
}

// --- Provider × instrument-type capability matrix ---

export interface ProviderCapabilitySurface {
  streaming: boolean;
  historical: boolean;
  symbolSearch: boolean;
  corporateActions: boolean;
  optionsChain: boolean;
  brokerage: boolean;
}

export interface ProviderInstrumentCapabilityCell {
  instrumentType: string;
  supported: boolean;
  stream: boolean;
  backfill: boolean;
  corporateActions: boolean;
  symbolSearch: boolean;
  optionsChain: boolean;
}

export interface ProviderInstrumentCapabilityRow {
  providerId: string;
  declaredInstrumentTypes: string[];
  surfaces: ProviderCapabilitySurface;
  cells: ProviderInstrumentCapabilityCell[];
}

export interface ProviderDiscoveryFailure {
  stage: string;
  subject: string;
  moduleId: string | null;
  errorType: string;
  errorMessage: string;
}

export interface ProviderCapabilityMatrixResponse {
  generatedAt: string;
  instrumentTypes: string[];
  providers: ProviderInstrumentCapabilityRow[];
  discoveryFailures: ProviderDiscoveryFailure[];
}

// --- Corporate action inbox ---

export interface CorporateActionProposalEntry {
  securityId: string;
  ticker: string;
  actionType: string;
  exDate: string;
  recordDate: string | null;
  payableDate: string | null;
  amount: number | null;
  currency: string | null;
  splitFromFactor: number | null;
  splitToFactor: number | null;
  winningSource: string;
  agreeingSources: string[];
  dissentingSources: string[];
  autoApplied: boolean;
}

export interface CorporateActionInboxResponse {
  lastIngestAt: string | null;
  stagedCount: number;
  appliedLastRun: number;
  duplicatesSkippedLastRun: number;
  staged: CorporateActionProposalEntry[];
  errors: string[];
}

// --- Security master quality report (RC001 coverage gaps) ---

export interface SecurityMasterQualityViolation {
  ruleId: string;
  ruleName: string;
  category: string;
  securityId: string;
  fieldPath: string | null;
  message: string;
  severity: string;
  detectedAt: string;
}

export interface SecurityMasterQualityReport {
  runAt: string;
  securitiesScanned: number;
  violationCount: number;
  violations: SecurityMasterQualityViolation[];
}

export interface CorporateActionInboxApplyRequest {
  securityId: string;
  actionType: string;
  exDate: string;
}

// --- Security master coverage drafts ---

export interface SecurityMasterDraftIdentifier {
  kind: string;
  value: string;
  isPrimary: boolean;
  validFrom: string;
  validTo: string | null;
  provider: string | null;
}

export interface SecurityMasterDraftProposal {
  symbol: string;
  resolved: boolean;
  proposedSecurityId: string;
  assetClass: string;
  displayName: string | null;
  exchange: string | null;
  currency: string | null;
  identifiers: SecurityMasterDraftIdentifier[];
  sourceSystem: string;
  provenance: string;
  notes: string | null;
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

/** Canonical corporate-action lifecycle vocabulary; Ex/Paid are derived from ExDate/PayDate at read time. */
export type CorporateActionLifecycleState = "Announced" | "Confirmed" | "Ex" | "Paid" | "Cancelled";

/**
 * One event in an effective corporate action's supersede chain (original announcement first,
 * chain tip last). Mirrors CorporateActionTimelineEntryDto.
 */
export interface CorporateActionTimelineEntry {
  corpActId: string;
  lifecycleState: string;
  exDate: string;
  payDate: string | null;
  isAmendment: boolean;
}

/**
 * Canonical-taxonomy projection of one effective corporate action: catalog display identity
 * (displayName + ISO 15022 caevCode), lifecycle state resolved at the snapshot's as-of time,
 * and the amendment timeline. Mirrors CorporateActionDescriptorDto; corpActId joins back to
 * the raw CorporateAction row.
 */
export interface CorporateActionDescriptor {
  corpActId: string;
  canonicalName: string;
  caevCode: string | null;
  displayName: string;
  lifecycleState: string;
  isCancelled: boolean;
  timeline: CorporateActionTimelineEntry[];
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
  owner?: string | null;
  blockerReason?: string | null;
  impactedOutputs?: string[];
  linkedCases?: string[];
  route?: string | null;
}

export interface InstrumentPassportOperationsWorkbenchItem {
  itemId: string;
  label: string;
  value: string;
  status: string;
  detail: string;
  evidenceCount: number;
  blockingIssueCount: number;
  route?: string | null;
}

export interface InstrumentPassportOperationsWorkbenchPanel {
  panelId: string;
  title: string;
  status: string;
  summary: string;
  items: InstrumentPassportOperationsWorkbenchItem[];
}
