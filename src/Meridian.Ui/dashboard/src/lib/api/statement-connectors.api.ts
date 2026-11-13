// Statement connector library endpoints. Re-exported through @/lib/api.
import type { MarginControlCenter, MarginCertificationRequest, MarginCertificationResult, ReconciliationBreakQueueItem, ReconciliationBulkCaseworkRequest, ReconciliationBulkCaseworkResult, ReconciliationCalibrationSummary, StatementConnectorDescriptor, StatementFetchPreviewRequest, StatementFetchSchedule, StatementFetchScheduleUpsertRequest, StatementImportCommitResult, StatementImportPreview, StatementImportPreviewRequest, StatementImportSourceKind, StatementMappingProfile, ReconciliationCaseworkCommand, ReconciliationCaseworkOperationResult, ResolveReconciliationBreakRequest, ReviewReconciliationBreakRequest } from "@/types";
import { RECONCILIATION_API_ENDPOINTS, reconciliationBreakAssignEndpoint, reconciliationBreakAuditEndpoint, reconciliationBreakBulkDryRunEndpoint, reconciliationBreakBulkExecuteEndpoint, reconciliationBreakBulkStatusEndpoint, reconciliationBreakCommentEndpoint, reconciliationBreakCommentsEndpoint, reconciliationBreakEndpoint, reconciliationBreakQueueEndpoint, reconciliationBreakReopenEndpoint, reconciliationBreakResolutionEndpoint, reconciliationBreakResolveEndpoint, reconciliationBreakReviewEndpoint, reconciliationBreakSupersedeEndpoint, reconciliationBreakRootCauseEndpoint, reconciliationBreakSignOffEndpoint, reconciliationBreakTransitionEndpoint, reconciliationBreakWaiveEndpoint, reconciliationStatementFetchScheduleEndpoint, reconciliationStatementFetchScheduleRunEndpoint, reconciliationStatementMappingProfileEndpoint, STATEMENT_CONNECTOR_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import { deleteJson, getJson, postFormData, postJson, putJson, type ApiRequestOptions } from "@/lib/api/http-client";

// --- Statement connector library ---

export function getStatementConnectors(options: ApiRequestOptions = {}) {
  return getJson<StatementConnectorDescriptor[]>(STATEMENT_CONNECTOR_API_ENDPOINTS.connectors, options);
}

export function getMarginControlCenter(options: ApiRequestOptions = {}) {
  return getJson<MarginControlCenter>(STATEMENT_CONNECTOR_API_ENDPOINTS.marginControl, options);
}

export function certifyMarginSnapshot(request: MarginCertificationRequest, options: ApiRequestOptions = {}) {
  return postJson<MarginCertificationResult>(STATEMENT_CONNECTOR_API_ENDPOINTS.marginCertifications, request, options);
}

export function listStatementMappingProfiles(options: ApiRequestOptions = {}) {
  return getJson<StatementMappingProfile[]>(STATEMENT_CONNECTOR_API_ENDPOINTS.mappingProfiles, options);
}

export function upsertStatementMappingProfile(profile: StatementMappingProfile, options: ApiRequestOptions = {}) {
  return putJson<StatementMappingProfile>(STATEMENT_CONNECTOR_API_ENDPOINTS.mappingProfiles, profile, options);
}

export function deleteStatementMappingProfile(profileId: string, options: ApiRequestOptions = {}) {
  return deleteJson<void>(reconciliationStatementMappingProfileEndpoint(profileId), options);
}

export function previewStatementImport(request: StatementImportPreviewRequest, options: ApiRequestOptions = {}) {
  const formData = new FormData();
  formData.append("file", request.file);
  appendOptionalStatementFormField(formData, "connectorId", request.connectorId);
  appendOptionalStatementFormField(formData, "mappingProfileId", request.mappingProfileId);
  formData.append("externalAccountId", request.externalAccountId);
  formData.append("sourceKind", request.sourceKind);
  formData.append("sourceInstitution", request.sourceInstitution);
  formData.append("fundAccountId", request.fundAccountId);
  formData.append("periodStart", request.periodStart);
  formData.append("periodEnd", request.periodEnd);
  return postFormData<StatementImportPreview>(STATEMENT_CONNECTOR_API_ENDPOINTS.importPreview, formData, options);
}

export function commitStatementImport(
  request: {
    file: File;
    connectorId?: string | null;
    mappingProfileId?: string | null;
    externalAccountId: string;
    sourceKind: StatementImportSourceKind;
    sourceInstitution: string;
    fundAccountId: string;
    periodStart: string;
    periodEnd: string;
    toleranceProfileId?: string | null;
  },
  options: ApiRequestOptions = {}
) {
  const formData = new FormData();
  formData.append("file", request.file);
  appendOptionalStatementFormField(formData, "connectorId", request.connectorId);
  appendOptionalStatementFormField(formData, "mappingProfileId", request.mappingProfileId);
  formData.append("externalAccountId", request.externalAccountId);
  formData.append("sourceKind", request.sourceKind);
  formData.append("sourceInstitution", request.sourceInstitution);
  formData.append("fundAccountId", request.fundAccountId);
  formData.append("periodStart", request.periodStart);
  formData.append("periodEnd", request.periodEnd);
  appendOptionalStatementFormField(formData, "toleranceProfileId", request.toleranceProfileId);
  return postFormData<StatementImportCommitResult>(STATEMENT_CONNECTOR_API_ENDPOINTS.importCommit, formData, options);
}

export function fetchStatementPreview(request: StatementFetchPreviewRequest, options: ApiRequestOptions = {}) {
  return postJson<StatementImportPreview>(STATEMENT_CONNECTOR_API_ENDPOINTS.fetchPreview, request, options);
}

export function listStatementFetchSchedules(options: ApiRequestOptions = {}) {
  return getJson<StatementFetchSchedule[]>(STATEMENT_CONNECTOR_API_ENDPOINTS.fetchSchedules, options);
}

export function upsertStatementFetchSchedule(
  request: StatementFetchScheduleUpsertRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<StatementFetchSchedule>(STATEMENT_CONNECTOR_API_ENDPOINTS.fetchSchedules, request, options);
}

export function deleteStatementFetchSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return deleteJson<void>(reconciliationStatementFetchScheduleEndpoint(scheduleId), options);
}

