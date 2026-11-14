import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import { deleteJson } from "@/lib/api/http-client";

export interface FailoverRule {
  id: string;
  primaryProviderId: string;
  backupProviderIds: string[];
  failoverThreshold: number;
  recoveryThreshold: number;
  dataQualityThreshold: number;
  maxLatencyMs: number;
  isInFailoverState: boolean;
  currentActiveProviderId: string | null;
}

export interface FailoverSettings {
  enableFailover: boolean;
  healthCheckIntervalSeconds: number;
  autoRecover: boolean;
  failoverTimeoutSeconds: number;
  rules: FailoverRule[];
  runtimeAvailable: boolean;
}

export type FailoverRuleDraft = Omit<FailoverRule, "isInFailoverState" | "currentActiveProviderId"> & { reason?: string };

export const failoverApi = {
  get: (options: ApiRequestOptions = {}) => apiGetJson<FailoverSettings>(
    `${UI_API_ROUTES.FailoverConfig}?configurationOnly=true`, { ...options, allowDevelopmentFallback: false }),
  saveSettings: (settings: Omit<FailoverSettings, "rules" | "runtimeAvailable">, options: ApiRequestOptions = {}) =>
    apiPostJson(UI_API_ROUTES.FailoverConfig, settings, options),
  saveRule: (rule: FailoverRuleDraft, options: ApiRequestOptions = {}) =>
    apiPostJson<{ id: string }>(UI_API_ROUTES.FailoverRules, rule, options),
  deleteRule: (ruleId: string, reason: string, options: ApiRequestOptions = {}) =>
    deleteJson(`${UI_API_ROUTES.FailoverRules}/${encodeURIComponent(ruleId)}`, options, { reason }),
  force: (ruleId: string, targetProviderId: string, options: ApiRequestOptions = {}) =>
    apiPostJson<{ success: boolean; currentActiveProviderId: string }>(
      UI_API_ROUTES.FailoverForce.replace("{ruleId}", encodeURIComponent(ruleId)), { targetProviderId }, options),
  reset: (ruleId: string, options: ApiRequestOptions = {}) =>
    apiPostJson<{ success: boolean; currentActiveProviderId: string }>(
      UI_API_ROUTES.ProviderFailoverReset, { ruleId }, options)
};
