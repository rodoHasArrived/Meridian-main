export const WORKSTATION_API_ENDPOINTS = {
  systemStatus: "/api/status",
  session: "/api/workstation/session",
  strategy: "/api/workstation/strategy",
  trading: "/api/workstation/trading",
  tradingReadiness: "/api/workstation/trading/readiness",
  operatorInbox: "/api/workstation/operator/inbox",
  portfolio: "/api/workstation/portfolio",
  portfolioSummary: "/api/workstation/portfolio/summary",
  data: "/api/workstation/data",
  accounting: "/api/workstation/accounting",
  reporting: "/api/workstation/reporting",
  workflowSummary: "/api/workstation/workflow-summary",
  featureCapabilities: "/api/workstation/settings/feature-capabilities",
  workflowLibrary: "/api/workstation/workflows",
  workflowPresets: "/api/workstation/workflows/presets",
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
  evidenceTemplates: "/api/workstation/evidence/templates"
} as const;

export const AUTH_API_ENDPOINTS = {
  roles: "/api/auth/roles",
  roleProfiles: "/api/auth/role-profiles"
} as const;

export const FUND_STRUCTURE_API_ENDPOINTS = {
  graph: "/api/fund-structure/graph",
  ownershipReview: "/api/fund-structure/ownership-review",
  ownershipLinks: "/api/fund-structure/links",
  ledgerMappingWorkbench: "/api/fund-structure/ledger-mapping-view",
  ledgerMappingAssignments: "/api/fund-structure/ledger-mapping-assignments",
  transactionLabPreview: "/api/fund-structure/accounting/transaction-lab/preview"
} as const;

export const WORKSTATION_API_ENDPOINT_TEMPLATES = {
  runLedger: "/api/workstation/runs/{runId}/ledger",
  runContinuity: "/api/workstation/runs/{runId}/continuity",
  runReviewPacket: "/api/workstation/runs/{runId}/review-packet",
  runReconciliation: "/api/workstation/runs/{runId}/reconciliation"
} as const;

export const EXECUTION_API_ENDPOINTS = {
  ordersSubmit: "/api/execution/orders/submit",
  ordersCancelAll: "/api/execution/orders/cancel-all",
  positionsActionClose: "/api/execution/positions/actions/close",
  sessions: "/api/execution/sessions",
  sessionsCreate: "/api/execution/sessions/create",
  audit: "/api/execution/audit",
  controls: "/api/execution/controls",
  defaultPositionLimit: "/api/execution/controls/position-limits/default",
  symbolPositionLimits: "/api/execution/controls/position-limits",
  manualOverrides: "/api/execution/controls/manual-overrides"
} as const;

export const RISK_API_ENDPOINTS = {
  rules: "/api/risk/rules"
} as const;

export const REPLAY_API_ENDPOINTS = {
  files: "/api/replay/files",
  start: "/api/replay/start"
} as const;

export const PROMOTION_API_ENDPOINTS = {
  approve: "/api/promotion/approve",
  evaluate: "/api/promotion/evaluate",
  reject: "/api/promotion/reject",
  history: "/api/promotion/history"
} as const;

export const PORTFOLIO_API_ENDPOINTS = {
  aggregate: "/api/portfolio/aggregate",
  exposure: "/api/portfolio/exposure",
  household: "/api/portfolio/household"
} as const;

export const EXPORT_API_ENDPOINTS = {
  analysis: "/api/export/analysis",
  formats: "/api/export/formats",
  preview: "/api/export/preview",
  reportPacks: "/api/fund-structure/report-packs"
} as const;

export const BROKERAGE_CONNECTION_API_ENDPOINTS = {
  base: "/api/brokerage-connections"
} as const;

export const STRATEGY_API_ENDPOINTS = {
  base: "/api/strategies"
} as const;

export const STRATEGY_DESIGNER_API_ENDPOINTS = {
  templates: "/api/workstation/strategy/designer/templates",
  fieldCatalog: "/api/workstation/strategy/designer/field-catalog",
  drafts: "/api/workstation/strategy/designer/drafts",
  validate: "/api/workstation/strategy/designer/validate",
  preview: "/api/workstation/strategy/designer/preview",
  runBacktest: "/api/workstation/strategy/designer/run-backtest"
} as const;

