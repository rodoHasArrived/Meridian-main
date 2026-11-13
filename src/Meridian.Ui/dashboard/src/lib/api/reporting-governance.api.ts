// Governance workspace, report template, reporting schedule, and analysis export endpoints.
// Re-exported through @/lib/api.
import { reportingSchedulePauseEndpoint, reportingScheduleResumeEndpoint, reportingScheduleRunNowEndpoint } from "@/lib/reporting-schedule-endpoints";
import type { ExportAnalysisResult, ReportingWorkspaceResponse, ReportTemplateDecisionRequest, ReportTemplateDraftRequest, ReportTemplateGovernanceRecord, RenderReportTemplateRequest, RenderReportTemplateResponse, ReportingScheduleRecord, ReportingScheduleRunResult, ReportingStarterKitProvisionResult, ReportingScheduleUpsertRequest } from "@/types";
import { EXPORT_API_ENDPOINTS, FUND_STRUCTURE_API_ENDPOINTS, WORKSTATION_API_ENDPOINTS, reportingTemplateApproveEndpoint, reportingTemplateRejectEndpoint, reportingTemplateSubmitEndpoint, reportingStarterKitProvisionEndpoint } from "@/lib/workstation-endpoints";
import { getJson, postJson, type ApiRequestOptions } from "@/lib/api/http-client";
import { getAccountingWorkspace } from "@/lib/api/accounting-workspace.api";

export function getGovernanceWorkspace(options: ApiRequestOptions = {}) {
  return getAccountingWorkspace(options);
}

export function getReportingWorkspace(options: ApiRequestOptions = {}) {
  return getJson<ReportingWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.reporting, options);
}

export function createReportTemplateDraft(request: ReportTemplateDraftRequest, options: ApiRequestOptions = {}) {
  return postJson<ReportTemplateGovernanceRecord>(
    FUND_STRUCTURE_API_ENDPOINTS.reportingTemplateDrafts,
    request,
    options
  );
}

export function renderReportTemplate(request: RenderReportTemplateRequest, options: ApiRequestOptions = {}) {
  return postJson<RenderReportTemplateResponse>(
    FUND_STRUCTURE_API_ENDPOINTS.reportingTemplateRender,
    request,
    options
  );
}

export function submitReportTemplateDraft(
  templateName: string,
  version: number,
  request: ReportTemplateDecisionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ReportTemplateGovernanceRecord>(
    reportingTemplateSubmitEndpoint(templateName, version),
    request,
    options
  );
}

export function approveReportTemplateDraft(
  templateName: string,
  version: number,
  request: ReportTemplateDecisionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ReportTemplateGovernanceRecord>(
    reportingTemplateApproveEndpoint(templateName, version),
    request,
    options
  );
}

export function rejectReportTemplateDraft(
  templateName: string,
  version: number,
  request: ReportTemplateDecisionRequest,
  options: ApiRequestOptions = {}
) {
  return postJson<ReportTemplateGovernanceRecord>(
    reportingTemplateRejectEndpoint(templateName, version),
    request,
    options
  );
}

export function listReportingSchedules(options: ApiRequestOptions = {}) {
  return getJson<ReportingScheduleRecord[]>(FUND_STRUCTURE_API_ENDPOINTS.reportingSchedules, options);
}

export function saveReportingSchedule(request: ReportingScheduleUpsertRequest, options: ApiRequestOptions = {}) {
  return postJson<ReportingScheduleRecord>(FUND_STRUCTURE_API_ENDPOINTS.reportingSchedules, request, options);
}

export function provisionReportingStarterKit(kitId: string, options: ApiRequestOptions = {}) {
  return postJson<ReportingStarterKitProvisionResult>(reportingStarterKitProvisionEndpoint(kitId), undefined, options);
}

export function pauseReportingSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return postJson<ReportingScheduleRecord>(reportingSchedulePauseEndpoint(scheduleId), undefined, options);
}

export function resumeReportingSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return postJson<ReportingScheduleRecord>(reportingScheduleResumeEndpoint(scheduleId), undefined, options);
}

export function runReportingScheduleNow(scheduleId: string, options: ApiRequestOptions = {}) {
  return postJson<ReportingScheduleRunResult>(reportingScheduleRunNowEndpoint(scheduleId), undefined, options);
}

export function runAnalysisExport(profileId: string, options: ApiRequestOptions = {}) {
  return postJson<ExportAnalysisResult>(EXPORT_API_ENDPOINTS.analysis, { profileId }, options);
}
