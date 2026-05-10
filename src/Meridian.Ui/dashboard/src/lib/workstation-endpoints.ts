export const WORKSTATION_API_ENDPOINTS = {
  systemStatus: "/api/status",
  session: "/api/workstation/session",
  strategy: "/api/workstation/strategy",
  trading: "/api/workstation/trading",
  tradingReadiness: "/api/workstation/trading/readiness",
  operatorInbox: "/api/workstation/operator/inbox",
  portfolio: "/api/workstation/portfolio",
  data: "/api/workstation/data",
  accounting: "/api/workstation/accounting",
  reporting: "/api/workstation/reporting",
  workflowSummary: "/api/workstation/workflow-summary",
  workflowLibrary: "/api/workstation/workflows",
  workflowPresets: "/api/workstation/workflows/presets",
  runHistory: "/api/workstation/runs/history",
  runTimeline: "/api/workstation/runs/timeline",
  runSweeps: "/api/workstation/runs/sweeps",
  evidenceSubjects: "/api/workstation/evidence/subjects",
  evidenceTemplates: "/api/workstation/evidence/templates"
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
  sessions: "/api/execution/sessions",
  sessionsCreate: "/api/execution/sessions/create",
  audit: "/api/execution/audit",
  controls: "/api/execution/controls",
  manualOverrides: "/api/execution/controls/manual-overrides"
} as const;

export const REPLAY_API_ENDPOINTS = {
  files: "/api/replay/files",
  start: "/api/replay/start"
} as const;

export const PROMOTION_API_ENDPOINTS = {
  approve: "/api/promotion/approve",
  reject: "/api/promotion/reject",
  history: "/api/promotion/history"
} as const;

export const PORTFOLIO_API_ENDPOINTS = {
  household: "/api/portfolio/household"
} as const;

export function workstationOperatorInboxEndpoint(fundAccountId?: string): string {
  return fundAccountId
    ? `${WORKSTATION_API_ENDPOINTS.operatorInbox}?fundAccountId=${encodeURIComponent(fundAccountId)}`
    : WORKSTATION_API_ENDPOINTS.operatorInbox;
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
    ? `${WORKSTATION_API_ENDPOINTS.workflowPresets}/${encodeURIComponent(presetId)}`
    : WORKSTATION_API_ENDPOINTS.workflowPresets;
}

export function workstationWorkflowPresetPinEndpoint(presetId: string): string {
  return `${workstationWorkflowPresetEndpoint(presetId)}/pin`;
}

export function workstationWorkflowPresetUsedEndpoint(presetId: string): string {
  return `${workstationWorkflowPresetEndpoint(presetId)}/used`;
}

export function workstationRunLedgerEndpoint(runId: string): string {
  return `${workstationRunBaseEndpoint(runId)}/ledger`;
}

export function workstationRunLedgerJournalEndpoint(runId: string): string {
  return `${workstationRunLedgerEndpoint(runId)}/journal`;
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
  return `${WORKSTATION_API_ENDPOINTS.evidenceSubjects}/${encodeURIComponent(subjectKind)}/${encodeURIComponent(subjectId)}`;
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

export function promotionEvaluateEndpoint(runId: string): string {
  return `/api/promotion/evaluate/${encodeURIComponent(runId)}`;
}

export function executionOrderCancelEndpoint(orderId: string): string {
  return `/api/execution/orders/${encodeURIComponent(orderId)}/cancel`;
}

export function executionPositionCloseEndpoint(symbol: string): string {
  return `/api/execution/positions/${encodeURIComponent(symbol)}/close`;
}

export function executionSessionEndpoint(sessionId: string): string {
  return `${EXECUTION_API_ENDPOINTS.sessions}/${encodeURIComponent(sessionId)}`;
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
  return `${EXECUTION_API_ENDPOINTS.manualOverrides}/${encodeURIComponent(overrideId)}/clear`;
}

export function replayFilesEndpoint(symbol?: string): string {
  return `${REPLAY_API_ENDPOINTS.files}${queryString({ symbol })}`;
}

export function portfolioHouseholdEndpoint(provider = "alpaca"): string {
  return `${PORTFOLIO_API_ENDPOINTS.household}${queryString({ provider })}`;
}

export function replaySessionActionEndpoint(
  sessionId: string,
  action: "pause" | "resume" | "stop" | "seek" | "speed" | "status"
): string {
  return `/api/replay/${encodeURIComponent(sessionId)}/${action}`;
}

function workstationRunBaseEndpoint(runId: string): string {
  return `/api/workstation/runs/${encodeURIComponent(runId)}`;
}

function queryString(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== "") {
      search.set(key, String(value));
    }
  }

  const value = search.toString();
  return value ? `?${value}` : "";
}
