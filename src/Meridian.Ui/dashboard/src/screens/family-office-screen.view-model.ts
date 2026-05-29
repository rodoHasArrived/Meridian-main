import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export type FamilyOfficeTone = "default" | "success" | "warning" | "danger";
export type FamilyOfficeEntityType = "family-office" | "trust" | "llc" | "foundation" | "direct-accounts";

export interface FamilyOfficeRouteMetadata {
  path: string;
  workspaceLabel: string;
  label: string;
  title: string;
  description: string;
  ariaLabel: string;
  emptyState: string;
  disabledReason: string | null;
}

export interface FamilyOfficePanelViewModel {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: FamilyOfficeTone;
  emptyState: string;
  ariaLabel: string;
}

export interface FamilyOfficeEntityNode {
  entityId: string;
  displayName: string;
  entityType: FamilyOfficeEntityType;
  parentEntityId: string | null;
  ownershipPercent: number;
  netWorth: number;
  cash: number;
  liabilities: number;
  privateAssetValue: number;
  unfundedCommitments: number;
  reconciliationBreakCount: number;
  staleValuationWarningCount: number;
  detail: string;
}

export interface FamilyOfficeAssetClassExposure {
  assetClass: string;
  value: number;
}

export interface FamilyOfficePrivateAssetSummary {
  assetId: string;
  entityId: string;
  displayName: string;
  assetType: string;
  value: number;
  valuationStatus: "current" | "stale" | "missing-evidence";
}

export interface FamilyOfficeCommitmentSummary {
  commitmentId: string;
  entityId: string;
  vehicleName: string;
  unfundedAmount: number;
}

export interface FamilyOfficeEntityStructure {
  familyOfficeId: string;
  displayName: string;
  baseCurrency: string;
  asOfDate: string;
  entities: FamilyOfficeEntityNode[];
  assetClassExposures: FamilyOfficeAssetClassExposure[];
  privateAssets: FamilyOfficePrivateAssetSummary[];
  commitments: FamilyOfficeCommitmentSummary[];
}

export interface FamilyOfficeOwnershipNode {
  id: string;
  label: string;
  type: "family" | "entity" | "asset";
  value: string;
  percentage: string;
  detail: string;
  parentId: string | null;
  tone: FamilyOfficeTone;
  rowClassName: string;
  isSelected: boolean;
  expanded: boolean;
  detailPanelId: string;
  selectAriaLabel: string;
  ariaLabel: string;
}

export interface FamilyOfficeOwnershipEdge {
  id: string;
  fromId: string;
  toId: string;
  label: string;
}

export interface FamilyOfficeOwnershipGraphViewModel {
  title: string;
  description: string;
  ariaLabel: string;
  keyboardInstructions: string;
  tableFallbackLabel: string;
  graphToggleLabel: string;
  tableToggleLabel: string;
  emptyState: string;
  selectedDetailTitle: string;
  selectedDetailEmptyTitle: string;
  selectedNodeId: string | null;
  selectedNode: FamilyOfficeOwnershipNode | null;
  nodes: FamilyOfficeOwnershipNode[];
  edges: FamilyOfficeOwnershipEdge[];
}

export interface FamilyOfficeScreenViewModel {
  route: FamilyOfficeRouteMetadata;
  statusChips: Array<{ label: string; value: string }>;
  panels: FamilyOfficePanelViewModel[];
  ownershipGraph: FamilyOfficeOwnershipGraphViewModel;
}

const FAMILY_OFFICE_ROUTE_METADATA: FamilyOfficeRouteMetadata = {
  path: WORKSTATION_ROUTE_CATALOG.portfolioFamilyOffice,
  workspaceLabel: "Portfolio",
  label: "Family office",
  title: "Family Office Portfolio",
  description: "Household-level net worth, entity ownership, private holdings, commitments, and reconciliation exceptions.",
  ariaLabel: "Family office portfolio route",
  emptyState: "Family office data is not connected yet. Link portfolio, accounting, and private-asset feeds before using this lane for operator decisions.",
  disabledReason: null
};

