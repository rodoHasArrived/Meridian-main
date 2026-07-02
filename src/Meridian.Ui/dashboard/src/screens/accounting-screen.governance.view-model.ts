import { useCallback, useEffect, useMemo, useState } from "react";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import {
  activateAccountingConfiguration,
  assessAccountingProductionReadiness,
  approveAccountingConfigurationPostingRulePromotion,
  buildAccountingPostingRuleJournalCandidate,
  createLedgerBook,
  dryRunAccountingConfigurationPostingRule,
  executeAccountingConfigurationPostingRuleTests,
  getAccountingMigrationRunArtifacts,
  getAccountingMigrationWorkerPlans,
  getAccountingSystemMappingProfiles,
  getAccountingTenantAdministrationProfile,
  getAccountingProductionCertificationProfile,
  getAccountingConfiguration,
  previewAccountingConfigurationTemplate,
  upsertAccountingTenantAdministrationProfile,
  upsertAccountingSystemMappingProfile,
  upsertAccountingProductionCertificationProfile,
  upsertAccountingConfigurationChartNode,
  upsertAccountingConfigurationPostingRule,
  upsertAccountingConfigurationPostingRuleTestCase,
} from "@/lib/api";
import {
  formatCount,
  formatCurrency,
  formatDateOnly,
  formatDateTimeLabel,
} from "./accounting-screen.formatting";
import type {
  AccountingApprovalQueueSetupEditorViewModel,
  AccountingConfigurationAuditViewModel,
  AccountingConfigurationIssueViewModel,
  AccountingConfigurationMetricViewModel,
  AccountingConfigurationServices,
  AccountingConfigurationTemplateViewModel,
  AccountingConfigurationViewModel,
  AccountingDimensionMappingSetupEditorViewModel,
  AccountingExternalGlMappingProfileEditorViewModel,
  AccountingLedgerBookAdminRowViewModel,
  AccountingMigrationRolloutPlanItemViewModel,
  AccountingMigrationRunArtifactViewModel,
  AccountingMigrationWorkerPlanViewModel,
  AccountingProductionCertificationProfileControlEditViewModel,
  AccountingProductionCertificationProfileEditorViewModel,
  AccountingProductionGapViewModel,
  AccountingProductionReadinessComponentViewModel,
  AccountingProductionReadinessIssueViewModel,
  AccountingProductionReadinessViewModel,
  AccountingRulesStudioDryRunViewModel,
  AccountingRulesStudioJournalCandidateViewModel,
  AccountingRulesStudioPromotionReadinessViewModel,
  AccountingRulesStudioRuleViewModel,
  AccountingRulesStudioTestCaseViewModel,
  AccountingRulesStudioTestSuiteViewModel,
  AccountingTenantAdministrationControlViewModel,
  AccountingTenantAdministrationProfileControlEditViewModel,
  AccountingTenantAdministrationProfileEditorViewModel,
  AccountingToolingTone,
} from "./accounting-screen.view-model";
import type {
  AccountingBasisKind,
  AccountingConfigurationWorkspace,
  AccountingMigrationRunArtifact,
  AccountingMigrationRunWorkerPlan,
  AccountingMigrationRolloutPlanItem,
  AccountingProductionReadiness,
  AccountingProductionReadinessRequest,
  AccountingProductionCertificationProfile,
  AccountingDimensionalCertificationArtifact,
  AccountingDimensionalCertificationLane,
  AccountingTenantAdminCertificationArtifact,
  AccountingTenantAdminCertificationLane,
  AccountingTenantAdministrationProfile,
  AccountingApprovalQueueConfiguration,
  AccountingDimensionMappingConfiguration,
  AccountingWorkflowCertificationArtifact,
  AccountingWorkflowCertificationLane,
  AccountingJournalTemplatePreview,
  AccountingRuleDryRunMatch,
  AccountingRuleTestCase,
  AccountingRuleTestSuiteResult,
  AccountingSystemMappingProfileUpsertRequest,
  AllocationRule,
  ExternalGlMappingProfile,
  AccountingRulesStudioPromotionQueueItem,
  AccountingRulesStudioRuleRow,
  LedgerDimensionSet,
  PostingRule,
  PostingRuleJournalCandidateRequest,
  PostingRuleJournalCandidateResult,
  RuleDryRunResult,
  GeneratedPostingLine,
} from "@/types";

const PRODUCTION_CERTIFICATION_RETAINED_EVIDENCE_REQUIRED = "Retained evidence is required before saving production certification controls.";
interface AccountingTenantAdministrationProfileDraft {
  tenantScopeConfigured: boolean;
  adminRoleProfileConfigured: boolean;
  scopedAccessPoliciesConfigured: boolean;
  reportingGroupsConfigured: boolean;
  accountingAdminSurfaceConfigured: boolean;
  browserAccountingAdminSurfaceConfigured: boolean;
  wpfAccountingAdminSurfaceConfigured: boolean;
  chartAdministrationStudioConfigured: boolean;
  ruleTestPromotionStudioConfigured: boolean;
  closeSetupStudioConfigured: boolean;
  providerMappingStudioConfigured: boolean;
  tenantCompanyReportGroupSetupStudioConfigured: boolean;
  auditReviewToolingConfigured: boolean;
  bulkImportExportSafeguardsConfigured: boolean;
  performanceValidationConfigured: boolean;
  disasterRecoveryRunbookConfigured: boolean;
  ledgerBookAdministrationStudioConfigured: boolean;
  postingRuleAuthoringStudioConfigured: boolean;
  approvalQueueStudioConfigured: boolean;
  approvalQueueSetup: AccountingApprovalQueueSetupDraft;
  dimensionMappingStudioConfigured: boolean;
  dimensionMappingSetup: AccountingDimensionMappingSetupDraft;
  implementationSandboxConfigured: boolean;
  evidenceText: string;
}

interface AccountingApprovalQueueSetupDraft {
  queueId: string;
  displayName: string;
  workflowKind: string;
  requiredApprovalRole: string;
  requiredApprovalCount: string;
  segregationPolicy: string;
  evidenceRequirement: string;
}

interface AccountingDimensionMappingSetupDraft {
  mappingId: string;
  displayName: string;
  providerId: string;
  meridianDimensionsText: string;
  providerDimensionsText: string;
  evidenceRequirement: string;
}

interface AccountingProductionCertificationProfileDraft {
  postingRulesLedgerBookNativeCertified: boolean;
  journalLifecycleLedgerBookNativeCertified: boolean;
  closeReportingLedgerBookNativeCertified: boolean;
  closePlanConfigurationLedgerBookNativeCertified: boolean;
  externalGlLedgerBookNativeCertified: boolean;
  reconciliationLedgerBookNativeCertified: boolean;
  directLendingLedgerBookNativeCertified: boolean;
  strategyLedgerReadLedgerBookNativeCertified: boolean;
  periodReportDimensionQueriesCertified: boolean;
  crossPeriodReportDimensionQueriesCertified: boolean;
  journalQueryDimensionFiltersCertified: boolean;
  externalExportDimensionMappingCertified: boolean;
  ledgerLineDimensionsPersistedCertified: boolean;
  trialBalanceDimensionFiltersCertified: boolean;
  reportPackageDimensionProvenanceCertified: boolean;
  evidenceText: string;
}

interface AccountingExternalGlMappingProfileDraft {
  providerId: string;
  profileId: string;
  displayName: string;
  accountMappingsText: string;
  meridianDimensionsText: string;
  externalDimensionsText: string;
  evidenceText: string;
  certified: boolean;
}

interface AccountingChartAccountDraft {
  nodeId: string;
  path: string;
  accountName: string;
  accountType: string;
  parentPath: string;
  financialAccountId: string;
  evidenceText: string;
}

