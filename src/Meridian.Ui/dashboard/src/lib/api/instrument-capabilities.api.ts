import { apiGetJson } from "@/lib/api/http-client";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { InstrumentCapabilityBatch } from "@/types";

export function getInstrumentCapabilities(securityIds: string[], signal?: AbortSignal): Promise<InstrumentCapabilityBatch> {
  if (securityIds.length === 0 || securityIds.length > 200) throw new Error("Select between 1 and 200 securities.");
  const query = new URLSearchParams();
  for (const id of securityIds) query.append("securityIds", id);
  return apiGetJson<InstrumentCapabilityBatch>(`${UI_API_ROUTES.SecurityMasterCapabilities}?${query}`, { signal, allowDevelopmentFallback: false });
}