export const STRATEGY_ENGINE_API_ENDPOINTS = {
  definitions: "/api/workstation/strategy/engine/definitions",
  validateRun: "/api/workstation/strategy/engine/validate-run"
} as const;

export const SECURITY_MASTER_API_ENDPOINTS = {
  base: "/api/security-master",
  assetProfiles: "/api/security-master/asset-profiles",
  assetProfileDrafts: "/api/security-master/asset-profiles/drafts",
  assetProfileApprove: "/api/security-master/asset-profiles/approve",
  assetProfileRollback: "/api/security-master/asset-profiles/rollback",
  workstationSecurities: "/api/workstation/security-master/securities",
  workstationConflictsBulkResolve: "/api/workstation/security-master/conflicts/bulk-resolve"
} as const;

export const RECONCILIATION_API_ENDPOINTS = {
  runs: "/api/workstation/reconciliation/runs",
  statementRuns: "/api/workstation/reconciliation/statement-runs",
  statementExceptions: "/api/workstation/reconciliation/statement-exceptions",
  breakQueue: "/api/workstation/reconciliation/break-queue",
  calibrationSummary: "/api/workstation/reconciliation/calibration-summary"
} as const;

export const BACKFILL_API_ENDPOINTS = {
  checkpoints: "/api/backfill/checkpoints",
  checkpointsResumable: "/api/backfill/checkpoints/resumable",
  checkpointsValidation: "/api/backfill/checkpoints/validation",
  progress: "/api/backfill/progress",
  run: "/api/backfill/run",
  runPreview: "/api/backfill/run/preview"
} as const;

export const PROVIDER_API_ENDPOINTS = {
  configure: "/api/providers/configure",
  status: "/api/providers/status",
  connections: "/api/providers/connections"
} as const;

export const PROVIDER_ROUTING_API_ENDPOINTS = {
  connections: "/api/provider-routing/connections",
  bindings: "/api/provider-routing/bindings",
  trustSnapshots: "/api/provider-routing/trust-snapshots",
  preview: "/api/provider-routing/preview"
} as const;

export const SYMBOL_API_ENDPOINTS = {
  symbols: "/api/symbols",
  statistics: "/api/symbols/statistics",
  search: "/api/symbols/search",
  add: "/api/symbols/add",
  bulkAdd: "/api/symbols/bulk-add"
} as const;

export const QUALITY_API_ENDPOINTS = {
  dashboard: "/api/quality/dashboard",
  gaps: "/api/quality/gaps",
  anomalies: "/api/quality/anomalies",
  completeness: "/api/quality/completeness"
} as const;

export const MARKET_DATA_API_ENDPOINTS = {
  quotes: "/api/data/quotes",
  trades: "/api/data/trades",
  orderbook: "/api/data/orderbook",
  quotesSnapshot: "/api/data/quotes-snapshot",
  historical: "/api/historical"
} as const;

export const QUANT_API_ENDPOINTS = {
  templates: "/api/quant/templates",
  parameters: "/api/quant/parameters",
  run: "/api/quant/run"
} as const;

export const COVERED_CALL_API_ENDPOINTS = {
  runs: "/api/strategies/covered-call/runs",
  chainPreview: "/api/strategies/covered-call/chain-preview",
  runStatus: coveredCallRunStatusEndpoint,
  runResult: coveredCallRunResultEndpoint,
  runCancel: coveredCallRunCancelEndpoint
} as const;

export const CONFIG_API_ENDPOINTS = {
  config: "/api/config"
} as const;

export type BrokerageConnectionProvider = "alpaca" | "robinhood";

export function brokerageConnectionEndpoint(provider: BrokerageConnectionProvider): string {
  return `${BROKERAGE_CONNECTION_API_ENDPOINTS.base}/${pathSegment(provider, "provider")}`;
}

