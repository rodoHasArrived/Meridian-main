/**
 * Family-office read-model types.
 *
 * Mirrors `Meridian.Ui.Shared.Contracts.FamilyOfficeContracts` (camelCase on the
 * wire, nulls omitted). Kept in a dedicated module rather than the shared
 * `@/types` barrel so the consolidated-household surface can grow without
 * inflating the central types file.
 */

/** Source document, packet, or operator route backing a family-office value. */
export interface FamilyOfficeEvidenceLink {
  evidenceId: string;
  sourceSystem: string;
  sourceDocumentId?: string | null;
  label: string;
  evidenceType: string;
  route?: string | null;
  capturedAtUtc?: string | null;
  asOfDate?: string | null;
  valuationDate?: string | null;
  completenessStatus?: string | null;
}

/** Consolidated family balance sheet with the evidence fields retained for review. */
export interface FamilyBalanceSheet {
  familyOfficeId: string;
  baseCurrency: string;
  totalAssets: number;
  totalLiabilities: number;
  netWorth: number;
  liquidAssets: number;
  marketableSecurities: number;
  privateAssets: number;
  realAssets: number;
  cashAndEquivalents: number;
  unfundedCommitments: number;
  sourceSystem: string;
  sourceDocumentId?: string | null;
  asOfDate: string;
  valuationDate?: string | null;
  evidenceCompleteness: string;
  reconciliationStatus: string;
  lastReviewedBy?: string | null;
  lastReviewedAtUtc?: string | null;
}

/** Legal, trust, fund, household, or operating entity in the family structure. */
export interface FamilyEntity {
  entityId: string;
  displayName: string;
  entityType: string;
  jurisdiction?: string | null;
  taxResidency?: string | null;
  baseCurrency: string;
  parentEntityId?: string | null;
  ownershipPercent?: number | null;
  isOperatingEntity: boolean;
  evidenceLinks: FamilyOfficeEvidenceLink[];
}

export interface FamilyOwnershipNode {
  nodeId: string;
  displayName: string;
  nodeType: string;
  entityId?: string | null;
  jurisdiction?: string | null;
  currency?: string | null;
}

export interface FamilyOwnershipEdge {
  edgeId: string;
  sourceNodeId: string;
  targetNodeId: string;
  relationshipType: string;
  ownershipPercent?: number | null;
  effectiveFrom?: string | null;
  effectiveTo?: string | null;
  sourceDocumentId?: string | null;
}

export interface FamilyOwnershipGraph {
  asOfDate: string;
  nodes: FamilyOwnershipNode[];
  edges: FamilyOwnershipEdge[];
  evidenceLinks: FamilyOfficeEvidenceLink[];
}

export interface FamilyAccountSummary {
  accountId: string;
  entityId: string;
  displayName: string;
  accountType: string;
  custodian?: string | null;
  providerAccountId?: string | null;
  currency: string;
  cashBalance: number;
  marketValue: number;
  accruedIncome: number;
  totalEquity: number;
  sourceSystem: string;
  sourceDocumentId?: string | null;
  asOfDate: string;
  valuationDate?: string | null;
  evidenceCompleteness: string;
  reconciliationStatus: string;
  lastReviewedBy?: string | null;
  lastReviewedAtUtc?: string | null;
}

export interface FamilyAssetSummary {
  assetId: string;
  entityId: string;
  accountId?: string | null;
  displayName: string;
  assetClass: string;
  symbol?: string | null;
  currency: string;
  quantity: number;
  marketValue: number;
  costBasis: number;
  unrealizedGainLoss: number;
  percentOfNetWorth: number;
  sourceSystem: string;
  sourceDocumentId?: string | null;
  asOfDate: string;
  valuationDate?: string | null;
  evidenceCompleteness: string;
  reconciliationStatus: string;
  lastReviewedBy?: string | null;
  lastReviewedAtUtc?: string | null;
}

/** Asset carried at a valuation mark rather than an exchange price. */
export interface PrivateAssetSummary {
  privateAssetId: string;
  entityId: string;
  displayName: string;
  assetType: string;
  currency: string;
  currentValue: number;
  commitmentAmount?: number | null;
  calledCapital?: number | null;
  distributedCapital?: number | null;
  valuationMethod?: string | null;
  sourceSystem: string;
  sourceDocumentId?: string | null;
  asOfDate: string;
  valuationDate?: string | null;
  evidenceCompleteness: string;
  reconciliationStatus: string;
  lastReviewedBy?: string | null;
  lastReviewedAtUtc?: string | null;
}

export interface CapitalCommitment {
  commitmentId: string;
  entityId: string;
  vehicleName: string;
  strategy: string;
  currency: string;
  commitmentAmount: number;
  calledAmount: number;
  unfundedAmount: number;
  distributedAmount: number;
  currentNav: number;
  vintageYear?: number | null;
  sourceSystem: string;
  sourceDocumentId?: string | null;
  asOfDate: string;
  valuationDate?: string | null;
  evidenceCompleteness: string;
  reconciliationStatus: string;
  lastReviewedBy?: string | null;
  lastReviewedAtUtc?: string | null;
}

export interface CapitalActivity {
  activityId: string;
  commitmentId: string;
  entityId: string;
  activityType: string;
  currency: string;
  amount: number;
  effectiveDate: string;
  noticeDate?: string | null;
  dueDate?: string | null;
  status: string;
  sourceSystem: string;
  sourceDocumentId?: string | null;
  asOfDate: string;
  valuationDate?: string | null;
  evidenceCompleteness: string;
  reconciliationStatus: string;
  lastReviewedBy?: string | null;
  lastReviewedAtUtc?: string | null;
}

/** Whether the family-office workspace can be trusted for decisions. */
export interface FamilyOfficeReadiness {
  status: string;
  evidenceCompleteness: string;
  reconciliationStatus: string;
  openExceptionCount: number;
  itemsNeedingReviewCount: number;
  generatedAtUtc: string;
  lastReviewedBy?: string | null;
  lastReviewedAtUtc?: string | null;
  blockers: string[];
  evidenceLinks: FamilyOfficeEvidenceLink[];
}

export interface FamilyOfficeOverview {
  familyOfficeId: string;
  displayName: string;
  baseCurrency: string;
  asOfDate: string;
  balanceSheet: FamilyBalanceSheet;
  entities: FamilyEntity[];
  ownershipGraph: FamilyOwnershipGraph;
  accounts: FamilyAccountSummary[];
  publicAssets: FamilyAssetSummary[];
  privateAssets: PrivateAssetSummary[];
  capitalCommitments: CapitalCommitment[];
  recentCapitalActivity: CapitalActivity[];
  readiness: FamilyOfficeReadiness;
  evidenceLinks: FamilyOfficeEvidenceLink[];
}

/** Empty and degraded guidance shared by the narrower family-office reads. */
export interface FamilyOfficeEndpointState {
  isEmpty: boolean;
  emptyStateGuidance?: string | null;
  warnings: string[];
  generatedAtUtc: string;
}

export interface FamilyOfficeBalanceSheetResponse {
  balanceSheet: FamilyBalanceSheet;
  state: FamilyOfficeEndpointState;
}

export interface FamilyOfficeEntitiesResponse {
  entities: FamilyEntity[];
  state: FamilyOfficeEndpointState;
}

export interface FamilyOfficeOwnershipGraphResponse {
  ownershipGraph: FamilyOwnershipGraph;
  state: FamilyOfficeEndpointState;
}
