// Accounting workspace, manual journal, valuation, production readiness, accounting-system export,
// ledger close, and accounting configuration endpoints. Re-exported through @/lib/api.
import type { AccountingMigrationRunArtifactList, AccountingMigrationRunKind, AccountingMigrationRunWorkerPlanList, AccountingProductionCertificationProfile, AccountingProductionCertificationProfileUpsertRequest, AccountingProductionReadiness, AccountingProductionReadinessRequest, AccountingTenantAdministrationProfile, AccountingTenantAdministrationProfileUpsertRequest, AccountingSystemExportPackageRequest, AccountingSystemImportDetail, AccountingSystemImportRequest, AccountingSystemMappingProfileUpsertRequest, AccountingSystemProvider, AccountingSystemReconciliationSummary, AccountingReportPackageBundle, AccountingReportPackageRequest, CertifyAccountingReportPackageRequest, CertifyAccountingSystemExportPackageRequest, ReportExportArtifactManifest, ClosePeriodPlan, ClosePeriodLockResult, CreateLateAdjustmentRequest, LockClosePeriodRequest, ReviewCloseEvidenceRequest, ReviewLateAdjustmentRequest, UpsertClosePeriodPlanConfigurationRequest, ExternalGlExportPackage, ExternalGlExportPackageManifest, ExternalGlMappingProfile, DailyValuationBatchLifecycleRequest, DailyValuationBatchLifecycleResult, DailyValuationScheduleWorkItem, DailyValuationScheduledBatchResult, FinancialRecordExplorerDto, FinancialRecordExplorerId, FinancialRecordExplorerSavedViewDto, FinancialRecordExplorerSavedViewSaveRequestDto, FinancialRecordExplorerSelectedRecordDto, AccountingWorkspaceResponse, SignOffCloseTaskRequest, AttachManualJournalEntryEvidenceRequest, JournalEntryLifecycleActionRequest, JournalEntryLifecycleActionResult, ManualJournalEntryDraft, ManualJournalEntryWorkbench, CapitalAccountWorkbench, PrivateCapitalActivityProjection, PrivateCapitalCapitalAccountSubledger, PrivateCapitalFundEventCommandCenter, PrivateCapitalFundEventLedgerRecord, PrivateCapitalReportOutput, SaveManualJournalEntryDraftRequest, SubmitManualJournalEntryApprovalRequest, ValidateManualJournalEntryDraftRequest } from "@/types";
import { ACCOUNTING_SYSTEM_API_ENDPOINTS, WORKSTATION_API_ENDPOINTS, workstationFinancialRecordExplorerEndpoint, type ExplorerFilterSelection, workstationFinancialRecordExplorerRecordEndpoint, workstationFinancialRecordExplorerSavedViewsEndpoint } from "@/lib/workstation-endpoints";
import { getJson, postJson, type ApiRequestOptions } from "@/lib/api/http-client";

export function getAccountingWorkspace(options: ApiRequestOptions = {}) {
  return getJson<AccountingWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.accounting, options);
}

export function getFinancialRecordExplorer(
  explorerId: FinancialRecordExplorerId,
  options: ApiRequestOptions = {},
  filters: readonly ExplorerFilterSelection[] = []
) {
  return getJson<FinancialRecordExplorerDto>(workstationFinancialRecordExplorerEndpoint(explorerId, filters), options);
}

export function getFinancialRecordExplorerRecord(
  explorerId: FinancialRecordExplorerId,
  recordId: string,
  options: ApiRequestOptions = {}
) {
  return getJson<FinancialRecordExplorerSelectedRecordDto>(
    workstationFinancialRecordExplorerRecordEndpoint(explorerId, recordId),
    options
  );
}

export function saveFinancialRecordExplorerView(
  explorerId: FinancialRecordExplorerId,
  request: FinancialRecordExplorerSavedViewSaveRequestDto,
  options: ApiRequestOptions = {}
) {
  return postJson<FinancialRecordExplorerSavedViewDto>(
    workstationFinancialRecordExplorerSavedViewsEndpoint(explorerId),
    request,
    options
  );
}