export function brokerageConnectionStatusEndpoint(provider: BrokerageConnectionProvider): string {
  return `${brokerageConnectionEndpoint(provider)}/status`;
}

export function brokerageConnectionConnectEndpoint(provider: BrokerageConnectionProvider): string {
  return `${brokerageConnectionEndpoint(provider)}/connect`;
}

export function providerCredentialEndpoint(providerId: string): string {
  return `/api/providers/${pathSegment(providerId, "providerId")}/credentials`;
}

export function providerVerifyEndpoint(providerId: string): string {
  return `/api/providers/${pathSegment(providerId, "providerId")}/verify`;
}

export function workstationTradingReadinessEndpoint(fundAccountId?: string): string {
  return fundAccountId
    ? `${WORKSTATION_API_ENDPOINTS.tradingReadiness}${queryString({ fundAccountId })}`
    : WORKSTATION_API_ENDPOINTS.tradingReadiness;
}

export function workstationOperatorInboxEndpoint(fundAccountId?: string): string {
  return fundAccountId
    ? `${WORKSTATION_API_ENDPOINTS.operatorInbox}${queryString({ fundAccountId })}`
    : WORKSTATION_API_ENDPOINTS.operatorInbox;
}

export function riskRuleEndpoint(ruleName: string): string {
  return `${RISK_API_ENDPOINTS.rules}/${pathSegment(ruleName, "ruleName")}`;
}

export function riskRuleStatusEndpoint(ruleName: string): string {
  return `${riskRuleEndpoint(ruleName)}/status`;
}

export function riskRuleConfigEndpoint(ruleName: string): string {
  return `${riskRuleEndpoint(ruleName)}/config`;
}

export function workstationWorkflowSummaryEndpoint(options: {
  hasOperatingContext?: boolean;
  operatingContext?: string;
  fundProfileId?: string;
  fundDisplayName?: string;
} = {}): string {
  return `${WORKSTATION_API_ENDPOINTS.workflowSummary}${queryString(options)}`;
}

export function workstationWorkflowPresetEndpoint(presetId?: string): string {
  return presetId
    ? `${WORKSTATION_API_ENDPOINTS.workflowPresets}/${pathSegment(presetId, "presetId")}`
    : WORKSTATION_API_ENDPOINTS.workflowPresets;
}

export function workstationWorkflowPresetPinEndpoint(presetId: string): string {
  return `${workstationWorkflowPresetEndpoint(presetId)}/pin`;
}

export function workstationWorkflowPresetUsedEndpoint(presetId: string): string {
  return `${workstationWorkflowPresetEndpoint(presetId)}/used`;
}

export function workstationOperationsContinuityEndpoint(options: {
  fundAccountId?: string;
  periodId?: string;
  status?: string;
} = {}): string {
  return `${WORKSTATION_API_ENDPOINTS.operationsContinuity}${queryString(options)}`;
}

export function workstationOperationsContinuityDetailEndpoint(workflowId: string): string {
  return `${WORKSTATION_API_ENDPOINTS.operationsContinuity}/${pathSegment(workflowId, "workflowId")}`;
}

export function workstationOperationsContinuityTimelineEndpoint(workflowId: string): string {
  return `${workstationOperationsContinuityDetailEndpoint(workflowId)}/timeline`;
}

export function workstationOperationsContinuityBreaksEndpoint(workflowId: string): string {
  return `${workstationOperationsContinuityDetailEndpoint(workflowId)}/breaks`;
}

export function workstationOperationsContinuityLedgerPreviewEndpoint(workflowId: string): string {
  return `${workstationOperationsContinuityDetailEndpoint(workflowId)}/ledger-preview`;
}

export function workstationOperationsContinuityCloseCalendarEndpoint(options: {
  fundAccountId?: string;
  periodId?: string;
} = {}): string {
  return `${WORKSTATION_API_ENDPOINTS.operationsContinuityCloseCalendar}${queryString(options)}`;
}

export function workstationChiefOfStaffSessionsEndpoint(options: {
  workspace?: string;
  fundProfileId?: string;
  fundAccountId?: string;
  status?: string;
  limit?: number;
} = {}): string {
  return `${WORKSTATION_API_ENDPOINTS.chiefOfStaff}/sessions${queryString(options)}`;
}

