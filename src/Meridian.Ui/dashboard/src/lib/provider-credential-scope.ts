import type { ApiRequestOptions } from "@/lib/api";
import type { ProviderRoutingConnection } from "@/types";

export interface ProviderCredentialRequestOptions extends ApiRequestOptions { connectionId?: string }

/** Legacy callers omit scope; migrated editors must pass an explicit, nonempty connection. */
export function providerConnectionScopeEndpoint(path: string, connectionId?: string): string {
  if (connectionId === undefined) return path;
  if (!connectionId.trim()) throw new Error("An owned connection must be selected.");
  return `${path}${path.includes("?") ? "&" : "?"}connectionId=${encodeURIComponent(connectionId)}`;
}

export function ownedCredentialConnections(rows: ProviderRoutingConnection[], providerId: string): ProviderRoutingConnection[] {
  const counts = new Map<string, number>();
  for (const row of rows) {
    const id = row.connectionId?.toLowerCase();
    if (id) counts.set(id, (counts.get(id) ?? 0) + 1);
  }
  return rows.filter(row => row.connectionId?.trim() && counts.get(row.connectionId.toLowerCase()) === 1 &&
    row.providerFamilyId?.toLowerCase() === providerId.toLowerCase() && row.enabled &&
    row.tenantId?.trim() && row.externalAccountId?.trim() && row.credentialEnvironment?.trim());
}

export function credentialOwnershipKey(row: ProviderRoutingConnection): string {
  return JSON.stringify([row.providerFamilyId, row.connectionId, row.tenantId, row.externalAccountId, row.credentialEnvironment]);
}