export function getAccountingConfiguration(options: ApiRequestOptions = {}) {
  return getJson<import("@/types").AccountingConfigurationWorkspace>(WORKSTATION_API_ENDPOINTS.accountingConfiguration, options);
}

export function createLedgerBook(
  request: import("@/types").CreateLedgerBookRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").LedgerBook>(WORKSTATION_API_ENDPOINTS.ledgerBooks, request, options);
}

export interface ManualJournalEntryWorkbenchQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  /** Client selection only; authoritative drafts still come from the scoped workbench. */
  journalEntryId?: string | null;
  periodId?: string | null;
}

export function getManualJournalEntryWorkbench(
  query: ManualJournalEntryWorkbenchQuery = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.fundProfileId) {
    params.set("fundProfileId", query.fundProfileId);
  }

  if (query.ledgerBookId) {
    params.set("ledgerBookId", query.ledgerBookId);
  }

  const suffix = params.toString();
  return getJson<ManualJournalEntryWorkbench>(
    `${WORKSTATION_API_ENDPOINTS.manualJournalEntryWorkbench}${suffix ? `?${suffix}` : ""}`,
    query.journalEntryId ? { ...options, allowDevelopmentFallback: false } : options
  );
}

export interface PrivateCapitalActivityQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  fundEventId?: string | null;
  capitalAccountId?: string | null;
  investorId?: string | null;
}

export function getPrivateCapitalActivity(
  query: PrivateCapitalActivityQuery = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  const suffix = params.toString();
  return getJson<PrivateCapitalActivityProjection>(
    `${WORKSTATION_API_ENDPOINTS.privateCapitalActivity}${suffix ? `?${suffix}` : ""}`,
    options
  );
}

export interface PrivateCapitalFundEventRecordQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  fundEventId: string;
}

export function getPrivateCapitalFundEventRecord(
  query: PrivateCapitalFundEventRecordQuery,
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  return getJson<PrivateCapitalFundEventLedgerRecord>(
    `${WORKSTATION_API_ENDPOINTS.privateCapitalFundEventRecord}?${params.toString()}`,
    options
  );
}

export interface PrivateCapitalFundEventCommandCenterQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  fundEventId: string;
}

export function getPrivateCapitalFundEventCommandCenter(
  query: PrivateCapitalFundEventCommandCenterQuery,
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  return getJson<PrivateCapitalFundEventCommandCenter>(
    `${WORKSTATION_API_ENDPOINTS.privateCapitalFundEventCommandCenter}?${params.toString()}`,
    options
  );
}

export interface PrivateCapitalCapitalAccountSubledgerQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  capitalAccountId: string;
  investorId?: string | null;
  currency?: string | null;
}

export function getPrivateCapitalCapitalAccountSubledger(
  query: PrivateCapitalCapitalAccountSubledgerQuery,
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  return getJson<PrivateCapitalCapitalAccountSubledger>(
    `${WORKSTATION_API_ENDPOINTS.privateCapitalCapitalAccountSubledger}?${params.toString()}`,
    options
  );
}

export interface PrivateCapitalReportOutputQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  reportOutputId?: string | null;
  reportPackId?: string | null;
  fundEventId?: string | null;
  capitalAccountId?: string | null;
  investorId?: string | null;
}

export function getPrivateCapitalReportOutput(
  query: PrivateCapitalReportOutputQuery,
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  return getJson<PrivateCapitalReportOutput>(
    `${WORKSTATION_API_ENDPOINTS.privateCapitalReportOutput}?${params.toString()}`,
    options
  );
}

export interface CapitalAccountWorkbenchQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  fundEventId?: string | null;
  capitalAccountId?: string | null;
  investorId?: string | null;
  currency?: string | null;
}