const defaultAccountingConfigurationServices: AccountingConfigurationServices = {
  getConfiguration: () => getAccountingConfiguration(),
  assessProductionReadiness: (request) => assessAccountingProductionReadiness(request),
  listMigrationRunArtifacts: (query) => getAccountingMigrationRunArtifacts(query),
  listMigrationWorkerPlans: (query) => getAccountingMigrationWorkerPlans(query),
  listExternalGlMappingProfiles: (query) => getAccountingSystemMappingProfiles(query),
  upsertExternalGlMappingProfile: (request) => upsertAccountingSystemMappingProfile(request),
  getProductionCertificationProfile: (query) => getAccountingProductionCertificationProfile(query),
  upsertProductionCertificationProfile: (request) => upsertAccountingProductionCertificationProfile(request),
  getTenantAdministrationProfile: (query) => getAccountingTenantAdministrationProfile(query),
  upsertTenantAdministrationProfile: (request) => upsertAccountingTenantAdministrationProfile(request),
  createLedgerBook: (request) => createLedgerBook(request),
  previewTemplate: (request) => previewAccountingConfigurationTemplate(request),
  upsertChartNode: (request) => upsertAccountingConfigurationChartNode(request),
  dryRunRule: (request) => dryRunAccountingConfigurationPostingRule(request),
  buildJournalCandidate: (request) => buildAccountingPostingRuleJournalCandidate(request),
  runRuleTests: (request) => executeAccountingConfigurationPostingRuleTests(request),
  saveRuleTestCase: (request) => upsertAccountingConfigurationPostingRuleTestCase(request),
  upsertRule: (request) => upsertAccountingConfigurationPostingRule(request),
  approveRulePromotion: (request) => approveAccountingConfigurationPostingRulePromotion(request),
  activate: (request) => activateAccountingConfiguration(request)
};
export function useAccountingConfigurationViewModel(
  services: AccountingConfigurationServices = defaultAccountingConfigurationServices
): AccountingConfigurationViewModel {
  const [workspace, setWorkspace] = useState<AccountingConfigurationWorkspace | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const [productionReadiness, setProductionReadiness] = useState<AccountingProductionReadiness | null>(null);
  const [migrationRunArtifacts, setMigrationRunArtifacts] = useState<AccountingMigrationRunArtifact[]>([]);
  const [migrationWorkerPlans, setMigrationWorkerPlans] = useState<AccountingMigrationRunWorkerPlan[]>([]);
  const [externalGlMappingProfiles, setExternalGlMappingProfiles] = useState<ExternalGlMappingProfile[]>([]);
  const [externalGlMappingProfileDraft, setExternalGlMappingProfileDraft] = useState<AccountingExternalGlMappingProfileDraft | null>(null);
  const [externalGlMappingProfileError, setExternalGlMappingProfileError] = useState<ApiErrorDisplay | null>(null);
  const [externalGlMappingProfileSaveBusy, setExternalGlMappingProfileSaveBusy] = useState(false);
  const [externalGlMappingProfileSaveError, setExternalGlMappingProfileSaveError] = useState<ApiErrorDisplay | null>(null);
  const [externalGlMappingProfileSaveMessage, setExternalGlMappingProfileSaveMessage] = useState<string | null>(null);
  const [productionReadinessLoading, setProductionReadinessLoading] = useState(false);
  const [productionReadinessError, setProductionReadinessError] = useState<ApiErrorDisplay | null>(null);
  const [productionCertificationProfile, setProductionCertificationProfile] = useState<AccountingProductionCertificationProfile | null>(null);
  const [productionCertificationDraft, setProductionCertificationDraft] = useState<AccountingProductionCertificationProfileDraft | null>(null);
  const [productionCertificationProfileError, setProductionCertificationProfileError] = useState<ApiErrorDisplay | null>(null);
  const [productionCertificationProfileSaveBusy, setProductionCertificationProfileSaveBusy] = useState(false);
  const [productionCertificationProfileSaveError, setProductionCertificationProfileSaveError] = useState<ApiErrorDisplay | null>(null);
  const [productionCertificationProfileSaveMessage, setProductionCertificationProfileSaveMessage] = useState<string | null>(null);
  const [tenantAdministrationProfile, setTenantAdministrationProfile] = useState<AccountingTenantAdministrationProfile | null>(null);
  const [tenantAdministrationDraft, setTenantAdministrationDraft] = useState<AccountingTenantAdministrationProfileDraft | null>(null);
  const [tenantAdministrationProfileError, setTenantAdministrationProfileError] = useState<ApiErrorDisplay | null>(null);
  const [tenantAdministrationProfileSaveBusy, setTenantAdministrationProfileSaveBusy] = useState(false);
  const [tenantAdministrationProfileSaveError, setTenantAdministrationProfileSaveError] = useState<ApiErrorDisplay | null>(null);
  const [tenantAdministrationProfileSaveMessage, setTenantAdministrationProfileSaveMessage] = useState<string | null>(null);
  const [sandboxProofBusy, setSandboxProofBusy] = useState(false);
  const [sandboxProofError, setSandboxProofError] = useState<ApiErrorDisplay | null>(null);
  const [sandboxProofMessage, setSandboxProofMessage] = useState<string | null>(null);
  const [preview, setPreview] = useState<AccountingJournalTemplatePreview | null>(null);
  const [previewBusy, setPreviewBusy] = useState(false);
  const [previewError, setPreviewError] = useState<ApiErrorDisplay | null>(null);
  const [selectedRuleId, setSelectedRuleId] = useState<string | null>(null);
  const [dryRunPreview, setDryRunPreview] = useState<RuleDryRunResult | null>(null);
  const [dryRunBusy, setDryRunBusy] = useState(false);
  const [dryRunError, setDryRunError] = useState<ApiErrorDisplay | null>(null);
  const [createLedgerBookBusy, setCreateLedgerBookBusy] = useState(false);
  const [createLedgerBookError, setCreateLedgerBookError] = useState<ApiErrorDisplay | null>(null);
  const [createdLedgerBookName, setCreatedLedgerBookName] = useState<string | null>(null);
  const [chartAccountDraft, setChartAccountDraft] = useState<AccountingChartAccountDraft>(() => buildDefaultAccountingChartAccountDraft());
  const [chartAccountSaveBusy, setChartAccountSaveBusy] = useState(false);
  const [chartAccountSaveError, setChartAccountSaveError] = useState<ApiErrorDisplay | null>(null);
  const [chartAccountSaveMessage, setChartAccountSaveMessage] = useState<string | null>(null);
  const [journalCandidatePreview, setJournalCandidatePreview] = useState<PostingRuleJournalCandidateResult | null>(null);
  const [journalCandidateBusy, setJournalCandidateBusy] = useState(false);
  const [journalCandidateError, setJournalCandidateError] = useState<ApiErrorDisplay | null>(null);
  const [applyEventPredicateBusy, setApplyEventPredicateBusy] = useState(false);
  const [applyEventPredicateError, setApplyEventPredicateError] = useState<ApiErrorDisplay | null>(null);
  const [eventPredicateRuleId, setEventPredicateRuleId] = useState<string | null>(null);
  const [applyThresholdBusy, setApplyThresholdBusy] = useState(false);
  const [applyThresholdError, setApplyThresholdError] = useState<ApiErrorDisplay | null>(null);
  const [thresholdRuleId, setThresholdRuleId] = useState<string | null>(null);
  const [applyEffectiveStartBusy, setApplyEffectiveStartBusy] = useState(false);
  const [applyEffectiveStartError, setApplyEffectiveStartError] = useState<ApiErrorDisplay | null>(null);
  const [effectiveStartRuleId, setEffectiveStartRuleId] = useState<string | null>(null);
  const [applyScopeBusy, setApplyScopeBusy] = useState(false);
  const [applyScopeError, setApplyScopeError] = useState<ApiErrorDisplay | null>(null);
  const [scopedRuleId, setScopedRuleId] = useState<string | null>(null);
  const [capturePostingsBusy, setCapturePostingsBusy] = useState(false);
  const [capturePostingsError, setCapturePostingsError] = useState<ApiErrorDisplay | null>(null);
  const [capturedPostingsRuleId, setCapturedPostingsRuleId] = useState<string | null>(null);
  const [applyFormulaBusy, setApplyFormulaBusy] = useState(false);
  const [applyFormulaError, setApplyFormulaError] = useState<ApiErrorDisplay | null>(null);
  const [formulaRuleId, setFormulaRuleId] = useState<string | null>(null);
  const [applyAllocationBusy, setApplyAllocationBusy] = useState(false);
  const [applyAllocationError, setApplyAllocationError] = useState<ApiErrorDisplay | null>(null);
  const [allocationRuleId, setAllocationRuleId] = useState<string | null>(null);
  const [raisePriorityBusy, setRaisePriorityBusy] = useState(false);
  const [raisePriorityError, setRaisePriorityError] = useState<ApiErrorDisplay | null>(null);
  const [raisedPriorityRuleId, setRaisedPriorityRuleId] = useState<string | null>(null);
  const [ruleTestSuite, setRuleTestSuite] = useState<AccountingRuleTestSuiteResult | null>(null);
  const [ruleTestBusy, setRuleTestBusy] = useState(false);
  const [ruleTestError, setRuleTestError] = useState<ApiErrorDisplay | null>(null);
  const [saveRuleTestBusy, setSaveRuleTestBusy] = useState(false);
  const [saveRuleTestError, setSaveRuleTestError] = useState<ApiErrorDisplay | null>(null);
  const [duplicateRuleBusy, setDuplicateRuleBusy] = useState(false);
  const [duplicateRuleError, setDuplicateRuleError] = useState<ApiErrorDisplay | null>(null);
  const [duplicatedRuleId, setDuplicatedRuleId] = useState<string | null>(null);
  const [archiveRuleBusy, setArchiveRuleBusy] = useState(false);
  const [archiveRuleError, setArchiveRuleError] = useState<ApiErrorDisplay | null>(null);
  const [archivedRuleId, setArchivedRuleId] = useState<string | null>(null);
  const [approveRulePromotionBusy, setApproveRulePromotionBusy] = useState(false);
  const [approveRulePromotionError, setApproveRulePromotionError] = useState<ApiErrorDisplay | null>(null);
  const [approvedRulePromotionId, setApprovedRulePromotionId] = useState<string | null>(null);
  const [activateBusy, setActivateBusy] = useState(false);
  const [activateError, setActivateError] = useState<ApiErrorDisplay | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    setProductionReadinessLoading(true);
    setProductionReadinessError(null);
    setProductionCertificationProfileError(null);
    setProductionCertificationProfileSaveError(null);
    setTenantAdministrationProfileError(null);
    setTenantAdministrationProfileSaveError(null);
    setSandboxProofError(null);
    setExternalGlMappingProfileError(null);
    setExternalGlMappingProfileSaveError(null);
    try {
      const next = await services.getConfiguration();
      if (!next) {
        throw new Error("Accounting configuration response was empty.");
      }
      setWorkspace(next);
      try {
        const readinessRequest: AccountingProductionReadinessRequest = {
          fundProfileId: next.fundProfileId,
          ledgerBookId: next.ledgerBookId ?? null,
          accountingBasis: next.ledgerBookSetupCandidate?.accountingBasis ?? null,
          requiredLedgerBookScopes: next.ledgerBookSetupCandidate
            ? [{
                fundStructureNodeId: next.ledgerBookSetupCandidate.fundStructureNodeId,
                fundStructureNodeKind: next.ledgerBookSetupCandidate.fundStructureNodeKind,
                accountingBasis: next.ledgerBookSetupCandidate.accountingBasis,
                displayName: next.ledgerBookSetupCandidate.displayName
              }]
            : null
        };
        const readiness = await services.assessProductionReadiness(readinessRequest);
        if (!readiness) {
          throw new Error("Accounting production readiness response was empty.");
        }
        setProductionReadiness(readiness);
        try {
          const profiles = await services.listExternalGlMappingProfiles({
            fundProfileId: next.fundProfileId,
            ledgerBookId: next.ledgerBookId ?? null
          });
          setExternalGlMappingProfiles(profiles);
          setExternalGlMappingProfileDraft(buildAccountingExternalGlMappingProfileDraft(next, profiles[0] ?? null));
        } catch (mappingErr) {
          setExternalGlMappingProfiles([]);
          setExternalGlMappingProfileDraft(buildAccountingExternalGlMappingProfileDraft(next, null));
          setExternalGlMappingProfileError(describeApiError(mappingErr, "Retained external GL mapping profiles are unavailable."));
        }
        try {
          const profile = await services.getProductionCertificationProfile({
            tenantId: readiness.tenantAdministration?.tenantId ?? null,
            companyId: readiness.tenantAdministration?.companyId ?? null,
            fundProfileId: readiness.fundProfileId,
            ledgerBookId: readiness.ledgerBookId ?? next.ledgerBookId ?? null
          });
          setProductionCertificationProfile(profile);
          setProductionCertificationDraft(buildAccountingProductionCertificationProfileDraft(profile));
        } catch (profileErr) {
          const fallbackProfile = buildAccountingProductionCertificationProfileFromReadiness(readiness);
          setProductionCertificationProfile(fallbackProfile);
          setProductionCertificationDraft(fallbackProfile ? buildAccountingProductionCertificationProfileDraft(fallbackProfile) : null);
          setProductionCertificationProfileError(describeApiError(profileErr, "Retained production certification profile has not been saved yet."));
        }
        try {
          const profile = await services.getTenantAdministrationProfile({
            tenantId: readiness.tenantAdministration?.tenantId ?? null,
            companyId: readiness.tenantAdministration?.companyId ?? null
          });
          setTenantAdministrationProfile(profile);
          setTenantAdministrationDraft(buildAccountingTenantAdministrationProfileDraft(profile));
        } catch (profileErr) {
          const fallbackProfile = buildAccountingTenantAdministrationProfileFromReadiness(readiness);
          setTenantAdministrationProfile(fallbackProfile);
          setTenantAdministrationDraft(fallbackProfile ? buildAccountingTenantAdministrationProfileDraft(fallbackProfile) : null);
          setTenantAdministrationProfileError(describeApiError(profileErr, "Retained tenant administration profile has not been saved yet."));
        }
        try {
          const artifactList = await services.listMigrationRunArtifacts({
            fundProfileId: next.fundProfileId,
            ledgerBookId: next.ledgerBookId ?? null
          });
          setMigrationRunArtifacts(artifactList.artifacts ?? readiness.migrationRunArtifacts ?? []);
        } catch {
          setMigrationRunArtifacts(readiness.migrationRunArtifacts ?? []);
        }
        try {
          const workerPlanList = await services.listMigrationWorkerPlans({
            fundProfileId: next.fundProfileId,
            ledgerBookId: next.ledgerBookId ?? null
          });
          setMigrationWorkerPlans(workerPlanList.plans ?? []);
        } catch {
          setMigrationWorkerPlans([]);
        }
      } catch (readinessErr) {
        setProductionReadiness(null);
        setProductionCertificationProfile(null);
        setProductionCertificationDraft(null);
        setMigrationRunArtifacts([]);
        setMigrationWorkerPlans([]);
        setExternalGlMappingProfiles([]);
        setExternalGlMappingProfileDraft(null);
        setProductionReadinessError(describeApiError(readinessErr, "Accounting production readiness is unavailable."));
      } finally {
        setProductionReadinessLoading(false);
      }
      setSelectedRuleId((current) => {
        if (current && next.postingRules.some((rule) => rule.ruleId === current && !rule.isArchived)) {
          return current;
        }

        return next.postingRules.find((rule) => !rule.isArchived)?.ruleId ?? null;
      });
    } catch (err) {
      setError(describeApiError(err, "Accounting configuration is unavailable."));
      setProductionReadiness(null);
      setMigrationRunArtifacts([]);
      setMigrationWorkerPlans([]);
      setExternalGlMappingProfiles([]);
      setExternalGlMappingProfileDraft(null);
      setProductionCertificationProfile(null);
      setProductionCertificationDraft(null);
      setTenantAdministrationProfile(null);
      setTenantAdministrationDraft(null);
      setProductionReadinessLoading(false);
    } finally {
      setLoading(false);
    }
  }, [services]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const updateProductionCertificationControl = useCallback((controlId: string, checked: boolean) => {
    setProductionCertificationDraft((current) => {
      if (!current) {
        return current;
      }

      switch (controlId) {
        case "posting-rules-book":
          return { ...current, postingRulesLedgerBookNativeCertified: checked };
        case "journal-lifecycle-book":
          return { ...current, journalLifecycleLedgerBookNativeCertified: checked };
        case "close-reporting-book":
          return { ...current, closeReportingLedgerBookNativeCertified: checked };
        case "close-plan-configuration-book":
          return { ...current, closePlanConfigurationLedgerBookNativeCertified: checked };
        case "external-gl-book":
          return { ...current, externalGlLedgerBookNativeCertified: checked };
        case "reconciliation-book":
          return { ...current, reconciliationLedgerBookNativeCertified: checked };
        case "direct-lending-book":
          return { ...current, directLendingLedgerBookNativeCertified: checked };
        case "strategy-ledger-reads-book":
          return { ...current, strategyLedgerReadLedgerBookNativeCertified: checked };
        case "period-report-dimensions":
          return { ...current, periodReportDimensionQueriesCertified: checked };
        case "cross-period-dimensions":
          return { ...current, crossPeriodReportDimensionQueriesCertified: checked };
        case "journal-dimension-filters":
          return { ...current, journalQueryDimensionFiltersCertified: checked };
        case "external-export-dimensions":
          return { ...current, externalExportDimensionMappingCertified: checked };
        case "ledger-line-dimensions":
          return { ...current, ledgerLineDimensionsPersistedCertified: checked };
        case "trial-balance-dimensions":
          return { ...current, trialBalanceDimensionFiltersCertified: checked };
        case "report-package-dimensions":
          return { ...current, reportPackageDimensionProvenanceCertified: checked };
        default:
          return current;
      }
    });
    setProductionCertificationProfileSaveMessage(null);
    setProductionCertificationProfileSaveError(null);
  }, []);

  const updateProductionCertificationEvidence = useCallback((value: string) => {
    setProductionCertificationDraft((current) => current ? { ...current, evidenceText: value } : current);
    setProductionCertificationProfileSaveMessage(null);
    setProductionCertificationProfileSaveError(null);
  }, []);

  const saveProductionCertificationProfile = useCallback(async () => {
    if (!productionCertificationProfile || !productionCertificationDraft || productionCertificationProfileSaveBusy) {
      return;
    }

    const correlationId = `browser-accounting-production-certification-${Date.now()}`;
    const retainedEvidenceReferences = splitAccountingEvidenceReferences(productionCertificationDraft.evidenceText);
    if (retainedEvidenceReferences.length === 0) {
      setProductionCertificationProfileSaveError({
        summary: PRODUCTION_CERTIFICATION_RETAINED_EVIDENCE_REQUIRED,
        details: []
      });
      setProductionCertificationProfileSaveMessage(null);
      return;
    }

    const evidenceReferences = withProductionCertificationControlEvidence(
      retainedEvidenceReferences,
      productionCertificationProfile,
      productionCertificationDraft
    );
    const workflowCertificationArtifact = buildAccountingWorkflowCertificationArtifact(
      productionCertificationProfile,
      productionCertificationDraft,
      evidenceReferences,
      correlationId
    );
    const dimensionalCertificationArtifact = buildAccountingDimensionalCertificationArtifact(
      productionCertificationProfile,
      productionCertificationDraft,
      evidenceReferences,
      correlationId
    );
    const tenantAdminCertificationArtifact = buildAccountingTenantAdminCertificationArtifact(
      productionCertificationProfile,
      tenantAdministrationProfile,
      tenantAdministrationDraft,
      evidenceReferences,
      workspace?.ledgerBookId ?? null,
      correlationId
    );
    const nextProfile: AccountingProductionCertificationProfile = {
      ...productionCertificationProfile,
      postingRulesLedgerBookNativeCertified: productionCertificationDraft.postingRulesLedgerBookNativeCertified,
      journalLifecycleLedgerBookNativeCertified: productionCertificationDraft.journalLifecycleLedgerBookNativeCertified,
      closeReportingLedgerBookNativeCertified: productionCertificationDraft.closeReportingLedgerBookNativeCertified,
      closePlanConfigurationLedgerBookNativeCertified: productionCertificationDraft.closePlanConfigurationLedgerBookNativeCertified,
      externalGlLedgerBookNativeCertified: productionCertificationDraft.externalGlLedgerBookNativeCertified,
      reconciliationLedgerBookNativeCertified: productionCertificationDraft.reconciliationLedgerBookNativeCertified,
      directLendingLedgerBookNativeCertified: productionCertificationDraft.directLendingLedgerBookNativeCertified,
      strategyLedgerReadLedgerBookNativeCertified: productionCertificationDraft.strategyLedgerReadLedgerBookNativeCertified,
      periodReportDimensionQueriesCertified: productionCertificationDraft.periodReportDimensionQueriesCertified,
      crossPeriodReportDimensionQueriesCertified: productionCertificationDraft.crossPeriodReportDimensionQueriesCertified,
      journalQueryDimensionFiltersCertified: productionCertificationDraft.journalQueryDimensionFiltersCertified,
      externalExportDimensionMappingCertified: productionCertificationDraft.externalExportDimensionMappingCertified,
      ledgerLineDimensionsPersistedCertified: productionCertificationDraft.ledgerLineDimensionsPersistedCertified,
      trialBalanceDimensionFiltersCertified: productionCertificationDraft.trialBalanceDimensionFiltersCertified,
      reportPackageDimensionProvenanceCertified: productionCertificationDraft.reportPackageDimensionProvenanceCertified,
      updatedAtUtc: new Date().toISOString(),
      updatedBy: "browser-accounting-operator",
      evidenceReferences,
      correlationId,
      workflowCertificationArtifacts: mergeAccountingCertificationArtifacts(
        productionCertificationProfile.workflowCertificationArtifacts,
        workflowCertificationArtifact
      ),
      dimensionalCertificationArtifacts: mergeAccountingCertificationArtifacts(
        productionCertificationProfile.dimensionalCertificationArtifacts,
        dimensionalCertificationArtifact
      ),
      tenantAdminCertificationArtifacts: mergeAccountingCertificationArtifacts(
        productionCertificationProfile.tenantAdminCertificationArtifacts,
        tenantAdminCertificationArtifact
      )
    };

    setProductionCertificationProfileSaveBusy(true);
    setProductionCertificationProfileSaveError(null);
    setProductionCertificationProfileSaveMessage(null);
    try {
      const saved = await services.upsertProductionCertificationProfile({
        profile: nextProfile,
        actor: "browser-accounting-operator",
        correlationId,
        evidenceLinks: evidenceReferences
      });
      setProductionCertificationProfile(saved);
      setProductionCertificationDraft(buildAccountingProductionCertificationProfileDraft(saved));
      setProductionCertificationProfileError(null);
      setProductionCertificationProfileSaveMessage("Production certification profile saved; readiness refreshed from retained book and dimension controls.");
      await refresh();
    } catch (err) {
      setProductionCertificationProfileSaveError(describeApiError(err, "Production certification profile save failed."));
    } finally {
      setProductionCertificationProfileSaveBusy(false);
    }
  }, [productionCertificationDraft, productionCertificationProfile, productionCertificationProfileSaveBusy, refresh, services, tenantAdministrationDraft, tenantAdministrationProfile, workspace?.ledgerBookId]);

  const updateTenantAdministrationControl = useCallback((controlId: string, checked: boolean) => {
    setTenantAdministrationDraft((current) => {
      if (!current) {
        return current;
      }

      switch (controlId) {
        case "tenant-config":
          return { ...current, tenantScopeConfigured: checked };
        case "admin-roles":
          return { ...current, adminRoleProfileConfigured: checked };
        case "scoped-access":
          return { ...current, scopedAccessPoliciesConfigured: checked };
        case "reporting-groups":
          return { ...current, reportingGroupsConfigured: checked };
        case "operator-surface":
          return { ...current, accountingAdminSurfaceConfigured: checked, browserAccountingAdminSurfaceConfigured: checked };
        case "chart-administration-studio":
          return { ...current, chartAdministrationStudioConfigured: checked };
        case "rule-test-promotion-studio":
          return { ...current, ruleTestPromotionStudioConfigured: checked };
        case "close-setup-studio":
          return { ...current, closeSetupStudioConfigured: checked };
        case "provider-mapping-studio":
          return { ...current, providerMappingStudioConfigured: checked };
        case "tenant-company-report-group-studio":
          return { ...current, tenantCompanyReportGroupSetupStudioConfigured: checked };
        case "audit-review-tooling":
          return { ...current, auditReviewToolingConfigured: checked };
        case "bulk-import-export-safeguards":
          return { ...current, bulkImportExportSafeguardsConfigured: checked };
        case "performance-validation":
          return { ...current, performanceValidationConfigured: checked };
        case "disaster-recovery-runbook":
          return { ...current, disasterRecoveryRunbookConfigured: checked };
        case "ledger-book-administration-studio":
          return { ...current, ledgerBookAdministrationStudioConfigured: checked };
        case "posting-rule-authoring-studio":
          return { ...current, postingRuleAuthoringStudioConfigured: checked };
        case "approval-queue-studio":
          return { ...current, approvalQueueStudioConfigured: checked };
        case "dimension-mapping-studio":
          return { ...current, dimensionMappingStudioConfigured: checked };
        case "implementation-sandbox":
          return { ...current, implementationSandboxConfigured: checked };
        default:
          return current;
      }
    });
    setTenantAdministrationProfileSaveMessage(null);
    setTenantAdministrationProfileSaveError(null);
  }, []);

  const updateTenantAdministrationEvidence = useCallback((value: string) => {
    setTenantAdministrationDraft((current) => current ? { ...current, evidenceText: value } : current);
    setTenantAdministrationProfileSaveMessage(null);
    setTenantAdministrationProfileSaveError(null);
  }, []);

  const updateApprovalQueueSetup = useCallback((patch: Partial<AccountingApprovalQueueSetupDraft>) => {
    setTenantAdministrationDraft((current) => current ? {
      ...current,
      approvalQueueSetup: {
        ...current.approvalQueueSetup,
        ...patch
      },
      approvalQueueStudioConfigured: true
    } : current);
    setTenantAdministrationProfileSaveMessage(null);
    setTenantAdministrationProfileSaveError(null);
  }, []);

  const updateDimensionMappingSetup = useCallback((patch: Partial<AccountingDimensionMappingSetupDraft>) => {
    setTenantAdministrationDraft((current) => current ? {
      ...current,
      dimensionMappingSetup: {
        ...current.dimensionMappingSetup,
        ...patch
      },
      dimensionMappingStudioConfigured: true
    } : current);
    setTenantAdministrationProfileSaveMessage(null);
    setTenantAdministrationProfileSaveError(null);
  }, []);

  const updateExternalGlMappingProfileDraft = useCallback((patch: Partial<AccountingExternalGlMappingProfileDraft>) => {
    setExternalGlMappingProfileDraft((current) => current ? { ...current, ...patch } : current);
    setExternalGlMappingProfileSaveMessage(null);
    setExternalGlMappingProfileSaveError(null);
  }, []);

  const saveExternalGlMappingProfile = useCallback(async () => {
    if (!workspace || !externalGlMappingProfileDraft || externalGlMappingProfileSaveBusy) {
      return;
    }

    const providerId = externalGlMappingProfileDraft.providerId.trim();
    const profileId = externalGlMappingProfileDraft.profileId.trim();
    const displayName = externalGlMappingProfileDraft.displayName.trim();
    const accountMappings = parseExternalGlAccountMappings(externalGlMappingProfileDraft.accountMappingsText);
    const meridianDimensions = parseLedgerDimensionSet(
      externalGlMappingProfileDraft.meridianDimensionsText,
      buildDefaultMeridianExternalGlDimensions(providerId, profileId, workspace)
    );
    const externalDimensions = parseLedgerDimensionSet(
      externalGlMappingProfileDraft.externalDimensionsText,
      buildDefaultProviderExternalGlDimensions(workspace)
    );
    const retainedEvidence = splitAccountingEvidenceReferences(externalGlMappingProfileDraft.evidenceText);
    if (!providerId || !profileId || !displayName || Object.keys(accountMappings).length === 0 || retainedEvidence.length === 0) {
      setExternalGlMappingProfileSaveError({
        summary: "Provider, profile id, display name, account mappings, and retained evidence are required before saving an external GL mapping profile.",
        details: []
      });
      setExternalGlMappingProfileSaveMessage(null);
      return;
    }

    const correlationId = `browser-external-gl-mapping-profile-${Date.now()}`;
    const evidenceLinks = withExternalGlMappingProfileEvidence(retainedEvidence, providerId, workspace.fundProfileId, profileId, workspace.ledgerBookId ?? null);
    const request: AccountingSystemMappingProfileUpsertRequest = {
      profile: {
        profileId,
        providerId,
        displayName,
        updatedAtUtc: new Date().toISOString(),
        accountMappings,
        dimensionMappings: [
          buildExternalGlDimensionMappingProfile(providerId, profileId, meridianDimensions, externalDimensions, externalGlMappingProfileDraft.certified)
        ],
        certificationState: externalGlMappingProfileDraft.certified ? "Certified" : "ReadyForReview"
      },
      actor: "browser-accounting-operator",
      providerId,
      fundProfileId: workspace.fundProfileId,
      ledgerBookId: workspace.ledgerBookId ?? null,
      tenantId: productionReadiness?.tenantAdministration?.tenantId ?? null,
      companyId: productionReadiness?.tenantAdministration?.companyId ?? null,
      correlationId,
      evidenceLinks,
      actionOrigin: "HumanOperator"
    };

    setExternalGlMappingProfileSaveBusy(true);
    setExternalGlMappingProfileSaveError(null);
    setExternalGlMappingProfileSaveMessage(null);
    try {
      const saved = await services.upsertExternalGlMappingProfile(request);
      setExternalGlMappingProfiles((current) => [saved, ...current.filter((profile) => profile.profileId !== saved.profileId)]);
      setExternalGlMappingProfileDraft(buildAccountingExternalGlMappingProfileDraft(workspace, saved));
      setExternalGlMappingProfileSaveMessage(`External GL mapping profile ${saved.profileId} saved as ${saved.certificationState}; readiness refreshed from retained provider mapping.`);
      await refresh();
    } catch (err) {
      setExternalGlMappingProfileSaveError(describeApiError(err, "External GL mapping profile save failed."));
    } finally {
      setExternalGlMappingProfileSaveBusy(false);
    }
  }, [externalGlMappingProfileDraft, externalGlMappingProfileSaveBusy, productionReadiness?.tenantAdministration?.companyId, productionReadiness?.tenantAdministration?.tenantId, refresh, services, workspace]);

  const saveTenantAdministrationProfile = useCallback(async () => {
    if (!tenantAdministrationProfile || !tenantAdministrationDraft || tenantAdministrationProfileSaveBusy) {
      return;
    }

    const correlationId = `browser-accounting-tenant-admin-${Date.now()}`;
    const retainedEvidenceReferences = splitAccountingTenantAdministrationEvidence(tenantAdministrationDraft.evidenceText);
    if (retainedEvidenceReferences.length === 0) {
      setTenantAdministrationProfileSaveError({
        summary: "Retained setup evidence is required before saving tenant administration controls.",
        details: []
      });
      setTenantAdministrationProfileSaveMessage(null);
      return;
    }

    const evidenceReferences = withLedgerBookTenantAdministrationEvidence(
      retainedEvidenceReferences,
      tenantAdministrationProfile,
      workspace?.ledgerBookId ?? null,
      tenantAdministrationDraft
    );
    const approvalQueueConfiguration = buildAccountingApprovalQueueConfiguration(tenantAdministrationDraft.approvalQueueSetup);
    if (tenantAdministrationDraft.approvalQueueStudioConfigured && !approvalQueueConfiguration) {
      setTenantAdministrationProfileSaveError({
        summary: "Complete approval queue id, workflow kind, approval role/count, segregation policy, and evidence requirement before saving approval queue setup.",
        details: []
      });
      setTenantAdministrationProfileSaveMessage(null);
      return;
    }
    const dimensionMappingConfiguration = buildAccountingDimensionMappingConfiguration(tenantAdministrationDraft.dimensionMappingSetup);
    if (tenantAdministrationDraft.dimensionMappingStudioConfigured && !dimensionMappingConfiguration) {
      setTenantAdministrationProfileSaveError({
        summary: "Complete dimension mapping id, display name, provider id, Meridian dimensions, provider dimensions, and evidence requirement before saving dimension mapping setup.",
        details: []
      });
      setTenantAdministrationProfileSaveMessage(null);
      return;
    }
    const nextProfile: AccountingTenantAdministrationProfile = {
      ...tenantAdministrationProfile,
      tenantScopeConfigured: tenantAdministrationDraft.tenantScopeConfigured,
      adminRoleProfileConfigured: tenantAdministrationDraft.adminRoleProfileConfigured,
      scopedAccessPoliciesConfigured: tenantAdministrationDraft.scopedAccessPoliciesConfigured,
      reportingGroupsConfigured: tenantAdministrationDraft.reportingGroupsConfigured,
      accountingAdminSurfaceConfigured: tenantAdministrationDraft.accountingAdminSurfaceConfigured,
      browserAccountingAdminSurfaceConfigured: tenantAdministrationDraft.browserAccountingAdminSurfaceConfigured,
      wpfAccountingAdminSurfaceConfigured: tenantAdministrationDraft.wpfAccountingAdminSurfaceConfigured,
      chartAdministrationStudioConfigured: tenantAdministrationDraft.chartAdministrationStudioConfigured,
      ruleTestPromotionStudioConfigured: tenantAdministrationDraft.ruleTestPromotionStudioConfigured,
      closeSetupStudioConfigured: tenantAdministrationDraft.closeSetupStudioConfigured,
      providerMappingStudioConfigured: tenantAdministrationDraft.providerMappingStudioConfigured,
      tenantCompanyReportGroupSetupStudioConfigured: tenantAdministrationDraft.tenantCompanyReportGroupSetupStudioConfigured,
      auditReviewToolingConfigured: tenantAdministrationDraft.auditReviewToolingConfigured,
      bulkImportExportSafeguardsConfigured: tenantAdministrationDraft.bulkImportExportSafeguardsConfigured,
      performanceValidationConfigured: tenantAdministrationDraft.performanceValidationConfigured,
      disasterRecoveryRunbookConfigured: tenantAdministrationDraft.disasterRecoveryRunbookConfigured,
      ledgerBookAdministrationStudioConfigured: tenantAdministrationDraft.ledgerBookAdministrationStudioConfigured,
      postingRuleAuthoringStudioConfigured: tenantAdministrationDraft.postingRuleAuthoringStudioConfigured,
      approvalQueueStudioConfigured: tenantAdministrationDraft.approvalQueueStudioConfigured,
      dimensionMappingStudioConfigured: tenantAdministrationDraft.dimensionMappingStudioConfigured,
      implementationSandboxConfigured: tenantAdministrationDraft.implementationSandboxConfigured,
      approvalQueueConfigurations: approvalQueueConfiguration ? [approvalQueueConfiguration] : [],
      dimensionMappingConfigurations: dimensionMappingConfiguration ? [dimensionMappingConfiguration] : [],
      updatedAtUtc: new Date().toISOString(),
      updatedBy: "browser-accounting-operator",
      evidenceReferences,
      correlationId
    };

    setTenantAdministrationProfileSaveBusy(true);
    setTenantAdministrationProfileSaveError(null);
    setTenantAdministrationProfileSaveMessage(null);
    try {
      const saved = await services.upsertTenantAdministrationProfile({
        profile: nextProfile,
        actor: "browser-accounting-operator",
        correlationId,
        evidenceLinks: evidenceReferences
      });
      setTenantAdministrationProfile(saved);
      setTenantAdministrationDraft(buildAccountingTenantAdministrationProfileDraft(saved));
      setTenantAdministrationProfileError(null);
      setTenantAdministrationProfileSaveMessage("Tenant administration setup profile saved; production readiness refreshed from retained controls.");
      await refresh();
    } catch (err) {
      setTenantAdministrationProfileSaveError(describeApiError(err, "Tenant administration setup profile save failed."));
    } finally {
      setTenantAdministrationProfileSaveBusy(false);
    }
  }, [refresh, services, tenantAdministrationDraft, tenantAdministrationProfile, tenantAdministrationProfileSaveBusy, workspace?.ledgerBookId]);

  const retainImplementationSandboxProof = useCallback(async () => {
    if (!tenantAdministrationProfile || !tenantAdministrationDraft || sandboxProofBusy) {
      return;
    }

    const ledgerBookId = workspace?.ledgerBookId?.trim() ?? null;
    if (!ledgerBookId) {
      setSandboxProofError({
        summary: "Select a ledger-book-scoped Accounting Configure workspace before retaining implementation sandbox proof.",
        details: []
      });
      setSandboxProofMessage(null);
      return;
    }

    const correlationId = `browser-accounting-sandbox-proof-${Date.now()}`;
    const approvalQueueConfiguration = buildAccountingApprovalQueueConfiguration(tenantAdministrationDraft.approvalQueueSetup);
    if (tenantAdministrationDraft.approvalQueueStudioConfigured && !approvalQueueConfiguration) {
      setSandboxProofError({
        summary: "Complete approval queue id, workflow kind, approval role/count, segregation policy, and evidence requirement before retaining implementation sandbox proof.",
        details: []
      });
      setSandboxProofMessage(null);
      return;
    }
    const dimensionMappingConfiguration = buildAccountingDimensionMappingConfiguration(tenantAdministrationDraft.dimensionMappingSetup);
    if (tenantAdministrationDraft.dimensionMappingStudioConfigured && !dimensionMappingConfiguration) {
      setSandboxProofError({
        summary: "Complete dimension mapping id, display name, provider id, Meridian dimensions, provider dimensions, and evidence requirement before retaining implementation sandbox proof.",
        details: []
      });
      setSandboxProofMessage(null);
      return;
    }
    const nextDraft: AccountingTenantAdministrationProfileDraft = {
      ...tenantAdministrationDraft,
      implementationSandboxConfigured: true,
      evidenceText: withImplementationSandboxEvidence(
        splitAccountingTenantAdministrationEvidence(tenantAdministrationDraft.evidenceText),
        tenantAdministrationProfile,
        ledgerBookId
      ).join("\n")
    };
    const evidenceReferences = withLedgerBookTenantAdministrationEvidence(
      splitAccountingTenantAdministrationEvidence(nextDraft.evidenceText),
      tenantAdministrationProfile,
      ledgerBookId,
      nextDraft
    );
    const nextProfile: AccountingTenantAdministrationProfile = {
      ...tenantAdministrationProfile,
      tenantScopeConfigured: nextDraft.tenantScopeConfigured,
      adminRoleProfileConfigured: nextDraft.adminRoleProfileConfigured,
      scopedAccessPoliciesConfigured: nextDraft.scopedAccessPoliciesConfigured,
      reportingGroupsConfigured: nextDraft.reportingGroupsConfigured,
      accountingAdminSurfaceConfigured: nextDraft.accountingAdminSurfaceConfigured,
      browserAccountingAdminSurfaceConfigured: nextDraft.browserAccountingAdminSurfaceConfigured,
      wpfAccountingAdminSurfaceConfigured: nextDraft.wpfAccountingAdminSurfaceConfigured,
      chartAdministrationStudioConfigured: nextDraft.chartAdministrationStudioConfigured,
      ruleTestPromotionStudioConfigured: nextDraft.ruleTestPromotionStudioConfigured,
      closeSetupStudioConfigured: nextDraft.closeSetupStudioConfigured,
      providerMappingStudioConfigured: nextDraft.providerMappingStudioConfigured,
      tenantCompanyReportGroupSetupStudioConfigured: nextDraft.tenantCompanyReportGroupSetupStudioConfigured,
      auditReviewToolingConfigured: nextDraft.auditReviewToolingConfigured,
      bulkImportExportSafeguardsConfigured: nextDraft.bulkImportExportSafeguardsConfigured,
      performanceValidationConfigured: nextDraft.performanceValidationConfigured,
      disasterRecoveryRunbookConfigured: nextDraft.disasterRecoveryRunbookConfigured,
      ledgerBookAdministrationStudioConfigured: nextDraft.ledgerBookAdministrationStudioConfigured,
      postingRuleAuthoringStudioConfigured: nextDraft.postingRuleAuthoringStudioConfigured,
      approvalQueueStudioConfigured: nextDraft.approvalQueueStudioConfigured,
      dimensionMappingStudioConfigured: nextDraft.dimensionMappingStudioConfigured,
      implementationSandboxConfigured: true,
      approvalQueueConfigurations: approvalQueueConfiguration ? [approvalQueueConfiguration] : [],
      dimensionMappingConfigurations: dimensionMappingConfiguration ? [dimensionMappingConfiguration] : [],
      updatedAtUtc: new Date().toISOString(),
      updatedBy: "browser-accounting-operator",
      evidenceReferences,
      correlationId
    };

    setSandboxProofBusy(true);
    setSandboxProofError(null);
    setSandboxProofMessage(null);
    try {
      const saved = await services.upsertTenantAdministrationProfile({
        profile: nextProfile,
        actor: "browser-accounting-operator",
        correlationId,
        evidenceLinks: evidenceReferences
      });
      setTenantAdministrationProfile(saved);
      setTenantAdministrationDraft(buildAccountingTenantAdministrationProfileDraft(saved));
      setSandboxProofMessage("Implementation sandbox proof retained; readiness refreshed from validation and ledger-book evidence.");
      await refresh();
    } catch (err) {
      setSandboxProofError(describeApiError(err, "Implementation sandbox proof save failed."));
    } finally {
      setSandboxProofBusy(false);
    }
  }, [refresh, sandboxProofBusy, services, tenantAdministrationDraft, tenantAdministrationProfile, workspace?.ledgerBookId]);

  const previewFirstTemplate = useCallback(async () => {
    const template = workspace?.journalTemplates.find((item) => !item.isArchived) ?? null;
    if (!workspace || !template || previewBusy) {
      return;
    }

    setPreviewBusy(true);
    setPreviewError(null);
    try {
      const result = await services.previewTemplate({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        templateId: template.templateId,
        actor: "browser-accounting-operator",
        correlationId: `browser-accounting-config-preview-${Date.now()}`
      });
      setPreview(result);
    } catch (err) {
      setPreviewError(describeApiError(err, "Template preview failed."));
    } finally {
      setPreviewBusy(false);
    }
  }, [previewBusy, services, workspace]);

  const dryRunSelectedRule = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (!workspace || !rule || dryRunBusy) {
      return;
    }

    setDryRunBusy(true);
    setDryRunError(null);
    try {
      const result = await services.dryRunRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        sourceEventType: rule.sourceEventType,
        eventAmount: resolveDryRunEventAmount(rule),
        currency: resolveDryRunCurrency(rule),
        effectiveDate: resolveDryRunEffectiveDate(rule),
        actor: "browser-accounting-operator",
        dimensions: rule.scope ?? null,
        counterpartyId: rule.scope?.counterpartyId ?? null,
        instrumentSymbol: null,
        correlationId: `browser-accounting-rule-dry-run-${Date.now()}`
      });
      setSelectedRuleId(result.selectedRuleId ?? rule.ruleId);
      setDryRunPreview(result);
    } catch (err) {
      setDryRunError(describeApiError(err, "Accounting rule dry run failed."));
    } finally {
      setDryRunBusy(false);
    }
  }, [dryRunBusy, selectedRuleId, services, workspace]);

  const createLedgerBookFromSetupCandidate = useCallback(async () => {
    const candidate = workspace?.ledgerBookSetupCandidate ?? null;
    if (!workspace || !candidate || createLedgerBookBusy) {
      return;
    }

    setCreateLedgerBookBusy(true);
    setCreateLedgerBookError(null);
    setCreatedLedgerBookName(null);
    try {
      const created = await services.createLedgerBook({
        fundProfileId: candidate.fundProfileId,
        fundStructureNodeId: candidate.fundStructureNodeId,
        fundStructureNodeKind: candidate.fundStructureNodeKind,
        displayName: candidate.displayName,
        baseCurrency: candidate.baseCurrency,
        description: candidate.description ?? null,
        accountingBasis: candidate.accountingBasis,
        accountingPolicyId: candidate.accountingPolicyId,
        accountingPolicyVersion: candidate.accountingPolicyVersion
      });
      setCreatedLedgerBookName(created.displayName);
      const refreshed = await services.getConfiguration();
      setWorkspace(refreshed);
      setSelectedRuleId((current) => {
        if (current && refreshed.postingRules.some((rule) => rule.ruleId === current && !rule.isArchived)) {
          return current;
        }

        return refreshed.postingRules.find((rule) => !rule.isArchived)?.ruleId ?? null;
      });
    } catch (err) {
      setCreateLedgerBookError(describeApiError(err, "Ledger book setup could not be created."));
    } finally {
      setCreateLedgerBookBusy(false);
    }
  }, [createLedgerBookBusy, services, workspace]);

  const updateChartAccountDraft = useCallback((patch: Partial<AccountingChartAccountDraft>) => {
    setChartAccountDraft((current) => ({ ...current, ...patch }));
    setChartAccountSaveError(null);
    setChartAccountSaveMessage(null);
  }, []);

  const saveChartAccount = useCallback(async () => {
    if (!workspace || chartAccountSaveBusy) {
      return;
    }

    const nodeId = chartAccountDraft.nodeId.trim();
    const path = chartAccountDraft.path.trim();
    const accountName = chartAccountDraft.accountName.trim();
    const accountType = chartAccountDraft.accountType.trim();
    if (!nodeId || !path || !accountName || !accountType) {
      setChartAccountSaveError({
        summary: "Chart account is missing required fields.",
        details: ["Node id, path, account name, and account type are required before saving."]
      });
      setChartAccountSaveMessage(null);
      return;
    }

    setChartAccountSaveBusy(true);
    setChartAccountSaveError(null);
    setChartAccountSaveMessage(null);
    try {
      const evidenceLinks = parseNonEmptyLines(chartAccountDraft.evidenceText);
      const refreshed = await services.upsertChartNode({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        correlationId: `browser-chart-account-upsert-${nodeId}-${Date.now()}`,
        evidenceLinks: evidenceLinks.length > 0 ? evidenceLinks : null,
        node: {
          nodeId,
          path,
          accountName,
          accountType,
          parentPath: chartAccountDraft.parentPath.trim() || null,
          financialAccountId: chartAccountDraft.financialAccountId.trim() || null,
          isArchived: false
        }
      });
      setWorkspace(refreshed);
      setChartAccountSaveMessage(`Saved chart account ${path}.`);
      setChartAccountDraft(buildNextAccountingChartAccountDraft(refreshed));
      setSelectedRuleId((current) => {
        if (current && refreshed.postingRules.some((rule) => rule.ruleId === current && !rule.isArchived)) {
          return current;
        }

        return refreshed.postingRules.find((rule) => !rule.isArchived)?.ruleId ?? null;
      });
    } catch (err) {
      setChartAccountSaveError(describeApiError(err, "Chart account setup could not be saved."));
    } finally {
      setChartAccountSaveBusy(false);
    }
  }, [chartAccountDraft, chartAccountSaveBusy, services, workspace]);

  const runRuleTests = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const savedTestCases = workspace?.ruleTestCases ?? [];
    if (!workspace || (activeRules.length === 0 && savedTestCases.length === 0) || ruleTestBusy) {
      return;
    }

    setRuleTestBusy(true);
    setRuleTestError(null);
    try {
      const result = await services.runRuleTests({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        correlationId: `browser-accounting-rule-tests-${Date.now()}`,
        testCases: savedTestCases.length > 0
          ? null
          : activeRules.map((rule) => buildAccountingRuleTestCase(workspace, rule))
      });
      setRuleTestSuite(result);
    } catch (err) {
      setRuleTestError(describeApiError(err, "Accounting rule test cases failed."));
    } finally {
      setRuleTestBusy(false);
    }
  }, [ruleTestBusy, services, workspace]);

  const buildJournalCandidate = useCallback(async () => {
    if (!workspace || !dryRunPreview || journalCandidateBusy) {
      return;
    }

    const activeRules = workspace.postingRules.filter((rule) => !rule.isArchived);
    const selectedRule = activeRules.find((rule) => rule.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (!selectedRule) {
      setJournalCandidateError({ summary: "Dry-run did not select an active posting rule.", details: [] });
      return;
    }

    const mismatchReason = resolveDryRunRuleMismatchReason(dryRunPreview, selectedRule, "building a journal candidate");
    if (mismatchReason) {
      setJournalCandidateError({ summary: mismatchReason, details: [] });
      return;
    }

    const timestamp = Date.now();
    const correlationId = buildBrowserGuid(timestamp, 1);
    const evidenceLinks = [
      `browser://accounting/rules-studio/dry-run/${selectedRule.ruleId}`,
      `browser://accounting/rules-studio/journal-candidate/${selectedRule.ruleId}/${correlationId}`
    ];
    const request: PostingRuleJournalCandidateRequest = {
      fundProfileId: workspace.fundProfileId,
      ledgerBookId: workspace.ledgerBookId ?? null,
      sourceEventType: dryRunPreview.sourceEventType,
      eventAmount: dryRunPreview.eventAmount,
      currency: dryRunPreview.currency,
      effectiveDate: dryRunPreview.effectiveDate,
      actor: "browser-accounting-operator",
      aggregateId: buildBrowserGuid(timestamp, 2),
      periodId: buildBrowserGuid(timestamp, 3),
      accountingTimestamp: new Date(timestamp).toISOString(),
      description: `${selectedRule.displayName} governed journal draft candidate`,
      accountingBasis: resolveRuleAccountingBasis(workspace),
      dimensions: cloneLedgerDimensionSet(selectedRule.scope),
      counterpartyId: selectedRule.scope?.counterpartyId ?? null,
      instrumentSymbol: selectedRule.scope?.instrumentId ?? null,
      correlationId,
      sourceEventId: buildBrowserGuid(timestamp, 4),
      policyId: resolveRulePolicyId(workspace),
      treatmentKind: "General",
      postingKind: "Originating",
      evidenceLinks
    };

    setJournalCandidateBusy(true);
    setJournalCandidateError(null);
    try {
      const result = await services.buildJournalCandidate(request);
      setJournalCandidatePreview(result);
      if (result.selectedRuleId) {
        setSelectedRuleId(result.selectedRuleId);
      }
    } catch (err) {
      setJournalCandidateError(describeApiError(err, "Journal draft candidate build failed."));
    } finally {
      setJournalCandidateBusy(false);
    }
  }, [dryRunPreview, journalCandidateBusy, selectedRuleId, services, workspace]);

  const saveDryRunAsRuleTest = useCallback(async () => {
    if (!workspace || !dryRunPreview || saveRuleTestBusy) {
      return;
    }

    const selectedRuleId = dryRunPreview.selectedRuleId;
    if (!selectedRuleId) {
      setSaveRuleTestError({ summary: "Dry-run did not select a posting rule.", details: [] });
      return;
    }

    const selectedRule = workspace.postingRules.find((rule) => rule.ruleId === selectedRuleId) ?? null;
    const testCase: AccountingRuleTestCase = {
      testCaseId: `rule-test-${selectedRuleId}`,
      displayName: `${selectedRule?.displayName ?? selectedRuleId} retained dry-run regression`,
      request: {
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        sourceEventType: dryRunPreview.sourceEventType,
        eventAmount: dryRunPreview.eventAmount,
        currency: dryRunPreview.currency,
        effectiveDate: dryRunPreview.effectiveDate,
        actor: "browser-accounting-operator",
        dimensions: selectedRule?.scope ?? null,
        counterpartyId: selectedRule?.scope?.counterpartyId ?? null,
        instrumentSymbol: null,
        correlationId: `browser-accounting-rule-test-${selectedRuleId}`
      },
      expectedRuleId: selectedRuleId,
      expectedRuleVersion: selectedRule?.ruleVersion ?? null,
      expectBalancedPosting: dryRunPreview.isPostingBalanced,
      expectedIssueCodes: dryRunPreview.validationIssues.map((issue) => issue.code),
      expectedGeneratedPostingLines: (dryRunPreview.generatedPostingLines ?? []).map((line) => ({
        ...line,
        dimensions: cloneLedgerDimensionSet(line.dimensions)
      })),
      evidenceLinks: [
        `browser://accounting/rules-studio/dry-run/${selectedRuleId}`,
        `browser://accounting/rules-studio/test-case/${selectedRuleId}`
      ]
    };

    setSaveRuleTestBusy(true);
    setSaveRuleTestError(null);
    try {
      const next = await services.saveRuleTestCase({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        correlationId: `browser-accounting-rule-test-save-${Date.now()}`,
        evidenceLinks: testCase.evidenceLinks,
        testCase
      });
      setWorkspace(next);
    } catch (err) {
      setSaveRuleTestError(describeApiError(err, "Accounting rule test case could not be saved."));
    } finally {
      setSaveRuleTestBusy(false);
    }
  }, [dryRunPreview, saveRuleTestBusy, services, workspace]);

  const applyDryRunEventPredicate = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (!workspace || !rule || !dryRunPreview || applyEventPredicateBusy) {
      return;
    }

    const mismatchReason = resolveDryRunRuleMismatchReason(dryRunPreview, rule, "applying an event predicate");
    if (mismatchReason) {
      setApplyEventPredicateError({ summary: mismatchReason, details: [] });
      return;
    }

    const sourceEventConditionId = `${rule.ruleId}-source-event`;
    const existingConditions = rule.conditions ?? [];
    const eventPredicateRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.event`,
      conditions: [
        ...existingConditions
          .filter((condition) =>
            condition.conditionId !== sourceEventConditionId &&
            !(condition.field === "event.kind" && condition.operator === "Equals"))
          .map((condition) => ({ ...condition })),
        {
          conditionId: sourceEventConditionId,
          field: "event.kind",
          operator: "Equals",
          value: dryRunPreview.sourceEventType,
          secondValue: null,
          isRequired: true,
          description: `Source event predicate captured from dry-run ${dryRunPreview.effectiveDate}.`
        }
      ],
      scope: cloneLedgerDimensionSet(rule.scope),
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setApplyEventPredicateBusy(true);
    setApplyEventPredicateError(null);
    setEventPredicateRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: eventPredicateRule,
        correlationId: `browser-accounting-rule-event-predicate-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/event-predicate/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setEventPredicateRuleId(rule.ruleId);
    } catch (err) {
      setApplyEventPredicateError(describeApiError(err, "Posting rule event predicate could not be applied."));
    } finally {
      setApplyEventPredicateBusy(false);
    }
  }, [applyEventPredicateBusy, dryRunPreview, selectedRuleId, services, workspace]);

  const applyDryRunAmountThreshold = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (!workspace || !rule || !dryRunPreview || applyThresholdBusy) {
      return;
    }

    const mismatchReason = resolveDryRunRuleMismatchReason(dryRunPreview, rule, "applying a threshold");
    if (mismatchReason) {
      setApplyThresholdError({ summary: mismatchReason, details: [] });
      return;
    }

    const thresholdConditionId = `${rule.ruleId}-minimum-amount`;
    const existingConditions = rule.conditions ?? [];
    const thresholdRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.threshold`,
      conditions: [
        ...existingConditions
          .filter((condition) => condition.conditionId !== thresholdConditionId)
          .map((condition) => ({ ...condition })),
        {
          conditionId: thresholdConditionId,
          field: "event.amount",
          operator: "AmountGreaterThanOrEqual",
          value: String(dryRunPreview.eventAmount),
          secondValue: null,
          isRequired: true,
          description: `Minimum amount captured from dry-run ${dryRunPreview.effectiveDate}.`
        }
      ],
      scope: cloneLedgerDimensionSet(rule.scope),
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setApplyThresholdBusy(true);
    setApplyThresholdError(null);
    setThresholdRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: thresholdRule,
        correlationId: `browser-accounting-rule-threshold-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/threshold/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setThresholdRuleId(rule.ruleId);
    } catch (err) {
      setApplyThresholdError(describeApiError(err, "Posting rule threshold could not be applied."));
    } finally {
      setApplyThresholdBusy(false);
    }
  }, [applyThresholdBusy, dryRunPreview, selectedRuleId, services, workspace]);

  const applyDryRunEffectiveStart = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (!workspace || !rule || !dryRunPreview || applyEffectiveStartBusy) {
      return;
    }

    const mismatchReason = resolveDryRunRuleMismatchReason(dryRunPreview, rule, "applying an effective start date");
    if (mismatchReason) {
      setApplyEffectiveStartError({ summary: mismatchReason, details: [] });
      return;
    }

    const effectiveRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.effective`,
      effectiveFrom: dryRunPreview.effectiveDate,
      effectiveTo: rule.effectiveTo && rule.effectiveTo < dryRunPreview.effectiveDate ? null : rule.effectiveTo ?? null,
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      scope: cloneLedgerDimensionSet(rule.scope),
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setApplyEffectiveStartBusy(true);
    setApplyEffectiveStartError(null);
    setEffectiveStartRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: effectiveRule,
        correlationId: `browser-accounting-rule-effective-start-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/effective-start/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setEffectiveStartRuleId(rule.ruleId);
    } catch (err) {
      setApplyEffectiveStartError(describeApiError(err, "Posting rule effective start could not be applied."));
    } finally {
      setApplyEffectiveStartBusy(false);
    }
  }, [applyEffectiveStartBusy, dryRunPreview, selectedRuleId, services, workspace]);

  const applyDryRunScope = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    const generatedPostings = dryRunPreview?.generatedPostingLines ?? [];
    if (!workspace || !rule || !dryRunPreview || generatedPostings.length === 0 || applyScopeBusy) {
      return;
    }

    const mismatchReason = resolveDryRunRuleMismatchReason(dryRunPreview, rule, "applying dry-run scope");
    if (mismatchReason) {
      setApplyScopeError({ summary: mismatchReason, details: [] });
      return;
    }

    const nextScope = mergeRuleScopeFromGeneratedPostings(rule.scope, generatedPostings);
    const scopedRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.scope`,
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      scope: nextScope,
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setApplyScopeBusy(true);
    setApplyScopeError(null);
    setScopedRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: scopedRule,
        correlationId: `browser-accounting-rule-scope-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/scope/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setScopedRuleId(rule.ruleId);
    } catch (err) {
      setApplyScopeError(describeApiError(err, "Posting rule scope could not be applied."));
    } finally {
      setApplyScopeBusy(false);
    }
  }, [applyScopeBusy, dryRunPreview, selectedRuleId, services, workspace]);

  const captureDryRunGeneratedPostings = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    const generatedPostings = dryRunPreview?.generatedPostingLines ?? [];
    if (!workspace || !rule || !dryRunPreview || generatedPostings.length === 0 || capturePostingsBusy) {
      return;
    }

    const mismatchReason = resolveDryRunRuleMismatchReason(dryRunPreview, rule, "capturing generated postings");
    if (mismatchReason) {
      setCapturePostingsError({ summary: mismatchReason, details: [] });
      return;
    }

    const capturedRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.postings`,
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      scope: cloneLedgerDimensionSet(rule.scope),
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: generatedPostings.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })),
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setCapturePostingsBusy(true);
    setCapturePostingsError(null);
    setCapturedPostingsRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: capturedRule,
        correlationId: `browser-accounting-rule-generated-postings-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/generated-postings/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setCapturedPostingsRuleId(rule.ruleId);
    } catch (err) {
      setCapturePostingsError(describeApiError(err, "Generated posting lines could not be captured."));
    } finally {
      setCapturePostingsBusy(false);
    }
  }, [capturePostingsBusy, dryRunPreview, selectedRuleId, services, workspace]);

  const applyDryRunFormulaAmount = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    const formulaCandidate = rule && dryRunPreview
      ? resolveDryRunFormulaCandidate(rule, dryRunPreview)
      : null;
    if (!workspace || !rule || !dryRunPreview || !formulaCandidate || applyFormulaBusy) {
      return;
    }

    const mismatchReason = resolveDryRunRuleMismatchReason(dryRunPreview, rule, "applying formula amounts");
    if (mismatchReason) {
      setApplyFormulaError({ summary: mismatchReason, details: [] });
      return;
    }

    const formulaRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.formula`,
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      scope: cloneLedgerDimensionSet(rule.scope),
      formulas: (rule.formulas ?? []).map((formula) => formula.formulaId === formulaCandidate.formulaId
        ? {
            ...formula,
            value: formulaCandidate.amount,
            currency: formulaCandidate.currency,
            description: `Formula amount retained from dry-run ${dryRunPreview.effectiveDate}.`
          }
        : { ...formula }),
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setApplyFormulaBusy(true);
    setApplyFormulaError(null);
    setFormulaRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: formulaRule,
        correlationId: `browser-accounting-rule-formula-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/formula/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setFormulaRuleId(rule.ruleId);
    } catch (err) {
      setApplyFormulaError(describeApiError(err, "Posting rule formula amount could not be applied."));
    } finally {
      setApplyFormulaBusy(false);
    }
  }, [applyFormulaBusy, dryRunPreview, selectedRuleId, services, workspace]);

  const applyDryRunAllocationTargets = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    const nextAllocations = rule && dryRunPreview
      ? mergeAllocationTargetsFromGeneratedPostings(rule.allocations ?? [], dryRunPreview.generatedPostingLines ?? [])
      : null;
    if (!workspace || !rule || !dryRunPreview || !nextAllocations || applyAllocationBusy) {
      return;
    }

    const mismatchReason = resolveDryRunRuleMismatchReason(dryRunPreview, rule, "applying allocation targets");
    if (mismatchReason) {
      setApplyAllocationError({ summary: mismatchReason, details: [] });
      return;
    }

    const allocationRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.allocation`,
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      scope: cloneLedgerDimensionSet(rule.scope),
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: nextAllocations,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setApplyAllocationBusy(true);
    setApplyAllocationError(null);
    setAllocationRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: allocationRule,
        correlationId: `browser-accounting-rule-allocation-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/allocation/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setAllocationRuleId(rule.ruleId);
    } catch (err) {
      setApplyAllocationError(describeApiError(err, "Posting rule allocation targets could not be applied."));
    } finally {
      setApplyAllocationBusy(false);
    }
  }, [applyAllocationBusy, dryRunPreview, selectedRuleId, services, workspace]);

  const raiseSelectedRulePriority = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (!workspace || !rule || raisePriorityBusy) {
      return;
    }

    const prioritizedRule: PostingRule = {
      ...rule,
      ruleVersion: `${rule.ruleVersion}.priority`,
      priority: (rule.priority ?? 0) + 1,
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      scope: cloneLedgerDimensionSet(rule.scope),
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    const timestamp = Date.now();
    setRaisePriorityBusy(true);
    setRaisePriorityError(null);
    setRaisedPriorityRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: prioritizedRule,
        correlationId: `browser-accounting-rule-priority-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/priority/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setRaisedPriorityRuleId(rule.ruleId);
    } catch (err) {
      setRaisePriorityError(describeApiError(err, "Posting rule priority could not be raised."));
    } finally {
      setRaisePriorityBusy(false);
    }
  }, [raisePriorityBusy, selectedRuleId, services, workspace]);

  const duplicateSelectedRule = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (duplicateRuleBusy) {
      return;
    }

    if (!workspace) {
      setDuplicateRuleError({ summary: "Load accounting configuration before drafting a posting rule.", details: [] });
      return;
    }

    if (!rule) {
      setDuplicateRuleError({ summary: "Select an active posting rule before drafting a copy.", details: [] });
      return;
    }

    const timestamp = Date.now();
    const draftRuleId = `${rule.ruleId}-draft-${timestamp}`;
    const duplicatedRule: PostingRule = {
      ...rule,
      ruleId: draftRuleId,
      displayName: `${rule.displayName} draft`,
      description: rule.description
        ? `${rule.description} Drafted from ${rule.ruleId}.`
        : `Drafted from ${rule.ruleId}.`,
      ruleVersion: `${rule.ruleVersion}.draft`,
      isArchived: false,
      priority: (rule.priority ?? 0) + 1,
      scope: cloneLedgerDimensionSet(rule.scope),
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null,
      versions: [],
      promotionApproval: null,
      requiresPromotionApproval: true
    };

    setDuplicateRuleBusy(true);
    setDuplicateRuleError(null);
    setDuplicatedRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: duplicatedRule,
        correlationId: `browser-accounting-rule-duplicate-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/duplicate/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(draftRuleId);
      setDuplicatedRuleId(draftRuleId);
    } catch (err) {
      setDuplicateRuleError(describeApiError(err, "Posting rule could not be duplicated."));
    } finally {
      setDuplicateRuleBusy(false);
    }
  }, [duplicateRuleBusy, selectedRuleId, services, workspace]);

  const archiveSelectedRule = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (archiveRuleBusy) {
      return;
    }

    if (!workspace) {
      setArchiveRuleError({ summary: "Load accounting configuration before archiving a posting rule.", details: [] });
      return;
    }

    if (!rule) {
      setArchiveRuleError({ summary: "Select an active posting rule before archiving.", details: [] });
      return;
    }

    const archivedRule: PostingRule = {
      ...rule,
      isArchived: true,
      scope: cloneLedgerDimensionSet(rule.scope),
      conditions: rule.conditions?.map((condition) => ({ ...condition })) ?? null,
      formulas: rule.formulas?.map((formula) => ({ ...formula })) ?? null,
      allocations: rule.allocations?.map((allocation) => ({
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      })) ?? null,
      generatedPostings: rule.generatedPostings?.map((posting) => ({
        ...posting,
        dimensions: cloneLedgerDimensionSet(posting.dimensions)
      })) ?? null
    };

    const timestamp = Date.now();
    setArchiveRuleBusy(true);
    setArchiveRuleError(null);
    setArchivedRuleId(null);
    try {
      const next = await services.upsertRule({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        rule: archivedRule,
        correlationId: `browser-accounting-rule-archive-${timestamp}`,
        evidenceLinks: [`browser://accounting/rules-studio/archive/${rule.ruleId}`]
      });
      setWorkspace(next);
      setSelectedRuleId(next.postingRules.find((item) => !item.isArchived)?.ruleId ?? null);
      setDryRunPreview(null);
      setArchivedRuleId(rule.ruleId);
    } catch (err) {
      setArchiveRuleError(describeApiError(err, "Posting rule could not be archived."));
    } finally {
      setArchiveRuleBusy(false);
    }
  }, [archiveRuleBusy, selectedRuleId, services, workspace]);

  const approveRulePromotion = useCallback(async () => {
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const rule = activeRules.find((item) => item.ruleId === selectedRuleId) ?? activeRules[0] ?? null;
    if (approveRulePromotionBusy) {
      return;
    }

    if (!workspace) {
      setApproveRulePromotionError({ summary: "Load accounting configuration before approving rule promotion.", details: [] });
      return;
    }

    if (!rule) {
      setApproveRulePromotionError({ summary: "Select an active posting rule before approving promotion.", details: [] });
      return;
    }

    const studioRule = workspace.rulesStudio?.rules.find((item) => item.ruleId === rule.ruleId) ?? null;
    if (studioRule?.isPromotionApproved === true || isApprovedRulePromotion(rule.promotionApproval)) {
      setApproveRulePromotionError({ summary: "Selected posting rule already has an approved promotion.", details: [] });
      return;
    }

    const evidenceLink = `browser://accounting/rules-studio/promotion-review/${rule.ruleId}/${rule.ruleVersion}`;
    setApproveRulePromotionBusy(true);
    setApproveRulePromotionError(null);
    try {
      const next = await services.approveRulePromotion({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        ruleId: rule.ruleId,
        ruleVersion: rule.ruleVersion,
        actor: "browser-accounting-operator",
        approvalId: `browser-approval-${rule.ruleId}-${rule.ruleVersion}`,
        notes: `Approved ${rule.displayName} ${rule.ruleVersion} from Accounting Rules Studio.`,
        evidenceLinks: [evidenceLink],
        requestedBy: rule.promotionApproval?.requestedBy ?? "browser-accounting-operator",
        requestedAtUtc: rule.promotionApproval?.requestedAtUtc ?? new Date().toISOString(),
        correlationId: `browser-accounting-rule-promotion-${Date.now()}`
      });
      setWorkspace(next);
      setSelectedRuleId(rule.ruleId);
      setApprovedRulePromotionId(rule.ruleId);
    } catch (err) {
      setApproveRulePromotionError(describeApiError(err, "Posting rule promotion approval failed."));
    } finally {
      setApproveRulePromotionBusy(false);
    }
  }, [approveRulePromotionBusy, selectedRuleId, services, workspace]);

  const activate = useCallback(async () => {
    if (activateBusy) {
      return;
    }

    if (!workspace) {
      setActivateError({ summary: "Load accounting configuration before activation.", details: [] });
      return;
    }

    const disabledReason = resolveAccountingConfigurationActivationDisabledReason(workspace, ruleTestSuite);
    if (disabledReason) {
      setActivateError({ summary: disabledReason, details: [] });
      return;
    }

    setActivateBusy(true);
    setActivateError(null);
    try {
      const activated = await services.activate({
        fundProfileId: workspace.fundProfileId,
        ledgerBookId: workspace.ledgerBookId ?? null,
        actor: "browser-accounting-operator",
        correlationId: `browser-accounting-config-activate-${Date.now()}`,
        evidenceLinks: ["browser://accounting/configure"]
      });
      setWorkspace(activated);
    } catch (err) {
      setActivateError(describeApiError(err, "Accounting configuration activation failed."));
    } finally {
      setActivateBusy(false);
    }
  }, [activateBusy, ruleTestSuite, services, workspace]);

  return useMemo(() => {
    const issueCount = workspace?.validationIssues.length ?? 0;
    const criticalIssueCount = workspace?.validationIssues.filter((issue) => issue.severity === "Critical").length ?? 0;
    const activeTemplateCount = workspace?.journalTemplates.filter((item) => !item.isArchived).length ?? 0;
    const activeRuleCount = workspace?.postingRules.filter((item) => !item.isArchived).length ?? 0;
    const activeChartNodeCount = workspace?.chartOfAccounts.filter((item) => !item.isArchived).length ?? 0;
    const hasTemplate = activeTemplateCount > 0;
    const activeRules = workspace?.postingRules.filter((item) => !item.isArchived) ?? [];
    const savedRuleTestCases = workspace?.ruleTestCases ?? [];
    const studioSummary = workspace?.rulesStudio?.summary ?? null;
    const studioRuleRows = workspace?.rulesStudio?.rules ?? [];
    const studioRuleRowsById = new Map(studioRuleRows.map((row) => [row.ruleId, row]));
    const studioPromotionQueue = workspace?.rulesStudio?.promotionQueue ?? [];
    const studioPromotionQueueById = new Map(studioPromotionQueue.map((item) => [item.ruleId, item]));
    const resolvedSelectedRuleId = selectedRuleId && activeRules.some((rule) => rule.ruleId === selectedRuleId)
      ? selectedRuleId
      : activeRules[0]?.ruleId ?? null;
    const selectedPostingRule = activeRules.find((rule) => rule.ruleId === resolvedSelectedRuleId) ?? null;
    const selectedStudioRule = resolvedSelectedRuleId ? studioRuleRowsById.get(resolvedSelectedRuleId) ?? null : null;
    const dryRunDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before running rule previews."
        : !resolvedSelectedRuleId
          ? "Create at least one active posting rule before dry run preview."
          : null;
    const createLedgerBookDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before creating ledger-book setup."
        : !workspace.ledgerBookSetupCandidate
          ? "No server-provided ledger-book setup candidate is available for this configuration scope."
          : null;
    const chartAccountSaveDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before saving a chart account."
        : !chartAccountDraft.nodeId.trim()
          ? "Node id is required."
          : !chartAccountDraft.path.trim()
            ? "Account path is required."
            : !chartAccountDraft.accountName.trim()
              ? "Account name is required."
              : !chartAccountDraft.accountType.trim()
                ? "Account type is required."
                : null;
    const applyThresholdDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before applying a threshold."
        : !selectedPostingRule
          ? "Select an active posting rule before applying a threshold."
          : !dryRunPreview
            ? "Run a dry-run preview before applying an amount threshold."
            : dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
              ? "Dry-run preview must match the selected posting rule before applying a threshold."
              : null;
    const applyEventPredicateDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before applying an event predicate."
        : !selectedPostingRule
          ? "Select an active posting rule before applying an event predicate."
          : !dryRunPreview
            ? "Run a dry-run preview before applying an event predicate."
            : dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
              ? "Dry-run preview must match the selected posting rule before applying an event predicate."
              : null;
    const applyEffectiveStartDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before applying an effective start date."
        : !selectedPostingRule
          ? "Select an active posting rule before applying an effective start date."
          : !dryRunPreview
            ? "Run a dry-run preview before applying an effective start date."
            : dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
              ? "Dry-run preview must match the selected posting rule before applying an effective start date."
              : null;
    const applyScopeDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before applying dry-run scope."
        : !selectedPostingRule
          ? "Select an active posting rule before applying dry-run scope."
          : !dryRunPreview
            ? "Run a dry-run preview before applying dry-run scope."
            : dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
              ? "Dry-run preview must match the selected posting rule before applying dry-run scope."
              : (dryRunPreview.generatedPostingLines?.length ?? 0) === 0
                ? "Dry-run preview did not return generated posting-line dimensions."
                : ledgerDimensionSetsEqual(
                    selectedPostingRule.scope,
                    mergeRuleScopeFromGeneratedPostings(selectedPostingRule.scope, dryRunPreview.generatedPostingLines ?? [])
                  )
                  ? "Dry-run generated postings do not add new unambiguous rule-scope dimensions."
                  : null;
    const capturePostingsDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before capturing generated postings."
        : !selectedPostingRule
          ? "Select an active posting rule before capturing generated postings."
          : !dryRunPreview
            ? "Run a dry-run preview before capturing generated posting lines."
            : dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
              ? "Dry-run preview must match the selected posting rule before capturing generated postings."
              : (dryRunPreview.generatedPostingLines?.length ?? 0) === 0
                ? "Dry-run preview did not return generated posting-line metadata."
                : null;
    const formulaCandidate = selectedPostingRule && dryRunPreview
      ? resolveDryRunFormulaCandidate(selectedPostingRule, dryRunPreview)
      : null;
    const applyFormulaDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before applying formula amounts."
        : !selectedPostingRule
          ? "Select an active posting rule before applying formula amounts."
          : !dryRunPreview
            ? "Run a dry-run preview before applying formula amounts."
            : dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
              ? "Dry-run preview must match the selected posting rule before applying formula amounts."
              : !formulaCandidate
                ? "Dry-run generated postings did not reference a retained rule formula."
                : null;
    const allocationCandidate = selectedPostingRule && dryRunPreview
      ? mergeAllocationTargetsFromGeneratedPostings(selectedPostingRule.allocations ?? [], dryRunPreview.generatedPostingLines ?? [])
      : null;
    const applyAllocationDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before applying allocation targets."
        : !selectedPostingRule
          ? "Select an active posting rule before applying allocation targets."
          : !dryRunPreview
            ? "Run a dry-run preview before applying allocation targets."
            : dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
              ? "Dry-run preview must match the selected posting rule before applying allocation targets."
              : (selectedPostingRule.allocations?.length ?? 0) === 0
                ? "Selected posting rule has no allocation rules to update."
                : (dryRunPreview.generatedPostingLines?.length ?? 0) === 0
                  ? "Dry-run preview did not return generated posting-line dimensions."
                  : !allocationCandidate
                    ? "Dry-run generated postings do not add new allocation target dimensions."
                    : null;
    const raisePriorityDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before raising posting-rule priority."
        : !selectedPostingRule
          ? "Select an active posting rule before raising priority."
          : null;
    const ruleTestDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before running rule test cases."
        : activeRules.length === 0 && savedRuleTestCases.length === 0
          ? "Create at least one active posting rule or saved test case before running test cases."
          : null;
    const saveDryRunAsRuleTestDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before saving rule test cases."
        : !dryRunPreview
          ? "Run a dry-run preview before saving a regression test case."
          : !dryRunPreview.selectedRuleId
            ? "Dry-run preview must select a posting rule before saving."
            : null;
    const journalCandidateDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before building a journal candidate."
        : !selectedPostingRule
          ? "Select an active posting rule before building a journal candidate."
          : !dryRunPreview
            ? "Run a dry-run preview before building a journal candidate."
            : !dryRunPreview.selectedRuleId
              ? "Dry-run preview must select a posting rule before building a journal candidate."
              : dryRunPreview.selectedRuleId !== selectedPostingRule.ruleId
                ? "Dry-run preview must match the selected posting rule before building a journal candidate."
                : null;
    const duplicateRuleDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before drafting a posting rule."
        : !selectedPostingRule
          ? "Select an active posting rule before drafting a copy."
          : null;
    const archiveRuleDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before archiving a posting rule."
        : !selectedPostingRule
          ? "Select an active posting rule before archiving."
          : null;
    const approveRulePromotionDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before approving rule promotion."
        : !selectedPostingRule
          ? "Select an active posting rule before approving promotion."
          : selectedStudioRule?.isPromotionApproved === true || isApprovedRulePromotion(selectedPostingRule.promotionApproval)
            ? "Selected posting rule already has an approved promotion."
            : null;
    const previewDisabledReason = loading
      ? "Accounting configuration is still loading."
      : !workspace
        ? "Load accounting configuration before previewing a template."
        : !hasTemplate
          ? "Create at least one active journal template before preview."
          : null;
    const activateDisabledReason = loading
      ? "Accounting configuration is still loading."
      : resolveAccountingConfigurationActivationDisabledReason(workspace, ruleTestSuite);
    const statusTone: AccountingConfigurationViewModel["statusTone"] = !workspace
      ? "default"
      : criticalIssueCount > 0
        ? "danger"
        : issueCount > 0 || workspace.status === "Draft"
          ? "warning"
          : workspace.status === "Active"
            ? "success"
            : "default";
    const metricRows: AccountingConfigurationMetricViewModel[] = [
      {
        id: "books",
        label: "Books",
        value: String(workspace?.ledgerBooks.length ?? 0),
        detail: "Reuses registered ledger books and basis policies.",
        tone: (workspace?.ledgerBooks.length ?? 0) > 0 ? "success" : "warning"
      },
      {
        id: "chart",
        label: "Chart accounts",
        value: String(activeChartNodeCount),
        detail: "Hierarchical account paths available to templates.",
        tone: activeChartNodeCount > 0 ? "success" : "warning"
      },
      {
        id: "templates",
        label: "Templates",
        value: String(activeTemplateCount),
        detail: "Balanced journal templates eligible for preview.",
        tone: activeTemplateCount > 0 ? "success" : "warning"
      },
      {
        id: "rules",
        label: "Posting rules",
        value: String(studioSummary?.activeRules ?? activeRuleCount),
        detail: studioSummary
          ? `${studioSummary.generatedPostingRules} generated / ${studioSummary.templateMappingRules} template mappings.`
          : "Source events mapped to non-posting previews.",
        tone: activeRuleCount > 0 ? "success" : "warning"
      },
      {
        id: "audit",
        label: "Audit events",
        value: String(workspace?.auditTrail.length ?? 0),
        detail: "Append-only action evidence for configuration changes.",
        tone: (workspace?.auditTrail.length ?? 0) > 0 ? "success" : "warning"
      },
      {
        id: "rule-tests",
        label: "Rule tests",
        value: String(studioSummary?.savedTestCaseCount ?? savedRuleTestCases.length),
        detail: studioSummary
          ? `${studioSummary.rulesWithSavedRegressionTests} rule(s) covered; ${studioSummary.rulesMissingCurrentVersionRegressionTests} current version gap(s).`
          : "Persisted dry-run regression cases for posting-rule promotion.",
        tone: (studioSummary?.savedTestCaseCount ?? savedRuleTestCases.length) > 0 ? "success" : "warning"
      }
    ];
    const selectedLedgerBook = workspace?.ledgerBooks.find((book) => book.ledgerBookId === workspace.ledgerBookId) ?? null;
    const missingLedgerBookIssue = workspace?.validationIssues.find((issue) => issue.code === "configuration.ledger-book-missing") ?? null;
    const ledgerBookRows = (workspace?.ledgerBooks ?? [])
      .slice()
      .sort((a, b) => {
        const selectedDelta = Number(b.ledgerBookId === workspace?.ledgerBookId) - Number(a.ledgerBookId === workspace?.ledgerBookId);
        return selectedDelta !== 0 ? selectedDelta : a.displayName.localeCompare(b.displayName);
      })
      .map<AccountingLedgerBookAdminRowViewModel>((book) => {
        const isSelected = book.ledgerBookId === workspace?.ledgerBookId;
        return {
          id: book.ledgerBookId,
          title: book.displayName,
          subtitle: `${book.accountingBasis} basis | ${book.baseCurrency}`,
          scopeLabel: `${book.fundProfileId} / ${book.fundStructureNodeKind} ${book.fundStructureNodeId}`,
          policyLabel: `${book.accountingPolicyId}/${book.accountingPolicyVersion}`,
          currencyLabel: book.baseCurrency,
          updatedLabel: `Updated ${formatDateOnly(book.updatedAt)}`,
          description: book.description?.trim() || "No ledger-book description supplied.",
          statusLabel: isSelected ? "Selected" : "Available",
          tone: isSelected ? "success" : "default"
        };
      });
    const ledgerBookSummaryLabel = workspace
      ? `${ledgerBookRows.length.toLocaleString()} ledger book${ledgerBookRows.length === 1 ? "" : "s"} registered${selectedLedgerBook ? ` | selected ${selectedLedgerBook.displayName}` : workspace.ledgerBookId ? " | selected book missing from catalog" : " | fund-level scope"}.`
      : "Ledger-book catalog is not loaded.";
    const ledgerBookEmptyText = workspace && ledgerBookRows.length === 0
      ? "No registered ledger books are available in this configuration workspace."
      : null;
    const setupReadinessRows: AccountingConfigurationMetricViewModel[] = [
      {
        id: "selected-ledger-book",
        label: "Selected book",
        value: missingLedgerBookIssue
          ? "Missing"
          : selectedLedgerBook
            ? selectedLedgerBook.displayName
            : workspace?.ledgerBookId
              ? "Scoped"
              : "Fund scope",
        detail: missingLedgerBookIssue?.message ??
          (selectedLedgerBook
            ? `${selectedLedgerBook.accountingBasis} basis; policy ${selectedLedgerBook.accountingPolicyId}/${selectedLedgerBook.accountingPolicyVersion}.`
            : workspace?.ledgerBookId
              ? `Ledger book ${workspace.ledgerBookId} is selected; setup details are not loaded.`
              : "Configuration is using the fund-level setup scope."),
        tone: missingLedgerBookIssue ? "danger" : selectedLedgerBook ? "success" : workspace?.ledgerBookId ? "warning" : "default"
      },
      {
        id: "activation-readiness",
        label: "Activation readiness",
        value: criticalIssueCount > 0 ? "Blocked" : "Ready",
        detail: missingLedgerBookIssue?.suggestedAction ??
          (criticalIssueCount > 0
            ? `${criticalIssueCount} critical validation issue${criticalIssueCount === 1 ? "" : "s"} must be cleared.`
            : "No critical validation blockers are attached to this configuration."),
        tone: criticalIssueCount > 0 ? "danger" : "success"
      }
    ];
    const productionReadinessView = buildAccountingProductionReadinessViewModel(
      productionReadiness,
      migrationRunArtifacts,
      migrationWorkerPlans,
      productionReadinessLoading,
      productionReadinessError,
      workspace
    );
    const productionCertificationProfileView = buildAccountingProductionCertificationProfileEditorViewModel(
      productionCertificationProfile,
      productionCertificationDraft,
      productionCertificationProfileError,
      productionCertificationProfileSaveError,
      productionCertificationProfileSaveMessage,
      productionCertificationProfileSaveBusy,
      updateProductionCertificationControl,
      updateProductionCertificationEvidence,
      saveProductionCertificationProfile
    );
    const tenantAdministrationProfileView = buildAccountingTenantAdministrationProfileEditorViewModel(
      tenantAdministrationProfile,
      tenantAdministrationDraft,
      tenantAdministrationProfileError,
      tenantAdministrationProfileSaveError,
      tenantAdministrationProfileSaveMessage,
      tenantAdministrationProfileSaveBusy,
      sandboxProofBusy,
      sandboxProofError,
      sandboxProofMessage,
      updateTenantAdministrationControl,
      updateApprovalQueueSetup,
      updateDimensionMappingSetup,
      updateTenantAdministrationEvidence,
      retainImplementationSandboxProof,
      saveTenantAdministrationProfile
    );
    const externalGlMappingProfileView = buildAccountingExternalGlMappingProfileEditorViewModel(
      workspace,
      externalGlMappingProfiles,
      externalGlMappingProfileDraft,
      externalGlMappingProfileError,
      externalGlMappingProfileSaveError,
      externalGlMappingProfileSaveMessage,
      externalGlMappingProfileSaveBusy,
      updateExternalGlMappingProfileDraft,
      saveExternalGlMappingProfile
    );
    const templates = (workspace?.journalTemplates ?? []).map<AccountingConfigurationTemplateViewModel>((template) => {
      const debitTotal = template.lines.filter((line) => line.side === "Debit").reduce((sum, line) => sum + line.amount, 0);
      const creditTotal = template.lines.filter((line) => line.side === "Credit").reduce((sum, line) => sum + line.amount, 0);
      return {
        id: template.templateId,
        title: template.displayName,
        subtitle: `${template.version} | ${template.description || "No description supplied."}`,
        lineCountLabel: `${template.lines.length} line${template.lines.length === 1 ? "" : "s"}`,
        balanceLabel: `${formatCurrency(debitTotal)} debit / ${formatCurrency(creditTotal)} credit`,
        statusLabel: template.isArchived ? "Archived" : debitTotal === creditTotal ? "Balanced" : "Unbalanced"
      };
    });
    const rules = activeRules.map<AccountingRulesStudioRuleViewModel>((rule) =>
      buildAccountingRulesStudioRuleViewModel(
        rule,
        rule.ruleId === resolvedSelectedRuleId,
        savedRuleTestCases,
        ruleTestSuite,
        studioRuleRowsById.get(rule.ruleId) ?? null,
        studioPromotionQueueById.get(rule.ruleId) ?? null));
    const dryRunPreviewView = dryRunPreview
      ? buildAccountingRulesStudioDryRunViewModel(dryRunPreview)
      : null;
    const journalCandidatePreviewView = journalCandidatePreview
      ? buildAccountingRulesStudioJournalCandidateViewModel(journalCandidatePreview)
      : null;
    const ruleTestSuiteView = ruleTestSuite
      ? buildAccountingRulesStudioTestSuiteViewModel(ruleTestSuite)
      : null;
    const ruleTestCases = savedRuleTestCases.map(buildAccountingRulesStudioTestCaseViewModel);
    const validationIssues = (workspace?.validationIssues ?? []).map<AccountingConfigurationIssueViewModel>((issue, index) => ({
      id: `${issue.code}-${issue.targetId ?? index}`,
      label: `${issue.severity} | ${issue.code}`,
      message: issue.message,
      detail: issue.suggestedAction ?? issue.targetId ?? "No additional action supplied.",
      tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
    }));
    const auditTrail = (workspace?.auditTrail ?? []).slice(0, 8).map<AccountingConfigurationAuditViewModel>((event) => ({
      id: event.auditEventId,
      title: `${event.action} by ${event.actor}`,
      subtitle: `${event.recordedAtUtc} | ${event.correlationId ?? "no correlation id"}`,
      hashLabel: `${event.beforeHash.slice(0, 8)} -> ${event.afterHash.slice(0, 8)}`
    }));
    const previewView = preview
      ? {
          title: preview.displayName,
          balanceLabel: `${formatCurrency(preview.totalDebits)} debit / ${formatCurrency(preview.totalCredits)} credit`,
          statusLabel: preview.isBalanced ? "Balanced non-posting preview" : "Unbalanced preview",
          lineRows: preview.lines.map((line, index) => ({
            id: `${line.accountPath}-${line.side}-${index}`,
            account: `${line.accountPath} | ${line.accountName}`,
            side: line.side,
            amount: `${formatCurrency(line.amount)} ${line.currency}`,
            description: line.description ?? "Template line"
          }))
        }
      : null;
    const selectedDryRunRuleVersion = dryRunPreview
      ? workspace.postingRules.find((rule) => rule.ruleId === dryRunPreview.selectedRuleId)?.ruleVersion ?? "v1"
      : null;

    return {
      title: "Configure accounting",
      description: "Set up books, chart accounts, journal templates, posting rules, validation, and audit evidence before accounting actions are activated.",
      statusLabel: workspace ? `${workspace.status} ${workspace.configurationVersion}` : "Not loaded",
      statusTone,
      loading,
      errorText: error?.summary ?? null,
      errorDetails: error?.details ?? [],
      metricRows,
      setupReadinessRows,
      ledgerBookRows,
      ledgerBookSummaryLabel,
      ledgerBookEmptyText,
      chartAccountEditor: {
        nodeIdValue: chartAccountDraft.nodeId,
        pathValue: chartAccountDraft.path,
        accountNameValue: chartAccountDraft.accountName,
        accountTypeValue: chartAccountDraft.accountType,
        parentPathValue: chartAccountDraft.parentPath,
        financialAccountIdValue: chartAccountDraft.financialAccountId,
        evidenceValue: chartAccountDraft.evidenceText,
        saveButtonLabel: chartAccountSaveBusy ? "Saving chart account" : "Save chart account",
        saveDisabledReason: chartAccountSaveDisabledReason,
        statusText: chartAccountSaveError?.summary ?? chartAccountSaveMessage,
        saveBusy: chartAccountSaveBusy,
        canSave: chartAccountSaveDisabledReason === null && !chartAccountSaveBusy,
        updateDraft: updateChartAccountDraft,
        save: saveChartAccount
      },
      productionReadiness: productionReadinessView,
      productionCertificationProfile: productionCertificationProfileView,
      tenantAdministrationProfile: tenantAdministrationProfileView,
      externalGlMappingProfile: externalGlMappingProfileView,
      templates,
      rules,
      selectedRule: rules.find((rule) => rule.id === resolvedSelectedRuleId) ?? null,
      selectedRuleId: resolvedSelectedRuleId,
      selectRule: setSelectedRuleId,
      dryRunPreview: dryRunPreviewView,
      dryRunStatusText: dryRunError?.summary ?? (dryRunPreviewView ? dryRunPreviewView.balanceLabel : null),
      dryRunButtonLabel: dryRunBusy ? "Running dry run" : "Dry-run selected rule",
      dryRunDisabledReason,
      dryRunBusy,
      canDryRun: dryRunDisabledReason === null && !dryRunBusy,
      dryRunSelectedRule,
      createLedgerBookButtonLabel: createLedgerBookBusy ? "Creating ledger book" : "Create ledger book setup",
      createLedgerBookDisabledReason,
      createLedgerBookStatusText: createLedgerBookError?.summary ?? (createdLedgerBookName ? `Created ${createdLedgerBookName}.` : workspace?.ledgerBookSetupCandidate?.suggestedAction ?? null),
      createLedgerBookBusy,
      canCreateLedgerBook: createLedgerBookDisabledReason === null && !createLedgerBookBusy,
      createLedgerBookFromSetupCandidate,
      journalCandidatePreview: journalCandidatePreviewView,
      journalCandidateStatusText: journalCandidateError?.summary ?? (journalCandidatePreviewView ? journalCandidatePreviewView.commandLabel : null),
      journalCandidateButtonLabel: journalCandidateBusy ? "Building candidate" : "Build journal candidate",
      journalCandidateDisabledReason,
      journalCandidateBusy,
      canBuildJournalCandidate: journalCandidateDisabledReason === null && !journalCandidateBusy,
      buildJournalCandidate,
      applyEventPredicateButtonLabel: applyEventPredicateBusy ? "Applying event predicate" : "Apply event predicate",
      applyEventPredicateDisabledReason,
      applyEventPredicateStatusText: applyEventPredicateError?.summary ?? (eventPredicateRuleId ? `Applied event predicate to ${eventPredicateRuleId}.` : null),
      applyEventPredicateBusy,
      canApplyEventPredicate: applyEventPredicateDisabledReason === null && !applyEventPredicateBusy,
      applyDryRunEventPredicate,
      applyThresholdButtonLabel: applyThresholdBusy ? "Applying threshold" : "Apply amount threshold",
      applyThresholdDisabledReason,
      applyThresholdStatusText: applyThresholdError?.summary ?? (thresholdRuleId ? `Applied amount threshold to ${thresholdRuleId}.` : null),
      applyThresholdBusy,
      canApplyThreshold: applyThresholdDisabledReason === null && !applyThresholdBusy,
      applyDryRunAmountThreshold,
      applyEffectiveStartButtonLabel: applyEffectiveStartBusy ? "Applying effective date" : "Set effective start",
      applyEffectiveStartDisabledReason,
      applyEffectiveStartStatusText: applyEffectiveStartError?.summary ?? (effectiveStartRuleId ? `Applied effective start to ${effectiveStartRuleId}.` : null),
      applyEffectiveStartBusy,
      canApplyEffectiveStart: applyEffectiveStartDisabledReason === null && !applyEffectiveStartBusy,
      applyDryRunEffectiveStart,
      applyScopeButtonLabel: applyScopeBusy ? "Applying scope" : "Apply dry-run scope",
      applyScopeDisabledReason,
      applyScopeStatusText: applyScopeError?.summary ?? (scopedRuleId ? `Applied dry-run scope to ${scopedRuleId}.` : null),
      applyScopeBusy,
      canApplyScope: applyScopeDisabledReason === null && !applyScopeBusy,
      applyDryRunScope,
      capturePostingsButtonLabel: capturePostingsBusy ? "Capturing postings" : "Capture generated postings",
      capturePostingsDisabledReason,
      capturePostingsStatusText: capturePostingsError?.summary ?? (capturedPostingsRuleId ? `Captured generated postings for ${capturedPostingsRuleId}.` : null),
      capturePostingsBusy,
      canCapturePostings: capturePostingsDisabledReason === null && !capturePostingsBusy,
      captureDryRunGeneratedPostings,
      applyFormulaButtonLabel: applyFormulaBusy ? "Applying formula amount" : "Apply formula amount",
      applyFormulaDisabledReason,
      applyFormulaStatusText: applyFormulaError?.summary ?? (formulaRuleId ? `Applied formula amount to ${formulaRuleId}.` : null),
      applyFormulaBusy,
      canApplyFormula: applyFormulaDisabledReason === null && !applyFormulaBusy,
      applyDryRunFormulaAmount,
      applyAllocationButtonLabel: applyAllocationBusy ? "Applying allocation targets" : "Apply allocation targets",
      applyAllocationDisabledReason,
      applyAllocationStatusText: applyAllocationError?.summary ?? (allocationRuleId ? `Applied allocation targets to ${allocationRuleId}.` : null),
      applyAllocationBusy,
      canApplyAllocation: applyAllocationDisabledReason === null && !applyAllocationBusy,
      applyDryRunAllocationTargets,
      raisePriorityButtonLabel: raisePriorityBusy ? "Raising priority" : "Raise priority",
      raisePriorityDisabledReason,
      raisePriorityStatusText: raisePriorityError?.summary ?? (raisedPriorityRuleId ? `Raised priority for ${raisedPriorityRuleId}.` : null),
      raisePriorityBusy,
      canRaisePriority: raisePriorityDisabledReason === null && !raisePriorityBusy,
      raiseSelectedRulePriority,
      ruleTestSuite: ruleTestSuiteView,
      ruleTestStatusText: ruleTestError?.summary ?? (ruleTestSuiteView ? ruleTestSuiteView.summaryLabel : null),
      ruleTestButtonLabel: ruleTestBusy ? "Running rule tests" : "Run rule tests",
      ruleTestDisabledReason,
      ruleTestBusy,
      canRunRuleTests: ruleTestDisabledReason === null && !ruleTestBusy,
      runRuleTests,
      saveDryRunAsRuleTestButtonLabel: saveRuleTestBusy ? "Saving test case" : "Save dry-run as test case",
      saveDryRunAsRuleTestDisabledReason,
      saveDryRunAsRuleTestStatusText: saveRuleTestError?.summary ?? (dryRunPreview && savedRuleTestCases.some((testCase) =>
        testCase.expectedRuleId === dryRunPreview.selectedRuleId &&
        testCase.expectedRuleVersion === selectedDryRunRuleVersion) ? "Selected dry-run has a saved regression case." : null),
      saveDryRunAsRuleTestBusy: saveRuleTestBusy,
      canSaveDryRunAsRuleTest: saveDryRunAsRuleTestDisabledReason === null && !saveRuleTestBusy,
      saveDryRunAsRuleTest,
      duplicateRuleButtonLabel: duplicateRuleBusy ? "Duplicating rule" : "Duplicate as draft",
      duplicateRuleDisabledReason,
      duplicateRuleStatusText: duplicateRuleError?.summary ?? (duplicatedRuleId ? `Draft rule ${duplicatedRuleId} saved.` : null),
      duplicateRuleBusy,
      canDuplicateRule: duplicateRuleDisabledReason === null && !duplicateRuleBusy,
      duplicateSelectedRule,
      archiveRuleButtonLabel: archiveRuleBusy ? "Archiving rule" : "Archive rule",
      archiveRuleDisabledReason,
      archiveRuleStatusText: archiveRuleError?.summary ?? (archivedRuleId ? `Archived posting rule ${archivedRuleId}.` : null),
      archiveRuleBusy,
      canArchiveRule: archiveRuleDisabledReason === null && !archiveRuleBusy,
      archiveSelectedRule,
      approveRulePromotionButtonLabel: approveRulePromotionBusy ? "Approving rule" : "Approve rule promotion",
      approveRulePromotionDisabledReason,
      approveRulePromotionStatusText: approveRulePromotionError?.summary ?? (approvedRulePromotionId && selectedPostingRule?.ruleId === approvedRulePromotionId
        ? `Approved promotion for ${selectedPostingRule.displayName}.`
        : null),
      approveRulePromotionBusy,
      canApproveRulePromotion: approveRulePromotionDisabledReason === null && !approveRulePromotionBusy,
      approveRulePromotion,
      ruleTestCases,
      validationIssues,
      auditTrail,
      preview: previewView,
      previewStatusText: previewError?.summary ?? (preview ? previewView?.statusLabel ?? null : null),
      previewButtonLabel: previewBusy ? "Previewing" : "Preview first template",
      previewDisabledReason,
      previewBusy,
      canPreview: previewDisabledReason === null && !previewBusy,
      activateButtonLabel: activateBusy ? "Activating" : "Activate configuration",
      activateDisabledReason: activateError?.summary ?? activateDisabledReason,
      activateBusy,
      canActivate: activateDisabledReason === null && !activateBusy,
      activate,
      emptyText: loading ? "Loading accounting configuration." : "No accounting configuration records are available yet.",
      refresh,
      previewFirstTemplate
    };
  }, [
    activate,
    activateBusy,
    activateError,
    applyAllocationBusy,
    applyAllocationError,
    applyDryRunEventPredicate,
    applyDryRunEffectiveStart,
    applyDryRunAllocationTargets,
    applyDryRunAmountThreshold,
    applyDryRunFormulaAmount,
    applyDryRunScope,
    applyEventPredicateBusy,
    applyEventPredicateError,
    applyEffectiveStartBusy,
    applyEffectiveStartError,
    applyFormulaBusy,
    applyFormulaError,
    applyScopeBusy,
    applyScopeError,
    applyThresholdBusy,
    applyThresholdError,
    captureDryRunGeneratedPostings,
    capturePostingsBusy,
    capturePostingsError,
    capturedPostingsRuleId,
    createLedgerBookBusy,
    createLedgerBookError,
    createLedgerBookFromSetupCandidate,
    createdLedgerBookName,
    raisePriorityBusy,
    raisePriorityError,
    raiseSelectedRulePriority,
    raisedPriorityRuleId,
    archiveRuleBusy,
    archiveRuleError,
    archiveSelectedRule,
    archivedRuleId,
    allocationRuleId,
    duplicateRuleBusy,
    duplicateRuleError,
    duplicateSelectedRule,
    duplicatedRuleId,
    approveRulePromotion,
    approveRulePromotionBusy,
    approveRulePromotionError,
    approvedRulePromotionId,
    buildJournalCandidate,
    chartAccountDraft,
    chartAccountSaveBusy,
    chartAccountSaveError,
    chartAccountSaveMessage,
    dryRunBusy,
    dryRunError,
    dryRunPreview,
    dryRunSelectedRule,
    externalGlMappingProfileDraft,
    externalGlMappingProfileError,
    externalGlMappingProfileSaveBusy,
    externalGlMappingProfileSaveError,
    externalGlMappingProfileSaveMessage,
    externalGlMappingProfiles,
    error,
    eventPredicateRuleId,
    effectiveStartRuleId,
    formulaRuleId,
    journalCandidateBusy,
    journalCandidateError,
    journalCandidatePreview,
    loading,
    preview,
    previewBusy,
    previewError,
    productionReadiness,
    migrationRunArtifacts,
    migrationWorkerPlans,
    productionReadinessError,
    productionReadinessLoading,
    productionCertificationDraft,
    productionCertificationProfile,
    productionCertificationProfileError,
    productionCertificationProfileSaveBusy,
    productionCertificationProfileSaveError,
    productionCertificationProfileSaveMessage,
    refresh,
    previewFirstTemplate,
    ruleTestBusy,
    ruleTestError,
    ruleTestSuite,
    runRuleTests,
    saveDryRunAsRuleTest,
    saveExternalGlMappingProfile,
    saveRuleTestBusy,
    saveRuleTestError,
    saveProductionCertificationProfile,
    saveTenantAdministrationProfile,
    retainImplementationSandboxProof,
    sandboxProofBusy,
    sandboxProofError,
    sandboxProofMessage,
    selectedRuleId,
    scopedRuleId,
    tenantAdministrationDraft,
    tenantAdministrationProfile,
    tenantAdministrationProfileError,
    tenantAdministrationProfileSaveBusy,
    tenantAdministrationProfileSaveError,
    tenantAdministrationProfileSaveMessage,
    thresholdRuleId,
    updateProductionCertificationControl,
    updateProductionCertificationEvidence,
    updateTenantAdministrationControl,
    updateDimensionMappingSetup,
    updateTenantAdministrationEvidence,
    updateChartAccountDraft,
    updateExternalGlMappingProfileDraft,
    saveChartAccount,
    workspace
  ]);
}