export function workstationChiefOfStaffSessionEndpoint(sessionId: string): string {
  return `${WORKSTATION_API_ENDPOINTS.chiefOfStaff}/sessions/${pathSegment(sessionId, "sessionId")}`;
}

export function workstationChiefOfStaffDecisionEndpoint(sessionId: string): string {
  return `${workstationChiefOfStaffSessionEndpoint(sessionId)}/decisions`;
}

export function workstationChiefOfStaffTraceExportEndpoint(sessionId: string): string {
  return `${workstationChiefOfStaffSessionEndpoint(sessionId)}/export-trace`;
}

export function workstationChiefOfStaffHealthEndpoint(): string {
  return `${WORKSTATION_API_ENDPOINTS.chiefOfStaff}/health`;
}

export function workstationRunLedgerEndpoint(runId: string): string {
  return `${workstationRunBaseEndpoint(runId)}/ledger`;
}

export function workstationRunLedgerJournalEndpoint(runId: string, options: { from?: string; to?: string } = {}): string {
  return `${workstationRunLedgerEndpoint(runId)}/journal${queryString(options)}`;
}

export function workstationRunContinuityEndpoint(runId: string): string {
  return `${workstationRunBaseEndpoint(runId)}/continuity`;
}

export function workstationRunReviewPacketEndpoint(runId: string, fundAccountId?: string): string {
  return `${workstationRunBaseEndpoint(runId)}/review-packet${queryString({ fundAccountId })}`;
}

export function workstationRunReconciliationEndpoint(runId: string): string {
  return `${workstationRunBaseEndpoint(runId)}/reconciliation`;
}

export function workstationRunReconciliationHistoryEndpoint(runId: string): string {
  return `${workstationRunReconciliationEndpoint(runId)}/history`;
}

export function workstationRunHistoryEndpoint(options: { mode?: string; status?: string; limit?: number } = {}): string {
  return `${WORKSTATION_API_ENDPOINTS.runHistory}${queryString(options)}`;
}

export function workstationRunTimelineEndpoint(
  options: { mode?: string; status?: string; strategyId?: string; limit?: number } = {}
): string {
  return `${WORKSTATION_API_ENDPOINTS.runTimeline}${queryString(options)}`;
}

export function workstationRunSweepsEndpoint(limit?: number): string {
  return `${WORKSTATION_API_ENDPOINTS.runSweeps}${queryString({ limit })}`;
}

export function workstationEvidenceSubjectBaseEndpoint(subjectKind: string, subjectId: string): string {
  return `${WORKSTATION_API_ENDPOINTS.evidenceSubjects}/${pathSegment(subjectKind, "subjectKind")}/${pathSegment(subjectId, "subjectId")}`;
}

export function workstationEvidencePacketEndpoint(subjectKind: string, subjectId: string): string {
  return `${workstationEvidenceSubjectBaseEndpoint(subjectKind, subjectId)}/packet`;
}

export function workstationEvidenceGraphEndpoint(subjectKind: string, subjectId: string): string {
  return `${workstationEvidenceSubjectBaseEndpoint(subjectKind, subjectId)}/graph`;
}

export function workstationEvidenceValidateEndpoint(subjectKind: string, subjectId: string): string {
  return `${workstationEvidenceSubjectBaseEndpoint(subjectKind, subjectId)}/validate`;
}

export function workstationEvidenceExportManifestEndpoint(subjectKind: string, subjectId: string): string {
  return `${workstationEvidenceSubjectBaseEndpoint(subjectKind, subjectId)}/export-manifest`;
}

export function coveredCallRunsEndpoint(limit?: number): string {
  return `${COVERED_CALL_API_ENDPOINTS.runs}${queryString({ limit })}`;
}

export function coveredCallRunEndpoint(runId: string): string {
  return `${COVERED_CALL_API_ENDPOINTS.runs}/${pathSegment(runId, "runId")}`;
}

