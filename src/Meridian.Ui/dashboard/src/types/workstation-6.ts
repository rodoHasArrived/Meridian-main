import type {
  AccountingActionAuditEvent,
  AccountingBasisKind,
  AccountingConfigurationValidationIssue,
  AccountingTemplateLineSide,
  ChartOfAccountsNode,
  EvidenceStatus,
  JournalEntryLifecycleAction,
  JournalEntryTemplate,
  LedgerBook,
  LedgerDimensionSet,
  ManualJournalEntryDraft,
  ManualJournalEntryEvidenceAttachment,
  ManualJournalEntryLine,
  ManualJournalEntryStatus,
  ManualJournalEntryType,
  OperationsActionOrigin,
  OperationsApprovalState,
  OperationsCloseReadinessBlocker,
  OperationsEvidenceLink,
  OperationsEvidencePackageSummary,
  OperationsNextAction,
  OperationsWorkflowStatus,
  PaymentIntentCashDirection,
  PaymentIntentWorkflowStatus,
  PortfolioSummary,
  PostingRule,
  PrivateCapitalFundEventLedgerReadiness,
  PrivateCapitalPaymentIntentEvidenceStatus,
  WorkstationSecurityReference,
} from "../types";

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
  ledgerBookId?: string | null;
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
