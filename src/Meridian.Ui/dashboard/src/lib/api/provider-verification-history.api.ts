import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

export interface ProviderVerificationHistory {
  connectionId: string;
  providerId: string;
  asOfUtc: string;
  observations: { recordedAtUtc: string; succeeded: boolean }[];
  isTruncated: boolean;
}

export async function getProviderVerificationHistory(connectionId: string, options: ApiRequestOptions = {}) {
  const response = await apiGetJson<ProviderVerificationHistory>(
    UI_API_ROUTES.WorkstationProviderVerificationHistory.replace("{connectionId}", encodeURIComponent(connectionId)),
    { ...options, allowDevelopmentFallback: false });
  if (response.connectionId !== connectionId || !Array.isArray(response.observations)
      || !Number.isFinite(Date.parse(response.asOfUtc)) || typeof response.isTruncated !== "boolean"
      || response.observations.some((entry) => !Number.isFinite(Date.parse(entry.recordedAtUtc)) || typeof entry.succeeded !== "boolean")) {
    throw new Error("Retained verification history does not match the selected connection or has incomplete evidence.");
  }
  return response;
}