export function coveredCallRunStatusEndpoint(runId: string): string {
  return `${coveredCallRunEndpoint(runId)}/status`;
}

export function coveredCallRunResultEndpoint(runId: string): string {
  return `${coveredCallRunEndpoint(runId)}/result`;
}

export function coveredCallRunCancelEndpoint(runId: string): string {
  return `${coveredCallRunEndpoint(runId)}/cancel`;
}

export function promotionEvaluateEndpoint(runId: string): string {
  return `${PROMOTION_API_ENDPOINTS.evaluate}/${pathSegment(runId, "runId")}`;
}

export function executionOrderCancelEndpoint(orderId: string): string {
  return `/api/execution/orders/${pathSegment(orderId, "orderId")}/cancel`;
}

export function executionPositionCloseEndpoint(): string {
  return EXECUTION_API_ENDPOINTS.positionsActionClose;
}

export function executionSessionEndpoint(sessionId: string): string {
  return `${EXECUTION_API_ENDPOINTS.sessions}/${pathSegment(sessionId, "sessionId")}`;
}

export function executionSessionCloseEndpoint(sessionId: string): string {
  return `${executionSessionEndpoint(sessionId)}/close`;
}

export function executionSessionReplayEndpoint(sessionId: string): string {
  return `${executionSessionEndpoint(sessionId)}/replay`;
}

export function executionAuditEndpoint(take = 20): string {
  return `${EXECUTION_API_ENDPOINTS.audit}${queryString({ take })}`;
}

export function executionManualOverrideClearEndpoint(overrideId: string): string {
  return `${EXECUTION_API_ENDPOINTS.manualOverrides}/${pathSegment(overrideId, "overrideId")}/clear`;
}

export function executionSymbolPositionLimitEndpoint(symbol: string): string {
  return `${EXECUTION_API_ENDPOINTS.symbolPositionLimits}/${pathSegment(symbol, "symbol")}`;
}

export function replayFilesEndpoint(symbol?: string): string {
  return `${REPLAY_API_ENDPOINTS.files}${queryString({ symbol })}`;
}

export function portfolioHouseholdEndpoint(provider = "alpaca"): string {
  return `${PORTFOLIO_API_ENDPOINTS.household}${queryString({ provider })}`;
}

export function portfolioSymbolExposureEndpoint(symbol: string): string {
  return `${PORTFOLIO_API_ENDPOINTS.exposure.replace(/\/exposure$/, "/symbols")}/${pathSegment(symbol, "symbol")}/exposure`;
}

export function exportPreviewEndpoint(profile?: string): string {
  return `${EXPORT_API_ENDPOINTS.preview}${queryString({ profile })}`;
}

export function strategyEndpoint(strategyId: string): string {
  return `${STRATEGY_API_ENDPOINTS.base}/${pathSegment(strategyId, "strategyId")}`;
}

export function strategyActionEndpoint(strategyId: string, action: "pause" | "stop"): string {
  return `${strategyEndpoint(strategyId)}/${action}`;
}

export function strategyRunsEndpoint(strategyId: string, type?: "backtest" | "paper" | "live"): string {
  return `${strategyEndpoint(strategyId)}/runs${queryString({ type })}`;
}

export function strategyDesignerDraftEndpoint(documentId?: string): string {
  return documentId
    ? `${STRATEGY_DESIGNER_API_ENDPOINTS.drafts}/${pathSegment(documentId, "documentId")}`
    : STRATEGY_DESIGNER_API_ENDPOINTS.drafts;
}

export function replaySessionActionEndpoint(
  sessionId: string,
  action: "pause" | "resume" | "stop" | "seek" | "speed" | "status"
): string {
  return `/api/replay/${pathSegment(sessionId, "sessionId")}/${action}`;
}

export function workstationRunCompareEndpoint(): string {
  return `${workstationRunRootEndpoint()}/compare`;
}

export function workstationRunDiffEndpoint(): string {
  return `${workstationRunRootEndpoint()}/diff`;
}

export function workstationRunAttributionEndpoint(runId: string): string {
  return `${workstationRunBaseEndpoint(runId)}/attribution`;
}

