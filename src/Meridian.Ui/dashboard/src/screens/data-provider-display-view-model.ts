import type {
  ProviderConnectionRow,
  ProviderReadinessStatus,
  ProviderReadinessSummary,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot
} from "@/types";

export function joinProviderDetailSentences(...values: Array<string | null | undefined>): string {
  const sentences = values
    .map((value) => value?.trim().replace(/[.\s]+$/g, "") ?? "")
    .filter(Boolean);
  return sentences.length > 0 ? `${sentences.join(". ")}.` : "No provider detail is available.";
}

const PROVIDER_REASON_ACRONYMS = new Set(["api", "dk", "id", "ok", "sla"]);

export function formatProviderReasonLabel(reasonCode: string): string {
  const trimmed = reasonCode.trim();
  if (trimmed.length === 0) {
    return "Reason not reported";
  }

  const isEnumCode = /^[A-Z0-9]+(?:[_-][A-Z0-9]+)+$/.test(trimmed);
  if (!isEnumCode) {
    return trimmed;
  }

  return trimmed
    .split(/[_-]+/)
    .filter(Boolean)
    .map((token, index) => {
      const lower = token.toLowerCase();
      if (PROVIDER_REASON_ACRONYMS.has(lower)) {
        return lower.toUpperCase();
      }

      return index === 0 ? `${lower.charAt(0).toUpperCase()}${lower.slice(1)}` : lower;
    })
    .join(" ");
}

export function buildProviderReadinessSummaryText(
  readiness: ProviderReadinessSummary | null | undefined,
  counts: {
    rowCount: number;
    readyCount: number;
    reviewCount: number;
    degradedCount: number;
    blockedCount: number;
  }
): string {
  const displayedCounts = [
    `${counts.readyCount} ready`,
    `${counts.reviewCount} review`,
    `${counts.degradedCount} degraded`,
    `${counts.blockedCount} blocked`
  ].join(" / ");

  if (!readiness) {
    return counts.rowCount > 0
      ? `Displayed posture: ${displayedCounts}. Provider readiness is assembled from connection, routing, and workspace evidence while the shared readiness summary is unavailable.`
      : "Provider readiness will appear after the shared provider setup, validation, degradation, and evidence data loads.";
  }

  const coverage = readiness.totalProviders === counts.rowCount
    ? ""
    : ` Shared readiness covers ${readiness.totalProviders} of ${counts.rowCount} displayed providers.`;
  return `${readiness.summary} Displayed posture: ${displayedCounts}.${coverage} Next action: ${readiness.recommendedAction}`;
}

export function normalizeProviderTrustScore(score: number): number {
  const percentage = score >= 0 && score <= 1 ? score * 100 : score;
  return Math.round(Math.min(100, Math.max(0, percentage)));
}

export function providerCredentialLabel(value: ProviderConnectionRow["credentialState"]): string {
  switch (value) {
    case "NotRequired":
      return "Not required";
    default:
      return splitProviderSetupPascalCase(value);
  }
}

export function diagnosticStatusFromReadiness(status: ProviderReadinessStatus): "pass" | "fail" | "warning" | "pending" {
  switch (status) {
    case "Ready":
      return "pass";
    case "Blocked":
      return "fail";
    case "Degraded":
      return "warning";
    default:
      return "pending";
  }
}

export function providerReadinessStatusLabel(status: ProviderReadinessStatus): string {
  switch (status) {
    case "Ready":
      return "Ready";
    case "Review":
      return "Review";
    case "Degraded":
      return "Degraded";
    case "Blocked":
      return "Blocked";
    default:
      return "Unknown";
  }
}

export function providerVerificationLabel(value: ProviderConnectionRow["verificationState"]): string {
  switch (value) {
    case "NotRequired":
      return "Not required";
    case "NotVerified":
      return "Not verified";
    default:
      return splitProviderSetupPascalCase(value);
  }
}

export function providerCredentialSourceLabel(value: ProviderConnectionRow["credentialSource"]): string {
  switch (value) {
    case "LocalEncryptedStore":
      return "Encrypted local store";
    case "ExternalVaultReference":
      return "External vault reference";
    case "NotRequired":
      return "Not required";
    default:
      return splitProviderSetupPascalCase(value);
  }
}