export function getCapitalAccountWorkbench(
  query: CapitalAccountWorkbenchQuery = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  const suffix = params.toString();
  return getJson<CapitalAccountWorkbench>(
    `${WORKSTATION_API_ENDPOINTS.privateCapitalCapitalAccountWorkbench}${suffix ? `?${suffix}` : ""}`,
    options
  );
}

export function saveManualJournalEntryDraft(
  request: SaveManualJournalEntryDraftRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ManualJournalEntryDraft>(WORKSTATION_API_ENDPOINTS.manualJournalEntryDrafts, request, options);
}

export function validateManualJournalEntryDraft(
  request: ValidateManualJournalEntryDraftRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ManualJournalEntryDraft>(WORKSTATION_API_ENDPOINTS.manualJournalEntryValidate, request, options);
}

export function submitManualJournalEntryApproval(
  request: SubmitManualJournalEntryApprovalRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ManualJournalEntryDraft>(WORKSTATION_API_ENDPOINTS.manualJournalEntrySubmitApproval, request, options);
}

export function attachManualJournalEntryEvidence(
  request: AttachManualJournalEntryEvidenceRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ManualJournalEntryDraft>(WORKSTATION_API_ENDPOINTS.manualJournalEntryEvidence, request, options);
}

export function applyManualJournalEntryLifecycleAction(
  request: JournalEntryLifecycleActionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<JournalEntryLifecycleActionResult>(WORKSTATION_API_ENDPOINTS.manualJournalEntryLifecycleAction, request, options);
}

export function listDailyValuationSchedules(options: ApiRequestOptions = {}) {
  return getJson<DailyValuationScheduleWorkItem[]>(
    WORKSTATION_API_ENDPOINTS.dailyValuationSchedules,
    options
  );
}

export function configureDailyValuationSchedule(
  request: DailyValuationScheduleWorkItem,
  options: ApiRequestOptions = {}
) {
  return postJson<DailyValuationScheduleWorkItem>(
    WORKSTATION_API_ENDPOINTS.dailyValuationSchedules,
    request,
    options
  );
}

export function runDueDailyValuationSchedules(options: ApiRequestOptions = {}) {
  return postJson<DailyValuationScheduledBatchResult>(
    WORKSTATION_API_ENDPOINTS.dailyValuationRunDue,
    undefined,
    options
  );
}

export function approveAndPostDailyValuationBatch(
  request: DailyValuationBatchLifecycleRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<DailyValuationBatchLifecycleResult>(
    WORKSTATION_API_ENDPOINTS.dailyValuationBatchLifecycle,
    request,
    options
  );
}

export function getAccountingSystemProviders(options: ApiRequestOptions = {}) {
  return getJson<AccountingSystemProvider[]>(ACCOUNTING_SYSTEM_API_ENDPOINTS.providers, options);
}

export function assessAccountingProductionReadiness(
  request: AccountingProductionReadinessRequest = {},
  options: ApiRequestOptions = {}
) {
  return postJson<AccountingProductionReadiness>(ACCOUNTING_SYSTEM_API_ENDPOINTS.productionReadiness, request, options);
}

export function getAccountingMigrationRunArtifacts(
  query: { fundProfileId?: string | null; ledgerBookId?: string | null } = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.fundProfileId) {
    params.set("fundProfileId", query.fundProfileId);
  }

  if (query.ledgerBookId) {
    params.set("ledgerBookId", query.ledgerBookId);
  }

  const suffix = params.toString();
  const route = suffix ? `${ACCOUNTING_SYSTEM_API_ENDPOINTS.migrationRunArtifacts}?${suffix}` : ACCOUNTING_SYSTEM_API_ENDPOINTS.migrationRunArtifacts;
  return getJson<AccountingMigrationRunArtifactList>(route, options);
}

