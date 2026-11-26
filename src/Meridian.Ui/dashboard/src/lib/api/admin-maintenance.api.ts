// Admin, storage maintenance, and data-package endpoints. Re-exported through @/lib/api.
import type { AdminCleanupExecuteResponse, AdminCleanupPreviewResponse, AdminErrorCodesResponse, AdminMaintenanceHistoryResponse, AdminMaintenanceRunRequest, AdminMaintenanceScheduleResponse, AdminQuickCheckResponse, AdminRetentionResponse, AdminSelfTestResponse, AdminShowConfigResponse, AdminStoragePermissionsResponse, AdminStorageTiersResponse, AdminStorageUsageResponse, DataPackageContentsResponse, DataPackageCreateRequest, DataPackageListResponse, DataPackageResult, DataPackageValidateRequest, DataPackageValidationResponse, MaintenanceExecution, MaintenanceScheduleHistoryResponse, MaintenanceSchedulesResponse } from "@/types";
import { ADMIN_OPERATIONS_API_ENDPOINTS, DIAGNOSTICS_API_ENDPOINTS, MAINTENANCE_API_ENDPOINTS, PACKAGING_API_ENDPOINTS, adminMaintenanceHistoryEndpoint, adminMaintenanceRunEndpoint, adminRetentionDeleteEndpoint, adminStorageMigrateEndpoint, packagingContentsEndpoint, packagingListEndpoint, maintenanceScheduleDisableEndpoint, maintenanceScheduleEnableEndpoint, maintenanceScheduleEndpoint, maintenanceScheduleHistoryEndpoint, maintenanceScheduleRunEndpoint } from "@/lib/workstation-endpoints";
import { deleteJson, getJson, postJson, type ApiRequestOptions } from "@/lib/api/http-client";

export function getAdminMaintenanceSchedule(options: ApiRequestOptions = {}) {
  return getJson<AdminMaintenanceScheduleResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.maintenanceSchedule, options);
}

export function runAdminMaintenance(request: AdminMaintenanceRunRequest = {}, options: ApiRequestOptions = {}) {
  return postJson<MaintenanceExecution>(ADMIN_OPERATIONS_API_ENDPOINTS.maintenanceRun, request, options);
}

export function getAdminMaintenanceRun(runId: string, options: ApiRequestOptions = {}) {
  return getJson<MaintenanceExecution>(adminMaintenanceRunEndpoint(runId), options);
}

export function getAdminMaintenanceHistory(limit = 20, options: ApiRequestOptions = {}) {
  return getJson<AdminMaintenanceHistoryResponse>(adminMaintenanceHistoryEndpoint(limit), options);
}

export function getAdminStorageTiers(options: ApiRequestOptions = {}) {
  return getJson<AdminStorageTiersResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.storageTiers, options);
}

export function migrateAdminStorage(targetTier: string, options: ApiRequestOptions = {}) {
  return postJson<{ targetTier: string; plan: Record<string, unknown>; timestamp?: string }>(adminStorageMigrateEndpoint(targetTier), undefined, options);
}

export function getAdminStorageUsage(options: ApiRequestOptions = {}) {
  return getJson<AdminStorageUsageResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.storageUsage, options);
}

export function getAdminRetention(options: ApiRequestOptions = {}) {
  return getJson<AdminRetentionResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.retention, options);
}

export function deleteAdminRetentionPolicy(policyId: string, options: ApiRequestOptions = {}) {
  return deleteJson<{ policyId: string; deleted: boolean; timestamp?: string }>(adminRetentionDeleteEndpoint(policyId), options);
}

export function applyAdminRetention(options: ApiRequestOptions = {}) {
  return postJson<Record<string, unknown>>(ADMIN_OPERATIONS_API_ENDPOINTS.retentionApply, undefined, options);
}

export function getAdminCleanupPreview(options: ApiRequestOptions = {}) {
  return getJson<AdminCleanupPreviewResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.cleanupPreview, options);
}

export function executeAdminCleanup(options: ApiRequestOptions = {}) {
  return postJson<AdminCleanupExecuteResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.cleanupExecute, undefined, options);
}

export function getAdminStoragePermissions(options: ApiRequestOptions = {}) {
  return getJson<AdminStoragePermissionsResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.storagePermissions, options);
}

export function runAdminSelftest(options: ApiRequestOptions = {}) {
  return postJson<AdminSelfTestResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.selftest, undefined, options);
}

export function getAdminErrorCodes(options: ApiRequestOptions = {}) {
  return getJson<AdminErrorCodesResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.errorCodes, options);
}

export function getAdminShowConfig(options: ApiRequestOptions = {}) {
  return getJson<AdminShowConfigResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.showConfig, options);
}

export function getAdminQuickCheck(options: ApiRequestOptions = {}) {
  return getJson<AdminQuickCheckResponse>(ADMIN_OPERATIONS_API_ENDPOINTS.quickCheck, options);
}

export function getDiagnosticsQuickCheck(options: ApiRequestOptions = {}) {
  return getJson<AdminQuickCheckResponse>(DIAGNOSTICS_API_ENDPOINTS.quickCheck, options);
}

export function getMaintenanceSchedules(options: ApiRequestOptions = {}) {
  return getJson<MaintenanceSchedulesResponse>(MAINTENANCE_API_ENDPOINTS.schedules, options);
}

export function getMaintenanceSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return getJson<MaintenanceSchedulesResponse>(maintenanceScheduleEndpoint(scheduleId), options);
}

export function enableMaintenanceSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return postJson<MaintenanceSchedulesResponse>(maintenanceScheduleEnableEndpoint(scheduleId), undefined, options);
}

export function disableMaintenanceSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return postJson<MaintenanceSchedulesResponse>(maintenanceScheduleDisableEndpoint(scheduleId), undefined, options);
}

export function runMaintenanceSchedule(scheduleId: string, options: ApiRequestOptions = {}) {
  return postJson<MaintenanceExecution>(maintenanceScheduleRunEndpoint(scheduleId), undefined, options);
}

export function getMaintenanceScheduleHistory(scheduleId: string, limit = 20, options: ApiRequestOptions = {}) {
  return getJson<MaintenanceScheduleHistoryResponse>(maintenanceScheduleHistoryEndpoint(scheduleId, limit), options);
}

export function listDataPackages(directory?: string | null, options: ApiRequestOptions = {}) {
  return getJson<DataPackageListResponse>(packagingListEndpoint(directory), options);
}

export function createDataPackage(request: DataPackageCreateRequest, options: ApiRequestOptions = {}) {
  return postJson<DataPackageResult>(PACKAGING_API_ENDPOINTS.create, request, options);
}

export function validateDataPackage(request: DataPackageValidateRequest, options: ApiRequestOptions = {}) {
  return postJson<DataPackageValidationResponse>(PACKAGING_API_ENDPOINTS.validate, request, options);
}

export function getDataPackageContents(packagePath: string, options: ApiRequestOptions = {}) {
  return getJson<DataPackageContentsResponse>(packagingContentsEndpoint(packagePath), options);
}
