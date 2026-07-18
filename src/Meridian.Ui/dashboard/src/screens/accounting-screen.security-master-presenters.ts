import type { ReferenceDataEndpointProbeResult } from "@/lib/api";
import { toDomId } from "./accounting-screen.formatting";
import type {
  SecurityConflictActionViewModel,
  SecurityConflictResolution,
  SecurityIdentityAliasRowViewModel,
  SecurityIdentityDrillInViewState,
  SecurityIdentityIdentifierRowViewModel,
  ReferenceDataEndpointRowViewModel,
  SecurityScheduleDetailFieldViewModel,
} from "./accounting-screen.view-model";
import type { SecurityAliasEntry, SecurityIdentifierEntry, SecurityMasterConflict } from "@/types";

interface ReferenceDataRoutePostureCounts {
  totalCount: number;
  readyCount: number;
  reviewCount: number;
  deferredCount: number;
  blockedCount: number;
  deferredOrBlockedCount: number;
}

export function summarizeReferenceDataRoutes(endpoints: ReferenceDataEndpointProbeResult[]): ReferenceDataRoutePostureCounts {
  return endpoints.reduce<ReferenceDataRoutePostureCounts>((counts, endpoint) => {
    counts.totalCount += 1;
    if (endpoint.status === "Ready") {
      counts.readyCount += 1;
    } else if (endpoint.status === "Deferred") {
      counts.deferredCount += 1;
      counts.deferredOrBlockedCount += 1;
    } else if (endpoint.status === "Blocked") {
      counts.blockedCount += 1;
      counts.deferredOrBlockedCount += 1;
    } else {
      // Empty, Missing, and Error all require operator review. Treat unknown
      // runtime statuses the same way so the visible buckets always reconcile.
      counts.reviewCount += 1;
    }
    return counts;
  }, {
    totalCount: 0,
    readyCount: 0,
    reviewCount: 0,
    deferredCount: 0,
    blockedCount: 0,
    deferredOrBlockedCount: 0
  });
}

export function buildSecurityConflictAction(
  conflict: SecurityMasterConflict,
  resolution: SecurityConflictResolution,
  label: string,
  enabled: boolean,
  variant: "outline" | "ghost",
  disabledReason: string | null
): SecurityConflictActionViewModel {
  const choice =
    resolution === "AcceptA"
      ? `${conflict.providerA} value ${formatSecurityReferenceValue(conflict.valueA)}`
      : resolution === "AcceptB"
        ? `${conflict.providerB} value ${formatSecurityReferenceValue(conflict.valueB)}`
        : "no provider value";
  const baseAriaLabel = resolution === "Dismiss"
    ? `Dismiss identifier conflict ${conflict.conflictId} on ${conflict.fieldPath}`
    : `Resolve identifier conflict ${conflict.conflictId} on ${conflict.fieldPath} with ${choice}`;

  return {
    resolution,
    label,
    ariaLabel: enabled || !disabledReason ? baseAriaLabel : `${baseAriaLabel}. Disabled: ${disabledReason}`,
    variant,
    disabled: !enabled,
    disabledReason: enabled ? null : disabledReason
  };
}

export function buildSecurityIdentityIdentifierRow(
  identifier: SecurityIdentifierEntry
): SecurityIdentityIdentifierRowViewModel {
  const providerLabel = presentSecurityProvider(identifier.provider);
  const primaryLabel = identifier.isPrimary ? "Primary" : "Secondary";
  const validRangeLabel = formatSecurityDateRange(identifier.validFrom, identifier.validTo);

  return {
    ...identifier,
    rowId: `identifier-${toDomId(`${identifier.kind}-${identifier.value}`)}`,
    providerLabel,
    primaryLabel,
    primaryBadgeVariant: identifier.isPrimary ? "success" : "outline",
    validRangeLabel,
    ariaLabel: `${identifier.kind} ${identifier.value}, ${primaryLabel}, provider ${providerLabel}, valid ${validRangeLabel}`
  };
}