export function getAccountingMigrationWorkerPlans(
  query: { fundProfileId?: string | null; ledgerBookId?: string | null; kind?: AccountingMigrationRunKind | null } = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.fundProfileId) {
    params.set("fundProfileId", query.fundProfileId);
  }

  if (query.ledgerBookId) {
    params.set("ledgerBookId", query.ledgerBookId);
  }

  if (query.kind) {
    params.set("kind", query.kind);
  }

  const suffix = params.toString();
  const route = suffix ? `${ACCOUNTING_SYSTEM_API_ENDPOINTS.migrationWorkerPlans}?${suffix}` : ACCOUNTING_SYSTEM_API_ENDPOINTS.migrationWorkerPlans;
  return getJson<AccountingMigrationRunWorkerPlanList>(route, options);
}

export function getAccountingTenantAdministrationProfile(
  query: { tenantId?: string | null; companyId?: string | null } = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.tenantId) {
    params.set("tenantId", query.tenantId);
  }

  if (query.companyId) {
    params.set("companyId", query.companyId);
  }

  const suffix = params.toString();
  const route = suffix
    ? `${ACCOUNTING_SYSTEM_API_ENDPOINTS.tenantAdministrationProfile}?${suffix}`
    : ACCOUNTING_SYSTEM_API_ENDPOINTS.tenantAdministrationProfile;
  return getJson<AccountingTenantAdministrationProfile>(route, options);
}

export function upsertAccountingTenantAdministrationProfile(
  request: AccountingTenantAdministrationProfileUpsertRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<AccountingTenantAdministrationProfile>(ACCOUNTING_SYSTEM_API_ENDPOINTS.tenantAdministrationProfile, request, options);
}

export function getAccountingProductionCertificationProfile(
  query: { tenantId?: string | null; companyId?: string | null; fundProfileId?: string | null; ledgerBookId?: string | null } = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.tenantId) {
    params.set("tenantId", query.tenantId);
  }

  if (query.companyId) {
    params.set("companyId", query.companyId);
  }

  if (query.fundProfileId) {
    params.set("fundProfileId", query.fundProfileId);
  }

  if (query.ledgerBookId) {
    params.set("ledgerBookId", query.ledgerBookId);
  }

  const suffix = params.toString();
  const route = suffix
    ? `${ACCOUNTING_SYSTEM_API_ENDPOINTS.productionCertificationProfile}?${suffix}`
    : ACCOUNTING_SYSTEM_API_ENDPOINTS.productionCertificationProfile;
  return getJson<AccountingProductionCertificationProfile>(route, options);
}

export function upsertAccountingProductionCertificationProfile(
  request: AccountingProductionCertificationProfileUpsertRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<AccountingProductionCertificationProfile>(ACCOUNTING_SYSTEM_API_ENDPOINTS.productionCertificationProfile, request, options);
}

export function previewAccountingSystemImport(
  request: AccountingSystemImportRequest = { providerId: "quickbooks-fixture", persistPreview: true },
  options: ApiRequestOptions = {}
) {
  return postJson<AccountingSystemImportDetail>(ACCOUNTING_SYSTEM_API_ENDPOINTS.importPreview, request, options);
}

export function getLatestAccountingSystemImport(options: ApiRequestOptions = {}) {
  return getJson<AccountingSystemImportDetail>(ACCOUNTING_SYSTEM_API_ENDPOINTS.importLatest, options);
}

export function getLatestAccountingSystemReconciliation(options: ApiRequestOptions = {}) {
  return getJson<AccountingSystemReconciliationSummary>(ACCOUNTING_SYSTEM_API_ENDPOINTS.reconciliationLatest, options);
}

export function getAccountingSystemMappingProfiles(
  query: { providerId?: string | null; fundProfileId?: string | null; ledgerBookId?: string | null } = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.providerId) {
    params.set("providerId", query.providerId);
  }

  if (query.fundProfileId) {
    params.set("fundProfileId", query.fundProfileId);
  }

  if (query.ledgerBookId) {
    params.set("ledgerBookId", query.ledgerBookId);
  }

  const suffix = params.toString();
  const route = suffix ? `${ACCOUNTING_SYSTEM_API_ENDPOINTS.mappingProfiles}?${suffix}` : ACCOUNTING_SYSTEM_API_ENDPOINTS.mappingProfiles;
  return getJson<ExternalGlMappingProfile[]>(route, options);
}