const DEFAULT_FAMILY_OFFICE_ENTITY_STRUCTURE: FamilyOfficeEntityStructure = {
  familyOfficeId: "family-holdco",
  displayName: "Meridian Family HoldCo",
  baseCurrency: "USD",
  asOfDate: "2026-05-29",
  entities: [
    {
      entityId: "family-holdco",
      displayName: "Meridian Family HoldCo",
      entityType: "family-office",
      parentEntityId: null,
      ownershipPercent: 100,
      netWorth: 128_400_000,
      cash: 14_200_000,
      liabilities: 8_700_000,
      privateAssetValue: 42_600_000,
      unfundedCommitments: 11_400_000,
      reconciliationBreakCount: 7,
      staleValuationWarningCount: 4,
      detail: "Consolidated beneficial ownership across family entities."
    },
    {
      entityId: "alpha-trust",
      displayName: "Alpha Family Trust",
      entityType: "trust",
      parentEntityId: "family-holdco",
      ownershipPercent: 42,
      netWorth: 54_100_000,
      cash: 6_100_000,
      liabilities: 1_300_000,
      privateAssetValue: 31_900_000,
      unfundedCommitments: 11_400_000,
      reconciliationBreakCount: 2,
      staleValuationWarningCount: 1,
      detail: "Public markets, cash reserves, and private fund LP interests."
    },
    {
      entityId: "beta-llc",
      displayName: "Beta Holdings LLC",
      entityType: "llc",
      parentEntityId: "family-holdco",
      ownershipPercent: 30,
      netWorth: 38_700_000,
      cash: 2_700_000,
      liabilities: 4_900_000,
      privateAssetValue: 23_600_000,
      unfundedCommitments: 0,
      reconciliationBreakCount: 3,
      staleValuationWarningCount: 3,
      detail: "Operating-company and real-estate holdings with stale-mark warnings."
    },
    {
      entityId: "gamma-foundation",
      displayName: "Gamma Foundation",
      entityType: "foundation",
      parentEntityId: "family-holdco",
      ownershipPercent: 17,
      netWorth: 21_300_000,
      cash: 2_200_000,
      liabilities: 700_000,
      privateAssetValue: 4_600_000,
      unfundedCommitments: 0,
      reconciliationBreakCount: 0,
      staleValuationWarningCount: 0,
      detail: "Restricted charitable assets and investment policy allocations."
    },
    {
      entityId: "direct-accounts",
      displayName: "Direct Family Accounts",
      entityType: "direct-accounts",
      parentEntityId: "family-holdco",
      ownershipPercent: 11,
      netWorth: 14_300_000,
      cash: 3_200_000,
      liabilities: 1_800_000,
      privateAssetValue: 2_500_000,
      unfundedCommitments: 0,
      reconciliationBreakCount: 2,
      staleValuationWarningCount: 0,
      detail: "Personal brokerage, treasury, and liability accounts."
    }
  ],
  assetClassExposures: [
    { assetClass: "Public equity", value: 24_800_000 },
    { assetClass: "Fixed income", value: 18_100_000 },
    { assetClass: "Cash", value: 14_200_000 },
    { assetClass: "Real estate", value: 23_600_000 },
    { assetClass: "Venture", value: 12_400_000 },
    { assetClass: "Private credit", value: 9_600_000 },
    { assetClass: "Operating assets", value: 7_100_000 },
    { assetClass: "Collectibles", value: 3_200_000 },
    { assetClass: "Alternatives", value: 15_400_000 }
  ],
  privateAssets: [
    { assetId: "private-funds", entityId: "alpha-trust", displayName: "Private fund sleeve", assetType: "Private funds", value: 31_900_000, valuationStatus: "missing-evidence" },
    { assetId: "real-estate", entityId: "beta-llc", displayName: "Real estate sleeve", assetType: "Real estate", value: 23_600_000, valuationStatus: "stale" },
    { assetId: "operating-assets", entityId: "gamma-foundation", displayName: "Operating asset sleeve", assetType: "Operating assets", value: 4_600_000, valuationStatus: "current" },
    { assetId: "direct-collectibles", entityId: "direct-accounts", displayName: "Collectibles sleeve", assetType: "Collectibles", value: 2_500_000, valuationStatus: "current" }
  ],
  commitments: [
    { commitmentId: "alpha-pe-2026", entityId: "alpha-trust", vehicleName: "Alpha PE Fund VII", unfundedAmount: 8_600_000 },
    { commitmentId: "alpha-credit-2025", entityId: "alpha-trust", vehicleName: "Alpha Private Credit II", unfundedAmount: 2_800_000 }
  ]
};

