import type { CorporateAction, SecurityIdentityDrillIn, SecurityMasterEntry } from "@/types";

export type AssetDetailStatusTone = "default" | "success" | "warning" | "danger" | "outline";

export interface AssetDetailOverviewField {
  label: string;
  value: string;
}

export interface AssetDetailCorporateActionRow {
  corpActId: string;
  label: string;
  detail: string;
}

export interface AssetDetailOverviewViewState {
  displayName: string;
  symbol: string;
  statusLabel: string;
  statusTone: AssetDetailStatusTone;
  fields: AssetDetailOverviewField[];
  corporateActions: AssetDetailCorporateActionRow[];
  hasCorporateActions: boolean;
}

function securityStatusTone(status: string): AssetDetailStatusTone {
  switch (status) {
    case "Active":
      return "success";
    case "Pending":
      return "warning";
    case "Inactive":
    case "Deactivated":
      return "danger";
    default:
      return "default";
  }
}

function buildCorporateActionRow(action: CorporateAction): AssetDetailCorporateActionRow {
  const detailParts = [
    action.dividendPerShare != null ? `${action.dividendPerShare} ${action.currency ?? ""}`.trim() + " per share" : null,
    action.splitRatio != null ? `Split ratio ${action.splitRatio}` : null,
    action.payDate ? `Pay date ${action.payDate}` : null
  ].filter((part): part is string => Boolean(part));

  return {
    corpActId: action.corpActId,
    label: `${action.eventType} - ${action.exDate}`,
    detail: detailParts.length > 0 ? detailParts.join(", ") : "No additional detail."
  };
}

export function buildAssetDetailOverviewViewState({
  entry,
  identity,
  corporateActions
}: {
  entry: SecurityMasterEntry;
  identity: SecurityIdentityDrillIn | null;
  corporateActions: CorporateAction[];
}): AssetDetailOverviewViewState {
  const symbol = entry.classification.primaryIdentifierValue ?? entry.securityId;

  return {
    displayName: entry.displayName,
    symbol,
    statusLabel: entry.status,
    statusTone: securityStatusTone(entry.status),
    fields: [
      { label: "Security ID", value: entry.securityId },
      { label: "Asset class", value: entry.classification.assetClass },
      { label: "Sub-type", value: entry.classification.subType ?? "Unclassified" },
      { label: "Primary identifier", value: `${entry.classification.primaryIdentifierKind ?? "None"} - ${symbol}` },
      { label: "Currency", value: entry.economicDefinition.currency },
      { label: "Effective from", value: entry.economicDefinition.effectiveFrom ?? "Unknown" },
      { label: "Effective to", value: entry.economicDefinition.effectiveTo ?? "Open" },
      { label: "Version", value: identity ? String(identity.version) : "Unknown" }
    ],
    corporateActions: corporateActions.map(buildCorporateActionRow),
    hasCorporateActions: corporateActions.length > 0
  };
}