export function upsertAccountingSystemMappingProfile(
  request: AccountingSystemMappingProfileUpsertRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ExternalGlMappingProfile>(ACCOUNTING_SYSTEM_API_ENDPOINTS.mappingProfiles, request, options);
}

export function listAccountingSystemExportPackages(
  query: {
    providerId?: string | null;
    fundProfileId?: string | null;
    ledgerBookId?: string | null;
    certificationState?: string | null;
    tenantId?: string | null;
    companyId?: string | null;
  } = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  if (query.providerId) {
    params.set("providerId", query.providerId);
  }

  if (query.fundProfileId) {
    params.set("fundProfileId", query.fundProfileId);
  }

  if (query.ledgerBookId) {
    params.set("ledgerBookId", query.ledgerBookId);
  }

  if (query.certificationState) {
    params.set("certificationState", query.certificationState);
  }

  if (query.tenantId) {
    params.set("tenantId", query.tenantId);
  }

  if (query.companyId) {
    params.set("companyId", query.companyId);
  }

  const suffix = params.toString();
  const route = suffix
    ? `${ACCOUNTING_SYSTEM_API_ENDPOINTS.exportPackages}?${suffix}`
    : ACCOUNTING_SYSTEM_API_ENDPOINTS.exportPackages;
  return getJson<ExternalGlExportPackage[]>(route, options);
}

export function createAccountingSystemExportPackage(
  request: AccountingSystemExportPackageRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ExternalGlExportPackage>(ACCOUNTING_SYSTEM_API_ENDPOINTS.exportPackages, request, options);
}

export function getAccountingSystemExportPackageManifest(
  exportPackageId: string,
  options: ApiRequestOptions = {}
) {
  const route = ACCOUNTING_SYSTEM_API_ENDPOINTS.exportPackageManifest
    .replace("{exportPackageId}", encodeURIComponent(exportPackageId));
  return getJson<ExternalGlExportPackageManifest>(route, options);
}

export function certifyAccountingSystemExportPackage(
  request: CertifyAccountingSystemExportPackageRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ExternalGlExportPackage>(ACCOUNTING_SYSTEM_API_ENDPOINTS.exportPackageCertification, request, options);
}

export function getLedgerCloseManagementPeriodPlan(workflowId: string, options: ApiRequestOptions = {}) {
  const endpoint = WORKSTATION_API_ENDPOINTS.closeManagementPeriodPlan.replace("{workflowId:guid}", workflowId);
  return getJson<ClosePeriodPlan>(endpoint, options);
}

export function configureLedgerCloseManagementPeriodPlan(
  request: UpsertClosePeriodPlanConfigurationRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ClosePeriodPlan>(WORKSTATION_API_ENDPOINTS.closeManagementPeriodPlanConfiguration, request, options);
}

export function createLedgerCloseManagementLateAdjustment(
  request: CreateLateAdjustmentRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ClosePeriodPlan>(WORKSTATION_API_ENDPOINTS.closeManagementLateAdjustments, request, options);
}

export function reviewLedgerCloseManagementLateAdjustment(
  request: ReviewLateAdjustmentRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ClosePeriodPlan>(WORKSTATION_API_ENDPOINTS.closeManagementLateAdjustmentReview, request, options);
}

export function signOffLedgerCloseManagementTask(
  request: SignOffCloseTaskRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ClosePeriodPlan>(WORKSTATION_API_ENDPOINTS.closeManagementTaskSignOffs, request, options);
}

export function reviewLedgerCloseManagementEvidence(
  request: ReviewCloseEvidenceRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ClosePeriodPlan>(WORKSTATION_API_ENDPOINTS.closeManagementEvidenceReview, request, options);
}

export function lockLedgerCloseManagementPeriod(
  request: LockClosePeriodRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ClosePeriodLockResult>(WORKSTATION_API_ENDPOINTS.closeManagementPeriodLock, request, options);
}