function resolveAccountingConfigurationActivationDisabledReason(
  workspace: AccountingConfigurationWorkspace | null,
  ruleTestSuite: AccountingRuleTestSuiteResult | null
): string | null {
  if (!workspace) {
    return "Load accounting configuration before activation.";
  }

  if (workspace.status === "Active") {
    return "Accounting configuration is already active.";
  }

  const criticalIssueCount = workspace.validationIssues.filter((issue) => issue.severity === "Critical").length;
  if (criticalIssueCount > 0) {
    return "Resolve critical validation issues before activation.";
  }

  const activeChartNodeCount = workspace.chartOfAccounts.filter((item) => !item.isArchived).length;
  if (activeChartNodeCount === 0) {
    return "Create at least one active chart account before activation.";
  }

  const activeTemplateCount = workspace.journalTemplates.filter((item) => !item.isArchived).length;
  if (activeTemplateCount === 0) {
    return "Create at least one active journal template before activation.";
  }

  const activeRules = workspace.postingRules.filter((item) => !item.isArchived);
  if (activeRules.length === 0) {
    return "Create at least one active posting rule before activation.";
  }

  const pendingPromotionApprovalRuleCount = workspace.rulesStudio?.summary.pendingPromotionApprovalRules ??
    activeRules.filter((rule) => rule.requiresPromotionApproval === true && !isApprovedRulePromotion(rule.promotionApproval)).length;
  if (pendingPromotionApprovalRuleCount > 0) {
    return `Approve promotion for ${pendingPromotionApprovalRuleCount} required posting rule${pendingPromotionApprovalRuleCount === 1 ? "" : "s"} before activation.`;
  }

  const savedRuleTestExpectedRuleVersions = new Set((workspace.ruleTestCases ?? [])
    .filter((testCase) => Boolean(testCase.expectedRuleId) && Boolean(testCase.expectedRuleVersion))
    .map((testCase) => `${testCase.expectedRuleId}:${testCase.expectedRuleVersion}`));
  const missingRegressionRuleCount = workspace.rulesStudio?.summary.rulesMissingCurrentVersionRegressionTests ??
    activeRules.filter((rule) =>
      rule.requiresPromotionApproval === true &&
      !savedRuleTestExpectedRuleVersions.has(`${rule.ruleId}:${rule.ruleVersion ?? "v1"}`)).length;
  if (missingRegressionRuleCount > 0) {
    return `Save regression test cases for ${missingRegressionRuleCount} promotion-gated posting rule${missingRegressionRuleCount === 1 ? "" : "s"} before activation.`;
  }

  if ((ruleTestSuite?.failedCount ?? 0) > 0) {
    return "Resolve failing rule test cases before activation.";
  }

  return null;
}