export function buildSecurityIdentityAliasRow(alias: SecurityAliasEntry): SecurityIdentityAliasRowViewModel {
  const providerLabel = presentSecurityProvider(alias.provider);
  const enabledLabel = alias.isEnabled ? "Enabled" : "Disabled";
  const validRangeLabel = formatSecurityDateRange(alias.validFrom, alias.validTo);

  return {
    ...alias,
    rowId: `alias-${toDomId(alias.aliasId)}`,
    providerLabel,
    enabledLabel,
    enabledBadgeVariant: alias.isEnabled ? "success" : "warning",
    validRangeLabel,
    createdLabel: formatSecurityDate(alias.createdAt),
    reasonText: alias.reason?.trim() || "No alias reason recorded.",
    ariaLabel: `${alias.aliasKind} ${alias.aliasValue}, ${enabledLabel}, scope ${alias.scope}, provider ${providerLabel}, valid ${validRangeLabel}`
  };
}

export function statusBadgeVariantForSecurityIdentity(
  status: string | null | undefined
): SecurityIdentityDrillInViewState["statusBadgeVariant"] {
  const normalized = status?.trim().toLowerCase();
  if (normalized === "active") {
    return "success";
  }

  if (normalized === "pending" || normalized === "inactive" || normalized === "deactivated") {
    return "warning";
  }

  return "outline";
}

export function formatSecurityReferenceValue(value: string): string {
  return value.length > 8 ? `${value.substring(0, 8)}...` : value;
}

export function formatSecurityDate(value: string | null | undefined): string {
  if (!value) {
    return "—";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric", timeZone: "UTC" });
}

export function formatSecurityDateRange(from: string | null | undefined, to: string | null | undefined): string {
  return `${formatSecurityDate(from)} – ${to ? formatSecurityDate(to) : "active"}`;
}

function presentSecurityProvider(value: string | null | undefined): string {
  const provider = value?.trim();
  if (!provider) {
    return "—";
  }

  return /fixture|screenshot/i.test(provider) ? "Meridian demo data" : provider;
}

export function formatConflictDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric", timeZone: "UTC" });
}

export function formatSecurityConflictField(value: string): string {
  const segments = value.split(".").filter(Boolean);
  const fieldName = segments.at(-1) ?? value;
  if (segments[0]?.toLowerCase() === "identifiers") {
    return `${fieldName.toUpperCase()} identifier`;
  }

  const words = fieldName
    .replace(/[_-]+/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .trim()
    .toLowerCase();
  return words ? `${words.charAt(0).toUpperCase()}${words.slice(1)}` : "Identifier conflict";
}

export function formatFinanceFacingSourceSummary(summary: string, errorSummary?: string | null): string {
  const source = errorSummary?.trim() || summary.trim();
  const normalized = source
    .replace(/\b(GET|POST|PUT|PATCH|DELETE)\b\s+/gi, "")
    .replace(/\bendpoint\b/gi, "source")
    .replace(/\bpayload\b/gi, "record set")
    .replace(/\bDTO\b/g, "record")
    .replace(/\bbackend\b/gi, "service")
    .replace(/\s+/g, " ")
    .trim();

  return normalized || "Reference data source status is available for review.";
}

export function referenceDataStatusLabel(status: ReferenceDataEndpointProbeResult["status"]): string {
  const labels: Record<ReferenceDataEndpointProbeResult["status"], string> = {
    Ready: "Ready",
    Empty: "Empty",
    Missing: "Missing",
    Blocked: "Blocked",
    Error: "Error",
    Deferred: "Deferred"
  };

  return labels[status];
}

export function referenceDataStatusBadgeVariant(
  status: ReferenceDataEndpointProbeResult["status"]
): ReferenceDataEndpointRowViewModel["statusBadgeVariant"] {
  if (status === "Ready") {
    return "success";
  }

  if (status === "Error" || status === "Blocked") {
    return "danger";
  }

  if (status === "Empty" || status === "Missing") {
    return "warning";
  }

  return "outline";
}

export function referenceDataStatusTone(status: ReferenceDataEndpointProbeResult["status"]): SecurityScheduleDetailFieldViewModel["tone"] {
  if (status === "Ready") {
    return "success";
  }

  if (status === "Error" || status === "Blocked") {
    return "danger";
  }

  if (status === "Empty" || status === "Missing" || status === "Deferred") {
    return "warning";
  }

  return "default";
}
