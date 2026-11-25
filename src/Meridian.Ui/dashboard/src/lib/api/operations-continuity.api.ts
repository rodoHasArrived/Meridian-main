// Operations continuity workflows, approval policy, close calendar, and command-center endpoints.
// Re-exported through @/lib/api.
import type { FinancialOperationsCommandCenter, OperationsContinuityWorkflow, OperationsContinuityWorkflowSummary, OperationsAssignBreakCaseRequest, OperationsBreakCase, OperationsApprovalDecisionRequest, OperationsApprovalPolicyMatrix, OperationsApprovalPolicyRuleUpsertRequest, OperationsApprovalPolicyRuleUpsertResult, OperationsChecklistAcknowledgeRequest, OperationsCloseCalendar, OperationsCloseCalendarItemUpsertRequest, OperationsCloseCalendarItemUpsertResult, OperationsCloseChecklistTask, OperationsCloseReadiness, OperationsCloseWorkflowRequest, OperationsGatePostureRequest, OperationsLedgerDraftRequest, OperationsLedgerPostRequest, OperationsLedgerPreview, OperationsLedgerValidationRequest, OperationsReconciliationRunRequest, OperationsReopenWorkflowRequest, OperationsRejectWorkflowRequest, OperationsResolveBreakCaseRequest, OperationsSecurityMasterOverrideApprovalRequest, OperationsSecurityMasterResolveRequest, OperationsStartWorkflowRequest, OperationsSubmitApprovalRequest, OperationsTimelineEntry, OperationsTransitionRequest, OperationsTransitionResult, PrivateCapitalCloseCockpit } from "@/types";
import { WORKSTATION_API_ENDPOINTS, workstationFinancialOperationsCommandCenterEndpoint, workstationOperationsContinuityApprovalApproveEndpoint, workstationOperationsContinuityApprovalRejectEndpoint, workstationOperationsContinuityApprovalSubmitEndpoint, workstationOperationsContinuityBrokerImportEndpoint, workstationOperationsContinuityBrokerNormalizeEndpoint, workstationOperationsContinuityBreakAssignEndpoint, workstationOperationsContinuityBreakResolveEndpoint, workstationOperationsContinuityBreaksEndpoint, workstationOperationsContinuityChecklistEndpoint, workstationOperationsContinuityChecklistAcknowledgeEndpoint, workstationOperationsContinuityCloseEndpoint, workstationOperationsContinuityDetailEndpoint, workstationOperationsContinuityEndpoint, workstationOperationsContinuityCloseCalendarEndpoint, workstationOperationsContinuityCloseReadinessEndpoint, workstationOperationsContinuityLedgerDraftEndpoint, workstationOperationsContinuityLedgerPostEndpoint, workstationOperationsPrivateCapitalCloseCockpitEndpoint, workstationOperationsContinuityLedgerPreviewEndpoint, workstationOperationsContinuityLedgerValidateEndpoint, workstationOperationsContinuityPostureRefreshEndpoint, workstationOperationsContinuityReconciliationRunEndpoint, workstationOperationsContinuityReopenEndpoint, workstationOperationsContinuitySecurityMasterOverrideApproveEndpoint, workstationOperationsContinuitySecurityMasterResolveEndpoint, workstationOperationsContinuityTimelineEndpoint } from "@/lib/workstation-endpoints";
import { getJson, postJson, type ApiRequestOptions } from "@/lib/api/http-client";

export function getOperationsContinuityWorkflows(
  filters: { fundAccountId?: string; ledgerBookId?: string; periodId?: string; status?: string } = {},
  options: ApiRequestOptions = {}
) {
  return getJson<OperationsContinuityWorkflowSummary[]>(workstationOperationsContinuityEndpoint(filters), options);
}

export function getOperationsContinuityWorkflow(workflowId: string, options: ApiRequestOptions = {}) {
  return getJson<OperationsContinuityWorkflow>(workstationOperationsContinuityDetailEndpoint(workflowId), options);
}

export function getOperationsContinuityTimeline(workflowId: string, options: ApiRequestOptions = {}) {
  return getJson<OperationsTimelineEntry[]>(workstationOperationsContinuityTimelineEndpoint(workflowId), options);
}

export function getOperationsContinuityBreaks(workflowId: string, options: ApiRequestOptions = {}) {
  return getJson<OperationsBreakCase[]>(workstationOperationsContinuityBreaksEndpoint(workflowId), options);
}

export function getOperationsContinuityLedgerPreview(workflowId: string, options: ApiRequestOptions = {}) {
  return getJson<OperationsLedgerPreview | null>(workstationOperationsContinuityLedgerPreviewEndpoint(workflowId), options);
}

export function getOperationsContinuityChecklist(workflowId: string, options: ApiRequestOptions = {}) {
  return getJson<OperationsCloseChecklistTask[]>(workstationOperationsContinuityChecklistEndpoint(workflowId), options);
}

export function getOperationsContinuityCloseReadiness(workflowId: string, options: ApiRequestOptions = {}) {
  return getJson<OperationsCloseReadiness>(workstationOperationsContinuityCloseReadinessEndpoint(workflowId), options);
}

export function startOperationsContinuityWorkflow(
  request: OperationsStartWorkflowRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(workstationOperationsContinuityEndpoint(), request, options);
}

export function importOperationsContinuityBrokerData(
  workflowId: string,
  request: OperationsTransitionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityBrokerImportEndpoint(workflowId),
    request,
    options
  );
}

export function normalizeOperationsContinuityBrokerTransactions(
  workflowId: string,
  request: OperationsTransitionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityBrokerNormalizeEndpoint(workflowId),
    request,
    options
  );
}

