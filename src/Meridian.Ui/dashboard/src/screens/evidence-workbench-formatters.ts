import type { EvidenceVaultDocumentEntry } from "@/types";

export function formatEvidenceNodeLabel(nodeId: string) {
  const parts = nodeId.split(":").filter(Boolean);
  if (parts.length <= 1) {
    return formatKind(nodeId);
  }

  return `${formatKind(parts[0])} ${formatKind(parts[parts.length - 1])}`;
}

export function formatKind(kind: string) {
  return kind
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .split(/[-_\s]+/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}

export function formatEvidenceDocumentAuthority(
  authority: EvidenceVaultDocumentEntry["document"]["authority"] | null | undefined
) {
  if (!authority) {
    return "Support, block, suggest, and link only";
  }

  const allowed = [
    authority.canSupport ? "support" : null,
    authority.canBlock ? "block" : null,
    authority.canSuggest ? "suggest" : null,
    authority.canLink ? "link" : null
  ].filter((value): value is string => Boolean(value));
  const prohibited = [
    authority.canApprove ? null : "approve",
    authority.canPost ? null : "post",
    authority.canCertify ? null : "certify",
    authority.canRelease ? null : "release"
  ].filter((value): value is string => Boolean(value));

  if (allowed.length === 0 && prohibited.length === 0) {
    return authority.boundary;
  }

  const allowedLabel = allowed.length === 0 ? "no authority actions" : allowed.join(", ");
  const prohibitedLabel = prohibited.length === 0 ? "none" : prohibited.join(", ");
  return `${allowedLabel}; cannot ${prohibitedLabel}`;
}

export function formatPageTag(value: string) {
  const tag = normalizePageTagForDisplay(value);
  return tag.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function normalizePageTagForDisplay(value: string) {
  const trimmed = value.trim();
  const separatorIndex = trimmed.indexOf(":");
  if (separatorIndex < 0) {
    return trimmed;
  }

  const prefix = trimmed.slice(0, separatorIndex).trim();
  return prefix.toLowerCase() === "evidenceworkbench" ? "EvidenceWorkbench" : trimmed;
}

export function formatRelationship(value: string) {
  return value
    .replace(/[-_]+/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .trim()
    .replace(/\s+/g, " ")
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

export function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return `${UTC_MONTH_LABELS[date.getUTCMonth()]} ${date.getUTCDate()}, ${padUtc(date.getUTCHours())}:${padUtc(date.getUTCMinutes())} UTC`;
}

const UTC_MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function padUtc(value: number) {
  return value.toString().padStart(2, "0");
}