function resolveDryRunRuleMismatchReason(
  dryRunPreview: RuleDryRunResult,
  selectedRule: PostingRule,
  action: string
): string | null {
  return dryRunPreview.selectedRuleId && dryRunPreview.selectedRuleId !== selectedRule.ruleId
    ? `Dry-run preview must match the selected posting rule before ${action}.`
    : null;
}

function buildDefaultAccountingChartAccountDraft(): AccountingChartAccountDraft {
  return {
    nodeId: "assets-cash-operating",
    path: "Assets:Cash:Operating",
    accountName: "Operating Cash",
    accountType: "Asset",
    parentPath: "Assets:Cash",
    financialAccountId: "",
    evidenceText: "browser://accounting/configure/chart-account/setup"
  };
}

function buildNextAccountingChartAccountDraft(workspace: AccountingConfigurationWorkspace): AccountingChartAccountDraft {
  const nextIndex = workspace.chartOfAccounts.filter((node) => !node.isArchived).length + 1;
  return {
    nodeId: `chart-account-${nextIndex}`,
    path: `Assets:Configured Account ${nextIndex}`,
    accountName: `Configured Account ${nextIndex}`,
    accountType: "Asset",
    parentPath: "Assets",
    financialAccountId: "",
    evidenceText: `browser://accounting/configure/chart-account/${nextIndex}`
  };
}

