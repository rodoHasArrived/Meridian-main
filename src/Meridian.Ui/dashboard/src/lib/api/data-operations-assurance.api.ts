import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import {
  WORKSTATION_API_ENDPOINTS,
  workstationIngestionOperationActionEndpoint
} from "@/lib/workstation-endpoints";
import type {
  IngestionOperationActionResult,
  IngestionOperationsSnapshot,
  StorageAssuranceSnapshot,
  StorageMaintenanceAction,
  StorageMaintenancePreview,
  StorageMaintenanceResult
} from "@/types/data-operations-assurance";

export function getIngestionOperations(options: ApiRequestOptions = {}) {
  return apiGetJson<IngestionOperationsSnapshot>(WORKSTATION_API_ENDPOINTS.ingestionOperations, options);
}

export function applyIngestionOperationAction(
  jobId: string,
  action: string,
  request: { idempotencyKey: string; rationale: string },
  options: ApiRequestOptions = {}
) {
  return apiPostJson<IngestionOperationActionResult>(
    workstationIngestionOperationActionEndpoint(jobId, action),
    request,
    options
  );
}

export function getStorageAssurance(options: ApiRequestOptions = {}) {
  return apiGetJson<StorageAssuranceSnapshot>(WORKSTATION_API_ENDPOINTS.storageAssurance, options);
}

export function previewStorageMaintenance(
  request: { action: StorageMaintenanceAction; relativePath?: string | null; targetTier?: string | null },
  options: ApiRequestOptions = {}
) {
  return apiPostJson<StorageMaintenancePreview>(WORKSTATION_API_ENDPOINTS.storageMaintenancePreview, request, options);
}

export function executeStorageMaintenance(
  request: { previewId: string; idempotencyKey: string; rationale: string; confirmationText: string },
  options: ApiRequestOptions = {}
) {
  return apiPostJson<StorageMaintenanceResult>(WORKSTATION_API_ENDPOINTS.storageMaintenanceExecute, request, options);
}
