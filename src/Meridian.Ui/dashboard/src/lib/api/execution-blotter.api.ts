/**
 * Client functions for the broker-side execution reads and the upsize action.
 *
 * Thin wrappers over the shared workstation API client so errors, aborts, and
 * no-host fixture semantics stay aligned with the rest of the dashboard. Each of
 * these endpoints answers 503 when its execution service is not registered, which
 * the caller must distinguish from an empty book.
 */

import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { EXECUTION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type {
  ExecutionAccountSnapshot,
  ExecutionBlotterSnapshot,
  ExecutionGatewayHealth,
  ExecutionPositionActionRequest,
  TradingActionResult
} from "@/types/execution-blotter.types";

export function getExecutionGatewayHealth(options: ApiRequestOptions = {}): Promise<ExecutionGatewayHealth> {
  return apiGetJson<ExecutionGatewayHealth>(EXECUTION_API_ENDPOINTS.health, options);
}

export function getExecutionAccountSnapshot(options: ApiRequestOptions = {}): Promise<ExecutionAccountSnapshot> {
  return apiGetJson<ExecutionAccountSnapshot>(EXECUTION_API_ENDPOINTS.account, options);
}

export function getExecutionBlotter(options: ApiRequestOptions = {}): Promise<ExecutionBlotterSnapshot> {
  return apiGetJson<ExecutionBlotterSnapshot>(EXECUTION_API_ENDPOINTS.positionsBlotter, options);
}

export function upsizeExecutionPosition(
  request: ExecutionPositionActionRequest,
  options: ApiRequestOptions = {}
): Promise<TradingActionResult> {
  return apiPostJson<TradingActionResult>(EXECUTION_API_ENDPOINTS.positionsActionUpsize, request, options);
}