export function workstationRunFillsEndpoint(runId: string, symbol?: string): string {
  return `${workstationRunBaseEndpoint(runId)}/fills${queryString({ symbol })}`;
}

export function workstationRunEquityCurveEndpoint(runId: string): string {
  return `${workstationRunBaseEndpoint(runId)}/equity-curve`;
}

export function portfolioRunCashFlowsEndpoint(runId: string): string {
  return `/api/portfolio/${pathSegment(runId, "runId")}/cash-flows`;
}

export function workstationRunLedgerTrialBalanceEndpoint(runId: string, accountType?: string): string {
  return `${workstationRunLedgerEndpoint(runId)}/trial-balance${queryString({ accountType })}`;
}

export function workstationSecurityMasterSearchEndpoint(options: {
  query?: string;
  take?: number;
  activeOnly?: boolean;
} = {}): string {
  return `${SECURITY_MASTER_API_ENDPOINTS.workstationSecurities}${queryString(options)}`;
}

export function workstationSecurityMasterEntryEndpoint(securityId: string): string {
  return `${SECURITY_MASTER_API_ENDPOINTS.workstationSecurities}/${pathSegment(securityId, "securityId")}`;
}

export function workstationSecurityMasterIdentityEndpoint(securityId: string): string {
  return `${workstationSecurityMasterEntryEndpoint(securityId)}/identity`;
}

export function workstationSecurityMasterHistoryEndpoint(securityId: string): string {
  return `${workstationSecurityMasterEntryEndpoint(securityId)}/history`;
}

export function workstationSecurityMasterEconomicDefinitionEndpoint(securityId: string): string {
  return `${workstationSecurityMasterEntryEndpoint(securityId)}/economic-definition`;
}

export function workstationSecurityMasterTrustSnapshotEndpoint(securityId: string): string {
  return `${workstationSecurityMasterEntryEndpoint(securityId)}/trust-snapshot`;
}

export function workstationSecurityMasterInstrumentPassportEndpoint(securityId: string): string {
  return `${workstationSecurityMasterEntryEndpoint(securityId)}/passport`;
}

export function securityMasterEntryEndpoint(): string {
  return SECURITY_MASTER_API_ENDPOINTS.base;
}

export function securityMasterAmendEndpoint(): string {
  return `${SECURITY_MASTER_API_ENDPOINTS.base}/amend`;
}

export function securityMasterAssetProfilesEndpoint(): string {
  return SECURITY_MASTER_API_ENDPOINTS.assetProfiles;
}

export function securityMasterAssetProfileLineageEndpoint(profileId: string): string {
  return `${SECURITY_MASTER_API_ENDPOINTS.assetProfiles}/${pathSegment(profileId, "profileId")}/lineage`;
}

export function securityMasterAssetProfileDraftsEndpoint(): string {
  return SECURITY_MASTER_API_ENDPOINTS.assetProfileDrafts;
}

export function securityMasterAssetProfileApproveEndpoint(): string {
  return SECURITY_MASTER_API_ENDPOINTS.assetProfileApprove;
}

export function securityMasterAssetProfileRollbackEndpoint(): string {
  return SECURITY_MASTER_API_ENDPOINTS.assetProfileRollback;
}

export function securityMasterAliasUpsertEndpoint(): string {
  return `${SECURITY_MASTER_API_ENDPOINTS.base}/aliases/upsert`;
}

export function securityMasterCorporateActionsEndpoint(securityId: string): string {
  return `${SECURITY_MASTER_API_ENDPOINTS.base}/${pathSegment(securityId, "securityId")}/corporate-actions`;
}

export function securityMasterTradingParametersEndpoint(securityId: string): string {
  return `${SECURITY_MASTER_API_ENDPOINTS.base}/${pathSegment(securityId, "securityId")}/trading-parameters`;
}

export function securityMasterOperatorOverridesEndpoint(securityId: string): string {
  return `${SECURITY_MASTER_API_ENDPOINTS.base}/${pathSegment(securityId, "securityId")}/operator-overrides`;
}

