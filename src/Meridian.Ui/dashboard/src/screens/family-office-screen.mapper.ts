/**
 * Projects the `/api/workstation/family-office/overview` contract onto the shape
 * the Family Office screen renders.
 *
 * Two rules govern every field below, because this surface reports money:
 *
 * 1. Consolidated figures come from the server's own balance sheet and readiness
 *    projection, never from re-adding the parts client-side.
 * 2. A figure the contract does not carry is not invented. Per-entity liabilities
 *    have no source in the contract, so they stay at zero and only ever suppress a
 *    tone; a missing ownership percentage stays null and renders as "Unmapped".
 */

import type {
  FamilyOfficeAssetClassExposure,
  FamilyOfficeCommitmentSummary,
  FamilyOfficeEntityNode,
  FamilyOfficeEntityStructure,
  FamilyOfficePrivateAssetSummary
} from "@/screens/family-office-screen.view-model";
import type { FamilyOfficeOverview, PrivateAssetSummary } from "@/types/family-office.types";

/** Reconciliation status the account read model reports for a clean account. */
const RECONCILED_STATUS = "Reconciled";

/** Evidence completeness that means an independent valuation is on file. */
const COMPLETE_EVIDENCE = "Complete";

/**
 * Maps the consolidated overview onto the screen read model, or null when the
 * server reports no family-office structure — the screen then keeps its honest
 * "not connected" state instead of rendering an all-zero household.
 */
export function mapFamilyOfficeOverview(overview: FamilyOfficeOverview | null): FamilyOfficeEntityStructure | null {
  if (!overview || overview.entities.length === 0) {
    return null;
  }

  const descendants = buildDescendantIndex(overview);
  const privateAssets = overview.privateAssets.map(toPrivateAssetSummary);
  const staleAssetsByEntity = groupCount(
    privateAssets.filter((asset) => asset.valuationStatus !== "current"),
    (asset) => asset.entityId
  );
  const brokenAccountsByEntity = groupCount(
    overview.accounts.filter((account) => account.reconciliationStatus !== RECONCILED_STATUS),
    (account) => account.entityId
  );

  const entities = overview.entities.map((entity) => {
    const scope = descendants.get(entity.entityId) ?? new Set([entity.entityId]);
    const accounts = overview.accounts.filter((account) => scope.has(account.entityId));
    const isRoot = entity.entityId === overview.familyOfficeId;

    const node: FamilyOfficeEntityNode = {
      entityId: entity.entityId,
      displayName: entity.displayName,
      entityType: entity.entityType,
      parentEntityId: entity.parentEntityId ?? null,
      ownershipPercent: entity.ownershipPercent ?? null,
      netWorth: sumOver(accounts, (account) => account.totalEquity)
        + sumOver(privateAssets.filter((asset) => scope.has(asset.entityId)), (asset) => asset.value),
      cash: sumOver(accounts, (account) => account.cashBalance),
      // The contract reports liabilities only for the consolidated household, so
      // every non-root entity stays at zero rather than carrying a made-up split.
      liabilities: 0,
      privateAssetValue: sumOver(privateAssets.filter((asset) => scope.has(asset.entityId)), (asset) => asset.value),
      unfundedCommitments: sumOver(
        overview.capitalCommitments.filter((commitment) => scope.has(commitment.entityId)),
        (commitment) => commitment.unfundedAmount
      ),
      reconciliationBreakCount: countOver(scope, brokenAccountsByEntity),
      staleValuationWarningCount: countOver(scope, staleAssetsByEntity),
      detail: buildEntityDetail(entity.entityType, entity.jurisdiction, entity.isOperatingEntity, accounts.length)
    };

    return isRoot ? applyConsolidatedTotals(node, overview) : node;
  });

  return {
    familyOfficeId: overview.familyOfficeId,
    displayName: overview.displayName,
    baseCurrency: overview.baseCurrency,
    asOfDate: overview.asOfDate,
    entities: hasRoot(entities, overview.familyOfficeId)
      ? entities
      : [buildSyntheticRoot(overview, entities, privateAssets), ...entities],
    assetClassExposures: buildAssetClassExposures(overview),
    privateAssets,
    commitments: overview.capitalCommitments.map<FamilyOfficeCommitmentSummary>((commitment) => ({
      commitmentId: commitment.commitmentId,
      entityId: commitment.entityId,
      vehicleName: commitment.vehicleName,
      unfundedAmount: commitment.unfundedAmount
    }))
  };
}

/**
 * Replaces the root entity's rolled-up figures with the server's consolidated
 * balance sheet and readiness counts. Re-adding accounts client-side would drop
 * whatever the balance sheet includes and the account list does not — ledger
 * liabilities among them.
 */
