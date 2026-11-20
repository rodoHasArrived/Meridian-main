import type { RecordIdentity } from "@/lib/record-workspace-context";
import type { SecurityIdentityDrillIn, SecurityMasterEntry } from "@/types";

export function instrumentRecordIdentity(entry: SecurityMasterEntry | null, detail: SecurityIdentityDrillIn | null): RecordIdentity | null {
  if (entry && detail?.securityId !== entry.securityId) detail = null;
  const id = entry?.securityId ?? detail?.securityId;
  if (!id) return null;

  const primary = detail?.identifiers.find((identifier) => identifier.isPrimary && !identifier.validTo);
  const primaryValue = entry?.classification.primaryIdentifierValue ?? primary?.value;
  const primaryKind = entry?.classification.primaryIdentifierKind ?? primary?.kind;
  const alternate = detail?.identifiers.find((identifier) => !identifier.validTo && identifier.value !== primaryValue
    && ["isin", "cusip"].includes(identifier.kind.toLowerCase()));
  const ticker = primaryKind?.toLowerCase() === "ticker" ? primaryValue
    : detail?.identifiers.find((identifier) => !identifier.validTo && identifier.kind.toLowerCase() === "ticker")?.value;
  const status = detail?.status ?? entry?.status;
  const assetClass = detail?.assetClass ?? entry?.classification.assetClass;
  const needsReview = status === "Pending" || !primaryValue;

  return {
    kind: "instrument",
    id,
    name: detail?.displayName ?? entry?.displayName ?? id,
    identifier: [[primaryKind, primaryValue].filter(Boolean).join(" "), alternate ? `${alternate.kind} ${alternate.value}` : null].filter(Boolean).join(" · "),
    assetClass,
    status,
    asOf: detail?.effectiveFrom ?? entry?.economicDefinition.effectiveFrom,
    trust: needsReview ? "Identity review required" : detail ? "Identity on file" : "Identity evidence unavailable",
    trustTone: needsReview ? "review" : detail ? "ready" : "unknown",
    scope: { securityId: id, symbol: ticker }
  };
}
