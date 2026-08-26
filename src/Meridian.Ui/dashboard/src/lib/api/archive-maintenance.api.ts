/**
 * Client functions for the archive-maintenance operations endpoints.
 *
 * Thin wrappers over the shared workstation API client so errors, aborts, and
 * no-host fixture semantics stay aligned with the rest of the dashboard. These
 * cover the run side of maintenance — service status, statistics, execution
 * history, and manual runs. Schedule CRUD lives in `@/lib/api`.
 */

import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import {
  MAINTENANCE_API_ENDPOINTS,
  maintenanceExecutionCancelEndpoint,
  maintenanceExecutionsEndpoint,
  maintenanceFailedExecutionsEndpoint,
  maintenanceStatisticsEndpoint
} from "@/lib/workstation-endpoints";
import type {
  ExecuteMaintenanceRequest,
  MaintenanceExecution,
  MaintenanceScheduleSummary,
  MaintenanceServiceStatus,
  MaintenanceStatistics,
  MaintenanceTaskTypeOption
} from "@/types/archive-maintenance.types";

export function getMaintenanceServiceStatus(options: ApiRequestOptions = {}): Promise<MaintenanceServiceStatus> {
  return apiGetJson<MaintenanceServiceStatus>(MAINTENANCE_API_ENDPOINTS.status, options);
}

export function getMaintenanceStatistics(
  hours?: number,
  options: ApiRequestOptions = {}
): Promise<MaintenanceStatistics> {
  return apiGetJson<MaintenanceStatistics>(maintenanceStatisticsEndpoint(hours), options);
}

export function getMaintenanceScheduleSummary(
  options: ApiRequestOptions = {}
): Promise<MaintenanceScheduleSummary> {
  return apiGetJson<MaintenanceScheduleSummary>(MAINTENANCE_API_ENDPOINTS.scheduleSummary, options);
}

export function getMaintenanceTaskTypes(options: ApiRequestOptions = {}): Promise<MaintenanceTaskTypeOption[]> {
  return apiGetJson<MaintenanceTaskTypeOption[]>(MAINTENANCE_API_ENDPOINTS.taskTypes, options);
}

export function getMaintenanceExecutions(
  limit?: number,
  options: ApiRequestOptions = {}
): Promise<MaintenanceExecution[]> {
  return apiGetJson<MaintenanceExecution[]>(maintenanceExecutionsEndpoint(limit), options);
}

export function getFailedMaintenanceExecutions(
  limit?: number,
  options: ApiRequestOptions = {}
): Promise<MaintenanceExecution[]> {
  return apiGetJson<MaintenanceExecution[]>(maintenanceFailedExecutionsEndpoint(limit), options);
}

export function executeMaintenanceTask(
  request: ExecuteMaintenanceRequest,
  options: ApiRequestOptions = {}
): Promise<MaintenanceExecution> {
  return apiPostJson<MaintenanceExecution>(MAINTENANCE_API_ENDPOINTS.execute, request, options);
}

export function cancelMaintenanceExecution(
  executionId: string,
  options: ApiRequestOptions = {}
): Promise<{ message: string }> {
  return apiPostJson<{ message: string }>(maintenanceExecutionCancelEndpoint(executionId), undefined, options);
}