export function buildFamilyOfficeScreenViewModel(
  selectedNodeId: string | null = null,
  entityStructure: FamilyOfficeEntityStructure = DEFAULT_FAMILY_OFFICE_ENTITY_STRUCTURE
): FamilyOfficeScreenViewModel {
  const baseNodes = buildOwnershipNodes(entityStructure);
  const stableSelectedNodeId = baseNodes.some((node) => node.id === selectedNodeId)
    ? selectedNodeId
    : baseNodes[0]?.id ?? null;
  const detailPanelId = "family-office-ownership-detail";
  const nodes = baseNodes.map((node) => {
    const selected = node.id === stableSelectedNodeId;
    return {
      ...node,
      rowClassName: ownershipToneRowClassName(node.tone),
      isSelected: selected,
      expanded: selected,
      detailPanelId,
      selectAriaLabel: `Inspect ownership node ${node.label}`,
      ariaLabel: `${node.label}, ${node.type}, value ${node.value}, ${node.percentage} of consolidated net worth. ${node.detail}`
    };
  });
  const selectedNode = nodes.find((node) => node.id === stableSelectedNodeId) ?? null;

  return {
    route: FAMILY_OFFICE_ROUTE_METADATA,
    statusChips: [
      { label: "Workspace", value: FAMILY_OFFICE_ROUTE_METADATA.workspaceLabel },
      { label: "Route", value: FAMILY_OFFICE_ROUTE_METADATA.path },
      { label: "Entity source", value: entityStructure.displayName },
      { label: "Graph mode", value: "Keyboard accessible" }
    ],
    panels: buildFamilyOfficePanels(entityStructure),
    ownershipGraph: {
      title: "Ownership graph",
      description: "Navigate the family-office entity structure and private-asset sleeves as a graph, or switch to the dense table fallback for assistive technology and audit review.",
      ariaLabel: "Family office ownership graph",
      keyboardInstructions: "Use Arrow keys, Home, and End to move between ownership nodes. Press Enter or Space to inspect the focused node. Use the table view for a dense accessible fallback.",
      tableFallbackLabel: "Family office ownership table fallback",
      graphToggleLabel: "Show ownership graph",
      tableToggleLabel: "Show ownership table fallback",
      emptyState: "No ownership graph data is available yet. Map entities and beneficial owners before graph review.",
      selectedDetailTitle: "Selected ownership detail",
      selectedDetailEmptyTitle: "No ownership node selected",
      selectedNodeId: stableSelectedNodeId,
      selectedNode,
      nodes,
      edges: buildOwnershipEdges(entityStructure)
    }
  };
}

export function selectAdjacentFamilyOfficeNode(
  currentNodeId: string | null,
  direction: "next" | "previous" | "first" | "last",
  entityStructure: FamilyOfficeEntityStructure = DEFAULT_FAMILY_OFFICE_ENTITY_STRUCTURE
): string | null {
  const nodeOrder = buildOwnershipNodes(entityStructure).map((node) => node.id);
  if (nodeOrder.length === 0) {
    return null;
  }

  const currentIndex = nodeOrder.findIndex((nodeId) => nodeId === currentNodeId);
  const safeIndex = currentIndex >= 0 ? currentIndex : 0;

  switch (direction) {
    case "first":
      return nodeOrder[0];
    case "last":
      return nodeOrder[nodeOrder.length - 1];
    case "previous":
      return nodeOrder[Math.max(0, safeIndex - 1)];
    case "next":
      return nodeOrder[Math.min(nodeOrder.length - 1, safeIndex + 1)];
  }
}

