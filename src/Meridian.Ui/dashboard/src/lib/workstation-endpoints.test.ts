import { describe, expect, it } from "vitest";
import {
  AUTH_API_ENDPOINTS,
  BACKFILL_API_ENDPOINTS,
  EXECUTION_API_ENDPOINTS,
  EXPORT_API_ENDPOINTS,
  FUND_STRUCTURE_API_ENDPOINTS,
  PROVIDER_API_ENDPOINTS,
  PROMOTION_API_ENDPOINTS,
  PORTFOLIO_API_ENDPOINTS,
  QUALITY_API_ENDPOINTS,
  QUANT_API_ENDPOINTS,
  RECONCILIATION_API_ENDPOINTS,
  REPLAY_API_ENDPOINTS,
  PROVIDER_ROUTING_API_ENDPOINTS,
  SECURITY_MASTER_API_ENDPOINTS,
  SYMBOL_API_ENDPOINTS,
  STRATEGY_DESIGNER_API_ENDPOINTS,
  WORKSTATION_API_ENDPOINTS,
  WORKSTATION_API_ENDPOINT_TEMPLATES,
  backfillCheckpointEndpoint,
  backfillCheckpointPendingEndpoint,
  backfillCheckpointResumeEndpoint,
  brokerageConnectionConnectEndpoint,
  brokerageConnectionEndpoint,
  brokerageConnectionStatusEndpoint,
  coveredCallRunCancelEndpoint,
  coveredCallRunEndpoint,
  coveredCallRunResultEndpoint,
  coveredCallRunStatusEndpoint,
  coveredCallRunsEndpoint,
  exportPreviewEndpoint,
  reportPackDeliveryPackageEndpoint,
  reportPackDeliveryPortalPackageEndpoint,
  reportPackEvidenceBundleEndpoint,
  reportingRunAuditTrailEndpoint,
  reportingRunReportWriterGridEndpoint,
  executionAuditEndpoint,
  executionManualOverrideClearEndpoint,
  executionOrderCancelEndpoint,
  executionPositionCloseEndpoint,
  executionSessionCloseEndpoint,
  executionSessionEndpoint,
  executionSessionReplayEndpoint,
  executionSymbolPositionLimitEndpoint,
  historicalBarsEndpoint,
  marketDataOrderbookEndpoint,
  marketDataQuoteEndpoint,
  marketDataQuotesSnapshotEndpoint,
  marketDataTradesEndpoint,
  portfolioHouseholdEndpoint,
  portfolioSymbolExposureEndpoint,
  promotionEvaluateEndpoint,
  providerRemoveEndpoint,
  providerTestEndpoint,
  qualityAnomalyAcknowledgeEndpoint,
  reconciliationBreakAssignEndpoint,
  reconciliationBreakAuditEndpoint,
  reconciliationBreakBulkDryRunEndpoint,
  reconciliationBreakBulkExecuteEndpoint,
  reconciliationBreakBulkResultEndpoint,
  reconciliationBreakBulkStatusEndpoint,
  reconciliationBreakCommentEndpoint,
  reconciliationBreakCommentsEndpoint,
  reconciliationBreakEndpoint,
  reconciliationBreakQueueEndpoint,
  reconciliationBreakReopenEndpoint,
  reconciliationBreakResolutionEndpoint,
  reconciliationBreakResolveEndpoint,
  reconciliationBreakReviewEndpoint,
  reconciliationBreakRootCauseEndpoint,
  reconciliationBreakSignOffEndpoint,
  reconciliationBreakTransitionEndpoint,
  reconciliationRunEndpoint,
  reconciliationStatementRunEndpoint,
  replayFilesEndpoint,
  replaySessionActionEndpoint,
  securityMasterAssetProfileApproveEndpoint,
  securityMasterAssetProfileDraftsEndpoint,
  securityMasterAssetProfileLineageEndpoint,
  securityMasterAssetProfileRollbackEndpoint,
  securityMasterAssetProfilesEndpoint,
  securityMasterAliasUpsertEndpoint,
  securityMasterAmendEndpoint,
  securityMasterConflictsEndpoint,
  securityMasterConflictResolveEndpoint,
  securityMasterCorporateActionsEndpoint,
  securityMasterEntryEndpoint,
  securityMasterTradingParametersEndpoint,
  strategyActionEndpoint,
  strategyDesignerDraftEndpoint,
  strategyRunsEndpoint,
  symbolArchiveEndpoint,
  symbolRemoveEndpoint,
  symbolSearchEndpoint,
  workstationEvidenceExportManifestEndpoint,
  workstationEvidenceGraphEndpoint,
  workstationEvidencePacketEndpoint,
  workstationEvidenceValidateEndpoint,
  workstationExtensibilityTenantTemplateActivateEndpoint,
  workstationExtensibilityTenantTemplateActivationsEndpoint,
  workstationExtensibilityTenantTemplateEndpoint,
  workstationExtensibilityTenantTemplateReadinessEndpoint,
  workstationFinancialRecordExplorerEndpoint,
  workstationFinancialRecordExplorerRecordEndpoint,
  workstationFinancialRecordExplorerSavedViewsEndpoint,
  workstationChiefOfStaffDecisionEndpoint,
  workstationChiefOfStaffHealthEndpoint,
  workstationChiefOfStaffSessionEndpoint,
  workstationChiefOfStaffSessionsEndpoint,
  workstationChiefOfStaffTraceExportEndpoint,
  workstationOperatorInboxEndpoint,
  workstationAssetOperationsEndpoint,
  workstationOperationsContinuityApprovalSubmitEndpoint,
  workstationOperationsContinuityBrokerImportEndpoint,
  workstationOperationsContinuityBrokerNormalizeEndpoint,
  workstationOperationsContinuityBreakAssignEndpoint,
  workstationOperationsContinuityBreakResolveEndpoint,
  workstationOperationsContinuityBreaksEndpoint,
  workstationOperationsContinuityChecklistEndpoint,
  workstationOperationsContinuityChecklistAcknowledgeEndpoint,
  workstationOperationsContinuityCloseEndpoint,
  workstationOperationsContinuityDetailEndpoint,
  workstationOperationsContinuityEndpoint,
  workstationOperationsContinuityCloseCalendarEndpoint,
  workstationOperationsContinuityCloseReadinessEndpoint,
  workstationOperationsContinuityLedgerDraftEndpoint,
  workstationOperationsContinuityLedgerPreviewEndpoint,
  workstationOperationsContinuityLedgerPostEndpoint,
  workstationOperationsContinuityLedgerValidateEndpoint,
  workstationOperationsContinuityPostureRefreshEndpoint,
  workstationOperationsPrivateCapitalCloseCockpitEndpoint,
  workstationOperationsContinuityReconciliationRunEndpoint,
  workstationOperationsContinuityReopenEndpoint,
  workstationOperationsContinuitySecurityMasterOverrideApproveEndpoint,
  workstationOperationsContinuitySecurityMasterResolveEndpoint,
  workstationOperationsContinuityTimelineEndpoint,
  workstationRunAttributionEndpoint,
  workstationRunCompareEndpoint,
  workstationRunContinuityEndpoint,
  workstationRunDiffEndpoint,
  workstationRunEquityCurveEndpoint,
  workstationRunFillsEndpoint,
  workstationRunHistoryEndpoint,
  workstationRunLedgerEndpoint,
  workstationRunLedgerJournalEndpoint,
  workstationRunLedgerTrialBalanceEndpoint,
  workstationRunReconciliationEndpoint,
  workstationRunReconciliationHistoryEndpoint,
  workstationRunReviewPacketEndpoint,
  workstationRunSweepsEndpoint,
  workstationRunTimelineEndpoint,
  workstationSecurityMasterEconomicDefinitionEndpoint,
  workstationSecurityMasterEntryEndpoint,
  workstationSecurityMasterHistoryEndpoint,
  workstationSecurityMasterIdentityEndpoint,
  workstationSecurityMasterInstrumentPassportEndpoint,
  workstationSecurityMasterSearchEndpoint,
  workstationSecurityMasterTrustSnapshotEndpoint,
  workstationWorkflowSummaryEndpoint,
  workstationWorkflowPresetEndpoint,
  workstationWorkflowPresetPinEndpoint,
  workstationWorkflowPresetUsedEndpoint
} from "@/lib/workstation-endpoints";

