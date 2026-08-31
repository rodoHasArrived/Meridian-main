import type { ProviderIntegrationActivationReadiness, ProviderIntegrationCapabilityKind } from "@/types";

export function providerIntegrationReadinessDetails(
  readiness: ProviderIntegrationActivationReadiness | null
): string[] {
  if (!readiness) {
    return [];
  }

  return [
    ...readiness.requiredEvidence.map((evidence) => `Evidence required: ${evidence}`),
    ...readiness.issues.map((issue) => `${issue.severity}: ${issue.message}`)
  ];
}

export function providerIntegrationFormatJson(value: unknown): string {
  return JSON.stringify(value, null, 2);
}

export function parseProviderIntegrationWorkbenchJson<T>(
  value: string,
  label: string
): { ok: true; value: T } | { ok: false; error: string } {
  try {
    return { ok: true, value: JSON.parse(value) as T };
  } catch (error) {
    return { ok: false, error: `${label}: ${error instanceof Error ? error.message : "Invalid JSON"}` };
  }
}

export function parseProviderIntegrationStringRecord(
  value: string,
  label: string
): { ok: true; value: Record<string, string> } | { ok: false; error: string } {
  const parsed = parseProviderIntegrationWorkbenchJson<unknown>(value || "{}", label);
  if (parsed.ok === false) {
    return { ok: false, error: parsed.error };
  }
  if (!parsed.value || typeof parsed.value !== "object" || Array.isArray(parsed.value)) {
    return { ok: false, error: `${label}: expected a JSON object.` };
  }

  return {
    ok: true,
    value: Object.fromEntries(Object.entries(parsed.value).map(([key, item]) => [key, String(item)]))
  };
}

export function providerIntegrationCredentialReference(
  row: { providerId: string; sourceLabel?: string | null }
): string {
  return `provider-credential:${providerIntegrationNormalizedId(row.providerId)}:${providerIntegrationNormalizedId(row.sourceLabel || "local")}`;
}

export function providerIntegrationWorkbenchSyncRunId(connectionId: string, mode: string, requestedAt: Date): string {
  return `settings-${mode}-${providerIntegrationNormalizedId(connectionId || "connection")}-${providerIntegrationTimestampSuffix(requestedAt)}`;
}

export function providerIntegrationWorkbenchEvidenceId(connectionId: string, purpose: string, requestedAt: Date): string {
  return `settings-provider-${purpose}-${providerIntegrationNormalizedId(connectionId || "connection")}-${providerIntegrationTimestampSuffix(requestedAt)}`;
}

export function providerIntegrationTimestampSuffix(value: Date): string {
  return value.toISOString().replace(/[^0-9A-Za-z]/g, "").toLowerCase();
}

export function providerIntegrationNormalizedId(value: string): string {
  return (value || "provider").replace(/[^0-9A-Za-z-]/g, "-").replace(/-+/g, "-").replace(/^-|-$/g, "").toLowerCase() || "provider";
}

export function providerIntegrationSampleCsv(capability: ProviderIntegrationCapabilityKind): string {
  if (capability === "Transactions") {
    return "transactionId,accountId,amount,currency,postedAt\ntxn-1,acct-1,125.00,USD,2026-06-01";
  }
  return "positionId,accountId,symbol,quantity,asOfDate\npos-1,acct-1,MSFT,10,2026-06-01";
}