export function securityMasterConflictsEndpoint(): string {
  return `${SECURITY_MASTER_API_ENDPOINTS.base}/conflicts`;
}

export function securityMasterConflictResolveEndpoint(conflictId: string): string {
  return `${securityMasterConflictsEndpoint()}/${pathSegment(conflictId, "conflictId")}/resolve`;
}

export function reconciliationRunEndpoint(reconciliationRunId: string): string {
  return `${RECONCILIATION_API_ENDPOINTS.runs}/${pathSegment(reconciliationRunId, "reconciliationRunId")}`;
}

export function reconciliationStatementRunsEndpoint(): string {
  return RECONCILIATION_API_ENDPOINTS.statementRuns;
}

export function reconciliationStatementRunEndpoint(runId: string): string {
  return `${RECONCILIATION_API_ENDPOINTS.statementRuns}/${pathSegment(runId, "runId")}`;
}

export function reconciliationStatementExceptionsEndpoint(): string {
  return RECONCILIATION_API_ENDPOINTS.statementExceptions;
}

export function reconciliationBreakQueueEndpoint(options: { status?: string; fundAccountId?: string } = {}): string {
  return `${RECONCILIATION_API_ENDPOINTS.breakQueue}${queryString(options)}`;
}

export function reconciliationBreakEndpoint(breakId: string): string {
  return `${RECONCILIATION_API_ENDPOINTS.breakQueue}/${pathSegment(breakId, "breakId")}`;
}

export function reconciliationBreakAuditEndpoint(breakId: string): string {
  return `${reconciliationBreakEndpoint(breakId)}/audit`;
}

export function reconciliationBreakReviewEndpoint(breakId: string): string {
  return `${reconciliationBreakEndpoint(breakId)}/review`;
}

export function reconciliationBreakResolveEndpoint(breakId: string): string {
  return `${reconciliationBreakEndpoint(breakId)}/resolve`;
}

export function reconciliationBreakAssignEndpoint(breakId: string): string {
  return `${reconciliationBreakEndpoint(breakId)}/assign`;
}

export function reconciliationBreakTransitionEndpoint(breakId: string): string {
  return `${reconciliationBreakEndpoint(breakId)}/transition`;
}

export function reconciliationBreakCommentsEndpoint(breakId: string): string {
  return `${reconciliationBreakEndpoint(breakId)}/comments`;
}

export function reconciliationBreakCommentEndpoint(breakId: string, commentId: string): string {
  return `${reconciliationBreakCommentsEndpoint(breakId)}/${pathSegment(commentId, "commentId")}`;
}

export function reconciliationBreakRootCauseEndpoint(breakId: string): string {
  return `${reconciliationBreakEndpoint(breakId)}/root-cause`;
}

export function reconciliationBreakResolutionEndpoint(breakId: string): string {
  return `${reconciliationBreakEndpoint(breakId)}/resolution`;
}

export function reconciliationBreakSignOffEndpoint(breakId: string): string {
  return `${reconciliationBreakEndpoint(breakId)}/sign-off`;
}

export function reconciliationBreakReopenEndpoint(breakId: string): string {
  return `${reconciliationBreakEndpoint(breakId)}/reopen`;
}

export function reconciliationBreakBulkDryRunEndpoint(): string {
  return `${RECONCILIATION_API_ENDPOINTS.breakQueue}/bulk/dry-run`;
}

export function reconciliationBreakBulkExecuteEndpoint(): string {
  return `${RECONCILIATION_API_ENDPOINTS.breakQueue}/bulk/execute`;
}

export function reconciliationBreakBulkStatusEndpoint(bulkActionId: string): string {
  return `${RECONCILIATION_API_ENDPOINTS.breakQueue}/bulk/${pathSegment(bulkActionId, "bulkActionId")}`;
}

export function reconciliationBreakBulkResultEndpoint(bulkActionId: string): string {
  return `${reconciliationBreakBulkStatusEndpoint(bulkActionId)}/result`;
}