function parseNonEmptyLines(value: string): string[] {
  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

function cloneLedgerDimensionSet(dimensions: PostingRule["scope"]): PostingRule["scope"] {
  if (!dimensions) {
    return dimensions ?? null;
  }

  const cloned = { ...dimensions };
  if (dimensions.externalGlDimensions) {
    cloned.externalGlDimensions = { ...dimensions.externalGlDimensions };
  }

  return cloned;
}

function buildAccountingExternalGlMappingProfileEditorViewModel(
  workspace: AccountingConfigurationWorkspace | null,
  profiles: ExternalGlMappingProfile[],
  draft: AccountingExternalGlMappingProfileDraft | null,
  loadError: ApiErrorDisplay | null,
  saveError: ApiErrorDisplay | null,
  saveMessage: string | null,
  saveBusy: boolean,
  updateDraft: (patch: Partial<AccountingExternalGlMappingProfileDraft>) => void,
  save: () => Promise<void>
): AccountingExternalGlMappingProfileEditorViewModel {
  const accountMappingCount = draft ? Object.keys(parseExternalGlAccountMappings(draft.accountMappingsText)).length : 0;
  const missingReason = !workspace
    ? "Load Accounting Configure before saving provider mappings."
    : !draft
      ? "Mapping profile draft is not available."
      : !draft.providerId.trim()
        ? "Provider id is required."
        : !draft.profileId.trim()
          ? "Mapping profile id is required."
          : !draft.displayName.trim()
            ? "Display name is required."
            : accountMappingCount === 0
              ? "At least one account mapping is required."
              : splitAccountingEvidenceReferences(draft.evidenceText).length === 0
                ? "Retained mapping approval evidence is required."
                : null;
  const mappingRows = profiles.map<AccountingTenantAdministrationControlViewModel>((profile) => ({
    id: profile.profileId,
    label: `${profile.displayName} (${profile.providerId})`,
    statusLabel: `${profile.certificationState}; ${Object.keys(profile.accountMappings ?? {}).length} account map${Object.keys(profile.accountMappings ?? {}).length === 1 ? "" : "s"}`,
    tone: profile.certificationState === "Certified" ? "success" : profile.certificationState === "Rejected" ? "danger" : "warning"
  }));

  return {
    title: "External GL provider mapping profile",
    scopeLabel: workspace
      ? `Fund ${workspace.fundProfileId} | ledger book ${workspace.ledgerBookId ?? "not selected"}`
      : "Accounting scope not loaded",
    providerIdValue: draft?.providerId ?? "",
    profileIdValue: draft?.profileId ?? "",
    displayNameValue: draft?.displayName ?? "",
    accountMappingsValue: draft?.accountMappingsText ?? "",
    meridianDimensionsValue: draft?.meridianDimensionsText ?? "",
    externalDimensionsValue: draft?.externalDimensionsText ?? "",
    evidenceValue: draft?.evidenceText ?? "",
    certified: draft?.certified ?? false,
    mappingRows,
    saveButtonLabel: saveBusy ? "Saving mapping" : "Save provider mapping",
    saveDisabledReason: missingReason,
    saveBusy,
    canSave: missingReason === null && !saveBusy,
    statusText: saveError?.summary ?? saveMessage ?? loadError?.summary ?? null,
    errorText: saveError?.summary ?? loadError?.summary ?? null,
    errorDetails: saveError?.details ?? loadError?.details ?? [],
    updateProviderId: (value) => updateDraft({ providerId: value }),
    updateProfileId: (value) => updateDraft({ profileId: value }),
    updateDisplayName: (value) => updateDraft({ displayName: value }),
    updateAccountMappings: (value) => updateDraft({ accountMappingsText: value }),
    updateMeridianDimensions: (value) => updateDraft({ meridianDimensionsText: value }),
    updateExternalDimensions: (value) => updateDraft({ externalDimensionsText: value }),
    updateEvidence: (value) => updateDraft({ evidenceText: value }),
    updateCertified: (checked) => updateDraft({ certified: checked }),
    save
  };
}

function buildAccountingExternalGlMappingProfileDraft(
  workspace: AccountingConfigurationWorkspace,
  profile: ExternalGlMappingProfile | null
): AccountingExternalGlMappingProfileDraft {
  const providerId = profile?.providerId?.trim() || "quickbooks-fixture";
  const profileId = profile?.profileId?.trim() || `${providerId}-${workspace.fundProfileId}-ledger-book-mapping`;
  const accountMappings = Object.entries(profile?.accountMappings ?? {});
  const retainedDimensionMapping = profile?.dimensionMappings?.[0] ?? null;
  return {
    providerId,
    profileId,
    displayName: profile?.displayName?.trim() || `${workspace.fundProfileId} external GL mapping`,
    accountMappingsText: accountMappings.length > 0
      ? accountMappings.map(([accountPath, externalAccountId]) => `${accountPath}=${externalAccountId}`).join("\n")
      : "Assets:Cash:Operating=qbo-1000\nIncome:Investment Income=qbo-4000\nExpenses:Operating Expenses=qbo-6000",
    meridianDimensionsText: formatExternalGlLedgerDimensionSet(
      retainedDimensionMapping?.meridianDimensions ?? buildDefaultMeridianExternalGlDimensions(providerId, profileId, workspace)
    ),
    externalDimensionsText: formatExternalGlLedgerDimensionSet(
      retainedDimensionMapping?.externalDimensions ?? buildDefaultProviderExternalGlDimensions(workspace)
    ),
    evidenceText: `approval:external-gl-mapping:${profileId}`,
    certified: profile?.certificationState === "Certified"
  };
}

function buildDefaultMeridianExternalGlDimensions(
  providerId: string,
  profileId: string,
  workspace: AccountingConfigurationWorkspace
): LedgerDimensionSet {
  return {
    fundId: workspace.fundProfileId,
    entityId: workspace.ledgerBookSetupCandidate?.fundStructureNodeId ?? workspace.fundProfileId,
    bookId: workspace.ledgerBookId ?? null,
    costCenterId: "fund-accounting",
    externalGlDimensions: {
      Provider: providerId,
      MappingProfile: profileId
    }
  };
}

function buildDefaultProviderExternalGlDimensions(workspace: AccountingConfigurationWorkspace): LedgerDimensionSet {
  const ledgerBookId = workspace.ledgerBookId ?? null;
  return {
    fundId: workspace.fundProfileId,
    entityId: workspace.ledgerBookSetupCandidate?.fundStructureNodeId ?? workspace.fundProfileId,
    bookId: ledgerBookId ? `Book:${ledgerBookId}` : null,
    costCenterId: "FundAccounting",
    externalGlDimensions: {
      Class: workspace.fundProfileId,
      Book: ledgerBookId ?? "fund-scope"
    }
  };
}

function parseExternalGlAccountMappings(value: string): Record<string, string> {
  const mappings: Record<string, string> = {};
  for (const line of value.split(/\r?\n/)) {
    const trimmed = line.trim();
    if (!trimmed) {
      continue;
    }

    const separatorIndex = trimmed.indexOf("=");
    if (separatorIndex <= 0 || separatorIndex >= trimmed.length - 1) {
      continue;
    }

    const key = trimmed.slice(0, separatorIndex).trim();
    const mapped = trimmed.slice(separatorIndex + 1).trim();
    if (key && mapped) {
      mappings[key] = mapped;
    }
  }

  return mappings;
}

type LedgerDimensionScalarKey = Exclude<keyof LedgerDimensionSet, "externalGlDimensions">;

const LEDGER_DIMENSION_KEYS = new Map<string, LedgerDimensionScalarKey>([
  ["fundid", "fundId"],
  ["entityid", "entityId"],
  ["sleeveid", "sleeveId"],
  ["strategyid", "strategyId"],
  ["investorid", "investorId"],
  ["capitalaccountid", "capitalAccountId"],
  ["instrumentid", "instrumentId"],
  ["taxlotid", "taxLotId"],
  ["costcenterid", "costCenterId"],
  ["counterpartyid", "counterpartyId"],
  ["organizationid", "organizationId"],
  ["portfolioid", "portfolioId"],
  ["bookid", "bookId"],
  ["accountid", "accountId"],
  ["customerid", "customerId"],
  ["vendorid", "vendorId"],
  ["projectid", "projectId"]
]);

const LEDGER_DIMENSION_FORMAT_KEYS: LedgerDimensionScalarKey[] = [
  "fundId",
  "entityId",
  "sleeveId",
  "strategyId",
  "investorId",
  "capitalAccountId",
  "instrumentId",
  "taxLotId",
  "costCenterId",
  "counterpartyId",
  "organizationId",
  "portfolioId",
  "bookId",
  "accountId",
  "customerId",
  "vendorId",
  "projectId"
];

function parseLedgerDimensionSet(value: string, defaults: LedgerDimensionSet): LedgerDimensionSet {
  const dimensions: LedgerDimensionSet = { ...defaults, externalGlDimensions: { ...(defaults.externalGlDimensions ?? {}) } };
  for (const line of value.split(/\r?\n/)) {
    const trimmed = line.trim();
    if (!trimmed) {
      continue;
    }

    const separatorIndex = trimmed.indexOf("=");
    if (separatorIndex <= 0 || separatorIndex >= trimmed.length - 1) {
      continue;
    }

    const rawKey = trimmed.slice(0, separatorIndex).trim();
    const mapped = trimmed.slice(separatorIndex + 1).trim();
    if (!rawKey || !mapped) {
      continue;
    }

    const normalizedKey = rawKey.replace(/[^a-z0-9]/gi, "").toLowerCase();
    const dimensionKey = LEDGER_DIMENSION_KEYS.get(normalizedKey);
    if (dimensionKey) {
      dimensions[dimensionKey] = mapped;
    } else {
      dimensions.externalGlDimensions = { ...(dimensions.externalGlDimensions ?? {}), [rawKey]: mapped };
    }
  }

  return dimensions;
}

function formatExternalGlLedgerDimensionSet(dimensions: LedgerDimensionSet): string {
  const entries: string[] = [];
  for (const key of LEDGER_DIMENSION_FORMAT_KEYS) {
    const value = dimensions[key];
    if (typeof value === "string" && value.trim()) {
      entries.push(`${key}=${value.trim()}`);
    }
  }

  for (const [key, value] of Object.entries(dimensions.externalGlDimensions ?? {}).sort(([left], [right]) => left.localeCompare(right))) {
    if (value?.trim()) {
      entries.push(`${key}=${value.trim()}`);
    }
  }

  return entries.join("\n");
}

function buildExternalGlDimensionMappingProfile(
  providerId: string,
  profileId: string,
  meridianDimensions: LedgerDimensionSet,
  externalDimensions: LedgerDimensionSet,
  certified: boolean
): ExternalGlMappingProfile["dimensionMappings"][number] {
  return {
    profileId: `${profileId}-dimension-scope`,
    providerId,
    displayName: "Canonical fund and ledger-book dimension scope",
    meridianDimensions,
    externalDimensions,
    certificationState: certified ? "Certified" : "ReadyForReview",
    validationIssues: []
  };
}

function withExternalGlMappingProfileEvidence(
  retainedEvidence: string[],
  providerId: string,
  fundProfileId: string,
  profileId: string,
  ledgerBookId: string | null
): string[] {
  return [...retainedEvidence,
    `approval:external-gl-mapping:${profileId}`,
    `evidence://external-gl/mapping-certification/provider/${providerId}/fund/${fundProfileId}/profile/${profileId}`,
    ledgerBookId ? `evidence://ledger-book/${ledgerBookId}/external-gl/mapping-certification/${profileId}` : null
  ]
    .filter((value): value is string => Boolean(value && value.trim()))
    .map((value) => value.trim())
    .filter((value, index, values) => values.findIndex((candidate) => candidate.toLowerCase() === value.toLowerCase()) === index)
    .sort((left, right) => left.localeCompare(right));
}

function buildAccountingProductionReadinessViewModel(
  readiness: AccountingProductionReadiness | null,
  retainedMigrationRunArtifacts: AccountingMigrationRunArtifact[],
  retainedMigrationWorkerPlans: AccountingMigrationRunWorkerPlan[],
  loading: boolean,
  error: ApiErrorDisplay | null,
  workspace: AccountingConfigurationWorkspace | null
): AccountingProductionReadinessViewModel {
  const artifactRows = buildAccountingMigrationRunArtifactRows(retainedMigrationRunArtifacts);
  const workerPlanRows = buildAccountingMigrationWorkerPlanRows(retainedMigrationWorkerPlans);
  if (!readiness) {
    return {
      title: "Accounting production readiness",
      statusLabel: loading ? "Assessing" : "Unavailable",
      scoreLabel: loading ? "..." : "No score",
      generatedAtLabel: loading ? "Refreshing assessment" : "No readiness assessment loaded.",
      scopeLabel: workspace?.ledgerBookId
        ? `Ledger book ${workspace.ledgerBookId}`
        : workspace?.fundProfileId
          ? `Fund ${workspace.fundProfileId}`
          : "Accounting scope not loaded",
      issueSummaryLabel: error?.summary ?? "Load accounting configuration to assess rollout readiness.",
      externalGlLabel: "External GL posture unavailable",
      ledgerBookRolloutLabel: "Ledger-book rollout unavailable",
      dimensionalReportingLabel: "Dimensional reporting unavailable",
      dimensionalReportingEvidenceLabel: "No dimensional reporting evidence loaded",
      tenantAdministrationLabel: "Tenant administration unavailable",
      tenantAdministrationEvidenceLabel: "No tenant setup evidence loaded",
      tenantAdministrationControls: [],
      migrationPlanRows: [],
      migrationArtifactSummaryLabel: artifactRows.length > 0
        ? `${artifactRows.length} retained migration run artifact${artifactRows.length === 1 ? "" : "s"} loaded`
        : "No retained migration run artifacts loaded",
      migrationArtifactRows: artifactRows,
      migrationWorkerPlanSummaryLabel: workerPlanRows.length > 0
        ? `${workerPlanRows.length} retained migration worker plan${workerPlanRows.length === 1 ? "" : "s"} loaded`
        : "No retained migration worker plans loaded",
      migrationWorkerPlanRows: workerPlanRows,
      productionGapRows: [],
      components: [],
      blockerIssues: [],
      loading,
      errorText: error?.summary ?? null,
      errorDetails: error?.details ?? []
    };
  }

  const criticalCount = readiness.criticalIssueCount ?? readiness.issues.filter((issue) => issue.severity === "Critical").length;
  const warningCount = readiness.warningIssueCount ?? readiness.issues.filter((issue) => issue.severity === "Warning").length;
  const blockerIssues = readiness.issues
    .filter((issue) => issue.severity === "Critical" || issue.severity === "Warning")
    .slice(0, 6)
    .map<AccountingProductionReadinessIssueViewModel>((issue) => ({
      id: `${issue.area}-${issue.code}`,
      label: `${formatProductionReadinessArea(issue.area)} | ${issue.severity}`,
      message: issue.message,
      suggestedAction: issue.suggestedAction,
      evidenceLabel: `${issue.evidenceReferences.length} evidence reference${issue.evidenceReferences.length === 1 ? "" : "s"}`,
      tone: issue.severity === "Critical" ? "danger" : "warning"
    }));
  const retainedArtifacts = retainedMigrationRunArtifacts.length > 0
    ? retainedMigrationRunArtifacts
    : readiness.migrationRunArtifacts ?? [];
  const migrationArtifactRows = buildAccountingMigrationRunArtifactRows(retainedArtifacts);
  const migrationWorkerPlanRows = buildAccountingMigrationWorkerPlanRows(retainedMigrationWorkerPlans);
  const migrationPlanRows = buildAccountingMigrationRolloutPlanRows(readiness.migrationRolloutPlan ?? []);
  const certifiedArtifactCount = retainedArtifacts.filter((artifact) => artifact.status === "Certified").length;
  const failedArtifactCount = retainedArtifacts.filter((artifact) => artifact.status === "Failed").length;
  const reconciledWorkerPlanCount = retainedMigrationWorkerPlans.filter((plan) => plan.sourceRecordCount === plan.migratedRecordCount).length;
  const tenantAdministration = readiness.tenantAdministration ?? null;
  const dimensionalReporting = readiness.dimensionalReporting ?? null;
  const dimensionalReportingLabel = dimensionalReporting
    ? `${dimensionalReporting.completedControlCount}/${dimensionalReporting.requiredControlCount} ledger/query/report/export dimension controls | ledger book ${dimensionalReporting.ledgerBookId?.trim() || "missing"}`
    : "Dimensional reporting evidence unavailable";
  const dimensionalReportingEvidenceLabel = dimensionalReporting
    ? `${dimensionalReporting.evidenceReferences.length} retained dimensional evidence reference${dimensionalReporting.evidenceReferences.length === 1 ? "" : "s"}`
    : "No dimensional reporting evidence loaded";
  const tenantAdministrationControls = buildAccountingTenantAdministrationControls(tenantAdministration);
  const tenantAdministrationLabel = tenantAdministration
    ? `${tenantAdministration.completedControlCount}/${tenantAdministration.requiredControlCount} admin controls | tenant ${tenantAdministration.tenantId?.trim() || "missing"} | company ${tenantAdministration.companyId?.trim() || "missing"}`
    : "Tenant administration evidence unavailable";
  const tenantAdministrationEvidenceLabel = tenantAdministration
    ? `${tenantAdministration.evidenceReferences.length} retained setup evidence reference${tenantAdministration.evidenceReferences.length === 1 ? "" : "s"}`
    : "No tenant setup evidence loaded";
  const productionGapRows = (readiness.productionGaps ?? []).map<AccountingProductionGapViewModel>((gap) => ({
    id: gap.code,
    label: gap.label,
    statusLabel: formatProductionReadinessStatus(gap.status),
    severityLabel: gap.highestSeverity,
    summary: gap.summary,
    requiredAction: gap.requiredAction,
    areaLabel: gap.areas.length > 0
      ? gap.areas.map(formatProductionReadinessArea).join(", ")
      : "No areas",
    blockingIssueLabel: gap.blockingIssueCodes.length > 0
      ? gap.blockingIssueCodes.join(", ")
      : "No blocking issue codes",
    issueDetailLabel: (gap.issues ?? []).length > 0
      ? (gap.issues ?? []).map((issue) => `${issue.code}: ${issue.message} -> ${issue.suggestedAction}`).join(" | ")
      : "No blocking issue details",
    routeLabel: gap.routes.length > 0
      ? gap.routes.join(", ")
      : "No routes",
    tone: productionReadinessStatusTone(gap.status)
  }));

  return {
    title: "Accounting production readiness",
    statusLabel: formatProductionReadinessStatus(readiness.status),
    scoreLabel: `${readiness.score}/100`,
    generatedAtLabel: `Generated ${formatDateTimeLabel(readiness.generatedAtUtc)}`,
    scopeLabel: readiness.ledgerBookId
      ? `Fund ${readiness.fundProfileId} | Ledger book ${readiness.ledgerBookId}`
      : `Fund ${readiness.fundProfileId} | Fund-level assessment`,
    issueSummaryLabel: criticalCount > 0
      ? `${criticalCount} critical blocker${criticalCount === 1 ? "" : "s"} and ${warningCount} warning${warningCount === 1 ? "" : "s"}`
      : warningCount > 0
        ? `${warningCount} warning${warningCount === 1 ? " requires" : "s require"} review`
        : "No critical production-readiness blockers",
    externalGlLabel: `${readiness.externalGlProviderCount} provider${readiness.externalGlProviderCount === 1 ? "" : "s"} | ${readiness.certifiedExternalGlMappingProfileCount} certified mapping${readiness.certifiedExternalGlMappingProfileCount === 1 ? "" : "s"} | live posting ${readiness.externalGlLivePostingEnabled ? "enabled" : "disabled"}`,
    ledgerBookRolloutLabel: readiness.ledgerBookRollout
      ? `${readiness.ledgerBookRollout.bookCount} book${readiness.ledgerBookRollout.bookCount === 1 ? "" : "s"} | ${readiness.ledgerBookRollout.openPeriodCount} open period${readiness.ledgerBookRollout.openPeriodCount === 1 ? "" : "s"} | ${readiness.ledgerBookRollout.criticalIssueCount} rollout blocker${readiness.ledgerBookRollout.criticalIssueCount === 1 ? "" : "s"} | ${readiness.ledgerBookWorkflows?.completedControlCount ?? 0}/${readiness.ledgerBookWorkflows?.requiredControlCount ?? 10} workflow controls`
      : "Ledger-book rollout evidence unavailable",
    dimensionalReportingLabel,
    dimensionalReportingEvidenceLabel,
    tenantAdministrationLabel,
    tenantAdministrationEvidenceLabel,
    tenantAdministrationControls,
    migrationPlanRows,
    migrationArtifactSummaryLabel: retainedArtifacts.length > 0
      ? `${certifiedArtifactCount}/${retainedArtifacts.length} retained artifact${retainedArtifacts.length === 1 ? "" : "s"} certified${failedArtifactCount > 0 ? ` | ${failedArtifactCount} failed` : ""}`
      : "No retained migration run artifacts loaded",
    migrationArtifactRows,
    migrationWorkerPlanSummaryLabel: retainedMigrationWorkerPlans.length > 0
      ? `${reconciledWorkerPlanCount}/${retainedMigrationWorkerPlans.length} retained worker plan${retainedMigrationWorkerPlans.length === 1 ? "" : "s"} reconciled`
      : "No retained migration worker plans loaded",
    migrationWorkerPlanRows,
    productionGapRows,
    components: readiness.components.map<AccountingProductionReadinessComponentViewModel>((component) => ({
      id: component.area,
      label: component.label || formatProductionReadinessArea(component.area),
      statusLabel: formatProductionReadinessStatus(component.status),
      scoreLabel: `${component.score}/100`,
      summary: component.summary,
      issueCountLabel: `${component.issues.length} issue${component.issues.length === 1 ? "" : "s"}`,
      evidenceLabel: `${component.evidenceReferences.length} evidence reference${component.evidenceReferences.length === 1 ? "" : "s"}`,
      routeLabel: component.route ?? "No route",
      tone: productionReadinessStatusTone(component.status)
    })),
    blockerIssues,
    loading,
    errorText: error?.summary ?? null,
    errorDetails: error?.details ?? []
  };
}

function buildAccountingProductionCertificationProfileEditorViewModel(
  profile: AccountingProductionCertificationProfile | null,
  draft: AccountingProductionCertificationProfileDraft | null,
  loadError: ApiErrorDisplay | null,
  saveError: ApiErrorDisplay | null,
  saveMessage: string | null,
  saveBusy: boolean,
  updateControl: (controlId: string, checked: boolean) => void,
  updateEvidence: (value: string) => void,
  save: () => Promise<void>
): AccountingProductionCertificationProfileEditorViewModel {
  const hasFundScope = Boolean(profile?.fundProfileId?.trim());
  const controls = buildAccountingProductionCertificationProfileControls(draft);
  const evidenceValue = draft?.evidenceText ?? "";
  const missingEvidence = splitAccountingEvidenceReferences(evidenceValue).length === 0;
  const saveDisabledReason = !profile || !draft
    ? "Load accounting production scope before saving certification controls."
    : !hasFundScope
      ? "Fund scope is required before saving production certification controls."
      : missingEvidence
        ? PRODUCTION_CERTIFICATION_RETAINED_EVIDENCE_REQUIRED
        : null;

  return {
    title: "Production certification profile",
    scopeLabel: profile
      ? `Tenant ${profile.tenantId || "context"} | company ${profile.companyId || "context"} | fund ${profile.fundProfileId || "missing"} | ledger book ${profile.ledgerBookId || "fund-level"}`
      : "Tenant, company, fund, and ledger-book scope have not loaded.",
    updatedLabel: profile
      ? `Last retained by ${profile.updatedBy || "unknown"} at ${profile.updatedAtUtc || "not retained"}`
      : "No retained profile loaded.",
    evidenceValue,
    controls,
    saveButtonLabel: saveBusy ? "Saving certification" : "Save certification",
    saveDisabledReason,
    saveBusy,
    canSave: saveDisabledReason === null && !saveBusy,
    statusText: saveError?.summary ?? saveMessage ?? loadError?.summary ?? null,
    errorText: saveError?.summary ?? null,
    errorDetails: saveError?.details ?? [],
    updateControl,
    updateEvidence,
    save
  };
}

function buildAccountingProductionCertificationProfileControls(
  draft: AccountingProductionCertificationProfileDraft | null
): AccountingProductionCertificationProfileControlEditViewModel[] {
  return [
    {
      id: "posting-rules-book",
      label: "Rules by book",
      description: "Posting rules execute with retained ledger-book scope and evidence.",
      checked: draft?.postingRulesLedgerBookNativeCertified ?? false
    },
    {
      id: "journal-lifecycle-book",
      label: "JE by book",
      description: "Journal lifecycle controls are certified for the selected ledger book.",
      checked: draft?.journalLifecycleLedgerBookNativeCertified ?? false
    },
    {
      id: "close-reporting-book",
      label: "Close by book",
      description: "Close and reporting workflows retain ledger-book-scoped evidence.",
      checked: draft?.closeReportingLedgerBookNativeCertified ?? false
    },
    {
      id: "close-plan-configuration-book",
      label: "Close setup by book",
      description: "Close-plan setup, materiality, dependencies, and sign-off configuration retain ledger-book scope.",
      checked: draft?.closePlanConfigurationLedgerBookNativeCertified ?? false
    },
    {
      id: "external-gl-book",
      label: "External GL by book",
      description: "Guarded exports and mappings carry ledger-book scope.",
      checked: draft?.externalGlLedgerBookNativeCertified ?? false
    },
    {
      id: "reconciliation-book",
      label: "Reconciliation by book",
      description: "Break queues, statement reconciliation, and cases retain ledger-book scope.",
      checked: draft?.reconciliationLedgerBookNativeCertified ?? false
    },
    {
      id: "direct-lending-book",
      label: "Direct lending by book",
      description: "Loan-account and borrower projections retain the selected ledger book.",
      checked: draft?.directLendingLedgerBookNativeCertified ?? false
    },
    {
      id: "strategy-ledger-reads-book",
      label: "Strategy reads by book",
      description: "Strategy run trial-balance and journal reads fail closed on cross-book data.",
      checked: draft?.strategyLedgerReadLedgerBookNativeCertified ?? false
    },
    {
      id: "period-report-dimensions",
      label: "Period dimensions",
      description: "Period reports support required accounting dimensions.",
      checked: draft?.periodReportDimensionQueriesCertified ?? false
    },
    {
      id: "cross-period-dimensions",
      label: "Cross-period dimensions",
      description: "Cross-period reporting preserves dimensional filters.",
      checked: draft?.crossPeriodReportDimensionQueriesCertified ?? false
    },
    {
      id: "journal-dimension-filters",
      label: "JE dimensions",
      description: "Journal queries expose fund, entity, instrument, and related dimensions.",
      checked: draft?.journalQueryDimensionFiltersCertified ?? false
    },
    {
      id: "external-export-dimensions",
      label: "Export dimensions",
      description: "External GL export mappings retain certified dimensions.",
      checked: draft?.externalExportDimensionMappingCertified ?? false
    },
    {
      id: "ledger-line-dimensions",
      label: "Line dimensions",
      description: "Posted ledger lines persist canonical LedgerDimensionSet scope.",
      checked: draft?.ledgerLineDimensionsPersistedCertified ?? false
    },
    {
      id: "trial-balance-dimensions",
      label: "Trial balance filters",
      description: "Trial-balance routes filter by retained canonical dimensions.",
      checked: draft?.trialBalanceDimensionFiltersCertified ?? false
    },
    {
      id: "report-package-dimensions",
      label: "Package provenance",
      description: "Report and NAV packages retain dimensional line provenance.",
      checked: draft?.reportPackageDimensionProvenanceCertified ?? false
    }
  ];
}

function buildAccountingProductionCertificationProfileDraft(
  profile: AccountingProductionCertificationProfile
): AccountingProductionCertificationProfileDraft {
  return {
    postingRulesLedgerBookNativeCertified: profile.postingRulesLedgerBookNativeCertified,
    journalLifecycleLedgerBookNativeCertified: profile.journalLifecycleLedgerBookNativeCertified,
    closeReportingLedgerBookNativeCertified: profile.closeReportingLedgerBookNativeCertified,
    closePlanConfigurationLedgerBookNativeCertified: profile.closePlanConfigurationLedgerBookNativeCertified ?? false,
    externalGlLedgerBookNativeCertified: profile.externalGlLedgerBookNativeCertified,
    reconciliationLedgerBookNativeCertified: profile.reconciliationLedgerBookNativeCertified ?? false,
    directLendingLedgerBookNativeCertified: profile.directLendingLedgerBookNativeCertified ?? false,
    strategyLedgerReadLedgerBookNativeCertified: profile.strategyLedgerReadLedgerBookNativeCertified ?? false,
    periodReportDimensionQueriesCertified: profile.periodReportDimensionQueriesCertified,
    crossPeriodReportDimensionQueriesCertified: profile.crossPeriodReportDimensionQueriesCertified,
    journalQueryDimensionFiltersCertified: profile.journalQueryDimensionFiltersCertified,
    externalExportDimensionMappingCertified: profile.externalExportDimensionMappingCertified,
    ledgerLineDimensionsPersistedCertified: profile.ledgerLineDimensionsPersistedCertified ?? false,
    trialBalanceDimensionFiltersCertified: profile.trialBalanceDimensionFiltersCertified ?? false,
    reportPackageDimensionProvenanceCertified: profile.reportPackageDimensionProvenanceCertified ?? false,
    evidenceText: profile.evidenceReferences.join("\n")
  };
}

function buildAccountingProductionCertificationProfileFromReadiness(
  readiness: AccountingProductionReadiness
): AccountingProductionCertificationProfile {
  const ledgerBookWorkflows = readiness.ledgerBookWorkflows;
  const dimensionalReporting = readiness.dimensionalReporting;
  const evidenceReferences = [
    ...(ledgerBookWorkflows?.evidenceReferences ?? []),
    ...(dimensionalReporting?.evidenceReferences ?? [])
  ].filter((item, index, items) => item.length > 0 && items.findIndex((candidate) => candidate.toLowerCase() === item.toLowerCase()) === index);

  return {
    fundProfileId: readiness.fundProfileId,
    ledgerBookId: readiness.ledgerBookId ?? ledgerBookWorkflows?.ledgerBookId ?? dimensionalReporting?.ledgerBookId ?? null,
    tenantId: readiness.tenantAdministration?.tenantId ?? null,
    companyId: readiness.tenantAdministration?.companyId ?? null,
    postingRulesLedgerBookNativeCertified: ledgerBookWorkflows?.postingRulesLedgerBookNativeCertified ?? false,
    journalLifecycleLedgerBookNativeCertified: ledgerBookWorkflows?.journalLifecycleLedgerBookNativeCertified ?? false,
    closeReportingLedgerBookNativeCertified: ledgerBookWorkflows?.closeReportingLedgerBookNativeCertified ?? false,
    closePlanConfigurationLedgerBookNativeCertified: ledgerBookWorkflows?.closePlanConfigurationLedgerBookNativeCertified ?? false,
    externalGlLedgerBookNativeCertified: ledgerBookWorkflows?.externalGlLedgerBookNativeCertified ?? false,
    reconciliationLedgerBookNativeCertified: ledgerBookWorkflows?.reconciliationLedgerBookNativeCertified ?? false,
    directLendingLedgerBookNativeCertified: ledgerBookWorkflows?.directLendingLedgerBookNativeCertified ?? false,
    strategyLedgerReadLedgerBookNativeCertified: ledgerBookWorkflows?.strategyLedgerReadLedgerBookNativeCertified ?? false,
    periodReportDimensionQueriesCertified: dimensionalReporting?.periodReportDimensionQueriesCertified ?? false,
    crossPeriodReportDimensionQueriesCertified: dimensionalReporting?.crossPeriodReportDimensionQueriesCertified ?? false,
    journalQueryDimensionFiltersCertified: dimensionalReporting?.journalQueryDimensionFiltersCertified ?? false,
    externalExportDimensionMappingCertified: dimensionalReporting?.externalExportDimensionMappingCertified ?? false,
    ledgerLineDimensionsPersistedCertified: dimensionalReporting?.ledgerLineDimensionsPersistedCertified ?? false,
    trialBalanceDimensionFiltersCertified: dimensionalReporting?.trialBalanceDimensionFiltersCertified ?? false,
    reportPackageDimensionProvenanceCertified: dimensionalReporting?.reportPackageDimensionProvenanceCertified ?? false,
    updatedAtUtc: "",
    updatedBy: "not retained",
    evidenceReferences,
    correlationId: null,
    workflowCertificationArtifacts: readiness.workflowCertificationArtifacts ?? [],
    dimensionalCertificationArtifacts: readiness.dimensionalCertificationArtifacts ?? [],
    tenantAdminCertificationArtifacts: readiness.tenantAdminCertificationArtifacts ?? []
  };
}

function withProductionCertificationControlEvidence(
  evidenceReferences: string[],
  profile: AccountingProductionCertificationProfile | null,
  draft: AccountingProductionCertificationProfileDraft | null
): string[] {
  const ledgerBookId = profile?.ledgerBookId?.trim();
  if (!profile || !draft || !ledgerBookId) {
    return evidenceReferences;
  }

  if (evidenceReferences.some((reference) => reference.toLowerCase().includes("production-certification/full"))) {
    return evidenceReferences;
  }

  const tenantId = profile.tenantId?.trim() || "tenant";
  const companyId = profile.companyId?.trim() || "company";
  const fundProfileId = profile.fundProfileId?.trim() || "fund";
  const scopePrefix = `evidence://tenant/${tenantId}/company/${companyId}/fund/${fundProfileId}/ledger-book/${ledgerBookId}/production-certification`;
  const generated: string[] = [];
  const addWorkflowEvidence = (certified: boolean, marker: string) => {
    if (!certified) {
      return;
    }

    generated.push(`${scopePrefix}/${marker}`);
  };
  const addDimensionEvidence = (certified: boolean, marker: string) => {
    if (!certified) {
      return;
    }

    generated.push(`${scopePrefix}/dimensions/${marker}/dimension-scope/canonical-production`);
  };

  addWorkflowEvidence(draft.postingRulesLedgerBookNativeCertified, "posting-candidate");
  addWorkflowEvidence(draft.journalLifecycleLedgerBookNativeCertified, "journal-lifecycle");
  addWorkflowEvidence(draft.closeReportingLedgerBookNativeCertified, "close-reporting");
  addWorkflowEvidence(draft.closePlanConfigurationLedgerBookNativeCertified, "close-plan-configuration");
  addWorkflowEvidence(draft.externalGlLedgerBookNativeCertified, "external-gl");
  addWorkflowEvidence(draft.reconciliationLedgerBookNativeCertified, "reconciliation");
  addWorkflowEvidence(draft.directLendingLedgerBookNativeCertified, "direct-lending");
  addWorkflowEvidence(draft.strategyLedgerReadLedgerBookNativeCertified, "strategy-ledger");
  addDimensionEvidence(draft.periodReportDimensionQueriesCertified, "period-report");
  addDimensionEvidence(draft.crossPeriodReportDimensionQueriesCertified, "cross-period");
  addDimensionEvidence(draft.journalQueryDimensionFiltersCertified, "journal-query");
  addDimensionEvidence(draft.externalExportDimensionMappingCertified, "external-export");
  addDimensionEvidence(draft.ledgerLineDimensionsPersistedCertified, "ledger-line");
  addDimensionEvidence(draft.trialBalanceDimensionFiltersCertified, "trial-balance-filter");
  addDimensionEvidence(draft.reportPackageDimensionProvenanceCertified, "report-package-provenance");

  return [...evidenceReferences, ...generated]
    .filter((item, index, items) => item.length > 0 && items.findIndex((candidate) => candidate.toLowerCase() === item.toLowerCase()) === index);
}

function buildAccountingWorkflowCertificationArtifact(
  profile: AccountingProductionCertificationProfile,
  draft: AccountingProductionCertificationProfileDraft,
  evidenceReferences: string[],
  correlationId: string
): AccountingWorkflowCertificationArtifact | null {
  const ledgerBookId = profile.ledgerBookId?.trim();
  if (!ledgerBookId) {
    return null;
  }

  const lanes: AccountingWorkflowCertificationLane[] = [
    buildWorkflowCertificationLane(draft.postingRulesLedgerBookNativeCertified, "PostingRules", evidenceReferences, "posting-candidate"),
    buildWorkflowCertificationLane(draft.journalLifecycleLedgerBookNativeCertified, "JournalLifecycle", evidenceReferences, "journal-lifecycle"),
    buildWorkflowCertificationLane(draft.closeReportingLedgerBookNativeCertified, "CloseReporting", evidenceReferences, "close-reporting"),
    buildWorkflowCertificationLane(draft.closePlanConfigurationLedgerBookNativeCertified, "ClosePlanConfiguration", evidenceReferences, "close-plan-configuration"),
    buildWorkflowCertificationLane(draft.externalGlLedgerBookNativeCertified, "ExternalGl", evidenceReferences, "external-gl"),
    buildWorkflowCertificationLane(draft.reconciliationLedgerBookNativeCertified, "Reconciliation", evidenceReferences, "reconciliation"),
    buildWorkflowCertificationLane(draft.directLendingLedgerBookNativeCertified, "DirectLendingProjection", evidenceReferences, "direct-lending"),
    buildWorkflowCertificationLane(draft.strategyLedgerReadLedgerBookNativeCertified, "StrategyLedgerReads", evidenceReferences, "strategy-ledger")
  ].filter((lane): lane is AccountingWorkflowCertificationLane => lane !== null);

  if (lanes.length === 0) {
    return null;
  }

  return {
    certificationId: `${correlationId}-workflow`,
    status: "Certified",
    tenantId: profile.tenantId ?? null,
    companyId: profile.companyId ?? null,
    fundProfileId: profile.fundProfileId,
    ledgerBookId,
    certifiedBy: "browser-accounting-operator",
    certifiedAtUtc: new Date().toISOString(),
    sourceService: "browser-accounting-configure",
    lanes,
    evidenceReferences,
    issues: [],
    correlationId
  };
}

function buildAccountingDimensionalCertificationArtifact(
  profile: AccountingProductionCertificationProfile,
  draft: AccountingProductionCertificationProfileDraft,
  evidenceReferences: string[],
  correlationId: string
): AccountingDimensionalCertificationArtifact | null {
  const ledgerBookId = profile.ledgerBookId?.trim();
  if (!ledgerBookId) {
    return null;
  }

  const lanes: AccountingDimensionalCertificationLane[] = [
    buildDimensionalCertificationLane(draft.ledgerLineDimensionsPersistedCertified, "LedgerLinePersistence", evidenceReferences, "ledger-line"),
    buildDimensionalCertificationLane(draft.trialBalanceDimensionFiltersCertified, "TrialBalanceFilters", evidenceReferences, "trial-balance-filter"),
    buildDimensionalCertificationLane(draft.periodReportDimensionQueriesCertified, "PeriodReports", evidenceReferences, "period-report"),
    buildDimensionalCertificationLane(draft.crossPeriodReportDimensionQueriesCertified, "CrossPeriodReports", evidenceReferences, "cross-period"),
    buildDimensionalCertificationLane(draft.journalQueryDimensionFiltersCertified, "JournalFilters", evidenceReferences, "journal-query"),
    buildDimensionalCertificationLane(draft.reportPackageDimensionProvenanceCertified, "ReportPackageProvenance", evidenceReferences, "report-package-provenance"),
    buildDimensionalCertificationLane(draft.externalExportDimensionMappingCertified, "ExternalExportMappings", evidenceReferences, "external-export")
  ].filter((lane): lane is AccountingDimensionalCertificationLane => lane !== null);

  if (lanes.length === 0) {
    return null;
  }

  return {
    certificationId: `${correlationId}-dimensions`,
    status: "Certified",
    tenantId: profile.tenantId ?? null,
    companyId: profile.companyId ?? null,
    fundProfileId: profile.fundProfileId,
    ledgerBookId,
    dimensionScopeEvidenceKey: "canonical-production",
    certifiedBy: "browser-accounting-operator",
    certifiedAtUtc: new Date().toISOString(),
    sourceService: "browser-accounting-configure",
    lanes,
    evidenceReferences,
    issues: [],
    correlationId
  };
}

function buildWorkflowCertificationLane(
  certified: boolean,
  kind: AccountingWorkflowCertificationLane["kind"],
  evidenceReferences: string[],
  marker: string
): AccountingWorkflowCertificationLane | null {
  if (!certified) {
    return null;
  }

  return {
    kind,
    status: "Passed",
    evidenceReferences: filterCertificationLaneEvidence(evidenceReferences, marker),
    issues: []
  };
}

function buildDimensionalCertificationLane(
  certified: boolean,
  kind: AccountingDimensionalCertificationLane["kind"],
  evidenceReferences: string[],
  marker: string
): AccountingDimensionalCertificationLane | null {
  if (!certified) {
    return null;
  }

  return {
    kind,
    status: "Passed",
    evidenceReferences: filterCertificationLaneEvidence(evidenceReferences, marker),
    issues: []
  };
}

function buildAccountingTenantAdminCertificationArtifact(
  profile: AccountingProductionCertificationProfile,
  tenantProfile: AccountingTenantAdministrationProfile | null,
  draft: AccountingTenantAdministrationProfileDraft | null,
  evidenceReferences: string[],
  ledgerBookId: string | null | undefined,
  correlationId: string
): AccountingTenantAdminCertificationArtifact | null {
  if (!tenantProfile || !draft) {
    return null;
  }

  const lanes: AccountingTenantAdminCertificationLane[] = [
    buildTenantAdminCertificationLane(draft.tenantScopeConfigured, "TenantScope", evidenceReferences, "tenant-scope"),
    buildTenantAdminCertificationLane(draft.adminRoleProfileConfigured, "AdminRoleProfile", evidenceReferences, "admin-role"),
    buildTenantAdminCertificationLane(draft.scopedAccessPoliciesConfigured, "ScopedAccessPolicies", evidenceReferences, "scoped-access"),
    buildTenantAdminCertificationLane(draft.reportingGroupsConfigured, "ReportingGroups", evidenceReferences, "reporting-group"),
    buildTenantAdminCertificationLane(draft.accountingAdminSurfaceConfigured, "AccountingAdminSurface", evidenceReferences, "accounting-admin-surface"),
    buildTenantAdminCertificationLane(draft.browserAccountingAdminSurfaceConfigured, "BrowserAccountingAdminSurface", evidenceReferences, "browser-accounting-admin"),
    buildTenantAdminCertificationLane(draft.wpfAccountingAdminSurfaceConfigured, "WpfAccountingAdminSurface", evidenceReferences, "wpf-admin-studio"),
    buildTenantAdminCertificationLane(draft.chartAdministrationStudioConfigured, "ChartAdministrationStudio", evidenceReferences, "chart-administration"),
    buildTenantAdminCertificationLane(draft.ruleTestPromotionStudioConfigured, "RuleTestPromotionStudio", evidenceReferences, "rule-test-promotion"),
    buildTenantAdminCertificationLane(draft.closeSetupStudioConfigured, "CloseSetupStudio", evidenceReferences, "close-setup"),
    buildTenantAdminCertificationLane(draft.providerMappingStudioConfigured, "ProviderMappingStudio", evidenceReferences, "provider-mapping"),
    buildTenantAdminCertificationLane(draft.tenantCompanyReportGroupSetupStudioConfigured, "TenantCompanyReportGroupSetupStudio", evidenceReferences, "tenant-company-report-group"),
    buildTenantAdminCertificationLane(draft.auditReviewToolingConfigured, "AuditReviewTooling", evidenceReferences, "audit-review"),
    buildTenantAdminCertificationLane(draft.bulkImportExportSafeguardsConfigured, "BulkImportExportSafeguards", evidenceReferences, "bulk-import-export"),
    buildTenantAdminCertificationLane(draft.performanceValidationConfigured, "PerformanceValidation", evidenceReferences, "performance-validation"),
    buildTenantAdminCertificationLane(draft.disasterRecoveryRunbookConfigured, "DisasterRecoveryRunbook", evidenceReferences, "disaster-recovery"),
    buildTenantAdminCertificationLane(draft.ledgerBookAdministrationStudioConfigured, "LedgerBookAdministrationStudio", evidenceReferences, "ledger-book-administration"),
    buildTenantAdminCertificationLane(draft.postingRuleAuthoringStudioConfigured, "PostingRuleAuthoringStudio", evidenceReferences, "posting-rule-authoring"),
    buildTenantAdminCertificationLane(draft.approvalQueueStudioConfigured, "ApprovalQueueStudio", evidenceReferences, "approval-queue"),
    buildTenantAdminCertificationLane(draft.dimensionMappingStudioConfigured, "DimensionMappingStudio", evidenceReferences, "dimension-mapping"),
    buildTenantAdminCertificationLane(draft.implementationSandboxConfigured, "ImplementationSandbox", evidenceReferences, "implementation-sandbox")
  ].filter((lane): lane is AccountingTenantAdminCertificationLane => lane !== null);

  if (lanes.length === 0) {
    return null;
  }

  return {
    certificationId: `${correlationId}-tenant-admin`,
    status: "Certified",
    tenantId: tenantProfile.tenantId,
    companyId: tenantProfile.companyId,
    fundProfileId: profile.fundProfileId,
    ledgerBookId: ledgerBookId?.trim() || profile.ledgerBookId || null,
    certifiedBy: "browser-accounting-operator",
    certifiedAtUtc: new Date().toISOString(),
    sourceService: "browser-accounting-configure",
    lanes,
    evidenceReferences,
    issues: [],
    correlationId
  };
}

function buildTenantAdminCertificationLane(
  certified: boolean,
  kind: AccountingTenantAdminCertificationLane["kind"],
  evidenceReferences: string[],
  marker: string
): AccountingTenantAdminCertificationLane | null {
  if (!certified) {
    return null;
  }

  return {
    kind,
    status: "Passed",
    evidenceReferences: filterCertificationLaneEvidence(evidenceReferences, marker),
    issues: []
  };
}

function filterCertificationLaneEvidence(evidenceReferences: string[], marker: string): string[] {
  const normalizedMarker = marker.toLowerCase();
  const scoped = evidenceReferences.filter((reference) => reference.toLowerCase().includes(normalizedMarker));
  return scoped.length > 0 ? scoped : evidenceReferences;
}

function mergeAccountingCertificationArtifacts<T extends { certificationId: string }>(
  current: T[] | null | undefined,
  next: T | null
): T[] {
  if (!next) {
    return current ?? [];
  }

  return [
    ...(current ?? []).filter((artifact) => artifact.certificationId.toLowerCase() !== next.certificationId.toLowerCase()),
    next
  ];
}

function buildAccountingTenantAdministrationProfileEditorViewModel(
  profile: AccountingTenantAdministrationProfile | null,
  draft: AccountingTenantAdministrationProfileDraft | null,
  loadError: ApiErrorDisplay | null,
  saveError: ApiErrorDisplay | null,
  saveMessage: string | null,
  saveBusy: boolean,
  sandboxBusy: boolean,
  sandboxError: ApiErrorDisplay | null,
  sandboxMessage: string | null,
  updateControl: (controlId: string, checked: boolean) => void,
  updateApprovalQueueSetup: (patch: Partial<AccountingApprovalQueueSetupDraft>) => void,
  updateDimensionMappingSetup: (patch: Partial<AccountingDimensionMappingSetupDraft>) => void,
  updateEvidence: (value: string) => void,
  retainSandboxProof: () => Promise<void>,
  save: () => Promise<void>
): AccountingTenantAdministrationProfileEditorViewModel {
  const hasScope = Boolean(profile?.tenantId?.trim()) && Boolean(profile?.companyId?.trim());
  const controls = buildAccountingTenantAdministrationProfileControls(draft);
  const evidenceValue = draft?.evidenceText ?? "";
  const missingEvidence = splitAccountingTenantAdministrationEvidence(evidenceValue).length === 0;
  const approvalQueueConfiguration = draft
    ? buildAccountingApprovalQueueConfiguration(draft.approvalQueueSetup)
    : null;
  const dimensionMappingConfiguration = draft
    ? buildAccountingDimensionMappingConfiguration(draft.dimensionMappingSetup)
    : null;
  const saveDisabledReason = !profile || !draft
    ? "Load accounting tenant scope before saving setup controls."
    : !hasScope
      ? "Tenant and company scope are required before saving setup controls."
      : missingEvidence
        ? "Retained setup evidence is required before saving tenant administration controls."
        : draft.approvalQueueStudioConfigured && !approvalQueueConfiguration
          ? "Complete approval queue id, workflow kind, approval role/count, segregation policy, and evidence requirement before saving approval queue setup."
          : draft.dimensionMappingStudioConfigured && !dimensionMappingConfiguration
            ? "Complete dimension mapping id, display name, provider id, Meridian dimensions, provider dimensions, and evidence requirement before saving dimension mapping setup."
            : null;
  const sandboxDisabledReason = !profile || !draft
    ? "Load accounting tenant scope before retaining sandbox proof."
    : !profile.tenantId?.trim() || !profile.companyId?.trim()
      ? "Tenant and company scope are required before retaining sandbox proof."
      : draft.approvalQueueStudioConfigured && !approvalQueueConfiguration
        ? "Complete approval queue id, workflow kind, approval role/count, segregation policy, and evidence requirement before retaining implementation sandbox proof."
        : draft.dimensionMappingStudioConfigured && !dimensionMappingConfiguration
          ? "Complete dimension mapping id, display name, provider id, Meridian dimensions, provider dimensions, and evidence requirement before retaining implementation sandbox proof."
          : null;

  return {
    title: "Tenant administration setup",
    scopeLabel: profile
      ? `Tenant ${profile.tenantId || "missing"} | company ${profile.companyId || "missing"}`
      : "Tenant and company scope have not loaded.",
    updatedLabel: profile
      ? `Last retained by ${profile.updatedBy || "unknown"} at ${profile.updatedAtUtc || "not retained"}`
      : "No retained profile loaded.",
    evidenceValue,
    controls,
    approvalQueueSetup: buildAccountingApprovalQueueSetupEditorViewModel(draft),
    dimensionMappingSetup: buildAccountingDimensionMappingSetupEditorViewModel(draft),
    saveButtonLabel: saveBusy ? "Saving setup controls" : "Save setup controls",
    saveDisabledReason,
    saveBusy,
    canSave: saveDisabledReason === null && !saveBusy,
    statusText: saveError?.summary ?? saveMessage ?? loadError?.summary ?? null,
    errorText: saveError?.summary ?? null,
    errorDetails: saveError?.details ?? [],
    sandboxButtonLabel: sandboxBusy ? "Retaining sandbox proof" : "Retain sandbox proof",
    sandboxDisabledReason,
    sandboxBusy,
    sandboxStatusText: sandboxError?.summary ?? sandboxMessage,
    canRetainSandboxProof: sandboxDisabledReason === null && !sandboxBusy,
    updateControl,
    updateApprovalQueueSetup,
    updateDimensionMappingSetup,
    updateEvidence,
    retainSandboxProof,
    save
  };
}

function buildAccountingTenantAdministrationProfileControls(
  draft: AccountingTenantAdministrationProfileDraft | null
): AccountingTenantAdministrationProfileControlEditViewModel[] {
  return [
    {
      id: "tenant-config",
      label: "Tenant config",
      description: "Tenant scope, accounting configuration, and company setup are retained.",
      checked: draft?.tenantScopeConfigured ?? false
    },
    {
      id: "admin-roles",
      label: "Admin roles",
      description: "Accounting admin role profiles are configured for governed setup.",
      checked: draft?.adminRoleProfileConfigured ?? false
    },
    {
      id: "scoped-access",
      label: "Scoped access",
      description: "Accounting permissions are scoped to tenant, company, fund, or book boundaries.",
      checked: draft?.scopedAccessPoliciesConfigured ?? false
    },
    {
      id: "reporting-groups",
      label: "Reporting groups",
      description: "Reporting group principals are configured for close, report, and export review.",
      checked: draft?.reportingGroupsConfigured ?? false
    },
    {
      id: "operator-surface",
      label: "Operator surface",
      description: "Accounting setup controls are exposed through the governed operator surface.",
      checked: draft?.accountingAdminSurfaceConfigured ?? false
    },
    {
      id: "chart-administration-studio",
      label: "Chart setup",
      description: "Chart, ledger-book chart, and account template setup are exposed in the configuration studio.",
      checked: draft?.chartAdministrationStudioConfigured ?? false
    },
    {
      id: "rule-test-promotion-studio",
      label: "Rule tests",
      description: "Rule authoring, saved tests, dry-runs, generated postings, and promotion queues are exposed.",
      checked: draft?.ruleTestPromotionStudioConfigured ?? false
    },
    {
      id: "close-setup-studio",
      label: "Close setup",
      description: "Close checklist, dependencies, sign-offs, materiality, period locks, and calendar setup are exposed.",
      checked: draft?.closeSetupStudioConfigured ?? false
    },
    {
      id: "provider-mapping-studio",
      label: "Provider mapping",
      description: "External provider, GL mapping, reconciliation, and guarded export setup are exposed.",
      checked: draft?.providerMappingStudioConfigured ?? false
    },
    {
      id: "tenant-company-report-group-studio",
      label: "Tenant groups",
      description: "Tenant, company, scoped access, admin role, report group, and delivery group setup are exposed.",
      checked: draft?.tenantCompanyReportGroupSetupStudioConfigured ?? false
    },
    {
      id: "audit-review-tooling",
      label: "Audit review",
      description: "Audit, evidence, transition, and exception review tooling are certified for operations.",
      checked: draft?.auditReviewToolingConfigured ?? false
    },
    {
      id: "bulk-import-export-safeguards",
      label: "Bulk safeguards",
      description: "Bulk import, guarded export, reconciliation, preview, approval, and rollback safeguards are certified.",
      checked: draft?.bulkImportExportSafeguardsConfigured ?? false
    },
    {
      id: "performance-validation",
      label: "Performance proof",
      description: "Accounting configuration, ledger queries, close, reports, and exports have retained performance validation.",
      checked: draft?.performanceValidationConfigured ?? false
    },
    {
      id: "disaster-recovery-runbook",
      label: "Recovery runbook",
      description: "Disaster recovery, restore, replay, escalation, and accounting operating runbook evidence is certified.",
      checked: draft?.disasterRecoveryRunbookConfigured ?? false
    },
    {
      id: "ledger-book-administration-studio",
      label: "Book admin",
      description: "Ledger-book creation, selection, policy, activation, and period setup are exposed.",
      checked: draft?.ledgerBookAdministrationStudioConfigured ?? false
    },
    {
      id: "posting-rule-authoring-studio",
      label: "Rule authoring",
      description: "Event predicates, scopes, thresholds, formulas, allocations, and posting previews are exposed.",
      checked: draft?.postingRuleAuthoringStudioConfigured ?? false
    },
    {
      id: "approval-queue-studio",
      label: "Approvals",
      description: "Rule promotion, journal, configuration, and segregation approval queues are exposed.",
      checked: draft?.approvalQueueStudioConfigured ?? false
    },
    {
      id: "dimension-mapping-studio",
      label: "Dimension maps",
      description: "Canonical and external GL dimension mapping setup is exposed.",
      checked: draft?.dimensionMappingStudioConfigured ?? false
    },
    {
      id: "implementation-sandbox",
      label: "Sandbox proof",
      description: "Implementation validation, migration rehearsal, import, rule, close, and report evidence is retained.",
      checked: draft?.implementationSandboxConfigured ?? false
    }
  ];
}

function buildAccountingApprovalQueueSetupEditorViewModel(
  draft: AccountingTenantAdministrationProfileDraft | null
): AccountingApprovalQueueSetupEditorViewModel {
  const setup = draft?.approvalQueueSetup ?? buildDefaultAccountingApprovalQueueSetup();
  return {
    queueIdValue: setup.queueId,
    displayNameValue: setup.displayName,
    workflowKindValue: setup.workflowKind,
    requiredApprovalRoleValue: setup.requiredApprovalRole,
    requiredApprovalCountValue: setup.requiredApprovalCount,
    segregationPolicyValue: setup.segregationPolicy,
    evidenceRequirementValue: setup.evidenceRequirement
  };
}

function buildDefaultAccountingApprovalQueueSetup(): AccountingApprovalQueueSetupDraft {
  return {
    queueId: "accounting-configuration-approval",
    displayName: "Accounting configuration approval",
    workflowKind: "ConfigurationPromotion",
    requiredApprovalRole: "Controller",
    requiredApprovalCount: "2",
    segregationPolicy: "Preparer cannot approve own configuration change.",
    evidenceRequirement: "approval-queue;configuration-approval;segregation-review"
  };
}

function buildAccountingApprovalQueueConfiguration(
  draft: AccountingApprovalQueueSetupDraft
): AccountingApprovalQueueConfiguration | null {
  const requiredApprovalCount = Number.parseInt(draft.requiredApprovalCount, 10);
  if (!draft.queueId.trim() ||
      !draft.displayName.trim() ||
      !draft.workflowKind.trim() ||
      !draft.requiredApprovalRole.trim() ||
      !Number.isFinite(requiredApprovalCount) ||
      requiredApprovalCount <= 0 ||
      !draft.segregationPolicy.trim() ||
      !draft.evidenceRequirement.trim()) {
    return null;
  }

  return {
    queueId: draft.queueId.trim(),
    displayName: draft.displayName.trim(),
    workflowKind: draft.workflowKind.trim(),
    requiredApprovalRole: draft.requiredApprovalRole.trim(),
    requiredApprovalCount,
    segregationPolicy: draft.segregationPolicy.trim(),
    evidenceRequirement: draft.evidenceRequirement.trim()
  };
}

function buildAccountingApprovalQueueSetupDraft(
  profile: AccountingTenantAdministrationProfile
): AccountingApprovalQueueSetupDraft {
  const retained = profile.approvalQueueConfigurations?.[0] ?? null;
  if (!retained) {
    return buildDefaultAccountingApprovalQueueSetup();
  }

  return {
    queueId: retained.queueId,
    displayName: retained.displayName,
    workflowKind: retained.workflowKind,
    requiredApprovalRole: retained.requiredApprovalRole,
    requiredApprovalCount: String(retained.requiredApprovalCount),
    segregationPolicy: retained.segregationPolicy,
    evidenceRequirement: retained.evidenceRequirement
  };
}

function buildAccountingDimensionMappingSetupEditorViewModel(
  draft: AccountingTenantAdministrationProfileDraft | null
): AccountingDimensionMappingSetupEditorViewModel {
  const setup = draft?.dimensionMappingSetup ?? buildDefaultAccountingDimensionMappingSetup();
  return {
    mappingIdValue: setup.mappingId,
    displayNameValue: setup.displayName,
    providerIdValue: setup.providerId,
    meridianDimensionsValue: setup.meridianDimensionsText,
    providerDimensionsValue: setup.providerDimensionsText,
    evidenceRequirementValue: setup.evidenceRequirement
  };
}

function buildDefaultAccountingDimensionMappingSetup(): AccountingDimensionMappingSetupDraft {
  return {
    mappingId: "accounting-dimension-map",
    displayName: "Accounting dimension mapping",
    providerId: "quickbooks-fixture",
    meridianDimensionsText: "fundId=fund-alpha\nbookId=book-primary\ncostCenterId=fund-accounting",
    providerDimensionsText: "Class=fund-alpha\nBook=book-primary\nDepartment=fund-accounting",
    evidenceRequirement: "dimension-mapping;provider-segment-review;controller-approval"
  };
}

function buildAccountingDimensionMappingConfiguration(
  draft: AccountingDimensionMappingSetupDraft
): AccountingDimensionMappingConfiguration | null {
  const meridianDimensions = parseLedgerDimensionSet(draft.meridianDimensionsText, {});
  const providerDimensions = parseLedgerDimensionSet(draft.providerDimensionsText, {});
  if (!draft.mappingId.trim() ||
      !draft.displayName.trim() ||
      !draft.providerId.trim() ||
      !hasLedgerDimensionSetValues(meridianDimensions) ||
      !hasLedgerDimensionSetValues(providerDimensions) ||
      !draft.evidenceRequirement.trim()) {
    return null;
  }

  return {
    mappingId: draft.mappingId.trim(),
    displayName: draft.displayName.trim(),
    providerId: draft.providerId.trim(),
    meridianDimensions,
    providerDimensions,
    evidenceRequirement: draft.evidenceRequirement.trim()
  };
}

function buildAccountingDimensionMappingSetupDraft(
  profile: AccountingTenantAdministrationProfile
): AccountingDimensionMappingSetupDraft {
  const retained = profile.dimensionMappingConfigurations?.[0] ?? null;
  if (!retained) {
    return buildDefaultAccountingDimensionMappingSetup();
  }

  return {
    mappingId: retained.mappingId,
    displayName: retained.displayName,
    providerId: retained.providerId,
    meridianDimensionsText: formatExternalGlLedgerDimensionSet(retained.meridianDimensions),
    providerDimensionsText: formatExternalGlLedgerDimensionSet(retained.providerDimensions),
    evidenceRequirement: retained.evidenceRequirement
  };
}

function hasLedgerDimensionSetValues(dimensions: LedgerDimensionSet): boolean {
  return LEDGER_DIMENSION_FORMAT_KEYS.some((key) => {
    const value = dimensions[key];
    return typeof value === "string" && value.trim().length > 0;
  }) || Object.values(dimensions.externalGlDimensions ?? {}).some((value) => value.trim().length > 0);
}

function buildAccountingTenantAdministrationProfileDraft(
  profile: AccountingTenantAdministrationProfile
): AccountingTenantAdministrationProfileDraft {
  return {
    tenantScopeConfigured: profile.tenantScopeConfigured,
    adminRoleProfileConfigured: profile.adminRoleProfileConfigured,
    scopedAccessPoliciesConfigured: profile.scopedAccessPoliciesConfigured,
    reportingGroupsConfigured: profile.reportingGroupsConfigured,
    accountingAdminSurfaceConfigured: profile.accountingAdminSurfaceConfigured,
    browserAccountingAdminSurfaceConfigured: profile.browserAccountingAdminSurfaceConfigured ?? false,
    wpfAccountingAdminSurfaceConfigured: profile.wpfAccountingAdminSurfaceConfigured ?? false,
    chartAdministrationStudioConfigured: profile.chartAdministrationStudioConfigured ?? false,
    ruleTestPromotionStudioConfigured: profile.ruleTestPromotionStudioConfigured ?? false,
    closeSetupStudioConfigured: profile.closeSetupStudioConfigured ?? false,
    providerMappingStudioConfigured: profile.providerMappingStudioConfigured ?? false,
    tenantCompanyReportGroupSetupStudioConfigured: profile.tenantCompanyReportGroupSetupStudioConfigured ?? false,
    auditReviewToolingConfigured: profile.auditReviewToolingConfigured ?? false,
    bulkImportExportSafeguardsConfigured: profile.bulkImportExportSafeguardsConfigured ?? false,
    performanceValidationConfigured: profile.performanceValidationConfigured ?? false,
    disasterRecoveryRunbookConfigured: profile.disasterRecoveryRunbookConfigured ?? false,
    ledgerBookAdministrationStudioConfigured: profile.ledgerBookAdministrationStudioConfigured ?? false,
    postingRuleAuthoringStudioConfigured: profile.postingRuleAuthoringStudioConfigured ?? false,
    approvalQueueStudioConfigured: profile.approvalQueueStudioConfigured ?? false,
    approvalQueueSetup: buildAccountingApprovalQueueSetupDraft(profile),
    dimensionMappingStudioConfigured: profile.dimensionMappingStudioConfigured ?? false,
    dimensionMappingSetup: buildAccountingDimensionMappingSetupDraft(profile),
    implementationSandboxConfigured: profile.implementationSandboxConfigured ?? false,
    evidenceText: profile.evidenceReferences.join("\n")
  };
}

function buildAccountingTenantAdministrationProfileFromReadiness(
  readiness: AccountingProductionReadiness
): AccountingTenantAdministrationProfile | null {
  const tenantAdministration = readiness.tenantAdministration;
  if (!tenantAdministration) {
    return null;
  }

  return {
    tenantId: tenantAdministration.tenantId ?? "",
    companyId: tenantAdministration.companyId ?? "",
    tenantScopeConfigured: tenantAdministration.tenantScopeConfigured,
    adminRoleProfileConfigured: tenantAdministration.adminRoleProfileConfigured,
    scopedAccessPoliciesConfigured: tenantAdministration.scopedAccessPoliciesConfigured,
    reportingGroupsConfigured: tenantAdministration.reportingGroupsConfigured,
    accountingAdminSurfaceConfigured: tenantAdministration.accountingAdminSurfaceConfigured,
    browserAccountingAdminSurfaceConfigured: tenantAdministration.browserAccountingAdminSurfaceConfigured ?? false,
    wpfAccountingAdminSurfaceConfigured: tenantAdministration.wpfAccountingAdminSurfaceConfigured ?? false,
    chartAdministrationStudioConfigured: tenantAdministration.chartAdministrationStudioConfigured ?? false,
    ruleTestPromotionStudioConfigured: tenantAdministration.ruleTestPromotionStudioConfigured ?? false,
    closeSetupStudioConfigured: tenantAdministration.closeSetupStudioConfigured ?? false,
    providerMappingStudioConfigured: tenantAdministration.providerMappingStudioConfigured ?? false,
    tenantCompanyReportGroupSetupStudioConfigured: tenantAdministration.tenantCompanyReportGroupSetupStudioConfigured ?? false,
    auditReviewToolingConfigured: tenantAdministration.auditReviewToolingConfigured ?? false,
    bulkImportExportSafeguardsConfigured: tenantAdministration.bulkImportExportSafeguardsConfigured ?? false,
    performanceValidationConfigured: tenantAdministration.performanceValidationConfigured ?? false,
    disasterRecoveryRunbookConfigured: tenantAdministration.disasterRecoveryRunbookConfigured ?? false,
    ledgerBookAdministrationStudioConfigured: tenantAdministration.ledgerBookAdministrationStudioConfigured ?? false,
    postingRuleAuthoringStudioConfigured: tenantAdministration.postingRuleAuthoringStudioConfigured ?? false,
    approvalQueueStudioConfigured: tenantAdministration.approvalQueueStudioConfigured ?? false,
    dimensionMappingStudioConfigured: tenantAdministration.dimensionMappingStudioConfigured ?? false,
    implementationSandboxConfigured: tenantAdministration.implementationSandboxConfigured ?? false,
    updatedAtUtc: "",
    updatedBy: "not retained",
    evidenceReferences: tenantAdministration.evidenceReferences ?? [],
    correlationId: null
  };
}

function splitAccountingTenantAdministrationEvidence(value: string): string[] {
  return splitAccountingEvidenceReferences(value);
}

function withLedgerBookTenantAdministrationEvidence(
  evidenceReferences: string[],
  profile: AccountingTenantAdministrationProfile | null,
  ledgerBookId: string | null | undefined,
  draft: Pick<AccountingTenantAdministrationProfileDraft, "ledgerBookAdministrationStudioConfigured" | "implementationSandboxConfigured">
): string[] {
  const normalizedBookId = ledgerBookId?.trim();
  if (!profile || !normalizedBookId) {
    return evidenceReferences;
  }

  const generated: string[] = [];
  if (draft.ledgerBookAdministrationStudioConfigured && !hasTenantAdminBookEvidence(evidenceReferences, normalizedBookId, "ledger-book-administration")) {
    generated.push(`evidence://tenant-admin/${profile.tenantId || "tenant"}/${profile.companyId || "company"}/ledger-book-administration/ledgerBookId=${normalizedBookId}`);
  }

  if (draft.implementationSandboxConfigured) {
    generated.push(...withImplementationSandboxEvidence([], profile, normalizedBookId));
  }

  return [...evidenceReferences, ...generated]
    .filter((item, index, items) => item.length > 0 && items.findIndex((candidate) => candidate.toLowerCase() === item.toLowerCase()) === index);
}

function hasTenantAdminBookEvidence(evidenceReferences: string[], ledgerBookId: string, marker: string): boolean {
  const normalizedBookId = ledgerBookId.toLowerCase();
  const normalizedMarker = marker.toLowerCase();
  return evidenceReferences.some((reference) => {
    const normalized = reference.toLowerCase();
    return normalized.includes(normalizedBookId) && normalized.includes(normalizedMarker);
  });
}

function withImplementationSandboxEvidence(
  evidenceReferences: string[],
  profile: AccountingTenantAdministrationProfile,
  ledgerBookId: string
): string[] {
  const tenantId = profile.tenantId || "tenant";
  const companyId = profile.companyId || "company";
  const generated = [
    `evidence://tenant-admin/${tenantId}/${companyId}/implementation-sandbox/ledgerBookId=${ledgerBookId}`,
    `evidence://tenant-admin/${tenantId}/${companyId}/sandbox-validation/ledgerBookId=${ledgerBookId}`,
    `evidence://tenant-admin/${tenantId}/${companyId}/fixture-validation/ledgerBookId=${ledgerBookId}`,
    `evidence://tenant-admin/${tenantId}/${companyId}/implementation-fixture/ledgerBookId=${ledgerBookId}`
  ];
  return [...evidenceReferences, ...generated]
    .filter((item, index, items) => item.length > 0 && items.findIndex((candidate) => candidate.toLowerCase() === item.toLowerCase()) === index);
}

function splitAccountingEvidenceReferences(value: string): string[] {
  return value
    .split(/[\n,]/)
    .map((item) => item.trim())
    .filter((item, index, items) => item.length > 0 && items.findIndex((candidate) => candidate.toLowerCase() === item.toLowerCase()) === index);
}

function buildAccountingTenantAdministrationControls(
  tenantAdministration: AccountingProductionReadiness["tenantAdministration"] | null | undefined
): AccountingTenantAdministrationControlViewModel[] {
  if (!tenantAdministration) {
    return [];
  }

  const controls: Array<[string, string, boolean]> = [
    ["tenant-scope", "Tenant scope", tenantAdministration.hasTenantScope],
    ["company-scope", "Company scope", tenantAdministration.hasCompanyScope],
    ["tenant-config", "Tenant config", tenantAdministration.tenantScopeConfigured],
    ["admin-roles", "Admin roles", tenantAdministration.adminRoleProfileConfigured],
    ["scoped-access", "Scoped access", tenantAdministration.scopedAccessPoliciesConfigured],
    ["reporting-groups", "Reporting groups", tenantAdministration.reportingGroupsConfigured],
    ["operator-surface", "Operator surface", tenantAdministration.accountingAdminSurfaceConfigured],
    ["browser-admin-studio", "Browser admin studio", tenantAdministration.browserAccountingAdminSurfaceConfigured ?? false],
    ["wpf-admin-studio", "WPF admin studio", tenantAdministration.wpfAccountingAdminSurfaceConfigured ?? false],
    ["chart-administration-studio", "Chart setup", tenantAdministration.chartAdministrationStudioConfigured ?? false],
    ["rule-test-promotion-studio", "Rule tests", tenantAdministration.ruleTestPromotionStudioConfigured ?? false],
    ["close-setup-studio", "Close setup", tenantAdministration.closeSetupStudioConfigured ?? false],
    ["provider-mapping-studio", "Provider mapping", tenantAdministration.providerMappingStudioConfigured ?? false],
    ["tenant-company-report-group-studio", "Tenant groups", tenantAdministration.tenantCompanyReportGroupSetupStudioConfigured ?? false],
    ["audit-review-tooling", "Audit review", tenantAdministration.auditReviewToolingConfigured ?? false],
    ["bulk-import-export-safeguards", "Bulk safeguards", tenantAdministration.bulkImportExportSafeguardsConfigured ?? false],
    ["performance-validation", "Performance proof", tenantAdministration.performanceValidationConfigured ?? false],
    ["disaster-recovery-runbook", "Recovery runbook", tenantAdministration.disasterRecoveryRunbookConfigured ?? false],
    ["ledger-book-administration-studio", "Book admin", tenantAdministration.ledgerBookAdministrationStudioConfigured ?? false],
    ["posting-rule-authoring-studio", "Rule authoring", tenantAdministration.postingRuleAuthoringStudioConfigured ?? false],
    ["approval-queue-studio", "Approvals", tenantAdministration.approvalQueueStudioConfigured ?? false],
    ["dimension-mapping-studio", "Dimension maps", tenantAdministration.dimensionMappingStudioConfigured ?? false],
    ["implementation-sandbox", "Sandbox proof", tenantAdministration.implementationSandboxConfigured ?? false],
    ["retained-evidence", "Setup evidence", tenantAdministration.hasRetainedEvidence]
  ];

  return controls.map(([id, label, complete]) => ({
    id,
    label,
    statusLabel: complete ? "Ready" : "Missing",
    tone: complete ? "success" : "danger"
  }));
}

function buildAccountingMigrationRunArtifactRows(artifacts: AccountingMigrationRunArtifact[]): AccountingMigrationRunArtifactViewModel[] {
  return [...artifacts]
    .sort((left, right) => right.startedAtUtc.localeCompare(left.startedAtUtc) || left.runId.localeCompare(right.runId))
    .slice(0, 6)
    .map((artifact) => {
      const scopeParts = [
        artifact.fundProfileId ? `Fund ${artifact.fundProfileId}` : "Fund default-fund",
        artifact.ledgerBookId ? `Book ${artifact.ledgerBookId}` : "Fund-level"
      ];
      const dimensionScope = formatLedgerDimensionSet(artifact.dimensions).join(", ");
      return {
        id: artifact.runId,
        title: artifact.summary?.trim() || artifact.runId,
        detail: `${scopeParts.join(" | ")}${dimensionScope ? ` | Dimensions ${dimensionScope}` : ""} | Started ${formatDateTimeLabel(artifact.startedAtUtc)}${artifact.completedAtUtc ? ` | Completed ${formatDateTimeLabel(artifact.completedAtUtc)}` : ""}`,
        statusLabel: formatMigrationRunStatus(artifact.status),
        kindLabel: formatMigrationRunKind(artifact.kind),
        recordCountLabel: `${artifact.migratedRecordCount} record${artifact.migratedRecordCount === 1 ? "" : "s"}`,
        issueCountLabel: `${artifact.issueCount} issue${artifact.issueCount === 1 ? "" : "s"}`,
        evidenceLabel: `${artifact.evidenceReferences.length} evidence reference${artifact.evidenceReferences.length === 1 ? "" : "s"}`,
        tone: migrationRunStatusTone(artifact.status)
      };
    });
}

function buildAccountingMigrationWorkerPlanRows(plans: AccountingMigrationRunWorkerPlan[]): AccountingMigrationWorkerPlanViewModel[] {
  return [...plans]
    .sort((left, right) => left.kind.localeCompare(right.kind) || left.planId.localeCompare(right.planId))
    .slice(0, 6)
    .map((plan) => {
      const reconciled = plan.sourceRecordCount === plan.migratedRecordCount;
      const dimensionScope = formatLedgerDimensionSet(plan.dimensions).join(", ");
      return {
        id: plan.planId,
        title: plan.summary?.trim() || plan.planId,
        detail: dimensionScope ? `Dimensions ${dimensionScope}` : "No retained dimension envelope",
        kindLabel: formatMigrationRunKind(plan.kind),
        countLabel: `${formatCount(plan.sourceRecordCount, "source record")} -> ${formatCount(plan.migratedRecordCount, "migrated record")}`,
        evidenceLabel: `${plan.evidenceReferences.length} evidence reference${plan.evidenceReferences.length === 1 ? "" : "s"}`,
        scopeLabel: `Tenant ${plan.tenantId?.trim() || "context"} | company ${plan.companyId?.trim() || "context"} | fund ${plan.fundProfileId || "default-fund"} | book ${plan.ledgerBookId || "missing"}`,
        tone: reconciled ? "success" : "danger"
      };
    });
}

function buildAccountingMigrationRolloutPlanRows(plan: AccountingMigrationRolloutPlanItem[]): AccountingMigrationRolloutPlanItemViewModel[] {
  return plan.map((item) => {
    const tone = productionReadinessStatusTone(item.status);
    const latestRunLabel = item.latestRunId
      ? `${item.latestRunId} | ${item.latestRunStatus ? formatMigrationRunStatus(item.latestRunStatus) : "status unknown"}`
      : "No retained run";
    return {
      id: item.code,
      title: item.label,
      statusLabel: formatProductionReadinessStatus(item.status),
      certificationLabel: item.certified ? "Certified" : "Not certified",
      scopeLabel: item.scopeLabel,
      latestRunLabel,
      metricsLabel: `${formatCount(item.migratedRecordCount, "record")} | ${formatCount(item.issueCount, "issue")}`,
      evidenceLabel: `${item.evidenceReferences.length} evidence reference${item.evidenceReferences.length === 1 ? "" : "s"}`,
      requiredAction: item.requiredAction,
      blockingIssueLabel: item.blockingIssueCodes.length > 0
        ? item.blockingIssueCodes.join(", ")
        : "No blocking migration issues",
      tone
    };
  });
}

function formatMigrationRunKind(kind: AccountingMigrationRunArtifact["kind"]): string {
  switch (kind) {
    case "LedgerBookScope":
      return "Ledger-book scope";
    case "HistoricalJournalBackfill":
      return "Historical journal backfill";
    case "DimensionalBackfill":
      return "Dimensional backfill";
    case "AccountingConfigurationPromotion":
      return "Configuration promotion";
    case "CloseReportingEvidence":
      return "Close/reporting evidence";
    default:
      return String(kind);
  }
}

function formatMigrationRunStatus(status: AccountingMigrationRunArtifact["status"]): string {
  return status === "Completed" ? "Completed" : status === "Certified" ? "Certified" : status === "Failed" ? "Failed" : status === "Running" ? "Running" : "Planned";
}

function migrationRunStatusTone(status: AccountingMigrationRunArtifact["status"]): AccountingMigrationRunArtifactViewModel["tone"] {
  switch (status) {
    case "Certified":
    case "Completed":
      return "success";
    case "Failed":
      return "danger";
    case "Running":
      return "warning";
    case "Planned":
    default:
      return "default";
  }
}

function formatProductionReadinessStatus(status: AccountingProductionReadiness["status"]): string {
  switch (status) {
    case "Ready":
      return "Ready";
    case "ReviewRequired":
      return "Review required";
    case "Blocked":
      return "Blocked";
    case "Unavailable":
      return "Unavailable";
    default:
      return String(status);
  }
}

function formatProductionReadinessArea(area: string): string {
  return area
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/\bGl\b/g, "GL");
}