function buildFamilyOfficePanels(entityStructure: FamilyOfficeEntityStructure): FamilyOfficePanelViewModel[] {
  const root = entityStructure.entities.find((entity) => entity.entityId === entityStructure.familyOfficeId) ?? entityStructure.entities[0] ?? null;
  const totalNetWorth = root?.netWorth ?? sum(entityStructure.entities.map((entity) => entity.netWorth));
  const totalCash = root?.cash ?? sum(entityStructure.entities.map((entity) => entity.cash));
  const totalLiabilities = root?.liabilities ?? sum(entityStructure.entities.map((entity) => entity.liabilities));
  const privateAssetValue = root?.privateAssetValue ?? sum(entityStructure.privateAssets.map((asset) => asset.value));
  const unfundedCommitments = root?.unfundedCommitments ?? sum(entityStructure.commitments.map((commitment) => commitment.unfundedAmount));
  const breakCount = root?.reconciliationBreakCount ?? sum(entityStructure.entities.map((entity) => entity.reconciliationBreakCount));
  const staleWarningCount = root?.staleValuationWarningCount
    ?? entityStructure.privateAssets.filter((asset) => asset.valuationStatus !== "current").length;
  const entityKinds = uniqueCount(entityStructure.entities.map((entity) => entity.entityType));

  const panels: Omit<FamilyOfficePanelViewModel, "ariaLabel">[] = [
    {
      id: "total-family-net-worth",
      label: "Total family net worth",
      value: formatCurrency(totalNetWorth),
      detail: `Consolidated ${entityStructure.displayName} NAV after liabilities and unfunded commitments as of ${entityStructure.asOfDate}.`,
      tone: "success",
      emptyState: "No household NAV feed has been connected."
    },
    {
      id: "entity-breakdown",
      label: "Entity breakdown",
      value: formatCountLabel(entityStructure.entities.length, "entity", "entities"),
      detail: `${formatCountLabel(entityKinds, "entity type", "entity types")} across trusts, holding companies, operating LLCs, foundations, and direct accounts mapped to beneficial owners.`,
      tone: entityStructure.entities.length > 0 ? "default" : "warning",
      emptyState: "No ownership entities have been mapped."
    },
    {
      id: "asset-class-breakdown",
      label: "Asset-class breakdown",
      value: formatCountLabel(entityStructure.assetClassExposures.length, "class", "classes"),
      detail: entityStructure.assetClassExposures.map((exposure) => exposure.assetClass).join(", ") || "No asset-class taxonomy is available.",
      tone: entityStructure.assetClassExposures.length > 0 ? "default" : "warning",
      emptyState: "No asset-class taxonomy is available."
    },
    {
      id: "cash-and-liabilities",
      label: "Cash and liabilities",
      value: `${formatCurrency(totalCash)} / ${formatCurrency(totalLiabilities)}`,
      detail: "Available cash and short-term liabilities across custody, treasury, and operating accounts.",
      tone: totalLiabilities > 0 ? "warning" : "success",
      emptyState: "Cash and liability ledgers have not been reconciled."
    },
    {
      id: "private-assets",
      label: "Private assets",
      value: formatCurrency(privateAssetValue),
      detail: `${formatCountLabel(entityStructure.privateAssets.length, "private asset")} tied to the entity structure and valuation evidence status.`,
      tone: staleWarningCount > 0 ? "warning" : "success",
      emptyState: "No private asset register is available."
    },
    {
      id: "unfunded-commitments",
      label: "Unfunded commitments",
      value: formatCurrency(unfundedCommitments),
      detail: `${formatCountLabel(entityStructure.commitments.length, "capital commitment")} across private funds and direct investment side letters.`,
      tone: unfundedCommitments > 0 ? "danger" : "success",
      emptyState: "Commitment schedule is unavailable."
    },
    {
      id: "unresolved-reconciliation-breaks",
      label: "Unresolved reconciliation breaks",
      value: formatCountLabel(breakCount, "break"),
      detail: "Custody, capital-call, and private valuation differences requiring operator resolution.",
      tone: breakCount > 0 ? "danger" : "success",
      emptyState: "No reconciliation queue has loaded."
    },
    {
      id: "stale-valuation-warnings",
      label: "Stale valuation warnings",
      value: formatCountLabel(staleWarningCount, "warning"),
      detail: "Private marks older than policy thresholds or missing independent valuation evidence.",
      tone: staleWarningCount > 0 ? "warning" : "success",
      emptyState: "No stale valuation policy data has loaded."
    }
  ];

  return panels.map((panel) => ({
    ...panel,
    ariaLabel: `${panel.label}: ${panel.value}. ${panel.detail}`
  }));
}

