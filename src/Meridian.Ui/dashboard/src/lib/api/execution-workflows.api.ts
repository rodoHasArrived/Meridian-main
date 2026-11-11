import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import type { OrderSubmitRequest } from "@/types";

export type SessionIntent = "RegularOnly" | "QueueNextSession" | "ExtendedHours";
export interface ServerOrderPreview {
  fingerprint: string; evaluatedAt: string; expiresAt: string; canSubmit: boolean; requiresApproval: boolean;
  estimatedNotional?: number | null; estimatedFees?: number | null;
  reference?: { price: number; observedAt: string; source: string } | null;
  checks: Array<{ code: string; status: string; message: string }>;
}
export interface JournalEvent {
  eventId: string; kind: string; recordedAt: string; detail?: string;
  report?: { gatewayOrderId?: string; replacesOrderId?: string; filledQuantity: number; fillPrice?: number; commission?: number; fees?: number; timestamp: string };
  activityFill?: { fillId: string; quantity: number; price: number; commission?: number; filledAt: string };
}
export interface JournalOrder {
  orderId: string; scope: string; accountId: string; symbol: string; side: string; status: string;
  quantity: number; filledQuantity: number; strategyId?: string; runId?: string; tag?: string;
  createdAt: string; updatedAt: string; events: JournalEvent[];
}
export interface JournalSummary {
  orders: number; fills: number; realizedPnl?: number | null; commissions?: number | null;
  fees?: number | null; slippage?: number | null; coverage: string; method: string; netRealizedPnl?: number | null;
  attributions?: Array<{ strategyId?: string; runId?: string; tag?: string; grossRealizedPnl: number; netRealizedPnl?: number | null }>;
}
export interface ExecutionNotification {
  id: string; accountId: string; scope: string; severity: "critical" | "warning" | "info" | "success";
  title: string; detail: string; timestamp: string; orderId?: string | null;
}
export interface ExecutionAlertSettings {
  accountId: string; dailyLossLimitUsd?: number | null; staleOrderMinutes: number;
  webhookEnabled: boolean; webhookUrl?: string | null; version: number;
}

export function previewExecutionOrder(request: OrderSubmitRequest, signal?: AbortSignal): Promise<ServerOrderPreview> {
  return apiPostJson<ServerOrderPreview>("/api/execution/orders/preview", {
    ...request, type: request.type === "Stop" ? "StopMarket" : request.type,
    ...(request.type === "Stop" ? { stopPrice: request.limitPrice, limitPrice: null } : {})
  }, { signal, allowDevelopmentFallback: false });
}
export function getJournal(accountId: string, filters: { symbol?: string; strategy?: string; tag?: string; from?: string; to?: string; status?: string } = {}, options: ApiRequestOptions = {}) {
  const query = new URLSearchParams({ accountId });
  Object.entries(filters).forEach(([key, value]) => { if (value) query.set(key, value); });
  return apiGetJson<JournalOrder[]>(`/api/execution/journal/orders?${query}`, { ...options, allowDevelopmentFallback: false });
}
export function getJournalOrder(accountId: string, orderId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<JournalOrder>(`/api/execution/journal/orders/${encodeURIComponent(orderId)}?accountId=${encodeURIComponent(accountId)}`, { ...options, allowDevelopmentFallback: false });
}
export function getJournalSummary(accountId: string, options: ApiRequestOptions = {}, window: { from?: string; to?: string } = {}) {
  const query = new URLSearchParams({ accountId });
  Object.entries(window).forEach(([key, value]) => { if (value) query.set(key, value); });
  return apiGetJson<JournalSummary>(`/api/execution/journal/summary?${query}`, { ...options, allowDevelopmentFallback: false });
}
export function getExecutionNotifications(accountId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<ExecutionNotification[]>(`/api/execution/notifications?accountId=${encodeURIComponent(accountId)}`, { ...options, allowDevelopmentFallback: false });
}
export function getExecutionAlertSettings(accountId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<ExecutionAlertSettings>(`/api/execution/alert-settings?accountId=${encodeURIComponent(accountId)}`, { ...options, allowDevelopmentFallback: false });
}
export function saveExecutionAlertSettings(settings: ExecutionAlertSettings) {
  return apiPostJson<void>("/api/execution/alert-settings", settings, { allowDevelopmentFallback: false });
}
