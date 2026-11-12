import { apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { workstationProviderIntegrationIdentityResolutionEndpoint } from "@/lib/workstation-endpoints";
import type {
  ProviderIntegrationIdentityDecision,
  ProviderIntegrationIdentityResolutionRequest
} from "@/types/provider-integration-identity";

export function resolveProviderIntegrationIdentity(
  connectionId: string,
  request: ProviderIntegrationIdentityResolutionRequest,
  options: ApiRequestOptions = {}
) {
  return apiPostJson<ProviderIntegrationIdentityDecision>(
    workstationProviderIntegrationIdentityResolutionEndpoint(connectionId), request, options
  );
}