function buildOwnershipNodes(entityStructure: FamilyOfficeEntityStructure): Array<Omit<FamilyOfficeOwnershipNode, "rowClassName" | "isSelected" | "expanded" | "detailPanelId" | "selectAriaLabel" | "ariaLabel">> {
  const totalNetWorth = entityStructure.entities.find((entity) => entity.entityId === entityStructure.familyOfficeId)?.netWorth
    ?? sum(entityStructure.entities.map((entity) => entity.netWorth));
  const entityNodes = entityStructure.entities.map((entity) => ({
    id: entity.entityId,
    label: entity.displayName,
    type: entity.parentEntityId === null ? "family" as const : "entity" as const,
    value: formatCurrency(entity.netWorth),
    percentage: formatPercent(entity.ownershipPercent),
    detail: entity.detail,
    parentId: entity.parentEntityId,
    tone: entityTone(entity)
  }));
  const assetNodes = entityStructure.privateAssets
    .filter((asset) => asset.valuationStatus !== "current")
    .map((asset) => ({
      id: asset.assetId,
      label: asset.displayName,
      type: "asset" as const,
      value: formatCurrency(asset.value),
      percentage: formatPercent(totalNetWorth === 0 ? 0 : (asset.value / totalNetWorth) * 100),
      detail: `${asset.assetType} held by ${entityDisplayName(entityStructure, asset.entityId)}. ${asset.valuationStatus === "stale" ? "Valuation is past the freshness policy." : "Independent valuation evidence is missing."}`,
      parentId: asset.entityId,
      tone: asset.valuationStatus === "missing-evidence" ? "danger" as const : "warning" as const
    }));

  return [...entityNodes, ...assetNodes];
}

function buildOwnershipEdges(entityStructure: FamilyOfficeEntityStructure): FamilyOfficeOwnershipEdge[] {
  const entityEdges = entityStructure.entities
    .filter((entity) => entity.parentEntityId !== null)
    .map((entity) => ({
      id: `${entity.parentEntityId}-${entity.entityId}`,
      fromId: entity.parentEntityId ?? "",
      toId: entity.entityId,
      label: `${formatPercent(entity.ownershipPercent)} ownership from ${entityDisplayName(entityStructure, entity.parentEntityId)} to ${entity.displayName}`
    }));
  const assetEdges = entityStructure.privateAssets
    .filter((asset) => asset.valuationStatus !== "current")
    .map((asset) => ({
      id: `${asset.entityId}-${asset.assetId}`,
      fromId: asset.entityId,
      toId: asset.assetId,
      label: `${asset.displayName} is held by ${entityDisplayName(entityStructure, asset.entityId)}`
    }));

  return [...entityEdges, ...assetEdges];
}

function entityTone(entity: FamilyOfficeEntityNode): FamilyOfficeTone {
  if (entity.reconciliationBreakCount > 0 && entity.unfundedCommitments > 0) {
    return "danger";
  }

  if (entity.staleValuationWarningCount > 0 || entity.liabilities > 0) {
    return "warning";
  }

  return entity.parentEntityId === null ? "success" : "default";
}

function ownershipToneRowClassName(tone: FamilyOfficeTone): string {
  switch (tone) {
    case "success":
      return "border-success/30 bg-success/5";
    case "warning":
      return "border-warning/30 bg-warning/5";
    case "danger":
      return "border-danger/30 bg-danger/5";
    default:
      return "";
  }
}

function entityDisplayName(entityStructure: FamilyOfficeEntityStructure, entityId: string | null): string {
  return entityStructure.entities.find((entity) => entity.entityId === entityId)?.displayName ?? "Unmapped entity";
}

function formatCurrency(value: number): string {
  const absoluteValue = Math.abs(value);
  const sign = value < 0 ? "-" : "";
  if (absoluteValue >= 1_000_000) {
    return `${sign}$${(absoluteValue / 1_000_000).toFixed(1)}M`;
  }

  if (absoluteValue >= 1_000) {
    return `${sign}$${(absoluteValue / 1_000).toFixed(1)}K`;
  }

  return `${sign}$${absoluteValue.toFixed(0)}`;
}

function formatPercent(value: number): string {
  return Number.isInteger(value) ? `${value}%` : `${value.toFixed(1)}%`;
}

function formatCountLabel(count: number, singular: string, plural = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : plural}`;
}

function uniqueCount(values: string[]): number {
  return new Set(values).size;
}

function sum(values: number[]): number {
  return values.reduce((total, value) => total + value, 0);
}
