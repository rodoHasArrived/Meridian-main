/**
 * Append-only "actor unverified" flags for provider-integration records stored before the server bound
 * actors to the signed-in operator. Mirrors ProviderIntegrationActorFlagDto in
 * Meridian.Contracts/Integrations/ProviderIntegrationActorFlags.cs. Flags never change the flagged record.
 */

import { getJson, postJson, type ApiRequestOptions } from "@/lib/api/http-client";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";

export interface ProviderIntegrationActorFlag {
  flagId: string;
  recordPath: string;
  recordKind: string;
  subjectId?: string | null;
  fieldPath: string;
  recordedValue: string;
  reason: string;
  recordedAt?: string | null;
  flaggedBy: string;
  flaggedAt: string;
}

export interface ProviderIntegrationActorFlagScanResult {
  actorFieldsScanned: number;
  flagsAppended: number;
  alreadyFlagged: number;
  flags: ProviderIntegrationActorFlag[];
}

export function listProviderIntegrationActorFlags(options: ApiRequestOptions = {}): Promise<ProviderIntegrationActorFlag[]> {
  return getJson<ProviderIntegrationActorFlag[]>(UI_API_ROUTES.WorkstationProviderIntegrationActorFlags, options);
}

/** Scans this tenant's integration records and appends a flag for each unverified actor not yet flagged. */
export function scanProviderIntegrationActorFlags(
  extraLabels: readonly string[] = [],
  options: ApiRequestOptions = {}
): Promise<ProviderIntegrationActorFlagScanResult> {
  return postJson<ProviderIntegrationActorFlagScanResult>(
    UI_API_ROUTES.WorkstationProviderIntegrationActorFlagsScan,
    { extraLabels: extraLabels.length > 0 ? [...extraLabels] : null },
    options
  );
}