export function providerRoutingVerificationLabel(
  connection: ProviderRoutingConnection | null,
  trust: ProviderRoutingTrustSnapshot | null
): string {
  if (trust?.isCertificationFresh) {
    return "Certified";
  }

  if (connection?.productionReady) {
    return "Production ready";
  }

  return connection ? "Certification pending" : "Not reported";
}

export function providerRoutingRecommendedAction(
  connection: ProviderRoutingConnection,
  trust: ProviderRoutingTrustSnapshot | null,
  bindings: ProviderRoutingBinding[],
  enabled: boolean
): string {
  if (!enabled) {
    return "Enable the routing connection before selecting it for provider workflows.";
  }

  if (bindings.length === 0) {
    return "Add a provider-routing binding before selecting this connection.";
  }

  if (!connection.productionReady) {
    return "Run provider certification before production routing.";
  }

  if (trust && !trust.isHealthy) {
    return "Inspect provider health before routing new workflow traffic.";
  }

  return "Provider routing is ready for supported capabilities.";
}

export function providerGateImpactText(
  connection: ProviderRoutingConnection | null,
  trust: ProviderRoutingTrustSnapshot | null
): string {
  if (!connection) {
    return "No routing gate loaded";
  }

  if (!connection.enabled) {
    return "Disabled for routing";
  }

  if (!connection.productionReady) {
    return "Certification needed";
  }

  if (trust && !trust.isHealthy) {
    return "Health gate needs review";
  }

  return "No gate impact reported";
}

export function providerRoutingFallbackLabel(bindings: ProviderRoutingBinding[]): string {
  const count = bindings.reduce((total, binding) => total + binding.failoverConnectionIds.length, 0);
  return count > 0 ? `${count} backup route${count === 1 ? "" : "s"}` : "No backup source active";
}

export function providerRoutingEnvironmentLabel(connection: ProviderRoutingConnection | null): string {
  if (!connection) {
    return "Not set";
  }

  if (connection.connectionMode.toLowerCase().includes("paper")) {
    return "PAPER";
  }

  if (connection.connectionMode.toLowerCase().includes("live")) {
    return "LIVE";
  }

  return connection.connectionMode;
}

export function formatProviderRoutingCapability(value: string): string {
  switch (value) {
    case "RealtimeMarketData":
      return "Live quotes";
    case "HistoricalBars":
      return "Historical backfill";
    case "ReferenceData":
      return "Reference data";
    case "BrokerageOrders":
      return "Brokerage/order routing";
    case "PortfolioSync":
      return "Portfolio sync";
    case "ReportingExport":
      return "Reporting exports";
    default:
      return splitProviderSetupPascalCase(value);
  }
}

export function formatLastGoodProviderResponse(connection: ProviderConnectionRow | null): string {
  return formatProviderUtcMinute(connection?.lastSuccessfulAt ?? connection?.lastVerifiedAt);
}

export function formatProviderUtcMinute(value: string | null | undefined): string {
  if (!value) {
    return "Never";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    timeZone: "UTC",
    timeZoneName: "short"
  }).format(date);
}

export function normalizeProviderToken(value: string | null | undefined): string {
  return (value ?? "").trim().toLowerCase().replace(/[^a-z0-9]/g, "");
}

export function uniqueStrings(values: string[]): string[] {
  return values.filter((value, index) => values.indexOf(value) === index);
}

export function findField(fields: Array<{ id: string; value: string }>, id: string): string | null {
  return fields.find((field) => field.id === id)?.value ?? null;
}

export function credentialToneFromLabel(value: string): "default" | "warning" | "success" | "danger" {
  return value === "Verified" || value === "Not required"
    ? "success"
    : value === "Missing" || value === "Partial" || value === "Invalid"
      ? "danger"
      : "warning";
}

export function verificationToneFromLabel(value: string): "default" | "warning" | "success" | "danger" {
  return value === "Verified" || value === "Not required"
    ? "success"
    : value === "Failed"
      ? "danger"
      : "warning";
}

export function splitProviderSetupPascalCase(value: string): string {
  return value
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/_/g, " ")
    .trim() || value;
}
