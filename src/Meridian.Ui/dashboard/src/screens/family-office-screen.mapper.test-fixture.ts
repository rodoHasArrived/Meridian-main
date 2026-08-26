/**
 * Shared `/api/workstation/family-office/overview` payload for mapper and screen
 * tests. Modeled on what `FamilyOfficeReadService` actually emits: a family-office
 * client entity, a trust and an LLC beneath it, custody accounts on the leaves,
 * one private mark without complete evidence, and one unfunded commitment.
 */

import type { FamilyOfficeOverview } from "@/types/family-office.types";

const ROOT_ID = "11111111-1111-1111-1111-111111111111";
const TRUST_ID = "22222222-2222-2222-2222-222222222222";
const LLC_ID = "33333333-3333-3333-3333-333333333333";

export function buildFamilyOfficeOverviewFixture(
  overrides: Partial<FamilyOfficeOverview> = {}
): FamilyOfficeOverview {
  return {
    familyOfficeId: ROOT_ID,
    displayName: "Ridgeline Family Office",
    baseCurrency: "USD",
    asOfDate: "2026-05-29",
    balanceSheet: {
      familyOfficeId: ROOT_ID,
      baseCurrency: "USD",
      totalAssets: 92_000_000,
      totalLiabilities: 7_000_000,
      netWorth: 85_000_000,
      liquidAssets: 12_000_000,
      marketableSecurities: 48_000_000,
      privateAssets: 26_000_000,
      realAssets: 6_000_000,
      cashAndEquivalents: 9_500_000,
      unfundedCommitments: 4_250_000,
      sourceSystem: "fund-structure",
      sourceDocumentId: "bs-2026-05",
      asOfDate: "2026-05-29",
      valuationDate: "2026-05-29",
      evidenceCompleteness: "Complete",
      reconciliationStatus: "OpenBreaks",
      lastReviewedBy: "controller@example.com",
      lastReviewedAtUtc: "2026-05-29T12:00:00Z"
    },
    entities: [
      entity(ROOT_ID, "Ridgeline Family Office", "FamilyOfficeClient", null, null),
      entity(TRUST_ID, "Ridgeline Family Trust", "Trust", ROOT_ID, 60),
      entity(LLC_ID, "Ridgeline Holdings LLC", "LimitedLiabilityCompany", ROOT_ID, null)
    ],
    ownershipGraph: {
      asOfDate: "2026-05-29",
      nodes: [
        { nodeId: ROOT_ID, displayName: "Ridgeline Family Office", nodeType: "Client" },
        { nodeId: TRUST_ID, displayName: "Ridgeline Family Trust", nodeType: "LegalEntity" },
        { nodeId: LLC_ID, displayName: "Ridgeline Holdings LLC", nodeType: "LegalEntity" }
      ],
      edges: [
        {
          edgeId: "edge-trust",
          sourceNodeId: ROOT_ID,
          targetNodeId: TRUST_ID,
          relationshipType: "Ownership",
          ownershipPercent: 60
        }
      ],
      evidenceLinks: []
    },
    accounts: [
      account("account-trust", TRUST_ID, 4_500_000, 31_000_000, "Reconciled"),
      account("account-llc", LLC_ID, 5_000_000, 22_000_000, "OpenBreaks")
    ],
    publicAssets: [
      asset("asset-equity", TRUST_ID, "Public equity", 28_000_000),
      asset("asset-credit", LLC_ID, "Fixed income", 20_000_000),
      asset("asset-equity-llc", LLC_ID, "Public equity", 2_000_000)
    ],
    privateAssets: [
      {
        privateAssetId: "private-fund-vii",
        entityId: TRUST_ID,
        displayName: "Ridgeline PE Fund VII",
        assetType: "PrivateAsset",
        currency: "USD",
        currentValue: 26_000_000,
        commitmentAmount: 30_000_000,
        calledCapital: 26_000_000,
        distributedCapital: 1_000_000,
        valuationMethod: "AssignmentReference",
        sourceSystem: "fund-structure-assignment",
        sourceDocumentId: "pe-vii",
        asOfDate: "2026-05-29",
        valuationDate: null,
        evidenceCompleteness: "Partial",
        reconciliationStatus: "NotReconciled",
        lastReviewedBy: null,
        lastReviewedAtUtc: null
      }
    ],
    capitalCommitments: [
      {
        commitmentId: "commitment-pe-vii",
        entityId: TRUST_ID,
        vehicleName: "Ridgeline PE Fund VII",
        strategy: "PrivateMarkets",
        currency: "USD",
        commitmentAmount: 30_000_000,
        calledAmount: 26_000_000,
        unfundedAmount: 4_250_000,
        distributedAmount: 1_000_000,
        currentNav: 26_000_000,
        vintageYear: 2024,
        sourceSystem: "fund-structure-assignment",
        sourceDocumentId: "pe-vii",
        asOfDate: "2026-05-29",
        valuationDate: null,
        evidenceCompleteness: "Partial",
        reconciliationStatus: "NotReconciled",
        lastReviewedBy: null,
        lastReviewedAtUtc: null
      }
    ],
    recentCapitalActivity: [],
    readiness: {
      status: "NeedsReview",
      evidenceCompleteness: "Partial",
      reconciliationStatus: "OpenBreaks",
      openExceptionCount: 3,
      itemsNeedingReviewCount: 2,
      generatedAtUtc: "2026-05-29T12:00:00Z",
      lastReviewedBy: "controller@example.com",
      lastReviewedAtUtc: "2026-05-29T12:00:00Z",
      blockers: [],
      evidenceLinks: []
    },
    evidenceLinks: [],
    ...overrides
  };
}