function productionReadinessStatusTone(status: AccountingProductionReadiness["status"]): AccountingProductionReadinessComponentViewModel["tone"] {
  switch (status) {
    case "Ready":
      return "success";
    case "Blocked":
    case "Unavailable":
      return "danger";
    case "ReviewRequired":
    default:
      return "warning";
  }
}

type LedgerScopeScalarKey = Exclude<keyof NonNullable<PostingRule["scope"]>, "externalGlDimensions">;

const LEDGER_SCOPE_SCALAR_KEYS: LedgerScopeScalarKey[] = [
  "fundId",
  "entityId",
  "sleeveId",
  "strategyId",
  "investorId",
  "capitalAccountId",
  "instrumentId",
  "taxLotId",
  "costCenterId",
  "counterpartyId"
];

function mergeRuleScopeFromGeneratedPostings(
  scope: PostingRule["scope"],
  generatedPostings: readonly GeneratedPostingLine[]
): PostingRule["scope"] {
  const merged = cloneLedgerDimensionSet(scope) ?? {};

  for (const key of LEDGER_SCOPE_SCALAR_KEYS) {
    const existing = normalizeDimensionValue(merged[key]);
    if (existing) {
      continue;
    }

    const values = new Set(
      generatedPostings
        .map((posting) => normalizeDimensionValue(posting.dimensions?.[key]))
        .filter((value): value is string => Boolean(value))
    );
    if (values.size === 1) {
      merged[key] = [...values][0];
    }
  }

  const externalGlDimensions = { ...(merged.externalGlDimensions ?? {}) };
  const externalKeys = new Set<string>();
  for (const posting of generatedPostings) {
    for (const key of Object.keys(posting.dimensions?.externalGlDimensions ?? {})) {
      externalKeys.add(key);
    }
  }

  for (const key of externalKeys) {
    if (normalizeDimensionValue(externalGlDimensions[key])) {
      continue;
    }

    const values = new Set(
      generatedPostings
        .map((posting) => normalizeDimensionValue(posting.dimensions?.externalGlDimensions?.[key]))
        .filter((value): value is string => Boolean(value))
    );
    if (values.size === 1) {
      externalGlDimensions[key] = [...values][0];
    }
  }

  merged.externalGlDimensions = Object.keys(externalGlDimensions).length > 0 ? externalGlDimensions : null;
  return merged;
}