function applyConsolidatedTotals(node: FamilyOfficeEntityNode, overview: FamilyOfficeOverview): FamilyOfficeEntityNode {
  return {
    ...node,
    netWorth: overview.balanceSheet.netWorth,
    cash: overview.balanceSheet.cashAndEquivalents,
    liabilities: overview.balanceSheet.totalLiabilities,
    privateAssetValue: overview.balanceSheet.privateAssets,
    unfundedCommitments: overview.balanceSheet.unfundedCommitments,
    reconciliationBreakCount: overview.readiness.openExceptionCount
  };
}

/**
 * The fund-structure graph does not always expose the family-office client itself
 * as an entity. Without a root node the panels would fall back to summing parts and
 * lose the consolidated balance sheet, so one is synthesized from it.
 */
function buildSyntheticRoot(
  overview: FamilyOfficeOverview,
  entities: readonly FamilyOfficeEntityNode[],
  privateAssets: readonly FamilyOfficePrivateAssetSummary[]
): FamilyOfficeEntityNode {
  return applyConsolidatedTotals(
    {
      entityId: overview.familyOfficeId,
      displayName: overview.displayName,
      entityType: "FamilyOffice",
      parentEntityId: null,
      ownershipPercent: 100,
      netWorth: 0,
      cash: 0,
      liabilities: 0,
      privateAssetValue: 0,
      unfundedCommitments: 0,
      reconciliationBreakCount: 0,
      // Counted from the asset register, not summed across entities: each entity's
      // count already includes its descendants, so summing them double-counts.
      staleValuationWarningCount: privateAssets.filter((asset) => asset.valuationStatus !== "current").length,
      detail: `Consolidated household position across ${entities.length} mapped structure records.`
    },
    overview
  );
}

/** Asset-class exposure rolled up from the public asset register. */
function buildAssetClassExposures(overview: FamilyOfficeOverview): FamilyOfficeAssetClassExposure[] {
  const totals = new Map<string, number>();
  for (const asset of overview.publicAssets) {
    totals.set(asset.assetClass, (totals.get(asset.assetClass) ?? 0) + asset.marketValue);
  }

  return [...totals.entries()]
    .map(([assetClass, value]) => ({ assetClass, value }))
    .sort((left, right) => right.value - left.value);
}

/**
 * Valuation confidence for a private mark. "missing-evidence" and "stale" are the
 * two states the screen escalates; anything with complete evidence and a valuation
 * date is treated as current.
 */
function toPrivateAssetSummary(asset: PrivateAssetSummary): FamilyOfficePrivateAssetSummary {
  return {
    assetId: asset.privateAssetId,
    entityId: asset.entityId,
    displayName: asset.displayName,
    assetType: asset.assetType,
    value: asset.currentValue,
    valuationStatus:
      asset.evidenceCompleteness !== COMPLETE_EVIDENCE
        ? "missing-evidence"
        : asset.valuationDate
          ? "current"
          : "stale"
  };
}

function buildEntityDetail(
  entityType: string,
  jurisdiction: string | null | undefined,
  isOperatingEntity: boolean,
  accountCount: number
): string {
  const parts = [entityType];
  if (jurisdiction) {
    parts.push(jurisdiction);
  }
  if (isOperatingEntity) {
    parts.push("operating entity");
  }
  parts.push(accountCount === 1 ? "1 linked account" : `${accountCount} linked accounts`);

  return `${parts.join(" · ")}.`;
}

/**
 * Each entity mapped to itself plus every descendant, so a holding entity's
 * figures include the accounts and marks held beneath it. Cycles in the ownership
 * links terminate instead of recursing forever.
 */
function buildDescendantIndex(overview: FamilyOfficeOverview): Map<string, Set<string>> {
  const children = new Map<string, string[]>();
  for (const entity of overview.entities) {
    const parentId = entity.parentEntityId;
    if (parentId) {
      children.set(parentId, [...(children.get(parentId) ?? []), entity.entityId]);
    }
  }

  const index = new Map<string, Set<string>>();
  for (const entity of overview.entities) {
    const scope = new Set<string>();
    const queue = [entity.entityId];
    while (queue.length > 0) {
      const current = queue.shift() as string;
      if (scope.has(current)) {
        continue;
      }
      scope.add(current);
      queue.push(...(children.get(current) ?? []));
    }
    index.set(entity.entityId, scope);
  }

  return index;
}

function hasRoot(entities: readonly FamilyOfficeEntityNode[], familyOfficeId: string): boolean {
  return entities.some((entity) => entity.entityId === familyOfficeId);
}

function groupCount<T>(items: readonly T[], keyOf: (item: T) => string): Map<string, number> {
  const counts = new Map<string, number>();
  for (const item of items) {
    const key = keyOf(item);
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }

  return counts;
}

function countOver(scope: ReadonlySet<string>, counts: ReadonlyMap<string, number>): number {
  let total = 0;
  for (const entityId of scope) {
    total += counts.get(entityId) ?? 0;
  }

  return total;
}

function sumOver<T>(items: readonly T[], valueOf: (item: T) => number): number {
  return items.reduce((total, item) => total + valueOf(item), 0);
}