export function backfillCheckpointEndpoint(jobId: string): string {
  return `${BACKFILL_API_ENDPOINTS.checkpoints}/${pathSegment(jobId, "jobId")}`;
}

export function backfillCheckpointPendingEndpoint(jobId: string): string {
  return `${backfillCheckpointEndpoint(jobId)}/pending`;
}

export function backfillCheckpointResumeEndpoint(jobId: string): string {
  return `${backfillCheckpointEndpoint(jobId)}/resume`;
}

export function providerEndpoint(providerId: string): string {
  return `/api/providers/${pathSegment(providerId, "providerId")}`;
}

export function providerRemoveEndpoint(providerId: string): string {
  return `${providerEndpoint(providerId)}/remove`;
}

export function providerTestEndpoint(providerId: string): string {
  return `${providerEndpoint(providerId)}/test`;
}

export function symbolSearchEndpoint(query: string): string {
  return `${SYMBOL_API_ENDPOINTS.search}${queryString({ query })}`;
}

export function symbolEndpoint(symbol: string): string {
  return `${SYMBOL_API_ENDPOINTS.symbols}/${pathSegment(symbol, "symbol")}`;
}

export function symbolRemoveEndpoint(symbol: string): string {
  return `${symbolEndpoint(symbol)}/remove`;
}

export function symbolArchiveEndpoint(symbol: string): string {
  return `${symbolEndpoint(symbol)}/archive`;
}

export function qualityAnomalyAcknowledgeEndpoint(anomalyId: string): string {
  return `${QUALITY_API_ENDPOINTS.anomalies}/${pathSegment(anomalyId, "anomalyId")}/acknowledge`;
}

export function marketDataQuoteEndpoint(symbol: string): string {
  return `${MARKET_DATA_API_ENDPOINTS.quotes}/${pathSegment(symbol, "symbol")}`;
}

export function marketDataTradesEndpoint(symbol: string, limit = 25): string {
  return `${MARKET_DATA_API_ENDPOINTS.trades}/${pathSegment(symbol, "symbol")}${queryString({ limit })}`;
}

export function marketDataOrderbookEndpoint(symbol: string, levels = 10): string {
  return `${MARKET_DATA_API_ENDPOINTS.orderbook}/${pathSegment(symbol, "symbol")}${queryString({ levels })}`;
}

export function marketDataQuotesSnapshotEndpoint(symbols?: readonly string[]): string {
  const trimmed = symbols?.map((symbol) => symbol.trim()).filter(Boolean) ?? [];
  return `${MARKET_DATA_API_ENDPOINTS.quotesSnapshot}${queryString({ symbols: trimmed })}`;
}

export function historicalBarsEndpoint(
  symbol: string,
  request: { intervalMinutes: number; from?: string; to?: string; maxBars?: number }
): string {
  return `${MARKET_DATA_API_ENDPOINTS.historical}/${pathSegment(symbol, "symbol")}/bars${queryString(request)}`;
}

function workstationRunBaseEndpoint(runId: string): string {
  return `/api/workstation/runs/${pathSegment(runId, "runId")}`;
}

function workstationRunRootEndpoint(): string {
  return "/api/workstation/runs";
}

function pathSegment(value: string, name: string): string {
  const trimmed = value.trim();
  if (!trimmed) {
    throw new Error(`Cannot build Meridian API endpoint: ${name} is required.`);
  }

  return encodeURIComponent(trimmed);
}

function queryString(params: Record<string, string | number | boolean | readonly string[] | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (Array.isArray(value)) {
      const normalizedValues = value.map((entry) => entry.trim()).filter(Boolean);
      if (normalizedValues.length > 0) {
        search.set(key, normalizedValues.join(","));
      }
      continue;
    }

    if (typeof value === "string") {
      const trimmed = value.trim();
      if (trimmed) {
        search.set(key, trimmed);
      }
      continue;
    }

    if (typeof value === "number") {
      if (Number.isFinite(value)) {
        search.set(key, String(value));
      }
      continue;
    }

    if (typeof value === "boolean") {
      search.set(key, String(value));
    }
  }

  const value = search.toString();
  return value ? `?${value}` : "";
}
