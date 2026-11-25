import { getJson, type ApiRequestOptions } from "@/lib/api/http-client";
import { executionCancelIncidentEndpoint, executionCancelIncidentsEndpoint } from "@/lib/workstation-endpoints";
import type { CancelAllIncident, CancelAllIncidentHistory } from "@/types/cancel-incidents.types";

export function getCancelAllIncidents(fundAccountId: string, options: ApiRequestOptions = {}) {
  return getJson<CancelAllIncidentHistory>(executionCancelIncidentsEndpoint(fundAccountId),
    { ...options, allowDevelopmentFallback: false });
}

export function getCancelAllIncident(incidentId: string, fundAccountId: string, options: ApiRequestOptions = {}) {
  return getJson<CancelAllIncident>(executionCancelIncidentEndpoint(incidentId, fundAccountId),
    { ...options, allowDevelopmentFallback: false });
}