function ledgerDimensionSetsEqual(left: PostingRule["scope"], right: PostingRule["scope"]): boolean {
  const normalizedLeft = normalizeLedgerDimensionSet(left);
  const normalizedRight = normalizeLedgerDimensionSet(right);
  return JSON.stringify(normalizedLeft) === JSON.stringify(normalizedRight);
}

function resolveDryRunFormulaCandidate(
  rule: PostingRule,
  dryRun: RuleDryRunResult
): { formulaId: string; amount: number; currency: string } | null {
  const formulaIds = new Set((rule.formulas ?? []).map((formula) => formula.formulaId));
  for (const posting of dryRun.generatedPostingLines ?? []) {
    if (formulaIds.has(posting.amountFormulaId) && posting.amount > 0) {
      return {
        formulaId: posting.amountFormulaId,
        amount: posting.amount,
        currency: posting.currency
      };
    }
  }

  return null;
}

function mergeAllocationTargetsFromGeneratedPostings(
  allocations: readonly AllocationRule[],
  generatedPostings: readonly GeneratedPostingLine[]
): AllocationRule[] | null {
  if (allocations.length === 0 || generatedPostings.length === 0) {
    return null;
  }

  let changed = false;
  const nextAllocations = allocations.map((allocation) => {
    const matchingPostings = allocation.formulaId
      ? generatedPostings.filter((posting) => posting.amountFormulaId === allocation.formulaId)
      : generatedPostings;
    if (matchingPostings.length === 0) {
      return {
        ...allocation,
        targetDimensions: cloneLedgerDimensionSet(allocation.targetDimensions)
      };
    }

    const targetDimensions = mergeRuleScopeFromGeneratedPostings(
      allocation.targetDimensions,
      matchingPostings
    );
    if (!ledgerDimensionSetsEqual(allocation.targetDimensions, targetDimensions)) {
      changed = true;
    }

    return {
      ...allocation,
      targetDimensions
    };
  });

  return changed ? nextAllocations : null;
}