export function buildLedgerAccountingReportPackage(
  request: AccountingReportPackageRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<AccountingReportPackageBundle>(WORKSTATION_API_ENDPOINTS.accountingReportPackage, request, options);
}

export function certifyLedgerAccountingReportPackage(
  request: CertifyAccountingReportPackageRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<AccountingReportPackageBundle>(
    WORKSTATION_API_ENDPOINTS.accountingReportPackageCertification,
    request,
    options
  );
}

export interface AccountingReportPackageHistoryQuery {
  fundProfileId?: string | null;
  periodId?: string | null;
  ledgerBookId?: string | null;
}

export function listLedgerAccountingReportPackages(
  query: AccountingReportPackageHistoryQuery = {},
  options: ApiRequestOptions = {}
) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value) {
      params.set(key, value);
    }
  }

  const suffix = params.toString();
  return getJson<AccountingReportPackageBundle[]>(
    `${WORKSTATION_API_ENDPOINTS.accountingReportPackages}${suffix ? `?${suffix}` : ""}`,
    options
  );
}

export function getLedgerAccountingReportPackageExport(
  packageId: string,
  artifactId: string,
  options: ApiRequestOptions = {}
) {
  const route = WORKSTATION_API_ENDPOINTS.accountingReportPackageExport
    .replace("{packageId}", encodeURIComponent(packageId))
    .replace("{artifactId}", encodeURIComponent(artifactId));
  return getJson<ReportExportArtifactManifest>(route, options);
}

export function previewAccountingConfigurationTemplate(
  request: import("@/types").PreviewJournalTemplateRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingJournalTemplatePreview>(WORKSTATION_API_ENDPOINTS.accountingConfigurationPreview, request, options);
}

export function upsertAccountingConfigurationChartNode(
  request: import("@/types").UpsertChartOfAccountsNodeRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingConfigurationWorkspace>(WORKSTATION_API_ENDPOINTS.accountingConfigurationChart, request, options);
}

export function upsertAccountingConfigurationTemplate(
  request: import("@/types").UpsertJournalEntryTemplateRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingConfigurationWorkspace>(WORKSTATION_API_ENDPOINTS.accountingConfigurationTemplates, request, options);
}

export function upsertAccountingConfigurationPostingRule(
  request: import("@/types").UpsertPostingRuleRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingConfigurationWorkspace>(WORKSTATION_API_ENDPOINTS.accountingConfigurationPostingRules, request, options);
}

export function approveAccountingConfigurationPostingRulePromotion(
  request: import("@/types").ApprovePostingRulePromotionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingConfigurationWorkspace>(
    WORKSTATION_API_ENDPOINTS.accountingConfigurationPostingRulePromotionApprovals,
    request,
    options
  );
}

export function dryRunAccountingConfigurationPostingRule(
  request: import("@/types").RuleDryRunRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").RuleDryRunResult>(WORKSTATION_API_ENDPOINTS.accountingConfigurationPostingRuleDryRun, request, options);
}

export function buildAccountingPostingRuleJournalCandidate(
  request: import("@/types").PostingRuleJournalCandidateRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").PostingRuleJournalCandidateResult>(
    WORKSTATION_API_ENDPOINTS.accountingConfigurationPostingRuleCandidates,
    request,
    options
  );
}

export function executeAccountingConfigurationPostingRuleTests(
  request: import("@/types").ExecuteAccountingRuleTestCasesRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingRuleTestSuiteResult>(WORKSTATION_API_ENDPOINTS.accountingConfigurationPostingRuleTests, request, options);
}

export function upsertAccountingConfigurationPostingRuleTestCase(
  request: import("@/types").UpsertAccountingRuleTestCaseRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingConfigurationWorkspace>(WORKSTATION_API_ENDPOINTS.accountingConfigurationPostingRuleTestCases, request, options);
}

export function activateAccountingConfiguration(
  request: import("@/types").ActivateAccountingConfigurationRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<import("@/types").AccountingConfigurationWorkspace>(WORKSTATION_API_ENDPOINTS.accountingConfigurationActivate, request, options);
}