export function runStatementFetchSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return postJson<StatementImportCommitResult>(
    reconciliationStatementFetchScheduleRunEndpoint(scheduleId),
    undefined,
    options
  );
}

function appendOptionalStatementFormField(formData: FormData, name: string, value: string | null | undefined) {
  const trimmed = value?.trim();
  if (trimmed) {
    formData.append(name, trimmed);
  }
}

export function getReconciliationBreakQueue(status?: string, fundAccountId?: string) { return getJson<ReconciliationBreakQueueItem[]>(reconciliationBreakQueueEndpoint({ status, fundAccountId })); }

export function getReconciliationBreakDetail(breakId: string) { return getJson<ReconciliationBreakQueueItem>(reconciliationBreakEndpoint(breakId)); }

export function getReconciliationBreakAudit(breakId: string) { return getJson<unknown>(reconciliationBreakAuditEndpoint(breakId)); }

export function reviewReconciliationBreak(request: ReviewReconciliationBreakRequest) {
  return postJson<ReconciliationCaseworkOperationResult>(
    reconciliationBreakReviewEndpoint(request.breakId),
    request
  );
}

export function resolveReconciliationBreak(request: ResolveReconciliationBreakRequest) {
  return postJson<ReconciliationCaseworkOperationResult>(
    reconciliationBreakResolveEndpoint(request.breakId),
    request
  );
}

export function waiveReconciliationBreak(request: ReconciliationCaseworkCommand) { return postJson<ReconciliationCaseworkOperationResult>(reconciliationBreakWaiveEndpoint(request.breakId), request); }

export function supersedeReconciliationBreak(request: ReconciliationCaseworkCommand) { return postJson<ReconciliationCaseworkOperationResult>(reconciliationBreakSupersedeEndpoint(request.breakId), request); }

export function assignReconciliationBreak(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationCaseworkOperationResult>(reconciliationBreakAssignEndpoint(request.breakId), request);
}

export function transitionReconciliationBreak(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationCaseworkOperationResult>(reconciliationBreakTransitionEndpoint(request.breakId), request);
}

export function addReconciliationBreakComment(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationCaseworkOperationResult>(reconciliationBreakCommentsEndpoint(request.breakId), request);
}

export function editReconciliationBreakComment(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationCaseworkOperationResult>(
    reconciliationBreakCommentEndpoint(request.breakId, request.commentId ?? ""),
    request
  );
}

export function deleteReconciliationBreakComment(request: ReconciliationCaseworkCommand) {
  return deleteJson<ReconciliationCaseworkOperationResult>(
    reconciliationBreakCommentEndpoint(request.breakId, request.commentId ?? ""),
    {},
    request
  );
}

export function setReconciliationBreakRootCause(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationCaseworkOperationResult>(reconciliationBreakRootCauseEndpoint(request.breakId), request);
}

export function setReconciliationBreakResolution(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationCaseworkOperationResult>(reconciliationBreakResolutionEndpoint(request.breakId), request);
}

export function signOffReconciliationBreak(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationCaseworkOperationResult>(reconciliationBreakSignOffEndpoint(request.breakId), request);
}

export function reopenReconciliationBreak(request: ReconciliationCaseworkCommand) {
  return postJson<ReconciliationCaseworkOperationResult>(reconciliationBreakReopenEndpoint(request.breakId), request);
}

export function dryRunReconciliationBreakBulkAction(request: ReconciliationBulkCaseworkRequest) {
  return postJson<ReconciliationBulkCaseworkResult>(reconciliationBreakBulkDryRunEndpoint(), request);
}

export function executeReconciliationBreakBulkAction(request: ReconciliationBulkCaseworkRequest) {
  return postJson<ReconciliationBulkCaseworkResult>(reconciliationBreakBulkExecuteEndpoint(), request);
}

export function getReconciliationBreakBulkActionStatus(bulkActionId: string) {
  return getJson<unknown>(reconciliationBreakBulkStatusEndpoint(bulkActionId));
}

export function getReconciliationCalibrationSummary() {
  return getJson<ReconciliationCalibrationSummary>(RECONCILIATION_API_ENDPOINTS.calibrationSummary); }