export function refreshOperationsContinuityGatePosture(
  workflowId: string,
  request: OperationsGatePostureRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityPostureRefreshEndpoint(workflowId),
    request,
    options
  );
}

export function resolveOperationsContinuitySecurityMasterMappings(
  workflowId: string,
  request: OperationsSecurityMasterResolveRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuitySecurityMasterResolveEndpoint(workflowId),
    request,
    options
  );
}

export function approveOperationsContinuitySecurityMasterOverride(
  workflowId: string,
  overrideId: string,
  request: OperationsSecurityMasterOverrideApprovalRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuitySecurityMasterOverrideApproveEndpoint(workflowId, overrideId),
    request,
    options
  );
}

export function draftOperationsContinuityLedger(
  workflowId: string,
  request: OperationsLedgerDraftRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityLedgerDraftEndpoint(workflowId),
    request,
    options
  );
}

export function validateOperationsContinuityLedger(
  workflowId: string,
  request: OperationsLedgerValidationRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityLedgerValidateEndpoint(workflowId),
    request,
    options
  );
}

export function postOperationsContinuityLedger(
  workflowId: string,
  request: OperationsLedgerPostRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityLedgerPostEndpoint(workflowId),
    request,
    options
  );
}

export function runOperationsContinuityReconciliation(
  workflowId: string,
  request: OperationsReconciliationRunRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityReconciliationRunEndpoint(workflowId),
    request,
    options
  );
}

export function assignOperationsContinuityBreakCase(
  workflowId: string,
  breakId: string,
  request: OperationsAssignBreakCaseRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityBreakAssignEndpoint(workflowId, breakId),
    request,
    options
  );
}

export function resolveOperationsContinuityBreakCase(
  workflowId: string,
  breakId: string,
  request: OperationsResolveBreakCaseRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityBreakResolveEndpoint(workflowId, breakId),
    request,
    options
  );
}

export function acknowledgeOperationsContinuityChecklistTask(
  workflowId: string,
  taskId: string,
  request: OperationsChecklistAcknowledgeRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityChecklistAcknowledgeEndpoint(workflowId, taskId),
    request,
    options
  );
}

export function submitOperationsContinuityApproval(
  workflowId: string,
  request: OperationsSubmitApprovalRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityApprovalSubmitEndpoint(workflowId),
    request,
    options
  );
}

export function approveOperationsContinuityWorkflow(
  workflowId: string,
  request: OperationsApprovalDecisionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityApprovalApproveEndpoint(workflowId),
    request,
    options
  );
}

export function rejectOperationsContinuityWorkflow(
  workflowId: string,
  request: OperationsRejectWorkflowRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityApprovalRejectEndpoint(workflowId),
    request,
    options
  );
}

export function closeOperationsContinuityWorkflow(
  workflowId: string,
  request: OperationsCloseWorkflowRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityCloseEndpoint(workflowId),
    request,
    options
  );
}

export function reopenOperationsContinuityWorkflow(
  workflowId: string,
  request: OperationsReopenWorkflowRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsTransitionResult>(
    workstationOperationsContinuityReopenEndpoint(workflowId),
    request,
    options
  );
}

export function getOperationsApprovalPolicyMatrix(options: ApiRequestOptions = {}) {
  return getJson<OperationsApprovalPolicyMatrix>(WORKSTATION_API_ENDPOINTS.operationsContinuityApprovalPolicyMatrix, options);
}

export function upsertOperationsApprovalPolicyRule(
  request: OperationsApprovalPolicyRuleUpsertRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsApprovalPolicyRuleUpsertResult>(
    WORKSTATION_API_ENDPOINTS.operationsContinuityApprovalPolicyRules,
    request,
    options
  );
}

export function getOperationsCloseCalendar(
  filters: { fundAccountId?: string; periodId?: string } = {},
  options: ApiRequestOptions = {}
) {
  return getJson<OperationsCloseCalendar>(workstationOperationsContinuityCloseCalendarEndpoint(filters), options);
}

export interface FinancialOperationsCommandCenterQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  fundAccountId?: string | null;
  periodId?: string | null;
  entityId?: string | null;
}

export function getFinancialOperationsCommandCenter(
  query: FinancialOperationsCommandCenterQuery = {},
  options: ApiRequestOptions = {}
) {
  return getJson<FinancialOperationsCommandCenter>(
    workstationFinancialOperationsCommandCenterEndpoint({
      fundProfileId: query.fundProfileId ?? undefined,
      ledgerBookId: query.ledgerBookId ?? undefined,
      fundAccountId: query.fundAccountId ?? undefined,
      periodId: query.periodId ?? undefined,
      entityId: query.entityId ?? undefined
    }),
    options
  );
}

export interface PrivateCapitalCloseCockpitQuery {
  fundProfileId?: string | null;
  ledgerBookId?: string | null;
  fundAccountId?: string | null;
  periodId?: string | null;
  entityId?: string | null;
}

export function getPrivateCapitalCloseCockpit(
  query: PrivateCapitalCloseCockpitQuery = {},
  options: ApiRequestOptions = {}
) {
  return getJson<PrivateCapitalCloseCockpit>(
    workstationOperationsPrivateCapitalCloseCockpitEndpoint({
      fundProfileId: query.fundProfileId ?? undefined,
      ledgerBookId: query.ledgerBookId ?? undefined,
      fundAccountId: query.fundAccountId ?? undefined,
      periodId: query.periodId ?? undefined,
      entityId: query.entityId ?? undefined
    }),
    options
  );
}

export function upsertOperationsCloseCalendarItem(
  request: OperationsCloseCalendarItemUpsertRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<OperationsCloseCalendarItemUpsertResult>(
    WORKSTATION_API_ENDPOINTS.operationsContinuityCloseCalendarItems,
    request,
    options
  );
}
