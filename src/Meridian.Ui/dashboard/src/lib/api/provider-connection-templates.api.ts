import {
  apiGetBlob, deleteJson, getJson, postJson, postJsonDocument, putJson, type ApiRequestOptions
} from "@/lib/api/http-client";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { ProviderCredentialFieldMetadata, ProviderCredentialMutationResult } from "@/types";

export interface ProviderConnectionTemplateOption {
  template: { name: string; connectionMode: string };
  revision: string;
  isBuiltIn: boolean;
  requiresExternalAccount: boolean;
  reviewRows: { field: string; value: string }[];
  verificationChecks: string[];
}

export interface ProviderTemplateSetupDraft {
  setupId: string;
  providerKind: string;
  environment: string;
  credentialFields: ProviderCredentialFieldMetadata[];
}

export interface ProviderTemplateSetupResult { canSave: boolean; detail: string }
export interface SavedTemplateConnection { connectionId: string; displayName: string; providerKind: string }

const routes = UI_API_ROUTES;
const strict = (signal?: AbortSignal): ApiRequestOptions => ({ ...(signal ? { signal } : {}), allowDevelopmentFallback: false });
const setupRoute = (template: string, id: string) => template.replace("{setupId}", encodeURIComponent(id));

export function listConnectionTemplates(signal?: AbortSignal) {
  return getJson<ProviderConnectionTemplateOption[]>(routes.ProviderConnectionTemplates, strict(signal));
}

export function exportConnectionTemplate(name: string, signal?: AbortSignal) {
  return apiGetBlob(`${routes.ProviderConnectionTemplateExport}?${new URLSearchParams({ name })}`, strict(signal));
}

export function importConnectionTemplates(json: string, signal?: AbortSignal) {
  return postJsonDocument<{ imported: number }>(routes.ProviderConnectionTemplateImport, json, strict(signal));
}

export function saveConnectionAsTemplate(connectionId: string, name: string, signal?: AbortSignal) {
  return postJson<ProviderConnectionTemplateOption>(routes.ProviderConnectionTemplates, { connectionId, name }, strict(signal));
}

export function beginTemplateSetup(request: { setupId: string; templateName: string; revision: string; externalAccountId?: string }, signal?: AbortSignal) {
  return postJson<ProviderTemplateSetupDraft>(routes.ProviderConnectionTemplateSetups, request, strict(signal));
}

export function putTemplateSetupCredentials(draft: ProviderTemplateSetupDraft, credentials: Record<string, string>, signal?: AbortSignal) {
  const target = routes.ProviderCredentialMutation.replace("{providerId}", encodeURIComponent(draft.providerKind));
  const query = new URLSearchParams({ templateSetupId: draft.setupId });
  return putJson<ProviderCredentialMutationResult>(`${target}?${query}`, { credentials, environment: draft.environment }, strict(signal));
}

export function testTemplateSetup(setupId: string, signal?: AbortSignal) {
  return postJson<ProviderTemplateSetupResult>(setupRoute(routes.ProviderConnectionTemplateSetupTest, setupId), undefined, strict(signal));
}

export function saveTemplateSetup(setupId: string, signal?: AbortSignal) {
  return postJson<SavedTemplateConnection>(setupRoute(routes.ProviderConnectionTemplateSetup, setupId), undefined, strict(signal));
}

export function discardTemplateSetup(setupId: string, signal?: AbortSignal) {
  return deleteJson<{ discarded: boolean }>(setupRoute(routes.ProviderConnectionTemplateSetup, setupId), { ...strict(signal), keepalive: true });
}
