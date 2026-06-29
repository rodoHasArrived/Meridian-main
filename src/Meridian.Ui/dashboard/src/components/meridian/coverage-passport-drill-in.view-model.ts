/**
 * Pure view-model for the coverage → passport drill-through (Phase 4 UI). A multi-asset coverage row is
 * an asset-class aggregate and carries no securityId, so the drill-through lists the Security Master
 * records belonging to that asset class; each row launches the governed passport editor.
 */

import type { SecurityMasterEntry } from "@/types";

export interface CoverageSecurityDrillInRow {
  securityId: string;
  displayName: string;
  symbol: string | null;
  assetClass: string;
}

function normalize(value: string | null | undefined): string {
  return (value ?? "").trim().toLowerCase();
}

/**
 * Returns the securities in <paramref>securities</paramref> whose classification matches
 * <paramref>assetClass</paramref>, sorted by display name, as launchable drill-in rows.
 */
export function buildCoverageSecurityDrillIns(
  securities: readonly SecurityMasterEntry[] | null | undefined,
  assetClass: string
): CoverageSecurityDrillInRow[] {
  if (!securities || securities.length === 0 || normalize(assetClass).length === 0) {
    return [];
  }

  const target = normalize(assetClass);
  return securities
    .filter((entry) => entry && normalize(entry.classification?.assetClass) === target)
    .map((entry) => ({
      securityId: entry.securityId,
      displayName: entry.displayName ?? "",
      symbol: entry.classification?.primaryIdentifierValue ?? null,
      assetClass: entry.classification?.assetClass ?? assetClass
    }))
    .sort((a, b) => a.displayName.localeCompare(b.displayName));
}
