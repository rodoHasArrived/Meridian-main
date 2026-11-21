// Data workspace query and upload endpoints. Re-exported through @/lib/api.
import type { DataQueryRequest, DataQueryResult, DataUploadPreviewResult, DataUploadTemplateCatalog, DataUploadWorkbookPreviewResult, DataWorkspaceResponse } from "@/types";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import { getJson, postFormData, postJson, type ApiRequestOptions } from "@/lib/api/http-client";

export function getDataWorkspace(options: ApiRequestOptions = {}) {
  return getJson<DataWorkspaceResponse>(WORKSTATION_API_ENDPOINTS.data, options);
}

export function runDataQuery(request: DataQueryRequest, options: ApiRequestOptions = {}) {
  return postJson<DataQueryResult>(WORKSTATION_API_ENDPOINTS.dataQuery, request, options);
}

export function getDataUploadTemplates(options: ApiRequestOptions = {}) {
  return getJson<DataUploadTemplateCatalog>(WORKSTATION_API_ENDPOINTS.dataUploadTemplates, options);
}

export function previewDataUpload(
  request: { templateId: string; file: File },
  options: ApiRequestOptions = {}
) {
  const formData = new FormData();
  formData.append("templateId", request.templateId);
  formData.append("file", request.file);
  return postFormData<DataUploadPreviewResult>(WORKSTATION_API_ENDPOINTS.dataUploadPreview, formData, options);
}

export function getOnboardingWorkbookDownloadUrl(templateIds?: string[]): string {
  const base = WORKSTATION_API_ENDPOINTS.dataUploadWorkbook;
  const selected = (templateIds ?? []).map((id) => id.trim()).filter(Boolean);
  if (selected.length === 0) {
    return base;
  }

  const query = new URLSearchParams({ templateIds: selected.join(",") });
  return `${base}?${query.toString()}`;
}

export function previewDataUploadWorkbook(
  request: { file: File },
  options: ApiRequestOptions = {}
) {
  const formData = new FormData();
  formData.append("file", request.file);
  return postFormData<DataUploadWorkbookPreviewResult>(
    WORKSTATION_API_ENDPOINTS.dataUploadWorkbookPreview,
    formData,
    options
  );
}
