import { apiGetJson, apiPostJson, apiPutJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { ProviderRoutingConnection } from "@/types";
import type {
  ApplyProviderRuntimeCredentialRequest,
  BindProviderRuntimeCredentialRequest,
  CreateOwnedProviderConnectionRequest,
  ProviderCredentialMigrationReceipt,
  ProviderCredentialMigrationRequest,
  ProviderCredentialStoreStatus,
  ProviderRuntimeCredentialApplicationStatus,
  ProviderRuntimeCredentialBinding
} from "@/types/provider-ownership.types";

function connectionRoute(template: string, connectionId: string): string {
  if (!connectionId.trim()) throw new Error("An owned connection must be selected.");
  return template.replace("{connectionId}", encodeURIComponent(connectionId.trim()));
}

const strict = (options: ApiRequestOptions): ApiRequestOptions => ({ ...options, allowDevelopmentFallback: false });

/** The authenticated tenant owns the new connection; the request never names an owner or actor. */
export function createOwnedProviderConnection(request: CreateOwnedProviderConnectionRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<ProviderRoutingConnection>(UI_API_ROUTES.ProviderRoutingConnections, request, strict(options));
}

export function bindProviderRuntimeCredential(connectionId: string, request: BindProviderRuntimeCredentialRequest, options: ApiRequestOptions = {}) {
  return apiPutJson<ProviderRuntimeCredentialBinding>(
    connectionRoute(UI_API_ROUTES.ProviderRoutingConnectionRuntimeCredentials, connectionId), request, strict(options));
}

/** Non-secret preview; its auditMetadata.credentialGeneration is the token a migrate command must echo. */
export function previewProviderCredentialMigration(connectionId: string, providerId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<ProviderCredentialStoreStatus>(
    `${connectionRoute(UI_API_ROUTES.ProviderRoutingConnectionCredentialMigrationPreview, connectionId)}?providerId=${encodeURIComponent(providerId)}`,
    strict(options));
}

export function migrateProviderCredential(connectionId: string, request: ProviderCredentialMigrationRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<ProviderCredentialMigrationReceipt>(
    connectionRoute(UI_API_ROUTES.ProviderRoutingConnectionCredentialMigrate, connectionId), request, strict(options));
}

export function rollbackProviderCredential(connectionId: string, request: ProviderCredentialMigrationRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<ProviderCredentialMigrationReceipt>(
    connectionRoute(UI_API_ROUTES.ProviderRoutingConnectionCredentialRollback, connectionId), request, strict(options));
}

/** Whether the retained binding for this connection has reached running instances of the host. */
export function getProviderRuntimeCredentialStatus(connectionId: string, providerId: string, options: ApiRequestOptions = {}) {
  return apiGetJson<ProviderRuntimeCredentialApplicationStatus>(
    `${connectionRoute(UI_API_ROUTES.ProviderRoutingConnectionRuntimeCredentialStatus, connectionId)}?providerId=${encodeURIComponent(providerId)}`, strict(options));
}

/** Opt-in, never hot: asks the host to recycle running instances under the retained binding; the default host reports pending restart. */
export function applyProviderRuntimeCredential(connectionId: string, request: ApplyProviderRuntimeCredentialRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<ProviderRuntimeCredentialApplicationStatus>(
    connectionRoute(UI_API_ROUTES.ProviderRoutingConnectionRuntimeCredentialApply, connectionId), request, strict(options));
}