function normalizeLedgerDimensionSet(dimensions: PostingRule["scope"]): Record<string, string> {
  const normalized: Record<string, string> = {};
  if (!dimensions) {
    return normalized;
  }

  for (const key of LEDGER_SCOPE_SCALAR_KEYS) {
    const value = normalizeDimensionValue(dimensions[key]);
    if (value) {
      normalized[key] = value;
    }
  }

  for (const [key, value] of Object.entries(dimensions.externalGlDimensions ?? {}).sort(([left], [right]) => left.localeCompare(right))) {
    const normalizedValue = normalizeDimensionValue(value);
    if (normalizedValue) {
      normalized[`externalGlDimensions.${key}`] = normalizedValue;
    }
  }

  return normalized;
}

function normalizeDimensionValue(value: string | null | undefined): string | null {
  const normalized = value?.trim();
  return normalized ? normalized : null;
}

function buildAccountingRulesStudioRuleViewModel(
  rule: PostingRule,
  isSelected: boolean,
  savedRuleTestCases: AccountingRuleTestCase[],
  ruleTestSuite: AccountingRuleTestSuiteResult | null,
  studioRule: AccountingRulesStudioRuleRow | null = null,
  promotionQueueItem: AccountingRulesStudioPromotionQueueItem | null = null
): AccountingRulesStudioRuleViewModel {
  const conditions = rule.conditions ?? [];
  const conditionGroups = rule.conditionGroups ?? [];
  const formulas = rule.formulas ?? [];
  const allocations = rule.allocations ?? [];
  const generatedPostings = rule.generatedPostings ?? [];
  const versions = rule.versions ?? [];
  const promotion = resolvePostingRulePromotion(rule);
  const needsApproval = studioRule
    ? studioRule.requiresPromotionApproval && !studioRule.isPromotionApproved
    : rule.requiresPromotionApproval && promotion?.approvalState !== "Approved";
  const usesGeneratedPostings = studioRule?.usesGeneratedPostings ?? generatedPostings.length > 0;
  const statusLabel = rule.isArchived
    ? "Archived"
    : needsApproval
      ? "Promotion review"
      : usesGeneratedPostings
        ? "Generated postings"
        : "Template mapping";

  return {
    id: rule.ruleId,
    title: rule.displayName,
    subtitle: `${rule.ruleVersion} | ${rule.description || "No description supplied."}`,
    eventLabel: rule.sourceEventType,
    effectiveLabel: formatRuleEffectiveRange(rule),
    priorityLabel: `Priority ${rule.priority ?? 0}`,
    scopeLabels: formatLedgerDimensionSet(rule.scope),
    conditionRows: conditions.length > 0 || conditionGroups.length > 0
      ? [
          ...conditions.map(formatAccountingRuleCondition),
          ...conditionGroups.map(formatAccountingRuleConditionGroup)
        ]
      : ["No predicates configured; rule falls back to source-event matching."],
    formulaRows: formulas.length > 0
      ? formulas.map(formatAccountingRuleFormula)
      : ["No formulas configured; legacy template amount mapping remains active."],
    allocationRows: allocations.length > 0
      ? allocations.map(formatAllocationRule)
      : ["No allocation split configured."],
    generatedPostingRows: generatedPostings.length > 0
      ? generatedPostings.map(formatGeneratedPostingLine)
      : [`Legacy template action: ${rule.templateId || "no template"}`],
    versionRows: versions.length > 0
      ? versions.map((version) => `${version.version} by ${version.createdBy} on ${formatDateOnly(version.createdAtUtc)} - ${version.changeSummary}`)
      : [`${rule.ruleVersion} is the only retained rule version.`],
    promotionReadiness: buildAccountingRulesStudioPromotionReadiness(
      rule,
      savedRuleTestCases,
      ruleTestSuite,
      studioRule,
      promotionQueueItem),
    promotionLabel: promotion
      ? `${promotion.approvalState} by ${promotion.approvedBy ?? promotion.requestedBy}`
      : rule.requiresPromotionApproval
        ? "Promotion approval required"
        : "Promotion approval not required",
    promotionTone: studioRule?.isPromotionApproved
      ? "success"
      : promotionQueueItem
        ? "warning"
        : promotionTone(promotion, rule.requiresPromotionApproval),
    statusLabel,
    statusTone: rule.isArchived
      ? "outline"
      : (studioRule?.criticalIssueCount ?? 0) > 0
        ? "danger"
        : needsApproval
          ? "warning"
          : "success",
    isSelected,
    selectAriaLabel: `Inspect accounting posting rule ${rule.displayName}`
  };
}

function resolvePostingRulePromotion(rule: PostingRule): PostingRule["promotionApproval"] {
  return rule.promotionApproval ?? rule.versions?.find((version) => version.promotionApproval)?.promotionApproval ?? null;
}

function buildAccountingRulesStudioPromotionReadiness(
  rule: PostingRule,
  savedRuleTestCases: AccountingRuleTestCase[],
  ruleTestSuite: AccountingRuleTestSuiteResult | null,
  studioRule: AccountingRulesStudioRuleRow | null = null,
  promotionQueueItem: AccountingRulesStudioPromotionQueueItem | null = null
): AccountingRulesStudioPromotionReadinessViewModel[] {
  const versions = rule.versions ?? [];
  const promotion = resolvePostingRulePromotion(rule);
  const promotionApproved = studioRule?.isPromotionApproved ?? isApprovedRulePromotion(promotion);
  const savedCases = savedRuleTestCases.filter((testCase) =>
    testCase.expectedRuleId === rule.ruleId &&
    testCase.expectedRuleVersion === (rule.ruleVersion ?? "v1"));
  const savedCaseCount = studioRule?.savedTestCaseCount ?? savedCases.length;
  const generatedPostings = rule.generatedPostings ?? [];
  const generatedPostingLineCount = studioRule?.generatedPostingLineCount ?? generatedPostings.length;
  const scopeRows = formatLedgerDimensionSet(rule.scope);
  const latestVersion = versions[0];
  const suiteTone: AccountingToolingTone = !ruleTestSuite
    ? "warning"
    : ruleTestSuite.failedCount > 0
      ? "danger"
      : ruleTestSuite.totalCount > 0
        ? "success"
        : "warning";
  const activationBlockedReason = studioRule && studioRule.canActivate
    ? "Rule satisfies current promotion gates."
    : promotionQueueItem
      ? promotionQueueItem.suggestedAction
      : rule.isArchived
        ? "Archived rules cannot be activated."
        : rule.requiresPromotionApproval && !promotionApproved
          ? "Promotion approval is required before activation."
          : rule.requiresPromotionApproval && savedCaseCount === 0
            ? "A saved regression case is required before activation."
            : (ruleTestSuite?.failedCount ?? 0) > 0
              ? "Latest regression suite has failing cases."
              : "Rule satisfies current promotion gates.";

  return [
    {
      id: "version-history",
      label: "Version history",
      value: versions.length > 0 ? `${versions.length} retained` : "Current only",
      detail: latestVersion
        ? `${latestVersion.version} by ${latestVersion.createdBy} on ${formatDateOnly(latestVersion.createdAtUtc)}.`
        : `${rule.ruleVersion} has no prior retained versions.`,
      tone: versions.length > 0 ? "success" : "warning"
    },
    ...(studioRule ? [{
      id: "server-readiness",
      label: "Shared readiness",
      value: studioRule.canActivate ? "Ready" : studioRule.canRequestPromotion ? "Promotion ready" : "Blocked",
      detail: `${studioRule.criticalIssueCount} critical, ${studioRule.warningIssueCount} warning, ${studioRule.savedTestCaseCount} saved test case(s).`,
      tone: studioRule.criticalIssueCount > 0 ? "danger" : studioRule.canActivate ? "success" : "warning"
    } satisfies AccountingRulesStudioPromotionReadinessViewModel] : []),
    ...(promotionQueueItem ? [{
      id: "promotion-queue",
      label: "Promotion queue",
      value: promotionQueueItem.approvalState ?? "Pending",
      detail: promotionQueueItem.suggestedAction,
      tone: promotionQueueItem.criticalIssueCount > 0 ? "danger" : "warning"
    } satisfies AccountingRulesStudioPromotionReadinessViewModel] : []),
    {
      id: "promotion-approval",
      label: "Promotion approval",
      value: promotionApproved
        ? "Approved"
        : rule.requiresPromotionApproval
          ? "Required"
          : "Not required",
      detail: promotion
        ? `${promotion.approvalState} by ${promotion.approvedBy ?? promotion.requestedBy}.`
        : rule.requiresPromotionApproval
          ? "No approval evidence has been attached."
          : "This rule can remain in draft configuration without a promotion gate.",
      tone: promotionApproved ? "success" : rule.requiresPromotionApproval ? "warning" : "default"
    },
    {
      id: "saved-regression",
      label: "Saved regression",
      value: savedCaseCount > 0
        ? `${savedCaseCount} saved`
        : "Missing",
      detail: savedCaseCount > 0
        ? `${savedCases[0]?.displayName ?? "Server-retained regression coverage"} anchors promotion coverage.`
        : "Save a dry-run regression case for this rule before promotion.",
      tone: savedCaseCount > 0 ? "success" : rule.requiresPromotionApproval ? "warning" : "default"
    },
    {
      id: "latest-suite",
      label: "Latest suite",
      value: ruleTestSuite
        ? `${ruleTestSuite.passedCount}/${ruleTestSuite.totalCount} passed`
        : "Not run",
      detail: ruleTestSuite
        ? `Executed ${ruleTestSuite.executedAtUtc} by ${ruleTestSuite.actor}.`
        : "Run rule tests to verify current rules against saved and generated cases.",
      tone: suiteTone
    },
    {
      id: "generated-postings",
      label: "Generated postings",
      value: generatedPostingLineCount > 0
        ? `${generatedPostingLineCount} lines`
        : "Template only",
      detail: generatedPostingLineCount > 0
        ? "Rule can generate explicit multi-line postings."
        : `Rule still maps to template ${rule.templateId || "none"}.`,
      tone: generatedPostingLineCount > 0 ? "success" : "warning"
    },
    {
      id: "scope-coverage",
      label: "Scope coverage",
      value: scopeRows.length > 0 ? `${scopeRows.length} scoped` : "Unscoped",
      detail: scopeRows.length > 0
        ? scopeRows.slice(0, 3).join(", ")
        : "No fund, entity, instrument, counterparty, or external GL dimensions are scoped.",
      tone: scopeRows.length > 0 ? "success" : "warning"
    },
    {
      id: "activation-gate",
      label: "Activation gate",
      value: activationBlockedReason === "Rule satisfies current promotion gates." ? "Ready" : "Blocked",
      detail: activationBlockedReason,
      tone: activationBlockedReason === "Rule satisfies current promotion gates." ? "success" : "warning"
    }
  ];
}

function buildAccountingRulesStudioDryRunViewModel(result: RuleDryRunResult): AccountingRulesStudioDryRunViewModel {
  const generatedPostingLines = result.generatedPostingLines ?? [];
  return {
    title: `${result.sourceEventType} dry run`,
    balanceLabel: result.isPostingBalanced
      ? `Balanced ${formatCurrency(result.eventAmount)} ${result.currency}`
      : `Unbalanced ${formatCurrency(result.eventAmount)} ${result.currency}`,
    selectedRuleLabel: result.selectedRuleId ? `Selected rule ${result.selectedRuleId}` : "No rule selected",
    matchRows: result.ruleMatches.map(formatRuleDryRunMatch),
    generatedLineRows: result.generatedLines.map((line, index) =>
      `${index + 1}. ${line.side} ${line.accountPath} ${formatCurrency(line.amount)} ${line.currency} - ${line.description ?? line.accountName}`
    ),
    generatedPostingRows: generatedPostingLines.length > 0
      ? generatedPostingLines.map(formatGeneratedPostingLine)
      : ["Dry run returned journal preview lines without generated posting-line metadata."],
    validationRows: result.validationIssues.map<AccountingConfigurationIssueViewModel>((issue, index) => ({
      id: `${issue.code}-${issue.targetId ?? index}`,
      label: `${issue.severity} | ${issue.code}`,
      message: issue.message,
      detail: issue.suggestedAction ?? issue.targetId ?? "No additional action supplied.",
      tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
    }))
  };
}

function buildAccountingRulesStudioJournalCandidateViewModel(
  result: PostingRuleJournalCandidateResult
): AccountingRulesStudioJournalCandidateViewModel {
  const command = result.postingCommand;
  const commandLabel = command
    ? `Draft command ${command.commandId} is ${command.approvalState.toLowerCase()} and remains lifecycle-gated.`
    : "No posting command was produced.";
  const approvalLabel = command
    ? `Approval ${command.approvalState}; post-without-approval ${result.canPostWithoutAdditionalApproval ? "allowed" : "blocked"}.`
    : "Approval state unavailable.";

  return {
    title: "Governed journal draft candidate",
    selectedRuleLabel: result.selectedRuleId
      ? `Selected rule ${result.selectedRuleId}${result.selectedRuleVersion ? ` version ${result.selectedRuleVersion}` : ""}`
      : "No selected posting rule",
    balanceLabel: result.isBalanced
      ? `Balanced ${formatCurrency(result.totalDebits)} / ${formatCurrency(result.totalCredits)}`
      : `Unbalanced by ${formatCurrency(result.imbalance)}`,
    commandLabel,
    approvalLabel,
    evidenceLabel: `${result.evidenceLinks.length} evidence link${result.evidenceLinks.length === 1 ? "" : "s"}`,
    generatedLineRows: result.generatedPostingLines.length > 0
      ? result.generatedPostingLines.map(formatGeneratedPostingLine)
      : ["No generated posting lines were converted to a draft candidate."],
    issueRows: result.issues.map<AccountingConfigurationIssueViewModel>((issue, index) => ({
      id: `${issue.code}-${issue.targetId ?? index}`,
      label: `${issue.severity} | ${issue.code}${issue.blocksCandidate ? " | blocking" : ""}`,
      message: issue.message,
      detail: issue.suggestedAction ?? issue.targetId ?? "No additional action supplied.",
      tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
    }))
  };
}

function buildAccountingRulesStudioTestCaseViewModel(testCase: AccountingRuleTestCase): AccountingRulesStudioTestCaseViewModel {
  const request = testCase.request;
  const expectedRule = testCase.expectedRuleId ?? "any matching rule";
  const expectedVersion = testCase.expectedRuleVersion ? ` version ${testCase.expectedRuleVersion}` : "";
  const expectedIssueCodes = testCase.expectedIssueCodes ?? [];
  const expectedGeneratedPostings = testCase.expectedGeneratedPostingLines?.length ?? 0;
  const evidenceCount = testCase.evidenceLinks?.length ?? 0;
  const expectedIssues = expectedIssueCodes.length > 0
    ? `${expectedIssueCodes.length} expected issue code${expectedIssueCodes.length === 1 ? "" : "s"}`
    : "no expected issue codes";
  const expectedPostings = expectedGeneratedPostings > 0
    ? `${expectedGeneratedPostings} expected generated posting line${expectedGeneratedPostings === 1 ? "" : "s"}`
    : "no expected generated posting lines";
  return {
    id: testCase.testCaseId,
    title: testCase.displayName,
    subtitle: `${request.sourceEventType} | ${request.effectiveDate} | ${formatCurrency(request.eventAmount)} ${request.currency}`,
    assertionLabel: `Expect ${expectedRule}${expectedVersion}, ${testCase.expectBalancedPosting ? "balanced" : "unbalanced"}, ${expectedIssues}, ${expectedPostings}.`,
    evidenceLabel: evidenceCount > 0
      ? `${evidenceCount} evidence link${evidenceCount === 1 ? "" : "s"}`
      : "Evidence required",
    evidenceTone: evidenceCount > 0 ? "success" : "warning"
  };
}

function isApprovedRulePromotion(approval: PostingRule["promotionApproval"]): boolean {
  return approval?.approvalState === "Approved" &&
    Boolean(approval.approvedBy) &&
    Boolean(approval.approvedAtUtc) &&
    (approval.evidenceLinks?.length ?? 0) > 0;
}

function buildAccountingRulesStudioTestSuiteViewModel(result: AccountingRuleTestSuiteResult): AccountingRulesStudioTestSuiteViewModel {
  const validationRows = result.results.flatMap((testCase) =>
    testCase.assertionIssues.map<AccountingConfigurationIssueViewModel>((issue, index) => ({
      id: `${testCase.testCaseId}-${issue.code}-${issue.targetId ?? index}`,
      label: `${testCase.displayName} | ${issue.severity} | ${issue.code}`,
      message: issue.message,
      detail: issue.suggestedAction ?? issue.targetId ?? "No additional action supplied.",
      tone: issue.severity === "Critical" ? "danger" : issue.severity === "Warning" ? "warning" : "default"
    }))
  );
  const statusTone: AccountingRulesStudioTestSuiteViewModel["statusTone"] = result.failedCount > 0
    ? "danger"
    : result.passedCount === result.totalCount
      ? "success"
      : "warning";

  return {
    title: "Accounting rule regression tests",
    summaryLabel: `${result.passedCount}/${result.totalCount} passed`,
    executedLabel: `${result.executedAtUtc} by ${result.actor}`,
    statusTone,
    resultRows: result.results.map((testCase) => {
      const selected = testCase.dryRunResult.selectedRuleId ?? "none";
      const balance = testCase.dryRunResult.isPostingBalanced ? "balanced" : "unbalanced";
      const issueCount = testCase.assertionIssues.length;
      return `${testCase.passed ? "Pass" : "Fail"}: ${testCase.displayName} selected ${selected}, ${balance}${issueCount > 0 ? `, ${issueCount} assertion issue(s)` : ""}.`;
    }),
    validationRows
  };
}

function buildAccountingRuleTestCase(
  workspace: AccountingConfigurationWorkspace,
  rule: PostingRule
): AccountingRuleTestCase {
  return {
    testCaseId: `rule-test-${rule.ruleId}`,
    displayName: `${rule.displayName} regression`,
    request: {
      fundProfileId: workspace.fundProfileId,
      ledgerBookId: workspace.ledgerBookId ?? null,
      sourceEventType: rule.sourceEventType,
      eventAmount: resolveDryRunEventAmount(rule),
      currency: resolveDryRunCurrency(rule),
      effectiveDate: resolveDryRunEffectiveDate(rule),
      actor: "browser-accounting-operator",
      dimensions: rule.scope ?? null,
      counterpartyId: rule.scope?.counterpartyId ?? null,
      instrumentSymbol: null,
      correlationId: `browser-accounting-rule-test-${rule.ruleId}`
    },
    expectedRuleId: rule.ruleId,
    expectedRuleVersion: rule.ruleVersion ?? "v1",
    expectBalancedPosting: true,
    expectedIssueCodes: []
  };
}

function buildBrowserGuid(timestamp: number, discriminator: number): string {
  const suffix = Math.abs(timestamp + discriminator).toString(16).padStart(12, "0").slice(-12);
  const group = discriminator.toString(16).padStart(4, "0").slice(-4);
  return `00000000-0000-4000-8000-${suffix.slice(0, 8)}${group}`;
}

function resolveRuleAccountingBasis(workspace: AccountingConfigurationWorkspace): AccountingBasisKind {
  return workspace.ledgerBooks.find((book) => book.ledgerBookId === workspace.ledgerBookId)?.accountingBasis ??
    workspace.ledgerBooks[0]?.accountingBasis ??
    "Primary";
}

function resolveRulePolicyId(workspace: AccountingConfigurationWorkspace): string | null {
  return workspace.ledgerBooks.find((book) => book.ledgerBookId === workspace.ledgerBookId)?.accountingPolicyId ??
    workspace.ledgerBooks[0]?.accountingPolicyId ??
    null;
}

function formatAccountingRuleCondition(condition: NonNullable<PostingRule["conditions"]>[number]): string {
  const value = condition.secondValue
    ? `${condition.value ?? "missing"} -> ${condition.secondValue}`
    : condition.value ?? "present";
  const required = condition.isRequired ? "required" : "optional";
  return `${condition.field} ${condition.operator} ${value} (${required})${condition.description ? ` - ${condition.description}` : ""}`;
}

function formatAccountingRuleConditionGroup(group: NonNullable<PostingRule["conditionGroups"]>[number]): string {
  const required = group.isRequired ? "required" : "optional";
  const conditions = group.conditions.length > 0
    ? group.conditions.map(formatAccountingRuleCondition).join("; ")
    : "no child predicates";
  return `${group.groupId}: ${group.operator} (${required}) -> ${conditions}${group.description ? ` - ${group.description}` : ""}`;
}

function formatAccountingRuleFormula(formula: NonNullable<PostingRule["formulas"]>[number]): string {
  return `${formula.formulaId}: ${formula.kind} ${formatCurrency(formula.value)} ${formula.currency}${formula.description ? ` - ${formula.description}` : ""}`;
}

function formatAllocationRule(allocation: AllocationRule): string {
  const scope = formatLedgerDimensionSet(allocation.targetDimensions).join(", ");
  return `${allocation.allocationRuleId}: ${allocation.basis} weight ${allocation.weight}${allocation.formulaId ? ` via ${allocation.formulaId}` : ""}${scope ? ` -> ${scope}` : ""}`;
}

function formatGeneratedPostingLine(line: GeneratedPostingLine): string {
  const scope = formatLedgerDimensionSet(line.dimensions).join(", ");
  return `${line.side} ${line.accountPath} ${formatCurrency(line.amount)} ${line.currency} via ${line.amountFormulaId}${scope ? ` | ${scope}` : ""}${line.description ? ` - ${line.description}` : ""}`;
}

function formatRuleDryRunMatch(match: AccountingRuleDryRunMatch): string {
  const state = match.isMatched ? "matched" : "skipped";
  const reasons = match.explanations.length > 0 ? match.explanations.join("; ") : "No explanation returned.";
  const issues = match.validationIssues.length > 0 ? ` Issues: ${match.validationIssues.map((issue) => issue.code).join(", ")}.` : "";
  return `${match.displayName} ${state} at priority ${match.priority}. ${reasons}${issues}`;
}

function formatRuleEffectiveRange(rule: PostingRule): string {
  const start = rule.effectiveFrom ?? "open start";
  const end = rule.effectiveTo ?? "open end";
  return `${start} -> ${end}`;
}

function formatLedgerDimensionSet(dimensions?: PostingRule["scope"]): string[] {
  if (!dimensions) {
    return [];
  }

  const rows: string[] = [];
  const scalarEntries: Array<[string, string | null | undefined]> = [
    ["Fund", dimensions.fundId],
    ["Entity", dimensions.entityId],
    ["Sleeve", dimensions.sleeveId],
    ["Strategy", dimensions.strategyId],
    ["Investor", dimensions.investorId],
    ["Capital account", dimensions.capitalAccountId],
    ["Instrument", dimensions.instrumentId],
    ["Tax lot", dimensions.taxLotId],
    ["Cost center", dimensions.costCenterId],
    ["Counterparty", dimensions.counterpartyId],
    ["Organization", dimensions.organizationId],
    ["Portfolio", dimensions.portfolioId],
    ["Book", dimensions.bookId],
    ["Account", dimensions.accountId],
    ["Customer", dimensions.customerId],
    ["Vendor", dimensions.vendorId],
    ["Project", dimensions.projectId]
  ];
  for (const [label, value] of scalarEntries) {
    if (value) {
      rows.push(`${label}: ${value}`);
    }
  }

  for (const [key, value] of Object.entries(dimensions.externalGlDimensions ?? {})) {
    rows.push(`External ${key}: ${value}`);
  }

  return rows;
}

function promotionTone(
  promotion: PostingRule["promotionApproval"],
  requiresPromotionApproval?: boolean
): AccountingRulesStudioRuleViewModel["promotionTone"] {
  if (promotion?.approvalState === "Approved") {
    return "success";
  }

  if (promotion?.approvalState === "Rejected") {
    return "danger";
  }

  if (requiresPromotionApproval || promotion) {
    return "warning";
  }

  return "outline";
}

function resolveDryRunEventAmount(rule: PostingRule): number {
  const sourceAmountFormula = rule.formulas?.find((formula) => formula.kind === "SourceAmount");
  if (sourceAmountFormula && sourceAmountFormula.value > 0) {
    return sourceAmountFormula.value;
  }

  const generatedAmount = rule.generatedPostings?.find((line) => line.amount > 0)?.amount;
  return generatedAmount && generatedAmount > 0 ? generatedAmount : 1000;
}

function resolveDryRunCurrency(rule: PostingRule): string {
  return rule.generatedPostings?.find((line) => line.currency)?.currency
    ?? rule.formulas?.find((formula) => formula.currency)?.currency
    ?? "USD";
}

function resolveDryRunEffectiveDate(rule: PostingRule): string {
  if (rule.effectiveFrom) {
    return rule.effectiveFrom;
  }

  return new Date().toISOString().slice(0, 10);
}