export const FAMILY_OFFICE_FIXTURE_IDS = { root: ROOT_ID, trust: TRUST_ID, llc: LLC_ID } as const;

function entity(
  entityId: string,
  displayName: string,
  entityType: string,
  parentEntityId: string | null,
  ownershipPercent: number | null
) {
  return {
    entityId,
    displayName,
    entityType,
    jurisdiction: parentEntityId === null ? null : "Delaware",
    taxResidency: null,
    baseCurrency: "USD",
    parentEntityId,
    ownershipPercent,
    isOperatingEntity: entityType === "LimitedLiabilityCompany",
    evidenceLinks: []
  };
}

function account(
  accountId: string,
  entityId: string,
  cashBalance: number,
  totalEquity: number,
  reconciliationStatus: string
) {
  return {
    accountId,
    entityId,
    displayName: `${accountId} custody`,
    accountType: "Custody",
    custodian: "Example Custody",
    providerAccountId: null,
    currency: "USD",
    cashBalance,
    marketValue: totalEquity - cashBalance,
    accruedIncome: 0,
    totalEquity,
    sourceSystem: "fund-accounts",
    sourceDocumentId: accountId,
    asOfDate: "2026-05-29",
    valuationDate: "2026-05-29",
    evidenceCompleteness: "Complete",
    reconciliationStatus,
    lastReviewedBy: null,
    lastReviewedAtUtc: null
  };
}

function asset(assetId: string, entityId: string, assetClass: string, marketValue: number) {
  return {
    assetId,
    entityId,
    accountId: null,
    displayName: `${assetClass} sleeve`,
    assetClass,
    symbol: null,
    currency: "USD",
    quantity: 1,
    marketValue,
    costBasis: marketValue,
    unrealizedGainLoss: 0,
    percentOfNetWorth: 0,
    sourceSystem: "fund-accounts",
    sourceDocumentId: assetId,
    asOfDate: "2026-05-29",
    valuationDate: "2026-05-29",
    evidenceCompleteness: "Complete",
    reconciliationStatus: "Reconciled",
    lastReviewedBy: null,
    lastReviewedAtUtc: null
  };
}