describe("workstation API endpoint catalog", () => {
  it("keeps canonical workspace bootstrap endpoints in one shared catalog", () => {
    expect(WORKSTATION_API_ENDPOINTS).toMatchObject({
      session: "/api/workstation/session",
      strategy: "/api/workstation/strategy",
      strategyBriefing: "/api/workstation/strategy/briefing",
      trading: "/api/workstation/trading",
      tradingReadiness: "/api/workstation/trading/readiness",
      portfolio: "/api/workstation/portfolio",
      assetOperations: "/api/workstation/assets",
      data: "/api/workstation/data",
      accounting: "/api/workstation/accounting",
      privateCapitalActivity: "/api/ledger/private-capital/activity",
      privateCapitalFundEventRecord: "/api/ledger/private-capital/fund-event-record",
      privateCapitalFundEventCommandCenter: "/api/ledger/private-capital/fund-event-command-center",
      privateCapitalCapitalAccountSubledger: "/api/ledger/private-capital/capital-account-subledger",
      privateCapitalReportOutput: "/api/ledger/private-capital/report-output",
      reporting: "/api/workstation/reporting",
      workflowSummary: "/api/workstation/workflow-summary",
      extensibilityCatalog: "/api/workstation/extensibility/catalog",
      extensibilityTenantTemplates: "/api/workstation/extensibility/tenant-templates",
      extensibilityTenantTemplate: "/api/workstation/extensibility/tenant-templates/{tenantTemplateId}",
      extensibilityTenantTemplateActivate: "/api/workstation/extensibility/tenant-templates/{tenantTemplateId}/activate",
      extensibilityTenantTemplateActivations: "/api/workstation/extensibility/tenant-templates/{tenantTemplateId}/activations",
      extensibilityTenantTemplateReadiness: "/api/workstation/extensibility/tenant-templates/{tenantTemplateId}/readiness",
      operationsContinuity: "/api/workstation/operations/continuity",
      operationsContinuityApprovalPolicyMatrix: "/api/workstation/operations/continuity/approval-policy-matrix",
      operationsContinuityApprovalPolicyRules: "/api/workstation/operations/continuity/approval-policy-rules",
      operationsContinuityCloseCalendar: "/api/workstation/operations/continuity/close-calendar",
      operationsContinuityCloseCalendarItems: "/api/workstation/operations/continuity/close-calendar-items",
      chiefOfStaff: "/api/workstation/chief-of-staff",
      runHistory: "/api/workstation/runs/history",
      runTimeline: "/api/workstation/runs/timeline",
      runSweeps: "/api/workstation/runs/sweeps",
      evidenceSubjects: "/api/workstation/evidence/subjects",
      evidenceVaultSearch: "/api/workstation/evidence/vault/search",
      evidenceTemplates: "/api/workstation/evidence/templates"
    });
  });

  it("builds security-id scoped Asset Operations endpoints", () => {
    expect(workstationAssetOperationsEndpoint("security / 1")).toBe(
      "/api/workstation/assets/security%20%2F%201/operations"
    );
  });

  it("builds shared financial record explorer endpoints from generated route contracts", () => {
    expect(WORKSTATION_API_ENDPOINTS.financialRecordExplorer).toBe(
      "/api/workstation/financial-record-explorers/{explorerId}"
    );
    expect(WORKSTATION_API_ENDPOINTS.financialRecordExplorerRecord).toBe(
      "/api/workstation/financial-record-explorers/{explorerId}/records/{recordId}"
    );
    expect(WORKSTATION_API_ENDPOINTS.financialRecordExplorerSavedViews).toBe(
      "/api/workstation/financial-record-explorers/{explorerId}/saved-views"
    );
    expect(workstationFinancialRecordExplorerEndpoint("security-instrument")).toBe(
      "/api/workstation/financial-record-explorers/security-instrument"
    );
    expect(workstationFinancialRecordExplorerRecordEndpoint("portfolio", "account / 1")).toBe(
      "/api/workstation/financial-record-explorers/portfolio/records/account%20%2F%201"
    );
    expect(workstationFinancialRecordExplorerSavedViewsEndpoint("ledger")).toBe(
      "/api/workstation/financial-record-explorers/ledger/saved-views"
    );
  });

  it("builds core extensibility tenant template endpoints from generated route contracts", () => {
    expect(WORKSTATION_API_ENDPOINTS.extensibilityCatalog).toBe("/api/workstation/extensibility/catalog");
    expect(WORKSTATION_API_ENDPOINTS.extensibilityTenantTemplates).toBe(
      "/api/workstation/extensibility/tenant-templates"
    );
    expect(WORKSTATION_API_ENDPOINTS.extensibilityTenantTemplate).toBe(
      "/api/workstation/extensibility/tenant-templates/{tenantTemplateId}"
    );
    expect(WORKSTATION_API_ENDPOINTS.extensibilityTenantTemplateActivate).toBe(
      "/api/workstation/extensibility/tenant-templates/{tenantTemplateId}/activate"
    );
    expect(WORKSTATION_API_ENDPOINTS.extensibilityTenantTemplateActivations).toBe(
      "/api/workstation/extensibility/tenant-templates/{tenantTemplateId}/activations"
    );
    expect(WORKSTATION_API_ENDPOINTS.extensibilityTenantTemplateReadiness).toBe(
      "/api/workstation/extensibility/tenant-templates/{tenantTemplateId}/readiness"
    );
    expect(workstationExtensibilityTenantTemplateEndpoint("fund admin / v1")).toBe(
      "/api/workstation/extensibility/tenant-templates/fund%20admin%20%2F%20v1"
    );
    expect(workstationExtensibilityTenantTemplateActivateEndpoint("fund admin / v1")).toBe(
      "/api/workstation/extensibility/tenant-templates/fund%20admin%20%2F%20v1/activate"
    );
    expect(workstationExtensibilityTenantTemplateActivationsEndpoint("fund admin / v1")).toBe(
      "/api/workstation/extensibility/tenant-templates/fund%20admin%20%2F%20v1/activations"
    );
    expect(workstationExtensibilityTenantTemplateReadinessEndpoint("fund admin / v1")).toBe(
      "/api/workstation/extensibility/tenant-templates/fund%20admin%20%2F%20v1/readiness"
    );
  });

  it("builds account-scoped operator inbox endpoints without changing the base contract", () => {
    expect(workstationOperatorInboxEndpoint()).toBe("/api/workstation/operator/inbox");
    expect(workstationOperatorInboxEndpoint("fund account/1")).toBe(
      "/api/workstation/operator/inbox?fundAccountId=fund+account%2F1"
    );
    expect(workstationOperatorInboxEndpoint("  fund account/1  ")).toBe(
      "/api/workstation/operator/inbox?fundAccountId=fund+account%2F1"
    );
    expect(workstationOperatorInboxEndpoint("")).toBe("/api/workstation/operator/inbox");
  });

  it("builds workflow preset endpoints from the shared preset root", () => {
    expect(workstationWorkflowSummaryEndpoint()).toBe("/api/workstation/workflow-summary");
    expect(workstationWorkflowSummaryEndpoint({
      hasOperatingContext: true,
      operatingContext: "portfolio/review",
      fundProfileId: "fund / 1",
      fundAccountId: "account / 1",
      fundDisplayName: "Core Fund"
    })).toBe(
      "/api/workstation/workflow-summary?hasOperatingContext=true&operatingContext=portfolio%2Freview&fundProfileId=fund+%2F+1&fundAccountId=account+%2F+1&fundDisplayName=Core+Fund"
    );
    expect(workstationWorkflowPresetEndpoint()).toBe("/api/workstation/workflows/presets");
    expect(workstationWorkflowPresetEndpoint("preset / 1")).toBe("/api/workstation/workflows/presets/preset%20%2F%201");
    expect(workstationWorkflowPresetPinEndpoint("preset / 1")).toBe("/api/workstation/workflows/presets/preset%20%2F%201/pin");
    expect(workstationWorkflowPresetUsedEndpoint("preset / 1")).toBe("/api/workstation/workflows/presets/preset%20%2F%201/used");
  });

  it("builds operations continuity endpoints from the shared accounting close root", () => {
    expect(workstationOperationsContinuityEndpoint()).toBe("/api/workstation/operations/continuity");
    expect(workstationOperationsContinuityEndpoint({
      fundAccountId: "fund / 1",
      periodId: "2026-05",
      status: "Blocked"
    })).toBe(
      "/api/workstation/operations/continuity?fundAccountId=fund+%2F+1&periodId=2026-05&status=Blocked"
    );
    expect(workstationOperationsContinuityDetailEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201"
    );
    expect(workstationOperationsContinuityTimelineEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/timeline"
    );
    expect(workstationOperationsContinuityBreaksEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/breaks"
    );
    expect(workstationOperationsContinuityChecklistEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/checklist"
    );
    expect(workstationOperationsContinuityCloseReadinessEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/close-readiness"
    );
    expect(workstationOperationsContinuityBrokerImportEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/broker/import"
    );
    expect(workstationOperationsContinuityBrokerNormalizeEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/broker/normalize"
    );
    expect(workstationOperationsContinuityPostureRefreshEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/posture/refresh"
    );
    expect(workstationOperationsContinuitySecurityMasterResolveEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/security-master/resolve"
    );
    expect(workstationOperationsContinuitySecurityMasterOverrideApproveEndpoint("workflow / 1", "override / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/security-master/overrides/override%20%2F%201/approve"
    );
    expect(workstationOperationsContinuityLedgerDraftEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/ledger/draft"
    );
    expect(workstationOperationsContinuityLedgerValidateEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/ledger/validate"
    );
    expect(workstationOperationsContinuityLedgerPostEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/ledger/post"
    );
    expect(workstationOperationsContinuityReconciliationRunEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/reconciliation/run"
    );
    expect(workstationOperationsContinuityApprovalSubmitEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/approval/submit"
    );
    expect(workstationOperationsContinuityChecklistAcknowledgeEndpoint("workflow / 1", "task / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/checklist/task%20%2F%201/acknowledge"
    );
    expect(workstationOperationsContinuityBreakAssignEndpoint("workflow / 1", "break / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/reconciliation/breaks/break%20%2F%201/assign"
    );
    expect(workstationOperationsContinuityBreakResolveEndpoint("workflow / 1", "break / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/reconciliation/breaks/break%20%2F%201/resolve"
    );
    expect(workstationOperationsContinuityLedgerPreviewEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/ledger-preview"
    );
    expect(workstationOperationsContinuityCloseEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/close"
    );
    expect(workstationOperationsContinuityReopenEndpoint("workflow / 1")).toBe(
      "/api/workstation/operations/continuity/workflow%20%2F%201/reopen"
    );
    expect(workstationOperationsContinuityCloseCalendarEndpoint()).toBe(
      "/api/workstation/operations/continuity/close-calendar"
    );
    expect(workstationOperationsContinuityCloseCalendarEndpoint({
      fundAccountId: "fund / 1",
      periodId: "2026-05"
    })).toBe(
      "/api/workstation/operations/continuity/close-calendar?fundAccountId=fund+%2F+1&periodId=2026-05"
    );
    expect(workstationOperationsPrivateCapitalCloseCockpitEndpoint({
      fundProfileId: "fund-alpha",
      ledgerBookId: "11111111-1111-1111-1111-111111111111",
      fundAccountId: "fund / 1",
      periodId: "2026-05",
      entityId: "entity-master"
    })).toBe(
      "/api/workstation/operations/private-capital-close-cockpit?fundProfileId=fund-alpha&ledgerBookId=11111111-1111-1111-1111-111111111111&fundAccountId=fund+%2F+1&periodId=2026-05&entityId=entity-master"
    );
    expect(AUTH_API_ENDPOINTS.roles).toBe("/api/auth/roles");
    expect(AUTH_API_ENDPOINTS.roleProfiles).toBe("/api/auth/role-profiles");
    expect(AUTH_API_ENDPOINTS.accounts).toBe("/api/auth/accounts");
    expect(AUTH_API_ENDPOINTS.accountPasswordReset).toBe("/api/auth/accounts/{username}/password-reset");
    expect(AUTH_API_ENDPOINTS.accountDisable).toBe("/api/auth/accounts/{username}/disable");
    expect(AUTH_API_ENDPOINTS.sessionsRevoke).toBe("/api/auth/sessions/revoke");
    expect(AUTH_API_ENDPOINTS.audit).toBe("/api/auth/audit");
    expect(FUND_STRUCTURE_API_ENDPOINTS.setupDraftValidate).toBe("/api/fund-structure/setup-drafts/validate");
    expect(FUND_STRUCTURE_API_ENDPOINTS.setupDraftCreate).toBe("/api/fund-structure/setup-drafts/create");
    expect(FUND_STRUCTURE_API_ENDPOINTS.ledgerMappingWorkbench).toBe("/api/fund-structure/ledger-mapping-view");
    expect(FUND_STRUCTURE_API_ENDPOINTS.ledgerMappingAssignments).toBe("/api/fund-structure/ledger-mapping-assignments");
    expect(FUND_STRUCTURE_API_ENDPOINTS.transactionLabPreview).toBe("/api/fund-structure/accounting/transaction-lab/preview");
    expect(FUND_STRUCTURE_API_ENDPOINTS.reportPackWorkflowDeliveryPackage).toBe(
      "/api/fund-structure/reporting/packs/{reportId}/deliveries/{attemptId}/package"
    );
    expect(FUND_STRUCTURE_API_ENDPOINTS.reportPackDeliveryPortalPackage).toBe("/portal/reporting/packages/{packageId}");
    expect(WORKSTATION_API_ENDPOINTS.reportingStructuredExport).toBe("/api/workstation/reporting/structured-exports/{exportId}");
    expect(FUND_STRUCTURE_API_ENDPOINTS.reportingStructuredExport).toBe("/api/fund-structure/reporting/structured-exports/{exportId}");
    expect(FUND_STRUCTURE_API_ENDPOINTS.reportingRunAuditTrail).toBe("/api/fund-structure/reporting/runs/{runId}/audit");
    expect(reportingRunAuditTrailEndpoint("run / 1")).toBe(
      "/api/fund-structure/reporting/runs/run%20%2F%201/audit"
    );
    expect(FUND_STRUCTURE_API_ENDPOINTS.reportingRunReportWriterGrid).toBe(
      "/api/fund-structure/reporting/runs/{runId}/report-writer-grids/{gridId}"
    );
    expect(reportingRunReportWriterGridEndpoint("run / 1", "grid / 1")).toBe(
      "/api/fund-structure/reporting/runs/run%20%2F%201/report-writer-grids/grid%20%2F%201"
    );
    expect(reportingRunReportWriterGridEndpoint("run / 1", "grid / 1", "csv")).toBe(
      "/api/fund-structure/reporting/runs/run%20%2F%201/report-writer-grids/grid%20%2F%201?format=csv"
    );
    expect(reportingRunReportWriterGridEndpoint("run / 1", "grid / 1", "pdf")).toBe(
      "/api/fund-structure/reporting/runs/run%20%2F%201/report-writer-grids/grid%20%2F%201?format=pdf"
    );
    expect(reportPackDeliveryPackageEndpoint("report / 1", "attempt / 1", "tok / 1")).toBe(
      "/api/fund-structure/reporting/packs/report%20%2F%201/deliveries/attempt%20%2F%201/package?token=tok+%2F+1"
    );
    expect(reportPackDeliveryPortalPackageEndpoint("pkg / 1", "tok / 1")).toBe(
      "/portal/reporting/packages/pkg%20%2F%201?token=tok+%2F+1"
    );
  });

  it("builds Chief of Staff workstation endpoint routes", () => {
    expect(workstationChiefOfStaffSessionsEndpoint()).toBe("/api/workstation/chief-of-staff/sessions");
    expect(workstationChiefOfStaffSessionsEndpoint({
      workspace: "Reporting",
      fundProfileId: "fund / 1",
      status: "AwaitingOperatorDecision",
      limit: 10
    })).toBe(
      "/api/workstation/chief-of-staff/sessions?workspace=Reporting&fundProfileId=fund+%2F+1&status=AwaitingOperatorDecision&limit=10"
    );
    expect(workstationChiefOfStaffSessionEndpoint("session / 1")).toBe(
      "/api/workstation/chief-of-staff/sessions/session%20%2F%201"
    );
    expect(workstationChiefOfStaffDecisionEndpoint("session / 1")).toBe(
      "/api/workstation/chief-of-staff/sessions/session%20%2F%201/decisions"
    );
    expect(workstationChiefOfStaffTraceExportEndpoint("session / 1")).toBe(
      "/api/workstation/chief-of-staff/sessions/session%20%2F%201/export-trace"
    );
    expect(workstationChiefOfStaffHealthEndpoint()).toBe("/api/workstation/chief-of-staff/health");
  });

  it("builds run evidence endpoints and matching Settings templates", () => {
    expect(WORKSTATION_API_ENDPOINT_TEMPLATES).toMatchObject({
      runLedger: "/api/workstation/runs/{runId}/ledger",
      runContinuity: "/api/workstation/runs/{runId}/continuity",
      runReviewPacket: "/api/workstation/runs/{runId}/review-packet",
      runReconciliation: "/api/workstation/runs/{runId}/reconciliation"
    });
    expect(workstationRunLedgerEndpoint("run / 1")).toBe("/api/workstation/runs/run%20%2F%201/ledger");
    expect(workstationRunLedgerJournalEndpoint("run / 1")).toBe("/api/workstation/runs/run%20%2F%201/ledger/journal");
    expect(workstationRunLedgerJournalEndpoint("run / 1", { from: "2026-01-01", to: "2026-01-31" })).toBe(
      "/api/workstation/runs/run%20%2F%201/ledger/journal?from=2026-01-01&to=2026-01-31"
    );
    expect(workstationRunContinuityEndpoint("run / 1")).toBe("/api/workstation/runs/run%20%2F%201/continuity");
    expect(workstationRunReviewPacketEndpoint("run / 1", "fund / 1")).toBe(
      "/api/workstation/runs/run%20%2F%201/review-packet?fundAccountId=fund+%2F+1"
    );
    expect(workstationRunReconciliationEndpoint("run / 1")).toBe("/api/workstation/runs/run%20%2F%201/reconciliation");
    expect(workstationRunReconciliationHistoryEndpoint("run / 1")).toBe(
      "/api/workstation/runs/run%20%2F%201/reconciliation/history"
    );
    expect(workstationRunHistoryEndpoint({ mode: "paper", status: "Ready", limit: 10 })).toBe(
      "/api/workstation/runs/history?mode=paper&status=Ready&limit=10"
    );
    expect(workstationRunHistoryEndpoint({ mode: " paper ", status: "", limit: Number.NaN })).toBe(
      "/api/workstation/runs/history?mode=paper"
    );
    expect(workstationRunTimelineEndpoint({ strategyId: "strategy / 1", limit: 5 })).toBe(
      "/api/workstation/runs/timeline?strategyId=strategy+%2F+1&limit=5"
    );
    expect(workstationRunSweepsEndpoint(20)).toBe("/api/workstation/runs/sweeps?limit=20");
  });

  it("builds evidence workbench subject endpoints from the shared evidence root", () => {
    expect(workstationEvidencePacketEndpoint("strategy/run", "run / 1")).toBe(
      "/api/workstation/evidence/subjects/strategy%2Frun/run%20%2F%201/packet"
    );
    expect(workstationEvidenceGraphEndpoint("strategy/run", "run / 1")).toBe(
      "/api/workstation/evidence/subjects/strategy%2Frun/run%20%2F%201/graph"
    );
    expect(workstationEvidenceValidateEndpoint("strategy/run", "run / 1")).toBe(
      "/api/workstation/evidence/subjects/strategy%2Frun/run%20%2F%201/validate"
    );
    expect(workstationEvidenceExportManifestEndpoint("strategy/run", "run / 1")).toBe(
      "/api/workstation/evidence/subjects/strategy%2Frun/run%20%2F%201/export-manifest"
    );
  });

  it("keeps execution, replay, and promotion endpoint builders encoded", () => {
    expect(EXECUTION_API_ENDPOINTS.sessions).toBe("/api/execution/sessions");
    expect(REPLAY_API_ENDPOINTS.start).toBe("/api/replay/start");
    expect(PROMOTION_API_ENDPOINTS.approve).toBe("/api/promotion/approve");
    expect(promotionEvaluateEndpoint("run / 1")).toBe("/api/promotion/evaluate/run%20%2F%201");
    expect(executionOrderCancelEndpoint("ord / 1")).toBe("/api/execution/orders/ord%20%2F%201/cancel");
    expect(executionPositionCloseEndpoint()).toBe("/api/execution/positions/actions/close");
    expect(executionSessionEndpoint("sess / 1")).toBe("/api/execution/sessions/sess%20%2F%201");
    expect(executionSessionCloseEndpoint("sess / 1")).toBe("/api/execution/sessions/sess%20%2F%201/close");
    expect(executionSessionReplayEndpoint("sess / 1")).toBe("/api/execution/sessions/sess%20%2F%201/replay");
    expect(executionAuditEndpoint(12)).toBe("/api/execution/audit?take=12");
    expect(executionManualOverrideClearEndpoint("override / 1")).toBe(
      "/api/execution/controls/manual-overrides/override%20%2F%201/clear"
    );
    expect(replayFilesEndpoint("ES / M6")).toBe("/api/replay/files?symbol=ES+%2F+M6");
    expect(replaySessionActionEndpoint("rep / 1", "seek")).toBe("/api/replay/rep%20%2F%201/seek");
    expect(PORTFOLIO_API_ENDPOINTS.household).toBe("/api/portfolio/household");
    expect(portfolioHouseholdEndpoint("alpaca paper")).toBe("/api/portfolio/household?provider=alpaca+paper");
    expect(PORTFOLIO_API_ENDPOINTS.aggregate).toBe("/api/portfolio/aggregate");
    expect(PORTFOLIO_API_ENDPOINTS.exposure).toBe("/api/portfolio/exposure");
    expect(portfolioSymbolExposureEndpoint("BRK/B")).toBe("/api/portfolio/symbols/BRK%2FB/exposure");
  });

  it("builds strategy run detail, comparison, and export endpoints from shared roots", () => {
    expect(strategyActionEndpoint("strategy / 1", "pause")).toBe("/api/strategies/strategy%20%2F%201/pause");
    expect(strategyRunsEndpoint("strategy / 1", "paper")).toBe("/api/strategies/strategy%20%2F%201/runs?type=paper");
    expect(STRATEGY_DESIGNER_API_ENDPOINTS.templates).toBe("/api/workstation/strategy/designer/templates");
    expect(STRATEGY_DESIGNER_API_ENDPOINTS.fieldCatalog).toBe("/api/workstation/strategy/designer/field-catalog");
    expect(STRATEGY_DESIGNER_API_ENDPOINTS.validate).toBe("/api/workstation/strategy/designer/validate");
    expect(STRATEGY_DESIGNER_API_ENDPOINTS.preview).toBe("/api/workstation/strategy/designer/preview");
    expect(STRATEGY_DESIGNER_API_ENDPOINTS.runBacktest).toBe("/api/workstation/strategy/designer/run-backtest");
    expect(strategyDesignerDraftEndpoint()).toBe("/api/workstation/strategy/designer/drafts");
    expect(strategyDesignerDraftEndpoint("draft / 1")).toBe("/api/workstation/strategy/designer/drafts/draft%20%2F%201");
    expect(workstationRunCompareEndpoint()).toBe("/api/workstation/runs/compare");
    expect(workstationRunDiffEndpoint()).toBe("/api/workstation/runs/diff");
    expect(workstationRunAttributionEndpoint("run / 1")).toBe("/api/workstation/runs/run%20%2F%201/attribution");
    expect(workstationRunFillsEndpoint("run / 1", "ES / M6")).toBe(
      "/api/workstation/runs/run%20%2F%201/fills?symbol=ES+%2F+M6"
    );
    expect(workstationRunEquityCurveEndpoint("run / 1")).toBe("/api/workstation/runs/run%20%2F%201/equity-curve");
    expect(workstationRunLedgerTrialBalanceEndpoint("run / 1", "Asset")).toBe(
      "/api/workstation/runs/run%20%2F%201/ledger/trial-balance?accountType=Asset"
    );
    expect(EXPORT_API_ENDPOINTS.analysis).toBe("/api/export/analysis");
    expect(EXPORT_API_ENDPOINTS.reportPacks).toBe("/api/fund-structure/report-packs");
    expect(EXPORT_API_ENDPOINTS.reportPackEvidenceBundle).toBe("/api/fund-structure/report-packs/{reportId}/evidence-bundle");
    expect(exportPreviewEndpoint("audit pack")).toBe("/api/export/preview?profile=audit+pack");
    expect(reportPackEvidenceBundleEndpoint("report / 1")).toBe("/api/fund-structure/report-packs/report%20%2F%201/evidence-bundle");
    expect(reportPackEvidenceBundleEndpoint()).toBe("/api/fund-structure/report-packs/{reportId}/evidence-bundle");
  });

  it("builds security-master and reconciliation endpoint families from shared roots", () => {
    expect(SECURITY_MASTER_API_ENDPOINTS.workstationSecurities).toBe("/api/workstation/security-master/securities");
    expect(workstationSecurityMasterSearchEndpoint({ query: "BRK/B", take: 5, activeOnly: true })).toBe(
      "/api/workstation/security-master/securities?query=BRK%2FB&take=5&activeOnly=true"
    );
    expect(workstationSecurityMasterEntryEndpoint("security / 1")).toBe(
      "/api/workstation/security-master/securities/security%20%2F%201"
    );
    expect(workstationSecurityMasterIdentityEndpoint("security / 1")).toBe(
      "/api/workstation/security-master/securities/security%20%2F%201/identity"
    );
    expect(workstationSecurityMasterHistoryEndpoint("security / 1")).toBe(
      "/api/workstation/security-master/securities/security%20%2F%201/history"
    );
    expect(workstationSecurityMasterEconomicDefinitionEndpoint("security / 1")).toBe(
      "/api/workstation/security-master/securities/security%20%2F%201/economic-definition"
    );
    expect(workstationSecurityMasterTrustSnapshotEndpoint("security / 1")).toBe(
      "/api/workstation/security-master/securities/security%20%2F%201/trust-snapshot"
    );
    expect(workstationSecurityMasterInstrumentPassportEndpoint("security / 1")).toBe(
      "/api/workstation/security-master/securities/security%20%2F%201/passport"
    );
    expect(securityMasterEntryEndpoint()).toBe("/api/security-master");
    expect(securityMasterAmendEndpoint()).toBe("/api/security-master/amend");
    expect(securityMasterAssetProfilesEndpoint()).toBe("/api/security-master/asset-profiles");
    expect(securityMasterAssetProfileLineageEndpoint("profile / 1")).toBe(
      "/api/security-master/asset-profiles/profile%20%2F%201/lineage"
    );
    expect(securityMasterAssetProfileDraftsEndpoint()).toBe("/api/security-master/asset-profiles/drafts");
    expect(securityMasterAssetProfileApproveEndpoint()).toBe("/api/security-master/asset-profiles/approve");
    expect(securityMasterAssetProfileRollbackEndpoint()).toBe("/api/security-master/asset-profiles/rollback");
    expect(securityMasterAliasUpsertEndpoint()).toBe("/api/security-master/aliases/upsert");
    expect(securityMasterCorporateActionsEndpoint("security / 1")).toBe(
      "/api/security-master/security%20%2F%201/corporate-actions"
    );
    expect(securityMasterTradingParametersEndpoint("security / 1")).toBe(
      "/api/security-master/security%20%2F%201/trading-parameters"
    );
    expect(securityMasterConflictsEndpoint()).toBe("/api/security-master/conflicts");
    expect(securityMasterConflictResolveEndpoint("conflict / 1")).toBe(
      "/api/security-master/conflicts/conflict%20%2F%201/resolve"
    );
    expect(SECURITY_MASTER_API_ENDPOINTS.workstationConflictsBulkResolve).toBe(
      "/api/workstation/security-master/conflicts/bulk-resolve"
    );
    expect(RECONCILIATION_API_ENDPOINTS.runs).toBe("/api/workstation/reconciliation/runs");
    expect(RECONCILIATION_API_ENDPOINTS.statementRuns).toBe("/api/workstation/reconciliation/statement-runs");
    expect(RECONCILIATION_API_ENDPOINTS.statementExceptions).toBe("/api/workstation/reconciliation/statement-exceptions");
    expect(reconciliationRunEndpoint("recon / 1")).toBe("/api/workstation/reconciliation/runs/recon%20%2F%201");
    expect(reconciliationStatementRunEndpoint("statement / 1")).toBe(
      "/api/workstation/reconciliation/statement-runs/statement%20%2F%201"
    );
    expect(reconciliationBreakQueueEndpoint({ status: "Open", fundAccountId: "fund / 1" })).toBe(
      "/api/workstation/reconciliation/break-queue?status=Open&fundAccountId=fund+%2F+1"
    );
    expect(reconciliationBreakQueueEndpoint({ fundAccountId: " fund / 1 " })).toBe(
      "/api/workstation/reconciliation/break-queue?fundAccountId=fund+%2F+1"
    );
    expect(reconciliationBreakEndpoint("break / 1")).toBe("/api/workstation/reconciliation/break-queue/break%20%2F%201");
    expect(reconciliationBreakAuditEndpoint("break / 1")).toBe(
      "/api/workstation/reconciliation/break-queue/break%20%2F%201/audit"
    );
    expect(reconciliationBreakReviewEndpoint("break / 1")).toBe(
      "/api/workstation/reconciliation/break-queue/break%20%2F%201/review"
    );
    expect(reconciliationBreakResolveEndpoint("break / 1")).toBe(
      "/api/workstation/reconciliation/break-queue/break%20%2F%201/resolve"
    );
    expect(reconciliationBreakAssignEndpoint("break / 1")).toBe(
      "/api/workstation/reconciliation/break-queue/break%20%2F%201/assign"
    );
    expect(reconciliationBreakTransitionEndpoint("break / 1")).toBe(
      "/api/workstation/reconciliation/break-queue/break%20%2F%201/transition"
    );
    expect(reconciliationBreakCommentsEndpoint("break / 1")).toBe(
      "/api/workstation/reconciliation/break-queue/break%20%2F%201/comments"
    );
    expect(reconciliationBreakCommentEndpoint("break / 1", "comment / 1")).toBe(
      "/api/workstation/reconciliation/break-queue/break%20%2F%201/comments/comment%20%2F%201"
    );
    expect(reconciliationBreakRootCauseEndpoint("break / 1")).toBe(
      "/api/workstation/reconciliation/break-queue/break%20%2F%201/root-cause"
    );
    expect(reconciliationBreakResolutionEndpoint("break / 1")).toBe(
      "/api/workstation/reconciliation/break-queue/break%20%2F%201/resolution"
    );
    expect(reconciliationBreakSignOffEndpoint("break / 1")).toBe(
      "/api/workstation/reconciliation/break-queue/break%20%2F%201/sign-off"
    );
    expect(reconciliationBreakReopenEndpoint("break / 1")).toBe(
      "/api/workstation/reconciliation/break-queue/break%20%2F%201/reopen"
    );
    expect(reconciliationBreakBulkDryRunEndpoint()).toBe(
      "/api/workstation/reconciliation/break-queue/bulk/dry-run"
    );
    expect(reconciliationBreakBulkExecuteEndpoint()).toBe(
      "/api/workstation/reconciliation/break-queue/bulk/execute"
    );
    expect(reconciliationBreakBulkStatusEndpoint("bulk / 1")).toBe(
      "/api/workstation/reconciliation/break-queue/bulk/bulk%20%2F%201"
    );
    expect(reconciliationBreakBulkResultEndpoint("bulk / 1")).toBe(
      "/api/workstation/reconciliation/break-queue/bulk/bulk%20%2F%201/result"
    );
  });

  it("builds data, provider, symbol, quality, and quant endpoints from shared roots", () => {
    expect(BACKFILL_API_ENDPOINTS.runPreview).toBe("/api/backfill/run/preview");
    expect(BACKFILL_API_ENDPOINTS.checkpoints).toBe("/api/backfill/checkpoints");
    expect(BACKFILL_API_ENDPOINTS.checkpointsResumable).toBe("/api/backfill/checkpoints/resumable");
    expect(BACKFILL_API_ENDPOINTS.checkpointsValidation).toBe("/api/backfill/checkpoints/validation");
    expect(backfillCheckpointEndpoint("job / 1")).toBe("/api/backfill/checkpoints/job%20%2F%201");
    expect(backfillCheckpointPendingEndpoint("job / 1")).toBe("/api/backfill/checkpoints/job%20%2F%201/pending");
    expect(backfillCheckpointResumeEndpoint("job / 1")).toBe("/api/backfill/checkpoints/job%20%2F%201/resume");
    expect(PROVIDER_API_ENDPOINTS.configure).toBe("/api/providers/configure");
    expect(PROVIDER_API_ENDPOINTS.status).toBe("/api/providers/status");
    expect(PROVIDER_ROUTING_API_ENDPOINTS).toMatchObject({
      connections: "/api/provider-routing/connections",
      bindings: "/api/provider-routing/bindings",
      trustSnapshots: "/api/provider-routing/trust-snapshots",
      preview: "/api/provider-routing/preview"
    });
    expect(providerRemoveEndpoint("provider / 1")).toBe("/api/providers/provider%20%2F%201/remove");
    expect(providerTestEndpoint("provider / 1")).toBe("/api/providers/provider%20%2F%201/test");
    expect(SYMBOL_API_ENDPOINTS.symbols).toBe("/api/symbols");
    expect(symbolSearchEndpoint("BRK/B")).toBe("/api/symbols/search?query=BRK%2FB");
    expect(symbolRemoveEndpoint("BRK/B")).toBe("/api/symbols/BRK%2FB/remove");
    expect(symbolArchiveEndpoint("BRK/B")).toBe("/api/symbols/BRK%2FB/archive");
    expect(QUALITY_API_ENDPOINTS.dashboard).toBe("/api/quality/dashboard");
    expect(qualityAnomalyAcknowledgeEndpoint("anomaly / 1")).toBe(
      "/api/quality/anomalies/anomaly%20%2F%201/acknowledge"
    );
    expect(marketDataQuoteEndpoint("BRK/B")).toBe("/api/data/quotes/BRK%2FB");
    expect(marketDataTradesEndpoint("BRK/B", 10)).toBe("/api/data/trades/BRK%2FB?limit=10");
    expect(marketDataOrderbookEndpoint("BRK/B", 3)).toBe("/api/data/orderbook/BRK%2FB?levels=3");
    expect(marketDataQuotesSnapshotEndpoint(["AAPL", "MSFT"])).toBe("/api/data/quotes-snapshot?symbols=AAPL%2CMSFT");
    expect(historicalBarsEndpoint("BRK/B", { intervalMinutes: 5, maxBars: 20 })).toBe(
      "/api/historical/BRK%2FB/bars?intervalMinutes=5&maxBars=20"
    );
    expect(QUANT_API_ENDPOINTS.templates).toBe("/api/quant/templates");
    expect(QUANT_API_ENDPOINTS.parameters).toBe("/api/quant/parameters");
    expect(QUANT_API_ENDPOINTS.run).toBe("/api/quant/run");
  });

  it("builds covered-call dynamic endpoints from guarded path segments", () => {
    expect(coveredCallRunsEndpoint()).toBe("/api/strategies/covered-call/runs");
    expect(coveredCallRunsEndpoint(25)).toBe("/api/strategies/covered-call/runs?limit=25");
    expect(coveredCallRunEndpoint("run / 1")).toBe("/api/strategies/covered-call/runs/run%20%2F%201");
    expect(coveredCallRunStatusEndpoint("run / 1")).toBe("/api/strategies/covered-call/runs/run%20%2F%201/status");
    expect(coveredCallRunResultEndpoint("run / 1")).toBe("/api/strategies/covered-call/runs/run%20%2F%201/result");
    expect(coveredCallRunCancelEndpoint("run / 1")).toBe("/api/strategies/covered-call/runs/run%20%2F%201/cancel");
    expect(() => coveredCallRunEndpoint("   ")).toThrow("runId is required");
  });

  it("builds brokerage connection endpoints from the shared catalog", () => {
    expect(brokerageConnectionEndpoint("alpaca")).toBe("/api/brokerage-connections/alpaca");
    expect(brokerageConnectionStatusEndpoint("alpaca")).toBe("/api/brokerage-connections/alpaca/status");
    expect(brokerageConnectionConnectEndpoint("robinhood")).toBe("/api/brokerage-connections/robinhood/connect");
  });

  it("rejects blank path segments before issuing malformed API routes", () => {
    expect(() => workstationRunLedgerEndpoint("   ")).toThrow("runId is required");
    expect(() => marketDataQuoteEndpoint("")).toThrow("symbol is required");
    expect(() => reconciliationBreakEndpoint("\t")).toThrow("breakId is required");
  });
});

describe("execution control route contract parity", () => {
  const CONTRACT_EXECUTION_DEFAULT_POSITION_LIMIT = "/api/execution/controls/position-limits/default" as const;
  const CONTRACT_EXECUTION_SYMBOL_POSITION_LIMIT_TEMPLATE = "/api/execution/controls/position-limits/{symbol}" as const;
  const CONTRACT_EXECUTION_MANUAL_OVERRIDES = "/api/execution/controls/manual-overrides" as const;
  const CONTRACT_EXECUTION_MANUAL_OVERRIDE_CLEAR_TEMPLATE =
    "/api/execution/controls/manual-overrides/{overrideId}/clear" as const;

  it("keeps frontend helper constants aligned with backend contracts", () => {
    expect(
      EXECUTION_API_ENDPOINTS.defaultPositionLimit,
      "frontend helper diverged from backend contract: default position-limit route"
    ).toBe(CONTRACT_EXECUTION_DEFAULT_POSITION_LIMIT);

    expect(
      executionSymbolPositionLimitEndpoint("AAPL"),
      "frontend helper diverged from backend contract: symbol position-limit route template"
    ).toBe(CONTRACT_EXECUTION_SYMBOL_POSITION_LIMIT_TEMPLATE.replace("{symbol}", "AAPL"));

    expect(
      EXECUTION_API_ENDPOINTS.manualOverrides,
      "frontend helper diverged from backend contract: manual override create route"
    ).toBe(CONTRACT_EXECUTION_MANUAL_OVERRIDES);

    expect(
      executionManualOverrideClearEndpoint("override-1"),
      "frontend helper diverged from backend contract: manual override clear route template"
    ).toBe(CONTRACT_EXECUTION_MANUAL_OVERRIDE_CLEAR_TEMPLATE.replace("{overrideId}", "override-1"));
  });
});
